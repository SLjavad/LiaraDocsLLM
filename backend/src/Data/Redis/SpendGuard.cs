using StackExchange.Redis;

namespace LiaraDocsAssistant.Data.Redis;

public interface ISpendGuard
{
    Task<bool> IsBudgetExhaustedAsync(CancellationToken ct = default);
    Task RecordAsync(string consumer, int totalTokens, CancellationToken ct = default);
}

public sealed class RedisSpendGuard(IConnectionMultiplexer redis, long dailyBudgetTokens) : ISpendGuard
{
    public async Task<bool> IsBudgetExhaustedAsync(CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var used = await db.StringGetAsync(RedisKeys.SpendDaily(DateTimeOffset.UtcNow));
        return used.HasValue && (long)used! >= dailyBudgetTokens;
    }

    public async Task RecordAsync(string consumer, int totalTokens, CancellationToken ct = default)
    {
        if (totalTokens <= 0)
        {
            return;
        }

        var db = redis.GetDatabase();
        var key = RedisKeys.SpendDaily(DateTimeOffset.UtcNow);
        var newTotal = await db.StringIncrementAsync(key, totalTokens);
        if (newTotal == totalTokens)
        {
            await db.KeyExpireAsync(key, TimeSpan.FromHours(48));
        }
    }
}
