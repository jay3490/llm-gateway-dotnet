using System.Collections.Concurrent;
using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Caching;

/// <summary>
/// Process-local cache store. When it grows past <see cref="CacheOptions.MaxEntries"/> it drops
/// expired entries first and then those closest to expiry.
/// </summary>
public sealed class InMemoryCompletionCacheStore(IOptions<GatewayOptions> options, TimeProvider timeProvider) : ICompletionCacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _evictionGate = new();

    public int Count => _entries.Count;

    public Task<CacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (!IsExpired(entry))
            {
                return Task.FromResult<CacheEntry?>(entry);
            }

            _entries.TryRemove(key, out _);
        }

        return Task.FromResult<CacheEntry?>(null);
    }

    /// <summary>A linear scan over the partition: fine for thousands of entries, not for millions.</summary>
    public Task<SemanticMatch?> FindNearestAsync(string partition, float[] embedding, CancellationToken cancellationToken)
    {
        SemanticMatch? best = null;

        foreach (var entry in _entries.Values)
        {
            if (entry.Embedding is null || entry.Partition != partition || IsExpired(entry))
            {
                continue;
            }

            var similarity = VectorMath.CosineSimilarity(embedding, entry.Embedding);
            if (best is null || similarity > best.Similarity)
            {
                best = new SemanticMatch(entry, similarity);
            }
        }

        return Task.FromResult(best);
    }

    public Task SetAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries[entry.Key] = entry;

        var maxEntries = options.Value.Cache.MaxEntries;
        if (_entries.Count > maxEntries)
        {
            Evict(maxEntries);
        }

        return Task.CompletedTask;
    }

    private bool IsExpired(CacheEntry entry) => entry.ExpiresAt <= timeProvider.GetUtcNow();

    private void Evict(int maxEntries)
    {
        lock (_evictionGate)
        {
            foreach (var expired in _entries.Values.Where(IsExpired).ToList())
            {
                _entries.TryRemove(expired.Key, out _);
            }

            var excess = _entries.Count - maxEntries;
            if (excess <= 0)
            {
                return;
            }

            foreach (var oldest in _entries.Values.OrderBy(e => e.ExpiresAt).Take(excess).ToList())
            {
                _entries.TryRemove(oldest.Key, out _);
            }
        }
    }
}
