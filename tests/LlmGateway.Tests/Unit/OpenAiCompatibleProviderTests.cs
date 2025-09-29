using System.Net;
using System.Text.Json;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Providers;
using LlmGateway.Tests.Infrastructure;

namespace LlmGateway.Tests.Unit;

public sealed class OpenAiCompatibleProviderTests : IDisposable
{
    private readonly FakeUpstreamHandler _handler = new();

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task Sends_upstream_model_and_forwards_unknown_fields()
    {
        _handler.On("api.test", FakeUpstreamHandler.ChatReply("hi"));
        var provider = CreateProvider(apiKey: "sk-test", headers: new() { ["OpenAI-Organization"] = "org-1" });
        var request = JsonSerializer.Deserialize<ChatCompletionRequest>(
            """{"model":"alias","messages":[{"role":"user","content":"x"}],"temperature":0.1,"seed":7}""",
            GatewayJson.Options)!;

        var response = await provider.CompleteChatAsync(request, "gpt-4o-mini", CancellationToken.None);

        var sent = Assert.Single(_handler.RequestsTo("api.test"));
        Assert.Equal("http://api.test/v1/chat/completions", sent.Uri.ToString());
        Assert.Equal("Bearer sk-test", sent.Authorization);
        Assert.Equal("org-1", sent.Headers["OpenAI-Organization"]);
        Assert.Equal("gpt-4o-mini", (string?)sent.Json["model"]);
        Assert.Equal(7, (int)sent.Json["seed"]!);
        Assert.Equal("hi", response.Choices[0].Message!.GetText());
        Assert.Equal(15, response.Usage!.TotalTokens);
        Assert.Equal("alias", request.Model);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task Non_success_responses_throw_with_status_and_body(HttpStatusCode status, bool transient)
    {
        _handler.On("api.test", FakeUpstreamHandler.Status(status, """{"error":{"message":"nope"}}"""));
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            provider.CompleteChatAsync(new ChatCompletionRequest { Model = "m" }, "m", CancellationToken.None));

        Assert.Equal((int)status, exception.StatusCode);
        Assert.Equal(transient, exception.IsTransient);
        Assert.Contains("nope", exception.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_json_from_upstream_is_reported_as_bad_gateway()
    {
        _handler.On("api.test", (_, _) => Task.FromResult(FakeUpstreamHandler.Json(HttpStatusCode.OK, "<html>")));
        var provider = CreateProvider();

        var exception = await Assert.ThrowsAsync<UpstreamException>(() =>
            provider.CompleteChatAsync(new ChatCompletionRequest { Model = "m" }, "m", CancellationToken.None));

        Assert.Equal(502, exception.StatusCode);
        Assert.True(exception.IsTransient);
    }

    private OpenAiCompatibleProvider CreateProvider(string? apiKey = null, Dictionary<string, string>? headers = null)
    {
        var options = new UpstreamOptions
        {
            Kind = UpstreamKind.OpenAICompatible,
            BaseUrl = new Uri("http://api.test/v1"),
            ApiKey = apiKey,
            Headers = headers ?? [],
        };

        return new OpenAiCompatibleProvider("test", options, new SingleHandlerHttpClientFactory(_handler));
    }
}
