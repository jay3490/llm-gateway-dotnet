using System.Text.Json;
using LlmGateway.Contracts;
using LlmGateway.Providers;
using Microsoft.AspNetCore.Http.Features;

namespace LlmGateway.Endpoints;

/// <summary>What a relayed stream reported, for token accounting.</summary>
/// <param name="Usage">The usage chunk the upstream sent, if any.</param>
/// <param name="CompletionCharacters">Characters of generated content, used to estimate tokens when no usage arrived.</param>
/// <param name="Completed">False when the upstream or the client broke the stream off.</param>
public sealed record StreamResult(TokenUsage? Usage, int CompletionCharacters, bool Completed);

/// <summary>
/// Relays an upstream chat stream to the client as server-sent events, flushing each event as it
/// arrives. The gateway always asks upstreams for a trailing usage chunk; that chunk is only
/// forwarded when the client asked for it with <c>stream_options.include_usage</c>.
/// </summary>
public sealed partial class SseRelay(ILogger<SseRelay> logger)
{
    private const string InterruptedEvent =
        """{"error":{"message":"The upstream stream was interrupted.","type":"upstream_error","code":"stream_interrupted"}}""";

    public async Task<StreamResult> RelayAsync(
        HttpContext httpContext,
        IChatChunkSource stream,
        bool clientWantsUsage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(stream);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        await response.StartAsync(cancellationToken);

        TokenUsage? usage = null;
        var completionCharacters = 0;

        try
        {
            await foreach (var payload in stream.ReadEventsAsync(cancellationToken))
            {
                var chunk = Inspect(payload);
                usage = chunk.Usage ?? usage;
                completionCharacters += chunk.ContentCharacters;

                if (chunk.IsUsageOnly && !clientWantsUsage)
                {
                    continue;
                }

                await WriteEventAsync(response, payload, cancellationToken);
            }

            await WriteEventAsync(response, "[DONE]", cancellationToken);
            return new StreamResult(usage, completionCharacters, Completed: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogClientDisconnected(logger);
            return new StreamResult(usage, completionCharacters, Completed: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            LogUpstreamInterrupted(logger, exception);
            await WriteEventAsync(response, InterruptedEvent, cancellationToken);
            return new StreamResult(usage, completionCharacters, Completed: false);
        }
    }

    private static async Task WriteEventAsync(HttpResponse response, string payload, CancellationToken cancellationToken)
    {
        await response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static ChunkSummary Inspect(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            TokenUsage? usage = null;
            if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
            {
                usage = usageElement.Deserialize<TokenUsage>(GatewayJson.Options);
            }

            var contentCharacters = 0;
            var choiceCount = 0;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    choiceCount++;
                    if (choice.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                    {
                        contentCharacters += content.GetString()!.Length;
                    }
                }
            }

            return new ChunkSummary(usage, contentCharacters, IsUsageOnly: usage is not null && choiceCount == 0);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private readonly record struct ChunkSummary(TokenUsage? Usage, int ContentCharacters, bool IsUsageOnly);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected during a streamed completion")]
    private static partial void LogClientDisconnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Upstream stream was interrupted after streaming had started")]
    private static partial void LogUpstreamInterrupted(ILogger logger, Exception exception);
}
