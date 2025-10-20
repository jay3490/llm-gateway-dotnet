using System.Diagnostics;
using LlmGateway.Observability;
using LlmGateway.Providers;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace LlmGateway.Routing;

/// <summary>The value an upstream returned, plus the target that produced it.</summary>
/// <param name="FailedTargets">How many targets failed before this one succeeded.</param>
public sealed record RoutedResult<T>(T Value, RouteTarget Target, int FailedTargets);

/// <summary>
/// Executes a call for a model alias against its targets in the order the <see cref="RoutePlanner"/>
/// picks, each through its upstream's resilience pipeline. Transient failures (429, 5xx, timeouts,
/// network errors, open circuits) move on to the next target; client errors such as a 400 are
/// thrown straight back, because another upstream would reject the same request.
/// </summary>
public sealed partial class ModelRouter(
    UpstreamRegistry upstreams,
    UpstreamResiliencePipelines pipelines,
    RoutePlanner planner,
    GatewayTelemetry telemetry,
    ILogger<ModelRouter> logger)
{
    public async Task<RoutedResult<T>> ExecuteAsync<T>(
        ModelRoute route,
        Func<ILlmProvider, string, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(call);

        var failures = new List<UpstreamFailure>();

        foreach (var target in planner.Plan(route))
        {
            var provider = upstreams.Get(target.Upstream);
            var pipeline = pipelines.Get(target.Upstream);

            using var activity = GatewayTelemetry.ActivitySource.StartActivity("upstream " + target.Upstream, ActivityKind.Client);
            activity?.SetTag("llm_gateway.model", route.Alias);
            activity?.SetTag("llm_gateway.upstream", target.Upstream);
            activity?.SetTag("gen_ai.request.model", target.Model);
            var started = Stopwatch.GetTimestamp();
            var outcome = "error";

            try
            {
                var value = await pipeline.ExecuteAsync(
                    async token => await call(provider, target.Model, token),
                    cancellationToken);

                outcome = "success";
                return new RoutedResult<T>(value, target, failures.Count);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && Describe(target, exception) is { } failure)
            {
                outcome = failure.CircuitOpen ? "circuit_open" : failure.TimedOut ? "timeout" : "transient_error";
                failures.Add(failure);
                LogFailover(logger, route.Alias, target.Upstream, target.Model, failure.Reason);
            }
            finally
            {
                telemetry.RecordUpstreamDuration(target.Upstream, target.Model, outcome, Stopwatch.GetElapsedTime(started));
                if (outcome != "success")
                {
                    activity?.SetStatus(ActivityStatusCode.Error, outcome);
                }
            }
        }

        throw new AllUpstreamsFailedException(route.Alias, failures);
    }

    /// <summary>Describes a failure that should fall through to the next target, or returns null for one that should not.</summary>
    private static UpstreamFailure? Describe(RouteTarget target, Exception exception) => exception switch
    {
        UpstreamException { IsTransient: true } upstream =>
            new(target.Upstream, target.Model, $"HTTP {upstream.StatusCode}", upstream.StatusCode, TimedOut: false, CircuitOpen: false),
        TimeoutRejectedException =>
            new(target.Upstream, target.Model, "timed out", null, TimedOut: true, CircuitOpen: false),
        BrokenCircuitException =>
            new(target.Upstream, target.Model, "circuit open", null, TimedOut: false, CircuitOpen: true),
        HttpRequestException http =>
            new(target.Upstream, target.Model, http.Message, null, TimedOut: false, CircuitOpen: false),
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Model {Alias}: target {Upstream}/{Model} failed ({Reason})")]
    private static partial void LogFailover(ILogger logger, string alias, string upstream, string model, string reason);
}
