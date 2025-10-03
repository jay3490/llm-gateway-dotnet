namespace LlmGateway.Configuration;

/// <summary>Root of the <c>Gateway</c> configuration section.</summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>Upstream backends keyed by name. Routes refer to upstreams by this name.</summary>
    public Dictionary<string, UpstreamOptions> Upstreams { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Model aliases exposed to clients, keyed by the alias clients send in <c>model</c>.</summary>
    public Dictionary<string, ModelRouteOptions> Models { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ResilienceOptions Resilience { get; init; } = new();
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

    /// <summary>Overrides <see cref="ResilienceOptions.AttemptTimeout"/> for this upstream, for example for slow local models.</summary>
    public TimeSpan? AttemptTimeout { get; set; }
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

/// <summary>Polly settings applied to every upstream; each upstream gets its own pipeline and circuit.</summary>
public sealed class ResilienceOptions
{
    /// <summary>Time allowed for one attempt to produce response headers. For streams this does not limit the stream's length.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Retries against the same upstream for 5xx, 408, network errors and timeouts. 429 is never retried in place.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    /// <summary>Base delay for exponential backoff with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    public int CircuitBreakerMinimumThroughput { get; set; } = 10;

    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}
