namespace LlmGateway.Security;

public static class HttpContextApiKeyExtensions
{
    private static readonly object ItemKey = new();

    internal static void SetApiKey(this HttpContext httpContext, ApiKeyDefinition key) =>
        httpContext.Items[ItemKey] = key;

    /// <summary>The authenticated API key. Only valid on endpoints that require authorization.</summary>
    public static ApiKeyDefinition GetApiKey(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items[ItemKey] as ApiKeyDefinition
            ?? throw new InvalidOperationException("The request has not been authenticated with an API key.");
    }
}
