namespace LlmGateway.Routing;

/// <summary>Why one target of a route could not serve a request.</summary>
/// <param name="StatusCode">The upstream's HTTP status, when it answered at all.</param>
public sealed record UpstreamFailure(string Upstream, string Model, string Reason, int? StatusCode, bool TimedOut, bool CircuitOpen);

/// <summary>Every target of a route, primary and fallback, failed with a transient error.</summary>
public sealed class AllUpstreamsFailedException : Exception
{
    public AllUpstreamsFailedException()
    {
    }

    public AllUpstreamsFailedException(string message)
        : base(message)
    {
    }

    public AllUpstreamsFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AllUpstreamsFailedException(string alias, IReadOnlyList<UpstreamFailure> failures)
        : base($"All upstreams for model '{alias}' failed: " + string.Join("; ", failures.Select(f => $"{f.Upstream} ({f.Reason})")))
    {
        Alias = alias;
        Failures = failures;
    }

    public string Alias { get; } = string.Empty;

    public IReadOnlyList<UpstreamFailure> Failures { get; } = [];

    /// <summary>True when every target was rate limited, so the caller should back off rather than report an outage.</summary>
    public bool AllRateLimited => Failures.Count > 0 && Failures.All(f => f.StatusCode == StatusCodes.Status429TooManyRequests);

    public bool LastTimedOut => Failures.Count > 0 && Failures[^1].TimedOut;
}
