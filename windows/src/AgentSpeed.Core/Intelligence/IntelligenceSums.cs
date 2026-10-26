namespace AgentSpeed.Core.Intelligence;

/// <summary>
/// The window sums of the live intelligence index (METRICS 6.9), in tokens over the live window. Additive over sources,
/// so a floating orb adds the sums of its own sources (6.8).
/// </summary>
/// <param name="Output">Σ w: output tokens generating in the window, measured and estimated.</param>
/// <param name="Graded">Σ w × grade.</param>
/// <param name="Thinking">Σ ρ: reasoning tokens.</param>
/// <param name="ThinkingBasis">Σ w': the output of sources that record reasoning apart from output.</param>
public readonly record struct IntelligenceSums(double Output = 0, double Graded = 0, double Thinking = 0, double ThinkingBasis = 0)
{
    public const double ThinkingBonus = 20;

    public static IntelligenceSums Zero => default;

    /// <summary>Something generated in the window: the index has a value.</summary>
    public bool IsActive => Output > 0;

    public double ThinkingShare => ThinkingBasis > 0 ? Thinking / ThinkingBasis : 0;

    /// <summary><c>liveThinking</c>: reasoning tok/s over the live window.</summary>
    public double LiveThinking => Thinking / (Engine.SpeedEngine.WindowMs / 1000.0);

    /// <summary><c>Σ (w × g) / Σ w + 20 × thinkingShare</c>; null when nothing generated in the window.</summary>
    public double? Index => IsActive ? Graded / Output + ThinkingBonus * ThinkingShare : null;

    /// <summary>The index as shown: rounded half away from zero.</summary>
    public int? Shown => Index is { } index ? (int)Math.Round(index, MidpointRounding.AwayFromZero) : null;

    /// <summary>DeepSeek Harness reports reasoning inside its output (METRICS 4.3): it adds its grade, not its share.</summary>
    public static bool RecordsReasoning(SourceKind source) => source != SourceKind.DeepSeek;

    /// <summary>Adds output <paramref name="weight"/> of which <paramref name="reasoning"/> is reasoning, at <paramref name="grade"/>.</summary>
    public IntelligenceSums Add(SourceKind source, double weight, double reasoning, ModelGrade grade)
    {
        if (weight <= 0)
        {
            return this;
        }

        var separate = RecordsReasoning(source);
        return new IntelligenceSums(
            Output + weight,
            Graded + weight * grade.Value,
            Thinking + (separate ? reasoning : 0),
            ThinkingBasis + (separate ? weight : 0));
    }

    public static IntelligenceSums operator +(IntelligenceSums a, IntelligenceSums b) =>
        new(a.Output + b.Output, a.Graded + b.Graded, a.Thinking + b.Thinking, a.ThinkingBasis + b.ThinkingBasis);
}
