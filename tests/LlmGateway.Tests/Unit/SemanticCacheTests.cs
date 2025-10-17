using LlmGateway.Caching;
using LlmGateway.Configuration;
using LlmGateway.Contracts;
using LlmGateway.Providers;
using LlmGateway.Routing;
using Microsoft.Extensions.Time.Testing;

namespace LlmGateway.Tests.Unit;

public sealed class SemanticCacheTests
{
    private const string Cached = "What is the capital of France?";
    private const string Reworded = "What is the capital city of France?";

    private static readonly ModelRoute Route = new("smart", ModelKind.Chat, [new RouteTarget("u", "m", 1)], []);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task Same_words_in_a_different_form_are_a_semantic_hit()
    {
        var cache = await CacheWithAsync(threshold: 0.95, Cached);

        var lookup = await cache.LookupAsync(Route, Chat("what is the CAPITAL of france"), CancellationToken.None);

        Assert.Equal(CacheOutcome.SemanticHit, lookup.Outcome);
        Assert.Equal(1.0, lookup.Similarity, precision: 6);
        Assert.Equal("answer", lookup.Response!.Choices[0].Message!.GetText());
    }

    [Fact]
    public async Task Threshold_decides_whether_a_near_match_is_a_hit()
    {
        var similarity = VectorMath.CosineSimilarity(HashingEmbedder.Embed("user: " + Cached), HashingEmbedder.Embed("user: " + Reworded));
        Assert.InRange(similarity, 0.80, 0.99);

        var lenient = await CacheWithAsync(threshold: similarity - 0.01, Cached);
        var strict = await CacheWithAsync(threshold: similarity + 0.01, Cached);

        Assert.Equal(CacheOutcome.SemanticHit, (await lenient.LookupAsync(Route, Chat(Reworded), CancellationToken.None)).Outcome);
        Assert.Equal(CacheOutcome.Miss, (await strict.LookupAsync(Route, Chat(Reworded), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Unrelated_prompts_miss()
    {
        var cache = await CacheWithAsync(threshold: 0.5, Cached);

        var lookup = await cache.LookupAsync(Route, Chat("Write a haiku about autumn rain"), CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, lookup.Outcome);
    }

    [Fact]
    public async Task Different_system_prompt_is_a_different_partition()
    {
        var cache = await CacheWithAsync(threshold: 0.5, Cached);
        var request = Chat(Cached);
        request.Messages.Insert(0, ChatMessage.FromText("system", "Answer in French."));

        var lookup = await cache.LookupAsync(Route, request, CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, lookup.Outcome);
    }

    [Fact]
    public async Task Different_parameters_are_a_different_partition()
    {
        var cache = await CacheWithAsync(threshold: 0.5, Cached);
        var request = Chat(Cached);
        request.MaxTokens = 5;

        var lookup = await cache.LookupAsync(Route, request, CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, lookup.Outcome);
    }

    [Fact]
    public async Task Semantic_entries_expire_with_their_ttl()
    {
        var cache = await CacheWithAsync(threshold: 0.95, Cached);

        _time.Advance(TimeSpan.FromHours(2));

        Assert.Equal(CacheOutcome.Miss, (await cache.LookupAsync(Route, Chat("what is the capital of france"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Embedding_failure_falls_back_to_exact_matching()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions());
        var cache = new CompletionCache(new InMemoryCompletionCacheStore(options, _time), new FailingEmbedder(), options, _time);

        var miss = await cache.LookupAsync(Route, Chat(Cached), CancellationToken.None);
        await cache.StoreAsync(miss, Answer(), CancellationToken.None);

        Assert.Equal(CacheOutcome.Miss, miss.Outcome);
        Assert.Equal(CacheOutcome.ExactHit, (await cache.LookupAsync(Route, Chat(Cached), CancellationToken.None)).Outcome);
    }

    private async Task<CompletionCache> CacheWithAsync(double threshold, string cachedPrompt)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new GatewayOptions
        {
            Cache = new CacheOptions { SimilarityThreshold = threshold, Ttl = TimeSpan.FromHours(1) },
        });
        var cache = new CompletionCache(new InMemoryCompletionCacheStore(options, _time), new HashingPromptEmbedder(), options, _time);

        var miss = await cache.LookupAsync(Route, Chat(cachedPrompt), CancellationToken.None);
        await cache.StoreAsync(miss, Answer(), CancellationToken.None);
        return cache;
    }

    private static ChatCompletionRequest Chat(string text) =>
        new() { Model = "smart", Messages = [ChatMessage.FromText("user", text)] };

    private static ChatCompletionResponse Answer() => new()
    {
        Id = "chatcmpl-1",
        Model = "m",
        Choices = [new ChatChoice { Message = ChatMessage.FromText("assistant", "answer"), FinishReason = "stop" }],
    };

    private sealed class HashingPromptEmbedder : IPromptEmbedder
    {
        public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult<float[]?>(HashingEmbedder.Embed(text));
    }

    private sealed class FailingEmbedder : IPromptEmbedder
    {
        public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult<float[]?>(null);
    }
}
