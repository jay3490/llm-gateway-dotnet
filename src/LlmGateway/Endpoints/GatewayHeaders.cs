namespace LlmGateway.Endpoints;

/// <summary>Response headers the gateway adds so callers can see how a request was served.</summary>
public static class GatewayHeaders
{
    /// <summary>Name of the upstream that produced the response.</summary>
    public const string Upstream = "X-Gateway-Upstream";

    /// <summary>
    /// On responses: how the cache was used (<c>hit-exact</c>, <c>miss</c> or <c>bypass</c>).
    /// On requests: <c>bypass</c> skips the cache for that request, as does <c>Cache-Control: no-cache</c>.
    /// </summary>
    public const string Cache = "X-Gateway-Cache";
}
