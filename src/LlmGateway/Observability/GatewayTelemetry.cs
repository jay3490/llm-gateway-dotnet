using System.Diagnostics;
using System.Diagnostics.Metrics;
using LlmGateway.Caching;
using LlmGateway.RateLimiting;
using LlmGateway.Usage;

namespace LlmGateway.Observability;

/// <summary>
/// The gateway's own traces and metrics. Everything is published under the <c>LlmGateway</c>
/// activity source and meter; ASP.NET Core and HttpClient instrumentation cover the rest.
/// </summary>
public sealed class GatewayTelemetry
{
    public const string Name = "LlmGateway";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _tokens;
    private readonly Counter<long> _cacheLookups;
    private readonly Histogram<double> _upstreamDuration;
    private readonly Counter<long> _rateLimitRejections;
    private long _cacheHits;
    private long _cacheLookupCount;

    public GatewayTelemetry(IMeterFactory meterFactory, UsageRecorder usageRecorder)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(usageRecorder);

        var meter = meterFactory.Create(Name);

        _tokens = meter.CreateCounter<long>(
            "llm_gateway.tokens", "{token}", "Tokens consumed upstream, by type (input or output).");
        _cacheLookups = meter.CreateCounter<long>(
            "llm_gateway.cache.lookups", "{lookup}", "Cache lookups by result: hit_exact, hit_semantic or miss.");
        _upstreamDuration = meter.CreateHistogram<double>(
            "llm_gateway.upstream.duration", "s", "Time spent on one upstream target, including retries against it.");
        _rateLimitRejections = meter.CreateCounter<long>(
            "llm_gateway.rate_limit.rejections", "{request}", "Requests rejected by a per-key budget.");

        meter.CreateObservableGauge(
            "llm_gateway.cache.hit_ratio",
            () => Interlocked.Read(ref _cacheLookupCount) == 0
                ? 0
                : (double)Interlocked.Read(ref _cacheHits) / Interlocked.Read(ref _cacheLookupCount),
            description: "Share of cache lookups answered from the cache since the process started.");
        meter.CreateObservableCounter(
            "llm_gateway.usage.dropped",
            () => usageRecorder.Dropped,
            "{record}",
            "Usage records dropped because the write queue was full.");
    }

    public void RecordTokens(string modelAlias, string upstream, int promptTokens, int completionTokens)
    {
        var alias = new KeyValuePair<string, object?>("llm_gateway.model", modelAlias);
        var source = new KeyValuePair<string, object?>("llm_gateway.upstream", upstream);
        _tokens.Add(promptTokens, alias, source, new("gen_ai.token.type", "input"));
        _tokens.Add(completionTokens, alias, source, new("gen_ai.token.type", "output"));
    }

    public void RecordCacheLookup(string modelAlias, CacheOutcome outcome)
    {
        var result = outcome switch
        {
            CacheOutcome.ExactHit => "hit_exact",
            CacheOutcome.SemanticHit => "hit_semantic",
            _ => "miss",
        };

        Interlocked.Increment(ref _cacheLookupCount);
        if (outcome is CacheOutcome.ExactHit or CacheOutcome.SemanticHit)
        {
            Interlocked.Increment(ref _cacheHits);
        }

        _cacheLookups.Add(1, new("llm_gateway.model", modelAlias), new("llm_gateway.cache.result", result));
    }

    public void RecordUpstreamDuration(string upstream, string upstreamModel, string outcome, TimeSpan duration) =>
        _upstreamDuration.Record(
            duration.TotalSeconds,
            new("llm_gateway.upstream", upstream),
            new("gen_ai.request.model", upstreamModel),
            new("llm_gateway.outcome", outcome));

    public void RecordRateLimitRejection(RateLimitKind kind) =>
        _rateLimitRejections.Add(1, new KeyValuePair<string, object?>("llm_gateway.rate_limit.kind", kind == RateLimitKind.Tokens ? "tokens" : "requests"));
}
