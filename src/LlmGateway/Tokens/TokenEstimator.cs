using LlmGateway.Contracts;

namespace LlmGateway.Tokens;

/// <summary>
/// Approximate token counts for budgeting before an upstream has reported real usage. It uses the
/// common rule of thumb of about four characters per token for English text plus the per-message
/// framing overhead of chat models. Real usage from the upstream always replaces the estimate.
/// </summary>
public static class TokenEstimator
{
    private const int CharactersPerToken = 4;
    private const int TokensPerMessage = 4;
    private const int TokensPerReply = 3;

    public static int Estimate(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.Length + CharactersPerToken - 1) / CharactersPerToken;

    /// <summary>Estimate for a text of <paramref name="characters"/> characters whose content is no longer at hand.</summary>
    public static int EstimateFromLength(int characters) =>
        characters <= 0 ? 0 : (characters + CharactersPerToken - 1) / CharactersPerToken;

    public static int EstimatePrompt(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Sum(m => TokensPerMessage + Estimate(m.GetText())) + TokensPerReply;
    }
}
