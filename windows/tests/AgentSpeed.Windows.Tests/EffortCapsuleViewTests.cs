using System.Windows;
using System.Windows.Media;
using AgentSpeed.Dashboard;
using AgentSpeed.Modes;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;
using AgentSpeed.Theme;
using Xunit.Abstractions;

namespace AgentSpeed.Windows.Tests;

/// <summary>
/// DESIGN 4, Agents, Effort capsule: measured with the Windows fonts, capsules in one column start their meters on one
/// vertical line and end their bolts on another, and the widest capsule leaves a top-level project name its 72 DIP.
/// </summary>
public class EffortCapsuleViewTests(ITestOutputHelper output)
{
    /// <summary>A top-level row's name room in English without a capsule: the intelligence cell and its gap come off too.</summary>
    private static double DashboardNameRoom => NameRoom(LanguageCodes.En);

    [Fact]
    public void TheViewTakesTheModelsWidthAndTheColumnAddsItsGap()
    {
        var capsule = new EffortCapsule("ultracode", "ultrafast");
        var (own, column, empty) = OnStaThread(() =>
        {
            var view = View(capsule, double.NaN, gap: 0);
            var inColumn = View(capsule, 120, gap: 8);
            var none = View(new EffortCapsule(null, "standard"), 0, gap: 8);
            return (view.Width, inColumn.Width, none.Width);
        });
        var expected = OnStaThread(() => capsule.Width(compact: false, CapsuleWordMeasure.For(LanguageCodes.En)));
        Assert.Equal(expected, own, 3);
        Assert.Equal(128, column, 3);
        Assert.Equal(0, empty);
    }

    [Fact]
    public void MetersStartAndBoltsEndOnTheColumnsLines()
    {
        var capsules = new[] { new EffortCapsule("xhigh", "fast"), new EffortCapsule("ultracode", "ultrafast"), new EffortCapsule("low", "ultrafast") };
        var edges = OnStaThread(() =>
        {
            var column = EffortCapsuleColumn.For(capsules, DashboardNameRoom, CapsuleWordMeasure.For(LanguageCodes.En));
            return capsules.Select(c =>
            {
                var view = View(c, column.Width, gap: 0);
                view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                view.Arrange(new Rect(view.DesiredSize));
                view.UpdateLayout();
                var meter = Find<EffortMeter>(view);
                var bolts = Find<SpeedBolts>(view);
                var meterLeft = meter.TranslatePoint(default, view).X;
                var boltsRight = bolts.TranslatePoint(new Point(bolts.ActualWidth, 0), view).X;
                return (column.Width, meterLeft, boltsRight, bolts.ActualWidth);
            }).ToArray();
        });
        foreach (var (width, meterLeft, boltsRight, boltsWidth) in edges)
        {
            output.WriteLine($"column {width:0.0}: meter at {meterLeft:0.0}, bolts {boltsWidth:0.0} end at {boltsRight:0.0}");
            Assert.Equal(EffortCapsule.Padding, meterLeft, 1);
            Assert.Equal(width - EffortCapsule.Padding, boltsRight, 1);
        }
    }

    [Fact]
    public void TheWidestCapsuleKeepsItsWordBesideATopLevelNameInEveryLanguage()
    {
        var widest = new[] { new EffortCapsule("ultracode", "ultrafast") };
        var columns = OnStaThread(() => LanguageCodes.All
            .Select(language => (language, NameRoom(language), EffortCapsuleColumn.For(widest, NameRoom(language), CapsuleWordMeasure.For(language))))
            .ToArray());
        foreach (var (language, room, column) in columns)
        {
            output.WriteLine($"{language}: name room {room:0.0}, capsule {column.Width:0.0} DIP, compact {column.Compact}");
            Assert.False(column.Compact, language);
            Assert.True(room - column.Footprint >= EffortCapsuleColumn.MinimumName, language);
        }
    }

    private static double NameRoom(string language) => DashboardViewModel.NameRoom(0,
        IntelligenceColumnMeasure.Width(language, new Localizer(Localizer.LoadEmbeddedTable(), language)["intel.glyph"]));

    private static EffortCapsuleView View(EffortCapsule capsule, double columnWidth, double gap)
    {
        var view = new EffortCapsuleView();
        view.Resources[AppFonts.UiFontKey] = AppFonts.Ui(LanguageCodes.En);
        view.Resources[AppFonts.SymbolFontKey] = AppFonts.Symbols;
        view.LanguageCode = LanguageCodes.En;
        view.Gap = gap;
        view.ColumnWidth = columnWidth;
        view.Capsule = capsule;
        return view;
    }

    private static T Find<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (child is Visual && Find<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null!;
    }

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
