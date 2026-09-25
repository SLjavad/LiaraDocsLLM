namespace LiaraDocsAssistant.Retrieval.Routing;

/// <summary>
/// Shared defensive parsing for JSON-mode chat-completions calls (router,
/// practice topic-scoping): some models still wrap JSON in a markdown code
/// fence despite response_format: json_object.
/// </summary>
public static class JsonModeHelpers
{
    public static string StripCodeFence(string content)
    {
        var json = content.Trim();
        if (!json.StartsWith("```"))
        {
            return json;
        }

        var firstNewline = json.IndexOf('\n');
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? json[(firstNewline + 1)..lastFence].Trim()
            : json;
    }
}
