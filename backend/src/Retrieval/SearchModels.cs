namespace LiaraDocsAssistant.Retrieval;

public sealed record SearchResultItem(
    string Title,
    string Url,
    string? Anchor,
    string Body,
    string Category,
    string? Platform,
    double Score);

public sealed record MergedSearchResult(
    string Title,
    string Url,
    string? Anchor,
    string Body,
    string Category,
    string? Platform,
    double Score,
    string MatchedSubQuery);
