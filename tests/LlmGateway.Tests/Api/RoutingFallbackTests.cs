using System.Net;
using System.Text;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class RoutingFallbackTests : IDisposable
{
    private const string Primary = "primary.test";
    private const string Secondary = "secondary.test";

    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithUpstream("primary", Primary)
        .WithUpstream("secondary", Secondary)
        .WithTarget("smart", "primary", "gpt-4o")
        .WithFallback("smart", "secondary", "llama3.1");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Server_errors_are_retried_then_fall_back_to_the_next_target()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "2");
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.InternalServerError))
            .On(Secondary, FakeUpstreamHandler.ChatReply("from fallback"));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secondary", response.Headers.GetValues("X-Gateway-Upstream").Single());
        Assert.Equal(3, _factory.Upstreams.RequestsTo(Primary).Count);
        Assert.Equal("llama3.1", (string?)_factory.Upstreams.RequestsTo(Secondary).Single().Json["model"]);
    }

    [Fact]
    public async Task A_transient_error_that_clears_is_absorbed_by_retry()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "2");
        _factory.Upstreams.On(Primary, FakeUpstreamHandler.Sequence(
            FakeUpstreamHandler.Status(HttpStatusCode.BadGateway),
            FakeUpstreamHandler.ChatReply("second try")));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("primary", response.Headers.GetValues("X-Gateway-Upstream").Single());
        Assert.Equal(2, _factory.Upstreams.RequestsTo(Primary).Count);
        Assert.Empty(_factory.Upstreams.RequestsTo(Secondary));
    }

    [Fact]
    public async Task Rate_limited_upstream_is_not_retried_in_place()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "3");
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.TooManyRequests))
            .On(Secondary, FakeUpstreamHandler.ChatReply("from fallback"));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.Upstreams.RequestsTo(Primary));
        Assert.Single(_factory.Upstreams.RequestsTo(Secondary));
    }

    [Fact]
    public async Task Client_errors_do_not_fall_back()
    {
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.BadRequest, """{"error":{"message":"bad","type":"invalid_request_error"}}"""))
            .On(Secondary, FakeUpstreamHandler.ChatReply("unused"));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(_factory.Upstreams.RequestsTo(Primary));
        Assert.Empty(_factory.Upstreams.RequestsTo(Secondary));
    }

    [Fact]
    public async Task Timed_out_attempt_falls_back()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "0")
            .WithSetting("Gateway:Resilience:AttemptTimeout", "00:00:00.200");
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Hang())
            .On(Secondary, FakeUpstreamHandler.ChatReply("from fallback"));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secondary", response.Headers.GetValues("X-Gateway-Upstream").Single());
    }

    [Fact]
    public async Task Open_circuit_skips_the_failing_upstream()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "0")
            .WithSetting("Gateway:Resilience:CircuitBreakerMinimumThroughput", "2")
            .WithSetting("Gateway:Resilience:CircuitBreakerFailureRatio", "0.5");
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.ServiceUnavailable))
            .On(Secondary, FakeUpstreamHandler.ChatReply("from fallback"));

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendChatAsync()).StatusCode);
        }

        Assert.Equal(2, _factory.Upstreams.RequestsTo(Primary).Count);
        Assert.Equal(4, _factory.Upstreams.RequestsTo(Secondary).Count);
    }

    [Fact]
    public async Task All_targets_failing_returns_bad_gateway()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "0");
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.ServiceUnavailable))
            .On(Secondary, FakeUpstreamHandler.Status(HttpStatusCode.InternalServerError));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("upstream_unavailable", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task All_targets_rate_limited_returns_429()
    {
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.TooManyRequests))
            .On(Secondary, FakeUpstreamHandler.Status(HttpStatusCode.TooManyRequests));

        var response = await SendChatAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Streaming_falls_back_before_the_first_byte()
    {
        _factory.WithSetting("Gateway:Resilience:MaxRetryAttempts", "0");
        const string chunk = """{"id":"c","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"}}]}""";
        _factory.Upstreams
            .On(Primary, FakeUpstreamHandler.Status(HttpStatusCode.ServiceUnavailable))
            .On(Secondary, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SseReader.Format(chunk, "[DONE]"), Encoding.UTF8, "text/event-stream"),
            }));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hi", stream: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secondary", response.Headers.GetValues("X-Gateway-Upstream").Single());
        Assert.Equal([chunk, "[DONE]"], await SseReader.ReadEventsAsync(response));
    }

    private async Task<HttpResponseMessage> SendChatAsync()
    {
        using var client = _factory.CreateClient();
        return await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hello")));
    }
}
