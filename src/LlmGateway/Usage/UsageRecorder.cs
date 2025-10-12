using System.Threading.Channels;

namespace LlmGateway.Usage;

/// <summary>
/// Accepts usage records from request handlers without waiting on the database. Records are queued
/// in a bounded channel and written in batches by <see cref="UsageWriter"/>; if the queue is full
/// (the database has fallen far behind) new records are dropped and counted rather than slowing
/// down requests.
/// </summary>
public sealed partial class UsageRecorder(ILogger<UsageRecorder> logger)
{
    private const int Capacity = 10_000;

    private readonly Channel<UsageRecord> _channel = Channel.CreateBounded<UsageRecord>(new BoundedChannelOptions(Capacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private long _dropped;

    public long Dropped => Interlocked.Read(ref _dropped);

    internal ChannelReader<UsageRecord> Reader => _channel.Reader;

    public void Record(UsageRecord record)
    {
        if (!_channel.Writer.TryWrite(record))
        {
            var dropped = Interlocked.Increment(ref _dropped);
            LogDropped(logger, dropped);
        }
    }

    internal void Complete() => _channel.Writer.TryComplete();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Usage queue is full; dropped a usage record ({Dropped} dropped so far)")]
    private static partial void LogDropped(ILogger logger, long dropped);
}
