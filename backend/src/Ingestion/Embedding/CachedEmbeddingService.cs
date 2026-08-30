using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace LiaraDocsAssistant.Ingestion.Embedding;

public sealed class CachedEmbeddingService(
    IEmbeddingService inner,
    IDatabase redis,
    ILogger<CachedEmbeddingService> logger) : IEmbeddingService
{
    public async Task<float[]> EmbedDocumentAsync(string text, CancellationToken ct = default) =>
        (await GetOrEmbedAsync("search_document", text, () => inner.EmbedDocumentAsync(text, ct), ct))!;

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default) =>
        (await GetOrEmbedAsync("search_query", text, () => inner.EmbedQueryAsync(text, ct), ct))!;

    public async Task<IReadOnlyList<float[]?>> EmbedDocumentsBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        var results = new float[texts.Count][];
        var misses = new List<(int Position, string Text)>();

        for (var i = 0; i < texts.Count; i++)
        {
            if (await TryReadCacheAsync("search_document", texts[i], ct) is { } cached)
            {
                results[i] = cached;
            }
            else
            {
                misses.Add((i, texts[i]));
            }
        }

        if (misses.Count > 0)
        {
            var embedded = await inner.EmbedDocumentsBatchAsync(
                [.. misses.Select(m => m.Text)], ct);

            for (var j = 0; j < misses.Count; j++)
            {
                var vector = embedded[j];
                if (vector is null)
                {
                    continue;
                }
                results[misses[j].Position] = vector;
                await WriteCacheAsync("search_document", misses[j].Text, vector, ct);
            }
        }

        return results;
    }

    private async Task<float[]?> GetOrEmbedAsync(
        string inputType,
        string text,
        Func<Task<float[]>> embed,
        CancellationToken ct)
    {
        if (await TryReadCacheAsync(inputType, text, ct) is { } cached)
        {
            return cached;
        }

        var vector = await embed();
        await WriteCacheAsync(inputType, text, vector, ct);
        return vector;
    }

    internal static string CacheKey(string inputType, string text)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{inputType}:{text}")));
        return $"cache:embedding:{hash}";
    }

    private async Task<float[]?> TryReadCacheAsync(string inputType, string text, CancellationToken ct)
    {
        try
        {
            var json = await redis.StringGetAsync(CacheKey(inputType, text));
            return json.IsNullOrEmpty ? null : JsonSerializer.Deserialize<float[]>(json.ToString());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "embedding cache read failed; falling through to the live embedding call");
            return null;
        }
    }

    private async Task WriteCacheAsync(string inputType, string text, float[] vector, CancellationToken ct)
    {
        try
        {
            await redis.StringSetAsync(
                CacheKey(inputType, text),
                JsonSerializer.Serialize(vector),
                TimeSpan.FromDays(7));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "embedding cache write failed; proceeding without caching this vector");
        }
    }
}
