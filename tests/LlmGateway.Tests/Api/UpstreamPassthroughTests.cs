using System.Net;
using System.Text.Json.Nodes;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class UpstreamPassthroughTests : IDisposable
{
    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithUpstream("primary", "primary.test", apiKey: "sk-upstream")
        .WithTarget("smart", "primary", "gpt-4o-mini")
        .WithTarget("vectors", "primary", "text-embedding-3-small", kind: "Embedding");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Chat_request_is_sent_to_the_upstream_with_its_model_name()
    {
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("from upstream"));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hello")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_factory.Upstreams.RequestsTo("primary.test"));
        Assert.Equal("gpt-4o-mini", (string?)sent.Json["model"]);
        Assert.Equal("Bearer sk-upstream", sent.Authorization);
        Assert.Equal("from upstream", (string?)(await response.ReadObjectAsync())["choices"]![0]!["message"]!["content"]);
    }

    [Fact]
    public async Task Upstream_client_errors_are_passed_through_verbatim()
    {
        const string upstreamError = """{"error":{"message":"Unsupported parameter: 'foo'","type":"invalid_request_error","param":"foo","code":null}}""";
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.Status(HttpStatusCode.BadRequest, upstreamError));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hello")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("foo", (string?)(await response.ReadObjectAsync())["error"]!["param"]);
    }

    [Fact]
    public async Task Embedding_request_is_forwarded()
    {
        _factory.Upstreams.On("primary.test", (_, _) => Task.FromResult(FakeUpstreamHandler.Json(
            HttpStatusCode.OK,
            """{"object":"list","data":[{"object":"embedding","index":0,"embedding":[0.1,0.2]}],"model":"text-embedding-3-small","usage":{"prompt_tokens":2,"total_tokens":2}}""")));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/embeddings", UriKind.Relative),
            TestRequests.Json(new JsonObject { ["model"] = "vectors", ["input"] = "hello" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text-embedding-3-small", (string?)_factory.Upstreams.RequestsTo("primary.test").Single().Json["model"]);
        Assert.Equal(2, (await response.ReadObjectAsync())["data"]![0]!["embedding"]!.AsArray().Count);
    }
}
