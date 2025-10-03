using LlmGateway.Configuration;
using LlmGateway.Providers;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Retry;
using Polly.Timeout;

namespace LlmGateway.Routing;

/// <summary>
/// One Polly pipeline per upstream: retry with exponential backoff and jitter, then a circuit
/// breaker, then a per-attempt timeout. Keeping a circuit per upstream means one failing backend is
/// skipped quickly without affecting routes that only use healthy ones.
/// </summary>
public sealed partial class UpstreamResiliencePipelines : IDisposable
{
    private readonly ResiliencePipelineRegistry<string> _registry = new();
    private readonly GatewayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpstreamResiliencePipelines> _logger;

    public UpstreamResiliencePipelines(IOptions<GatewayOptions> options, TimeProvider timeProvider, ILogger<UpstreamResiliencePipelines> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public ResiliencePipeline Get(string upstream) =>
        _registry.GetOrAddPipeline(upstream, builder => Configure(builder, upstream));

    public void Dispose() => _registry.Dispose();

    /// <summary>Failures worth another attempt on the same upstream. Rate limits are not: the next target is a better bet.</summary>
    internal static bool IsRetryable(Exception exception) => exception switch
    {
        UpstreamException upstream => upstream.IsServerError,
        HttpRequestException or TimeoutRejectedException => true,
        _ => false,
    };

    /// <summary>Failures that count against an upstream's health.</summary>
    internal static bool CountsAsFailure(Exception exception) => exception switch
    {
        UpstreamException upstream => upstream.IsTransient,
        HttpRequestException or TimeoutRejectedException => true,
        _ => false,
    };

    private void Configure(ResiliencePipelineBuilder builder, string upstream)
    {
        var resilience = _options.Resilience;
        var timeout = _options.Upstreams.TryGetValue(upstream, out var upstreamOptions) && upstreamOptions.AttemptTimeout is { } own
            ? own
            : resilience.AttemptTimeout;

        builder.TimeProvider = _timeProvider;

        if (resilience.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = resilience.MaxRetryAttempts,
                Delay = resilience.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is { } e && IsRetryable(e)),
                OnRetry = args =>
                {
                    LogRetry(_logger, upstream, args.AttemptNumber + 1, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                },
            });
        }

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = resilience.CircuitBreakerFailureRatio,
            MinimumThroughput = resilience.CircuitBreakerMinimumThroughput,
            SamplingDuration = resilience.CircuitBreakerSamplingDuration,
            BreakDuration = resilience.CircuitBreakerBreakDuration,
            ShouldHandle = args => ValueTask.FromResult(args.Outcome.Exception is { } e && CountsAsFailure(e)),
            OnOpened = args =>
            {
                LogCircuitOpened(_logger, upstream, args.BreakDuration.TotalSeconds);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                LogCircuitClosed(_logger, upstream);
                return ValueTask.CompletedTask;
            },
        });

        builder.AddTimeout(timeout);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retrying upstream {Upstream} (attempt {Attempt}) in {DelayMs:F0} ms: {Reason}")]
    private static partial void LogRetry(ILogger logger, string upstream, int attempt, double delayMs, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Circuit for upstream {Upstream} opened for {BreakSeconds:F0} s")]
    private static partial void LogCircuitOpened(ILogger logger, string upstream, double breakSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Circuit for upstream {Upstream} closed")]
    private static partial void LogCircuitClosed(ILogger logger, string upstream);
}
