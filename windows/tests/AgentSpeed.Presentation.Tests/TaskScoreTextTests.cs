using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;
using static AgentSpeed.Presentation.Tests.TestSupport.TaskData;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The task score's numbers and words (DESIGN 12, <i>Values and forms</i>, <i>Accessibility</i>; METRICS 6.9, <i>Score</i>).</summary>
public class TaskScoreTextTests
{
    [Theory]
    [InlineData(98.75, 99)]
    [InlineData(44.5, 45)]
    [InlineData(0.5, 1)]
    [InlineData(0.49, 0)]
    [InlineData(100.4, 100)]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    public void AScoreShowsAsAnIntegerRoundedHalfAwayFromZero(double score, int shown) => Assert.Equal(shown, TaskScoreText.Round(score));

    [Fact]
    public void NoneIsTheEnDashNeverZero()
    {
        Assert.Equal("–", TaskScoreText.Number(null));
        Assert.Equal("0", TaskScoreText.Number(0));
        Assert.Null(TaskScoreFacts.None.Shown);
        Assert.Null((Passing with { Form = TaskForm.None }).Shown);
    }

    [Fact]
    public void TheScoreWordsWrapUnprovenThenIdleThenTheHighestOfN()
    {
        var loc = Loc();
        Assert.Equal("86，4 個 agent 中最高", TaskScoreText.Words(86, TaskState.Passing, TaskForm.Live, 4, loc));
        Assert.Equal("9，尚未驗證", TaskScoreText.Words(9, TaskState.Unverified, TaskForm.Live, 1, loc));
        Assert.Equal("72，閒置中，2 個 agent 中最高", TaskScoreText.Words(72, TaskState.Passing, TaskForm.Resting, 2, loc));
        Assert.Equal("34，待重新驗證，閒置中", TaskScoreText.Words(34, TaskState.Pending, TaskForm.Resting, 0, loc));
        Assert.Equal("智力 9，尚未驗證", TaskScoreText.AgentLabel(Unverified, 1, loc));
        Assert.Equal(string.Empty, TaskScoreText.AgentLabel(TaskScoreFacts.None, 3, loc));
        Assert.Equal("intelligence 72, idle, the highest of 2 agents", TaskScoreText.AgentLabel(Resting, 2, Loc(LanguageCodes.En)));
    }

    [Theory]
    [InlineData(0.004, "<1%")]
    [InlineData(0.005, "1%")]
    [InlineData(0.5245, "52%")]
    [InlineData(0.525, "53%")]
    [InlineData(0.9949, "99%")]
    [InlineData(0.995, ">99%")]
    [InlineData(1.2, ">99%")]
    public void APredictionReadsAsAWholePercentWithClamps(double p, string text) => Assert.Equal(text, TaskScoreText.Percent(p));

    [Fact]
    public void AClampedPredictionIsSpokenInWords()
    {
        var loc = Loc();
        Assert.Equal("不到 1%", TaskScoreText.PercentWords(0.001, loc));
        Assert.Equal("超過 99%", TaskScoreText.PercentWords(0.999, loc));
        Assert.Equal("41%", TaskScoreText.PercentWords(0.41, loc));
        Assert.Equal("over 99%", TaskScoreText.PercentWords(1, Loc(LanguageCodes.En)));
    }

    [Fact]
    public void TheStatusLineKeepsTheSuiteNameAsItsOwnRun()
    {
        var runs = TaskScoreText.Status(Failing, Loc());
        Assert.Equal([new TaskTextRun("cargo test", true), new TaskTextRun("：還有 3 個錯誤（最多時 9 個）")], runs);
        Assert.Equal("cargo test: 3 errors left (peak 9)", TaskScoreText.Plain(TaskScoreText.Status(Failing, Loc(LanguageCodes.En))));
    }

    [Fact]
    public void EveryStatusNamesItsCauseInEveryLanguage()
    {
        TaskScoreFacts[] cases =
        [
            TaskScoreFacts.None with { Form = TaskForm.Live }, Unverified, Failing, Passing, Pending,
            Failing with { OpenFailure = new TaskOpenFailure(null, TaskCheckClass.Build, 1, 1, Counted: false, Edited: false) },
            Failing with { OpenFailure = Failing.OpenFailure! with { Edited = true } },
            Passing with { LastPass = new TaskLastPass("cargo build", TaskCheckClass.Build, BuildOnly: true) },
        ];
        foreach (var language in LanguageCodes.All)
        {
            var loc = Loc(language);
            foreach (var facts in cases)
            {
                var runs = TaskScoreText.Status(facts, loc);
                var text = TaskScoreText.Plain(runs);
                Assert.DoesNotContain("task.", text, StringComparison.Ordinal);
                Assert.DoesNotContain("%", text, StringComparison.Ordinal);
                Assert.DoesNotContain("\u0001", text, StringComparison.Ordinal);
                var suite = facts.OpenFailure?.Suite ?? (facts.State is TaskState.Passing or TaskState.Pending ? facts.LastPass?.Suite : null);
                Assert.Equal(suite is null ? 0 : 1, runs.Count(r => r.IsCode));
                if (suite is not null)
                {
                    Assert.Equal(suite, runs.Single(r => r.IsCode).Text);
                }
            }
        }
    }

    [Fact]
    public void AStatusWithoutASuiteNamesTheCheckByItsClass()
    {
        var loc = Loc();
        var noCount = Failing with { OpenFailure = new TaskOpenFailure(null, TaskCheckClass.Build, 1, 1, Counted: false, Edited: false) };
        Assert.Equal("建置：沒有通過", TaskScoreText.Plain(TaskScoreText.Status(noCount, loc)));
        var edited = Failing with { OpenFailure = Failing.OpenFailure! with { Edited = true } };
        Assert.Equal("cargo test：失敗後已修改，等待重新執行", TaskScoreText.Plain(TaskScoreText.Status(edited, loc)));
        var buildOnly = Passing with { LastPass = new TaskLastPass(null, TaskCheckClass.Check, BuildOnly: true) };
        Assert.Equal("檢查：通過，但還沒有執行測試", TaskScoreText.Plain(TaskScoreText.Status(buildOnly, loc)));
        Assert.Equal("pytest：通過後又修改，等待重新執行", TaskScoreText.Plain(TaskScoreText.Status(Pending, loc)));
        Assert.Equal("已修改，但還沒有執行測試、建置或檢查", TaskScoreText.Plain(TaskScoreText.Status(Unverified, loc)));
    }

    [Fact]
    public void TheDimensionsAddUpToTheShownScore()
    {
        Assert.Equal([0, 0, 0, 9, 0], TaskDimensionPoints.Split(Unverified.Dimensions, 9));
        Assert.Equal([0, 25, 15, 5, 0], TaskDimensionPoints.Split(new TaskDimensions(0, 25, 15, 5, 0), 45));
        Assert.Equal([40, 25, 15, 9, 10], TaskDimensionPoints.Split(new TaskDimensions(40, 25, 15, 8.75, 10), 99));
        Assert.Equal([0, 17, 9, 7, 8], TaskDimensionPoints.Split(Failing.Dimensions, 41));

        // Equal fractions: the missing point goes to the first in V C R E K.
        Assert.Equal([11, 10, 0, 0, 0], TaskDimensionPoints.Split(new TaskDimensions(10.5, 10.5, 0, 0, 0), 21));
    }

    [Fact]
    public void SplitDimensionsAlwaysMatchTheShownTotalAndStayInRange()
    {
        var random = new Random(20261028);
        for (var i = 0; i < 2000; i++)
        {
            var dims = new TaskDimensions(random.NextDouble() * 40, random.NextDouble() * 25, random.NextDouble() * 15, random.NextDouble() * 10,
                random.NextDouble() * 10);
            var shown = TaskScoreText.Round(dims.Sum);
            var points = TaskDimensionPoints.Split(dims, shown);
            Assert.Equal(shown, points.Sum());
            for (var k = 0; k < points.Count; k++)
            {
                var dimension = TaskDimensions.Order[k];
                Assert.InRange(points[k], (int)Math.Floor(dims[dimension]), Math.Min(TaskDimensions.Max(dimension), (int)Math.Ceiling(dims[dimension])));
            }
        }
    }
}
