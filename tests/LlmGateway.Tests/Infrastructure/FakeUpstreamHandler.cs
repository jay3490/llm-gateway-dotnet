using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace LlmGateway.Tests.Infrastructure;

public sealed record RecordedRequest(Uri Uri, string? Authorization, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonObject Json => JsonNode.Parse(Body)!.AsObject();
}

/// <summary>
/// Stands in for upstream LLM servers. Each fake upstream is identified by the host of its base URL
/// and answers with a scripted <see cref="Responder"/>; every request is recorded for assertions.
/// </summary>
public sealed class FakeUpstreamHandler : HttpMessageHandler
{
    public delegate Task<HttpResponseMessage> Responder(RecordedRequest request, CancellationToken cancellationToken);

    private readonly ConcurrentDictionary<string, Responder> _responders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Host, RecordedRequest Request)> _requests = new();

    public FakeUpstreamHandler On(string host, Responder responder)
    {
        _responders[host] = responder;
        return this;
    }

    public IReadOnlyList<RecordedRequest> RequestsTo(string host) =>
        _requests.Where(r => string.Equals(r.Host, host, StringComparison.OrdinalIgnoreCase)).Select(r => r.Request).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(request.RequestUri!, request.Headers.Authorization?.ToString(), headers, body);
        var host = request.RequestUri!.Host;
        _requests.Enqueue((host, recorded));

        return _responders.TryGetValue(host, out var responder)
            ? await responder(recorded, cancellationToken)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>Answers chat requests with <paramref name="content"/>, echoing the model the gateway sent.</summary>
    public static Responder ChatReply(string content, int promptTokens = 10, int completionTokens = 5) =>
        (request, _) =>
        {
            var body = new JsonObject
            {
                ["id"] = "chatcmpl-test",
                ["object"] = "chat.completion",
                ["created"] = 1_700_000_000,
                ["model"] = (string?)request.Json["model"],
                ["choices"] = new JsonArray(new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
                    ["finish_reason"] = "stop",
                }),
                ["usage"] = new JsonObject
                {
                    ["prompt_tokens"] = promptTokens,
                    ["completion_tokens"] = completionTokens,
                    ["total_tokens"] = promptTokens + completionTokens,
                },
            };

            return Task.FromResult(Json(HttpStatusCode.OK, body.ToJsonString()));
        };

    public static Responder Status(HttpStatusCode status, string body = """{"error":{"message":"scripted failure","type":"server_error"}}""") =>
        (_, _) => Task.FromResult(Json(status, body));

    /// <summary>Waits longer than any test timeout, honouring cancellation like a stalled server.</summary>
    public static Responder Hang() =>
        async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

    /// <summary>Uses each responder once in order, then keeps using the last one.</summary>
    public static Responder Sequence(params Responder[] responders)
    {
        var next = -1;
        return (request, cancellationToken) =>
        {
            var index = Math.Min(Interlocked.Increment(ref next), responders.Length - 1);
            return responders[index](request, cancellationToken);
        };
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
