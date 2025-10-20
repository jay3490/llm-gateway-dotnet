using LlmGateway.Endpoints;
using LlmGateway.Guardrails;
using LlmGateway.Hosting;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddGatewayObservability();
builder.Services.AddGateway(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseMiddleware<RequestSizeLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGatewayHealthChecks();
app.MapOpenAiApi();
app.MapAdminApi();

app.Run();

/// <summary>Entry point, exposed so integration tests can host the app in memory.</summary>
public partial class Program
{
}
