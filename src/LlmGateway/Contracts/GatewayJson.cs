using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmGateway.Contracts;

/// <summary>Serializer settings shared by the HTTP pipeline and the upstream clients.</summary>
public static class GatewayJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }
}
