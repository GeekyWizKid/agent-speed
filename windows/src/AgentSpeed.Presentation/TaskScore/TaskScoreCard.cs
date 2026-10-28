using AgentSpeed.Core;
using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.TaskScore;

/// <summary>The state chip's look (DESIGN 12, <i>State chips</i>): failing is amber, never red.</summary>
public enum TaskChipTone
{
    Mint,
    Amber,

    /// <summary>Secondary text, no fill, a dotted outline.</summary>
    Neutral,
}

public enum TaskChipGlyph
{
    None,

    /// <summary>CompletedSolid U+E930.</summary>
    Check,

    /// <summary>Error U+E783.</summary>
    Alert,

    /// <summary>Clock U+E917.</summary>
    Clock,

    /// <summary>A drawn ring of 8 dots.</summary>
    DotRing,
}

public sealed record TaskChip(TaskState State, string Text, TaskChipTone Tone, TaskChipGlyph Glyph)
{
    public static TaskChip Of(TaskState state, Localizer loc) => new(state, TaskScoreText.StateName(state, loc), state switch
    {
        TaskState.Passing => TaskChipTone.Mint,
        TaskState.Failing => TaskChipTone.Amber,
        _ => TaskChipTone.Neutral,
    }, state switch
    {
        TaskState.Passing => TaskChipGlyph.Check,
        TaskState.Failing => TaskChipGlyph.Alert,
        TaskState.Pending => TaskChipGlyph.Clock,
        TaskState.Unverified => TaskChipGlyph.DotRing,
        _ => TaskChipGlyph.None,
    });
}

/// <summary>The card's big number: label color while live, secondary while resting, a tertiary <c>–</c> while none.</summary>
public enum TaskNumberTone
{
    Live,
    Resting,
    None,
}

/// <summary>What a dimension row shows in place of <c>value / max</c>.</summary>
public enum TaskDimensionValueKind
{
    /// <summary>A real number, 0 included.</summary>
    Value,

    /// <summary><c>task.state.unverified</c> (tertiary) over a dotted track: no verification result yet, never a fake 0.</summary>
    Unverified,

    /// <summary>V while pending: <c>task.state.pending</c> (secondary) and the dotted credit a passing re-run restores.</summary>
    Pending,

    /// <summary>E while none: <c>–</c>.</summary>
    Absent,
}

/// <param name="Points">The shown integer (0 unless <see cref="Kind"/> is <see cref="TaskDimensionValueKind.Value"/>).</param>
/// <param name="ValueText">The number, or the words or dash that replace it.</param>
/// <param name="MaxText"><c> / 25</c>, only beside a number.</param>
/// <param name="Fill">The bar's solid fill, 0–1 of its width.</param>
/// <param name="Ghost">The dotted held credit, 0–1 of its width (pending V only).</param>
/// <param name="DottedTrack">No verification result yet: the track itself is dotted, with no fill.</param>
public sealed record TaskDimensionRow(
    TaskDimension Dimension,
    string Label,
    TaskDimensionValueKind Kind,
    int Points,
    int Max,
    string ValueText,
    string MaxText,
    double Fill,
    double Ghost,
    bool DottedTrack,
    string Description,
    string Spoken);

/// <summary>One stretch of the composition bar, in points of 100 (DESIGN 12, <i>Score card</i>, 2).</summary>
/// <param name="IsGhost">Pending V's dotted held credit.</param>
public readonly record struct TaskCompositionSegment(TaskDimension Dimension, double Start, double Length, bool IsGhost);

public enum TaskPredictionKind
{
    Success,
    Stall,
    FixFails,
}

/// <param name="Percent"><c>52%</c>, <c>&lt;1%</c> or <c>&gt;99%</c>.</param>
/// <param name="Flag"><c>task.predict.cell.calibrated</c>, <c>.prior</c> or <c>.priorOnly</c>.</param>
public sealed record TaskPredictionCell(TaskPredictionKind Kind, string Percent, string Label, string Flag, bool Calibrated, string Spoken);

public enum TaskSignalGlyph
{
    /// <summary>The amber loop glyph of the dashboard's looping mark (Sync U+E895).</summary>
    Loop,

    /// <summary>RepeatAll U+E8EE, secondary.</summary>
    Repeat,

    /// <summary>Switch U+E8AB: edits undone back and forth.</summary>
    Oscillation,

    /// <summary>BulletedList U+E8FD.</summary>
    Summary,
}

public sealed record TaskSignalLine(TaskSignalGlyph Glyph, string Text, bool IsWarning);

/// <summary>Everything a score card is built from.</summary>
/// <param name="Project">The row's project (the card's title).</param>
/// <param name="Model">The model id, or null when unknown.</param>
/// <param name="FactLines">The row's former tooltip lines, in their order (DESIGN 12, <i>Score card</i>, 6).</param>
/// <param name="Work">The agent's work signals (METRICS 6.10) for the 行為 block.</param>
/// <param name="SubAgents">A board group row's sub-agents (<c>boards.subCount</c> on line 2); 0 elsewhere.</param>
/// <param name="TopCount">The hero card's N: agents of the set with a score in the shown form (eyebrow and words from 2).</param>
/// <param name="Supported">The source records tool calls; null takes <see cref="TaskScoredSources"/>.</param>
public sealed record TaskCardSubject(
    SourceKind Source,
    string Project,
    string? Model,
    TaskScoreFacts Facts,
    IReadOnlyList<string>? FactLines = null,
    WorkSignals? Work = null,
    int SubAgents = 0,
    int TopCount = 0,
    bool? Supported = null);

/// <summary>
/// The score card (任務評分卡, DESIGN 12): the breakdown behind an agent's number, as text, values and fractions the
/// window draws, plus the row's extra content for screen readers (<see cref="FullDescription"/>).
/// </summary>
/// <param name="Meta">Line 2: model · source (· sub-agents).</param>
/// <param name="Chip">The state chip; null for an unsupported source.</param>
/// <param name="Eyebrow">Above the number: <c>phase.idle</c> while resting, <c>task.card.top</c> on the hero's card; null when neither applies.</param>
/// <param name="Shown">The number as an integer, for motion; null while none.</param>
/// <param name="HasProofMark">The dotted underline goes under the number.</param>
/// <param name="Composition">The composition bar's stretches, V C R E K.</param>
/// <param name="Predictions">Empty unless the agent is live with a score.</param>
/// <param name="FullDescription">The row's <c>AutomationProperties.FullDescription</c>, in DESIGN 12's order.</param>
public sealed record TaskScoreCard(
    SourceKind Source,
    string Project,
    string Meta,
    TaskChip? Chip,
    bool IsUnsupported,
    string UnsupportedText,
    string? Eyebrow,
    string Number,
    int? Shown,
    TaskNumberTone NumberTone,
    bool HasProofMark,
    string OutOf,
    IReadOnlyList<TaskTextRun> Status,
    IReadOnlyList<TaskCompositionSegment> Composition,
    string DimensionsTitle,
    IReadOnlyList<TaskDimensionRow> Dimensions,
    string PredictionsTitle,
    IReadOnlyList<TaskPredictionCell> Predictions,
    string PredictionNote,
    string SignalsTitle,
    IReadOnlyList<TaskSignalLine> Signals,
    IReadOnlyList<string> Facts,
    string Footnote,
    string FullDescription)
{
    /// <summary>The prediction models' threshold for calibration (METRICS 6.11, <i>Calibration</i>).</summary>
    public const int CalibrationTasks = 40;

    public const string OutOfText = "/100";

    public bool HasEyebrow => !string.IsNullOrEmpty(Eyebrow);

    public bool HasPredictions => Predictions.Count > 0;

    public bool HasSignals => Signals.Count > 0;

    public bool HasFacts => Facts.Count > 0;

    public string StatusText => TaskScoreText.Plain(Status);

    public static TaskScoreCard Build(TaskCardSubject subject, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(loc);
        var sourceName = loc[DisplayText.SourceKey(subject.Source)];
        var meta = MetaLine(subject, sourceName, loc);
        var facts = subject.FactLines?.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray() ?? [];
        var footnote = loc["task.card.footnote"];
        if (!(subject.Supported ?? TaskScoredSources.Contains(subject.Source)))
        {
            var unsupported = loc["task.unsupported"];
            return new TaskScoreCard(subject.Source, subject.Project, meta, null, true, unsupported, null, TaskScoreText.Dash, null,
                TaskNumberTone.None, false, OutOfText, [], [], loc["task.dims.title"], [], loc["task.predict.title"], [], string.Empty,
                loc["task.signals.title"], [], facts, footnote, HistoryText.JoinParts([unsupported, .. facts], loc));
        }

        var score = subject.Facts;
        var shown = score.Shown;
        var state = shown is null ? TaskState.None : score.State;
        var chip = TaskChip.Of(state, loc);
        var status = TaskScoreText.Status(score with { State = state }, loc);
        var points = TaskDimensionPoints.Split(score.Dimensions, shown ?? 0);
        var dimensions = DimensionRows(score, state, points, loc);
        var predictions = PredictionCells(score, loc);
        var signals = SignalLines(subject, loc);
        var scoreWords = shown is { } value
            ? loc.Format("a11y.task.score", TaskScoreText.Words(value, state, score.Form, subject.TopCount, loc), chip.Text)
            : loc["intel.none"];

        var description = new List<string> { scoreWords, TaskScoreText.Plain(status) };
        foreach (var row in dimensions)
        {
            description.Add(row.Spoken);
            description.Add(row.Description);
        }

        description.AddRange(predictions.Select(p => p.Spoken));
        description.AddRange(signals.Select(s => s.Text));
        description.AddRange(facts);
        description.Add(footnote);

        return new TaskScoreCard(
            subject.Source,
            subject.Project,
            meta,
            chip,
            false,
            string.Empty,
            EyebrowLine(score.Form, subject.TopCount, loc),
            TaskScoreText.Number(shown),
            shown,
            shown is null ? TaskNumberTone.None : score.Form == TaskForm.Resting ? TaskNumberTone.Resting : TaskNumberTone.Live,
            shown is not null && TaskScoreText.IsUnproven(state),
            OutOfText,
            status,
            shown is null ? [] : CompositionOf(points, state, score.VHeld),
            loc["task.dims.title"],
            dimensions,
            loc["task.predict.title"],
            predictions,
            predictions.Count > 0 ? loc.Format("task.predict.note", loc[DisplayText.SourceKey(subject.Source)], CalibrationTasks) : string.Empty,
            loc["task.signals.title"],
            signals,
            facts,
            footnote,
            HistoryText.JoinParts(description, loc));
    }

    /// <summary>The composition bar's stretches from the shown integers; pending V holds its dotted credit in V's place.</summary>
    public static IReadOnlyList<TaskCompositionSegment> CompositionOf(IReadOnlyList<int> points, TaskState state, double? vHeld)
    {
        ArgumentNullException.ThrowIfNull(points);
        var segments = new List<TaskCompositionSegment>(5);
        double at = 0;
        for (var i = 0; i < TaskDimensions.Order.Count && i < points.Count; i++)
        {
            var dimension = TaskDimensions.Order[i];
            var ghost = dimension == TaskDimension.Verify && state == TaskState.Pending;
            double length = ghost ? HeldCredit(vHeld) : points[i];
            if (length <= 0)
            {
                continue;
            }

            length = Math.Min(length, TaskScoreText.MaxScore - at);
            segments.Add(new TaskCompositionSegment(dimension, at, length, ghost));
            at += length;
        }

        return segments;
    }

    private static string MetaLine(TaskCardSubject subject, string sourceName, Localizer loc)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(subject.Model))
        {
            parts.Add(subject.Model);
        }

        parts.Add(sourceName);
        if (subject.SubAgents > 0)
        {
            parts.Add(loc.Format("boards.subCount", subject.SubAgents));
        }

        return string.Join(" · ", parts);
    }

    private static string? EyebrowLine(TaskForm form, int topCount, Localizer loc)
    {
        var parts = new List<string>(2);
        if (form == TaskForm.Resting)
        {
            parts.Add(loc["phase.idle"]);
        }

        if (topCount >= 2)
        {
            parts.Add(loc.Format("task.card.top", topCount));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static IReadOnlyList<TaskDimensionRow> DimensionRows(TaskScoreFacts score, TaskState state, IReadOnlyList<int> points, Localizer loc)
    {
        var rows = new List<TaskDimensionRow>(5);
        var noProof = state is TaskState.None or TaskState.Unverified;
        for (var i = 0; i < TaskDimensions.Order.Count; i++)
        {
            var dimension = TaskDimensions.Order[i];
            var key = LabelKey(dimension);
            var label = loc[key];
            var description = loc[key + ".desc"];
            var max = TaskDimensions.Max(dimension);
            if (noProof && (dimension != TaskDimension.Efficiency || state == TaskState.None))
            {
                rows.Add(dimension == TaskDimension.Efficiency
                    ? new TaskDimensionRow(dimension, label, TaskDimensionValueKind.Absent, 0, max, TaskScoreText.Dash, string.Empty, 0, 0, true,
                        description, HistoryText.Labelled(label, loc["task.state.none"], loc))
                    : new TaskDimensionRow(dimension, label, TaskDimensionValueKind.Unverified, 0, max, loc["task.state.unverified"], string.Empty,
                        0, 0, true, description, loc.Format("a11y.task.unverified", label)));
                continue;
            }

            if (dimension == TaskDimension.Verify && state == TaskState.Pending)
            {
                rows.Add(new TaskDimensionRow(dimension, label, TaskDimensionValueKind.Pending, 0, max, loc["task.state.pending"], string.Empty, 0,
                    HeldCredit(score.VHeld) / max, false, description, loc.Format("a11y.task.pending", label)));
                continue;
            }

            var value = points[i];
            rows.Add(new TaskDimensionRow(dimension, label, TaskDimensionValueKind.Value, value, max, value.ToString(loc.Culture),
                " / " + max.ToString(loc.Culture), (double)value / max, 0, false, description,
                loc.Format("a11y.task.dim", label, value, max)));
        }

        return rows;
    }

    private static IReadOnlyList<TaskPredictionCell> PredictionCells(TaskScoreFacts score, Localizer loc)
    {
        if (score.Form != TaskForm.Live || score.Shown is null || score.Predictions is not { } predictions)
        {
            return [];
        }

        var cells = new List<TaskPredictionCell>(3)
        {
            Cell(TaskPredictionKind.Success, predictions.Success, loc),
            Cell(TaskPredictionKind.Stall, predictions.Stall, loc),
        };
        if (score.State == TaskState.Failing && predictions.FixFails is { } fix)
        {
            cells.Add(Cell(TaskPredictionKind.FixFails, fix, loc));
        }

        return cells;
    }

    private static TaskPredictionCell Cell(TaskPredictionKind kind, TaskPrediction prediction, Localizer loc)
    {
        var (labelKey, spokenKey) = kind switch
        {
            TaskPredictionKind.Success => ("task.predict.success", "a11y.task.predict.success"),
            TaskPredictionKind.Stall => ("task.predict.stall", "a11y.task.predict.stall"),
            _ => ("task.predict.fixFails", "a11y.task.predict.fixFails"),
        };
        var flag = prediction.Flag;
        var flagText = flag.Calibrated
            ? loc.Format("task.predict.cell.calibrated", flag.Tasks)
            : flag.Tasks < CalibrationTasks
                ? loc.Format("task.predict.cell.prior", flag.Tasks, CalibrationTasks)
                : loc["task.predict.cell.priorOnly"];
        var spoken = loc.Format(spokenKey, TaskScoreText.PercentWords(prediction.Probability, loc));
        spoken = flag.Calibrated ? loc.Format("a11y.task.predict.calibrated", spoken, flag.Tasks) : loc.Format("a11y.task.predict.prior", spoken);
        return new TaskPredictionCell(kind, TaskScoreText.Percent(prediction.Probability), loc[labelKey], flagText, flag.Calibrated, spoken);
    }

    private static IReadOnlyList<TaskSignalLine> SignalLines(TaskCardSubject subject, Localizer loc)
    {
        var lines = new List<TaskSignalLine>(4);
        var work = subject.Work ?? WorkSignals.None;
        if (work.Looping is { } looping)
        {
            lines.Add(new TaskSignalLine(TaskSignalGlyph.Loop, loc.Format("work.looping", looping), IsWarning: true));
        }

        if (work.Repeating is { } repeating)
        {
            lines.Add(new TaskSignalLine(TaskSignalGlyph.Repeat, loc.Format("work.repeating", repeating), IsWarning: false));
        }

        if (subject.Facts.Oscillations > 0)
        {
            lines.Add(new TaskSignalLine(TaskSignalGlyph.Oscillation, loc.Format("task.signal.oscillation", subject.Facts.Oscillations), IsWarning: false));
        }

        if (work.Novelty is { } novelty)
        {
            lines.Add(new TaskSignalLine(TaskSignalGlyph.Summary,
                loc.Format("work.summary", HistoryText.Percent(novelty), work.Failed, work.Recovered), IsWarning: false));
        }

        return lines;
    }

    private static double HeldCredit(double? vHeld) =>
        vHeld is { } held && double.IsFinite(held) && held > 0 ? Math.Min(held, TaskDimensions.Max(TaskDimension.Verify)) : TaskDimensions.Max(TaskDimension.Verify);

    private static string LabelKey(TaskDimension dimension) => dimension switch
    {
        TaskDimension.Verify => "task.dim.verify",
        TaskDimension.Converge => "task.dim.converge",
        TaskDimension.Recover => "task.dim.recover",
        TaskDimension.Efficiency => "task.dim.efficiency",
        _ => "task.dim.complete",
    };
}

/// <summary>The sources whose tasks get a score: they record tool calls (METRICS 6.9).</summary>
public static class TaskScoredSources
{
    /// <summary>In source order.</summary>
    public static IReadOnlyList<SourceKind> All { get; } =
    [
        SourceKind.Codex, SourceKind.ClaudeCode, SourceKind.ClaudeDesktop, SourceKind.Omp, SourceKind.Pi, SourceKind.OpenClaw, SourceKind.Droid,
    ];

    public static bool Contains(SourceKind source) => All.Contains(source);
}
