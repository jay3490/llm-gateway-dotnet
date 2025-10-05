using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.Routing;
using LlmGateway.Security;

namespace LlmGateway.Endpoints;

public static class GatewayEndpoints
{
    private const string OwnedBy = "llm-gateway";

    /// <summary>Maps the OpenAI-compatible <c>/v1</c> surface.</summary>
    public static RouteGroupBuilder MapOpenAiApi(this IEndpointRouteBuilder endpoints)
    {
        var v1 = endpoints.MapGroup("/v1").RequireAuthorization();

        v1.MapGet("/models", (HttpContext http, ModelCatalog catalog) =>
        {
            var key = http.GetApiKey();
            var visible = catalog.Routes.Where(r => key.CanUse(r.Alias)).Select(ToCard).ToList();
            return Results.Json(new ModelListResponse(visible), GatewayJson.Options);
        });

        v1.MapGet("/models/{id}", (string id, HttpContext http, ModelCatalog catalog) =>
            catalog.Routes.FirstOrDefault(r => string.Equals(r.Alias, id, StringComparison.OrdinalIgnoreCase)) is { } route
                && http.GetApiKey().CanUse(route.Alias)
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
