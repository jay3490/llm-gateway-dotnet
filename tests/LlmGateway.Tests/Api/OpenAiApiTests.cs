using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class OpenAiApiTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;

    public OpenAiApiTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Models_lists_configured_aliases()
    {
        var response = await _client.GetAsync(new Uri("/v1/models", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadObjectAsync();
        Assert.Equal("list", (string?)body["object"]);
        var ids = body["data"]!.AsArray().Select(m => (string?)m!["id"]).ToList();
        Assert.Contains("chat-default", ids);
        Assert.Contains("embed-default", ids);
    }

    [Fact]
    public async Task Chat_completion_returns_openai_shaped_response()
    {
        var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", "hello there")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("offline", response.Headers.GetValues("X-Gateway-Upstream").Single());

        var body = await response.ReadObjectAsync();
        Assert.Equal("chat.completion", (string?)body["object"]);
        Assert.Equal("offline-chat", (string?)body["model"]);
        Assert.Equal("Offline reply. You said: hello there", (string?)body["choices"]![0]!["message"]!["content"]);
        Assert.True((int)body["usage"]!["total_tokens"]! > 0);
    }

    [Fact]
    public async Task Embeddings_return_one_vector_per_input()
    {
        var request = new JsonObject
        {
            ["model"] = "embed-default",
            ["input"] = new JsonArray("first text", "second text"),
        };

        var response = await _client.PostAsync(new Uri("/v1/embeddings", UriKind.Relative), TestRequests.Json(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await response.ReadObjectAsync())["data"]!.AsArray();
        Assert.Equal(2, data.Count);
        Assert.Equal(256, data[0]!["embedding"]!.AsArray().Count);
    }

    [Fact]
    public async Task Unknown_model_returns_404_in_openai_error_format()
    {
        var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("no-such-model", "hi")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = (await response.ReadObjectAsync())["error"]!;
        Assert.Equal("model_not_found", (string?)error["code"]);
    }

    [Fact]
    public async Task Embedding_alias_cannot_be_used_for_chat()
    {
        var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("embed-default", "hi")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_json_returns_400_in_openai_error_format()
    {
        using var content = new StringContent("{\"model\": ", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await response.ReadObjectAsync())["error"]!;
        Assert.Equal("invalid_request_error", (string?)error["type"]);
    }

    [Fact]
    public async Task Missing_messages_returns_400()
    {
        var response = await _client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(new JsonObject { ["model"] = "chat-default", ["messages"] = new JsonArray() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("messages", (string?)(await response.ReadObjectAsync())["error"]!["param"]);
    }

    [Fact]
    public async Task Unknown_request_fields_are_accepted()
    {
        var request = TestRequests.Chat("chat-default", "hi");
        request["temperature"] = 0.2;
        request["response_format"] = new JsonObject { ["type"] = "text" };

        var response = await _client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(request));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
