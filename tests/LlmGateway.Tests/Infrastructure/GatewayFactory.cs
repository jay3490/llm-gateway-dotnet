using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace LlmGateway.Tests.Infrastructure;

/// <summary>Hosts the gateway in memory with per-test configuration layered over appsettings.json.</summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings = new(StringComparer.OrdinalIgnoreCase);

    public GatewayFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
    }
}
