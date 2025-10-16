using LlmGateway.Caching;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Endpoints;
using LlmGateway.Errors;
using LlmGateway.Guardrails;
using LlmGateway.Providers;
using LlmGateway.RateLimiting;
using LlmGateway.Routing;
using LlmGateway.Security;
using LlmGateway.Usage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GatewayOptions>()
    .Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<GatewayOptions>, GatewayOptionsValidator>();

builder.Services.Configure<JsonOptions>(options => GatewayJson.Configure(options.SerializerOptions));
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<GatewayExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(TimeProvider.System);
// Polly's per-attempt timeout governs upstream calls, so the client itself never times out.
builder.Services.AddHttpClient(OpenAiCompatibleProvider.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<UpstreamRegistry>();
builder.Services.AddSingleton<ModelCatalog>();
builder.Services.AddSingleton(new RoutePlanner(Random.Shared));
builder.Services.AddSingleton<UpstreamResiliencePipelines>();
builder.Services.AddSingleton<ModelRouter>();
builder.Services.AddSingleton<SseRelay>();
builder.Services.AddSingleton<ApiKeyStore>();
builder.Services.AddSingleton<KeyRateLimiter>();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(GatewayPolicies.Admin, policy => policy.RequireRole(ApiKeyAuthenticationHandler.AdminRole));

builder.Services.AddDbContextFactory<UsageDbContext>((services, options) =>
    options.UseSqlite(services.GetRequiredService<IConfiguration>().GetConnectionString("Usage") ?? "Data Source=data/usage.db"));
builder.Services.AddSingleton<UsageRecorder>();
builder.Services.AddHostedService<UsageWriter>();
builder.Services.AddSingleton<UsageReportService>();

builder.Services.AddSingleton<BlockedTermPolicy>();
builder.Services.AddSingleton<ICompletionCacheStore, InMemoryCompletionCacheStore>();
builder.Services.AddSingleton<CompletionCache>();
builder.Services.AddScoped<ChatCompletionHandler>();
builder.Services.AddScoped<EmbeddingsHandler>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<RequestSizeLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live");
app.MapOpenAiApi();
app.MapAdminApi();

app.Run();

/// <summary>Entry point, exposed so integration tests can host the app in memory.</summary>
public partial class Program
{
}
