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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateUpstream(string name, UpstreamOptions upstream, List<string> failures)
    {
        if (!Enum.IsDefined(upstream.Kind))
        {
            failures.Add($"Upstream '{name}' has an unknown kind.");
        }
    }
}
