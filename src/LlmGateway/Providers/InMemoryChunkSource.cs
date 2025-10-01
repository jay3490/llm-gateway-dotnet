using System.Runtime.CompilerServices;

namespace LlmGateway.Providers;

/// <summary>A stream over events that are already in memory, used by the offline provider.</summary>
public sealed class InMemoryChunkSource(IReadOnlyList<string> events) : IChatChunkSource
{
    public async IAsyncEnumerable<string> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var payload in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return payload;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
