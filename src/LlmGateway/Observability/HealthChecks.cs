using System.Text.Json;
using LlmGateway.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LlmGateway.Observability;

/// <summary>Pings Redis when it backs the cache; reports healthy without touching it otherwise.</summary>
public sealed class RedisCacheHealthCheck(IOptions<GatewayOptions> options, IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (options.Value.Cache is not { Enabled: true, Store: CacheStoreKind.Redis })
        {
            return HealthCheckResult.Healthy("Redis is not used by this configuration.");
        }

        try
        {
            var latency = await services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:F1} ms");
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // The gateway keeps serving without its cache, so an unreachable Redis degrades rather than fails readiness.
            return HealthCheckResult.Degraded("Redis is unreachable; requests bypass the cache.", exception);
        }
    }
}

public static class HealthResponseWriter
{
    /// <summary>Writes <c>{"status": ..., "checks": {name: {status, description}}}</c>.</summary>
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }
}
