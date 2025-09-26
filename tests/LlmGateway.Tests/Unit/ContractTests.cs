using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Contracts;
using LlmGateway.Providers;

namespace LlmGateway.Tests.Unit;

public sealed class ContractTests
{
    [Fact]
    public void Unknown_request_fields_survive_a_round_trip()
    {
        const string json = """{"model":"m","messages":[{"role":"user","content":"hi","name":"bob"}],"temperature":0.5,"tools":[]}""";

        var request = JsonSerializer.Deserialize<ChatCompletionRequest>(json, GatewayJson.Options)!;
        var written = JsonNode.Parse(JsonSerializer.Serialize(request, GatewayJson.Options))!;

        Assert.Equal(0.5, (double)written["temperature"]!);
        Assert.NotNull(written["tools"]);
        Assert.Equal("bob", (string?)written["messages"]![0]!["name"]);
        Assert.Null(written["stream"]);
    }

    [Fact]
    public void Text_is_read_and_rewritten_in_multi_part_content()
    {
        const string json = """{"role":"user","content":[{"type":"text","text":"one"},{"type":"image_url","image_url":{"url":"x"}},{"type":"text","text":"two"}]}""";
        var message = JsonSerializer.Deserialize<ChatMessage>(json, GatewayJson.Options)!;

        Assert.Equal("one\ntwo", message.GetText());

        message.RewriteText(text => text.ToUpperInvariant());

        Assert.Equal("ONE\nTWO", message.GetText());
        Assert.Equal("x", (string?)message.Content![1]!["image_url"]!["url"]);
    }

    [Fact]
    public void Base64_embeddings_decode_to_the_same_vector()
    {
        float[] vector = [0.25f, -1.5f, 3f];
        var base64 = Convert.ToBase64String(vector.SelectMany(BitConverter.GetBytes).ToArray());

        var data = new EmbeddingData { Embedding = JsonSerializer.SerializeToElement(base64) };

        Assert.Equal(vector, data.ToVector());
    }

    [Fact]
    public void Hashing_embedder_is_deterministic_and_normalised()
    {
        var first = HashingEmbedder.Embed("The quick brown fox");
        var second = HashingEmbedder.Embed("the QUICK brown fox!");

        Assert.Equal(first, second);
        Assert.Equal(1.0, Math.Sqrt(first.Sum(v => (double)v * v)), precision: 5);
    }
}
