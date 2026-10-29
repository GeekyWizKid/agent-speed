namespace AgentSpeed.Presentation.TaskScore;

/// <summary>
/// The dimensions as the card shows them (DESIGN 12, <i>Score card</i>, 3): integers that add up to the shown score — each
/// rounded down, then the points still missing go to the largest fractional parts, ties in the order V C R E K.
/// </summary>
public static class TaskDimensionPoints
{
    /// <summary>The five integers in <see cref="TaskDimensions.Order"/>.</summary>
    public static IReadOnlyList<int> Split(TaskDimensions dimensions, int shown)
    {
        var order = TaskDimensions.Order;
        var points = new int[order.Count];
        var fractions = new double[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            var value = Math.Clamp(Finite(dimensions[order[i]]), 0, TaskDimensions.Max(order[i]));
            points[i] = (int)Math.Floor(value);
            fractions[i] = value - points[i];
        }

        var missing = shown - points.Sum();
        if (missing <= 0)
        {
            return points;
        }

        var ranked = Enumerable.Range(0, order.Count)
            .Where(i => points[i] < TaskDimensions.Max(order[i]))
            .OrderByDescending(i => fractions[i])
            .ThenBy(i => i)
            .ToArray();
        for (var k = 0; k < missing && k < ranked.Length; k++)
        {
            points[ranked[k]]++;
        }

        return points;
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
