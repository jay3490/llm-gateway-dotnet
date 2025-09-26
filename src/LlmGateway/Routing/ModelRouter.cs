using LlmGateway.Providers;

namespace LlmGateway.Routing;

/// <summary>The value an upstream returned, plus the target that produced it.</summary>
public sealed record RoutedResult<T>(T Value, RouteTarget Target);

/// <summary>Sends a call for a model alias to one of the alias's upstream targets.</summary>
public sealed class ModelRouter(UpstreamRegistry upstreams)
{
    public async Task<RoutedResult<T>> ExecuteAsync<T>(
        ModelRoute route,
        Func<ILlmProvider, string, CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(call);

        var target = route.Targets[0];
        var value = await call(upstreams.Get(target.Upstream), target.Model, cancellationToken);
        return new RoutedResult<T>(value, target);
    }
}
