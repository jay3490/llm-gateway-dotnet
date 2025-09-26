using LlmGateway.Configuration;
using Microsoft.Extensions.Options;

namespace LlmGateway.Routing;

/// <summary>A target a model alias can be served from: an upstream plus the model name it knows.</summary>
public sealed record RouteTarget(string Upstream, string Model, int Weight);

/// <summary>A model alias with its weighted primary targets and ordered fallbacks.</summary>
public sealed record ModelRoute(
    string Alias,
    ModelKind Kind,
    IReadOnlyList<RouteTarget> Targets,
    IReadOnlyList<RouteTarget> Fallbacks);

/// <summary>The model aliases clients may request, built once from configuration.</summary>
public sealed class ModelCatalog
{
    private readonly Dictionary<string, ModelRoute> _routes;

    public ModelCatalog(IOptions<GatewayOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _routes = options.Value.Models.ToDictionary(
            pair => pair.Key,
            pair => new ModelRoute(
                pair.Key,
                pair.Value.Kind,
                pair.Value.Targets.Select(ToTarget).ToList(),
                pair.Value.Fallbacks.Select(ToTarget).ToList()),
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ModelRoute> Routes => _routes.Values;

    public bool TryGet(string alias, ModelKind kind, out ModelRoute route)
    {
        if (_routes.TryGetValue(alias, out var found) && found.Kind == kind)
        {
            route = found;
            return true;
        }

        route = null!;
        return false;
    }

    private static RouteTarget ToTarget(RouteTargetOptions target) => new(target.Upstream, target.Model, target.Weight);
}
