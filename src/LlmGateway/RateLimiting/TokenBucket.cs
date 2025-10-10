namespace LlmGateway.RateLimiting;

/// <summary>
/// A token bucket that refills continuously at <c>capacity / period</c>. Besides plain takes it
/// supports settling a reservation after the fact: token budgets are reserved from an estimate
/// before the upstream call and corrected to the real usage afterwards, which may leave the bucket
/// below zero so that later requests wait until the overspend has refilled.
/// </summary>
public sealed class TokenBucket
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly double _refillPerSecond;
    private double _tokens;
    private long _updatedAt;

    public TokenBucket(double capacity, TimeSpan period, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Capacity = capacity;
        _refillPerSecond = capacity / period.TotalSeconds;
        _timeProvider = timeProvider;
        _tokens = capacity;
        _updatedAt = timeProvider.GetTimestamp();
    }

    public double Capacity { get; }

    public double Available
    {
        get
        {
            lock (_gate)
            {
                Refill();
                return _tokens;
            }
        }
    }

    /// <summary>
    /// Takes <paramref name="amount"/> if the bucket holds it. A request larger than the whole bucket
    /// is allowed once the bucket is full, so oversized requests are slowed down rather than refused forever.
    /// </summary>
    public bool TryTake(double amount, out TimeSpan retryAfter)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        lock (_gate)
        {
            Refill();
            var required = Math.Min(amount, Capacity);

            if (_tokens >= required)
            {
                _tokens -= amount;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            retryAfter = TimeSpan.FromSeconds((required - _tokens) / _refillPerSecond);
            return false;
        }
    }

    /// <summary>Adds tokens back, never beyond capacity.</summary>
    public void Return(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        lock (_gate)
        {
            Refill();
            _tokens = Math.Min(Capacity, _tokens + amount);
        }
    }

    /// <summary>Removes tokens unconditionally; the balance may go negative.</summary>
    public void Consume(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        lock (_gate)
        {
            Refill();
            _tokens -= amount;
        }
    }

    private void Refill()
    {
        var now = _timeProvider.GetTimestamp();
        var elapsed = _timeProvider.GetElapsedTime(_updatedAt, now);
        _tokens = Math.Min(Capacity, _tokens + (elapsed.TotalSeconds * _refillPerSecond));
        _updatedAt = now;
    }
}
