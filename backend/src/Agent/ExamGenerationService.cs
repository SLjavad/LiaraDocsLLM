using System.Text.Json;
using System.Text.Json.Serialization;
using LiaraDocsAssistant.Data.Redis;
using LiaraDocsAssistant.Retrieval.Routing;
using Microsoft.Extensions.Logging;

namespace LiaraDocsAssistant.Agent;

public sealed record ExamGenerationChunk(int Index, string SubTopic, string Title, string Url, string? Anchor, string Body);

public sealed record ExamGenerationStep(
    int SourceChunkIndex,
    bool Skip,
    string? Question,
    IReadOnlyList<string>? Options,
    int? CorrectIndex,
    string? Explanation);

public interface IExamGenerationService
{
    Task<IReadOnlyList<ExamGenerationStep>> GenerateAsync(
        string topic, IReadOnlyList<ExamGenerationChunk> chunks, CancellationToken ct = default);
}

/// <summary>
/// specs/04-prompts.md §3: one call, batched across every planned sub-topic's
/// chunk (03-plan.md Phase 4b). The literal prompt asks for a bare JSON
/// array, not an object, so this deliberately does NOT set
/// response_format: json_object (that mode requires a top-level object on
/// most providers) — relies on the prompt's own "output ... nothing else"
/// instruction plus defensive code-fence stripping instead, same as every
/// other JSON-mode call here.
/// </summary>
public sealed class ExamGenerationService(
    HttpClient http,
    string modelName,
    string apiKey,
    ISpendGuard spendGuard,
    ILogger<ExamGenerationService> logger) : IExamGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<IReadOnlyList<ExamGenerationStep>> GenerateAsync(
        string topic, IReadOnlyList<ExamGenerationChunk> chunks, CancellationToken ct = default)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var result = await JsonModeChatClient.CallAsync(http, apiKey, new
        {
            model = modelName,
            messages = new object[]
            {
                new { role = "system", content = ExamGenerationPrompt.Text },
                new { role = "user", content = FormatInput(topic, chunks) },
            },
            temperature = 0,
        }, ct);

        var usage = result.Usage;
        if (usage is not null)
        {
            logger.LogInformation(
                "Exam-generation call {Model} promptTokens={PromptTokens} completionTokens={CompletionTokens} totalTokens={TotalTokens}",
                modelName, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
            if ((usage.TotalTokens ?? 0) > 0)
            {
                await spendGuard.RecordAsync("practice-exam-gen", usage.TotalTokens!.Value, ct);
            }
        }

        var content = result.Content
                      ?? throw new InvalidOperationException("exam-generation returned no message content");

        var json = JsonModeHelpers.StripCodeFence(content);
        var parsed = JsonSerializer.Deserialize<List<StepPayload>>(json, JsonOptions)
                     ?? throw new InvalidOperationException("exam-generation JSON deserialized to null");

        return [.. parsed.Select(p => new ExamGenerationStep(
            p.SourceChunkIndex,
            p.Skip ?? false,
            p.Question,
            p.Options,
            p.CorrectIndex,
            p.Explanation))];
    }

    private static string FormatInput(string topic, IReadOnlyList<ExamGenerationChunk> chunks)
    {
        var sections = chunks.Select(c =>
            $"[{c.Index}] sub-topic: {c.SubTopic}\ntitle: {c.Title}\nurl: {c.Url}\nanchor: {c.Anchor ?? "none"}\nbody: {c.Body}");
        return $"Topic: {topic}\n\nChunks:\n" + string.Join("\n\n", sections);
    }

    private sealed record StepPayload(
        [property: JsonPropertyName("skip")] bool? Skip,
        [property: JsonPropertyName("sourceChunkIndex")] int SourceChunkIndex,
        [property: JsonPropertyName("question")] string? Question,
        [property: JsonPropertyName("options")] List<string>? Options,
        [property: JsonPropertyName("correctIndex")] int? CorrectIndex,
        [property: JsonPropertyName("explanation")] string? Explanation);
}
