namespace LiaraDocsAssistant.Api.Configuration;

public sealed record ChatModelOptions(string BaseUrl, string ApiKey, string ModelName, string RouterModelName);

public sealed record EmbeddingOptions(string BaseUrl, string ApiKey, string ModelName, int Dim, bool UseInputType);

public sealed record RateLimitOptions(int SessionPerMin, int IpPerMin);

public sealed record RetrievalOptions(int TopK, double GroundednessThreshold);

public sealed record TriageOptions(int MaxClarifyingRounds);

public sealed record PracticeOptions(int MinSteps, int MaxSteps);

public sealed record ObjectStorageOptions(string? Endpoint, string? AccessKey, string? SecretKey, string? BucketName, string? SeedObjectKey)
{
    public bool IsSeedConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(AccessKey) &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(BucketName) &&
        !string.IsNullOrWhiteSpace(SeedObjectKey);
}

public sealed record IngestionOptions(string SitemapUrl, int CrawlConcurrency, int CrawlDelayMs, int EmbedBatchSize);

public sealed record ApiGatewayOptions(int MaxInputChars, string[] CorsAllowedOrigins, string? SupportChannelUrl);

public sealed record AppOptions(
    ChatModelOptions ChatModel,
    EmbeddingOptions Embedding,
    string PostgresConnectionString,
    string RedisConnectionString,
    RateLimitOptions RateLimit,
    int DailySpendBudgetTokens,
    RetrievalOptions Retrieval,
    TriageOptions Triage,
    PracticeOptions Practice,
    ApiGatewayOptions Api,
    ObjectStorageOptions ObjectStorage,
    IngestionOptions Ingestion)
{
    public static AppOptions Load(IConfiguration configuration)
    {
        var errors = new List<string>();

        var chatBaseUrl = Require(configuration, "CHAT_MODEL_BASE_URL", errors);
        var chatApiKey = Require(configuration, "CHAT_MODEL_API_KEY", errors);
        var chatModelName = Require(configuration, "CHAT_MODEL_NAME", errors);
        var routerModelName = Require(configuration, "ROUTER_MODEL_NAME", errors);

        var embeddingBaseUrl = Require(configuration, "EMBEDDING_BASE_URL", errors);
        var embeddingApiKey = Require(configuration, "EMBEDDING_API_KEY", errors);
        var embeddingModelName = Require(configuration, "EMBEDDING_MODEL_NAME", errors);
        var embeddingDim = RequireInt(configuration, "EMBED_DIM", errors, min: 1);
        var embeddingUseInputType = RequireBool(configuration, "EMBEDDING_USE_INPUT_TYPE", errors);

        var postgres = Require(configuration, "POSTGRES_CONNECTION_STRING", errors);
        var redis = Require(configuration, "REDIS_CONNECTION_STRING", errors);

        var rateSession = RequireInt(configuration, "RATE_LIMIT_SESSION_PER_MIN", errors, min: 1);
        var rateIp = RequireInt(configuration, "RATE_LIMIT_IP_PER_MIN", errors, min: 1);
        var dailyBudget = RequireInt(configuration, "DAILY_SPEND_BUDGET_TOKENS", errors, min: 1);

        var topK = RequireInt(configuration, "RETRIEVAL_TOP_K", errors, min: 1);
        var groundedness = RequireDouble(configuration, "RETRIEVAL_GROUNDEDNESS_THRESHOLD", errors, min: 0, max: 1);

        var maxClarifying = RequireInt(configuration, "MAX_CLARIFYING_ROUNDS", errors, min: 0);
        var practiceMin = RequireInt(configuration, "PRACTICE_MIN_STEPS", errors, min: 1);
        var practiceMax = RequireInt(configuration, "PRACTICE_MAX_STEPS", errors, min: 1);

        var maxInputChars = RequireInt(configuration, "MAX_INPUT_CHARS", errors, min: 1);
        var corsOrigin = Require(configuration, "CORS_ALLOWED_ORIGIN", errors);
        var supportChannel = Optional(configuration, "SUPPORT_CHANNEL_URL");

        var sitemapUrl = Require(configuration, "DOCS_SITEMAP_URL", errors);
        var crawlConcurrency = RequireInt(configuration, "CRAWL_CONCURRENCY", errors, min: 1);
        var crawlDelayMs = RequireInt(configuration, "CRAWL_DELAY_MS", errors, min: 0);
        var embedBatchSize = RequireInt(configuration, "EMBED_BATCH_SIZE", errors, min: 1);

        var objectStorageVars = new Dictionary<string, string?>
        {
            ["OBJECT_STORAGE_API_ENDPOINT"] = Optional(configuration, "OBJECT_STORAGE_API_ENDPOINT"),
            ["OBJECT_STORAGE_ACCESS_KEY"] = Optional(configuration, "OBJECT_STORAGE_ACCESS_KEY"),
            ["OBJECT_STORAGE_SECRET_KEY"] = Optional(configuration, "OBJECT_STORAGE_SECRET_KEY"),
            ["OBJECT_STORAGE_BUCKET_NAME"] = Optional(configuration, "OBJECT_STORAGE_BUCKET_NAME"),
            ["SEED_OBJECT_KEY"] = Optional(configuration, "SEED_OBJECT_KEY"),
        };

        var objectStorageSetCount = objectStorageVars.Values.Count(v => v is not null);
        if (objectStorageSetCount > 0 && objectStorageSetCount < objectStorageVars.Count)
        {
            foreach (var (name, value) in objectStorageVars)
            {
                if (value is null)
                {
                    errors.Add(
                        $"{name} must be set: the five Object Storage variables are configured as a group — all five set or none set");
                }
            }
        }

        var objectStorage = new ObjectStorageOptions(
            objectStorageVars["OBJECT_STORAGE_API_ENDPOINT"],
            objectStorageVars["OBJECT_STORAGE_ACCESS_KEY"],
            objectStorageVars["OBJECT_STORAGE_SECRET_KEY"],
            objectStorageVars["OBJECT_STORAGE_BUCKET_NAME"],
            objectStorageVars["SEED_OBJECT_KEY"]);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Invalid or missing configuration. The following environment variables must be set " +
                "(see .env.example and specs/02-technical-spec.md §8):\n  - " + string.Join("\n  - ", errors));
        }

        if (practiceMin > practiceMax)
        {
            throw new InvalidOperationException("PRACTICE_MIN_STEPS must not exceed PRACTICE_MAX_STEPS.");
        }

        return new AppOptions(
            ChatModel: new ChatModelOptions(chatBaseUrl, chatApiKey, chatModelName, routerModelName),
            Embedding: new EmbeddingOptions(embeddingBaseUrl, embeddingApiKey, embeddingModelName, embeddingDim, embeddingUseInputType),
            PostgresConnectionString: postgres,
            RedisConnectionString: redis,
            RateLimit: new RateLimitOptions(rateSession, rateIp),
            DailySpendBudgetTokens: dailyBudget,
            Retrieval: new RetrievalOptions(topK, groundedness),
            Triage: new TriageOptions(maxClarifying),
            Practice: new PracticeOptions(practiceMin, practiceMax),
            Api: new ApiGatewayOptions(maxInputChars, corsOrigin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), supportChannel),
            ObjectStorage: objectStorage,
            Ingestion: new IngestionOptions(sitemapUrl, crawlConcurrency, crawlDelayMs, embedBatchSize));
    }

    private static string Require(IConfiguration configuration, string key, List<string> errors)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is missing or empty");
            return string.Empty;
        }
        return value.Trim();
    }

    private static string? Optional(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int RequireInt(IConfiguration configuration, string key, List<string> errors, int min)
    {
        var value = Require(configuration, key, errors);
        if (value.Length == 0) return 0;
        if (!int.TryParse(value, out var parsed))
        {
            errors.Add($"{key} must be an integer (got '{value}')");
            return 0;
        }
        if (parsed < min)
        {
            errors.Add($"{key} must be >= {min} (got {parsed})");
            return 0;
        }
        return parsed;
    }

    private static double RequireDouble(IConfiguration configuration, string key, List<string> errors, double min, double max)
    {
        var value = Require(configuration, key, errors);
        if (value.Length == 0) return 0;
        if (!double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            errors.Add($"{key} must be a number (got '{value}')");
            return 0;
        }
        if (parsed < min || parsed > max)
        {
            errors.Add($"{key} must be between {min} and {max} (got {parsed})");
            return 0;
        }
        return parsed;
    }

    private static bool RequireBool(IConfiguration configuration, string key, List<string> errors)
    {
        var value = Require(configuration, key, errors);
        if (value.Length == 0) return false;
        if (!bool.TryParse(value, out var parsed))
        {
            errors.Add($"{key} must be 'true' or 'false' (got '{value}')");
            return false;
        }
        return parsed;
    }
}
