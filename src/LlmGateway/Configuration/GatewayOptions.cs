namespace LlmGateway.Configuration;

/// <summary>Root of the <c>Gateway</c> configuration section.</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Upstream backends keyed by name. Routes refer to upstreams by this name.</summary>
    public Dictionary<string, UpstreamOptions> Upstreams { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Model aliases exposed to clients, keyed by the alias clients send in <c>model</c>.</summary>
    public Dictionary<string, ModelRouteOptions> Models { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum UpstreamKind
{
    /// <summary>Deterministic in-process provider; needs no network and no API key.</summary>
    Fake,

    /// <summary>Any server speaking the OpenAI HTTP API: OpenAI itself, Ollama, vLLM, LM Studio and similar.</summary>
    OpenAICompatible,
}

public sealed class UpstreamOptions
{
    public UpstreamKind Kind { get; set; } = UpstreamKind.Fake;

    /// <summary>Base URL including the API version segment, for example <c>https://api.openai.com/v1</c> or <c>http://localhost:11434/v1</c>.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Sent as a bearer token. Leave empty for servers that need no key, such as a local Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Extra headers sent with every request, for example <c>OpenAI-Organization</c>.</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum ModelKind
{
    Chat,
    Embedding,
}

public sealed class ModelRouteOptions
{
    public ModelKind Kind { get; set; } = ModelKind.Chat;

    /// <summary>Primary targets. One is picked per request in proportion to its weight.</summary>
    public List<RouteTargetOptions> Targets { get; init; } = [];

    /// <summary>Tried in order once every primary target has failed.</summary>
    public List<RouteTargetOptions> Fallbacks { get; init; } = [];
}

public sealed class RouteTargetOptions
{
    /// <summary>Name of an entry in <see cref="GatewayOptions.Upstreams"/>.</summary>
    public string Upstream { get; set; } = string.Empty;

    /// <summary>Model name sent to the upstream in place of the alias.</summary>
    public string Model { get; set; } = string.Empty;

    public int Weight { get; set; } = 1;
}
