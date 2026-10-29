using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Presentation.History.Tasks;
using AgentSpeed.Theme;

namespace AgentSpeed.History.Tasks;

/// <summary>
/// The by-model table and narrow list; its data context is the <see cref="TaskPerformanceViewModel"/>. Builds the
/// table's columns from <see cref="TaskPerformanceViewModel.Headers"/>, keeps the sort glyph and its words current, fits
/// the columns to the width, and copies the selected row as text (context menu and Ctrl+C).
/// </summary>
internal sealed partial class TaskModelsView : UserControl
{
    private const string SortDescendingGlyph = "";
    private const string SortAscendingGlyph = "";
    private const double CellSize = 12.5;
    private const double HeaderSize = 12;

    private readonly List<(TaskModelHeader Header, GridViewColumn Column, GridViewColumnHeader Button, TextBlock Label, TextBlock Glyph)> _columns = [];
    private TaskPerformanceViewModel? _model;
    private IReadOnlyList<TaskModelHeader> _built = [];
    private bool _fitQueued;

    public TaskModelsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as TaskPerformanceViewModel);
        // Fitted inside the same layout pass, so a new width never shows a frame with the old columns.
        Table.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5)
            {
                Fit();
            }
        };
        foreach (var list in new ListBox[] { Table, List })
        {
            list.PreviewKeyDown += OnListKeyDown;
            list.ContextMenu = CopyMenu(list);
        }
    }

    public static Thickness TitleMargin { get; } = new(0, TaskPerformanceLayout.ByModelGap, 0, 8);

    private void Attach(TaskPerformanceViewModel? model)
    {
        if (_model is not null)
        {
            _model.Rebuilt -= OnRebuilt;
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (model is not null)
        {
            model.Rebuilt += OnRebuilt;
            model.PropertyChanged += OnModelChanged;
            OnRebuilt(model, EventArgs.Empty);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskPerformanceViewModel.ShowsTable) && _model is { ShowsTable: true })
        {
            QueueFit();
        }
    }

    private void OnRebuilt(object? sender, EventArgs e)
    {
        if (_model is not { } model)
        {
            return;
        }

        if (!_built.SequenceEqual(model.Headers))
        {
            BuildColumns(model.Headers);
        }

        UpdateHeaders(model);
        if (Table.IsVisible && Table.ActualWidth > 0)
        {
            Fit();
        }
        else
        {
            QueueFit();
        }
    }

    // ---- Columns ----

    private void BuildColumns(IReadOnlyList<TaskModelHeader> headers)
    {
        _built = headers;
        _columns.Clear();
        TableView.Columns.Clear();
        var style = (Style)FindResource("HeaderStyle");
        foreach (var header in headers)
        {
            var label = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWithOverflow,
                TextAlignment = header.Numeric ? TextAlignment.Right : TextAlignment.Left,
                LineHeight = TaskTableColumns.HeaderLineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            var glyph = new TextBlock
            {
                FontSize = 9,
                Width = TaskTableColumns.GlyphRoom,
                TextAlignment = header.Numeric ? TextAlignment.Left : TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 3),
                Visibility = Visibility.Hidden,
            };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.SymbolFontKey);
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(glyph, header.Numeric ? Dock.Left : Dock.Right);
            dock.Children.Add(glyph);
            dock.Children.Add(label);
            var button = new GridViewColumnHeader
            {
                Style = style,
                Content = dock,
                HorizontalContentAlignment = header.Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
            };
            var column = header.Column;
            button.Click += (_, _) => _model?.SortBy(column);
            var gridColumn = new GridViewColumn { Header = button, CellTemplate = (DataTemplate)FindResource(CellKey(header)) };
            TableView.Columns.Add(gridColumn);
            _columns.Add((header, gridColumn, button, label, glyph));
        }
    }

    private static string CellKey(TaskModelHeader header) => header.Column switch
    {
        TaskModelColumn.Model => "ModelCell",
        TaskModelColumn.Tasks => header.Merged ? "MergedCell" : "TasksCell",
        TaskModelColumn.Scored => "ScoredCell",
        TaskModelColumn.Median => "MedianCell",
        TaskModelColumn.Success => "SuccessCell",
        TaskModelColumn.Stall => "StallCell",
        TaskModelColumn.FixFail => "FixFailCell",
        TaskModelColumn.Attempts => "AttemptsCell",
        _ => "FirstPassCell",
    };

    /// <summary>Short names with the full name as tooltip and accessible name; the sorted column in the label color with its glyph.</summary>
    private void UpdateHeaders(TaskPerformanceViewModel model)
    {
        var loc = model.Loc;
        foreach (var (header, _, button, label, glyph) in _columns)
        {
            label.Text = header.Text;
            var active = header.Column == model.SortColumn && model.HasModels;
            glyph.Text = model.SortDescending ? SortDescendingGlyph : SortAscendingGlyph;
            glyph.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            if (active)
            {
                button.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            }
            else
            {
                button.ClearValue(ForegroundProperty);
            }

            button.ToolTip = header.FullName;
            AutomationProperties.SetName(button, header.FullName);
            AutomationProperties.SetHelpText(button,
                active ? loc[model.SortDescending ? "win.history.sorted.desc" : "win.history.sorted.asc"] : string.Empty);
        }
    }

    // ---- Widths ----

    private void QueueFit()
    {
        if (_fitQueued)
        {
            return;
        }

        _fitQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitQueued = false;
            Fit();
        }, DispatcherPriority.Loaded);
    }

    private void Fit()
    {
        var available = Table.ActualWidth;
        if (_model is not { } model || _columns.Count == 0 || available <= 0 || !Table.IsVisible)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var family = Table.FontFamily;
        var cell = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var heading = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var rows = model.Models;
        var needs = new List<TaskColumnNeed>();
        var modelWant = TaskTableColumns.TextWidth(_columns[0].Header.Text, heading, HeaderSize, dpi) + TaskTableColumns.GlyphRoom;
        foreach (var row in rows)
        {
            var dots = TaskPerformanceLayout.ModelDot + TaskPerformanceLayout.ModelDotStep * Math.Max(0, row.Sources.Count - 1) + 7;
            var face = new Typeface(family, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            var id = TaskTableColumns.TextWidth(row.Name, face, TaskPerformanceLayout.ModelIdSize, dpi);
            var chip = row.LowSample ? TaskTableColumns.TextWidth(row.LowSampleText, face, TaskPerformanceLayout.ChipSize, dpi) + 10 + 7 : 0;
            modelWant = Math.Max(modelWant, dots + id + chip);
        }

        foreach (var (header, _, _, _, _) in _columns.Skip(1))
        {
            var content = 0.0;
            foreach (var row in rows)
            {
                var width = TaskTableColumns.TextWidth(CellText(row, header), cell, CellSize, dpi);
                if (header.Column == TaskModelColumn.Median)
                {
                    width += 8 + TaskPerformanceLayout.StripWidth;
                }

                content = Math.Max(content, width);
            }

            needs.Add(new TaskColumnNeed(content,
                TaskTableColumns.TextWidth(header.Text, heading, HeaderSize, dpi),
                TaskTableColumns.TextWidth(header.Text, heading, HeaderSize, dpi, lines: 2),
                TaskTableColumns.TextWidth(header.Text, heading, HeaderSize, dpi, lines: 3)));
        }

        var widths = TaskTableColumns.Fit(Math.Floor(available), modelWant + 2 * TaskTableColumns.CellPadding, needs,
            model.Width == TaskPerformanceWidth.Wide);
        for (var i = 0; i < _columns.Count && i < widths.Length; i++)
        {
            if (Math.Abs(_columns[i].Column.Width - widths[i]) > 0.5 || double.IsNaN(_columns[i].Column.Width))
            {
                _columns[i].Column.Width = widths[i];
            }
        }
    }

    private static string CellText(TaskModelRowView row, TaskModelHeader header) => header.Column switch
    {
        TaskModelColumn.Tasks => header.Merged ? row.ScoredOfTasks : row.Tasks,
        TaskModelColumn.Scored => row.Scored,
        TaskModelColumn.Median => row.Median,
        _ => row.Rate(header.Column)?.Text ?? string.Empty,
    };

    // ---- Copy ----

    private ContextMenu CopyMenu(ListBox list)
    {
        var item = new MenuItem { InputGestureText = "Ctrl+C" };
        item.SetBinding(HeaderedItemsControl.HeaderProperty, new System.Windows.Data.Binding("DataContext." + nameof(TaskPerformanceViewModel.CopyRow)) { Source = this });
        item.Click += (_, _) => Copy(list.SelectedItem);
        var menu = new ContextMenu { Items = { item } };
        menu.SetResourceReference(FontFamilyProperty, AppFonts.UiFontKey);
        menu.Opened += (_, _) => item.IsEnabled = list.SelectedItem is TaskModelRowView;
        return menu;
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && sender is ListBox list)
        {
            e.Handled = Copy(list.SelectedItem);
        }
    }

    private static bool Copy(object? item)
    {
        if (item is not TaskModelRowView row)
        {
            return false;
        }

        try
        {
            Clipboard.SetText(row.CopyText);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }
}
