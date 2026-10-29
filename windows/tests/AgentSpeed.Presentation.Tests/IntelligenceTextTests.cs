using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Tasks;
using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb.Lines;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The 智力 and work-signal wording of orbs and agent rows (DESIGN 4, 10; METRICS 6.9 task score).</summary>
public class IntelligenceTextTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly Localizer ZhHant = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    [Fact]
    public void TheOrbTooltipAndLabelReadTheScoreAndThinking()
    {
        var sums = new IntelligenceSums(Output: 1200, Thinking: 528, ThinkingBasis: 1200) { Score = Live(74.25) };
        var content = OrbLinesText.Intelligence(TaskSetReading.Of(sums, []), sums, generating: false, ZhHant).Content!;
        Assert.Equal("任務進展的估算，不是模型能力測驗", content.Detail);
        Assert.Equal("智力 74，思考每秒 176 個 token", content.Spoken);
    }

    [Fact]
    public void ASetWithoutAScoreShowsADashOnlyWhileItsAgentsGenerate()
    {
        var sums = new IntelligenceSums(Output: 1200, Thinking: 528, ThinkingBasis: 1200);
        var generating = OrbLinesText.Intelligence(TaskSetReading.None, sums, generating: true, En).Content!;
        Assert.Equal(("–", "176"), (generating.First.Number, generating.Second.Number));
        Assert.Equal("intelligence: no score yet, thinking 176 tokens per second", generating.Spoken);
        Assert.Null(OrbLinesText.Intelligence(TaskSetReading.None, sums, generating: false, En).Content);
    }

    [Theory]
    [InlineData(LanguageCodes.ZhHant, "智力 9，尚未驗證，3 個 agent 中最高", "智力 72，閒置中")]
    [InlineData(LanguageCodes.ZhHans, "智力 9，尚未验证，3 个 agent 中最高", "智力 72，空闲中")]
    [InlineData(LanguageCodes.En, "intelligence 9, not verified, the highest of 3 agents", "intelligence 72, idle")]
    [InlineData(LanguageCodes.Ja, "知能 9、未検証、3 個の agent の中で最高", "知能 72、待機中")]
    public void TheOrbsWordsCarryTheProofAndTheRestingForm(string language, string unverified, string resting)
    {
        var localizer = new Localizer(Localizer.LoadEmbeddedTable(), language);
        var score = new TaskSetReading(TaskForm.Live, 9, Presentation.TaskScore.TaskState.Unverified, 3, null);
        Assert.Equal(unverified, OrbLinesText.Intelligence(score, IntelligenceSums.Zero, true, localizer).Content!.SpokenAlone);
        var pale = new TaskSetReading(TaskForm.Resting, 72, Presentation.TaskScore.TaskState.Passing, 1, null);
        Assert.Equal(resting, OrbLinesText.Intelligence(pale, IntelligenceSums.Zero, false, localizer).Content!.SpokenAlone);
    }

    [Theory]
    [InlineData(LanguageCodes.ZhHant, "智力", "思考")]
    [InlineData(LanguageCodes.ZhHans, "智力", "思考")]
    [InlineData(LanguageCodes.En, "IQ", "TH")]
    [InlineData(LanguageCodes.Ja, "知", "思考")]
    public void TheOrbReadoutsGlyphsNameIntelligenceAndThinking(string language, string intelligence, string thinking)
    {
        var localizer = new Localizer(Localizer.LoadEmbeddedTable(), language);
        var sums = new IntelligenceSums(Output: 300) { Score = Live(92) };
        var content = OrbLinesText.Intelligence(TaskSetReading.Of(sums, []), sums, generating: true, localizer).Content!;
        Assert.Equal((intelligence, "92", string.Empty), (content.First.Glyph, content.First.Number, content.First.Unit));
        Assert.Equal((thinking, localizer["unit.tokS"]), (content.Second.Glyph, content.Second.Unit));
    }

    [Fact]
    public void LoopingLeadsTheTooltipAndTheSummaryClosesIt()
    {
        var looping = new WorkSignals(Looping: 3, Repeating: null, Novelty: 0.5, Failed: 4, Recovered: 1);
        Assert.Equal(En.Format("work.looping", 3), WorkText.Looping(looping, En));
        Assert.Equal([En.Format("work.summary", "50%", 4, 1)], WorkText.Closing(looping, En));

        var waiting = new WorkSignals(Looping: null, Repeating: 4, Novelty: null, Failed: 0, Recovered: 0);
        Assert.Null(WorkText.Looping(waiting, En));
        Assert.Equal([En.Format("work.repeating", 4)], WorkText.Closing(waiting, En));
        Assert.Empty(WorkText.Closing(WorkSignals.None, En));
    }

    private static TaskSetValue Live(double score) => new(TaskValueForm.Live, score, TaskRounding.HalfAwayFromZero(score), "a");
}
