using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmGateway.Contracts;

/// <summary>Body of <c>POST /v1/embeddings</c>. <see cref="Input"/> is a string or an array of strings.</summary>
public sealed class EmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public JsonNode? Input { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public static EmbeddingRequest ForText(string model, string text) =>
        new() { Model = model, Input = JsonValue.Create(text) };

    /// <summary>The text inputs, or an empty list when the input is missing or not text (for example pre-tokenized arrays).</summary>
    public IReadOnlyList<string> GetTextInputs()
    {
        switch (Input)
        {
            case JsonValue value when value.TryGetValue(out string? text):
                return [text];
            case JsonArray items:
                var texts = new List<string>(items.Count);
                foreach (var item in items)
                {
                    if (item is not JsonValue v || !v.TryGetValue(out string? s))
                    {
                        return [];
                    }

                    texts.Add(s);
                }

                return texts;
            default:
                return [];
        }
    }
}

public sealed class EmbeddingResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; set; } = "list";

    [JsonPropertyName("data")]
    public IList<EmbeddingData> Data { get; set; } = [];

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("usage")]
    public EmbeddingUsage? Usage { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class EmbeddingData
{
    [JsonPropertyName("object")]
    public string ObjectType { get; set; } = "embedding";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>A float array, or a base64 string when the caller asked for <c>encoding_format: base64</c>.</summary>
    [JsonPropertyName("embedding")]
    public JsonElement Embedding { get; set; }

    public static EmbeddingData FromVector(int index, float[] vector) =>
        new() { Index = index, Embedding = JsonSerializer.SerializeToElement(vector) };

    /// <summary>Decodes the embedding whichever encoding the upstream used.</summary>
    public float[] ToVector() => Embedding.ValueKind switch
    {
        JsonValueKind.Array => Embedding.Deserialize<float[]>() ?? [],
        JsonValueKind.String => MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(Embedding.GetString()!)).ToArray(),
        _ => [],
    };
}

public sealed class EmbeddingUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
