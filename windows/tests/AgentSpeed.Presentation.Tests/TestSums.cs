using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Presentation.Tests;

/// <summary>Readout sums for presentation tests: thinking window sums and a 智力 value, live or resting.</summary>
internal static class TestSums
{
    /// <summary>
    /// <paramref name="output"/> tokens in the window, <paramref name="thinking"/> of them reasoning out of
    /// <paramref name="basis"/>, and a live score of <paramref name="score"/> (none when the window is empty).
    /// </summary>
    public static IntelligenceSums Of(double output, double score, double thinking = 0, double basis = 0) =>
        new(output, thinking, basis) { Score = output > 0 ? Live(score) : TaskSetValue.None };

    public static TaskSetValue Live(double score) => Value(TaskValueForm.Live, score);

    public static TaskSetValue Resting(double score) => Value(TaskValueForm.Resting, score);

    private static TaskSetValue Value(TaskValueForm form, double score) => new(form, score, TaskRounding.HalfAwayFromZero(score), "a");
}
