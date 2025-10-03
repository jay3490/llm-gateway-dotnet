using LlmGateway.Configuration;
using LlmGateway.Routing;

namespace LlmGateway.Tests.Unit;

public sealed class RoutePlannerTests
{
    private static readonly RouteTarget Heavy = new("a", "model-a", 3);
    private static readonly RouteTarget Light = new("b", "model-b", 1);
    private static readonly RouteTarget FirstFallback = new("c", "model-c", 1);
    private static readonly RouteTarget SecondFallback = new("d", "model-d", 1);

    private static readonly ModelRoute Route = new("alias", ModelKind.Chat, [Heavy, Light], [FirstFallback, SecondFallback]);

    [Fact]
    public void Primary_targets_lead_in_proportion_to_their_weight()
    {
        var planner = new RoutePlanner(new Random(1234));
        const int runs = 20_000;

        var heavyFirst = Enumerable.Range(0, runs).Count(_ => planner.Plan(Route)[0] == Heavy);

        Assert.InRange(heavyFirst / (double)runs, 0.73, 0.77);
    }

    [Fact]
    public void Every_target_appears_once_with_fallbacks_last_and_in_order()
    {
        var planner = new RoutePlanner(new Random(42));

        for (var i = 0; i < 100; i++)
        {
            var plan = planner.Plan(Route);

            Assert.Equal(4, plan.Count);
            Assert.Equal([Heavy, Light], plan.Take(2).OrderByDescending(t => t.Weight));
            Assert.Equal([FirstFallback, SecondFallback], plan.Skip(2));
        }
    }
}
