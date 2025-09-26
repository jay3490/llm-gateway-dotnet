using System.Text.Json.Serialization;

namespace LlmGateway.Contracts;

/// <summary>The OpenAI error envelope: <c>{"error": {"message", "type", "param", "code"}}</c>.</summary>
public sealed record ErrorResponse([property: JsonPropertyName("error")] ErrorBody Error);

public sealed record ErrorBody(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("param")] string? Param = null,
    [property: JsonPropertyName("code")] string? Code = null);
