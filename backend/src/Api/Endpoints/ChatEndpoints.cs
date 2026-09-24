using System.Text.Json;
using System.Text.Json.Serialization;
using LiaraDocsAssistant.Agent;
using LiaraDocsAssistant.Api;
using LiaraDocsAssistant.Api.Configuration;
using LiaraDocsAssistant.Api.RateLimiting;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Redis;
using LiaraDocsAssistant.Retrieval.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Api.Endpoints;

public sealed record ChatRequest(
    [property: JsonPropertyName("sessionId")] Guid? SessionId,
    [property: JsonPropertyName("message")] string? Message);

public sealed record SessionMessageDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("sources")] object? Sources,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
            ChatRequest request,
            HttpContext http,
            ChatOrchestrator orchestrator,
            RateLimiter rateLimiter,
            ISpendGuard spendGuard,
            AppOptions options,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            if (request.SessionId is not { } sessionId || sessionId == Guid.Empty)
            {
                return Results.Json(new { error = "sessionId is required" }, statusCode: StatusCodes.Status400BadRequest);
            }
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.Json(new { error = "message is required" }, statusCode: StatusCodes.Status400BadRequest);
            }

            var message = TextInput.TruncateSafely(request.Message.Trim(), options.Api.MaxInputChars);
            var locale = RefusalTemplates.DetectLocale(message);
            var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var rate = await rateLimiter.CheckAndIncrementAsync(
                sessionId.ToString(), clientIp, options.RateLimit.SessionPerMin, options.RateLimit.IpPerMin, ct);
            if (!rate.Allowed)
            {
                logger.LogWarning("Rate limit tripped for {LimitingKey}", rate.LimitingKey);
                return Results.Json(new
                {
                    error = RefusalTemplates.Pick(locale, RefusalTemplates.RateLimitEn, RefusalTemplates.RateLimitFa),
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            if (await spendGuard.IsBudgetExhaustedAsync(ct))
            {
                logger.LogWarning("Daily spend budget exhausted; blocking new /api/chat calls");
                return Results.Json(new
                {
                    error = RefusalTemplates.Pick(locale, RefusalTemplates.SpendBudgetEn, RefusalTemplates.SpendBudgetFa),
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers.Connection = "keep-alive";
            http.Response.ContentType = "text/event-stream";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            try
            {
                var result = await orchestrator.RunTurnAsync(sessionId, message, ct);

                await WriteEventAsync(http, "meta", BuildMeta(result), ct);
                foreach (var chunk in ChunkText(result.Text))
                {
                    await WriteEventAsync(http, "token", new { delta = chunk }, ct);
                }
                if (result.Kind == "answer" && result.Sources is { Count: > 0 })
                {
                    await WriteEventAsync(http, "sources", new { sources = result.Sources }, ct);
                }
                await WriteEventAsync(http, "done", new { }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error during /api/chat turn for session {SessionId}", sessionId);
                await WriteEventAsync(http, "error", new { message = "Something went wrong generating a response.", retryable = true }, ct);
            }

            return Results.Empty;
        });

        app.MapGet("/api/sessions/{sessionId:guid}/messages", async (
            Guid sessionId, AppDbContext db, CancellationToken ct) =>
        {
            var messages = await db.Messages.AsNoTracking()
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new SessionMessageDto(
                    m.Id,
                    m.Role,
                    m.Content,
                    m.Sources == null ? null : JsonSerializer.Deserialize<object>(m.Sources, JsonOptions),
                    m.CreatedAt))
                .ToListAsync(ct);

            return Results.Ok(new { messages });
        });
    }

    private static object BuildMeta(ChatTurnResult result) => result.Kind switch
    {
        "scope_refusal" => new { kind = result.Kind, reason = result.Reason },
        "triage" => new { kind = result.Kind, triageRound = result.TriageRound },
        _ => new { kind = result.Kind },
    };

    private static IEnumerable<string> ChunkText(string text)
    {
        const int wordsPerChunk = 6;
        var words = text.Split(' ', StringSplitOptions.None);
        for (var i = 0; i < words.Length; i += wordsPerChunk)
        {
            var slice = words.Skip(i).Take(wordsPerChunk);
            yield return (i == 0 ? "" : " ") + string.Join(' ', slice);
        }
    }

    private static async Task WriteEventAsync(HttpContext http, string eventName, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await http.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
