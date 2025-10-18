using LlmGateway.Caching;
using LlmGateway.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace LlmGateway.Tests.Unit;

public sealed class RedisCacheStoreTests
{
    [Fact]
    public void Codec_round_trips_entries_with_embeddings()
    {
        var entry = new CacheEntry(
            "key",
            "partition",
            [0.5f, -0.25f, 1e-7f, 3.4e38f],
            """{"id":"chatcmpl-1","choices":[]}""",
            DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_123));

        var decoded = RedisCacheEntryCodec.Decode(RedisCacheEntryCodec.Encode(entry))!;

        Assert.Equal(entry.Key, decoded.Key);
        Assert.Equal(entry.Partition, decoded.Partition);
        Assert.Equal(entry.Embedding, decoded.Embedding);
        Assert.Equal(entry.ResponseJson, decoded.ResponseJson);
        Assert.Equal(entry.ExpiresAt, decoded.ExpiresAt);
    }

    [Fact]
    public void Codec_round_trips_entries_without_embeddings()
    {
        var entry = new CacheEntry("key", string.Empty, null, "{}", DateTimeOffset.FromUnixTimeMilliseconds(0));

        Assert.Null(RedisCacheEntryCodec.Decode(RedisCacheEntryCodec.Encode(entry))!.Embedding);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"k":"key","r":"{}","e":"%%%","x":0}""")]
    [InlineData("""{"x":0}""")]
    public void Codec_rejects_unreadable_values(string value) =>
        Assert.Null(RedisCacheEntryCodec.Decode(value));

    [Fact]
    public async Task Unreachable_redis_degrades_to_cache_misses()
    {
        var configuration = ConfigurationOptions.Parse("127.0.0.1:1");
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = 200;
        configuration.ConnectRetry = 0;
        configuration.BacklogPolicy = BacklogPolicy.FailFast;
        await using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);

        var store = new RedisCompletionCacheStore(
            redis,
            Microsoft.Extensions.Options.Options.Create(new GatewayOptions()),
            TimeProvider.System,
            NullLogger<RedisCompletionCacheStore>.Instance);
        var entry = new CacheEntry("k", "p", [1f], "{}", DateTimeOffset.UtcNow.AddMinutes(5));

        await store.SetAsync(entry, CancellationToken.None);

        Assert.Null(await store.GetAsync("k", CancellationToken.None));
        Assert.Null(await store.FindNearestAsync("p", [1f], CancellationToken.None));
    }
}
