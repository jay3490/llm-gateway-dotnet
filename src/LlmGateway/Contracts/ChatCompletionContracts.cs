using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LlmGateway.Contracts;

/// <summary>
/// Body of <c>POST /v1/chat/completions</c> in the OpenAI wire format. Only the fields the gateway
/// acts on are typed; every other field is kept in <see cref="AdditionalProperties"/> and forwarded
/// to the upstream unchanged.
/// </summary>
public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public IList<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }

    [JsonPropertyName("stream_options")]
    public StreamOptions? StreamOptions { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }

    [JsonIgnore]
    public bool IsStreaming => Stream == true;
}

public sealed class StreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; set; }
}

/// <summary>
/// A chat message. <see cref="Content"/> is either a string or an array of content parts, so it is
/// kept as a JSON node; <see cref="GetText"/> and <see cref="RewriteText"/> work on both shapes.
/// </summary>
public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public JsonNode? Content { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }

    public static ChatMessage FromText(string role, string text) =>
        new() { Role = role, Content = JsonValue.Create(text) };

    /// <summary>The message text: the string content, or the text parts of a multi-part content array joined by newlines.</summary>
    public string GetText()
    {
        switch (Content)
        {
            case JsonValue value when value.TryGetValue(out string? text):
                return text;
            case JsonArray parts:
                var builder = new StringBuilder();
                foreach (var part in parts.OfType<JsonObject>())
                {
                    if (TryGetPartText(part, out var partText))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append('\n');
                        }

                        builder.Append(partText);
                    }
                }

                return builder.ToString();
            default:
                return string.Empty;
        }
    }

    /// <summary>Applies <paramref name="rewrite"/> to every piece of text in the message, leaving non-text parts untouched.</summary>
    public void RewriteText(Func<string, string> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);

        switch (Content)
        {
            case JsonValue value when value.TryGetValue(out string? text):
                Content = JsonValue.Create(rewrite(text));
                break;
            case JsonArray parts:
                foreach (var part in parts.OfType<JsonObject>())
                {
                    if (TryGetPartText(part, out var partText))
                    {
                        part["text"] = rewrite(partText);
                    }
                }

                break;
        }
    }

    private static bool TryGetPartText(JsonObject part, out string text)
    {
        text = string.Empty;
        return part["type"] is JsonValue type && type.TryGetValue(out string? kind) && kind == "text"
            && part["text"] is JsonValue value && value.TryGetValue(out text!);
    }
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectType { get; set; } = "chat.completion";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public IList<ChatChoice> Choices { get; set; } = [];

    [JsonPropertyName("usage")]
    public TokenUsage? Usage { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class ChatChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class TokenUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
}
