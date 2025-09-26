namespace LlmGateway.Providers;

/// <summary>An upstream answered with a non-success status code.</summary>
public sealed class UpstreamException : Exception
{
    public UpstreamException()
    {
    }

    public UpstreamException(string message)
        : base(message)
    {
    }

    public UpstreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UpstreamException(string upstream, int statusCode, string? responseBody)
        : base($"Upstream '{upstream}' returned HTTP {statusCode}.")
    {
        Upstream = upstream;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string Upstream { get; } = string.Empty;

    public int StatusCode { get; }

    /// <summary>The raw response body, kept so client errors can be passed through verbatim.</summary>
    public string? ResponseBody { get; }

    public bool IsRateLimited => StatusCode == StatusCodes.Status429TooManyRequests;

    /// <summary>Server-side failures that another attempt, or another upstream, may not hit.</summary>
    public bool IsServerError => StatusCode >= 500 || StatusCode == StatusCodes.Status408RequestTimeout;

    /// <summary>Failures that should move the request on to a fallback target.</summary>
    public bool IsTransient => IsRateLimited || IsServerError;
}
