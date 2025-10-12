namespace LlmGateway.Usage;

/// <summary>One served request and the tokens it used.</summary>
public sealed class UsageRecord
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string ApiKeyName { get; set; } = string.Empty;

    /// <summary><c>chat.completions</c> or <c>embeddings</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string ModelAlias { get; set; } = string.Empty;

    public string Upstream { get; set; } = string.Empty;

    public string UpstreamModel { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    public bool Streamed { get; set; }

    /// <summary>True when the token counts are the gateway's estimate because the upstream reported none.</summary>
    public bool Estimated { get; set; }

    public int LatencyMs { get; set; }
}
