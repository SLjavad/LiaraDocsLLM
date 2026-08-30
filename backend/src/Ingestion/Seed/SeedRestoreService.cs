using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;

namespace LiaraDocsAssistant.Ingestion.Seed;

public sealed class SeedRestoreService(
    IngestionSettings settings,
    ILogger<SeedRestoreService> logger)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(settings.ObjectStorageEndpoint) &&
        !string.IsNullOrWhiteSpace(settings.ObjectStorageAccessKey) &&
        !string.IsNullOrWhiteSpace(settings.ObjectStorageSecretKey) &&
        !string.IsNullOrWhiteSpace(settings.ObjectStorageBucketName) &&
        !string.IsNullOrWhiteSpace(settings.ObjectStorageSeedKey);

    public async Task<bool> TryRestoreAsync(string postgresConnectionString, CancellationToken ct)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"liara-docs-seed-{Guid.NewGuid():N}.dump");
        try
        {
            await DownloadSeedAsync(tempFile, ct);

            logger.LogInformation("Seed dump downloaded ({SizeBytes} bytes); running pg_restore", new FileInfo(tempFile).Length);

            var exitCode = RunPgRestore(postgresConnectionString, tempFile, out var output, out var errors);
            if (exitCode != 0)
            {
                logger.LogError("pg_restore failed with exit {ExitCode}: {Errors}", exitCode, Truncate(errors));
                return false;
            }

            if (output.Length > 0)
            {
                logger.LogInformation("pg_restore output: {Output}", Truncate(output));
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Seed fetch/restore failed; falling back to the live crawl path");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch (IOException)
            {
            }
        }
    }

    private async Task DownloadSeedAsync(string destination, CancellationToken ct)
    {
        using var s3 = new AmazonS3Client(
            settings.ObjectStorageAccessKey,
            settings.ObjectStorageSecretKey,
            new AmazonS3Config
            {
                ServiceURL = settings.ObjectStorageEndpoint,
                ForcePathStyle = true,
            });

        using var response = await s3.GetObjectAsync(new GetObjectRequest
        {
            BucketName = settings.ObjectStorageBucketName,
            Key = settings.ObjectStorageSeedKey,
        }, ct);

        await using var fileStream = File.Create(destination);
        await response.ResponseStream.CopyToAsync(fileStream, ct);
    }

    private int RunPgRestore(string connectionString, string dumpFile, out string output, out string errors)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pg_restore",
                ArgumentList =
                {
                    "--data-only",
                    "--table=doc_chunks",
                    "--no-owner",
                    "--dbname=" + connectionString,
                    dumpFile,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        output = stdoutTask.Result;
        errors = stderrTask.Result;
        return process.ExitCode;
    }

    private static string Truncate(string s) => s.Length <= 1000 ? s : s[..1000] + "…";
}
