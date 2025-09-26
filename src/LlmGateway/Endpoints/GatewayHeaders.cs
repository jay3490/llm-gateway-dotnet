namespace LlmGateway.Endpoints;

/// <summary>Response headers the gateway adds so callers can see how a request was served.</summary>
public static class GatewayHeaders
{
    /// <summary>Name of the upstream that produced the response.</summary>
    public const string Upstream = "X-Gateway-Upstream";
}
