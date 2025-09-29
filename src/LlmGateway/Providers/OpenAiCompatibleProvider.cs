using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmGateway.Configuration;
using LlmGateway.Contracts;

namespace LlmGateway.Providers;

/// <summary>
/// Talks to any server implementing the OpenAI HTTP API. The client's request is re-serialised with
/// <c>model</c> swapped for the upstream's model name; all other fields go through as sent.
/// </summary>
public sealed class OpenAiCompatibleProvider : ILlmProvider
{
    /// <summary>Name of the <see cref="IHttpClientFactory"/> client shared by every OpenAI-compatible upstream.</summary>
    public const string HttpClientName = "upstreams";

    private readonly string _name;
    private readonly UpstreamOptions _options;
    private readonly Uri _baseUrl;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenAiCompatibleProvider(string name, UpstreamOptions options, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _name = name;
        _options = options;
        _httpClientFactory = httpClientFactory;

        var baseUrl = options.BaseUrl ?? throw new ArgumentException($"Upstream '{name}' has no BaseUrl.", nameof(options));
        _baseUrl = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
    }

    public async Task<ChatCompletionResponse> CompleteChatAsync(ChatCompletionRequest request, string upstreamModel, CancellationToken cancellationToken)
    {
        var body = Serialize(request, upstreamModel);
        using var response = await SendAsync("chat/completions", body, cancellationToken);
        return await ReadAsync<ChatCompletionResponse>(response, cancellationToken);
    }

    public async Task<EmbeddingResponse> CreateEmbeddingsAsync(EmbeddingRequest request, string upstreamModel, CancellationToken cancellationToken)
    {
        var body = Serialize(request, upstreamModel);
        using var response = await SendAsync("embeddings", body, cancellationToken);
        return await ReadAsync<EmbeddingResponse>(response, cancellationToken);
    }

    private static JsonObject Serialize<TRequest>(TRequest request, string upstreamModel)
    {
        var body = JsonSerializer.SerializeToNode(request, GatewayJson.Options)!.AsObject();
        body["model"] = upstreamModel;
        return body;
    }

    /// <summary>Sends the request and returns the response once its headers arrive, throwing for non-success codes.</summary>
    private async Task<HttpResponseMessage> SendAsync(string path, JsonObject body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl, path))
        {
            Content = new StringContent(body.ToJsonString(GatewayJson.Options), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        foreach (var (header, value) in _options.Headers)
        {
            message.Headers.TryAddWithoutValidation(header, value);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new UpstreamException(_name, (int)response.StatusCode, error);
            }
        }

        return response;
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(GatewayJson.Options, cancellationToken)
                ?? throw new UpstreamException(_name, StatusCodes.Status502BadGateway, "The upstream returned an empty body.");
        }
        catch (JsonException exception)
        {
            throw new UpstreamException(_name, StatusCodes.Status502BadGateway, $"The upstream returned invalid JSON: {exception.Message}");
        }
    }
}
