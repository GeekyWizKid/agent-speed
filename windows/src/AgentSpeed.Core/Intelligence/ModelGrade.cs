using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.Intelligence;

/// <summary>A model's class by the base output price of its price-table entry (METRICS 6.9).</summary>
public enum ModelClass
{
    Light,
    Standard,
    Flagship,
}

/// <summary>
/// The grade of a model at an effort (METRICS 6.9): <c>class + 6 × level</c>, 60 – 136. The class is the model's, never
/// the request's: tiers, periods, off-peak and fast prices are not used.
/// </summary>
/// <param name="Level">The effort's meter level, plus one for a top rung; 2 (medium) when unknown.</param>
public readonly record struct ModelGrade(ModelClass Class, int Level)
{
    public const int LevelStep = 6;
    public const int DefaultLevel = 2;

    /// <summary>An unknown model at an unknown effort.</summary>
    public static ModelGrade Unknown { get; } = new(ModelClass.Standard, DefaultLevel);

    /// <summary>Base output prices (micro-USD per million tokens) from which a model is flagship or standard.</summary>
    private const long FlagshipOutput = 15_000_000;
    private const long StandardOutput = 3_000_000;

    public int ClassPoints => Class switch
    {
        ModelClass.Flagship => 100,
        ModelClass.Standard => 80,
        _ => 60,
    };

    public int Value => ClassPoints + LevelStep * Level;

    public static ModelGrade Of(PriceTable table, string? model, string? provider, string? effort) =>
        new(ClassOf(table, model, provider), LevelOf(effort));

    /// <summary>A free entry or provider is light; a model the table does not know is standard.</summary>
    public static ModelClass ClassOf(PriceTable table, string? model, string? provider)
    {
        ArgumentNullException.ThrowIfNull(table);
        var resolution = table.Resolver.Resolve(model, provider);
        return resolution.Kind switch
        {
            ResolutionKind.Free => ModelClass.Light,
            ResolutionKind.Priced when table.Models[resolution.Index].Pricing is { } pricing => pricing.Base.Prices.Output switch
            {
                >= FlagshipOutput => ModelClass.Flagship,
                >= StandardOutput => ModelClass.Standard,
                _ => ModelClass.Light,
            },
            _ => ModelClass.Standard,
        };
    }

    /// <summary>The EXECUTION-MODES meter level plus one for <c>ultra</c> and <c>ultracode</c>.</summary>
    public static int LevelOf(string? effort) =>
        effort is not null && EffortAndSpeed.Level(effort) is { } level
            ? level + (EffortAndSpeed.IsTopRung(effort) ? 1 : 0)
            : DefaultLevel;
}
