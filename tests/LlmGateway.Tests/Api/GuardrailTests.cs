using System.Net;
using System.Text.Json.Nodes;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class GuardrailTests : IDisposable
{
    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithUpstream("primary", "primary.test")
        .WithTarget("smart", "primary", "gpt-4o-mini")
        .WithTarget("vectors", "primary", "text-embedding-3-small", kind: "Embedding")
        .WithSetting("Gateway:Guardrails:BlockedTerms:0", "project nightfall")
        .WithSetting("Gateway:Guardrails:MaxRequestBytes", "2048");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Pii_is_redacted_before_the_request_reaches_the_upstream()
    {
        _factory.WithSetting("Gateway:Guardrails:RedactPii", "true");
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("noted"));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "I am jane@example.com, card 4111 1111 1111 1111")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2", response.Headers.GetValues("X-Gateway-Pii-Redacted").Single());
        var forwarded = (string?)_factory.Upstreams.RequestsTo("primary.test").Single().Json["messages"]![0]!["content"];
        Assert.Equal("I am [REDACTED_EMAIL], card [REDACTED_CARD]", forwarded);
    }

    [Fact]
    public async Task Redaction_also_covers_embedding_inputs()
    {
        _factory.WithSetting("Gateway:Guardrails:RedactPii", "true");
        _factory.Upstreams.On("primary.test", (_, _) => Task.FromResult(FakeUpstreamHandler.Json(
            HttpStatusCode.OK,
            """{"object":"list","data":[],"model":"m","usage":{"prompt_tokens":1,"total_tokens":1}}""")));
        using var client = _factory.CreateClient();

        await client.PostAsync(
            new Uri("/v1/embeddings", UriKind.Relative),
            TestRequests.Json(new JsonObject { ["model"] = "vectors", ["input"] = new JsonArray("call 415-555-2671", "clean") }));

        var input = _factory.Upstreams.RequestsTo("primary.test").Single().Json["input"]!.AsArray();
        Assert.Equal("call [REDACTED_PHONE]", (string?)input[0]);
        Assert.Equal("clean", (string?)input[1]);
    }

    [Fact]
    public async Task Redaction_can_be_switched_off()
    {
        _factory.WithSetting("Gateway:Guardrails:RedactPii", "false");
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("noted"));
        using var client = _factory.CreateClient();

        await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "I am jane@example.com")));

        var forwarded = (string?)_factory.Upstreams.RequestsTo("primary.test").Single().Json["messages"]![0]!["content"];
        Assert.Equal("I am jane@example.com", forwarded);
    }

    [Fact]
    public async Task Blocked_terms_are_rejected_without_calling_the_upstream()
    {
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("unused"));
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "Tell me about PROJECT Nightfall.")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("content_policy_violation", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
        Assert.Empty(_factory.Upstreams.RequestsTo("primary.test"));
    }

    [Fact]
    public async Task Blocked_terms_only_match_whole_words()
    {
        _factory.WithSetting("Gateway:Guardrails:BlockedTerms:1", "secret");
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", "Ask the secretary")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_bodies_get_413()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("chat-default", new string('x', 4096))));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("request_too_large", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
    }
}
