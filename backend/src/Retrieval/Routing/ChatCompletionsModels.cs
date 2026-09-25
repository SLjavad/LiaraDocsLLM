using System.Text.Json.Serialization;

namespace LiaraDocsAssistant.Retrieval.Routing;

/// <summary>
/// Minimal OpenAI-wire chat-completions response shape, shared by every
/// one-shot JSON-mode call in this codebase (router, practice topic-scoping)
/// — just enough fields to read the message content and token usage.
/// </summary>
public sealed record ChatCompletionsResponse(
    [property: JsonPropertyName("choices")] List<ChatCompletionsChoice>? Choices,
    [property: JsonPropertyName("usage")] ChatCompletionsUsage? Usage)
{
    public string? FirstMessageContent => Choices?.FirstOrDefault()?.Message?.Content;
}

public sealed record ChatCompletionsChoice(
    [property: JsonPropertyName("message")] ChatCompletionsMessage? Message);

public sealed record ChatCompletionsMessage(
    [property: JsonPropertyName("content")] string? Content);

public sealed record ChatCompletionsUsage(
    [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
    [property: JsonPropertyName("completion_tokens")] int? CompletionTokens,
    [property: JsonPropertyName("total_tokens")] int? TotalTokens);
