using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmGateway.Tests.Infrastructure;

internal static class TestRequests
{
    public static JsonObject Chat(string model, string userMessage, bool stream = false)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = userMessage }),
        };

        if (stream)
        {
            body["stream"] = true;
        }

        return body;
    }

    public static StringContent Json(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public static async Task<JsonObject> ReadObjectAsync(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!;
}
