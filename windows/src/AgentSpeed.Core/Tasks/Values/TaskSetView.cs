namespace AgentSpeed.Core.Tasks;

/// <summary>A set's value: its sources (null: all) and the highest-score value of its listed agents.</summary>
internal sealed record TaskSetRow(IReadOnlyList<SourceKind>? Sources, TaskSetValue Value);

/// <summary>A board group's value: its members (root first, depth first) and their highest-score value.</summary>
internal sealed record TaskGroupRow(string Root, IReadOnlyList<string> Members, TaskSetValue Value);

/// <summary>
/// The set values of the dumps and the end-to-end fixture (METRICS 6.9 <i>Sets and groups</i>): the agents list at an
/// instant (6.7 steps 1–4), the value of every set over it, and of every board group (DESIGN 2c).
/// </summary>
internal sealed record TaskSetView(IReadOnlyList<string> Listed, IReadOnlyList<TaskSetRow> Sets, IReadOnlyList<TaskGroupRow> Groups)
{
    public const long ListedMs = 600_000;

    /// <summary>The sources with a task score, in the dumps' order.</summary>
    public static IReadOnlyList<SourceKind> ScoredSources { get; } =
    [
        SourceKind.Codex, SourceKind.ClaudeCode, SourceKind.ClaudeDesktop, SourceKind.Omp, SourceKind.Pi, SourceKind.OpenClaw,
        SourceKind.Droid,
    ];

    /// <param name="sets">The sets to evaluate (null entry: all sources); null evaluates all sources and every scored source
    /// with listed agents.</param>
    public static TaskSetView Of(IReadOnlyList<TaskAgentRow> rows, long nowMs, IReadOnlyList<IReadOnlyList<SourceKind>?>? sets = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var tree = ListedTree(rows.Select(r => r.Agent).ToList(), nowMs);
        var shown = rows.Where(r => tree.ContainsKey(r.Agent.Id)).OrderBy(r => r.Agent.Id, StringComparer.Ordinal).ToList();
        var listed = shown.Select(r => r.Agent.Id).ToList();
        var outSets = new List<TaskSetRow>();
        foreach (var sources in sets ?? [null, .. ScoredSources.Select(s => (IReadOnlyList<SourceKind>?)[s])])
        {
            var members = shown.Where(r => sources is null || sources.Contains(r.Agent.Source)).ToList();
            if (sources is not null && members.Count == 0 && sets is null)
            {
                continue;
            }

            outSets.Add(new TaskSetRow(sources, Highest(members)));
        }

        var groups = BoardGroups(shown.Select(r => (r.Agent.Id, tree[r.Agent.Id])).ToList());
        var byId = shown.ToDictionary(r => r.Agent.Id, StringComparer.Ordinal);
        var outGroups = groups.Select(g => new TaskGroupRow(g.Root, g.Members, Highest(g.Members.Select(m => byId[m]))))
            .OrderBy(g => g.Root, StringComparer.Ordinal)
            .ToList();
        return new TaskSetView(listed, outSets, outGroups);
    }

    public static TaskSetValue Highest(IEnumerable<TaskAgentRow> rows) =>
        TaskValues.Highest(rows.Select(r => new TaskCandidate(r.Agent.Id, r.Agent.LastLineMs ?? 0, r.Value)));

    /// <summary>
    /// 6.7 steps 1–4: agents generating or with a line in the last 10 minutes, with every ancestor; each listed agent's
    /// tree parent (null when its parent is not listed).
    /// </summary>
    public static Dictionary<string, string?> ListedTree(IReadOnlyList<TaskAgentView> agents, long nowMs)
    {
        var byId = new Dictionary<string, TaskAgentView>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            byId[agent.Id] = agent;
        }

        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (agent.Phase == AgentPhase.Generating || (agent.LastLineMs is { } last && nowMs - last <= ListedMs))
            {
                chosen.Add(agent.Id);
            }
        }

        foreach (var id in chosen.ToList())
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var current = byId[id].ParentId;
            while (current is not null && byId.TryGetValue(current, out var parent) && seen.Add(current))
            {
                chosen.Add(current);
                current = parent.ParentId;
            }
        }

        return chosen.ToDictionary(id => id, id => byId[id].ParentId is { } p && chosen.Contains(p) ? p : null, StringComparer.Ordinal);
    }

    /// <summary>
    /// DESIGN 2c groups: every root (no parent, or a parent that is not in the list) with all its descendants, the root
    /// first and then depth first in list order.
    /// </summary>
    public static List<(string Root, List<string> Members)> BoardGroups(IReadOnlyList<(string Id, string? ParentId)> agents)
    {
        var ids = agents.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var roots = new List<string>();
        foreach (var (id, parent) in agents)
        {
            if (parent is not null && ids.Contains(parent) && parent != id)
            {
                (children.TryGetValue(parent, out var list) ? list : children[parent] = []).Add(id);
            }
            else
            {
                roots.Add(id);
            }
        }

        var groups = new List<(string, List<string>)>();
        foreach (var root in roots)
        {
            var members = new List<string>();
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                members.Add(id);
                if (children.TryGetValue(id, out var kids))
                {
                    for (var i = kids.Count - 1; i >= 0; i--)
                    {
                        stack.Push(kids[i]);
                    }
                }
            }

            groups.Add((root, members));
        }

        return groups;
    }
}
