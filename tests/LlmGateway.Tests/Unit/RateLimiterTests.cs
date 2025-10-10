using LlmGateway.RateLimiting;
using LlmGateway.Security;
using Microsoft.Extensions.Time.Testing;

namespace LlmGateway.Tests.Unit;

public sealed class RateLimiterTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Bucket_refills_continuously()
    {
        var bucket = new TokenBucket(60, TimeSpan.FromMinutes(1), _time);
        Assert.True(bucket.TryTake(60, out _));
        Assert.False(bucket.TryTake(1, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);

        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(10, bucket.Available, precision: 6);
        Assert.True(bucket.TryTake(10, out _));
        Assert.False(bucket.TryTake(1, out _));
    }

    [Fact]
    public void Bucket_never_exceeds_capacity()
    {
        var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1), _time);

        _time.Advance(TimeSpan.FromHours(1));
        bucket.Return(100);

        Assert.Equal(5, bucket.Available, precision: 6);
    }

    [Fact]
    public void Oversized_request_is_allowed_from_a_full_bucket_and_leaves_debt()
    {
        var bucket = new TokenBucket(100, TimeSpan.FromMinutes(1), _time);

        Assert.True(bucket.TryTake(250, out _));
        Assert.Equal(-150, bucket.Available, precision: 6);
        Assert.False(bucket.TryTake(250, out var retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(150), retryAfter);
    }

    [Fact]
    public void Request_budget_is_enforced_per_key()
    {
        var limiter = new KeyRateLimiter(_time);
        var first = Key("a", requestsPerMinute: 2);
        var second = Key("b", requestsPerMinute: 2);

        Assert.True(limiter.TryAcquire(first, 0).IsAcquired);
        Assert.True(limiter.TryAcquire(first, 0).IsAcquired);
        var rejected = limiter.TryAcquire(first, 0);

        Assert.False(rejected.IsAcquired);
        Assert.Equal(RateLimitKind.Requests, rejected.LimitedBy);
        Assert.Equal(TimeSpan.FromSeconds(30), rejected.RetryAfter);
        Assert.True(limiter.TryAcquire(second, 0).IsAcquired);
    }

    [Fact]
    public void Settling_charges_real_usage_against_the_token_budget()
    {
        var limiter = new KeyRateLimiter(_time);
        var key = Key("a", tokensPerMinute: 1000);

        using (var lease = limiter.TryAcquire(key, 100))
        {
            lease.Settle(900);
        }

        var next = limiter.TryAcquire(key, 200);

        Assert.False(next.IsAcquired);
        Assert.Equal(RateLimitKind.Tokens, next.LimitedBy);
    }

    [Fact]
    public void Unsettled_lease_returns_its_reservation()
    {
        var limiter = new KeyRateLimiter(_time);
        var key = Key("a", tokensPerMinute: 1000);

        limiter.TryAcquire(key, 1000).Dispose();

        Assert.True(limiter.TryAcquire(key, 1000).IsAcquired);
    }

    [Fact]
    public void Token_rejection_gives_back_the_request_slot()
    {
        var limiter = new KeyRateLimiter(_time);
        var key = Key("a", requestsPerMinute: 2, tokensPerMinute: 10);

        limiter.TryAcquire(key, 10).Settle(10);
        Assert.Equal(RateLimitKind.Tokens, limiter.TryAcquire(key, 10).LimitedBy);

        Assert.True(limiter.TryAcquire(key, 0).IsAcquired);
        Assert.Equal(RateLimitKind.Requests, limiter.TryAcquire(key, 0).LimitedBy);
    }

    [Fact]
    public void Zero_limits_mean_unlimited()
    {
        var limiter = new KeyRateLimiter(_time);
        var key = Key("a");

        for (var i = 0; i < 1000; i++)
        {
            Assert.True(limiter.TryAcquire(key, 10_000).IsAcquired);
        }
    }

    private static ApiKeyDefinition Key(string name, int requestsPerMinute = 0, int tokensPerMinute = 0) =>
        new(name, new HashSet<string>(), IsAdmin: false, requestsPerMinute, tokensPerMinute);
}
