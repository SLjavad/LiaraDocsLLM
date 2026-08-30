using System.Security.Cryptography;
using System.Text;
using LiaraDocsAssistant.Data;

namespace LiaraDocsAssistant.Ingestion.Chunking;

public sealed record ChunkDraft(
    string Url,
    string? Anchor,
    string Title,
    string CategoryId,
    string Body,
    int TokenCount,
    string ContentHash);

public interface ISectionChunker
{
    IReadOnlyList<ChunkDraft> BuildChunks(Crawling.CrawledPage page);
}

public sealed class SectionChunker(int minTokens = 200, int maxTokens = 800) : ISectionChunker
{
    public IReadOnlyList<ChunkDraft> BuildChunks(Crawling.CrawledPage page)
    {
        var pendingSmall = new List<(string Title, string Body, string? Anchor)>();
        var chunks = new List<ChunkDraft>();

        foreach (var section in page.Sections)
        {
            var tokens = EstimateTokens(section.Body);

            if (tokens > maxTokens)
            {
                FlushPending(chunks, page, pendingSmall);
                AddSplitChunks(chunks, page, section.Title, section.Body, section.Anchor);
                continue;
            }

            pendingSmall.Add((section.Title, section.Body, section.Anchor));

            if (EstimateTokens(string.Join("\n\n", pendingSmall.Select(p => p.Body))) >= minTokens)
            {
                FlushPending(chunks, page, pendingSmall);
            }
        }

        FlushPending(chunks, page, pendingSmall);
        return chunks;
    }

    private static void FlushPending(
        List<ChunkDraft> chunks,
        Crawling.CrawledPage page,
        List<(string Title, string Body, string? Anchor)> pendingSmall)
    {
        if (pendingSmall.Count == 0) return;

        var first = pendingSmall[0];
        var body = string.Join("\n\n", pendingSmall.Select(p => p.Body));
        chunks.Add(new ChunkDraft(
            page.Url,
            first.Anchor,
            first.Title.Length > 0 ? first.Title : page.Title,
            page.CategoryId,
            body,
            EstimateTokens(body),
            Hash(body)));

        pendingSmall.Clear();
    }

    private static void AddSplitChunks(
        List<ChunkDraft> chunks,
        Crawling.CrawledPage page,
        string sectionTitle,
        string body,
        string? anchor)
    {
        var parts = new List<List<string>>();
        var current = new List<string>();
        var currentTokens = 0;

        foreach (var paragraph in SplitParagraphs(body))
        {
            var paragraphTokens = EstimateTokens(paragraph);

            if (current.Count > 0 && currentTokens + paragraphTokens > 800)
            {
                parts.Add(current);
                current = [];
                currentTokens = 0;
            }

            current.Add(paragraph);
            currentTokens += paragraphTokens;
        }

        if (current.Count > 0)
        {
            parts.Add(current);
        }

        for (var index = 0; index < parts.Count; index++)
        {
            var partNumber = index + 1;
            var partBody = string.Join("\n\n", parts[index]);
            var effectiveTitle = partNumber == 1 ? sectionTitle : $"{sectionTitle} ({partNumber})";

            chunks.Add(new ChunkDraft(
                page.Url,
                partNumber == 1 ? anchor : $"{anchor ?? "page"}-part{partNumber}",
                effectiveTitle.Length > 0 ? effectiveTitle : page.Title,
                page.CategoryId,
                partBody,
                EstimateTokens(partBody),
                Hash(partBody)));
        }
    }

    private static IEnumerable<string> SplitParagraphs(string body) =>
        body.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0);

    internal static int EstimateTokens(string text) =>
        Math.Max((int)Math.Ceiling(text.Length / 4.0), text.Count(char.IsWhiteSpace) + 1);

    internal static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
