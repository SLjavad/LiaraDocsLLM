using LiaraDocsAssistant.Data;
using LiaraDocsAssistant.Data.Redis;
using StackExchange.Redis;

namespace LiaraDocsAssistant.Api.RateLimiting;

public sealed record RateLimitResult(bool Allowed, string? LimitingKey, int RetryAfterSeconds);

/// <summary>
/// NFR1 dual-key rate limiting: fixed one-minute windows, counters in Redis,
/// whichever limit (session or IP) is hit first blocks the request.
/// </summary>
public sealed class RateLimiter(IConnectionMultiplexer redis)
{
    private static readonly TimeSpan WindowTtl = TimeSpan.FromSeconds(65);

    public async Task<RateLimitResult> CheckAndIncrementAsync(
        string? sessionId,
        string clientIp,
        int sessionLimit,
        int ipLimit,
        CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionResult = await IncrementAsync(db, RedisKeys.RateSession(sessionId, now), sessionLimit, ct);
            if (!sessionResult.Allowed)
            {
                return sessionResult with { LimitingKey = $"session:{sessionId}" };
            }
        }

        var ipResult = await IncrementAsync(db, RedisKeys.RateIp(clientIp, now), ipLimit, ct);
        return ipResult.Allowed
            ? ipResult
            : ipResult with { LimitingKey = $"ip:{clientIp}" };
    }

    private static async Task<RateLimitResult> IncrementAsync(IDatabase db, string key, int limit, CancellationToken ct)
    {
        var count = await db.StringIncrementAsync(key);
        if (count == 1)
        {
            await db.KeyExpireAsync(key, WindowTtl);
        }

        if (count <= limit)
        {
            return new RateLimitResult(true, null, 0);
        }

        var secondsIntoWindow = 60 - (DateTimeOffset.UtcNow.Second);
        return new RateLimitResult(false, key, Math.Max(1, secondsIntoWindow));
    }
}
