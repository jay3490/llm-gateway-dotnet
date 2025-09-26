using LlmGateway.Contracts;

namespace LlmGateway.Providers;

/// <summary>
/// One upstream backend. Implementations send the request with <c>model</c> replaced by
/// <paramref name="upstreamModel"/> and throw <see cref="UpstreamException"/> for non-success responses.
/// </summary>
public interface ILlmProvider
{
    Task<ChatCompletionResponse> CompleteChatAsync(ChatCompletionRequest request, string upstreamModel, CancellationToken cancellationToken);

    Task<EmbeddingResponse> CreateEmbeddingsAsync(EmbeddingRequest request, string upstreamModel, CancellationToken cancellationToken);
}
