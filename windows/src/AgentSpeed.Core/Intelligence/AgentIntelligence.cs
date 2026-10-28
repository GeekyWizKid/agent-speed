namespace AgentSpeed.Core.Intelligence;

/// <summary>Which of its three forms an agent's intelligence takes (METRICS 6.9, Agent intelligence).</summary>
public enum AgentIntelligenceForm
{
    /// <summary>Nothing generated in the window and no model known: no value.</summary>
    None,

    /// <summary>Nothing generated in the window: the grade of the agent's current model and effort.</summary>
    Resting,

    /// <summary>Output in the window: the index over the agent's own sums.</summary>
    Live,
}

/// <summary>
/// One agent's intelligence (METRICS 6.9, Agent intelligence): <b>live</b> from the window sums restricted to its own
/// contributions, else <b>resting</b> on its grade, else <b>none</b>. The forms are never mixed.
/// </summary>
/// <param name="Sums">The agent's own window sums; a sub-agent's never count for its parent.</param>
/// <param name="Grade">The grade of its current model, latest named provider and current effort; null without a model.</param>
public readonly record struct AgentIntelligence(IntelligenceSums Sums, ModelGrade? Grade)
{
    public static AgentIntelligence None => default;

    public AgentIntelligenceForm Form => Sums.IsActive ? AgentIntelligenceForm.Live
        : Grade is not null ? AgentIntelligenceForm.Resting
        : AgentIntelligenceForm.None;

    /// <summary>The live index, or the grade at rest; null for none.</summary>
    public double? Value => Form switch
    {
        AgentIntelligenceForm.Live => Sums.Index,
        AgentIntelligenceForm.Resting => Grade!.Value.Value,
        _ => null,
    };

    /// <summary>The value as shown: rounded half away from zero (a grade is an integer already).</summary>
    public int? Shown => Value is { } value ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : null;

    /// <summary>The agent's own reasoning tok/s; 0 unless live.</summary>
    public double LiveThinking => Sums.LiveThinking;

    /// <summary>The agent's own reasoning share; 0 unless live.</summary>
    public double ThinkingShare => Sums.ThinkingShare;
}
