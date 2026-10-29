namespace AgentSpeed.Core.Tasks;

/// <summary>
/// <c>model_rows</c> (METRICS 6.11 <i>Calibration</i> step 2): one model's rows over records already in calibration
/// order, as <c>x</c> with a leading 1, the label and the weight <c>1 / m</c>, and the number of tasks with a row.
/// </summary>
internal sealed class TaskModelRows
{
    private const int SnapshotWidth = 9;
    private const int FixWidth = 6;

    /// <summary>Per model, the columns of its features in a snapshot or fix row, in the model's order.</summary>
    private static readonly int[] SuccessColumns = [1, 2, 3, 4, 5, 6, 7];
    private static readonly int[] StallColumns = [4, 5, 6, 2, 7];
    private static readonly int[] FixColumns = [0, 1, 2, 3, 4];

    private readonly List<double> _x = [];
    private readonly List<double> _labels = [];
    private readonly List<double> _weights = [];

    private TaskModelRows(int width)
    {
        Width = width;
    }

    /// <summary><c>d</c>: the features plus the intercept.</summary>
    public int Width { get; }

    public int Count => _labels.Count;

    public int Tasks { get; private set; }

    public double X(int row, int column) => _x[(row * Width) + column];

    public double Label(int row) => _labels[row];

    public double Weight(int row) => _weights[row];

    /// <summary>
    /// The rows of <paramref name="kind"/>. A row of the wrong width (a damaged stored record) is left out; the records
    /// this code writes always have the full width.
    /// </summary>
    public static TaskModelRows Of(List<TaskRecord> records, TaskModelKind kind)
    {
        var columns = kind switch
        {
            TaskModelKind.Success => SuccessColumns,
            TaskModelKind.Stall => StallColumns,
            _ => FixColumns,
        };
        var width = kind == TaskModelKind.Fix ? FixWidth : SnapshotWidth;
        var rows = new TaskModelRows(columns.Length + 1);
        foreach (var record in records)
        {
            var source = kind == TaskModelKind.Fix ? record.FixRows : record.Snapshots;
            var m = 0;
            foreach (var row in source)
            {
                if (row is not null && row.Length == width)
                {
                    m++;
                }
            }

            if (m == 0)
            {
                continue;
            }

            rows.Tasks++;
            var weight = 1.0 / m;
            foreach (var row in source)
            {
                if (row is null || row.Length != width)
                {
                    continue;
                }

                rows._x.Add(1.0);
                foreach (var column in columns)
                {
                    rows._x.Add(row[column]);
                }

                rows._labels.Add(kind == TaskModelKind.Success ? (record.Success ? 1 : 0) : row[^1]);
                rows._weights.Add(weight);
            }
        }

        return rows;
    }
}
