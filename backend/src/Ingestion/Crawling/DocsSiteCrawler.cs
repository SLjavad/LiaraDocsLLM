using AngleSharp.Dom;
using LiaraDocsAssistant.Data;

namespace LiaraDocsAssistant.Ingestion.Crawling;

public sealed class DocsSiteCrawler(HttpClient httpClient)
{
    public async Task<List<string>> EnumerateUrlsAsync(string sitemapUrl, CancellationToken ct)
    {
        var xml = await httpClient.GetStringAsync(sitemapUrl, ct);

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);

        return [.. doc.GetElementsByTagName("*").Cast<System.Xml.XmlElement>()
            .Where(e => e.LocalName == "loc" && e.InnerText is not null)
            .Select(e => e.InnerText.Trim())
            .Where(uri => Uri.TryCreate(uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .SelectMany(MapToTaxonomyUrls)];
    }

    private static IEnumerable<string> MapToTaxonomyUrls(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            DocsTaxonomy.MatchPathSegment(parsed.AbsolutePath) is null)
        {
            yield break;
        }

        yield return uri;
    }

    public async Task<CrawledPage?> CrawlAsync(
        string pageUrl,
        string categoryId,
        CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(pageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"page request returned {(int)response.StatusCode}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var parser = new AngleSharp.Html.Parser.HtmlParser();
        var document = await parser.ParseDocumentAsync(stream, ct);

        var title = document.QuerySelector("h1")?.TextContent.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = document.Title?.Trim() ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var root = document.QuerySelector("main") ?? document.Body;
        if (root is null)
        {
            return null;
        }

        var headings = root.QuerySelectorAll("h2, h3, h4, h5, h6").ToList();
        var sections = new List<CrawledSection>();

        if (headings.Count == 0)
        {
            var body = ExtractText(root);
            if (!string.IsNullOrWhiteSpace(body))
            {
                sections.Add(new CrawledSection(title, body, null));
            }
        }
        else
        {
            foreach (var heading in headings)
            {
                var sectionTitle = heading.TextContent.Trim();
                var anchor = ResolveAnchor(heading);
                var bodyBuilder = new System.Text.StringBuilder();

                var node = heading.NextSibling;
                while (node is not null && !IsHeading(node))
                {
                    AppendText(node, bodyBuilder);
                    node = node.NextSibling;
                }

                var body = Clean(bodyBuilder.ToString());
                if (sectionTitle.Length == 0 && body.Length == 0)
                {
                    continue;
                }

                sections.Add(new CrawledSection(sectionTitle, body, anchor));
            }

            var introText = CollectIntro(root, headings[0]);
            if (!string.IsNullOrWhiteSpace(introText))
            {
                sections.Insert(0, new CrawledSection(title, introText, null));
            }
        }

        if (sections.Count == 0)
        {
            return null;
        }

        return new CrawledPage(pageUrl, categoryId, title, sections);
    }

    private static string? CollectIntro(IElement root, IElement firstHeading)
    {
        var builder = new System.Text.StringBuilder();
        var seenHeading = false;
        Walk(root);

        void Walk(INode node)
        {
            if (seenHeading) return;
            if (ReferenceEquals(node, firstHeading))
            {
                seenHeading = true;
                return;
            }
            if (node.HasChildNodes)
            {
                foreach (var child in node.ChildNodes) Walk(child);
            }
            else if (node.NodeType == NodeType.Text)
            {
                builder.Append(' ').Append(node.TextContent);
            }
        }

        return Clean(builder.ToString());
    }

    private static bool IsHeading(INode node) =>
        node.NodeType == NodeType.Element &&
        ((IElement)node).LocalName is "h2" or "h3" or "h4" or "h5" or "h6";

    private static string? IdAttr(IElement? element) =>
        element is not null && element.HasAttribute("id") ? element.GetAttribute("id") : null;

    private static string? ResolveAnchor(IElement heading)
    {
        if (IdAttr(heading) is { } own) return own;

        if (heading.QuerySelector("[id]") is { } childAnchor && IdAttr(childAnchor) is { } childId) return childId;

        if (IdAttr(heading.PreviousElementSibling) is { } prevId) return prevId;

        return null;
    }

    private static string ExtractText(IElement element)
    {
        var builder = new System.Text.StringBuilder();
        AppendText(element, builder);
        return Clean(builder.ToString());
    }

    private static void AppendText(INode node, System.Text.StringBuilder builder)
    {
        if (node.NodeType == NodeType.Text)
        {
            builder.Append(' ').Append(node.TextContent);
            return;
        }

        if (node is not IElement element || element.LocalName is "script" or "style" or "nav" or "noscript")
        {
            return;
        }

        foreach (var child in element.ChildNodes)
        {
            AppendText(child, builder);
        }
    }

    private static string Clean(string raw)
    {
        var parts = raw.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts).Trim();
    }
}
