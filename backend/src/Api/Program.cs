using LiaraDocsAssistant.Api.Configuration;
using LiaraDocsAssistant.Api.Middleware;
using LiaraDocsAssistant.Data;
using Microsoft.EntityFrameworkCore;
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
