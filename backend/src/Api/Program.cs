using LiaraDocsAssistant.Api.Configuration;
using LiaraDocsAssistant.Api.Middleware;
using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Ingestion;
using LiaraDocsAssistant.Ingestion.Chunking;
using LiaraDocsAssistant.Ingestion.Crawling;
using LiaraDocsAssistant.Ingestion.Embedding;
using LiaraDocsAssistant.Ingestion.Persistence;
using LiaraDocsAssistant.Ingestion.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

LocalEnvFile.Load();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var options = AppOptions.Load(builder.Configuration);

    builder.Host.UseSerilog();

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton(new DbConfig(options.Embedding.Dim));

    builder.Services.AddDbContext<AppDbContext>(dbOptions =>
        dbOptions.UseNpgsql(options.PostgresConnectionString, npgsql => npgsql.UseVector()));

    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    {
        var redisOptions = ConfigurationOptions.Parse(options.RedisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(redisOptions);
    });

    builder.Services.AddCors(corsOptions => corsOptions.AddDefaultPolicy(policy => policy
        .WithOrigins(options.Api.CorsAllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()));

    builder.Services.AddHttpClient("embeddings", client =>
        {
            client.BaseAddress = new Uri(options.Embedding.BaseUrl);
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddStandardResilienceHandler(resilience =>
        {
            resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
            resilience.Retry.MaxRetryAttempts = 4;
            resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(5);
        });

    builder.Services.AddHttpClient("docs-crawler", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("LiaraDocsAssistant-Ingestion/1.0");
        })
        .AddStandardResilienceHandler();

    builder.Services.AddSingleton<OpenAiCompatibleEmbeddingService>((sp) =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        return new OpenAiCompatibleEmbeddingService(
            httpClientFactory.CreateClient("embeddings"),
            options.Embedding.ModelName,
            options.Embedding.ApiKey,
            options.Embedding.UseInputType,
            sp.GetRequiredService<ILogger<OpenAiCompatibleEmbeddingService>>());
    });

    builder.Services.AddSingleton<IEmbeddingService>(sp => new CachedEmbeddingService(
        sp.GetRequiredService<OpenAiCompatibleEmbeddingService>(),
        sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase(),
        sp.GetRequiredService<ILogger<CachedEmbeddingService>>()));

    builder.Services.AddSingleton(new IngestionSettings(
        options.Embedding.Dim,
        options.Embedding.UseInputType,
        options.Ingestion.EmbedBatchSize,
        options.Ingestion.SitemapUrl,
        options.Ingestion.CrawlConcurrency,
        options.Ingestion.CrawlDelayMs,
        options.ObjectStorage.Endpoint,
        options.ObjectStorage.AccessKey,
        options.ObjectStorage.SecretKey,
        options.ObjectStorage.BucketName,
        options.ObjectStorage.SeedObjectKey));

    builder.Services.AddSingleton<DocsSiteCrawler>(sp => new DocsSiteCrawler(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("docs-crawler")));

    builder.Services.AddSingleton<ISectionChunker>(new SectionChunker());
    builder.Services.AddSingleton<SeedRestoreService>();
    builder.Services.AddScoped<DocChunkStore>();

    builder.Services.AddHostedService<IngestionBackgroundService>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        Log.Information("Database migrations applied");

        await db.VerifyEmbeddingDimensionAsync(options.Embedding.Dim);
        Log.Information("Embedding dimension verified ({EmbedDim})", options.Embedding.Dim);

        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        if (redis.IsConnected)
        {
            Log.Information("Redis connection established");
        }
        else
        {
            Log.Warning("Redis not yet connected at startup ({Endpoint}); StackExchange.Redis will keep retrying in the background",
                ConfigurationOptions.Parse(options.RedisConnectionString).EndPoints.FirstOrDefault());
        }
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseCors();

    app.MapGet("/health", async (AppDbContext db, ILogger<Program> logger, CancellationToken ct) =>
    {
        var ingestion = "pending";
        try
        {
            ingestion = await db.DocChunks.AnyAsync(ct) ? "complete" : "pending";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Health check could not read doc_chunks");
        }

        return Results.Ok(new { status = "ok", ingestion });
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Liara Docs Assistant terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
