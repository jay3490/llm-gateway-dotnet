using System.Net;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class RateLimitTests : IDisposable
{
    private const string Key = "sk-test-metered";

    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithApiKey("metered", Key)
        .WithUpstream("primary", "primary.test")
        .WithTarget("smart", "primary", "gpt-4o-mini");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Requests_over_the_per_minute_budget_get_429_with_retry_after()
    {
        _factory.WithSetting("Gateway:ApiKeys:metered:RequestsPerMinute", "2");
        using var client = _factory.CreateClientWithKey(Key);

        var first = await SendAsync(client, "chat-default");
        var second = await SendAsync(client, "chat-default");
        var third = await SendAsync(client, "chat-default");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("1", first.Headers.GetValues("x-ratelimit-remaining-requests").Single());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), third.Headers.RetryAfter!.Delta);
        Assert.Equal("rate_limit_exceeded", (string?)(await third.ReadObjectAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task Real_token_usage_reported_by_the_upstream_is_charged()
    {
        _factory.WithSetting("Gateway:ApiKeys:metered:TokensPerMinute", "1000");
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("long answer", promptTokens: 400, completionTokens: 800));
        using var client = _factory.CreateClientWithKey(Key);

        var first = await SendAsync(client, "smart");
        var second = await SendAsync(client, "smart");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Contains("tokens per minute", (string?)(await second.ReadObjectAsync())["error"]!["message"], StringComparison.Ordinal);
        Assert.Single(_factory.Upstreams.RequestsTo("primary.test"));
    }

    [Fact]
    public async Task Failed_upstream_calls_do_not_consume_the_token_budget()
    {
        _factory.WithSetting("Gateway:ApiKeys:metered:TokensPerMinute", "300")
            .WithSetting("Gateway:Resilience:MaxRetryAttempts", "0");
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.Status(HttpStatusCode.ServiceUnavailable));
        using var client = _factory.CreateClientWithKey(Key);
        var request = TestRequests.Chat("smart", "hi");
        request["max_tokens"] = 250;

        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(request));
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string model) =>
        client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(TestRequests.Chat(model, "hello")));
}
