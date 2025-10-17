using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Contracts;

namespace LlmGateway.Caching;

/// <summary>Derives cache keys from requests in a way that ignores JSON key order and transport-only fields.</summary>
public static class CacheKeys
{
    /// <summary>Fields that do not change what the model generates, so they must not split the cache.</summary>
    private static readonly string[] TransportFields = ["stream", "stream_options", "user", "metadata", "store"];

    /// <summary>Key for an exact match: alias plus the whole canonical request.</summary>
    public static string Exact(string alias, ChatCompletionRequest request)
    {
        var canonical = Canonical(alias, request);
        return Hash(canonical.ToJsonString());
    }

    /// <summary>
    /// Semantic comparisons only happen within a partition: the same alias, the same parameters and
    /// the same system prompt. Only the rest of the conversation is compared by meaning.
    /// </summary>
    public static string Partition(string alias, ChatCompletionRequest request)
    {
        var canonical = Canonical(alias, request);
        canonical.Remove("messages");
        canonical["system"] = string.Join("\n", SystemMessages(request).Select(m => m.GetText()));
        return Hash(canonical.ToJsonString());
    }

    /// <summary>The non-system conversation as <c>role: text</c> lines, which is what gets embedded.</summary>
    public static string SemanticText(ChatCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Join("\n", request.Messages
            .Where(m => !IsSystem(m))
            .Select(m => $"{m.Role}: {m.GetText()}"));
    }

    private static IEnumerable<ChatMessage> SystemMessages(ChatCompletionRequest request) => request.Messages.Where(IsSystem);

    private static bool IsSystem(ChatMessage message) => message.Role is "system" or "developer";

    internal static JsonObject Canonical(string alias, ChatCompletionRequest request)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(request);

        var node = JsonSerializer.SerializeToNode(request, GatewayJson.Options)!.AsObject();
        node["model"] = alias.ToLowerInvariant();
        foreach (var field in TransportFields)
        {
            node.Remove(field);
        }

        return (JsonObject)Sorted(node)!;
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>A copy of <paramref name="node"/> with object properties in ordinal order at every level.</summary>
    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };
}
