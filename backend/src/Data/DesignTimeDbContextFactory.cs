using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace LiaraDocsAssistant.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        LocalEnvFile.Load();

        var embedDim = Environment.GetEnvironmentVariable("EMBED_DIM");
        if (string.IsNullOrWhiteSpace(embedDim) || !int.TryParse(embedDim, out var dim) || dim <= 0)
        {
            throw new InvalidOperationException(
                $"EMBED_DIM must be set to a positive integer to design-time-build the DbContext. Set it in the repo-root .env file (found at '{LocalEnvFile.Find() ?? "<repo root>"}').");
        }

        var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "POSTGRES_CONNECTION_STRING must be set to design-time-build the DbContext. Set it in the repo-root .env file.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;

        return new AppDbContext(options, new DbConfig(dim));
    }
}
