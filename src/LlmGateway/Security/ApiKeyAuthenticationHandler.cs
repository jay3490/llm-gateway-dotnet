using System.Security.Claims;
using System.Text.Encodings.Web;
using LlmGateway.Errors;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LlmGateway.Security;

/// <summary>
/// Authenticates clients by API key, taken from <c>Authorization: Bearer</c> as OpenAI clients send
/// it, or from an <c>api-key</c> / <c>x-api-key</c> header. Challenges and forbidden responses use
/// the OpenAI error envelope.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyStore keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string AdminRole = "admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadPresentedKey(Request);
        if (presented is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!keys.TryFind(presented, out var key))
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, key.Name) };
        if (key.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        }

        Context.SetApiKey(key);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        var message = ReadPresentedKey(Request) is null
            ? "You didn't provide an API key. Send it as 'Authorization: Bearer <key>'."
            : "Incorrect API key provided.";

        await GatewayErrors.Create(StatusCodes.Status401Unauthorized, message, "invalid_request_error", code: "invalid_api_key")
            .ExecuteAsync(Context);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        await GatewayErrors.Create(StatusCodes.Status403Forbidden, "This API key is not allowed to use this endpoint.", "permission_error", code: "permission_denied")
            .ExecuteAsync(Context);

    private static string? ReadPresentedKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            return token.Length > 0 ? token : null;
        }

        foreach (var header in (string[])["api-key", "x-api-key"])
        {
            var value = request.Headers[header].ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }
}
