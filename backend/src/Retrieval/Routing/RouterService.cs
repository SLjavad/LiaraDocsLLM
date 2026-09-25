using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using LiaraDocsAssistant.Data.Redis;

namespace LiaraDocsAssistant.Retrieval.Routing;

/// <summary>
/// One cheap-model JSON-mode call (NFR7): the literal §2 router prompt as the
/// system message, the formatted input as the user message. Posts to
/// CHAT_MODEL_BASE_URL + "/chat/completions" — the chat base URL is a prefix,
/// unlike EMBEDDING_BASE_URL which is a full endpoint.
/// </summary>
public sealed class RouterService(
    HttpClient http,
    string routerModelName,
    string apiKey,
    ISpendGuard spendGuard,
    ILogger<RouterService> logger) : IRouterService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<RouterResult> ClassifyAsync(RouterRequest request, CancellationToken ct = default)
    {
        var result = await JsonModeChatClient.CallAsync(http, apiKey, new
        {
            model = routerModelName,
            messages = new object[]
            {
                new { role = "system", content = RouterPrompt.Text },
                new { role = "user", content = FormatInput(request) },
            },
            response_format = new { type = "json_object" },
            temperature = 0,
        }, ct);

        var usage = result.Usage;
        if (usage is not null)
        {
            logger.LogInformation(
                "Router call {Model} router promptTokens={PromptTokens} completionTokens={CompletionTokens} totalTokens={TotalTokens}",
                routerModelName, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
            if ((usage.TotalTokens ?? 0) > 0)
            {
                await spendGuard.RecordAsync("router", usage.TotalTokens!.Value, ct);
            }
        }

        var content = result.Content
                      ?? throw new InvalidOperationException("router returned no message content");

        var parsed = ParseRouterJson(content);

        if (parsed.Scope is not (RouterResult.ScopeTrivial or RouterResult.ScopeOutOfScope or RouterResult.ScopeInScope))
        {
            logger.LogWarning(
                "Router returned an unrecognized scope '{Scope}'; treating it as out_of_scope with a generic reason",
                parsed.Scope);
            return new RouterResult(RouterResult.ScopeOutOfScope, "general_knowledge", []);
        }

        return parsed.Scope == RouterResult.ScopeInScope
            ? parsed
            : parsed with { SubQueries = [] };
    }

    internal static string FormatInput(RouterRequest request)
    {
        var lines = new List<string>();
        if (request.RecentMessages is { Count: > 0 } recent)
        {
            lines.Add($"[recent: {string.Join(", ", recent.Take(4).Select(m => $"\"{JsonModeChatClient.Escape(m)}\""))}]");
        }

        lines.Add(request.Mode == "search"
            ? $"[input, mode=search: \"{JsonModeChatClient.Escape(request.Message)}\"]"
            : $"[input: \"{JsonModeChatClient.Escape(request.Message)}\"]");

        return string.Join("\n", lines);
    }

    internal static RouterResult ParseRouterJson(string content)
    {
        var json = JsonModeHelpers.StripCodeFence(content);
        var parsed = JsonSerializer.Deserialize<RouterPayload>(json, JsonOptions)
                     ?? throw new InvalidOperationException("router JSON deserialized to null");

        return new RouterResult(
            parsed.Scope ?? string.Empty,
            parsed.Reason,
            parsed.SubQueries ?? []);
    }

    private sealed record RouterPayload(
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("subQueries")] List<string>? SubQueries);
}
