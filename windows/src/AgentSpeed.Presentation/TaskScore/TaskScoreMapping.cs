using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.TaskParsers;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;
using CoreTasks = AgentSpeed.Core.Tasks;

namespace AgentSpeed.Presentation.TaskScore;

/// <summary>Maps the engine's task values (METRICS 6.9, 6.11) onto the presentation's task score records.</summary>
public static class TaskScoreMapping
{
    public static TaskForm Form(AgentIntelligenceForm form) => form switch
    {
        AgentIntelligenceForm.Live => TaskForm.Live,
        AgentIntelligenceForm.Resting => TaskForm.Resting,
        _ => TaskForm.None,
    };

    public static TaskForm Form(CoreTasks.TaskValueForm form) => form switch
    {
        CoreTasks.TaskValueForm.Live => TaskForm.Live,
        CoreTasks.TaskValueForm.Resting => TaskForm.Resting,
        _ => TaskForm.None,
    };

    public static TaskState State(CoreTasks.TaskState? state) => state switch
    {
        CoreTasks.TaskState.Unverified => TaskState.Unverified,
        CoreTasks.TaskState.Passing => TaskState.Passing,
        CoreTasks.TaskState.Failing => TaskState.Failing,
        CoreTasks.TaskState.Pending => TaskState.Pending,
        _ => TaskState.None,
    };

    /// <summary>A run is a test, build or check; <c>other</c> never names a check, so it reads as a check.</summary>
    public static TaskCheckClass Check(CommandClass commandClass) => commandClass switch
    {
        CommandClass.Test => TaskCheckClass.Test,
        CommandClass.Build => TaskCheckClass.Build,
        _ => TaskCheckClass.Check,
    };

    /// <summary>An agent's own 智力 with its current task's facts and predictions (the dashboard row, a board sub row).</summary>
    public static TaskScoreFacts Facts(AgentStatus agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return Facts(agent.Intelligence, agent.Task);
    }

    public static TaskScoreFacts Facts(AgentIntelligence intelligence, CoreTasks.AgentTaskStatus? task)
    {
        var form = Form(intelligence.Form);
        var state = State(intelligence.State ?? task?.Result?.State);
        var result = task?.Result;
        if (result is null)
        {
            return new TaskScoreFacts(form, state, intelligence.Value);
        }

        var dims = result.Dims;
        return new TaskScoreFacts(
            form,
            state,
            intelligence.Value,
            new TaskDimensions(dims.V, dims.C, dims.R, dims.E, dims.K),
            result.VHeld,
            result.OpenFailure is { } failure
                ? new TaskOpenFailure(failure.Suite, Check(failure.Class), Clamp(failure.Errors), Clamp(failure.Peak), failure.Counted, failure.Edited)
                : null,
            result.LastPass is { } pass ? new TaskLastPass(pass.Suite, Check(pass.Class), pass.BuildOnly) : null,
            result.Oscillations,
            Predictions(task!));
    }

    /// <summary>The live task's predictions with each model's flag; null without them.</summary>
    public static TaskPredictions? Predictions(CoreTasks.AgentTaskStatus task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Predictions is not { } p)
        {
            return null;
        }

        var models = task.Models;
        return new TaskPredictions(
            new TaskPrediction(p.Success, Flag(models.Success)),
            new TaskPrediction(p.Stall, Flag(models.Stall)),
            p.Fix is { } fix ? new TaskPrediction(fix, Flag(models.Fix)) : null);
    }

    /// <summary>The engine's comparison over a range (METRICS 6.11 <i>Comparison</i>), numerators included.</summary>
    public static TaskComparison Comparison(CoreTasks.TaskComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TaskComparison(
            [.. result.BySource.Select(s => new TaskSourceStats(s.Source, Stats(s)))],
            [.. result.ByModel.Select(m => new TaskModelStats(m.Model, m.Sources, Stats(m)))]);
    }

    public static TaskStats Stats(CoreTasks.TaskAggregate row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new TaskStats(row.Tasks, row.ScoredTasks, row.MedianScore, row.LowerQuartile, row.UpperQuartile, row.Histogram,
            row.SuccessRate, row.StallRate, row.FixFailureRate, row.RecoverAttempts, row.TimeToFirstPassMs,
            Clamp(row.Successes), Clamp(row.Stalls), Clamp(row.FixFailures), Clamp(row.FixAttempts), Clamp(row.Recovered));
    }

    /// <summary>A score card for <paramref name="agent"/>, with the row's former tooltip lines as its facts.</summary>
    /// <param name="supplier">The agent whose score the row shows (a board group: the supplying member; null: the agent itself).</param>
    public static TaskCardSubject Subject(AgentStatus agent, IReadOnlyList<string> facts, AgentStatus? supplier = null, int subAgents = 0,
        int topCount = 0)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var shown = supplier ?? agent;
        return new TaskCardSubject(shown.Source, shown.Project, shown.Model, Facts(shown), facts, shown.Work, subAgents, topCount,
            Supported: shown.Task is not null || TaskScoredSources.Contains(shown.Source));
    }

    private static TaskModelFlag Flag(CoreTasks.TaskModel model) => new(model.Calibrated, model.Tasks);

    private static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}

/// <summary>
/// A set's or group's 智力 as its surface reads it (DESIGN 12, <i>Values and forms</i>): the highest score of the set's
/// agents — live before resting — with the supplying agent's state for the proof mark and N, the number of the set's
/// agents with a score in that form, for the words and the hero card's eyebrow.
/// </summary>
/// <param name="Agent">The supplying agent (METRICS 6.9: the same score and form, latest line, then smallest id).</param>
public sealed record TaskSetReading(TaskForm Form, int? Shown, TaskState State, int Count, AgentStatus? Agent)
{
    public static TaskSetReading None { get; } = new(TaskForm.None, null, TaskState.None, 0, null);

    public bool HasScore => Shown is not null;

    public bool IsResting => HasScore && Form == TaskForm.Resting;

    /// <summary>The number is drawn with the proof mark (DESIGN 12, <i>Proof mark</i>): the supplying agent's state is unproven.</summary>
    public bool IsUnproven => HasScore && TaskScoreText.IsUnproven(State);

    public string Number => TaskScoreText.Number(Shown);

    /// <summary>The set's value from the engine's <paramref name="value"/> and the set's <paramref name="agents"/>.</summary>
    public static TaskSetReading Of(CoreTasks.TaskSetValue value, IEnumerable<AgentStatus> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        if (value.Shown is not { } shown || value.Form == CoreTasks.TaskValueForm.None)
        {
            return None;
        }

        var form = TaskScoreMapping.Form(value.Form);
        var count = 0;
        AgentStatus? supplier = null;
        foreach (var agent in agents)
        {
            if (agent.Intelligence.Shown is not null && TaskScoreMapping.Form(agent.Intelligence.Form) == form)
            {
                count++;
            }

            if (supplier is null && string.Equals(agent.Id, value.AgentId, StringComparison.Ordinal))
            {
                supplier = agent;
            }
        }

        return new TaskSetReading(form, shown, TaskScoreMapping.State(supplier?.Intelligence.State), Math.Max(1, count), supplier);
    }

    public static TaskSetReading Of(IntelligenceSums sums, IEnumerable<AgentStatus> agents) => Of(sums.Score, agents);

    /// <summary>A board group's value: its highest member, among the root and its sub-agents.</summary>
    public static TaskSetReading Group(AgentStatus root, IReadOnlyList<AgentStatus> subs)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(subs);
        return Of(AgentIntelligence.Group(root, subs).Score, [root, .. subs]);
    }

    /// <summary>The score words (<c>智力 72，閒置中，2 個 agent 中最高</c> without 智力); null for none.</summary>
    public string? Words(Localizer loc) => Shown is { } shown ? TaskScoreText.Words(shown, State, Form, Count, loc) : null;

    /// <summary>
    /// The 智力 sentence of a set's label: <c>a11y.orb.intel</c> with the thinking words, <c>a11y.orb.intel.only</c> while
    /// 思考 is hidden (<paramref name="thinking"/> null); while none, <c>intel.none</c> takes the 智力 words' place.
    /// </summary>
    public string Sentence(string? thinking, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (Words(loc) is { } words)
        {
            return thinking is null ? loc.Format("a11y.orb.intel.only", words) : loc.Format("a11y.orb.intel", words, thinking);
        }

        if (thinking is null)
        {
            return loc["intel.none"];
        }

        const string Marker = "";
        var full = loc.Format("a11y.orb.intel", Marker, thinking);
        var intelligence = loc.Format("a11y.orb.intel.only", Marker);
        var at = full.IndexOf(intelligence, StringComparison.Ordinal);
        return at < 0 ? loc["intel.none"] : string.Concat(full.AsSpan(0, at), loc["intel.none"], full.AsSpan(at + intelligence.Length));
    }

    /// <summary>The tooltip line <c>task.tooltip.top</c>: the score and the supplying agent's project (its source without one); null for none.</summary>
    public string? Tooltip(Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (Shown is not { } shown)
        {
            return null;
        }

        var name = Agent is null ? string.Empty
            : string.IsNullOrWhiteSpace(Agent.Project) ? loc[Formatting.DisplayText.SourceKey(Agent.Source)] : Agent.Project;
        return name.Length == 0 ? null : loc.Format("task.tooltip.top", shown.ToString(loc.Culture), name);
    }
}
