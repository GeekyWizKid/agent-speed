using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shapes;
using AgentSpeed.Modes;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Menus;
using AgentSpeed.Theme;

namespace AgentSpeed.Menus;

/// <summary>
/// Turns a menu model into a Fluent context menu whose rows share one text column (MenuStyles.xaml): every item of a
/// level gets that level's icon and trailing column flags, so labels line up with or without an icon or a check.
/// </summary>
internal static class MenuBuilder
{
    public const string ItemStyleKey = "AppMenuItemStyle";

    /// <param name="capsule">How an effort capsule row draws (DESIGN 2b, item 1).</param>
    public static ContextMenu Build(MenuLevel level, IAppCommands commands, Localizer localizer, CapsuleAppearance capsule = default)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(localizer);
        var menu = new ContextMenu { UseLayoutRounding = true };
        menu.SetResourceReference(Control.FontFamilyProperty, AppFonts.UiFontKey);
        menu.Language = AppFonts.XmlLanguageFor(localizer.LanguageCode);
        Fill(menu.Items, level, commands, new ItemContext(localizer.LanguageCode, capsule));
        return menu;
    }

    /// <summary>Runs a row's action.</summary>
    public static void Run(MenuAction action, IAppCommands commands)
    {
        switch (action)
        {
            case MenuAction.OpenUpdate:
                commands.OpenUpdate();
                break;
            case MenuAction.OpenDashboard:
                commands.ToggleDashboardFromMenu();
                break;
            case MenuAction.OpenHistory:
                commands.OpenHistory();
                break;
            case MenuAction.OpenSettings a:
                commands.OpenSettings(a.Page);
                break;
            case MenuAction.CheckForUpdates:
                commands.CheckForUpdates();
                break;
            case MenuAction.Quit:
                commands.Quit();
                break;
            case MenuAction.SetEffects a:
                commands.UpdateSettings(s => s with { Effects = a.Level });
                break;
            case MenuAction.SetShowOnTaskbar a:
                commands.UpdateSettings(s => s with { ShowOnTaskbar = a.Show });
                break;
            case MenuAction.SetShowOrbs a:
                commands.SetShowOrbs(a.Show);
                break;
            case MenuAction.SetOrbStyle a:
                commands.SetOrbStyle(a.OrbId, a.Style);
                break;
            case MenuAction.SetOrbUnit a:
                commands.SetOrbUnit(a.OrbId, a.Unit);
                break;
            case MenuAction.SetOrbTraffic a:
                commands.SetOrbTraffic(a.OrbId, a.Traffic);
                break;
            case MenuAction.SetOrbAllSources a:
                commands.SetOrbAllSources(a.OrbId);
                break;
            case MenuAction.ToggleOrbSource a:
                commands.ToggleOrbSource(a.OrbId, a.Source);
                break;
            case MenuAction.SetOrbSize a:
                commands.SetOrbSize(a.OrbId, a.Size);
                break;
            case MenuAction.SetOrbAlwaysOnTop a:
                commands.SetOrbAlwaysOnTop(a.OrbId, a.On);
                break;
            case MenuAction.SetOrbClickThrough a:
                commands.SetOrbClickThrough(a.OrbId, a.On);
                break;
            case MenuAction.SetOrbVisible a:
                commands.SetOrbVisible(a.OrbId, a.Visible);
                break;
            case MenuAction.SetOrbShowName a:
                commands.SetOrbShowName(a.OrbId, a.Show);
                break;
            case MenuAction.AddOrb a:
                commands.AddOrb(a.FromOrbId);
                break;
            case MenuAction.DuplicateOrb a:
                commands.DuplicateOrb(a.OrbId);
                break;
            case MenuAction.SplitOrb a:
                commands.SplitOrb(a.OrbId);
                break;
            case MenuAction.MergeOrbs a:
                commands.MergeOrbs(a.KeepOrbId);
                break;
            case MenuAction.RemoveOrb a:
                commands.RemoveOrb(a.OrbId);
                break;
            case MenuAction.OpenOrbSettings a:
                commands.OpenOrbSettings(a.OrbId);
                break;
            case MenuAction.PauseReporting a:
                commands.PauseReporting(a.Indefinitely);
                break;
            case MenuAction.ResumeReporting:
                commands.ResumeReporting();
                break;
            case MenuAction.OpenLeaderboard:
                commands.OpenReportLeaderboard();
                break;
        }
    }

    private static void Fill(ItemCollection items, MenuLevel level, IAppCommands commands, ItemContext context)
    {
        var iconColumn = level.ReservesIconColumn;
        var trailingColumn = level.ReservesTrailingColumn;
        foreach (var entry in level.Entries)
        {
            items.Add(entry is MenuRow row ? Item(row, iconColumn, trailingColumn, commands, context) : new Separator());
        }
    }

    private static MenuItem Item(MenuRow row, bool iconColumn, bool trailingColumn, IAppCommands commands, ItemContext context)
    {
        var item = new MenuItem
        {
            // Labels never carry access keys; a custom orb name may contain '_'.
            Header = row.Label.Replace("_", "__", StringComparison.Ordinal),
            Icon = Icon(row.Icon),
            IsCheckable = row.IsCheckable,
            IsChecked = row.IsChecked,
            IsEnabled = row.IsEnabled,
        };
        item.SetResourceReference(FrameworkElement.StyleProperty, ItemStyleKey);
        MenuChrome.SetIconColumn(item, iconColumn);
        MenuChrome.SetTrailingColumn(item, trailingColumn);
        MenuChrome.SetIsHeader(item, row.IsHeader);
        if (row.IsDefault)
        {
            item.FontWeight = FontWeights.SemiBold;
        }

        if (row.Capsule is { } capsule)
        {
            // The capsule replaces the label, which stays the row's accessible name.
            item.Header = new EffortCapsuleView { Capsule = capsule, Appearance = context.Capsule, LanguageCode = context.LanguageCode };
            AutomationProperties.SetName(item, row.Label);
        }
        else if (row.IsSecondary)
        {
            var text = new TextBlock { Text = row.Label };
            text.SetResourceReference(TextBlock.ForegroundProperty, CapsuleBrushes.SecondaryKey);
            item.Header = text;
            AutomationProperties.SetName(item, row.Label);
        }

        if (row.Detail is { Length: > 0 } detail)
        {
            MenuChrome.SetDetail(item, detail);
            AutomationProperties.SetItemStatus(item, detail);
        }

        if (row.ToolTip is { Length: > 0 } tip)
        {
            item.ToolTip = tip;
            ToolTipService.SetShowOnDisabled(item, true);
        }

        if ((row.ToolTip ?? row.Detail) is { Length: > 0 } help)
        {
            AutomationProperties.SetHelpText(item, help);
        }

        if (row.Submenu is { } submenu)
        {
            Fill(item.Items, submenu, commands, context);
        }
        else if (row.Action is { } action)
        {
            item.Click += (_, _) => Run(action, commands);
        }

        return item;
    }

    private static UIElement? Icon(MenuIcon? icon) => icon switch
    {
        MenuIcon.Glyph glyph => GlyphIcon(glyph.Kind),
        MenuIcon.Dots dots => new MenuDots(dots.Sources),
        MenuIcon.AccentDot => AccentDot(),
        MenuIcon.StatusDot status => StatusDot(status.Tone),
        _ => null,
    };

    /// <summary>A 7 DIP dot in the status color; the row's text always says what it means.</summary>
    private static Ellipse StatusDot(MenuStatusTone tone)
    {
        var dot = new Ellipse { Width = 7, Height = 7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, tone switch
        {
            MenuStatusTone.Green => "SystemFillColorSuccessBrush",
            MenuStatusTone.Amber => "SystemFillColorCautionBrush",
            MenuStatusTone.Red => "SystemFillColorCriticalBrush",
            _ => "TextFillColorTertiaryBrush",
        });
        return dot;
    }

    private static TextBlock GlyphIcon(MenuGlyph glyph)
    {
        var icon = new TextBlock
        {
            Text = glyph switch
            {
                MenuGlyph.Dashboard => "",
                MenuGlyph.History => "",
                MenuGlyph.Settings => "",
                MenuGlyph.About => "",
                MenuGlyph.CheckForUpdates => "",
                _ => "",
            },
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, AppFonts.SymbolFontKey);
        return icon;
    }

    private static Ellipse AccentDot()
    {
        var dot = new Ellipse { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
        return dot;
    }

    /// <summary>What every row of one menu shares.</summary>
    private readonly record struct ItemContext(string LanguageCode, CapsuleAppearance Capsule);
}
