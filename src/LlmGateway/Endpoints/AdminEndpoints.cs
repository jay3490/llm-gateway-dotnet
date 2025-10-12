using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.Security;
using LlmGateway.Usage;
using Microsoft.AspNetCore.Mvc;

namespace LlmGateway.Endpoints;

public static class AdminEndpoints
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(1);

    /// <summary>Maps <c>/admin</c>, available only to API keys with <c>IsAdmin</c>.</summary>
    public static RouteGroupBuilder MapAdminApi(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").RequireAuthorization(GatewayPolicies.Admin);

        admin.MapGet("/usage", async (
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromQuery(Name = "api_key")] string? apiKey,
            UsageReportService reports,
            TimeProvider time,
            CancellationToken ct) =>
        {
            var toUtc = to?.UtcDateTime ?? time.GetUtcNow().UtcDateTime;
            var fromUtc = from?.UtcDateTime ?? toUtc - DefaultWindow;
            if (fromUtc >= toUtc)
            {
                return GatewayErrors.InvalidRequest("'from' must be earlier than 'to'.", "from");
            }

            var report = await reports.BuildAsync(fromUtc, toUtc, apiKey, ct);
            return Results.Json(report, GatewayJson.Options);
        });

        return admin;
    }
}
