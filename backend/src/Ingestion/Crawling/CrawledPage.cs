namespace LiaraDocsAssistant.Ingestion.Crawling;

public sealed record CrawledSection(string Title, string Body, string? Anchor);

public sealed record CrawledPage(string Url, string CategoryId, string Title, IReadOnlyList<CrawledSection> Sections);
