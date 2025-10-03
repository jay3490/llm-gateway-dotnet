namespace LlmGateway.Routing;

/// <summary>
/// Decides the order in which a route's targets are tried. Primary targets are put in a weighted
/// random order, so each one is tried first in proportion to its weight and the others still serve
/// as fallbacks before the explicit fallback list, which is always tried last and in order.
/// </summary>
public sealed class RoutePlanner(Random random)
{
    public IReadOnlyList<RouteTarget> Plan(ModelRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var plan = new List<RouteTarget>(route.Targets.Count + route.Fallbacks.Count);
        var remaining = route.Targets.ToList();

        while (remaining.Count > 0)
        {
            var next = PickWeighted(remaining);
            plan.Add(next);
            remaining.Remove(next);
        }

        plan.AddRange(route.Fallbacks);
        return plan;
    }

    private RouteTarget PickWeighted(List<RouteTarget> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var roll = random.NextDouble() * candidates.Sum(c => c.Weight);
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll < 0)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }
}
