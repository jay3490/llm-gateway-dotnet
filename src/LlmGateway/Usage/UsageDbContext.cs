using Microsoft.EntityFrameworkCore;

namespace LlmGateway.Usage;

public sealed class UsageDbContext(DbContextOptions<UsageDbContext> options) : DbContext(options)
{
    public DbSet<UsageRecord> Usage => Set<UsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var usage = modelBuilder.Entity<UsageRecord>();
        usage.ToTable("usage_records");
        usage.HasKey(u => u.Id);
        usage.Property(u => u.ApiKeyName).HasMaxLength(128);
        usage.Property(u => u.Endpoint).HasMaxLength(32);
        usage.Property(u => u.ModelAlias).HasMaxLength(128);
        usage.Property(u => u.Upstream).HasMaxLength(128);
        usage.Property(u => u.UpstreamModel).HasMaxLength(256);
        usage.HasIndex(u => u.TimestampUtc);
        usage.HasIndex(u => new { u.ApiKeyName, u.TimestampUtc });
    }
}
