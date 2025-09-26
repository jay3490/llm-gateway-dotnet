using System.Text.Json.Serialization;

namespace LlmGateway.Contracts;

public sealed record ModelListResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<ModelCard> Data)
{
    [JsonPropertyName("object")]
    public string ObjectType => "list";
}

public sealed record ModelCard(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("created")] long Created,
    [property: JsonPropertyName("owned_by")] string OwnedBy)
{
    [JsonPropertyName("object")]
    public string ObjectType => "model";
}
