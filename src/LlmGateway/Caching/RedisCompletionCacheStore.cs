using LlmGateway.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LlmGateway.Caching;

/// <summary>
/// Cache store shared by every gateway instance through Redis.
/// <list type="bullet">
/// <item><c>{prefix}entry:{key}</c> holds an encoded entry and expires with it.</item>
/// <item><c>{prefix}partition:{partition}</c> is a sorted set of entry keys scored by expiry time,
/// used to find semantic candidates.</item>
/// </list>
/// Similarity is computed in the gateway over at most <see cref="CacheOptions.MaxSemanticCandidates"/>
/// of the newest entries in a partition, so plain Redis is enough; no vector search module is needed.
/// Redis errors are logged and treated as misses: an unavailable cache slows requests down rather
/// than failing them.
/// </summary>
public sealed partial class RedisCompletionCacheStore(
    IConnectionMultiplexer redis,
    IOptions<GatewayOptions> options,
    TimeProvider timeProvider,
    ILogger<RedisCompletionCacheStore> logger) : ICompletionCacheStore
{
    private CacheOptions Options => options.Value.Cache;

    private IDatabase Database => redis.GetDatabase();

    public async Task<CacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await Database.StringGetAsync(EntryKey(key));
            return value.IsNullOrEmpty ? null : RedisCacheEntryCodec.Decode(value.ToString());
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            LogRedisFailure(logger, exception, "get");
            return null;
        }
    }

    public async Task<SemanticMatch?> FindNearestAsync(string partition, float[] embedding, CancellationToken cancellationToken)
    {
        try
        {
            var index = IndexKey(partition);
            var now = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            await Database.SortedSetRemoveRangeByScoreAsync(index, double.NegativeInfinity, now);

            var keys = await Database.SortedSetRangeByRankAsync(index, 0, Options.MaxSemanticCandidates - 1, Order.Descending);
            if (keys.Length == 0)
            {
                return null;
            }

            var values = await Database.StringGetAsync(keys.Select(k => (RedisKey)EntryKey(k.ToString())).ToArray());

            SemanticMatch? best = null;
            foreach (var value in values)
            {
                if (value.IsNullOrEmpty || RedisCacheEntryCodec.Decode(value.ToString()) is not { Embedding: { } candidate } entry)
                {
                    continue;
                }

                var similarity = VectorMath.CosineSimilarity(embedding, candidate);
                if (best is null || similarity > best.Similarity)
                {
                    best = new SemanticMatch(entry, similarity);
                }
            }

            return best;
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            LogRedisFailure(logger, exception, "semantic lookup");
            return null;
        }
    }

    public async Task SetAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var ttl = entry.ExpiresAt - timeProvider.GetUtcNow();
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await Database.StringSetAsync(EntryKey(entry.Key), RedisCacheEntryCodec.Encode(entry), ttl);

            if (entry.Embedding is not null)
            {
                var index = IndexKey(entry.Partition);
                await Database.SortedSetAddAsync(index, entry.Key, entry.ExpiresAt.ToUnixTimeMilliseconds());
                await Database.KeyExpireAsync(index, ttl);
            }
        }
        catch (Exception exception) when (IsRedisFailure(exception))
        {
            LogRedisFailure(logger, exception, "set");
        }
    }

    private static bool IsRedisFailure(Exception exception) => exception is RedisException or TimeoutException;

    private string EntryKey(string key) => $"{Options.RedisKeyPrefix}entry:{key}";

    private string IndexKey(string partition) => $"{Options.RedisKeyPrefix}partition:{partition}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Redis cache {Operation} failed; treating it as a miss")]
    private static partial void LogRedisFailure(ILogger logger, Exception exception, string operation);
}
