using System.ComponentModel;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Retrieval;

namespace LiaraDocsAssistant.Agent;

/// <summary>
/// search_docs / list_categories (specs/02-technical-spec.md §7), built fresh
/// per /api/chat request so each tool call is scoped to that request's session
/// and can record which sources the agent actually used across a multi-hop
/// turn, for the SSE `sources` event afterward.
/// </summary>
public sealed class ChatTools(IRetrievalService retrieval, Guid sessionId)
{
    private readonly List<SearchResultItem> collectedSources = [];

    public IReadOnlyList<SearchResultItem> CollectedSources => collectedSources;

    [Description(
        "Hybrid retrieval over Liara's documentation. Call whenever you need to " +
        "ground a claim about how a Liara service works. Call it more than once " +
        "per turn to decompose a complex question into sub-queries.")]
    public async Task<object> SearchDocsAsync(
        [Description("The search query, in the user's own words or a focused sub-query.")] string query,
        [Description("Optional taxonomy category id to restrict the search to.")] string? category = null,
        [Description("Optional platform/framework/language filter (e.g. nextjs, django).")] string? platform = null)
    {
        var results = await retrieval.SearchAsync(query, category, platform, mode: "chat", sessionId: sessionId);
        collectedSources.AddRange(results);

        return new
        {
            results = results.Select(r => new
            {
                title = r.Title,
                url = r.Url,
                anchor = r.Anchor,
                body = r.Body,
                score = Math.Round(r.Score, 4),
                category = r.Category,
                platform = r.Platform,
            }),
        };
    }

    [Description("Returns Liara's documentation category taxonomy, for asking a disambiguating question.")]
    public object ListCategories() => new
    {
        categories = DocsTaxonomy.Categories.Select(c => new
        {
            id = c.Id,
            labelFa = c.LabelFa,
            labelEn = c.LabelEn,
        }),
    };
}
