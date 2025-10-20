using LlmGateway.Caching;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Endpoints;
using LlmGateway.Errors;
using LlmGateway.Guardrails;
using LlmGateway.Observability;
using LlmGateway.Providers;
using LlmGateway.RateLimiting;
using LlmGateway.Routing;
using LlmGateway.Security;
using LlmGateway.Usage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace LlmGateway.Hosting;

public static class GatewayServiceCollectionExtensions
{
    /// <summary>Registers configuration, routing, security, usage accounting, guardrails and caching.</summary>
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>()
            .Bind(configuration.GetSection(GatewayOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>();

        services.Configure<JsonOptions>(options => GatewayJson.Configure(options.SerializerOptions));
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.AddExceptionHandler<GatewayExceptionHandler>();
        services.AddProblemDetails();
        services.AddSingleton(TimeProvider.System);

        services.AddRouting();
        services.AddSecurity();
        services.AddUsageAccounting();
        services.AddCaching();

        services.AddSingleton<BlockedTermPolicy>();
        services.AddSingleton<GatewayTelemetry>();
        services.AddScoped<ChatCompletionHandler>();
        services.AddScoped<EmbeddingsHandler>();

        return services;
    }

    private static void AddRouting(this IServiceCollection services)
    {
        // Polly's per-attempt timeout governs upstream calls, so the client itself never times out.
        services.AddHttpClient(OpenAiCompatibleProvider.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<UpstreamRegistry>();
        services.AddSingleton<ModelCatalog>();
        services.AddSingleton(new RoutePlanner(Random.Shared));
        services.AddSingleton<UpstreamResiliencePipelines>();
        services.AddSingleton<ModelRouter>();
        services.AddSingleton<SseRelay>();
    }

    private static void AddSecurity(this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyStore>();
        services.AddSingleton<KeyRateLimiter>();
        services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(GatewayPolicies.Admin, policy => policy.RequireRole(ApiKeyAuthenticationHandler.AdminRole));
    }

    private static void AddUsageAccounting(this IServiceCollection services)
    {
        services.AddDbContextFactory<UsageDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<IConfiguration>().GetConnectionString("Usage") ?? "Data Source=data/usage.db"));
        services.AddSingleton<UsageRecorder>();
        services.AddHostedService<UsageWriter>();
        services.AddSingleton<UsageReportService>();
    }

    private static void AddCaching(this IServiceCollection services)
    {
        // The store is chosen from options at resolution time, so environments can switch it by configuration alone.
        services.AddSingleton<ICompletionCacheStore>(provider =>
            provider.GetRequiredService<IOptions<GatewayOptions>>().Value.Cache.Store == CacheStoreKind.Redis
                ? ActivatorUtilities.CreateInstance<RedisCompletionCacheStore>(provider)
                : ActivatorUtilities.CreateInstance<InMemoryCompletionCacheStore>(provider));
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Cache:Store is Redis but ConnectionStrings:Redis is not set.");
            var redisOptions = ConfigurationOptions.Parse(connectionString);
            redisOptions.AbortOnConnectFail = false;
            // Fail cache calls straight away while disconnected instead of queueing them behind a reconnect.
            redisOptions.BacklogPolicy = BacklogPolicy.FailFast;
            return ConnectionMultiplexer.Connect(redisOptions);
        });
        services.AddSingleton<IPromptEmbedder, RoutedPromptEmbedder>();
        services.AddSingleton<CompletionCache>();
    }
}
