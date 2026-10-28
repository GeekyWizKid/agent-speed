namespace AgentSpeed.Core.Tasks;

/// <summary>How the task score and its percentages are shown (METRICS 6.9 <i>Score</i>, 6.11 <i>Predictions</i>).</summary>
public static class TaskRounding
{
    /// <summary>
    /// Rounded half away from zero exactly as the reference: <c>int(v + 0.5)</c>, or <c>-int(-v + 0.5)</c> below zero —
    /// the sum is formed in double and truncated toward zero. NaN reads as 0; values past the int range saturate.
    /// </summary>
    public static int HalfAwayFromZero(double value) => value >= 0 ? Truncate(value + 0.5) : -Truncate(-value + 0.5);

    private static int Truncate(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return value <= -int.MaxValue ? -int.MaxValue : (int)value;
    }
}
