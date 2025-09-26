using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.Routing;

namespace LlmGateway.Endpoints;

/// <summary>Serves <c>POST /v1/embeddings</c>.</summary>
public sealed class EmbeddingsHandler(ModelCatalog catalog, ModelRouter router)
{
    public async Task<IResult> HandleAsync(HttpContext httpContext, EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return GatewayErrors.InvalidRequest("'model' is required.", "model");
        }

        if (request.Input is null)
        {
            return GatewayErrors.InvalidRequest("'input' is required.", "input");
        }

        if (!catalog.TryGet(request.Model, ModelKind.Embedding, out var route))
        {
            return GatewayErrors.ModelNotFound(request.Model);
        }

        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.CreateEmbeddingsAsync(request, upstreamModel, token),
            cancellationToken);

        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        return Results.Json(routed.Value, GatewayJson.Options);
    }
}
