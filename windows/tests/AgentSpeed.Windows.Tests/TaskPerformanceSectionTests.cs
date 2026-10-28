using System.Windows;
using AgentSpeed.Core;
using AgentSpeed.History.Tasks;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Windows.Tests;

/// <summary>
/// DESIGN 12, <i>Usage History: task performance</i>, on the Windows side: the section loads and lays out in every
/// layout and state, its rows stay the same objects across updates (so values crossfade and bars spring), and the
/// by-model table's columns always fit the section.
/// </summary>
public class TaskPerformanceSectionTests
{
    private static readonly StringTable Table = Localizer.LoadEmbeddedTable();

    [Theory]
    [InlineData(920)]
    [InlineData(700)]
    [InlineData(420)]
    public void TheSectionLaysOutInEveryLayoutAndState(double width)
    {
        var heights = OnStaThread(() =>
        {
            using var model = new TaskPerformanceViewModel(new Localizer(Table, LanguageCodes.ZhHant));
            var section = new TaskPerformanceSection { DataContext = model };
            var shown = new List<double>();
            foreach (var (comparison, source, analyzing) in States())
            {
                model.Update(comparison, source, analyzing);
                section.Measure(new Size(width, double.PositiveInfinity));
                section.Arrange(new Rect(0, 0, width, section.DesiredSize.Height));
                model.SetContentWidth(width);
                section.Measure(new Size(width, double.PositiveInfinity));
                section.Arrange(new Rect(0, 0, width, section.DesiredSize.Height));
                shown.Add(section.DesiredSize.Height);
            }

            return shown;
        });
        Assert.All(heights, h => Assert.True(h > 0));
    }

    [Fact]
    public void RowsAndSidesStayTheSameObjectsAcrossUpdates()
    {
        using var model = new TaskPerformanceViewModel(new Localizer(Table, LanguageCodes.En));
        model.Update(Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode)), null, analyzing: false);
        var row = model.Rows[0];
        var side = model.SideA;
        var first = model.Models[0];
        var before = row.A;

        model.Update(Comparison((SourceKind.Codex, ClaudeCode), (SourceKind.ClaudeCode, Codex)), null, analyzing: false);

        Assert.Same(row, model.Rows[0]);
        Assert.Same(side, model.SideA);
        Assert.Contains(first, model.Models);
        Assert.NotEqual(before, row.A);
    }

    [Fact]
    public void APickRemembersThePairAndSortingMovesTheSameRows()
    {
        using var model = new TaskPerformanceViewModel(new Localizer(Table, LanguageCodes.En));
        TaskPair? saved = null;
        model.RememberPair(null, pair => saved = pair);
        model.Update(Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Pi, Small)), null, analyzing: false);
        Assert.Equal(TaskPerformanceMode.Pair, model.Mode);
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode), model.Pair);
        Assert.DoesNotContain(model.OptionsA, o => o.Source == SourceKind.ClaudeCode);

        model.Pick(first: true, SourceKind.Pi);
        Assert.Equal(new TaskPair(SourceKind.Pi, SourceKind.ClaudeCode), saved);
        Assert.Equal(new TaskPair(SourceKind.Pi, SourceKind.ClaudeCode), model.Pair);

        var rows = model.Models.ToArray();
        model.SortBy(TaskModelColumn.Model);
        Assert.False(model.SortDescending);
        Assert.Equal(rows.OrderBy(r => r.Model, StringComparer.Ordinal), model.Models);
        model.SortBy(TaskModelColumn.Model);
        Assert.True(model.SortDescending);
    }

    [Fact]
    public void TheWidthPicksTheLayout()
    {
        using var model = new TaskPerformanceViewModel(new Localizer(Table, LanguageCodes.En));
        model.Update(Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode)), null, analyzing: false);
        model.SetContentWidth(500);
        Assert.True(model.ShowsPairCards);
        Assert.True(model.ShowsList);
        model.SetContentWidth(700);
        Assert.True(model.ShowsButterfly);
        Assert.Contains(model.Headers, h => h.Merged);
        model.SetContentWidth(900);
        Assert.DoesNotContain(model.Headers, h => h.Merged);
        Assert.Equal(140, model.CenterColumn.Value);
    }

    [Theory]
    [InlineData(640, false)]
    [InlineData(900, true)]
    [InlineData(380, false)]
    public void TableColumnsFillTheWidthWithoutScrollingSideways(double available, bool wide)
    {
        TaskColumnNeed[] needs =
        [
            new(56, 90, 52, 40), new(72, 44, 44, 44), new(28, 76, 50, 50), new(28, 60, 32, 32),
            new(28, 96, 64, 64), new(20, 54, 54, 54), new(34, 62, 30, 30),
        ];
        var widths = TaskTableColumns.Fit(available, 150, needs, wide);
        Assert.Equal(needs.Length + 1, widths.Length);
        Assert.True(widths.Sum() <= available + 0.5);
        for (var i = 0; i < needs.Length; i++)
        {
            Assert.True(widths[i + 1] >= needs[i].Content + 2 * TaskTableColumns.CellPadding - 0.5);
        }
    }

    [Fact]
    public void ALongModelIdTakesTheAirBeforeTheNumbersDo()
    {
        var needs = Enumerable.Repeat(new TaskColumnNeed(73, 40, 40, 40), 8).ToArray();
        var widths = TaskTableColumns.Fit(900, 252, needs, wide: true);
        Assert.Equal(220, widths[0]);
        Assert.All(widths.Skip(1), w => Assert.Equal(73 + 2 * TaskTableColumns.CellPadding, w));
    }

    private static IEnumerable<(TaskComparison Comparison, SourceKind? Source, bool Analyzing)> States() =>
    [
        (TaskComparison.Empty, null, true),
        (TaskComparison.Empty, null, false),
        (Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Pi, Small)), null, false),
        (Comparison((SourceKind.Codex, Codex)), null, false),
        (Comparison((SourceKind.ClaudeCode, ClaudeCode)), SourceKind.ClaudeCode, false),
    ];

    private static TaskStats Codex { get; } = new(128, 96, 71.2, 48.6, 86.9, [2, 3, 4, 5, 6, 8, 12, 22, 20, 14], 75, 13, 25, 100, 2.04, 300_000);

    private static TaskStats ClaudeCode { get; } = new(214, 171, 65.9, 41.2, 79.5, [4, 6, 8, 10, 14, 22, 30, 36, 25, 16], 118, 34, 48, 150, 1.42, 180_000);

    private static TaskStats Small { get; } = new(6, 4, 52, 50, 55, [0, 0, 0, 1, 1, 1, 1, 0, 0, 0], 2, 1, 0, 0, null, null);

    private static TaskComparison Comparison(params (SourceKind Source, TaskStats Stats)[] sources) => new(
        [.. sources.Select(s => new TaskSourceStats(s.Source, s.Stats))],
        [
            new("claude-opus-5-5", [SourceKind.ClaudeCode], ClaudeCode),
            new("gpt-6.1-sol", [SourceKind.Codex], Codex),
            new(string.Empty, [SourceKind.Pi], Small),
        ]);

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Layout failed.", error);
    }
}
