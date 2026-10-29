using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.TaskScore;
using AgentSpeed.Theme;

namespace AgentSpeed.TaskScore;

/// <summary>
/// The score card's content (任務評分卡, DESIGN 12, <i>Score card</i>, Layout): header, score with its status line and
/// composition, 評分構成, 預測, 行為, the facts and the footnote, 300 DIP wide with 14 DIP padding and blocks 12 apart.
/// Built once and updated in place, so the number counts up and the bars ease when the engine tick changes them.
/// </summary>
internal sealed class ScoreCardView : StackPanel
{
    public const double CardWidth = TaskCardPlacement.Width;
    public const double CardPadding = 14;
    public const double BlockGap = 12;

    private const double SourceDot = 7;
    private const double DotGap = 7;
    private const int MaxPredictions = 3;

    private readonly Ellipse _dot = new() { Width = SourceDot, Height = SourceDot, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _project = Text(13, FontWeights.SemiBold, trim: true);
    private readonly StateChip _chip = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBlock _meta = Text(11, FontWeights.Normal, trim: true);
    private readonly Rectangle _headerRule = Hairline();
    private readonly TextBlock _unsupported = Text(12, FontWeights.Normal, wrap: true);

    private readonly StackPanel _score = new();
    private readonly TextBlock _eyebrow = Text(11, FontWeights.Normal, trim: true);
    private readonly ScoreNumber _number = new() { FontSize = 34, Suffix = TaskScoreCard.OutOfText, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = Text(12, FontWeights.Normal, wrap: true);
    private readonly CompositionBar _composition = new() { Margin = new Thickness(0, 10, 0, 0) };

    private readonly StackPanel _dimensions = new() { Margin = new Thickness(0, BlockGap, 0, 0) };
    private readonly TextBlock _dimensionsTitle = Title();
    private readonly DimensionRowView[] _rows = [.. Enumerable.Range(0, 5).Select(_ => new DimensionRowView())];

    private readonly StackPanel _predictions = new() { Margin = new Thickness(0, BlockGap, 0, 0) };
    private readonly TextBlock _predictionsTitle = Title();
    private readonly Grid _cells = new();
    private readonly PredictionCellView[] _cellViews = [.. Enumerable.Range(0, MaxPredictions).Select(_ => new PredictionCellView())];
    private readonly TextBlock _predictionNote = Text(10.5, FontWeights.Normal, wrap: true);

    private readonly StackPanel _signals = new() { Margin = new Thickness(0, BlockGap, 0, 0) };
    private readonly TextBlock _signalsTitle = Title();
    private readonly StackPanel _signalLines = new();

    private readonly Rectangle _factsRule = Hairline();
    private readonly StackPanel _facts = new();
    private readonly TextBlock _footnote = Text(10.5, FontWeights.Normal, wrap: true);

    private TaskCardAppearance _appearance;
    private TaskScoreCard? _card;
    private int _compact;

    public ScoreCardView()
    {
        Width = CardWidth - 2 * CardPadding;
        UseLayoutRounding = true;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DotGap) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_project, 2);
        Grid.SetColumn(_chip, 3);
        header.Children.Add(_dot);
        header.Children.Add(_project);
        header.Children.Add(_chip);
        _meta.Margin = new Thickness(SourceDot + DotGap, 2, 0, 0);
        Children.Add(header);
        Children.Add(_meta);
        Children.Add(_headerRule);
        Children.Add(_unsupported);

        _number.SetResourceReference(ScoreNumber.FontFamilyProperty, AppFonts.DisplayFontKey);
        _eyebrow.Margin = new Thickness(0, 0, 0, 2);
        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.MaxHeight = 2 * 17 + 1;
        _status.LineHeight = 17;
        _status.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        _score.Children.Add(_eyebrow);
        _score.Children.Add(_number);
        _score.Children.Add(_status);
        _score.Children.Add(_composition);
        Children.Add(_score);

        _dimensions.Children.Add(_dimensionsTitle);
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0);
            _dimensions.Children.Add(_rows[i]);
        }

        Children.Add(_dimensions);

        _predictions.Children.Add(_predictionsTitle);
        _predictions.Children.Add(_cells);
        _predictionNote.Margin = new Thickness(0, 7, 0, 0);
        _predictions.Children.Add(_predictionNote);
        Children.Add(_predictions);

        _signals.Children.Add(_signalsTitle);
        _signals.Children.Add(_signalLines);
        Children.Add(_signals);

        Children.Add(_factsRule);
        Children.Add(_facts);
        _footnote.Margin = new Thickness(0, 10, 0, 0);
        Children.Add(_footnote);
    }

    /// <summary>
    /// Shows <paramref name="card"/>. While <paramref name="sameRow"/> (the open card's row on a new engine tick) the number
    /// follows DESIGN 12's motion and the bars ease; a newly opened or switched card shows its values as they are.
    /// </summary>
    public void Apply(TaskScoreCard card, TaskCardAppearance appearance, bool sameRow)
    {
        ArgumentNullException.ThrowIfNull(card);
        _appearance = appearance;
        _card = card;

        _dot.Fill = appearance.HighContrast ? SystemColors.WindowTextBrush : HeatPalette.SourceColor(card.Source).ToBrush();
        Set(_project, card.Project, Role.Primary);
        _chip.Apply(card.Chip, appearance);
        Set(_meta, card.Meta, Role.Secondary);
        _headerRule.Margin = new Thickness(0, BlockGap, 0, BlockGap);
        PaintRule(_headerRule);

        _unsupported.Visibility = card.IsUnsupported ? Visibility.Visible : Visibility.Collapsed;
        Set(_unsupported, card.UnsupportedText, Role.Secondary);
        foreach (var block in new FrameworkElement[] { _score, _dimensions })
        {
            block.Visibility = card.IsUnsupported ? Visibility.Collapsed : Visibility.Visible;
        }

        if (!card.IsUnsupported)
        {
            ApplyScore(card, sameRow);
            ApplyDimensions(card, sameRow);
        }

        ApplyPredictions(card);
        ApplySignals(card);
        ApplyFacts(card);
        Set(_footnote, card.Footnote, Role.Caption);
        _footnote.Visibility = card.IsUnsupported || _compact >= 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// How much a card taller than the screen leaves out: 1 drops the dimensions' meanings, 2 the footnote too. Both stay in
    /// the row's spoken text.
    /// </summary>
    public int Compact
    {
        get => _compact;
        set
        {
            if (value == _compact)
            {
                return;
            }

            _compact = value;
            foreach (var row in _rows)
            {
                row.ShowMeaning(value < 1);
            }

            _footnote.Visibility = _card is { IsUnsupported: false } && value < 2 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// The card window shows what the row already says: the row carries the card's text as its help text, so the card
    /// is one element named with it, without children (no glyphs or drawn bars to step through).
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new CardPeer(this);

    private enum Role
    {
        Primary,
        Secondary,
        Tertiary,
        Caption,
    }

    private void ApplyScore(TaskScoreCard card, bool sameRow)
    {
        _eyebrow.Visibility = card.HasEyebrow ? Visibility.Visible : Visibility.Collapsed;
        Set(_eyebrow, card.Eyebrow ?? string.Empty, Role.Secondary);

        _number.ReduceMotion = _appearance.ReduceMotion;
        _number.HighContrast = _appearance.HighContrast;
        PaintNumber();
        if (sameRow)
        {
            _number.Value = card.Shown;
            _number.Unproven = card.HasProofMark;
            _number.Tone = card.NumberTone;
        }
        else
        {
            _number.Reset(card.Shown, card.HasProofMark, card.NumberTone);
        }

        _status.Inlines.Clear();
        foreach (var run in card.Status)
        {
            var inline = new Run(run.Text);
            if (run.IsCode)
            {
                inline.FontFamily = AppFonts.Monospace;
                inline.FontSize = 11;
                inline.Foreground = BrushFor(Role.Primary);
            }

            _status.Inlines.Add(inline);
        }

        _status.Foreground = BrushFor(Role.Secondary);
        _status.ToolTip = null;

        PaintBar(_composition);
        _composition.Apply(card.Composition);
    }

    private void ApplyDimensions(TaskScoreCard card, bool sameRow)
    {
        Set(_dimensionsTitle, card.DimensionsTitle, Role.Tertiary);
        for (var i = 0; i < _rows.Length; i++)
        {
            var visible = i < card.Dimensions.Count;
            _rows[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
                _rows[i].Apply(card.Dimensions[i], this, sameRow);
            }
        }
    }

    private void ApplyPredictions(TaskScoreCard card)
    {
        _predictions.Visibility = card.HasPredictions ? Visibility.Visible : Visibility.Collapsed;
        if (!card.HasPredictions)
        {
            return;
        }

        Set(_predictionsTitle, card.PredictionsTitle, Role.Tertiary);
        var count = card.Predictions.Count;
        if (_cells.ColumnDefinitions.Count != count * 2 - 1)
        {
            _cells.Children.Clear();
            _cells.ColumnDefinitions.Clear();
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                }

                _cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(_cellViews[i], i * 2);
                _cells.Children.Add(_cellViews[i]);
            }
        }

        for (var i = 0; i < count; i++)
        {
            _cellViews[i].Apply(card.Predictions[i], this);
        }

        Set(_predictionNote, card.PredictionNote, Role.Caption);
    }

    private void ApplySignals(TaskScoreCard card)
    {
        _signals.Visibility = card.HasSignals ? Visibility.Visible : Visibility.Collapsed;
        Set(_signalsTitle, card.SignalsTitle, Role.Tertiary);
        _signalLines.Children.Clear();
        for (var i = 0; i < card.Signals.Count; i++)
        {
            var signal = card.Signals[i];
            var line = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
            var glyph = new TextBlock
            {
                Text = SignalGlyph(signal.Glyph),
                FontSize = 13,
                FontFamily = AppFonts.Symbols,
                Width = 16,
                Margin = new Thickness(0, 1, 6, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Foreground = signal.Glyph == TaskSignalGlyph.Loop ? _appearance.LoopGlyph : BrushFor(Role.Secondary),
            };
            DockPanel.SetDock(glyph, Dock.Left);
            var text = Text(12, FontWeights.Normal, wrap: true);
            Set(text, signal.Text, signal.IsWarning ? Role.Primary : Role.Secondary);
            line.Children.Add(glyph);
            line.Children.Add(text);
            _signalLines.Children.Add(line);
        }
    }

    private void ApplyFacts(TaskScoreCard card)
    {
        _factsRule.Visibility = card.HasFacts ? Visibility.Visible : Visibility.Collapsed;
        _factsRule.Margin = new Thickness(0, BlockGap, 0, 9);
        PaintRule(_factsRule);
        _facts.Children.Clear();
        foreach (var fact in card.Facts)
        {
            var line = Text(11, FontWeights.Normal, wrap: true);
            line.LineHeight = 16.5;
            line.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Typography.SetNumeralAlignment(line, FontNumeralAlignment.Tabular);
            Set(line, fact, Role.Secondary);
            _facts.Children.Add(line);
        }
    }

    private void PaintNumber()
    {
        if (_appearance.HighContrast)
        {
            _number.LiveBrush = SystemColors.WindowTextBrush;
            _number.RestingBrush = SystemColors.GrayTextBrush;
            _number.NoneBrush = SystemColors.GrayTextBrush;
            _number.SuffixBrush = SystemColors.GrayTextBrush;
            return;
        }

        _number.SetResourceReference(ScoreNumber.LiveBrushProperty, TaskCardAppearance.PrimaryKey);
        _number.SetResourceReference(ScoreNumber.RestingBrushProperty, TaskCardAppearance.SecondaryKey);
        _number.SetResourceReference(ScoreNumber.NoneBrushProperty, TaskCardAppearance.TertiaryKey);
        _number.SetResourceReference(ScoreNumber.SuffixBrushProperty, TaskCardAppearance.TertiaryKey);
    }

    private void PaintBar(ScoreBarBase bar)
    {
        bar.Ink = _appearance.Ink;
        bar.Track = _appearance.Track;
        bar.OutlinedTrack = _appearance.OutlinedTracks;
        if (_appearance.TrackDots is { } dots)
        {
            bar.Dots = dots;
        }
        else
        {
            bar.SetResourceReference(ScoreBarBase.DotsProperty, TaskCardAppearance.TertiaryKey);
        }
    }

    private void PaintRule(Rectangle rule)
    {
        if (_appearance.HighContrast)
        {
            rule.Fill = SystemColors.GrayTextBrush;
        }
        else
        {
            rule.SetResourceReference(Shape.FillProperty, "DividerStrokeColorDefaultBrush");
        }
    }

    private Brush BrushFor(Role role)
    {
        if (role == Role.Caption)
        {
            return _appearance.Caption;
        }

        if (_appearance.HighContrast)
        {
            return role == Role.Tertiary ? SystemColors.GrayTextBrush : SystemColors.WindowTextBrush;
        }

        var key = role switch
        {
            Role.Primary => TaskCardAppearance.PrimaryKey,
            Role.Secondary => TaskCardAppearance.SecondaryKey,
            _ => TaskCardAppearance.TertiaryKey,
        };
        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    private void Set(TextBlock block, string text, Role role)
    {
        block.Text = text;
        if (role != Role.Caption && !_appearance.HighContrast)
        {
            block.SetResourceReference(TextBlock.ForegroundProperty, role switch
            {
                Role.Primary => TaskCardAppearance.PrimaryKey,
                Role.Secondary => TaskCardAppearance.SecondaryKey,
                _ => TaskCardAppearance.TertiaryKey,
            });
            return;
        }

        block.Foreground = BrushFor(role);
    }

    private static string SignalGlyph(TaskSignalGlyph glyph) => glyph switch
    {
        TaskSignalGlyph.Loop => "",
        TaskSignalGlyph.Repeat => "",
        TaskSignalGlyph.Oscillation => "",
        _ => "",
    };

    private static TextBlock Text(double size, FontWeight weight, bool trim = false, bool wrap = false)
    {
        var block = new TextBlock { FontSize = size, FontWeight = weight };
        if (trim)
        {
            block.TextTrimming = TextTrimming.CharacterEllipsis;
        }

        if (wrap)
        {
            block.TextWrapping = TextWrapping.Wrap;
        }

        return block;
    }

    private static TextBlock Title()
    {
        var title = Text(11, FontWeights.SemiBold, trim: true);
        title.Margin = new Thickness(0, 0, 0, 8);
        return title;
    }

    private static Rectangle Hairline() => new() { Height = 1, SnapsToDevicePixels = true };

    /// <summary>One dimension: label and value, the bar 5 DIP under it, the meaning 4 DIP under the bar.</summary>
    private sealed class DimensionRowView : StackPanel
    {
        private readonly TextBlock _label = Text(12.5, FontWeights.SemiBold, trim: true);
        private readonly TextBlock _value = Text(12, FontWeights.Medium);
        private readonly ScoreBar _bar = new() { Margin = new Thickness(0, 5, 0, 0) };
        private readonly TextBlock _meaning = Text(10.5, FontWeights.Normal, wrap: true);

        public DimensionRowView()
        {
            var top = new DockPanel();
            _value.HorizontalAlignment = HorizontalAlignment.Right;
            _value.Margin = new Thickness(8, 0, 0, 0);
            Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(_value, Dock.Right);
            top.Children.Add(_value);
            top.Children.Add(_label);
            _meaning.Margin = new Thickness(0, 4, 0, 0);
            _meaning.LineHeight = 14.5;
            _meaning.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Children.Add(top);
            Children.Add(_bar);
            Children.Add(_meaning);
        }

        public void ShowMeaning(bool shown) => _meaning.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

        public void Apply(TaskDimensionRow row, ScoreCardView card, bool animate)
        {
            card.Set(_label, row.Label, Role.Primary);
            _value.Inlines.Clear();
            switch (row.Kind)
            {
                case TaskDimensionValueKind.Value:
                    _value.FontWeight = FontWeights.Medium;
                    _value.FontSize = 12;
                    _value.Inlines.Add(new Run(row.ValueText) { Foreground = card.BrushFor(Role.Primary) });
                    _value.Inlines.Add(new Run(row.MaxText) { Foreground = card.BrushFor(Role.Tertiary) });
                    break;
                case TaskDimensionValueKind.Pending:
                    _value.FontWeight = FontWeights.Normal;
                    _value.FontSize = 11.5;
                    _value.Inlines.Add(new Run(row.ValueText) { Foreground = card.BrushFor(Role.Secondary) });
                    break;
                default:
                    _value.FontWeight = FontWeights.Normal;
                    _value.FontSize = 11.5;
                    _value.Inlines.Add(new Run(row.ValueText) { Foreground = card.BrushFor(Role.Tertiary) });
                    break;
            }

            card.PaintBar(_bar);
            _bar.Apply(row, animate, card._appearance.ReduceMotion);
            card.Set(_meaning, row.Description, Role.Caption);
        }
    }

    /// <summary>One prediction: the percentage, its label and the model's flag, on the label color at 5 % (dark 6 %).</summary>
    private sealed class PredictionCellView : Border
    {
        private readonly TextBlock _percent = Text(17, FontWeights.SemiBold);
        private readonly TextBlock _label = Text(11, FontWeights.Normal, trim: true);
        private readonly TextBlock _flag = Text(10, FontWeights.Normal, trim: true);

        public PredictionCellView()
        {
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(10, 8, 8, 7);
            _percent.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.DisplayFontKey);
            Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
            Typography.SetNumeralAlignment(_flag, FontNumeralAlignment.Tabular);
            _label.Margin = new Thickness(0, 2, 0, 0);
            _flag.Margin = new Thickness(0, 3, 0, 0);
            Child = new StackPanel { Children = { _percent, _label, _flag } };
        }

        public void Apply(TaskPredictionCell cell, ScoreCardView card)
        {
            Background = card._appearance.PredictionFill;
            BorderBrush = card._appearance.PredictionStroke;
            BorderThickness = new Thickness(card._appearance.PredictionStroke is null ? 0 : 1);
            card.Set(_percent, cell.Percent, Role.Primary);
            card.Set(_label, cell.Label, Role.Secondary);
            card.Set(_flag, cell.Flag, Role.Tertiary);
        }
    }

    private sealed class CardPeer(ScoreCardView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(ScoreCardView);

        protected override string GetNameCore() => ((ScoreCardView)Owner)._card?.Project ?? string.Empty;

        protected override string GetHelpTextCore() => ((ScoreCardView)Owner)._card?.FullDescription ?? string.Empty;

        protected override List<AutomationPeer> GetChildrenCore() => [];
    }
}
