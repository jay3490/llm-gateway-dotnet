using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Providers;

/// <summary>Creates one <see cref="ILlmProvider"/> per configured upstream and hands them out by name.</summary>
public sealed class UpstreamRegistry
{
    private readonly Dictionary<string, ILlmProvider> _providers;

    public UpstreamRegistry(IOptions<GatewayOptions> options, IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);

        _providers = options.Value.Upstreams.ToDictionary(
            pair => pair.Key,
            pair => Create(pair.Key, pair.Value, httpClientFactory, timeProvider),
            StringComparer.OrdinalIgnoreCase);
    }

    public ILlmProvider Get(string upstream) =>
        _providers.TryGetValue(upstream, out var provider)
            ? provider
            : throw new InvalidOperationException($"Upstream '{upstream}' is not configured.");

    private static ILlmProvider Create(string name, UpstreamOptions upstream, IHttpClientFactory httpClientFactory, TimeProvider timeProvider) =>
        upstream.Kind switch
        {
            UpstreamKind.Fake => new FakeLlmProvider(timeProvider),
            UpstreamKind.OpenAICompatible => new OpenAiCompatibleProvider(name, upstream, httpClientFactory),
            _ => throw new InvalidOperationException($"Unsupported upstream kind '{upstream.Kind}'."),
        };
}
