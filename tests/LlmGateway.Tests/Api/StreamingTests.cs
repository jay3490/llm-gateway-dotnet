using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Providers;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class StreamingTests : IDisposable
{
    private const string Chunk1 = """{"id":"c1","object":"chat.completion.chunk","model":"gpt-4o-mini","choices":[{"index":0,"delta":{"role":"assistant","content":"Hel"},"finish_reason":null}]}""";
    private const string Chunk2 = """{"id":"c1","object":"chat.completion.chunk","model":"gpt-4o-mini","choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":"stop"}]}""";
    private const string UsageChunk = """{"id":"c1","object":"chat.completion.chunk","model":"gpt-4o-mini","choices":[],"usage":{"prompt_tokens":9,"completion_tokens":2,"total_tokens":11}}""";

    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithUpstream("primary", "primary.test")
        .WithTarget("smart", "primary", "gpt-4o-mini");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Upstream_events_are_relayed_in_order_and_terminated_with_done()
    {
        _factory.Upstreams.On("primary.test", SseResponder(Chunk1, Chunk2, UsageChunk, "[DONE]"));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hi", stream: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        var events = await SseReader.ReadEventsAsync(response);
        Assert.Equal([Chunk1, Chunk2, "[DONE]"], events);
    }

    [Fact]
    public async Task Gateway_asks_the_upstream_for_usage_while_streaming()
    {
        _factory.Upstreams.On("primary.test", SseResponder(Chunk1, "[DONE]"));
        using var client = _factory.CreateClient();

        await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hi", stream: true)));

        var sent = _factory.Upstreams.RequestsTo("primary.test").Single().Json;
        Assert.True((bool)sent["stream"]!);
        Assert.True((bool)sent["stream_options"]!["include_usage"]!);
    }

    [Fact]
    public async Task Usage_chunk_is_forwarded_when_the_client_asked_for_it()
    {
        _factory.Upstreams.On("primary.test", SseResponder(Chunk1, Chunk2, UsageChunk, "[DONE]"));
        using var client = _factory.CreateClient();
        var request = TestRequests.Chat("smart", "hi", stream: true);
        request["stream_options"] = new JsonObject { ["include_usage"] = true };

        var response = await client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(request));

        var events = await SseReader.ReadEventsAsync(response);
        Assert.Equal([Chunk1, Chunk2, UsageChunk, "[DONE]"], events);
    }

    [Fact]
    public async Task Upstream_rejection_before_streaming_returns_a_normal_error()
    {
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.Status(HttpStatusCode.BadRequest, """{"error":{"message":"bad","type":"invalid_request_error"}}"""));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hi", stream: true)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Offline_provider_streams_its_reply_word_by_word()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", "stream this please", stream: true)));

        var events = await SseReader.ReadEventsAsync(response);
        Assert.Equal("[DONE]", events[^1]);
        Assert.True(events.Count > 4);
        Assert.Equal("Offline reply. You said: stream this please", SseReader.AssembleContent(events));
        Assert.DoesNotContain(events, e => e.Contains("\"usage\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sse_parser_joins_multi_line_data_and_skips_comments()
    {
        const string body = ": keep-alive\n\ndata: {\"a\":\ndata: 1}\n\nevent: ignored\ndata: second\n\ndata: [DONE]\n\ndata: after-done\n\n";
        using var upstreamResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
        };
        await using var stream = new SseChunkSource(upstreamResponse);

        var events = new List<string>();
        await foreach (var payload in stream.ReadEventsAsync(CancellationToken.None))
        {
            events.Add(payload);
        }

        Assert.Equal(["{\"a\":\n1}", "second"], events);
    }

    private static FakeUpstreamHandler.Responder SseResponder(params string[] payloads) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SseReader.Format(payloads), Encoding.UTF8, "text/event-stream"),
        });
}
