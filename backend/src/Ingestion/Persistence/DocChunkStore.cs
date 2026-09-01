using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Ingestion.Chunking;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace LiaraDocsAssistant.Ingestion.Persistence;

public sealed record ExistingChunkRow(Guid Id, string? Anchor, string ContentHash);

public sealed class DocChunkStore(AppDbContext db)
{
    public async Task<bool> IsPopulatedAsync(CancellationToken ct) =>
        await db.DocChunks.CountAsync(ct) > 0;

    public async Task<IReadOnlyList<ExistingChunkRow>> GetExistingChunksAsync(string pageUrl, CancellationToken ct)
    {
        var rows = await db.DocChunks
            .AsNoTracking()
            .Where(c => c.Url == pageUrl)
            .Select(c => new { c.Id, c.Anchor, c.ContentHash })
            .ToListAsync(ct);

        return [.. rows.Select(r => new ExistingChunkRow(r.Id, r.Anchor, r.ContentHash))];
    }

    public async Task<PageWriteResult> WritePageChunksAsync(
        IReadOnlyList<ChunkDraft> drafts,
        IReadOnlyList<ExistingChunkRow> existingRows,
        Func<ChunkDraft, Vector?> embeddingFor,
        CancellationToken ct)
    {
        var dedupedDrafts = DedupeDrafts(drafts);
        if (dedupedDrafts.Count == 0)
        {
            return new PageWriteResult(0, 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var unclaimed = new HashSet<ExistingChunkRow>(existingRows);
        var rowsToUpdate = new List<(ExistingChunkRow Row, ChunkDraft Draft, Vector Embedding)>();
        var written = 0;
        var skipped = 0;

        foreach (var draft in dedupedDrafts)
        {
            var embedding = embeddingFor(draft);
            if (embedding is null)
            {
                continue;
            }

            var unchanged = unclaimed.FirstOrDefault(e =>
                e.Anchor == draft.Anchor &&
                string.Equals(e.ContentHash, draft.ContentHash, StringComparison.Ordinal));
            if (unchanged is not null)
            {
                unclaimed.Remove(unchanged);
                skipped++;
                continue;
            }

            var stale = unclaimed.FirstOrDefault(e => e.Anchor == draft.Anchor);
            if (stale is not null)
            {
                unclaimed.Remove(stale);
                rowsToUpdate.Add((stale, draft, embedding));
            }
            else
            {
                db.DocChunks.Add(new LiaraDocsAssistant.Data.Entities.DocChunk
                {
                    Url = draft.Url,
                    Anchor = draft.Anchor,
                    Title = draft.Title,
                    Category = draft.CategoryId,
                    Body = draft.Body,
                    TokenCount = draft.TokenCount,
                    Embedding = embedding,
                    ContentHash = draft.ContentHash,
                });
                written++;
            }
        }

        var trackedUpdates = rowsToUpdate.Count > 0
            ? await db.DocChunks
                .Where(c => rowsToUpdate.Select(r => r.Row.Id).Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct)
            : [];

        foreach (var (row, draft, embedding) in rowsToUpdate)
        {
            if (!trackedUpdates.TryGetValue(row.Id, out var tracked))
            {
                throw new InvalidOperationException(
                    $"doc_chunks row {row.Id} vanished between the read and update of page {draft.Url}");
            }

            tracked.Title = draft.Title;
            tracked.Category = draft.CategoryId;
            tracked.Body = draft.Body;
            tracked.TokenCount = draft.TokenCount;
            tracked.Embedding = embedding;
            tracked.ContentHash = draft.ContentHash;
            tracked.UpdatedAt = DateTimeOffset.UtcNow;
            written++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.ChangeTracker.Clear();

        return new PageWriteResult(written, skipped);
    }

    private static List<ChunkDraft> DedupeDrafts(IReadOnlyList<ChunkDraft> drafts)
    {
        var seen = new HashSet<(string?, string)>();
        var result = new List<ChunkDraft>(drafts.Count);
        foreach (var draft in drafts)
        {
            if (seen.Add((draft.Anchor, draft.ContentHash)))
            {
                result.Add(draft);
            }
        }
        return result;
    }
}

public sealed record PageWriteResult(int Written, int Skipped);
