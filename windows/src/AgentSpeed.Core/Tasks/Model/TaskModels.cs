namespace AgentSpeed.Core.Tasks;

/// <summary>The three logistic models of METRICS 6.11 <i>Predictions</i>.</summary>
internal enum TaskModelKind
{
    Success,
    Stall,
    Fix,
}

/// <summary>
/// One logistic model: <c>P = σ(β₀ + Σ βᵢ·xᵢ)</c> with the intercept first, whether it was calibrated on the source's
/// history, and the number of tasks with a row for it (METRICS 6.11 <i>Calibration</i>).
/// </summary>
public sealed record TaskModel(IReadOnlyList<double> Coefficients, bool Calibrated, int Tasks)
{
    /// <summary>The model's probability for features in its order.</summary>
    public double Probability(ReadOnlySpan<double> features) => TaskPredictions.Sigmoid(TaskPredictions.Linear(Coefficients, features));
}

/// <summary>The coefficients of a source: <see cref="Prior"/> until its history calibrates a model.</summary>
public sealed record TaskModels(TaskModel Success, TaskModel Stall, TaskModel Fix)
{
    internal static readonly double[] SuccessPrior = [-1.5, 3.0, -0.5, 1.0, 1.0, -2.0, -1.5, -0.5];
    internal static readonly double[] StallPrior = [-1.0, -2.0, 3.0, 2.0, 0.8, 0.5];
    internal static readonly double[] FixPrior = [-0.5, 1.5, 1.0, 1.0, 1.5, -1.0];

    /// <summary>The documented prior coefficients, not calibrated.</summary>
    public static TaskModels Prior { get; } = new(PriorModel(TaskModelKind.Success, 0), PriorModel(TaskModelKind.Stall, 0), PriorModel(TaskModelKind.Fix, 0));

    internal static double[] PriorOf(TaskModelKind kind) => kind switch
    {
        TaskModelKind.Success => SuccessPrior,
        TaskModelKind.Stall => StallPrior,
        _ => FixPrior,
    };

    internal static TaskModel PriorModel(TaskModelKind kind, int tasks) => new(Array.AsReadOnly((double[])PriorOf(kind).Clone()), false, tasks);
}
