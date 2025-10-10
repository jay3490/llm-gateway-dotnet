using System.Globalization;

namespace LlmGateway.RateLimiting;

/// <summary>
/// The outcome of a rate-limit check. A granted lease holds a token reservation that must be settled
/// with the real usage once it is known; disposing an unsettled lease releases the reservation, which
/// is what should happen when the upstream call fails.
/// </summary>
public sealed class RateLimitLease : IDisposable
{
    private readonly KeyBuckets _buckets;
    private readonly int _reservedTokens;
    private bool _settled;

    private RateLimitLease(KeyBuckets buckets, bool isAcquired, int reservedTokens, RateLimitKind? limitedBy, TimeSpan retryAfter)
    {
        _buckets = buckets;
        IsAcquired = isAcquired;
        _reservedTokens = reservedTokens;
        LimitedBy = limitedBy;
        RetryAfter = retryAfter;
        _settled = !isAcquired;
    }

    public bool IsAcquired { get; }

    /// <summary>Which budget ran out, for rejected leases.</summary>
    public RateLimitKind? LimitedBy { get; }

    public TimeSpan RetryAfter { get; }

    internal static RateLimitLease Granted(KeyBuckets buckets, int reservedTokens) =>
        new(buckets, isAcquired: true, reservedTokens, limitedBy: null, TimeSpan.Zero);

    internal static RateLimitLease Rejected(KeyBuckets buckets, RateLimitKind limitedBy, TimeSpan retryAfter) =>
        new(buckets, isAcquired: false, reservedTokens: 0, limitedBy, retryAfter);

    /// <summary>Replaces the token reservation with what the request really used.</summary>
    public void Settle(int actualTokens)
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        if (_buckets.Tokens is not { } tokens)
        {
            return;
        }

        var difference = actualTokens - _reservedTokens;
        if (difference > 0)
        {
            tokens.Consume(difference);
        }
        else if (difference < 0)
        {
            tokens.Return(-difference);
        }
    }

    /// <summary>Writes the OpenAI-style <c>x-ratelimit-*</c> headers, plus <c>Retry-After</c> when rejected.</summary>
    public void WriteHeaders(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (_buckets.Requests is { } requests)
        {
            response.Headers["x-ratelimit-limit-requests"] = Format(requests.Capacity);
            response.Headers["x-ratelimit-remaining-requests"] = Format(Math.Max(0, requests.Available));
        }

        if (_buckets.Tokens is { } tokens)
        {
            response.Headers["x-ratelimit-limit-tokens"] = Format(tokens.Capacity);
            response.Headers["x-ratelimit-remaining-tokens"] = Format(Math.Max(0, tokens.Available));
        }

        if (!IsAcquired)
        {
            response.Headers.RetryAfter = Format(Math.Max(1, Math.Ceiling(RetryAfter.TotalSeconds)));
        }
    }

    public void Dispose() => Settle(0);

    private static string Format(double value) => Math.Floor(value).ToString(CultureInfo.InvariantCulture);
}
