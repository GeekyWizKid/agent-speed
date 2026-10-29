using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Theme;

namespace AgentSpeed.TaskScore;

/// <summary>
/// How the score card draws (DESIGN 12, <i>Colors</i>, <i>Window</i>): the appearance, Windows high contrast, Reduce
/// Motion and transparency effects. High contrast takes system colors only: bars in the highlight color, tracks and dots
/// in the gray-text color, chips in the text color with a 1 px stroke and no fill.
/// </summary>
internal readonly record struct TaskCardAppearance(bool Dark, bool HighContrast, bool ReduceMotion, bool Transparency = true)
{
    public const string PrimaryKey = "TextFillColorPrimaryBrush";
    public const string SecondaryKey = "TextFillColorSecondaryBrush";
    public const string TertiaryKey = "TextFillColorTertiaryBrush";

    /// <summary>The card's fill is solid: transparency effects off, or high contrast.</summary>
    public bool Solid => !Transparency || HighContrast;

    /// <summary>Bars, the composition and the held credit's dots.</summary>
    public Brush Ink => HighContrast ? SystemColors.HighlightBrush : TaskScoreColors.ScoreInk(Dark).ToBrush();

    public Brush Track => HighContrast ? SystemColors.GrayTextBrush : TaskScoreColors.Track(Dark).ToBrush();

    /// <summary>A high contrast track is a hairline outline, not a fill, so a bar's fill stays distinct from it.</summary>
    public bool OutlinedTracks => HighContrast;

    /// <summary>The dots of an unproven track: tertiary text; high contrast gray text.</summary>
    public Brush? TrackDots => HighContrast ? SystemColors.GrayTextBrush : null;

    /// <summary>The meanings, the prediction note and the footnote: a little darker than tertiary.</summary>
    public Brush Caption => HighContrast ? SystemColors.WindowTextBrush : TaskScoreColors.CaptionInk(Dark).ToBrush();

    /// <summary>The label color (black or white) at <paramref name="opacity"/>.</summary>
    public Brush Label(double opacity) =>
        HighContrast ? SystemColors.WindowTextBrush : MediaColors.Frozen((Dark ? Colors.White : Colors.Black) with { A = (byte)Math.Round(255 * opacity) });

    public Brush PredictionFill => HighContrast ? Brushes.Transparent : Label(TaskScoreColors.PredictionFill(Dark));

    public Brush? PredictionStroke => HighContrast ? SystemColors.GrayTextBrush : null;

    /// <summary>The card's fill with transparency effects off (high contrast: the window color).</summary>
    public Brush SolidFill => HighContrast ? SystemColors.WindowBrush : TaskScoreColors.SolidCard(Dark).ToBrush();

    public Brush? ChipText(TaskChipTone tone) =>
        HighContrast ? SystemColors.WindowTextBrush : TaskScoreColors.ChipText(tone, Dark) is { } color ? color.ToBrush() : null;

    public Brush ChipFill(TaskChipTone tone) => HighContrast ? Brushes.Transparent : TaskScoreColors.ChipFill(tone, Dark).ToBrush();

    /// <summary>The neutral chip's dotted outline; high contrast draws every chip with a 1 px solid stroke in its text color.</summary>
    public Brush ChipOutline => HighContrast ? SystemColors.WindowTextBrush : Label(TaskScoreColors.NeutralOutline);

    /// <summary>The amber loop glyph of the dashboard's looping mark.</summary>
    public Brush LoopGlyph => new Modes.CapsuleBrushes(Dark, HighContrast).Bolt;
}
