namespace LiaraDocsAssistant.Ingestion.Embedding;

public sealed record EmbeddingRequestItem(string Text);

public interface IEmbeddingService
{
    Task<float[]> EmbedDocumentAsync(string text, CancellationToken ct = default);
    Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default);
    Task<IReadOnlyList<float[]?>> EmbedDocumentsBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
