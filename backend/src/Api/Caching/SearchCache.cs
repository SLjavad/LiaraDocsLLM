using LiaraDocsAssistant.Data.Redis;
using StackExchange.Redis;

namespace LiaraDocsAssistant.Api.Caching;

/// <summary>
/// NFR6 response cache for /api/search: cache:search:{sha256(query|category|platform)},
/// TTL 1h per the §3 key scheme. Failures degrade to cache-miss.
/// </summary>
public sealed class SearchCache(IConnectionMultiplexer redis)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task<string?> GetRawAsync(string query, string? category, string? platform, CancellationToken ct = default)
    {
        try
        {
            var json = await redis.GetDatabase().StringGetAsync(Key(query, category, platform));
            return json.IsNullOrEmpty ? null : json.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task SetRawAsync(string query, string? category, string? platform, string json, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(Key(query, category, platform), json, Ttl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    private static string Key(string query, string? category, string? platform) =>
        RedisKeys.CacheSearch(Retrieval.RetrievalScorer.Sha256Hex($"{query}|{category ?? ""}|{platform ?? ""}"));
}
