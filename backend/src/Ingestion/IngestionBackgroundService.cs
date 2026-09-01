using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Ingestion.Chunking;
using LiaraDocsAssistant.Ingestion.Crawling;
using LiaraDocsAssistant.Ingestion.Embedding;
using LiaraDocsAssistant.Ingestion.Persistence;
using LiaraDocsAssistant.Ingestion.Seed;
using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Ingestion;

public sealed class IngestionBackgroundService(
    IServiceScopeFactory scopeFactory,
    DocsSiteCrawler crawler,
    ISectionChunker chunker,
    IEmbeddingService embeddings,
    SeedRestoreService seedRestore,
    IngestionSettings settings,
    ILogger<IngestionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var countScope = scopeFactory.CreateScope();
            var store = countScope.ServiceProvider.GetRequiredService<DocChunkStore>();

            if (await store.IsPopulatedAsync(stoppingToken))
            {
                logger.LogInformation("Ingestion skipped: doc_chunks already populated");
                return;
            }

            if (seedRestore.IsConfigured)
            {
                logger.LogInformation("Ingestion starting via Object Storage seed path");
                var connectionString = ResolvePostgresConnectionString(countScope);
                if (connectionString is null)
                {
                    logger.LogError("Object Storage seed path selected but POSTGRES_CONNECTION_STRING is unavailable; falling back to live crawl");
                }
                else if (await seedRestore.TryRestoreAsync(connectionString, stoppingToken))
                {
                    var db = countScope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var chunks = await db.DocChunks.CountAsync(stoppingToken);
                    logger.LogInformation(
                        "Seed restore complete: {ChunkCount} chunks in {DurationMs} ms",
                        chunks, stopwatch.ElapsedMilliseconds);
                    return;
                }
            }
            else
            {
                logger.LogInformation("Object Storage not configured; ingestion starting via live-crawl fallback path");
            }

            await RunLiveCrawlAsync(stopwatch, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ingestion pipeline failed after {DurationMs} ms", stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task RunLiveCrawlAsync(Stopwatch stopwatch, CancellationToken ct)
    {
        var urls = (await crawler.EnumerateUrlsAsync(settings.DocsSitemapUrl, ct))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        logger.LogInformation(
            "Sitemap enumeration complete: {MatchedCount} pages matched the taxonomy from {SitemapUrl}",
            urls.Count, settings.DocsSitemapUrl);

        if (urls.Count == 0)
        {
            logger.LogWarning("No taxonomy-matched pages found; nothing to ingest");
            return;
        }

        var queue = new ConcurrentQueue<string>(urls);
        var totalWritten = 0L;
        var failedPages = 0L;
        var processedPages = 0L;

        async Task Worker(int workerId)
        {
            using var workerScope = scopeFactory.CreateScope();
            var store = workerScope.ServiceProvider.GetRequiredService<DocChunkStore>();
            var firstIteration = true;

            while (!ct.IsCancellationRequested && queue.TryDequeue(out var pageUrl))
            {
                if (!firstIteration && settings.CrawlDelayMs > 0)
                {
                    await Task.Delay(settings.CrawlDelayMs, ct);
                }
                firstIteration = false;

                try
                {
                    var written = await ProcessPageAsync(pageUrl, store, ct);
                    if (written > 0)
                    {
                        Interlocked.Add(ref totalWritten, written);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Increment(ref failedPages);
                    logger.LogWarning(ex, "Page crawl/ingest failed and was skipped: {PageUrl}", pageUrl);
                }
                finally
                {
                    var processed = Interlocked.Increment(ref processedPages);
                    if (processed % 25 == 0)
                    {
                        logger.LogInformation(
                            "Ingestion progress: {ProcessedPages}/{TotalPages} pages, {ChunksWritten} chunks written, {FailedPages} skipped",
                            processed, urls.Count, Interlocked.Read(ref totalWritten), Interlocked.Read(ref failedPages));
                    }
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Max(1, settings.CrawlConcurrency)).Select(i => Worker(i)));

        using var finalScope = scopeFactory.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persistedChunks = await db.DocChunks.CountAsync(ct);

        logger.LogInformation(
            "Live-crawl ingestion finished: {PersistedChunks} chunks in database after {DurationMs} ms ({FailedPages}/{Total} pages skipped on error)",
            persistedChunks, stopwatch.ElapsedMilliseconds, Interlocked.Read(ref failedPages), urls.Count);
    }

    private async Task<int> ProcessPageAsync(string pageUrl, DocChunkStore store, CancellationToken ct)
    {
        var categoryId = DocsTaxonomy.MatchPathSegment(new Uri(pageUrl).AbsolutePath);
        if (categoryId is null)
        {
            return 0;
        }

        var page = await crawler.CrawlAsync(pageUrl, categoryId, ct);
        if (page is null)
        {
            logger.LogDebug("Page yielded no parseable content: {PageUrl}", pageUrl);
            return 0;
        }

        var drafts = chunker.BuildChunks(page);
        if (drafts.Count == 0)
        {
            return 0;
        }

        var existingRows = await store.GetExistingChunksAsync(page.Url, ct);
        var existingKeySet = existingRows
            .Select(r => (r.Anchor, r.ContentHash))
            .ToHashSet();
        var needed = drafts
            .Where(d => !existingKeySet.Contains((d.Anchor, d.ContentHash)))
            .ToList();
        if (needed.Count == 0)
        {
            logger.LogDebug(
                "Page skipped entirely by content_hash: {PageUrl} ({Chunks} chunks unchanged)",
                pageUrl, drafts.Count);
            return 0;
        }

        var bodiesByPosition = needed.Select(d => d.Body).ToList();
        var embeddingMap = new Dictionary<string, Pgvector.Vector>();

        foreach (var batch in ChunkBatches(bodiesByPosition, settings.EmbedBatchSize))
        {
            var vectors = await embeddings.EmbedDocumentsBatchAsync(batch, ct);
            for (var i = 0; i < batch.Count; i++)
            {
                var vector = vectors[i];
                if (vector is null)
                {
                    continue;
                }

                if (vector.Length != settings.EmbedDim)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimension drift: provider returned {vector.Length}-dim vector but EMBED_DIM={settings.EmbedDim}");
                }

                embeddingMap[batch[i]] = new Pgvector.Vector(vector);
            }
        }

        var result = await store.WritePageChunksAsync(
            drafts,
            existingRows,
            draft => embeddingMap.TryGetValue(draft.Body, out var vector) ? vector : null,
            ct);

        logger.LogDebug(
            "Page ingested {PageUrl}: sections={Sections} written={Written} skippedHash={Skipped}",
            pageUrl, page.Sections.Count, result.Written, result.Skipped);

        return result.Written;
    }

    internal static IEnumerable<List<string>> ChunkBatches(List<string> bodies, int batchSize)
    {
        for (var offset = 0; offset < bodies.Count; offset += batchSize)
        {
            yield return [.. bodies.Skip(offset).Take(batchSize)];
        }
    }

    private static string? ResolvePostgresConnectionString(IServiceScope scope) =>
        scope.ServiceProvider.GetService<IConfiguration>()?["POSTGRES_CONNECTION_STRING"]
        ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
}
