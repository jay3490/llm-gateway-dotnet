using System.Runtime.CompilerServices;
using System.Text;

namespace LlmGateway.Providers;

/// <summary>
/// Reads a <c>text/event-stream</c> HTTP response event by event. Multi-line <c>data</c> fields are
/// joined with newlines as the SSE specification requires; comments and other fields are ignored.
/// </summary>
public sealed class SseChunkSource(HttpResponseMessage response) : IChatChunkSource
{
    private const string DoneMarker = "[DONE]";

    public async IAsyncEnumerable<string> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(body, Encoding.UTF8);
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length == 0)
                {
                    continue;
                }

                var payload = data.ToString();
                data.Clear();

                if (payload == DoneMarker)
                {
                    yield break;
                }

                yield return payload;
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                var value = line[5..];
                data.Append(value.StartsWith(' ') ? value[1..] : value);
            }
        }

        if (data.Length > 0 && data.ToString() != DoneMarker)
        {
            yield return data.ToString();
        }
    }

    public ValueTask DisposeAsync()
    {
        response.Dispose();
        return ValueTask.CompletedTask;
    }
}
