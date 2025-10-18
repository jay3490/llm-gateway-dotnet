using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmGateway.Caching;

/// <summary>Serialises cache entries for Redis, storing embeddings as base64 float32 bytes to keep them compact.</summary>
public static class RedisCacheEntryCodec
{
    public static string Encode(CacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var stored = new StoredEntry(
            entry.Key,
            entry.Partition,
            entry.Embedding is null ? null : Convert.ToBase64String(MemoryMarshal.AsBytes(entry.Embedding.AsSpan())),
            entry.ResponseJson,
            entry.ExpiresAt.ToUnixTimeMilliseconds());

        return JsonSerializer.Serialize(stored);
    }

    /// <summary>Decodes an entry, or returns null for a value this version cannot read.</summary>
    public static CacheEntry? Decode(string value)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredEntry>(value);
            if (stored is null || stored.Key is null || stored.Response is null)
            {
                return null;
            }

            var embedding = stored.Embedding is null
                ? null
                : MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(stored.Embedding)).ToArray();

            return new CacheEntry(
                stored.Key,
                stored.Partition ?? string.Empty,
                embedding,
                stored.Response,
                DateTimeOffset.FromUnixTimeMilliseconds(stored.ExpiresAt));
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return null;
        }
    }

    private sealed record StoredEntry(
        [property: JsonPropertyName("k")] string? Key,
        [property: JsonPropertyName("p")] string? Partition,
        [property: JsonPropertyName("e")] string? Embedding,
        [property: JsonPropertyName("r")] string? Response,
        [property: JsonPropertyName("x")] long ExpiresAt);
}
