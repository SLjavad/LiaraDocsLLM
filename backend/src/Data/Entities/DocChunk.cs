using Pgvector;

namespace LiaraDocsAssistant.Data.Entities;

public sealed class DocChunk
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Anchor { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Platform { get; set; }
    public string Body { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
