using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Tasks;
using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb.Lines;

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
        var content = OrbLinesText.Intelligence(sums, generating: false, ZhHant).Content!;
        Assert.Equal("任務進展的估算，不是模型能力測驗", content.Detail);
        Assert.Equal("智力 74，思考每秒 176 個 token", content.Spoken);
    }

    [Fact]
    public void ASetWithoutAScoreHasNoIntelligenceLine()
    {
        var sums = new IntelligenceSums(Output: 1200, Thinking: 528, ThinkingBasis: 1200);
        Assert.Null(OrbLinesText.Intelligence(sums, generating: true, En).Content);
    }

    [Theory]
    [InlineData(LanguageCodes.ZhHant, "智力", "思考")]
    [InlineData(LanguageCodes.ZhHans, "智力", "思考")]
    [InlineData(LanguageCodes.En, "IQ", "TH")]
    [InlineData(LanguageCodes.Ja, "知", "思考")]
    public void TheOrbReadoutsGlyphsNameIntelligenceAndThinking(string language, string intelligence, string thinking)
    {
        var localizer = new Localizer(Localizer.LoadEmbeddedTable(), language);
        var content = OrbLinesText.Intelligence(new IntelligenceSums(Output: 300) { Score = Live(92) }, generating: true, localizer).Content!;
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
