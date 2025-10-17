using System.Text.Json;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Routing;
using Microsoft.Extensions.Options;

namespace LlmGateway.Caching;

public enum CacheOutcome
{
    /// <summary>The cache was not consulted: disabled, bypassed by the client, or a streaming request.</summary>
    None,
    Miss,
    ExactHit,
    SemanticHit,
}

/// <summary>The result of a cache lookup; a miss carries what is needed to store the response afterwards.</summary>
/// <param name="Similarity">Cosine similarity of a semantic hit; 1 for exact hits.</param>
public sealed record CacheLookup(
    CacheOutcome Outcome,
    ChatCompletionResponse? Response,
    string Key,
    string Partition,
    float[]? Embedding,
    double Similarity)
{
    public bool IsHit => Outcome is CacheOutcome.ExactHit or CacheOutcome.SemanticHit;

    public static CacheLookup NotConsulted { get; } = new(CacheOutcome.None, null, string.Empty, string.Empty, null, 0);
}

/// <summary>
/// Two-layer response cache for chat completions. The exact layer matches the canonical request
/// hash. The semantic layer embeds the conversation and accepts the nearest cached conversation in
/// the same partition (same alias, parameters and system prompt) when its cosine similarity reaches
/// <see cref="CacheOptions.SimilarityThreshold"/>.
/// </summary>
public sealed class CompletionCache(
    ICompletionCacheStore store,
    IPromptEmbedder embedder,
    IOptions<GatewayOptions> options,
    TimeProvider timeProvider)
{
    private CacheOptions Options => options.Value.Cache;

    public bool Enabled => Options.Enabled;

    public async Task<CacheLookup> LookupAsync(ModelRoute route, ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(request);

        var key = CacheKeys.Exact(route.Alias, request);
        if (await store.GetAsync(key, cancellationToken) is { } exact && TryDeserialize(exact.ResponseJson) is { } response)
        {
            return new CacheLookup(CacheOutcome.ExactHit, response, key, exact.Partition, null, 1);
        }

        if (!Options.SemanticEnabled)
        {
            return new CacheLookup(CacheOutcome.Miss, null, key, string.Empty, null, 0);
        }

        var partition = CacheKeys.Partition(route.Alias, request);
        var embedding = await embedder.EmbedAsync(CacheKeys.SemanticText(request), cancellationToken);
        if (embedding is not null
            && await store.FindNearestAsync(partition, embedding, cancellationToken) is { } nearest
            && nearest.Similarity >= Options.SimilarityThreshold
            && TryDeserialize(nearest.Entry.ResponseJson) is { } similar)
        {
            return new CacheLookup(CacheOutcome.SemanticHit, similar, key, partition, embedding, nearest.Similarity);
        }

        return new CacheLookup(CacheOutcome.Miss, null, key, partition, embedding, 0);
    }

    public async Task StoreAsync(CacheLookup miss, ChatCompletionResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(miss);
        ArgumentNullException.ThrowIfNull(response);

        if (miss.Outcome != CacheOutcome.Miss || response.Choices.Count == 0)
        {
            return;
        }

        var entry = new CacheEntry(
            miss.Key,
            miss.Partition,
            miss.Embedding,
            JsonSerializer.Serialize(response, GatewayJson.Options),
            timeProvider.GetUtcNow() + Options.Ttl);

        await store.SetAsync(entry, cancellationToken);
    }

    /// <summary>An entry that no longer deserialises (written by an older version, say) is treated as a miss.</summary>
    private static ChatCompletionResponse? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatCompletionResponse>(json, GatewayJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
