using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Theme;
using Capsule = AgentSpeed.Presentation.Modes.EffortCapsule;

namespace AgentSpeed.Modes;

/// <summary>
/// The effort capsule (DESIGN 4, Agents): meter, word and bolts in a fully rounded capsule. Given a column width it
/// stretches to fill it, so meters start on one vertical line and bolts end on another; width changes animate 0.2 s.
/// </summary>
internal sealed class EffortCapsuleView : Grid
{
    public static readonly DependencyProperty CapsuleProperty = Register(nameof(Capsule), typeof(Capsule), null);

    public static readonly DependencyProperty CompactProperty = Register(nameof(Compact), typeof(bool), false);

    /// <summary>The shared column's width; NaN draws the capsule at its own width.</summary>
    public static readonly DependencyProperty ColumnWidthProperty = Register(nameof(ColumnWidth), typeof(double), double.NaN);

    /// <summary>Space after the capsule while the column shows (the gap before the live value).</summary>
    public static readonly DependencyProperty GapProperty = Register(nameof(Gap), typeof(double), 0.0);

    public static readonly DependencyProperty AppearanceProperty = Register(nameof(Appearance), typeof(CapsuleAppearance), default(CapsuleAppearance));

    /// <summary>The language whose UI face measures the word.</summary>
    public static readonly DependencyProperty LanguageCodeProperty = Register(nameof(LanguageCode), typeof(string), LanguageCodes.ZhHant);

    private readonly Border _shell;
    private readonly EffortMeter _meter = new();
    private readonly TextBlock _word = Word();
    private readonly TextBlock _fadingWord = Word();
    private readonly Grid _words = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SpeedBolts _bolts = new();
    private string? _shownWord;
    private bool _drawn;

    public EffortCapsuleView()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Focusable = false;
        // The fading word sits on a canvas so it never holds the capsule at the old word's width.
        var fading = new Canvas { ClipToBounds = false };
        fading.Children.Add(_fadingWord);
        _words.Children.Add(fading);
        _words.Children.Add(_word);
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetColumn(_words, 1);
        SetColumn(_bolts, 3);
        content.Children.Add(_meter);
        content.Children.Add(_words);
        content.Children.Add(_bolts);
        _shell = new Border
        {
            Height = Capsule.Height,
            CornerRadius = new CornerRadius(Capsule.Height / 2),
            Child = content,
            Opacity = 0,
            SnapsToDevicePixels = false,
        };
        Children.Add(_shell);
        Width = 0;
        Loaded += (_, _) => Refresh(animate: false);
    }

    public Capsule? Capsule
    {
        get => (Capsule?)GetValue(CapsuleProperty);
        set => SetValue(CapsuleProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public CapsuleAppearance Appearance
    {
        get => (CapsuleAppearance)GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public string LanguageCode
    {
        get => (string)GetValue(LanguageCodeProperty);
        set => SetValue(LanguageCodeProperty, value);
    }

    private static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(EffortCapsuleView),
            new PropertyMetadata(fallback, (d, _) => ((EffortCapsuleView)d).Refresh(animate: true)));

    private static TextBlock Word()
    {
        var word = new TextBlock
        {
            FontSize = Capsule.WordFontSize,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        word.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.UiFontKey);
        return word;
    }

    private void Refresh(bool animate)
    {
        var capsule = Capsule ?? new Capsule(null, null);
        var appearance = Appearance;
        var brushes = appearance.Brushes;
        animate &= IsLoaded && IsVisible && _drawn;
        _drawn = IsLoaded;

        _shell.Background = brushes.Fill;
        _shell.BorderBrush = brushes.Stroke;
        _shell.BorderThickness = new Thickness(brushes.StrokeThickness);
        // The stroke sits inside the 6 DIP padding, so meters and bolts keep their lines in high contrast too.
        var inset = Capsule.Padding - brushes.StrokeThickness;
        _shell.Padding = new Thickness(inset, 0, inset, 0);
        _shell.Margin = new Thickness(0, 0, Gap, 0);
        CapsuleMotion.To(_shell, OpacityProperty, capsule.IsVisible ? 1 : 0, CapsuleMotion.Crossfade, CapsuleMotion.Smooth, animate);

        // A capsule that disappears keeps its last parts while it fades.
        if (capsule.IsVisible)
        {
            _meter.Visibility = capsule.Level is null ? Visibility.Collapsed : Visibility.Visible;
            _meter.Show(capsule.Effort, brushes, animate, appearance.ReduceMotion, appearance.Shimmers);
            ShowWord(capsule.ShowsWord(Compact) ? capsule.Effort : null, capsule, brushes, animate);
            _words.Margin = new Thickness(capsule.Level is not null && _shownWord is not null ? Capsule.PartSpacing : 0, 0, 0, 0);
            _bolts.Show(capsule.Bolts, brushes, animate, appearance.ReduceMotion);
        }

        var own = capsule.Width(Compact, CapsuleWordMeasure.For(LanguageCode));
        var width = double.IsNaN(ColumnWidth) ? own : ColumnWidth;
        CapsuleMotion.To(this, WidthProperty, width > 0 ? width + Gap : 0, CapsuleMotion.Crossfade, CapsuleMotion.Smooth, animate);
    }

    private void ShowWord(string? word, Capsule capsule, CapsuleBrushes brushes, bool animate)
    {
        Paint(_word, brushes.Word(capsule.Tone));
        if (word == _shownWord)
        {
            CapsuleMotion.To(_word, OpacityProperty, word is null ? 0 : 1, CapsuleMotion.Crossfade, CapsuleMotion.Smooth, animate);
            return;
        }

        // The old word crossfades out in place while the new one fades in.
        _fadingWord.Text = _word.Text;
        _fadingWord.Foreground = _word.Foreground;
        _fadingWord.Opacity = _word.Opacity;
        CapsuleMotion.To(_fadingWord, OpacityProperty, 0, CapsuleMotion.Crossfade, CapsuleMotion.Smooth, animate);
        _word.BeginAnimation(OpacityProperty, null);
        _word.Opacity = 0;
        _word.Text = word ?? string.Empty;
        _shownWord = word;
        CapsuleMotion.To(_word, OpacityProperty, word is null ? 0 : 1, CapsuleMotion.Crossfade, CapsuleMotion.Smooth, animate);
        if (!animate)
        {
            _fadingWord.Text = string.Empty;
        }
    }

    private static void Paint(TextBlock text, Brush? brush)
    {
        if (brush is null)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, CapsuleBrushes.SecondaryKey);
        }
        else
        {
            text.Foreground = brush;
        }
    }
}
