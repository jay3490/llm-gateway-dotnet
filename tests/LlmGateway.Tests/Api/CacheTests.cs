using System.Net;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class CacheTests : IDisposable
{
    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithSetting("Gateway:Cache:Enabled", "true")
        .WithUpstream("primary", "primary.test")
        .WithTarget("smart", "primary", "gpt-4o-mini");

    public CacheTests()
    {
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("cached answer", promptTokens: 20, completionTokens: 10));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Identical_request_is_served_from_the_cache()
    {
        using var client = _factory.CreateClient();

        var first = await SendAsync(client, "What is a token bucket?");
        var second = await SendAsync(client, "What is a token bucket?");

        Assert.Equal("miss", first.Headers.GetValues("X-Gateway-Cache").Single());
        Assert.Equal("hit-exact", second.Headers.GetValues("X-Gateway-Cache").Single());
        Assert.False(second.Headers.Contains("X-Gateway-Upstream"));
        Assert.Equal(
            (string?)(await first.ReadObjectAsync())["choices"]![0]!["message"]!["content"],
            (string?)(await second.ReadObjectAsync())["choices"]![0]!["message"]!["content"]);
        Assert.Single(_factory.Upstreams.RequestsTo("primary.test"));
    }

    [Fact]
    public async Task Different_prompts_are_not_confused()
    {
        using var client = _factory.CreateClient();

        await SendAsync(client, "first question");
        var other = await SendAsync(client, "second question");

        Assert.Equal("miss", other.Headers.GetValues("X-Gateway-Cache").Single());
        Assert.Equal(2, _factory.Upstreams.RequestsTo("primary.test").Count);
    }

    [Theory]
    [InlineData("X-Gateway-Cache", "bypass")]
    [InlineData("Cache-Control", "no-cache")]
    public async Task Clients_can_bypass_the_cache(string header, string value)
    {
        using var client = _factory.CreateClient();
        await SendAsync(client, "same prompt");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/chat/completions", UriKind.Relative))
        {
            Content = TestRequests.Json(TestRequests.Chat("smart", "same prompt")),
        };
        request.Headers.TryAddWithoutValidation(header, value);
        var response = await client.SendAsync(request);

        Assert.Equal("bypass", response.Headers.GetValues("X-Gateway-Cache").Single());
        Assert.Equal(2, _factory.Upstreams.RequestsTo("primary.test").Count);
    }

    [Fact]
    public async Task Cache_hits_are_reported_with_no_upstream_tokens()
    {
        using var client = _factory.CreateClient();
        await SendAsync(client, "count me once");
        await SendAsync(client, "count me once");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        System.Text.Json.Nodes.JsonObject report;
        do
        {
            report = await (await client.GetAsync(new Uri("/admin/usage", UriKind.Relative))).ReadObjectAsync();
        }
        while ((int)report["totals"]!["requests"]! < 2 && DateTime.UtcNow < deadline);

        Assert.Equal(2, (int)report["totals"]!["requests"]!);
        Assert.Equal(1, (int)report["totals"]!["cache_hits"]!);
        Assert.Equal(30, (long)report["totals"]!["total_tokens"]!);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string prompt) =>
        client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(TestRequests.Chat("smart", prompt)));
}
