using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;

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
/// One agent row's 智力 (DESIGN 4, Intelligence column; DESIGN 12; METRICS 6.9 <i>Values</i>): the agent's own task score
/// — live in the live value's number color, resting in the tertiary color, the tertiary <c>–</c> without a score — with the
/// proof mark under an unverified or pending score. The row's tooltip is the score card, so the cell has no tooltip lines.
/// </summary>
/// <param name="Shown">The integer shown, for the cell's motion; null for none.</param>
/// <param name="Unproven">No verified result behind the score: the dotted underline.</param>
/// <param name="Spoken">Added to the row's label after the live value: <c>a11y.agent.intel</c> with the score words; empty for none.</param>
public sealed record AgentIntelligenceCell(AgentIntelligenceForm Form, string Text, int? Shown, bool Unproven, string Spoken)
{
    public const string Dash = TaskScoreText.Dash;

    public static AgentIntelligenceCell Of(AgentStatus agent, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(localizer);
        var facts = TaskScoreMapping.Facts(agent);
        if (facts.Shown is not { } shown)
        {
            return new AgentIntelligenceCell(AgentIntelligenceForm.None, Dash, null, false, string.Empty);
        }

        return new AgentIntelligenceCell(agent.Intelligence.Form, TaskScoreText.Number(shown), shown, facts.IsUnproven,
            TaskScoreText.AgentLabel(facts, 1, localizer));
    }
}
