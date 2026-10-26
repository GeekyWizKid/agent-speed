using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Work;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb.Lines;

namespace AgentSpeed.Presentation.Tests;

/// <summary>The intelligence and work-signal wording of orbs and agent rows (DESIGN 4, 10).</summary>
public class IntelligenceTextTests
{
    private static readonly Localizer En = new(Localizer.LoadEmbeddedTable(), LanguageCodes.En);
    private static readonly Localizer ZhHant = new(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);

    [Fact]
    public void AnAgentsGradeNamesItsClass()
    {
        Assert.Equal("等級 124 · 旗艦", OrbLinesText.Grade(new ModelGrade(ModelClass.Flagship, 4), ZhHant));
        Assert.Equal(En.Format("intel.grade", "92", En["intel.class.standard"]), OrbLinesText.Grade(ModelGrade.Unknown, En));
        Assert.Equal(En.Format("intel.grade", "60", En["intel.class.light"]), OrbLinesText.Grade(new ModelGrade(ModelClass.Light, 0), En));
    }

    [Fact]
    public void TheOrbTooltipAndLabelReadTheIndexAndThinking()
    {
        var sums = new IntelligenceSums(Output: 1200, Graded: 1200 * 124, Thinking: 528, ThinkingBasis: 1200);
        var content = OrbLinesText.Intelligence(sums, generating: false, ZhHant).Content!;
        Assert.Equal("智力 133 · 思考 44%\n估算指數，不是測驗分數", content.Detail);
        Assert.Equal("智力 133，思考每秒 176 個 token", content.Spoken);
        Assert.Equal(("智", "思"), (content.First.Glyph, content.Second.Glyph));
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
}
