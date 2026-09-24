using LiaraDocsAssistant.Api.Caching;
using LiaraDocsAssistant.Api.Configuration;
using LiaraDocsAssistant.Api;
using LiaraDocsAssistant.Api.RateLimiting;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Redis;
using LiaraDocsAssistant.Retrieval;
using LiaraDocsAssistant.Retrieval.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiaraDocsAssistant.Api.Endpoints;

public sealed record SearchRequest(
    [property: JsonPropertyName("sessionId")] Guid? SessionId,
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("locale")] string? Locale);

public sealed record SearchResultDto(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("anchor")] string? Anchor,
    [property: JsonPropertyName("snippet")] string Snippet,
    [property: JsonPropertyName("score")] double Score,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("matchedSubQuery")] string? MatchedSubQuery);

public static class SearchEndpoints
{
    private const int SnippetMaxLength = 240;

    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", () => Results.Ok(new
        {
            categories = DocsTaxonomy.Categories.Select(c => new
            {
                id = c.Id,
                labelFa = c.LabelFa,
                labelEn = c.LabelEn,
            }),
        }));

        app.MapPost("/api/search", async (
            SearchRequest request,
            HttpContext http,
            IRouterService router,
            IRetrievalService retrieval,
            RateLimiter rateLimiter,
            ISpendGuard spendGuard,
            SearchCache cache,
            AppOptions options,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var startedAt = DateTimeOffset.UtcNow;

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.Json(new { error = "query is required" },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var query = TextInput.TruncateSafely(request.Query.Trim(), options.Api.MaxInputChars);

            var sessionId = request.SessionId
                            ?? (Guid.TryParse(http.Request.Headers["X-Session-Id"].FirstOrDefault(), out var parsed)
                                ? parsed
                                : null);

            var clientIp = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var locale = request.Locale is "fa" or "en"
                ? request.Locale
                : RefusalTemplates.DetectLocale(query);

            var rate = await rateLimiter.CheckAndIncrementAsync(
                sessionId?.ToString(), clientIp,
                options.RateLimit.SessionPerMin, options.RateLimit.IpPerMin, ct);
            if (!rate.Allowed)
            {
                logger.LogWarning("Rate limit tripped for {LimitingKey}", rate.LimitingKey);
                return Results.Json(new
                {
                    error = RefusalTemplates.Pick(locale, RefusalTemplates.RateLimitEn, RefusalTemplates.RateLimitFa),
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            var cached = await cache.GetRawAsync(query, request.Category, request.Platform, locale, ct);
            if (cached is not null)
            {
                return Results.Content(cached, "application/json; charset=utf-8");
            }

            if (await spendGuard.IsBudgetExhaustedAsync(ct))
            {
                logger.LogWarning("Daily spend budget exhausted; blocking new AI calls");
                return Results.Json(new
                {
                    error = RefusalTemplates.Pick(locale, RefusalTemplates.SpendBudgetEn, RefusalTemplates.SpendBudgetFa),
                }, statusCode: StatusCodes.Status429TooManyRequests);
            }
            var routerResult = await router.ClassifyAsync(
                new RouterRequest(query, "search"), ct);

            if (routerResult.Scope != RouterResult.ScopeInScope)
            {
                var refusalJson = WebJson(new
                {
                    scope = routerResult.Scope,
                    reason = routerResult.Reason,
                    message = RefusalTemplates.ForScope(routerResult.Scope, routerResult.Reason, locale),
                    results = Array.Empty<SearchResultDto>(),
                });
                await cache.SetRawAsync(query, request.Category, request.Platform, locale, refusalJson, ct);
                return Results.Content(refusalJson, "application/json; charset=utf-8");
            }

            var perSubQuery = new List<(string SubQuery, IReadOnlyList<SearchResultItem> Results)>();
            foreach (var subQuery in routerResult.SubQueries.Count > 0
                         ? routerResult.SubQueries
                         : [query])
            {
                var results = await retrieval.SearchAsync(
                    subQuery,
                    request.Category,
                    request.Platform,
                    mode: "search",
                    sessionId: sessionId,
                    ct: ct);
                perSubQuery.Add((subQuery, results));
            }

            var merged = SearchMerger.Merge(perSubQuery);
            var responseJson = WebJson(new
            {
                scope = "in_scope",
                subQueries = routerResult.SubQueries,
                results = merged.Select(r => new SearchResultDto(
                    r.Title,
                    r.Url,
                    r.Anchor,
                    Snippet(r.Body),
                    Math.Round(r.Score, 4),
                    r.Category,
                    r.MatchedSubQuery)),
                tookMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds,
            });
            await cache.SetRawAsync(query, request.Category, request.Platform, locale, responseJson, ct);
            return Results.Content(responseJson, "application/json; charset=utf-8");
        });
    }

    private static string Snippet(string body) =>
        body.Length <= SnippetMaxLength
            ? body
            : body[..SnippetMaxLength].TrimEnd() + "…";

    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private static string WebJson(object value) => JsonSerializer.Serialize(value, WebJsonOptions);
}
