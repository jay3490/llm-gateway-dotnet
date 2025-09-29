using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Endpoints;
using LlmGateway.Errors;
using LlmGateway.Providers;
using LlmGateway.Routing;
using Microsoft.AspNetCore.Http.Json;
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
builder.Services.AddHttpClient(OpenAiCompatibleProvider.HttpClientName);
builder.Services.AddSingleton<UpstreamRegistry>();
builder.Services.AddSingleton<ModelCatalog>();
builder.Services.AddSingleton<ModelRouter>();
builder.Services.AddScoped<ChatCompletionHandler>();
builder.Services.AddScoped<EmbeddingsHandler>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

app.MapHealthChecks("/health/live");
app.MapOpenAiApi();

app.Run();

/// <summary>Entry point, exposed so integration tests can host the app in memory.</summary>
public partial class Program
{
}
