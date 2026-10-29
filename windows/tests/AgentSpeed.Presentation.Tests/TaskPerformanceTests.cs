using AgentSpeed.Core;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Presentation.Localization;
using static AgentSpeed.Presentation.Tests.TestSupport.TaskData;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The Usage History window's task performance section (DESIGN 12, <i>Usage History: task performance</i>; METRICS 6.11).</summary>
public class TaskPerformanceTests
{
    private static TaskComparison Both => Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode));

    [Theory]
    [InlineData(920, TaskPerformanceWidth.Wide)]
    [InlineData(860, TaskPerformanceWidth.Wide)]
    [InlineData(859.5, TaskPerformanceWidth.Medium)]
    [InlineData(672, TaskPerformanceWidth.Medium)]
    [InlineData(600, TaskPerformanceWidth.Medium)]
    [InlineData(599, TaskPerformanceWidth.Narrow)]
    [InlineData(358, TaskPerformanceWidth.Narrow)]
    public void TheContentWidthPicksTheLayout(double width, TaskPerformanceWidth expected) =>
        Assert.Equal(expected, TaskPerformanceLayout.Classify(width));

    [Fact]
    public void EachLayoutSizesItsCentreDistributionsAndTable()
    {
        Assert.Equal((140.0, 120.0), (TaskPerformanceLayout.CenterColumn(TaskPerformanceWidth.Wide), TaskPerformanceLayout.HistogramWidth(TaskPerformanceWidth.Wide)));
        Assert.Equal((120.0, 96.0), (TaskPerformanceLayout.CenterColumn(TaskPerformanceWidth.Medium), TaskPerformanceLayout.HistogramWidth(TaskPerformanceWidth.Medium)));
        Assert.Equal(150, TaskPerformanceLayout.HistogramWidth(TaskPerformanceWidth.Narrow, 150));
        Assert.True(TaskPerformanceLayout.MergesCounts(TaskPerformanceWidth.Medium));
        Assert.False(TaskPerformanceLayout.MergesCounts(TaskPerformanceWidth.Wide));
        Assert.True(TaskPerformanceLayout.ListsModels(TaskPerformanceWidth.Narrow));
        Assert.True(TaskPerformanceLayout.PickersOnOwnLine(TaskPerformanceWidth.Narrow));
        Assert.Equal(10.2, TaskPerformanceLayout.BinWidth(120), 9);
        Assert.Equal(0, TaskPerformanceLayout.BinHeight(0, 10, 18));
        Assert.Equal(1.5, TaskPerformanceLayout.BinHeight(1, 1000, 18));
        Assert.Equal(18, TaskPerformanceLayout.BinHeight(10, 10, 18));
    }

    [Fact]
    public void CodexAndClaudeCodeFaceEachOtherByDefault()
    {
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode), TaskPerformancePair.Default(Both));

        var noCodex = Comparison((SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Omp, Pi), (SourceKind.Pi, Codex));
        Assert.Equal(new TaskPair(SourceKind.ClaudeCode, SourceKind.Pi), TaskPerformancePair.Default(noCodex));
        Assert.Null(TaskPerformancePair.Default(Comparison((SourceKind.Codex, Codex))));
    }

    [Fact]
    public void TheRememberedPairStaysWhileOneSideHasRecords()
    {
        var comparison = Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Pi, Pi));
        var remembered = new TaskPair(SourceKind.Pi, SourceKind.Droid);
        Assert.Equal(remembered, TaskPerformancePair.Resolve(comparison, remembered));
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode),
            TaskPerformancePair.Resolve(comparison, new TaskPair(SourceKind.Omp, SourceKind.Droid)));
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode),
            TaskPerformancePair.Resolve(comparison, new TaskPair(SourceKind.Codex, SourceKind.Codex)));
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode),
            TaskPerformancePair.Resolve(comparison, new TaskPair(SourceKind.Codex, SourceKind.Gemini)));
    }

    [Fact]
    public void EachPickerLeavesOutTheOthersChoice()
    {
        var comparison = Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Pi, Pi));
        Assert.Equal([SourceKind.Codex, SourceKind.Pi], TaskPerformancePair.Options(comparison, SourceKind.Codex, SourceKind.ClaudeCode));
        Assert.Equal([SourceKind.ClaudeCode, SourceKind.Pi], TaskPerformancePair.Options(comparison, SourceKind.ClaudeCode, SourceKind.Codex));

        // A remembered source without records stays listed as its picker's choice.
        Assert.Equal([SourceKind.Codex, SourceKind.ClaudeCode, SourceKind.Droid],
            TaskPerformancePair.Options(comparison, SourceKind.Droid, SourceKind.Pi));

        var pair = new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode);
        Assert.Equal(new TaskPair(SourceKind.Pi, SourceKind.ClaudeCode), TaskPerformancePair.Pick(pair, true, SourceKind.Pi));
        Assert.Equal(new TaskPair(SourceKind.ClaudeCode, SourceKind.Codex), TaskPerformancePair.Pick(pair, true, SourceKind.ClaudeCode));
        Assert.Equal(new TaskPair(SourceKind.ClaudeCode, SourceKind.Codex), TaskPerformancePair.Pick(pair, false, SourceKind.Codex));
    }

    [Fact]
    public void TheSectionPicksItsStateFromTheRecords()
    {
        var loc = Loc();
        Assert.Equal(TaskPerformanceMode.Analyzing, TaskPerformanceView.Build(new(TaskComparison.Empty, Analyzing: true), 900, loc).Mode);
        var empty = TaskPerformanceView.Build(new(TaskComparison.Empty), 900, loc);
        Assert.Equal(TaskPerformanceMode.Empty, empty.Mode);
        Assert.Equal("這段期間沒有評分的任務", empty.EmptyText);
        Assert.Contains("Factory Droid", empty.EmptyHint, StringComparison.Ordinal);
        Assert.Equal("正在重新分析過去的任務…", empty.AnalyzingText);

        var pair = TaskPerformanceView.Build(new(Both), 900, loc);
        Assert.Equal((TaskPerformanceMode.Pair, TaskPerformanceWidth.Wide), (pair.Mode, pair.Width));
        Assert.True(pair.ShowsPickers);
        Assert.Null(pair.OnlyOne);

        var only = TaskPerformanceView.Build(new(Comparison((SourceKind.Codex, Codex))), 900, loc);
        Assert.Equal(TaskPerformanceMode.Single, only.Mode);
        Assert.Equal("這段期間只有 Codex 有任務", only.OnlyOne);
        Assert.False(only.ShowsPickers);

        var chosen = TaskPerformanceView.Build(new(Both, SelectedSource: SourceKind.ClaudeCode), 700, loc);
        Assert.Equal((TaskPerformanceMode.Single, SourceKind.ClaudeCode), (chosen.Mode, chosen.Single!.Source));
        Assert.Null(chosen.OnlyOne);
        Assert.Equal(5, chosen.SingleRows.Count);
        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5"], chosen.Models.Rows.Select(r => r.Model));

        Assert.Equal(TaskPerformanceMode.Empty, TaskPerformanceView.Build(new(Both, SelectedSource: SourceKind.Gemini), 900, loc).Mode);
    }

    [Fact]
    public void ASideHeaderShowsCountsMedianAndDistribution()
    {
        var side = TaskPerformanceView.Side(SourceKind.Codex, Codex, Loc());
        Assert.Equal("Codex", side.Name);
        Assert.Equal("96 個已評分 · 共 128 個任務", side.Counts);
        Assert.Equal("71", side.Median);
        Assert.Equal("中位數", side.MedianCaption);
        Assert.False(side.LowSample);
        Assert.Equal(7, side.Histogram.MedianBin);
        Assert.Equal(0.712, side.Histogram.MedianAt!.Value, 9);
        Assert.Equal(22, side.Histogram.MaxCount);
        Assert.Equal("Codex：128 個任務，96 個已評分，中位數 71", side.Spoken);
        Assert.Equal("分數分布：四分之一的任務低於 49，一半低於 71，四分之三低於 87", side.Histogram.Spoken);
    }

    [Fact]
    public void ASmallSampleCarriesItsChipAndAnEmptySideSaysSo()
    {
        var loc = Loc();
        var small = TaskPerformanceView.Side(SourceKind.Pi, Pi, loc);
        Assert.True(small.LowSample);
        Assert.Equal("樣本少", small.LowSampleText);
        Assert.Equal("只有 4 個已評分的任務，數字容易波動；雙方都有 10 個以上才會標出較好的一方。", small.LowSampleHint);
        Assert.EndsWith("樣本少", small.Spoken, StringComparison.Ordinal);

        var empty = TaskPerformanceView.Side(SourceKind.Droid, TaskStats.Empty, loc);
        Assert.True(empty.IsEmpty);
        Assert.False(empty.LowSample);
        Assert.Equal("–", empty.Median);
        Assert.Equal(-1, empty.Histogram.MedianBin);
        Assert.Equal("這段期間沒有任務", empty.EmptyText);
        Assert.Equal("Factory Droid，這段期間沒有任務", empty.Spoken);
    }

    [Theory]
    [InlineData(9.999, 0)]
    [InlineData(10, 1)]
    [InlineData(89.99, 8)]
    [InlineData(90, 9)]
    [InlineData(100, 9)]
    [InlineData(0, 0)]
    public void AScoresBinCountsTheEdgesItReaches(double score, int bin) => Assert.Equal(bin, TaskHistogram.BinOf(score));

    [Fact]
    public void TheButterflyMarksTheBetterSideOnlyWithEnoughTasks()
    {
        var view = TaskPerformanceView.Build(new(Both), 900, Loc());
        Assert.Equal(new TaskPair(SourceKind.Codex, SourceKind.ClaudeCode), view.Pair);
        var rows = view.Rows.ToDictionary(r => r.Metric);

        var success = rows[TaskMetric.Success];
        Assert.Equal(("78%", "69%", 1), (success.A.Text, success.B.Text, success.Better));
        Assert.Equal((TaskEmphasis.Better, TaskEmphasis.Other), (success.A.Emphasis, success.B.Emphasis));
        Assert.Equal("75 / 96 個已評分的任務", success.A.Tooltip);
        Assert.Equal(0.78125, success.A.Fraction, 9);
        Assert.False(success.LowerIsBetter);
        Assert.Equal("成功率：Codex 78%，Claude Code 69%，Codex 較好", success.Spoken);

        var stall = rows[TaskMetric.Stall];
        Assert.Equal(("10%", "16%", 1), (stall.A.Text, stall.B.Text, stall.Better));
        Assert.Equal("13 / 128 個任務", stall.A.Tooltip);
        Assert.Equal("卡住率，越低越好：Codex 10%，Claude Code 16%，Codex 較好", stall.Spoken);
        Assert.Equal("越低越好", stall.LowerBetterText);

        Assert.Equal("25 / 100 次失敗後的下一次執行", rows[TaskMetric.FixFail].A.Tooltip);

        var attempts = rows[TaskMetric.Attempts];
        Assert.Equal(("2.0 次", "1.4 次", 2), (attempts.A.Text, attempts.B.Text, attempts.Better));
        Assert.Equal(2.04 / 3, attempts.A.Fraction, 9);

        var firstPass = rows[TaskMetric.FirstPass];
        Assert.Equal(("5 分鐘", "3 分鐘", 2), (firstPass.A.Text, firstPass.B.Text, firstPass.Better));
        Assert.Equal(300_000 / 375_000.0, firstPass.A.Fraction, 9);
    }

    [Fact]
    public void SmallOrCloseSamplesCrownNoWinner()
    {
        Assert.Equal(0, TaskMetrics.Better(TaskMetric.Success, 0.9, 0.5, 9, 100));
        Assert.Equal(0, TaskMetrics.Better(TaskMetric.Success, 0.785, 0.776, 50, 50));
        Assert.Equal(1, TaskMetrics.Better(TaskMetric.Success, 0.786, 0.776, 50, 50));
        Assert.Equal(0, TaskMetrics.Better(TaskMetric.Attempts, 2.0, 1.92, 50, 50));
        Assert.Equal(2, TaskMetrics.Better(TaskMetric.Attempts, 2.0, 1.9, 50, 50));
        Assert.Equal(0, TaskMetrics.Better(TaskMetric.FirstPass, null, 1000, 50, 50));

        // Two sides that both read 5 分鐘 are not ranked, although they are 5 % apart.
        Assert.Equal(2, TaskMetrics.Better(TaskMetric.FirstPass, 300_000, 285_000, 50, 50));
        Assert.Equal(0, TaskMetrics.Better(TaskMetric.FirstPass, 300_000, 285_000, 50, 50, "5 分鐘", "5 分鐘"));
        var close = Comparison((SourceKind.Codex, Codex with { TimeToFirstPassMs = 300_000 }), (SourceKind.ClaudeCode, ClaudeCode with { TimeToFirstPassMs = 285_000 }));
        var firstPass = TaskPerformanceView.Build(new(close), 900, Loc()).Rows.Single(r => r.Metric == TaskMetric.FirstPass);
        Assert.Equal(("5 分鐘", "5 分鐘", 0), (firstPass.A.Text, firstPass.B.Text, firstPass.Better));

        var view = TaskPerformanceView.Build(new(Comparison((SourceKind.Codex, Codex), (SourceKind.Pi, Pi))), 900, Loc());
        Assert.All(view.Rows, r => Assert.Equal(0, r.Better));
        Assert.All(view.Rows, r => Assert.Equal(TaskEmphasis.Plain, r.A.Emphasis));
    }

    [Fact]
    public void ARatesTooltipNeedsItsNumerator()
    {
        var withoutCounts = Codex with { Successes = null, Stalls = null, FixFailures = null };
        var view = TaskPerformanceView.Build(new(Comparison((SourceKind.Codex, withoutCounts), (SourceKind.ClaudeCode, ClaudeCode))), 900, Loc());
        var rows = view.Rows.ToDictionary(r => r.Metric);
        Assert.Equal("78%", rows[TaskMetric.Success].A.Text);
        Assert.Null(rows[TaskMetric.Success].A.Tooltip);
        Assert.Null(rows[TaskMetric.Stall].A.Tooltip);
        Assert.Null(rows[TaskMetric.FixFail].A.Tooltip);
        Assert.Equal("118 / 171 個已評分的任務", rows[TaskMetric.Success].B.Tooltip);
    }

    [Fact]
    public void TheInfoButtonHasItsOwnLabel() =>
        Assert.Equal("這些數字怎麼算", TaskPerformanceView.Build(new(Both), 900, Loc()).InfoLabel);

    [Fact]
    public void AMissingValueShowsTheDashAndSpeaksWhy()
    {
        var view = TaskPerformanceView.Build(new(Comparison((SourceKind.Codex, Codex), (SourceKind.Pi, Pi))), 900, Loc());
        var attempts = view.Rows.Single(r => r.Metric == TaskMetric.Attempts);
        Assert.Equal(("–", 0.0, true), (attempts.B.Text, attempts.B.Fraction, attempts.B.IsNull));
        Assert.Equal("這段期間沒有出錯後恢復的任務", attempts.B.Tooltip);
        Assert.Contains("Pi 這段期間沒有出錯後恢復的任務", attempts.Spoken, StringComparison.Ordinal);
        Assert.Equal("這段期間沒有通過檢查的任務", view.Rows.Single(r => r.Metric == TaskMetric.FirstPass).B.Tooltip);
        Assert.Equal(3, TaskMetrics.Scale(TaskMetric.Attempts, 1.2, null));
        Assert.Equal(300_000, TaskMetrics.Scale(TaskMetric.FirstPass, 60_000, 90_000));
        Assert.Equal(1, TaskMetrics.Scale(TaskMetric.Stall, 0.2, 0.4));

        var oneSideEmpty = TaskPerformanceView.Build(new(Both, RememberedPair: new TaskPair(SourceKind.Codex, SourceKind.Droid)), 900, Loc());
        Assert.True(oneSideEmpty.SideB!.IsEmpty);
        Assert.All(oneSideEmpty.Rows, r => Assert.Equal(("–", 0.0), (r.B.Text, r.B.Fraction)));
    }

    [Theory]
    [InlineData(45_000, "45 秒")]
    [InlineData(59_400, "59 秒")]
    [InlineData(59_600, "1 分鐘")]
    [InlineData(89_500, "1 分鐘")]
    [InlineData(90_000, "2 分鐘")]
    [InlineData(180_000, "3 分鐘")]
    [InlineData(3_570_000, "1 小時")]
    [InlineData(3_660_000, "1 小時 1 分鐘")]
    [InlineData(7_200_000, "2 小時")]
    public void TimesReadInSecondsMinutesOrHours(double ms, string text) => Assert.Equal(text, TaskPerformanceText.Duration(ms, Loc()));

    [Fact]
    public void RatesAndAttemptsUseTheirFormats()
    {
        Assert.Equal("–", TaskPerformanceText.Rate(null));
        Assert.Equal("13%", TaskPerformanceText.Rate(0.125));
        Assert.Equal("15%", TaskPerformanceText.Rate(29 / 200.0));
        Assert.Equal("0%", TaskPerformanceText.Rate(0.003));
        Assert.Equal("1.4", TaskPerformanceText.Attempts(1.42, Loc(LanguageCodes.En)));
        Assert.Equal("2.0 次", TaskPerformanceText.Attempts(2.04, Loc()));
        Assert.Equal("1 hr 1 min", TaskPerformanceText.Duration(3_660_000, Loc(LanguageCodes.En)));
    }

    [Fact]
    public void ModelsSortByTasksThenTheIdsBytes()
    {
        TaskModelStats M(string id, int tasks, double? median = 50) => new(id, [SourceKind.Codex], Stats(tasks, tasks, median));
        TaskModelStats[] models = [M("b", 5), M("a", 5), M("Z", 5), M("c", 9, null), M("é", 5)];
        Assert.Equal(["c", "Z", "a", "b", "é"], TaskModelTable.Sort(models, TaskModelColumn.Tasks, true).Select(m => m.Model));
        Assert.Equal(["Z", "a", "b", "c", "é"], TaskModelTable.Sort(models, TaskModelColumn.Model, false).Select(m => m.Model));
        Assert.Equal("c", TaskModelTable.Sort(models, TaskModelColumn.Median, true)[^1].Model);
        Assert.Equal("c", TaskModelTable.Sort(models, TaskModelColumn.Median, false)[^1].Model);
        Assert.True(TaskModelTable.FirstDirection(TaskModelColumn.Success));
        Assert.False(TaskModelTable.FirstDirection(TaskModelColumn.Model));
    }

    [Fact]
    public void TheModelTableNamesItsColumnsAndMergesCountsWhenMedium()
    {
        var loc = Loc();
        var wide = TaskModelTable.Build(Models, loc, TaskPerformanceWidth.Wide);
        Assert.Equal(["模型", "任務", "已評分", "中位數", "成功率", "卡住率", "修復失敗率", "恢復嘗試", "首次通過"], wide.Headers.Select(h => h.Text));
        Assert.Equal("已評分任務", wide.Headers[2].FullName);
        Assert.Equal("首次通過時間（中位數）", wide.Headers[^1].FullName);
        var medium = TaskModelTable.Build(Models, loc, TaskPerformanceWidth.Medium);
        Assert.Equal(8, medium.Headers.Count);
        Assert.Equal(("已評分 / 任務", true), (medium.Headers[1].Text, medium.Headers[1].Merged));
        Assert.Equal(["claude-opus-5-5", "claude-sonnet-5-5", "gpt-6.1-sol", "gpt-6.1-terra", "claude-fable-5-1"], wide.Rows.Select(r => r.Model));
    }

    [Fact]
    public void AModelRowCarriesDotsStripChipAndItsWords()
    {
        var loc = Loc();
        var table = TaskModelTable.Build(Models, loc, TaskPerformanceWidth.Wide);
        var opus = table.Rows[0];
        Assert.Equal([SourceKind.ClaudeCode, SourceKind.Omp], opus.Sources);
        Assert.Equal("來源：Claude Code、omp", opus.SourcesTooltip);
        Assert.Equal(("137", "113", "113 / 137", "70"), (opus.Tasks, opus.Scored, opus.ScoredOfTasks, opus.Median));
        var strip = opus.Strip!.Value;
        Assert.Equal((0.621, 0.783, 0.704), (Math.Round(strip.Lower, 9), Math.Round(strip.Upper, 9), Math.Round(strip.Median, 9)));
        Assert.Equal(SourceKind.ClaudeCode, strip.Color);
        Assert.Equal("70，中間一半的任務在 62 到 78 分之間", opus.MedianSpoken);
        Assert.Equal(["71%", "14%", "30%", "1.3 次", "3 分鐘"], opus.Rates.Select(r => r.Text));
        Assert.False(opus.LowSample);
        Assert.Equal("成功率 71% · 卡住率 14% · 修復失敗率 30%", opus.Line2);
        Assert.Equal("113 個已評分 · 共 137 個任務 · 恢復嘗試 1.3 次 · 首次通過 3 分鐘", opus.Line3);
        Assert.Equal("claude-opus-5-5\t137\t113\t70\t71%\t14%\t30%\t1.3 次\t3 分鐘", opus.CopyText);
        Assert.StartsWith("claude-opus-5-5，來源：Claude Code、omp，任務：137，已評分任務：113，中位數分數：70，中間一半的任務在 62 到 78 分之間，成功率：71%",
            opus.Spoken, StringComparison.Ordinal);

        var fable = table.Rows[^1];
        Assert.True(fable.LowSample);
        Assert.All(fable.Rates, r => Assert.Equal(TaskEmphasis.Other, r.Emphasis));
        Assert.EndsWith("樣本少", fable.Spoken, StringComparison.Ordinal);
        Assert.Equal("樣本少", table.LowSampleText);
    }

    [Fact]
    public void AnUnknownModelReadsAsNoModelMetadata()
    {
        var table = TaskModelTable.Build([new TaskModelStats(string.Empty, [SourceKind.Pi], Pi)], Loc(), TaskPerformanceWidth.Wide);
        Assert.True(table.Rows[0].IsUnknown);
        Assert.Equal("無模型中繼資料", table.Rows[0].Name);
    }

    [Fact]
    public void TheInfoPopoverDefinesEveryMetric()
    {
        var terms = TaskPerformanceView.InfoTermsOf(Loc());
        Assert.Equal(7, terms.Count);
        Assert.Equal(("已評分任務", "有修改或有驗證、因此有分數的任務。只讀程式碼或聊天的任務沒有分數。"), (terms[0].Term, terms[0].Hint));
        Assert.Equal("首次通過時間（中位數）", terms[^1].Term);
    }

    [Fact]
    public void TheSectionIsWordedInEveryLanguage()
    {
        foreach (var language in LanguageCodes.All)
        {
            var loc = Loc(language);
            foreach (var width in new[] { 900.0, 700, 400 })
            {
                var view = TaskPerformanceView.Build(new(Comparison((SourceKind.Codex, Codex), (SourceKind.ClaudeCode, ClaudeCode), (SourceKind.Pi, Pi))), width, loc);
                string[] texts =
                [
                    view.Title, view.Info, view.Versus, view.PickA, view.PickB, view.ByModel, view.Footnote, view.SideA!.Counts, view.SideA.Spoken,
                    view.SideA.Histogram.Spoken, view.SideB!.LowSampleHint,
                    .. view.Rows.SelectMany(r => new[] { r.Name, r.Hint, r.LowerBetterText, r.Spoken, r.A.Text, r.A.Tooltip ?? string.Empty }),
                    .. view.Models.Headers.SelectMany(h => new[] { h.Text, h.FullName }),
                    .. view.Models.Rows.SelectMany(r => new[] { r.Spoken, r.Line2, r.Line3, r.SourcesTooltip }),
                    .. view.InfoTerms.SelectMany(t => new[] { t.Term, t.Hint }),
                ];
                foreach (var text in texts)
                {
                    Assert.DoesNotContain("history.", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("a11y.", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("%@", text, StringComparison.Ordinal);
                    Assert.DoesNotContain("%d", text, StringComparison.Ordinal);
                }
            }
        }
    }
}
