using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Api;

public sealed class UsageReportTests : IDisposable
{
    private const string TeamKey = "sk-test-team";

    private readonly GatewayFactory _factory = new GatewayFactory()
        .WithApiKey("team", TeamKey)
        .WithUpstream("primary", "primary.test")
        .WithTarget("smart", "primary", "gpt-4o-mini");

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Report_aggregates_reported_tokens_by_key_and_model()
    {
        _factory.Upstreams.On("primary.test", FakeUpstreamHandler.ChatReply("answer", promptTokens: 12, completionTokens: 30));
        using var team = _factory.CreateClientWithKey(TeamKey);
        using var admin = _factory.CreateClient();

        await PostChatAsync(team, "smart");
        await PostChatAsync(team, "smart");
        await PostChatAsync(admin, "smart");

        var report = await WaitForReportAsync(admin, expectedRequests: 3);

        Assert.Equal(3, (int)report["totals"]!["requests"]!);
        Assert.Equal(36, (long)report["totals"]!["prompt_tokens"]!);
        Assert.Equal(90, (long)report["totals"]!["completion_tokens"]!);

        var byKey = report["by_api_key"]!.AsArray();
        Assert.Equal("team", (string?)byKey[0]!["name"]);
        Assert.Equal(84, (long)byKey[0]!["usage"]!["total_tokens"]!);
        Assert.Equal("test-admin", (string?)byKey[1]!["name"]);
        Assert.Equal("smart", (string?)report["by_model"]![0]!["name"]);
    }

    [Fact]
    public async Task Report_can_be_filtered_to_one_key()
    {
        using var team = _factory.CreateClientWithKey(TeamKey);
        using var admin = _factory.CreateClient();
        await PostChatAsync(team, "chat-default");
        await PostChatAsync(admin, "chat-default");
        await WaitForReportAsync(admin, expectedRequests: 2);

        var report = await GetReportAsync(admin, "?api_key=team");

        Assert.Equal(1, (int)report["totals"]!["requests"]!);
        Assert.Equal("team", (string?)Assert.Single(report["by_api_key"]!.AsArray())!["name"]);
    }

    [Fact]
    public async Task Streamed_completions_record_the_usage_chunk()
    {
        const string chunk = """{"id":"c","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"ok"}}]}""";
        const string usage = """{"id":"c","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":7,"completion_tokens":3,"total_tokens":10}}""";
        _factory.Upstreams.On("primary.test", (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SseReader.Format(chunk, usage, "[DONE]"), Encoding.UTF8, "text/event-stream"),
        }));
        using var admin = _factory.CreateClient();

        var response = await admin.PostAsync(
            new Uri("/v1/chat/completions", UriKind.Relative),
            TestRequests.Json(TestRequests.Chat("smart", "hi", stream: true)));
        await response.Content.ReadAsStringAsync();

        var report = await WaitForReportAsync(admin, expectedRequests: 1);
        Assert.Equal(10, (long)report["totals"]!["total_tokens"]!);
    }

    [Fact]
    public async Task Non_admin_keys_cannot_read_usage()
    {
        using var team = _factory.CreateClientWithKey(TeamKey);

        var response = await team.GetAsync(new Uri("/admin/usage", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_denied", (string?)(await response.ReadObjectAsync())["error"]!["code"]);
    }

    [Fact]
    public async Task Inverted_window_is_rejected()
    {
        using var admin = _factory.CreateClient();

        var response = await admin.GetAsync(new Uri("/admin/usage?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task PostChatAsync(HttpClient client, string model)
    {
        var response = await client.PostAsync(new Uri("/v1/chat/completions", UriKind.Relative), TestRequests.Json(TestRequests.Chat(model, "hello")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonObject> GetReportAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(new Uri("/admin/usage" + query, UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadObjectAsync();
    }

    /// <summary>Usage is written in the background, so poll until the expected rows have landed.</summary>
    private static async Task<JsonObject> WaitForReportAsync(HttpClient client, int expectedRequests)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var report = await GetReportAsync(client);
            if ((int)report["totals"]!["requests"]! >= expectedRequests || DateTime.UtcNow > deadline)
            {
                return report;
            }

            await Task.Delay(50);
        }
    }
}
