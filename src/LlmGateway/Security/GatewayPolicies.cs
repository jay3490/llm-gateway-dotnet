namespace LlmGateway.Security;

public static class GatewayPolicies
{
    /// <summary>Requires an API key with <c>IsAdmin</c> set.</summary>
    public const string Admin = "admin";
}
