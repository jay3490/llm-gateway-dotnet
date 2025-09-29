namespace LlmGateway.Tests.Infrastructure;

/// <summary>An <see cref="IHttpClientFactory"/> whose clients all send through one handler.</summary>
public sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
