using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LiaraDocsAssistant.Ingestion.Embedding;

public sealed class OpenAiCompatibleEmbeddingService(
    HttpClient httpClient,
    string model,
    string apiKey,
    bool useInputType,
    ILogger<OpenAiCompatibleEmbeddingService> logger) : IEmbeddingService
{
    public async Task<float[]> EmbedDocumentAsync(string text, CancellationToken ct = default)
    {
        var result = await EmbedBatchInternalAsync([text], InputType.Document, ct);
        return Single(result, "document");
    }

    public async Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
    {
        var result = await EmbedBatchInternalAsync([text], InputType.Query, ct);
        return Single(result, "query");
    }

    public async Task<IReadOnlyList<float[]?>> EmbedDocumentsBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        try
        {
            var embedded = await EmbedBatchInternalAsync(texts, InputType.Document, ct);
            return [.. embedded];
        }
        catch (SizeRelatedException ex)
        {
            logger.LogWarning(ex,
                "Embedding provider rejected a batch of {Count} items as too large; halving",
                texts.Count);
            return await HalveAndRetryAsync(texts, ct);
        }
    }

    private async Task<List<float[]?>> HalveAndRetryAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 1)
        {
            try
            {
                var single = await EmbedBatchInternalAsync([texts[0]], InputType.Document, ct);
                return single;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Single-text embedding failed after halving to the minimum; skipping item");
                return [null];
            }
        }

        var midpoint = texts.Count / 2;
        var left = await HalveAndRetryAsync(texts.Take(midpoint).ToList(), ct);
        var right = await HalveAndRetryAsync(texts.Skip(midpoint).ToList(), ct);
        return [.. left, .. right];
    }

    private static float[] Single(List<float[]?> result, string kind)
    {
        if (result.Count == 0 || result[0] is null)
        {
            throw new InvalidOperationException($"embedding call returned no {kind} embedding");
        }
        return result[0]!;
    }

    private async Task<List<float[]?>> EmbedBatchInternalAsync(
        IReadOnlyList<string> texts,
        InputType inputType,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, httpClient.BaseAddress);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input"] = texts,
        };
        if (useInputType)
        {
            body["input_type"] = inputType == InputType.Query ? "query" : "passage";
        }
        request.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            var detail = await response.Content.ReadAsStringAsync(ct);

            if ((int)status is >= 400 and < 500 && IsPlausiblySizeRelated(status, detail))
            {
                throw new SizeRelatedException(status, detail);
            }

            throw new HttpRequestException(
                $"embedding endpoint returned {(int)status} {(HttpStatusCode)status}", null, status);
        }

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct);
        if (payload?.Data is null || payload.Data.Count == 0)
        {
            throw new InvalidOperationException("embedding endpoint returned an empty data array");
        }

        if (payload.Usage is { } usage)
        {
            logger.LogInformation(
                "Embedding call {Model} embeddings items={Items} promptTokens={PromptTokens} totalTokens={TotalTokens}",
                model, texts.Count, usage.PromptTokens, usage.TotalTokens);
        }

        if (payload.Data.Count != texts.Count)
        {
            throw new InvalidOperationException(
                $"embedding endpoint returned {payload.Data.Count} vectors for {texts.Count} inputs");
        }

        var byIndex = new Dictionary<int, float[]>();
        foreach (var item in payload.Data)
        {
            if (item.Embedding is null)
            {
                throw new InvalidOperationException($"embedding response missing vector at index {item.Index}");
            }
            if (!byIndex.TryAdd(item.Index, item.Embedding))
            {
                throw new InvalidOperationException($"embedding response returned duplicate index {item.Index}");
            }
        }

        var ordered = new List<float[]?>(payload.Data.Count);
        for (var i = 0; i < payload.Data.Count; i++)
        {
            if (!byIndex.TryGetValue(i, out var vector))
            {
                throw new InvalidOperationException($"embedding response missing vector at index {i}");
            }
            ordered.Add(vector);
        }

        return ordered;
    }

    private static bool IsPlausiblySizeRelated(HttpStatusCode statusCode, string detail)
    {
        if ((int)statusCode is not (400 or 404 or 413 or 422 or 431))
        {
            return false;
        }

        return (int)statusCode == 413 ||
               detail.Contains("too large", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("too long", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("too many input", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("exceeds", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("maximum", StringComparison.OrdinalIgnoreCase);
    }

    private enum InputType { Document, Query }

    private sealed class SizeRelatedException(HttpStatusCode status, string body)
        : Exception($"size-related embedding rejection ({(int)status}): {Truncate(body)}")
    {
        private static string Truncate(string s) =>
            s.Length <= 300 ? s : s[..300] + "…";
    }

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingData>? Data,
        [property: JsonPropertyName("usage")] EmbeddingUsage? Usage);

    private sealed record EmbeddingUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("total_tokens")] int? TotalTokens);

    private sealed record EmbeddingData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
