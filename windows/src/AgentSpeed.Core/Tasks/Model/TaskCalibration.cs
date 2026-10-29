namespace AgentSpeed.Core.Tasks;

/// <summary>
/// <c>calibrate</c> (METRICS 6.11 <i>Calibration</i>): per model, the coefficients fitted on a source's latest complete
/// records, or the prior when there are too few tasks or one class weighs too little. In memory, never stored.
/// </summary>
public static class TaskCalibration
{
    private const int MinTasks = 40;
    private const double MinClassWeight = 5.0;
    private const int MaxTasks = 2000;

    /// <summary>One source's models from its records (each needs its <see cref="TaskRecord.AgentId"/> for the order).</summary>
    public static TaskModels Calibrate(IEnumerable<TaskRecord> records)
    {
        var chosen = Chosen(records);
        return new TaskModels(Model(chosen, TaskModelKind.Success), Model(chosen, TaskModelKind.Stall), Model(chosen, TaskModelKind.Fix));
    }

    /// <summary>
    /// The gradient of the penalised negative log-likelihood of one model at <paramref name="coefficients"/>, over the rows
    /// <see cref="Calibrate"/> fits (the fixture's independent optimality check).
    /// </summary>
    internal static double[] Gradient(IEnumerable<TaskRecord> records, TaskModelKind kind, IReadOnlyList<double> coefficients)
    {
        var rows = TaskModelRows.Of(Chosen(records), kind);
        return TaskLogistic.Gradient(coefficients, TaskModels.PriorOf(kind), rows);
    }

    private static TaskModel Model(List<TaskRecord> chosen, TaskModelKind kind)
    {
        var rows = TaskModelRows.Of(chosen, kind);
        var positive = 0.0;
        var negative = 0.0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows.Label(i) == 1)
            {
                positive += rows.Weight(i);
            }
            else if (rows.Label(i) == 0)
            {
                negative += rows.Weight(i);
            }
        }

        if (rows.Tasks < MinTasks || positive < MinClassWeight || negative < MinClassWeight)
        {
            return TaskModels.PriorModel(kind, rows.Tasks);
        }

        return new TaskModel(Array.AsReadOnly(TaskLogistic.Fit(rows, TaskModels.PriorOf(kind))), true, rows.Tasks);
    }

    /// <summary>The complete records ordered by end, start, agent id (ordinal), then ordinal; the latest 2000.</summary>
    private static List<TaskRecord> Chosen(IEnumerable<TaskRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var complete = new List<(TaskRecord Record, int Position)>();
        foreach (var record in records)
        {
            if (record.Complete)
            {
                complete.Add((record, complete.Count));
            }
        }

        complete.Sort(static (a, b) =>
        {
            var order = a.Record.EndMs.CompareTo(b.Record.EndMs);
            if (order == 0)
            {
                order = a.Record.StartMs.CompareTo(b.Record.StartMs);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(a.Record.AgentId, b.Record.AgentId);
            }

            if (order == 0)
            {
                order = a.Record.Ordinal.CompareTo(b.Record.Ordinal);
            }

            return order != 0 ? order : a.Position.CompareTo(b.Position);
        });

        var skip = Math.Max(0, complete.Count - MaxTasks);
        var chosen = new List<TaskRecord>(complete.Count - skip);
        for (var i = skip; i < complete.Count; i++)
        {
            chosen.Add(complete[i].Record);
        }

        return chosen;
    }
}
