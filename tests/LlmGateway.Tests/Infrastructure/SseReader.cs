using System.Text;
using System.Text.Json.Nodes;

namespace LlmGateway.Tests.Infrastructure;

internal static class SseReader
{
    /// <summary>The data payloads of a complete server-sent event body, including <c>[DONE]</c>.</summary>
    public static async Task<IReadOnlyList<string>> ReadEventsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Trim())
            .Where(block => block.StartsWith("data: ", StringComparison.Ordinal))
            .Select(block => block["data: ".Length..])
            .ToList();
    }

    /// <summary>Concatenates the <c>delta.content</c> of every chunk.</summary>
    public static string AssembleContent(IEnumerable<string> events)
    {
        var text = new StringBuilder();
        foreach (var payload in events.Where(e => e != "[DONE]"))
        {
            foreach (var choice in JsonNode.Parse(payload)!["choices"]?.AsArray() ?? [])
            {
                text.Append((string?)choice!["delta"]?["content"]);
            }
        }

        return text.ToString();
    }

    public static string Format(params string[] payloads) =>
        string.Concat(payloads.Select(p => $"data: {p}\n\n"));
}
