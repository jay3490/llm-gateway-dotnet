using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Providers;

/// <summary>Creates one <see cref="ILlmProvider"/> per configured upstream and hands them out by name.</summary>
public sealed class UpstreamRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers;

    public UpstreamRegistry(IOptions<GatewayOptions> options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        _providers = options.Value.Upstreams.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Kind switch
            {
                UpstreamKind.Fake => (ILlmProvider)new FakeLlmProvider(timeProvider),
                _ => throw new InvalidOperationException($"Unsupported upstream kind '{pair.Value.Kind}'."),
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public ILlmProvider Get(string upstream) =>
        _providers.TryGetValue(upstream, out var provider)
            ? provider
            : throw new InvalidOperationException($"Upstream '{upstream}' is not configured.");
}
