using AgentSpeed.Core;

namespace AgentSpeed.Presentation.Modes;

/// <summary>
/// What a group of agents shows as one capsule (DESIGN 2b, Context menu item 1): the agents that are generating, or all
/// of them when none is; their shared effort and speed when they agree, <see cref="Mixed"/> when they differ.
/// </summary>
/// <param name="Capsule">The shared capsule; null when mixed.</param>
public sealed record SharedEffortCapsule(EffortCapsule? Capsule)
{
    public bool Mixed => Capsule is null;

    /// <summary>Null when nothing is known, or when the agreed values would draw nothing.</summary>
    public static SharedEffortCapsule? Of(IEnumerable<AgentStatus> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var list = agents.ToArray();
        var generating = list.Where(a => a.Phase == AgentPhase.Generating).ToArray();
        var known = (generating.Length > 0 ? generating : list).Select(a => EffortCapsule.Of(a.Modes))
            .Where(c => c.IsKnown).Distinct().Take(2).ToArray();
        return known switch
        {
            [] => null,
            [var only] => only.IsVisible ? new SharedEffortCapsule(only) : null,
            _ => new SharedEffortCapsule(Capsule: null),
        };
    }
}
