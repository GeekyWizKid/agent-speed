using System.Text.Json;
using System.Text.Json.Nodes;
using AgentSpeed.Core.TaskParsers;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary><c>tasks</c> (every query and fact, METRICS 6.9) and <c>values</c> (agents, groups and the set).</summary>
public sealed partial class TaskScoreCaseTests
{
    public static TheoryData<int, string> TaskCases() => Indices("tasks");

    public static TheoryData<int, string> ValueCases() => Indices("values");

    [Theory]
    [MemberData(nameof(TaskCases))]
    public void TaskQueriesMatch(int index, string name)
    {
        var item = Case("tasks", index);
        var events = Events(item.GetProperty("events"));
        var query = 0;
        foreach (var q in item.GetProperty("queries").EnumerateArray())
        {
            var now = OptionalLong(Member(q, "nowMs"));
            var task = TaskSegments.Current(events, now);
            Assert.NotNull(task);
            var evaluation = TaskEvaluation.Of(task.Events, now);
            AssertClose(q.GetProperty("expect"), TaskResult(evaluation), name + " #" + query++);
        }
    }

    [Theory]
    [MemberData(nameof(ValueCases))]
    public void ValuesMatch(int index, string name)
    {
        var item = Case("values", index);
        var now = item.GetProperty("nowMs").GetInt64();
        var agents = item.GetProperty("agents").EnumerateArray().ToList();
        var values = new Dictionary<string, TaskValue>(StringComparer.Ordinal);
        var candidates = new Dictionary<string, TaskCandidate>(StringComparer.Ordinal);
        var agentsNode = new JsonObject();
        foreach (var agent in agents)
        {
            var id = agent.GetProperty("id").GetString()!;
            var value = TaskValues.Agent(Events(agent.GetProperty("events")), agent.GetProperty("phase").GetString() == "idle", now);
            values[id] = value;
            candidates[id] = new TaskCandidate(id, OptionalLong(Member(agent, "lastLineMs")) ?? 0, value);
            agentsNode[id] = new JsonObject
            {
                ["form"] = FormName(value.Form),
                ["score"] = Number(value.Score),
                ["shown"] = Number(value.Shown),
                ["state"] = value.State is { } state ? TaskStateNames.Name(state) : null,
            };
        }

        var groupsNode = new JsonObject();
        foreach (var (root, members) in BoardGroups(agents))
        {
            var group = SetValue(TaskValues.Highest(members.Select(m => candidates[m])));
            group.Insert(0, "members", new JsonArray([.. members.Select(m => (JsonNode)m)]));
            groupsNode[root] = group;
        }

        var actual = new JsonObject
        {
            ["agents"] = agentsNode,
            ["groups"] = groupsNode,
            ["all"] = SetValue(TaskValues.Highest(agents.Select(a => candidates[a.GetProperty("id").GetString()!]))),
        };
        AssertClose(item.GetProperty("expect"), actual, name);
    }

    internal static JsonObject TaskResult(TaskEvaluation ev) => new()
    {
        ["state"] = TaskStateNames.Name(ev.State),
        ["score"] = Number(ev.Score),
        ["shown"] = Number(ev.Shown),
        ["dims"] = Dims(ev.Dims),
        ["ig"] = ev.Ig,
        ["loop"] = ev.Loop,
        ["osc"] = ev.Osc,
        ["counts"] = Counts(ev.Counts),
        ["firstPassMs"] = Number(ev.FirstPassMs),
        ["stallEnds"] = new JsonArray([.. ev.StallEnds.Select(e => (JsonNode)e)]),
        ["vHeld"] = Number(ev.VHeld),
        ["openFailure"] = ev.OpenFailure is { } open
            ? new JsonObject
            {
                ["suite"] = Text(open.Suite),
                ["class"] = TaskWire.Name(open.Class),
                ["key"] = open.Key,
                ["errors"] = open.Errors,
                ["peak"] = open.Peak,
                ["counted"] = open.Counted,
                ["edited"] = open.Edited,
            }
            : null,
        ["lastPass"] = ev.LastPass is { } pass
            ? new JsonObject
            {
                ["suite"] = Text(pass.Suite),
                ["class"] = TaskWire.Name(pass.Class),
                ["key"] = pass.Key,
                ["buildOnly"] = pass.BuildOnly,
            }
            : null,
    };

    private static string FormName(TaskValueForm form) => form switch
    {
        TaskValueForm.Live => "live",
        TaskValueForm.Resting => "resting",
        _ => "none",
    };

    private static JsonObject SetValue(TaskSetValue value) => new()
    {
        ["form"] = FormName(value.Form),
        ["score"] = Number(value.Score),
        ["shown"] = Number(value.Shown),
        ["agent"] = Text(value.AgentId),
    };

    /// <summary>
    /// <c>board_groups</c> of <c>live_thinking.py</c>: every root (no parent, or a parent that is not listed) with all its
    /// descendants, the root first and then depth first in list order.
    /// </summary>
    private static List<(string Root, List<string> Members)> BoardGroups(List<JsonElement> agents)
    {
        var ids = agents.Select(a => a.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var roots = new List<string>();
        foreach (var agent in agents)
        {
            var id = agent.GetProperty("id").GetString()!;
            var parent = OptionalString(Member(agent, "parentId"));
            if (parent is not null && ids.Contains(parent) && parent != id)
            {
                (children.TryGetValue(parent, out var list) ? list : children[parent] = []).Add(id);
            }
            else
            {
                roots.Add(id);
            }
        }

        var groups = new List<(string Root, List<string> Members)>();
        foreach (var root in roots)
        {
            var members = new List<string>();
            var stack = new Stack<string>([root]);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                members.Add(id);
                if (children.TryGetValue(id, out var kids))
                {
                    for (var k = kids.Count - 1; k >= 0; k--)
                    {
                        stack.Push(kids[k]);
                    }
                }
            }

            groups.Add((root, members));
        }

        return groups;
    }
}
