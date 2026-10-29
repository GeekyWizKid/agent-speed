using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Intelligence;

/// <summary>Which of its three forms a 智力 value takes (METRICS 6.9 <i>Values</i>).</summary>
public enum AgentIntelligenceForm
{
    /// <summary>No current task, or one without a score: shown as "—".</summary>
    None,

    /// <summary>The agent is idle: its current task's final score, shown pale.</summary>
    Resting,

    /// <summary>The agent is working: its current task's score.</summary>
    Live,
}

/// <summary>
/// One agent's (or a board group's) 智力 (METRICS 6.9): the task score of its current task by its phase — live, resting
/// or none — and its own live thinking sums. A sub-agent's never count for its parent; a group takes the highest member
/// score.
/// </summary>
/// <param name="Sums">The agent's own window sums of the thinking line (a group: its members' added); their
/// <see cref="IntelligenceSums.Score"/> is <paramref name="Score"/>, so adding agents' sums gives the highest score.</param>
/// <param name="Score">Its value, with the agent that supplied it (itself, or a group's member).</param>
/// <param name="State">The supplying agent's task state (unverified and pending numbers are not final); null for none.</param>
public readonly record struct AgentIntelligence(IntelligenceSums Sums, TaskSetValue Score, TaskState? State = null)
{
    public static AgentIntelligence None => default;

    /// <summary>
    /// A board group's 智力 (6.9 <i>Sets and groups</i>): the highest score among the root and its sub-agents, with the
    /// state of the member that supplied it; the thinking sums of every member added.
    /// </summary>
    public static AgentIntelligence Group(AgentStatus root, IReadOnlyList<AgentStatus> subs)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(subs);
        var sums = root.Intelligence.Sums;
        var score = root.Intelligence.Score;
        var state = root.Intelligence.State;
        for (var i = 0; i < subs.Count; i++)
        {
            var sub = subs[i].Intelligence;
            sums += sub.Sums;
            var merged = TaskSetValue.Merge(score, sub.Score);
            if (merged != score)
            {
                state = sub.State;
            }

            score = merged;
        }

        return new AgentIntelligence(sums, score, score.Score is null ? null : state);
    }

    public AgentIntelligenceForm Form => Score.Form switch
    {
        TaskValueForm.Live => AgentIntelligenceForm.Live,
        TaskValueForm.Resting => AgentIntelligenceForm.Resting,
        _ => AgentIntelligenceForm.None,
    };

    /// <summary>The unrounded score; null for none.</summary>
    public double? Value => Score.Score;

    /// <summary>The score as shown: 0–100, rounded half away from zero; null for none.</summary>
    public int? Shown => Score.Shown;

    /// <summary>The agent's own reasoning tok/s.</summary>
    public double LiveThinking => Sums.LiveThinking;

    /// <summary>The agent's own reasoning share.</summary>
    public double ThinkingShare => Sums.ThinkingShare;
}
