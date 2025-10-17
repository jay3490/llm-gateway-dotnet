using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Routing;
using Microsoft.Extensions.Options;

namespace LlmGateway.Caching;

/// <summary>Turns prompt text into a vector for semantic cache lookups.</summary>
public interface IPromptEmbedder
{
    /// <summary>The embedding, or null when none could be produced; the cache then falls back to exact matching.</summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Embeds prompts through the gateway's own routing, using the alias named by
/// <see cref="CacheOptions.EmbeddingModel"/>, so cache embeddings get the same fallback and circuit
/// breaking as client traffic. Failures are logged and turn into cache misses.
/// </summary>
public sealed partial class RoutedPromptEmbedder(
    ModelCatalog catalog,
    ModelRouter router,
    IOptions<GatewayOptions> options,
    ILogger<RoutedPromptEmbedder> logger) : IPromptEmbedder
{
    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var alias = options.Value.Cache.EmbeddingModel;
        if (!catalog.TryGet(alias, ModelKind.Embedding, out var route))
        {
            return null;
        }

        try
        {
            var request = EmbeddingRequest.ForText(alias, text);
            var routed = await router.ExecuteAsync(
                route,
                (provider, upstreamModel, token) => provider.CreateEmbeddingsAsync(request, upstreamModel, token),
                cancellationToken);

            return routed.Value.Data.Count > 0 ? routed.Value.Data[0].ToVector() : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogEmbeddingFailed(logger, exception, alias);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not embed a prompt with {Alias}; semantic cache lookup skipped")]
    private static partial void LogEmbeddingFailed(ILogger logger, Exception exception, string alias);
}
