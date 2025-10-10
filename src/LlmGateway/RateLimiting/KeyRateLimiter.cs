using System.Collections.Concurrent;
using LlmGateway.Security;

namespace LlmGateway.RateLimiting;

/// <summary>
/// Per-key request and token budgets, each a <see cref="TokenBucket"/> refilled over one minute.
/// State is in memory, so limits apply per gateway instance.
/// </summary>
public sealed class KeyRateLimiter(TimeProvider timeProvider)
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, KeyBuckets> _buckets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Takes one request and reserves <paramref name="estimatedTokens"/> from the key's budgets.</summary>
    public RateLimitLease TryAcquire(ApiKeyDefinition key, int estimatedTokens)
    {
        ArgumentNullException.ThrowIfNull(key);

        var buckets = _buckets.GetOrAdd(key.Name, _ => new KeyBuckets(
            Create(key.RequestsPerMinute),
            Create(key.TokensPerMinute)));

        if (buckets.Requests is { } requests && !requests.TryTake(1, out var requestWait))
        {
            return RateLimitLease.Rejected(buckets, RateLimitKind.Requests, requestWait);
        }

        if (buckets.Tokens is { } tokens && !tokens.TryTake(estimatedTokens, out var tokenWait))
        {
            buckets.Requests?.Return(1);
            return RateLimitLease.Rejected(buckets, RateLimitKind.Tokens, tokenWait);
        }

        return RateLimitLease.Granted(buckets, estimatedTokens);
    }

    private TokenBucket? Create(int perMinute) =>
        perMinute > 0 ? new TokenBucket(perMinute, Period, timeProvider) : null;
}

/// <summary>The buckets of one key; a null bucket means that dimension is unlimited.</summary>
public sealed record KeyBuckets(TokenBucket? Requests, TokenBucket? Tokens);

public enum RateLimitKind
{
    Requests,
    Tokens,
}
