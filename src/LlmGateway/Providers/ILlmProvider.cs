using LlmGateway.Contracts;

namespace LlmGateway.Providers;

/// <summary>
/// One upstream backend. Implementations send the request with <c>model</c> replaced by
/// <paramref name="upstreamModel"/> and throw <see cref="UpstreamException"/> for non-success responses.
/// </summary>
public interface ILlmProvider
{
    Task<ChatCompletionResponse> CompleteChatAsync(ChatCompletionRequest request, string upstreamModel, CancellationToken cancellationToken);

    /// <summary>
    /// Starts a streaming completion and returns once the upstream has accepted it. The upstream is
    /// always asked to include a final usage chunk so the gateway can account for tokens.
    /// </summary>
    Task<IChatChunkSource> StreamChatAsync(ChatCompletionRequest request, string upstreamModel, CancellationToken cancellationToken);

    Task<EmbeddingResponse> CreateEmbeddingsAsync(EmbeddingRequest request, string upstreamModel, CancellationToken cancellationToken);
}
