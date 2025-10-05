using System.Net;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class AuthenticationTests : IDisposable
{
    private const string LimitedKey = "sk-test-limited";

    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithApiKey("limited", LimitedKey, isAdmin: false, "chat-default");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Missing_key_is_rejected_with_openai_error()
    {
        using var client = _factory.CreateClientWithKey(null);

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", "hi")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("invalid_api_key", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task Unknown_key_is_rejected()
    {
        using var client = _factory.CreateClientWithKey("sk-not-configured");

        var response = await client.GetAsync(new Uri("/v1/models", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Incorrect API key provided.", (string?)(await response.ReadObjectAsync())["error"]!["message"]);
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("x-api-key")]
    public async Task Key_is_accepted_from_alternative_headers(string header)
    {
        using var client = _factory.CreateClientWithKey(null);
        client.DefaultRequestHeaders.Add(header, LimitedKey);

        var response = await client.GetAsync(new Uri("/v1/models", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Model_outside_the_allow_list_is_forbidden()
    {
        using var client = _factory.CreateClientWithKey(LimitedKey);

        var response = await client.PostAsync(
            new Uri("/v1/embeddings", UriKind.Relative),
            TestRequests.Json(new System.Text.Json.Nodes.JsonObject { ["model"] = "embed-default", ["input"] = "x" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("model_not_allowed", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task Model_list_only_shows_allowed_models()
    {
        using var client = _factory.CreateClientWithKey(LimitedKey);

        var body = await (await client.GetAsync(new Uri("/v1/models", UriKind.Relative))).ReadObjectAsync();

        Assert.Equal(["chat-default"], body["data"]!.AsArray().Select(m => (string?)m!["id"]));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/v1/models/embed-default", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Allowed_model_is_served()
    {
        using var client = _factory.CreateClientWithKey(LimitedKey);

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", "hi")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_needs_no_key()
    {
        using var client = _factory.CreateClientWithKey(null);

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
