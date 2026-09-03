using Microsoft.Extensions.Logging;
using LiaraDocsAssistant.Ingestion.Embedding;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Redis;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace LiaraDocsAssistant.Retrieval;

public interface IRetrievalService
{
    /// <summary>
    /// Hybrid retrieval: pgvector cosine similarity (exact scan, primary) over a
    /// candidate pool, re-ranked with a pg_trgm title-similarity boost.
    /// Writes a doc_gap_events row when the best score is below the groundedness
    /// threshold. Pure scoring/merging logic lives in RetrievalScorer/Merger.
    /// </summary>
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        string? category = null,
        string? platform = null,
        string mode = "search",
        Guid? sessionId = null,
        CancellationToken ct = default);
}

public sealed class HybridRetrievalService(
    AppDbContext db,
    IEmbeddingService embeddings,
    int topK,
    double groundednessThreshold,
    ILogger<HybridRetrievalService> logger) : IRetrievalService
{
    private const int CandidatePoolMultiplier = 4;

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query,
        string? category = null,
        string? platform = null,
        string mode = "search",
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var queryVector = new Vector(await embeddings.EmbedQueryAsync(query, ct));

        var efQuery = db.DocChunks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category))
        {
            efQuery = efQuery.Where(c => c.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(platform))
        {
            efQuery = efQuery.Where(c => c.Platform == platform);
        }

        var candidates = await efQuery
            .OrderBy(c => c.Embedding.CosineDistance(queryVector))
            .Take(Math.Max(topK, 1) * CandidatePoolMultiplier)
            .Select(c => new
            {
                c.Title,
                c.Url,
                c.Anchor,
                c.Body,
                c.Category,
                c.Platform,
                CosineDistance = c.Embedding.CosineDistance(queryVector),
            })
            .ToListAsync(ct);

        var scored = candidates
            .Select(c =>
            {
                var cosineSimilarity = 1d - c.CosineDistance;
                var titleSimilarity = RetrievalScorer.TrigramSimilarity(c.Title, query);
                return new SearchResultItem(
                    c.Title,
                    c.Url,
                    c.Anchor,
                    c.Body,
                    c.Category,
                    c.Platform,
                    RetrievalScorer.Combine(cosineSimilarity, titleSimilarity));
            })
            .OrderByDescending(r => r.Score)
            .Take(Math.Max(topK, 1))
            .ToList();

        var bestScore = scored.Count > 0 ? scored[0].Score : 0d;
        if (bestScore < groundednessThreshold)
        {
            logger.LogInformation(
                "Retrieval below groundedness threshold: best={BestScore:F3} < {Threshold} (query length {Length})",
                bestScore, groundednessThreshold, query.Length);

            try
            {
                db.DocGapEvents.Add(new Data.Entities.DocGapEvent
                {
                    Query = query,
                    BestScore = (float)bestScore,
                    CategoryGuess = scored.Count > 0 ? scored[0].Category : null,
                    Mode = mode,
                    SessionId = sessionId,
                });
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to record doc_gap_events for below-threshold retrieval; continuing (session row may not exist yet, which is expected for /api/search)");
            }
        }

        return scored;
    }
}

public static class SearchMerger
{
    /// <summary>
    /// Merges per-sub-query result lists: dedupes by (url, anchor), keeps the
    /// highest score per chunk, records which sub-query produced it, and sorts
    /// by score descending.
    /// </summary>
    public static IReadOnlyList<MergedSearchResult> Merge(
        IReadOnlyList<(string SubQuery, IReadOnlyList<SearchResultItem> Results)> perSubQuery)
    {
        var best = new Dictionary<(string Url, string? Anchor), MergedSearchResult>();

        foreach (var (subQuery, results) in perSubQuery)
        {
            foreach (var item in results)
            {
                var key = (item.Url, item.Anchor);
                if (!best.TryGetValue(key, out var current) || item.Score > current.Score)
                {
                    best[key] = new MergedSearchResult(
                        item.Title,
                        item.Url,
                        item.Anchor,
                        item.Body,
                        item.Category,
                        item.Platform,
                        item.Score,
                        subQuery);
                }
            }
        }

        return [.. best.Values.OrderByDescending(r => r.Score)];
    }
}
