using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.TaskScore;
using Xunit.Abstractions;

namespace AgentSpeed.Windows.Tests;

/// <summary>
/// The score card on Windows (DESIGN 12, <i>Score card</i>): every state lays out and renders at the card's width in both
/// appearances and in high contrast, in every language, and an update of the open card keeps its width.
/// </summary>
public class ScoreCardViewTests(ITestOutputHelper output)
{
    private static readonly StringTable Table = Localizer.LoadEmbeddedTable();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EveryStateLaysOutAndRendersAtTheCardsWidth(bool dark, bool highContrast)
    {
        var sizes = OnStaThread(() => LanguageCodes.All.SelectMany(language =>
        {
            var loc = new Localizer(Table, language);
            return TaskScoreSamples.Cards(loc).Select(sample =>
            {
                var view = new ScoreCardView();
                view.Apply(TaskScoreCard.Build(sample.Subject, loc), new TaskCardAppearance(dark, highContrast, ReduceMotion: true), sameRow: false);
                view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                view.Arrange(new Rect(view.DesiredSize));
                view.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                return (language, sample.Caption, view.ActualWidth, view.ActualHeight);
            }).ToArray();
        }).ToArray());

        foreach (var (language, caption, width, height) in sizes)
        {
            output.WriteLine($"{language} {caption}: {width:0} × {height:0}");
            Assert.Equal(ScoreCardView.CardWidth - 2 * ScoreCardView.CardPadding, width, 1);
            Assert.InRange(height, 60, 900);
        }
    }

    [Fact]
    public void ANewTickUpdatesTheOpenCardInPlace()
    {
        var (before, after) = OnStaThread(() =>
        {
            var loc = new Localizer(Table, LanguageCodes.ZhHant);
            var samples = TaskScoreSamples.Cards(loc);
            var view = new ScoreCardView();
            var appearance = new TaskCardAppearance(Dark: true, HighContrast: false, ReduceMotion: false);
            view.Apply(TaskScoreCard.Build(samples[2].Subject, loc), appearance, sameRow: false);
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var first = view.DesiredSize;
            view.Apply(TaskScoreCard.Build(samples[4].Subject, loc), appearance, sameRow: true);
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return (first, view.DesiredSize);
        });
        Assert.Equal(before.Width, after.Width, 1);
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
