namespace LlmGateway.Providers;

/// <summary>
/// A streaming chat completion that an upstream has already accepted. Obtaining one means the
/// upstream answered with a success status, so routing can still fall back until this point.
/// </summary>
public interface IChatChunkSource : IAsyncDisposable
{
    /// <summary>The <c>data</c> payload of each server-sent event in order, stopping before <c>[DONE]</c>.</summary>
    IAsyncEnumerable<string> ReadEventsAsync(CancellationToken cancellationToken);
}
