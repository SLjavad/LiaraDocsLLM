using System.Text.Json;
using System.Text.Json.Serialization;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Redis;
using Microsoft.Extensions.Logging;

namespace LiaraDocsAssistant.Retrieval.Routing;

public sealed record PracticeScopingResult(
    bool Scoped,
    string? ClarifyingQuestion,
    string? RefinedTopic,
    IReadOnlyList<string> SubTopics);

public interface IPracticeTopicScopingService
{
    Task<PracticeScopingResult> ScopeTopicAsync(
        string topic, int minSteps, int maxSteps, IReadOnlyList<string>? recentContext, bool forceScoped = false, CancellationToken ct = default);
}

/// <summary>
/// specs/04-prompts.md §2a: one cheap JSON-mode call (same cost philosophy as
/// the §2 router, same model tier) that decides whether a Practice Mode
/// topic is scoped enough to plan, and if so, decomposes it into sub-topics.
/// </summary>
public sealed class PracticeTopicScopingService(
    HttpClient http,
    string modelName,
    string apiKey,
    ISpendGuard spendGuard,
    ILogger<PracticeTopicScopingService> logger) : IPracticeTopicScopingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<PracticeScopingResult> ScopeTopicAsync(
        string topic, int minSteps, int maxSteps, IReadOnlyList<string>? recentContext, bool forceScoped = false, CancellationToken ct = default)
    {
        var systemPrompt = PracticeTopicScopingPrompt.Text
            .Replace("{{practice_min_steps}}", minSteps.ToString())
            .Replace("{{practice_max_steps}}", maxSteps.ToString())
            .Replace("{{taxonomy_list}}", DocsTaxonomy.RenderForPrompt());
        if (forceScoped)
        {
            systemPrompt += "\n\n" + PracticeTopicScopingPrompt.CapReachedInstruction;
        }

        var result = await JsonModeChatClient.CallAsync(http, apiKey, new
        {
            model = modelName,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = FormatInput(topic, recentContext) },
            },
            response_format = new { type = "json_object" },
            temperature = 0,
        }, ct);

        var usage = result.Usage;
        if (usage is not null)
        {
            logger.LogInformation(
                "Practice topic-scoping call {Model} promptTokens={PromptTokens} completionTokens={CompletionTokens} totalTokens={TotalTokens}",
                modelName, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
            if ((usage.TotalTokens ?? 0) > 0)
            {
                await spendGuard.RecordAsync("practice-scoping", usage.TotalTokens!.Value, ct);
            }
        }

        var content = result.Content
                      ?? throw new InvalidOperationException("practice topic-scoping returned no message content");

        var json = JsonModeHelpers.StripCodeFence(content);
        var parsed = JsonSerializer.Deserialize<ScopingPayload>(json, JsonOptions)
                     ?? throw new InvalidOperationException("practice topic-scoping JSON deserialized to null");

        return new PracticeScopingResult(
            parsed.Scoped ?? false,
            parsed.ClarifyingQuestion,
            parsed.RefinedTopic,
            parsed.SubTopics ?? []);
    }

    internal static string FormatInput(string topic, IReadOnlyList<string>? recentContext)
    {
        var lines = new List<string>();
        if (recentContext is { Count: > 0 })
        {
            lines.Add($"[recentContext: {string.Join(", ", recentContext.Select(c => $"\"{JsonModeChatClient.Escape(c)}\""))}]");
        }
        lines.Add($"[topic: \"{JsonModeChatClient.Escape(topic)}\"]");
        return string.Join("\n", lines);
    }

    private sealed record ScopingPayload(
        [property: JsonPropertyName("scoped")] bool? Scoped,
        [property: JsonPropertyName("clarifyingQuestion")] string? ClarifyingQuestion,
        [property: JsonPropertyName("refinedTopic")] string? RefinedTopic,
        [property: JsonPropertyName("subTopics")] List<string>? SubTopics);
}
