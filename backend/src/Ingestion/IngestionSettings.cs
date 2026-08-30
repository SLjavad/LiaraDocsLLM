namespace LiaraDocsAssistant.Ingestion;

public sealed record IngestionSettings(
    int EmbedDim,
    bool EmbeddingUseInputType,
    int EmbedBatchSize,
    string DocsSitemapUrl,
    int CrawlConcurrency,
    int CrawlDelayMs,
    string? ObjectStorageEndpoint,
    string? ObjectStorageAccessKey,
    string? ObjectStorageSecretKey,
    string? ObjectStorageBucketName,
    string? ObjectStorageSeedKey);
