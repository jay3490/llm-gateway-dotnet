using LlmGateway.Observability;
using LlmGateway.Usage;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace LlmGateway.Hosting;

public static class ObservabilityExtensions
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// Serilog for logs; OpenTelemetry traces and metrics for the gateway, ASP.NET Core and outgoing
    /// HTTP; health checks for the usage database and, when used, Redis. Telemetry is exported over
    /// OTLP when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set and collected but not exported otherwise.
    /// </summary>
    public static WebApplicationBuilder AddGatewayObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                builder.Configuration["OTEL_SERVICE_NAME"] ?? "llm-gateway",
                serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing => tracing
                .AddSource(GatewayTelemetry.Name)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(GatewayTelemetry.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<UsageDbContext>("usage-database", tags: [ReadyTag])
            .AddCheck<RedisCacheHealthCheck>("redis-cache", tags: [ReadyTag]);

        return builder;
    }

    /// <summary>
    /// <c>/health/live</c> only says the process is serving; <c>/health/ready</c> also checks its dependencies.
    /// </summary>
    public static WebApplication MapGatewayHealthChecks(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        });

        return app;
    }
}
