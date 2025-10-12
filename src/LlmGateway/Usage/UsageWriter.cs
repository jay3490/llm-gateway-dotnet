using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LlmGateway.Usage;

/// <summary>
/// Applies pending migrations at startup, then drains <see cref="UsageRecorder"/> into SQLite in
/// batches. On shutdown the queue is closed and whatever is left is written before the host stops.
/// </summary>
public sealed partial class UsageWriter(
    UsageRecorder recorder,
    IDbContextFactory<UsageDbContext> contextFactory,
    ILogger<UsageWriter> logger) : BackgroundService
{
    private const int MaxBatchSize = 200;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
        {
            EnsureDirectoryExists(context.Database.GetConnectionString());
            await context.Database.MigrateAsync(cancellationToken);
        }

        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        recorder.Complete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = recorder.Reader;
        var batch = new List<UsageRecord>(MaxBatchSize);

        // Not tied to stoppingToken: StopAsync completes the channel, and the loop ends once it is drained.
        while (await reader.WaitToReadAsync(CancellationToken.None))
        {
            while (batch.Count < MaxBatchSize && reader.TryRead(out var record))
            {
                batch.Add(record);
            }

            await WriteAsync(batch);
            batch.Clear();
        }
    }

    /// <summary>SQLite creates the database file but not the directory it lives in.</summary>
    private static void EnsureDirectoryExists(string? connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private async Task WriteAsync(List<UsageRecord> batch)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync();
            context.Usage.AddRange(batch);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            LogWriteFailed(logger, exception, batch.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to write {Count} usage records")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, int count);
}
