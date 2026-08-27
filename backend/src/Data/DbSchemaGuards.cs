using Microsoft.EntityFrameworkCore;

namespace LiaraDocsAssistant.Data;

public static class DbSchemaGuards
{
    public static async Task VerifyEmbeddingDimensionAsync(this DbContext db, int expectedDim)
    {
        var actualDim = await db.Database
            .SqlQuery<int?>($"""
                select a.atttypmod as "Value"
                from pg_class c
                join pg_namespace n on n.oid = c.relnamespace
                join pg_attribute a on a.attrelid = c.oid and a.attname = 'embedding'
                where c.relname = 'doc_chunks'
                  and n.nspname = current_schema()
                  and not a.attisdropped
                """)
            .SingleOrDefaultAsync();

        if (actualDim is null)
        {
            throw new InvalidOperationException(
                "Startup dimension guard: doc_chunks.embedding was not found in the live schema; " +
                "the InitialCreate migration may have been superseded or corrupted.");
        }

        if (actualDim != expectedDim)
        {
            throw new InvalidOperationException(
                $"Startup dimension guard mismatch: config EMBED_DIM={expectedDim} but doc_chunks.embedding is vector({actualDim}). " +
                $"The committed migration bakes the column dimension at creation time — changing EMBED_DIM requires a new migration plus a full re-ingest.");
        }
    }
}
