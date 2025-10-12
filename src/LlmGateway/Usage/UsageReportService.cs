using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace LlmGateway.Usage;

public sealed record UsageTotals(
    [property: JsonPropertyName("requests")] int Requests,
    [property: JsonPropertyName("prompt_tokens")] long PromptTokens,
    [property: JsonPropertyName("completion_tokens")] long CompletionTokens,
    [property: JsonPropertyName("total_tokens")] long TotalTokens);

public sealed record UsageGroup(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("usage")] UsageTotals Usage);

public sealed record UsageReport(
    [property: JsonPropertyName("from")] DateTime From,
    [property: JsonPropertyName("to")] DateTime To,
    [property: JsonPropertyName("totals")] UsageTotals Totals,
    [property: JsonPropertyName("by_api_key")] IReadOnlyList<UsageGroup> ByApiKey,
    [property: JsonPropertyName("by_model")] IReadOnlyList<UsageGroup> ByModel);

/// <summary>Aggregates stored usage for the admin report.</summary>
public sealed class UsageReportService(IDbContextFactory<UsageDbContext> contextFactory)
{
    public async Task<UsageReport> BuildAsync(DateTime fromUtc, DateTime toUtc, string? apiKeyName, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var records = context.Usage.AsNoTracking()
            .Where(u => u.TimestampUtc >= fromUtc && u.TimestampUtc < toUtc);
        if (!string.IsNullOrEmpty(apiKeyName))
        {
            records = records.Where(u => u.ApiKeyName == apiKeyName);
        }

        var byKey = await Summarise(records.GroupBy(u => u.ApiKeyName), cancellationToken);
        var byModel = await Summarise(records.GroupBy(u => u.ModelAlias), cancellationToken);

        var totals = new UsageTotals(
            byKey.Sum(g => g.Usage.Requests),
            byKey.Sum(g => g.Usage.PromptTokens),
            byKey.Sum(g => g.Usage.CompletionTokens),
            byKey.Sum(g => g.Usage.TotalTokens));

        return new UsageReport(fromUtc, toUtc, totals, byKey, byModel);
    }

    private static async Task<List<UsageGroup>> Summarise(IQueryable<IGrouping<string, UsageRecord>> groups, CancellationToken cancellationToken)
    {
        var rows = await groups
            .Select(g => new
            {
                g.Key,
                Requests = g.Count(),
                Prompt = g.Sum(u => (long)u.PromptTokens),
                Completion = g.Sum(u => (long)u.CompletionTokens),
                Total = g.Sum(u => (long)u.TotalTokens),
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new UsageGroup(r.Key, new UsageTotals(r.Requests, r.Prompt, r.Completion, r.Total)))
            .ToList();
    }
}
