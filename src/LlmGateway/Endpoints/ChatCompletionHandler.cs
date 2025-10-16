using System.Diagnostics;
using LlmGateway.Caching;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Errors;
using LlmGateway.RateLimiting;
using LlmGateway.Routing;
using LlmGateway.Security;
using LlmGateway.Tokens;
using LlmGateway.Usage;

namespace LlmGateway.Endpoints;

/// <summary>Serves <c>POST /v1/chat/completions</c>.</summary>
public sealed class ChatCompletionHandler(
    ModelCatalog catalog,
    ModelRouter router,
    KeyRateLimiter rateLimiter,
    SseRelay relay,
    UsageRecorder usage,
    CompletionCache cache,
    TimeProvider timeProvider)
{
    private const string Endpoint = "chat.completions";

    public async Task<IResult> HandleAsync(HttpContext httpContext, ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();

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
            var (target, streamed) = await StreamAsync(httpContext, request, route, cancellationToken);
            var streamUsage = streamed.Usage
                ?? EstimatedUsage(promptEstimate, TokenEstimator.EstimateFromLength(streamed.CompletionCharacters));
            lease.Settle(streamUsage.TotalTokens);
            Record(apiKey, route, target, streamUsage, estimated: streamed.Usage is null, streamed: true, CacheOutcome.None, started);
            return Results.Empty;
        }

        var lookup = await LookupCacheAsync(httpContext, route, request, cancellationToken);
        if (lookup is { IsHit: true, Response: { } cached })
        {
            // A cache hit costs no upstream tokens, so the whole reservation goes back.
            lease.Settle(0);
            Record(apiKey, route, target: null, new TokenUsage(), estimated: false, streamed: false, lookup.Outcome, started);
            return Results.Json(cached, GatewayJson.Options);
        }

        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.CompleteChatAsync(request, upstreamModel, token),
            cancellationToken);

        var completionUsage = routed.Value.Usage ?? EstimatedUsage(promptEstimate, EstimateCompletion(routed.Value));
        lease.Settle(completionUsage.TotalTokens);
        Record(apiKey, route, routed.Target, completionUsage, estimated: routed.Value.Usage is null, streamed: false, lookup.Outcome, started);
        await cache.StoreAsync(lookup, routed.Value, cancellationToken);

        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        return Results.Json(routed.Value, GatewayJson.Options);
    }

    private async Task<CacheLookup> LookupCacheAsync(HttpContext httpContext, ModelRoute route, ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        if (!cache.Enabled)
        {
            return CacheLookup.NotConsulted;
        }

        if (BypassRequested(httpContext.Request))
        {
            httpContext.Response.Headers[GatewayHeaders.Cache] = "bypass";
            return CacheLookup.NotConsulted;
        }

        var lookup = await cache.LookupAsync(route, request, cancellationToken);
        httpContext.Response.Headers[GatewayHeaders.Cache] = lookup.Outcome switch
        {
            CacheOutcome.ExactHit => "hit-exact",
            _ => "miss",
        };

        return lookup;
    }

    private static bool BypassRequested(HttpRequest request) =>
        string.Equals(request.Headers[GatewayHeaders.Cache], "bypass", StringComparison.OrdinalIgnoreCase)
        || request.Headers.CacheControl.Any(v => v is not null
            && (v.Contains("no-cache", StringComparison.OrdinalIgnoreCase) || v.Contains("no-store", StringComparison.OrdinalIgnoreCase)));

    private async Task<(RouteTarget Target, StreamResult Result)> StreamAsync(
        HttpContext httpContext,
        ChatCompletionRequest request,
        ModelRoute route,
        CancellationToken cancellationToken)
    {
        var routed = await router.ExecuteAsync(
            route,
            (provider, upstreamModel, token) => provider.StreamChatAsync(request, upstreamModel, token),
            cancellationToken);

        await using var stream = routed.Value;
        httpContext.Response.Headers[GatewayHeaders.Upstream] = routed.Target.Upstream;
        var result = await relay.RelayAsync(httpContext, stream, request.StreamOptions?.IncludeUsage == true, cancellationToken);
        return (routed.Target, result);
    }

    private void Record(
        ApiKeyDefinition apiKey,
        ModelRoute route,
        RouteTarget? target,
        TokenUsage tokens,
        bool estimated,
        bool streamed,
        CacheOutcome cacheOutcome,
        long started) =>
        usage.Record(new UsageRecord
        {
            TimestampUtc = timeProvider.GetUtcNow().UtcDateTime,
            ApiKeyName = apiKey.Name,
            Endpoint = Endpoint,
            ModelAlias = route.Alias,
            Upstream = target?.Upstream ?? string.Empty,
            UpstreamModel = target?.Model ?? string.Empty,
            PromptTokens = tokens.PromptTokens,
            CompletionTokens = tokens.CompletionTokens,
            TotalTokens = tokens.TotalTokens,
            Streamed = streamed,
            Estimated = estimated,
            LatencyMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            CacheResult = cacheOutcome switch
            {
                CacheOutcome.Miss => UsageCacheResults.Miss,
                CacheOutcome.ExactHit => UsageCacheResults.Exact,
                _ => UsageCacheResults.None,
            },
        });

    private static TokenUsage EstimatedUsage(int promptTokens, int completionTokens) => new()
    {
        PromptTokens = promptTokens,
        CompletionTokens = completionTokens,
        TotalTokens = promptTokens + completionTokens,
    };

    private static int EstimateCompletion(ChatCompletionResponse response) =>
        response.Choices.Sum(c => TokenEstimator.Estimate(c.Message?.GetText()));

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
