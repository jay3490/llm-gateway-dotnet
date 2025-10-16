using System.Text.Json;
using LlmGateway.Caching;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace LlmGateway.Tests.Unit;

public sealed class ExactCacheTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Key_ignores_property_order_and_transport_fields()
    {
        var first = Parse("""{"model":"Smart","temperature":0,"messages":[{"role":"user","content":"hi"}],"stream":false,"user":"u1"}""");
        var second = Parse("""{"messages":[{"content":"hi","role":"user"}],"temperature":0,"model":"smart","user":"u2"}""");

        Assert.Equal(CacheKeys.Exact("smart", first), CacheKeys.Exact("smart", second));
    }

    [Theory]
    [InlineData("""{"model":"m","messages":[{"role":"user","content":"hello"}]}""")]
    [InlineData("""{"model":"m","messages":[{"role":"user","content":"hi"}],"temperature":1}""")]
    [InlineData("""{"model":"m","messages":[{"role":"system","content":"be brief"},{"role":"user","content":"hi"}]}""")]
    public void Key_changes_with_anything_that_affects_the_output(string other)
    {
        var baseline = Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""");

        Assert.NotEqual(CacheKeys.Exact("m", baseline), CacheKeys.Exact("m", Parse(other)));
    }

    [Fact]
    public void Key_differs_per_alias()
    {
        var request = Parse("""{"model":"m","messages":[{"role":"user","content":"hi"}]}""");

        Assert.NotEqual(CacheKeys.Exact("a", request), CacheKeys.Exact("b", request));
    }

    [Fact]
    public async Task Entries_expire_after_their_ttl()
    {
        var store = new InMemoryCompletionCacheStore(Options(), _time);
        await store.SetAsync(Entry("k", TimeSpan.FromMinutes(5)), CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.NotNull(await store.GetAsync("k", CancellationToken.None));

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await store.GetAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task Store_evicts_the_entries_closest_to_expiry_when_full()
    {
        var store = new InMemoryCompletionCacheStore(Options(maxEntries: 2), _time);

        await store.SetAsync(Entry("soon", TimeSpan.FromMinutes(1)), CancellationToken.None);
        await store.SetAsync(Entry("later", TimeSpan.FromMinutes(10)), CancellationToken.None);
        await store.SetAsync(Entry("latest", TimeSpan.FromMinutes(20)), CancellationToken.None);

        Assert.Equal(2, store.Count);
        Assert.Null(await store.GetAsync("soon", CancellationToken.None));
        Assert.NotNull(await store.GetAsync("latest", CancellationToken.None));
    }

    private CacheEntry Entry(string key, TimeSpan ttl) =>
        new(key, string.Empty, null, "{}", _time.GetUtcNow() + ttl);

    private static IOptions<GatewayOptions> Options(int maxEntries = 100) =>
        Microsoft.Extensions.Options.Options.Create(new GatewayOptions { Cache = new CacheOptions { MaxEntries = maxEntries } });

    private static ChatCompletionRequest Parse(string json) =>
        JsonSerializer.Deserialize<ChatCompletionRequest>(json, GatewayJson.Options)!;
}
