using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.Routing;

namespace LlmGateway.Endpoints;

/// <summary>Serves <c>POST /v1/chat/completions</c>.</summary>
public sealed class ChatCompletionHandler(ModelCatalog catalog, ModelRouter router)
{
    public async Task<IResult> HandleAsync(HttpContext httpContext, ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        if (!catalog.TryGet(request.Model, ModelKind.Chat, out var route))
        {
            return GatewayErrors.ModelNotFound(request.Model);
        }

        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.CompleteChatAsync(request, upstreamModel, token),
            cancellationToken);

        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        return Results.Json(routed.Value, GatewayJson.Options);
    }

    private static IResult? Validate(ChatCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return GatewayErrors.InvalidRequest("'model' is required.", "model");
        }

        if (request.Messages.Count == 0)
        {
            return GatewayErrors.InvalidRequest("'messages' must contain at least one message.", "messages");
        }

        return null;
    }
}
