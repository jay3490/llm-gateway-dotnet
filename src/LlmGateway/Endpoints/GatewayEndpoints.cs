using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.Routing;

namespace LlmGateway.Endpoints;

public static class GatewayEndpoints
{
    private const string OwnedBy = "llm-gateway";

    /// <summary>Maps the OpenAI-compatible <c>/v1</c> surface.</summary>
    public static RouteGroupBuilder MapOpenAiApi(this IEndpointRouteBuilder endpoints)
    {
        var v1 = endpoints.MapGroup("/v1");

        v1.MapGet("/models", (ModelCatalog catalog) =>
            Results.Json(new ModelListResponse(catalog.Routes.Select(ToCard).ToList()), GatewayJson.Options));

        v1.MapGet("/models/{id}", (string id, ModelCatalog catalog) =>
            catalog.Routes.FirstOrDefault(r => string.Equals(r.Alias, id, StringComparison.OrdinalIgnoreCase)) is { } route
                ? Results.Json(ToCard(route), GatewayJson.Options)
                : GatewayErrors.ModelNotFound(id));

        v1.MapPost("/chat/completions", (HttpContext http, ChatCompletionRequest request, ChatCompletionHandler handler, CancellationToken ct) =>
            handler.HandleAsync(http, request, ct));

        v1.MapPost("/embeddings", (HttpContext http, EmbeddingRequest request, EmbeddingsHandler handler, CancellationToken ct) =>
            handler.HandleAsync(http, request, ct));

        return v1;
    }

    private static ModelCard ToCard(ModelRoute route) => new(route.Alias, 0, OwnedBy);
}
