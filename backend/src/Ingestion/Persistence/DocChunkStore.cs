using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Ingestion.Chunking;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace LiaraDocsAssistant.Ingestion.Persistence;

public sealed class DocChunkStore(AppDbContext db)
{
    public async Task<bool> IsPopulatedAsync(CancellationToken ct) =>
        await db.DocChunks.CountAsync(ct) > 0;

    public async Task<Dictionary<(string?, string), byte>> GetExistingHashesAsync(string pageUrl, CancellationToken ct)
    {
        var rows = await db.DocChunks
            .AsNoTracking()
            .Where(c => c.Url == pageUrl)
            .Select(c => new { c.Anchor, c.ContentHash })
            .ToListAsync(ct);

        return rows.ToDictionary(r => (r.Anchor, r.ContentHash), _ => (byte)0);
    }

    public async Task<PageWriteResult> WritePageChunksAsync(
        IReadOnlyList<ChunkDraft> drafts,
        Func<ChunkDraft, Vector?> embeddingFor,
        CancellationToken ct)
    {
        var written = 0;
        var skipped = 0;

        if (drafts.Count == 0)
        {
            return new PageWriteResult(0, 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var pageUrl = drafts[0].Url;
        var existingRows = await db.DocChunks
            .Where(c => c.Url == pageUrl)
            .Select(c => new { c.Id, c.Anchor, c.ContentHash })
            .ToListAsync(ct);

        foreach (var draft in drafts)
        {
            var embedding = embeddingFor(draft);
            if (embedding is null)
            {
                skipped++;
                continue;
            }

            var match = existingRows.FirstOrDefault(e =>
                e.Anchor == draft.Anchor && string.Equals(e.ContentHash, draft.ContentHash, StringComparison.Ordinal));
            if (match is not null)
            {
                skipped++;
                continue;
            }

            var row = existingRows.FirstOrDefault(e =>
                e.Anchor == draft.Anchor && !string.Equals(e.ContentHash, draft.ContentHash, StringComparison.Ordinal));

            if (row is null)
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
            }
            else
            {
                var tracked = await db.DocChunks.SingleAsync(c => c.Id == row.Id, ct);
                tracked.Title = draft.Title;
                tracked.Category = draft.CategoryId;
                tracked.Body = draft.Body;
                tracked.TokenCount = draft.TokenCount;
                tracked.Embedding = embedding;
                tracked.ContentHash = draft.ContentHash;
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
            }

            written++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new PageWriteResult(written, skipped);
    }
}

public sealed record PageWriteResult(int Written, int Skipped);
