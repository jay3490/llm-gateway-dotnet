using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.RateLimiting;
using LlmGateway.Routing;
using LlmGateway.Security;
using LlmGateway.Tokens;

namespace LlmGateway.Endpoints;

/// <summary>Serves <c>POST /v1/chat/completions</c>.</summary>
public sealed class ChatCompletionHandler(
    ModelCatalog catalog,
    ModelRouter router,
    KeyRateLimiter rateLimiter,
    SseRelay relay)
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

        var apiKey = httpContext.GetApiKey();
        if (!apiKey.CanUse(route.Alias))
        {
            return GatewayErrors.ModelNotAllowed(route.Alias);
        }

        // Budget the prompt plus the most the caller allows the model to generate; settled to real usage afterwards.
        var promptEstimate = TokenEstimator.EstimatePrompt(request.Messages);
        using var lease = rateLimiter.TryAcquire(apiKey, promptEstimate + (request.MaxTokens ?? 0));
        lease.WriteHeaders(httpContext.Response);
        if (!lease.IsAcquired)
        {
            return GatewayErrors.RateLimited(apiKey.Name, lease);
        }

        if (request.IsStreaming)
        {
            var streamed = await StreamAsync(httpContext, request, route, cancellationToken);
            lease.Settle(streamed.Usage?.TotalTokens ?? promptEstimate + TokenEstimator.EstimateFromLength(streamed.CompletionCharacters));
            return Results.Empty;
        }

        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.CompleteChatAsync(request, upstreamModel, token),
            cancellationToken);

        lease.Settle(routed.Value.Usage?.TotalTokens ?? promptEstimate);
        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        return Results.Json(routed.Value, GatewayJson.Options);
    }

    private async Task<StreamResult> StreamAsync(HttpContext httpContext, ChatCompletionRequest request, ModelRoute route, CancellationToken cancellationToken)
    {
        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.StreamChatAsync(request, upstreamModel, token),
            cancellationToken);

        await using var stream = routed.Value;
        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        return await relay.RelayAsync(httpContext, stream, request.StreamOptions?.IncludeUsage == true, cancellationToken);
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
