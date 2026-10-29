namespace AgentSpeed.Presentation.History.Tasks;

/// <summary>The section's three layouts, by its content width (DESIGN 12, <i>Usage History: task performance</i>, Widths).</summary>
public enum TaskPerformanceWidth
{
    /// <summary>Below 600: pickers on a line of their own, side cards, a three-column comparison and a model list.</summary>
    Narrow,

    /// <summary>600–860 (the window's minimum lands here): a narrower centre and distributions; tasks and scored merge.</summary>
    Medium,

    /// <summary>From 860.</summary>
    Wide,
}

/// <summary>Sizes of the task performance section (DESIGN 12; the prototype where DESIGN gives no value).</summary>
public static class TaskPerformanceLayout
{
    public const double WideFrom = 860;
    public const double MediumFrom = 600;

    public const double TitleSize = 15;
    public const double InfoWidth = 340;
    public const double VersusSize = 11;
    public const double PickerDot = 7;

    public const double SideNameSize = 13;
    public const double CountsSize = 11.5;
    public const double MedianSize = 34;
    public const double NarrowMedianSize = 24;
    public const double CaptionSize = 11;
    public const double AxisSize = 9;
    public const double ChipSize = 10;

    public const double HistogramHeight = 28;
    public const double HistogramGap = 2;
    public const double HistogramCorner = 1.5;

    /// <summary>A non-empty bin is never drawn shorter than this.</summary>
    public const double HistogramMinBar = 1.5;

    /// <summary>The source color's opacity for bins other than the median's (which is at 100 %).</summary>
    public const double HistogramOpacity = 0.7;

    public const double RowHeight = 36;
    public const double MetricSize = 12;
    public const double LowerBetterSize = 10;
    public const double ValueColumn = 64;
    public const double ValueSize = 13;
    public const double TrackHeight = 6;

    /// <summary>A comparison track's fill: the source color, at 85 % on dark.</summary>
    public const double DarkTrackOpacity = 0.85;

    public const double SingleLabelColumn = 180;
    public const double NarrowValueColumn = 72;
    public const double NarrowCardRadius = 10;
    public const double NarrowCardPadding = 10;

    public const double ByModelSize = 13;
    public const double ByModelGap = 16;
    public const double ModelRowHeight = 30;
    public const double ModelIdSize = 12.5;
    public const double ModelDot = 7;
    public const double ModelDotStep = 4;
    public const double StripWidth = 48;
    public const double StripHeight = 4;

    /// <summary>The strip's quartile range: the first source's color at 45 %.</summary>
    public const double StripOpacity = 0.45;

    public const double FootnoteSize = 11;

    public static TaskPerformanceWidth Classify(double contentWidth) =>
        !double.IsFinite(contentWidth) || contentWidth >= WideFrom ? TaskPerformanceWidth.Wide
        : contentWidth >= MediumFrom ? TaskPerformanceWidth.Medium
        : TaskPerformanceWidth.Narrow;

    /// <summary>The comparison's centre column between the two sides; 0 in the narrow layout.</summary>
    public static double CenterColumn(TaskPerformanceWidth width) => width switch
    {
        TaskPerformanceWidth.Wide => 140,
        TaskPerformanceWidth.Medium => 120,
        _ => 0,
    };

    /// <summary>A side header's distribution width; the narrow layout's fills its card (<paramref name="cardContent"/>).</summary>
    public static double HistogramWidth(TaskPerformanceWidth width, double cardContent = 0) => width switch
    {
        TaskPerformanceWidth.Wide => 120,
        TaskPerformanceWidth.Medium => 96,
        _ => Math.Max(0, cardContent),
    };

    /// <summary>The model table merges tasks and scored into one column (<c>96 / 128</c>).</summary>
    public static bool MergesCounts(TaskPerformanceWidth width) => width == TaskPerformanceWidth.Medium;

    /// <summary>The model table becomes a list of rows.</summary>
    public static bool ListsModels(TaskPerformanceWidth width) => width == TaskPerformanceWidth.Narrow;

    /// <summary>The pickers take a line of their own under the title, sharing it equally.</summary>
    public static bool PickersOnOwnLine(TaskPerformanceWidth width) => width == TaskPerformanceWidth.Narrow;

    /// <summary>Each bin's bar width over <paramref name="width"/> with 2 DIP between bars.</summary>
    public static double BinWidth(double width) => Math.Max(0, (width - HistogramGap * (TaskStats.Bins - 1)) / TaskStats.Bins);

    /// <summary>A bin's bar height in a plot of <paramref name="plotHeight"/>: empty bins draw nothing.</summary>
    public static double BinHeight(int count, int maxCount, double plotHeight) =>
        count <= 0 || maxCount <= 0 ? 0 : Math.Max(HistogramMinBar, (double)count / maxCount * plotHeight);
}
