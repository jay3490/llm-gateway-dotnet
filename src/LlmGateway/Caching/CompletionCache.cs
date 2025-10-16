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
}

/// <summary>The result of a cache lookup; a miss carries what is needed to store the response afterwards.</summary>
public sealed record CacheLookup(CacheOutcome Outcome, ChatCompletionResponse? Response, string Key)
{
    public bool IsHit => Outcome is CacheOutcome.ExactHit;

    public static CacheLookup NotConsulted { get; } = new(CacheOutcome.None, null, string.Empty);
}

/// <summary>Serves repeated chat requests from the cache before they reach an upstream.</summary>
public sealed class CompletionCache(ICompletionCacheStore store, IOptions<GatewayOptions> options, TimeProvider timeProvider)
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
            return new CacheLookup(CacheOutcome.ExactHit, response, key);
        }

        return new CacheLookup(CacheOutcome.Miss, null, key);
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
            Partition: string.Empty,
            Embedding: null,
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
