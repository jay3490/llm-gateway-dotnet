using System.Net.Http.Headers;
using LlmGateway.Providers;
using LlmGateway.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LlmGateway.Tests.Infrastructure;

/// <summary>Hosts the gateway in memory with per-test configuration layered over appsettings.json.</summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    /// <summary>Admin key with access to every model; clients from <c>CreateClient()</c> send it by default.</summary>
    public const string AdminKey = "sk-test-admin";

    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _targetCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"llm-gateway-tests-{Guid.NewGuid():N}.db");

    public GatewayFactory()
    {
        // Keep retries fast; individual tests override these where timing matters.
        WithSetting("Gateway:Resilience:RetryBaseDelay", "00:00:00.001");
        WithSetting("Gateway:Resilience:AttemptTimeout", "00:00:10");
        WithApiKey("test-admin", AdminKey, isAdmin: true);
        WithSetting("ConnectionStrings:Usage", $"Data Source={_databasePath}");
    }

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

    public GatewayFactory WithApiKey(string name, string key, bool isAdmin = false, params string[] allowedModels)
    {
        WithSetting($"Gateway:ApiKeys:{name}:KeyHash", ApiKeyStore.Hash(key));
        WithSetting($"Gateway:ApiKeys:{name}:IsAdmin", isAdmin ? "true" : "false");
        for (var i = 0; i < allowedModels.Length; i++)
        {
            WithSetting($"Gateway:ApiKeys:{name}:AllowedModels:{i}", allowedModels[i]);
        }

        return this;
    }

    /// <summary>A client that authenticates with <paramref name="key"/>, or sends no key when it is null.</summary>
    public HttpClient CreateClientWithKey(string? key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = key is null ? null : new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

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

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminKey);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_databasePath);
        }
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
