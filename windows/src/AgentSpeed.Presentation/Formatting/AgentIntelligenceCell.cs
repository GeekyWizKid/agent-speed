using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Formatting;

/// <summary>
/// The intelligence column of the dashboard's agent rows (DESIGN 4, Intelligence column): a fixed cell between the
/// effort capsule column and the live value, as wide as the wider of <c>888</c> in its number font and the header
/// caption; the number is right-aligned in it.
/// </summary>
public static class AgentIntelligenceColumn
{
    /// <summary>Between the cell and the live value.</summary>
    public const double Gap = 6;

    /// <summary>The widest value: an integer up to 136 (a live index above that is still three digits).</summary>
    public const string WidestNumber = "888";

    /// <summary>The number: 12 DIP semibold, tabular, one under the live value so tok/s stays the row's main number.</summary>
    public const double NumberSize = 12;

    /// <summary>The header captions: 11 DIP medium.</summary>
    public const double CaptionSize = 11;

    /// <summary>The cell's width from the measured <c>888</c> and caption, rounded up to a whole DIP.</summary>
    public static double Width(double numberWidth, double captionWidth) => Math.Ceiling(Math.Max(numberWidth, captionWidth));

    /// <summary>Space the cell takes in a row, its gap included.</summary>
    public static double Footprint(double width) => width + Gap;
}

/// <summary>
/// One agent row's 智力 (DESIGN 4, Intelligence column; METRICS 6.9 <i>Values</i>): the live task score in the live
/// value's number color, the resting score in the tertiary color, or the tertiary <c>–</c> without a score.
/// </summary>
/// <param name="Spoken">Added to the row's label after the live value: <c>a11y.agent.intel</c> (resting: inside <c>a11y.task.idle</c>); empty for none.</param>
/// <param name="TooltipLines">The row tooltip's lines after effort and speed: the task's state.</param>
public sealed record AgentIntelligenceCell(AgentIntelligenceForm Form, string Text, string Spoken, IReadOnlyList<string> TooltipLines)
{
    public const string Dash = "–";

    public static AgentIntelligenceCell Of(AgentStatus agent, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(localizer);
        var intelligence = agent.Intelligence;
        var lines = new List<string>(2);
        if (intelligence.Shown is not { } shown)
        {
            return new AgentIntelligenceCell(AgentIntelligenceForm.None, Dash, string.Empty, lines);
        }

        var text = shown.ToString(localizer.Culture);
        var live = intelligence.Form == AgentIntelligenceForm.Live;
        if (intelligence.State is { } state)
        {
            lines.Add(localizer["task.state." + Core.Tasks.TaskStateNames.Name(state)]);
        }

        var spoken = localizer.Format("a11y.agent.intel", text);
        return new AgentIntelligenceCell(intelligence.Form, text, live ? spoken : localizer.Format("a11y.task.idle", spoken), lines);
    }
}
