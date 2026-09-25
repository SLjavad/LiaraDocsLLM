using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LiaraDocsAssistant.Retrieval.Routing;

public sealed record ChatCompletionsResult(string? Content, ChatCompletionsUsage? Usage);

/// <summary>
/// Shared HTTP mechanics for every one-shot chat-completions call in this
/// codebase (router, practice topic-scoping, exam-generation): build the
/// request, post it, unwrap or throw. Callers still build their own request
/// body (model/messages/response_format differ) and handle their own
/// spend-guard recording (the "consumer" name differs per caller).
/// </summary>
public static class JsonModeChatClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ChatCompletionsResult> CallAsync(
        HttpClient http, string apiKey, object requestBody, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = JsonContent.Create(requestBody);

        using var response = await http.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"chat-completions call returned {(int)response.StatusCode}: {Truncate(body)}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionsResponse>(JsonOptions, ct)
                      ?? throw new InvalidOperationException("chat-completions call returned an empty payload");

        return new ChatCompletionsResult(payload.FirstMessageContent, payload.Usage);
    }

    public static string Escape(string s) => s.Replace("\"", "'");

    public static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "…";
}
