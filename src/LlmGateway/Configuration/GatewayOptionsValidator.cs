using Microsoft.Extensions.Options;

namespace LlmGateway.Configuration;

/// <summary>Fails startup on configuration that would only surface as errors at request time.</summary>
public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        foreach (var (upstreamName, upstream) in options.Upstreams)
        {
            ValidateUpstream(upstreamName, upstream, failures);
        }

        foreach (var (alias, route) in options.Models)
        {
            if (route.Targets.Count == 0)
            {
                failures.Add($"Model '{alias}' has no targets.");
            }

            foreach (var target in route.Targets.Concat(route.Fallbacks))
            {
                if (!options.Upstreams.ContainsKey(target.Upstream))
                {
                    failures.Add($"Model '{alias}' refers to unknown upstream '{target.Upstream}'.");
                }

                if (string.IsNullOrWhiteSpace(target.Model))
                {
                    failures.Add($"Model '{alias}' has a target on '{target.Upstream}' without an upstream model name.");
                }

                if (target.Weight <= 0)
                {
                    failures.Add($"Model '{alias}' has a target on '{target.Upstream}' with a non-positive weight.");
                }
            }
        }

        ValidateResilience(options.Resilience, failures);
        ValidateApiKeys(options, failures);

        if (options.Cache.Ttl <= TimeSpan.Zero || options.Cache.MaxEntries <= 0)
        {
            failures.Add("Cache:Ttl and Cache:MaxEntries must be positive.");
        }

        if (options.Cache is { Enabled: true, SemanticEnabled: true })
        {
            if (!options.Models.TryGetValue(options.Cache.EmbeddingModel, out var embeddingRoute) || embeddingRoute.Kind != ModelKind.Embedding)
            {
                failures.Add($"Cache:EmbeddingModel '{options.Cache.EmbeddingModel}' must name an Embedding model.");
            }

            if (options.Cache.SimilarityThreshold is <= 0 or > 1)
            {
                failures.Add("Cache:SimilarityThreshold must be in (0, 1].");
            }
        }

        if (options.Guardrails.MaxRequestBytes <= 0)
        {
            failures.Add("Guardrails:MaxRequestBytes must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateApiKeys(GatewayOptions options, List<string> failures)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, key) in options.ApiKeys)
        {
            if (key.KeyHash.Length != 64 || !key.KeyHash.All(char.IsAsciiHexDigit))
            {
                failures.Add($"API key '{name}' needs a KeyHash of 64 hex characters (SHA-256).");
            }
            else if (!seen.Add(key.KeyHash))
            {
                failures.Add($"API key '{name}' has the same KeyHash as another key.");
            }

            if (key.RequestsPerMinute < 0 || key.TokensPerMinute < 0)
            {
                failures.Add($"API key '{name}' has a negative rate limit.");
            }

            foreach (var model in key.AllowedModels.Where(m => m != "*" && !options.Models.ContainsKey(m)))
            {
                failures.Add($"API key '{name}' allows unknown model '{model}'.");
            }
        }
    }

    private static void ValidateResilience(ResilienceOptions resilience, List<string> failures)
    {
        if (resilience.AttemptTimeout <= TimeSpan.Zero)
        {
            failures.Add("Resilience:AttemptTimeout must be positive.");
        }

        if (resilience.MaxRetryAttempts < 0)
        {
            failures.Add("Resilience:MaxRetryAttempts cannot be negative.");
        }

        if (resilience.CircuitBreakerFailureRatio is <= 0 or > 1)
        {
            failures.Add("Resilience:CircuitBreakerFailureRatio must be in (0, 1].");
        }

        if (resilience.CircuitBreakerMinimumThroughput < 2)
        {
            failures.Add("Resilience:CircuitBreakerMinimumThroughput must be at least 2.");
        }

        if (resilience.CircuitBreakerSamplingDuration < TimeSpan.FromMilliseconds(500)
            || resilience.CircuitBreakerBreakDuration < TimeSpan.FromMilliseconds(500))
        {
            failures.Add("Resilience circuit breaker durations must be at least 500 ms.");
        }
    }

    private static void ValidateUpstream(string name, UpstreamOptions upstream, List<string> failures)
    {
        if (!Enum.IsDefined(upstream.Kind))
        {
            failures.Add($"Upstream '{name}' has an unknown kind.");
        }

        if (upstream.Kind == UpstreamKind.OpenAICompatible && upstream.BaseUrl is not { IsAbsoluteUri: true })
        {
            failures.Add($"Upstream '{name}' needs an absolute BaseUrl.");
        }
    }
}
