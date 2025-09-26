using System.Security.Cryptography;
using System.Text;
using LlmGateway.Contracts;
using LlmGateway.Tokens;

namespace LlmGateway.Providers;

/// <summary>
/// An in-process provider with deterministic output. It lets the gateway run with no network access
/// and no API keys, and gives tests stable responses. Chat replies echo the last user message;
/// embeddings come from <see cref="HashingEmbedder"/>.
/// </summary>
public sealed class FakeLlmProvider(TimeProvider timeProvider) : ILlmProvider
{
    public Task<ChatCompletionResponse> CompleteChatAsync(ChatCompletionRequest request, string upstreamModel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var reply = BuildReply(request);
        var promptTokens = TokenEstimator.EstimatePrompt(request.Messages);
        var completionTokens = TokenEstimator.Estimate(reply);

        var response = new ChatCompletionResponse
        {
            Id = "chatcmpl-" + ShortHash(upstreamModel + "\n" + reply),
            Created = timeProvider.GetUtcNow().ToUnixTimeSeconds(),
            Model = upstreamModel,
            Choices =
            [
                new ChatChoice { Index = 0, Message = ChatMessage.FromText("assistant", reply), FinishReason = "stop" },
            ],
            Usage = new TokenUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens,
            },
        };

        return Task.FromResult(response);
    }

    public Task<EmbeddingResponse> CreateEmbeddingsAsync(EmbeddingRequest request, string upstreamModel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = request.GetTextInputs();
        var tokens = inputs.Sum(TokenEstimator.Estimate);

        var response = new EmbeddingResponse
        {
            Model = upstreamModel,
            Data = inputs.Select((text, index) => EmbeddingData.FromVector(index, HashingEmbedder.Embed(text))).ToList(),
            Usage = new EmbeddingUsage { PromptTokens = tokens, TotalTokens = tokens },
        };

        return Task.FromResult(response);
    }

    /// <summary>The reply text the fake provider produces for <paramref name="request"/>.</summary>
    public static string BuildReply(ChatCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lastUser = request.Messages.LastOrDefault(m => m.Role == "user")?.GetText() ?? string.Empty;
        return $"Offline reply. You said: {lastUser}";
    }

    private static string ShortHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
}
