namespace LiaraDocsAssistant.Data.Redis;

public static class RedisKeys
{
    public static string RateSession(string sessionId, DateTimeOffset windowStart) =>
        $"rate:session:{sessionId}:{windowStart.ToUniversalTime():yyyyMMddHHmm}";

    public static string RateIp(string ip, DateTimeOffset windowStart) =>
        $"rate:ip:{ip}:{windowStart.ToUniversalTime():yyyyMMddHHmm}";

    public static string SpendDaily(DateTimeOffset utcNow) =>
        $"spend:daily:{utcNow.ToUniversalTime():yyyy-MM-dd}";

    public static string CacheSearch(string hash) => $"cache:search:{hash}";

    public static string CacheEmbedding(string hash) => $"cache:embedding:{hash}";
}
