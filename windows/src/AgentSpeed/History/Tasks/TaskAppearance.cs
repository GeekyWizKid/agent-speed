using System.Windows;
using System.Windows.Media;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Theme;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// The task performance section's appearance, set once on the section and inherited by everything drawn in it: dark or
/// light, Windows high contrast and Reduce Motion (DESIGN 12, <i>Colors</i>; DESIGN 7).
/// </summary>
internal static class TaskAppearance
{
    public static readonly DependencyProperty DarkProperty = DependencyProperty.RegisterAttached(
        "Dark", typeof(bool), typeof(TaskAppearance),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighContrastProperty = DependencyProperty.RegisterAttached(
        "HighContrast", typeof(bool), typeof(TaskAppearance),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached(
        "ReduceMotion", typeof(bool), typeof(TaskAppearance), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// Bumped on every theme notification, so drawn elements also redraw when only the system colors changed (one high
    /// contrast theme for another).
    /// </summary>
    public static readonly DependencyProperty GenerationProperty = DependencyProperty.RegisterAttached(
        "Generation", typeof(int), typeof(TaskAppearance),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<Color, SolidColorBrush> Brushes = new();

    public static bool GetDark(DependencyObject element) => (bool)element.GetValue(DarkProperty);

    public static void SetDark(DependencyObject element, bool value) => element.SetValue(DarkProperty, value);

    public static bool GetHighContrast(DependencyObject element) => (bool)element.GetValue(HighContrastProperty);

    public static void SetHighContrast(DependencyObject element, bool value) => element.SetValue(HighContrastProperty, value);

    public static bool GetReduceMotion(DependencyObject element) => (bool)element.GetValue(ReduceMotionProperty);

    public static void SetReduceMotion(DependencyObject element, bool value) => element.SetValue(ReduceMotionProperty, value);

    public static int GetGeneration(DependencyObject element) => (int)element.GetValue(GenerationProperty);

    public static void SetGeneration(DependencyObject element, int value) => element.SetValue(GenerationProperty, value);

    public static Color SourceColor(SourceKind source) => HeatPalette.SourceColor(source).ToColor();

    /// <summary>A bar in a source color at <paramref name="opacity"/>; the highlight color in high contrast.</summary>
    public static Color Bar(DependencyObject element, Color color, double opacity = 1) =>
        GetHighContrast(element) ? SystemColors.HighlightColor : WithOpacity(color, opacity);

    /// <summary>Tracks and placeholders: white 9 % on dark, black 7 % on light; the gray-text color in high contrast.</summary>
    public static Color Track(DependencyObject element) =>
        GetHighContrast(element) ? SystemColors.GrayTextColor : TaskScoreColors.Track(GetDark(element)).ToColor();

    /// <summary>A source dot: its color, or the gray-text color in high contrast.</summary>
    public static Color Dot(DependencyObject element, Color color) => GetHighContrast(element) ? SystemColors.GrayTextColor : color;

    public static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B);

    /// <summary>A frozen brush per color, shared by every element of the section.</summary>
    public static SolidColorBrush Brush(Color color)
    {
        if (!Brushes.TryGetValue(color, out var brush))
        {
            if (Brushes.Count > 512)
            {
                Brushes.Clear();
            }

            brush = MediaColors.Frozen(color);
            Brushes[color] = brush;
        }

        return brush;
    }
}
