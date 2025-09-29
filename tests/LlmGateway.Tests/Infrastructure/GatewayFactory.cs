using LlmGateway.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmGateway.Tests.Infrastructure;

/// <summary>Hosts the gateway in memory with per-test configuration layered over appsettings.json.</summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _targetCounts = new(StringComparer.OrdinalIgnoreCase);

    public FakeUpstreamHandler Upstreams { get; } = new();

    public GatewayFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    /// <summary>Adds an OpenAI-compatible upstream served by <see cref="Upstreams"/> at <c>http://{host}/v1</c>.</summary>
    public GatewayFactory WithUpstream(string name, string host, string? apiKey = null) =>
        WithSetting($"Gateway:Upstreams:{name}:Kind", "OpenAICompatible")
            .WithSetting($"Gateway:Upstreams:{name}:BaseUrl", $"http://{host}/v1")
            .WithSetting($"Gateway:Upstreams:{name}:ApiKey", apiKey);

    public GatewayFactory WithTarget(string alias, string upstream, string model, int weight = 1, string kind = "Chat") =>
        AddTarget(alias, "Targets", upstream, model, weight, kind);

    public GatewayFactory WithFallback(string alias, string upstream, string model, string kind = "Chat") =>
        AddTarget(alias, "Fallbacks", upstream, model, 1, kind);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(OpenAiCompatibleProvider.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Upstreams));
    }

    private GatewayFactory AddTarget(string alias, string list, string upstream, string model, int weight, string kind)
    {
        var key = $"{alias}:{list}";
        var index = _targetCounts.GetValueOrDefault(key);
        _targetCounts[key] = index + 1;

        return WithSetting($"Gateway:Models:{alias}:Kind", kind)
            .WithSetting($"Gateway:Models:{alias}:{list}:{index}:Upstream", upstream)
            .WithSetting($"Gateway:Models:{alias}:{list}:{index}:Model", model)
            .WithSetting($"Gateway:Models:{alias}:{list}:{index}:Weight", weight.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
