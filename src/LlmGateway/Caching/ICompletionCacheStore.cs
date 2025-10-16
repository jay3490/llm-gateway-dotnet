namespace LlmGateway.Caching;

/// <summary>A cached chat completion.</summary>
/// <param name="Key">Exact-match key: a hash of the canonical request.</param>
/// <param name="Partition">Requests are only compared semantically within the same partition (alias, parameters, system prompt).</param>
/// <param name="Embedding">Embedding of the conversation, or null when only exact matching applies.</param>
/// <param name="ResponseJson">The upstream response exactly as it was returned.</param>
public sealed record CacheEntry(string Key, string Partition, float[]? Embedding, string ResponseJson, DateTimeOffset ExpiresAt);

/// <summary>Storage for cached completions.</summary>
public interface ICompletionCacheStore
{
    Task<CacheEntry?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(CacheEntry entry, CancellationToken cancellationToken);
}
