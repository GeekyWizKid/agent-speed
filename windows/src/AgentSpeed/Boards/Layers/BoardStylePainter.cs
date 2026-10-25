using System.Globalization;
using System.Numerics;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Modes;
using Vortice;
using Vortice.Direct2D1;

namespace AgentSpeed.Boards.Layers;

/// <summary>What a row layer shows, resolved once per row from the tick.</summary>
/// <param name="Column">The shared capsule column's width (the prototypes' <c>--capw</c>: the widest visible capsule).</param>
/// <param name="Fastest">Timing: this row's agent is the fastest of all (the purple dot).</param>
/// <param name="Interval">Timing: the interval to the group above, or <c>boards.leader</c> on the first.</param>
internal readonly record struct BoardRowContent(
    BoardScene Scene,
    BoardTick Tick,
    BoardPalette Palette,
    BoardEntry Entry,
    double Live,
    string Name,
    string Detail,
    AgentPhase Phase,
    SourceKind Source,
    EffortCapsule Capsule,
    float Column,
    bool Fastest,
    string Interval)
{
    public bool Lead => Entry.Kind == BoardEntryKind.Group && Entry.Rank == 1;

    public bool Compact => Scene.Size == BoardSize.S;
}

/// <summary>
/// The shared part of the three styles' painters (DESIGN 2c Content): layer sizes and signatures, what each row says, the
/// capsule column, and the parts every style draws alike (glyph layers, the elbow of sub rows, the more lines' dots).
/// A layer's signature holds everything it shows, so an unchanged layer is never rasterized again.
/// </summary>
internal abstract class BoardStylePainter : IDisposable
{
    private BoardTick? _columnTick;
    private float _column;
    private string? _fastest;

    protected BoardStylePainter(BoardStyle style, Localizer localizer, BoardText text)
    {
        Style = style;
        Loc = localizer;
        Text = text;
        Capsule = new BoardModeCapsule(text);
    }

    public BoardStyle Style { get; }

    protected Localizer Loc { get; }

    protected BoardText Text { get; }

    protected BoardModeCapsule Capsule { get; }

    /// <summary>Width of the value column's layer at the row's right end.</summary>
    protected abstract float ValueWidth(BoardSize size);

    protected abstract BoardCapsuleLook CapsuleLook(BoardScene scene, bool sub);

    /// <summary>The hero number's text style.</summary>
    protected abstract BoardTextStyle NumberStyle(BoardScene scene);

    public BoardLayerSpec? Describe(in BoardLayerRequest request)
    {
        var scene = request.Scene;
        if (scene.Tick is not { } tick)
        {
            return null;
        }

        Text.Locale = Loc.Culture.Name;
        var metrics = scene.Metrics;
        var theme = Theme(scene);
        switch (request.Kind)
        {
            case BoardLayerKind.RowBody when request.Row is { } row:
            {
                var content = Content(scene, tick, row.Entry, row.Live);
                var width = metrics.ListWidth - (row.Entry.Kind is BoardEntryKind.Group or BoardEntryKind.Sub ? ValueWidth(scene.Size) : 0);
                var more = row.Entry.Kind is BoardEntryKind.MoreSubs or BoardEntryKind.MoreGroups ? MoreText(row.Entry, row.Live) : string.Empty;
                return new BoardLayerSpec(width, scene.RowHeight(row.Entry), string.Join('|', theme, "b", row.Entry.Kind, row.Entry.Rank,
                    content.Name, content.Detail, content.Phase, content.Source, content.Capsule.Effort, content.Capsule.Speed,
                    content.Column.ToString("0.#", CultureInfo.InvariantCulture), content.Fastest, content.Interval, more, BodyKey(content)));
            }

            case BoardLayerKind.RowValue when request.Row is { Entry.Kind: BoardEntryKind.Group or BoardEntryKind.Sub } row:
            {
                var content = Content(scene, tick, row.Entry, row.Live);
                return new BoardLayerSpec(ValueWidth(scene.Size), scene.RowHeight(row.Entry),
                    string.Join('|', theme, "v", row.Entry.Kind, ValueText(row.Live), ValueKey(content)));
            }

            case BoardLayerKind.Hero:
            {
                var hero = scene.Hero;
                return new BoardLayerSpec(hero.Width, hero.Height, string.Join('|', theme, "h", tick.ValueText, tick.Title, tick.GeneratingAgents,
                    tick.ToolAgents, tick.Generating, HeatKey(tick.Heat), HistoryKey(tick.History), HeatKey(HeatMath.Heat(tick.PeakToday, tick.Redline))));
            }

            case BoardLayerKind.Footer:
                return new BoardLayerSpec(metrics.Side - 2 * metrics.Pad, metrics.FooterHeight,
                    string.Join('|', theme, "f", tick.TodayText, tick.CostText, tick.PeakText));

            case BoardLayerKind.Backdrop:
                return DescribeBackdrop(scene, theme);

            case BoardLayerKind.Glyph:
            {
                var style = NumberStyle(scene);
                var advance = Text.Measure(request.Glyph.ToString(), style);
                var pad = BoardHeroNumber.GlyphPad;
                return new BoardLayerSpec(advance + 2 * pad, scene.Hero.NumberHeight + 2 * pad,
                    string.Join('|', theme, "g", request.Glyph, GlyphKey(scene)));
            }

            case BoardLayerKind.Callout when request.Callout is { } callout:
                return DescribeCallout(scene, callout, theme);

            default:
                return null;
        }
    }

    public void Paint(ID2D1DeviceContext context, in BoardLayerRequest request, in BoardLayerSpec spec)
    {
        var scene = request.Scene;
        if (scene.Tick is not { } tick)
        {
            return;
        }

        context.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        var width = (float)spec.Width;
        var height = (float)spec.Height;
        switch (request.Kind)
        {
            case BoardLayerKind.RowBody when request.Row is { } row:
            {
                var content = Content(scene, tick, row.Entry, row.Live);
                switch (row.Entry.Kind)
                {
                    case BoardEntryKind.Group:
                        PaintGroup(context, content, width, height);
                        break;
                    case BoardEntryKind.Sub:
                        PaintSub(context, content, width, height);
                        break;
                    default:
                        PaintMore(context, content, MoreText(row.Entry, row.Live), width, height);
                        break;
                }

                break;
            }

            case BoardLayerKind.RowValue when request.Row is { } row:
                PaintValue(context, Content(scene, tick, row.Entry, row.Live), width, height);
                break;
            case BoardLayerKind.Hero:
                PaintHero(context, scene, tick, width, height);
                break;
            case BoardLayerKind.Footer:
                PaintFooter(context, scene, tick, width, height);
                break;
            case BoardLayerKind.Backdrop:
                PaintBackdrop(context, scene, width, height);
                break;
            case BoardLayerKind.Glyph:
                PaintGlyph(context, scene, request.Glyph, width, height);
                break;
            case BoardLayerKind.Callout when request.Callout is { } callout:
                PaintCallout(context, scene, callout, width, height);
                break;
        }
    }

    public virtual void Dispose()
    {
    }

    protected abstract void PaintGroup(ID2D1DeviceContext context, in BoardRowContent row, float width, float height);

    protected abstract void PaintSub(ID2D1DeviceContext context, in BoardRowContent row, float width, float height);

    protected abstract void PaintMore(ID2D1DeviceContext context, in BoardRowContent row, string text, float width, float height);

    protected abstract void PaintValue(ID2D1DeviceContext context, in BoardRowContent row, float width, float height);

    protected abstract void PaintHero(ID2D1DeviceContext context, BoardScene scene, BoardTick tick, float width, float height);

    protected abstract void PaintFooter(ID2D1DeviceContext context, BoardScene scene, BoardTick tick, float width, float height);

    protected abstract void PaintGlyph(ID2D1DeviceContext context, BoardScene scene, char glyph, float width, float height);

    protected abstract BoardLayerSpec? DescribeCallout(BoardScene scene, BoardCallout callout, string theme);

    protected abstract void PaintCallout(ID2D1DeviceContext context, BoardScene scene, BoardCallout callout, float width, float height);

    protected virtual BoardLayerSpec? DescribeBackdrop(BoardScene scene, string theme) => null;

    protected virtual void PaintBackdrop(ID2D1DeviceContext context, BoardScene scene, float width, float height)
    {
    }

    /// <summary>Extra identity of a row body (what a style draws beyond the shared content).</summary>
    protected virtual string BodyKey(in BoardRowContent row) => string.Empty;

    /// <summary>Identity of a value's color (glass and neon color hot values by heat).</summary>
    protected virtual string ValueKey(in BoardRowContent row) => string.Empty;

    /// <summary>Identity of the glyphs' look beyond the theme (a heat-colored glow).</summary>
    protected virtual string GlyphKey(BoardScene scene) => string.Empty;

    /// <summary>A row's value (METRICS 7), or an en dash below 0.05 (never shown as a number).</summary>
    protected static string ValueText(double live) => live > 0.05 ? SpeedFormat.Speed(live) : "–";

    protected static int HeatKey(double heat) => (int)Math.Round(Math.Clamp(heat, 0, 1) * 32);

    /// <summary>Text on a value: the palette's heat (darkened on light) above heat 0.45, as the prototypes color hot values.</summary>
    protected static Rgba? HotValue(in BoardRowContent row)
    {
        var heat = HeatMath.Heat(row.Live, row.Tick.Redline);
        return row.Live > 0.05 && heat > 0.45 && !row.Palette.HighContrast ? EffortCapsulePalette.Heat(heat, row.Palette.Dark) : null;
    }

    /// <summary>The heat color of the board (the prototypes' <c>--heat</c>, at least <paramref name="floor"/>).</summary>
    protected static Rgba HeatColor(BoardTick tick, double floor = 0.06) => HeatPalette.Sample(Math.Max(floor, tick.Heat));

    protected static Rgba SourceColor(SourceKind source) => HeatPalette.SourceColor(source);

    protected string MoreText(BoardEntry entry, double live) =>
        Loc.Format(entry.Kind == BoardEntryKind.MoreSubs ? "boards.moreSubs" : "boards.moreGroups", entry.Rest, SpeedFormat.Speed(live));

    /// <summary>Draws the capsule right-aligned in the shared column ending at <paramref name="right"/>; returns the column's left.</summary>
    protected float DrawCapsule(ID2D1DeviceContext context, in BoardRowContent row, float right, float mid, float height)
    {
        if (row.Column <= 0)
        {
            return right;
        }

        var left = right - row.Column;
        if (row.Capsule.IsVisible)
        {
            Capsule.Look = CapsuleLook(row.Scene, row.Entry.Kind == BoardEntryKind.Sub);
            Capsule.Paint(context, row.Capsule, new RawRectF(left, mid - height / 2, right, mid + height / 2));
        }

        return left;
    }

    /// <summary>
    /// A name and its detail on one baseline (the prototypes' <c>.name b</c> and <c>.name small</c>, 6 pt apart): the name keeps
    /// its width while it fits (at least 22 pt), the detail takes what remains; both trim with an ellipsis.
    /// </summary>
    protected void DrawName(ID2D1DeviceContext context, string name, in BoardTextStyle nameStyle, Rgba nameColor, string detail,
        in BoardTextStyle detailStyle, Rgba detailColor, float x, float right, float mid)
    {
        var room = right - x;
        if (room <= 4)
        {
            return;
        }

        using var nameLayout = Text.Layout(name, nameStyle);
        var nameWidth = nameLayout.Metrics.WidthIncludingTrailingWhitespace;
        var shown = string.IsNullOrEmpty(detail) ? Math.Min(nameWidth, room) : Math.Max(Math.Min(22, room), Math.Min(nameWidth, room - 6));
        using var nameBrush = BoardPaint.Brush(context, nameColor);
        using var fitted = Text.Layout(name, nameStyle, shown);
        var top = BoardText.Top(fitted, mid);
        context.DrawTextLayout(new Vector2(x, top), fitted, nameBrush, DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
        var rest = room - shown - 6;
        if (string.IsNullOrEmpty(detail) || rest < 8)
        {
            return;
        }

        using var detailLayout = Text.Layout(detail, detailStyle, rest);
        var baseline = top + (fitted.LineMetrics.Length > 0 ? fitted.LineMetrics[0].Baseline : 0);
        var detailTop = baseline - (detailLayout.LineMetrics.Length > 0 ? detailLayout.LineMetrics[0].Baseline : 0);
        using var detailBrush = BoardPaint.Brush(context, detailColor);
        context.DrawTextLayout(new Vector2(x + shown + 6, detailTop), detailLayout, detailBrush, DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
    }

    /// <summary>
    /// A footer item from a localized format (<c>boards.today</c>, <c>boards.peak</c>): the label in <paramref name="label"/>, the
    /// value in <paramref name="value"/>, then <paramref name="suffix"/>; aligned at <paramref name="x"/> by <paramref name="align"/>
    /// (−1 left, 0 center, 1 right).
    /// </summary>
    protected void DrawLabeled(ID2D1DeviceContext context, string format, string valueText, string suffix, in BoardTextStyle label,
        Rgba labelColor, in BoardTextStyle value, Rgba valueColor, float x, float mid, int align)
    {
        var at = format.IndexOf("%@", StringComparison.Ordinal);
        var before = at < 0 ? string.Empty : format[..at];
        var after = (at < 0 ? string.Empty : format[(at + 2)..]) + suffix;
        var widths = Text.Measure(before, label) + Text.Measure(valueText, value) + Text.Measure(after, label);
        var left = align < 0 ? x : align == 0 ? x - widths / 2 : x - widths;
        using var labelBrush = BoardPaint.Brush(context, labelColor);
        using var valueBrush = BoardPaint.Brush(context, valueColor);
        left += Text.Draw(context, before, label, left, mid, labelBrush);
        left += Text.Draw(context, valueText, value, left, mid, valueBrush);
        Text.Draw(context, after, label, left, mid, labelBrush);
    }

    /// <summary>The prototypes' sub-row elbow (<c>.sub .name::before</c>): down from the row above, then right, 5 pt rounded.</summary>
    protected static void Elbow(ID2D1DeviceContext context, float x, float mid, Rgba color)
    {
        using var brush = BoardPaint.Brush(context, color);
        using var factory = context.Factory;
        using var path = factory.CreatePathGeometry();
        using (var sink = path.Open())
        {
            sink.BeginFigure(new Vector2(x + 0.5f, 0), FigureBegin.Hollow);
            sink.AddLine(new Vector2(x + 0.5f, mid - 5));
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(x + 5.5f, mid - 0.5f),
                Size = new Vortice.Mathematics.Size(5, 5),
                SweepDirection = SweepDirection.CounterClockwise,
                ArcSize = ArcSize.Small,
            });
            sink.AddLine(new Vector2(x + 7, mid - 0.5f));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }

        context.DrawGeometry(path, brush, 1);
    }

    /// <summary>The more lines' stack of three source dots (<c>.more .stack</c>).</summary>
    protected static float Stack(ID2D1DeviceContext context, float x, float mid, float size, float overlap, Rgba color, Rgba ring, float glow)
    {
        for (var i = 0; i < 3; i++)
        {
            var cx = x + size / 2 + i * (size - overlap);
            if (glow > 0)
            {
                BoardPaint.Glow(context, color, glow, c =>
                {
                    using var white = BoardPaint.Brush(c, Rgba.White);
                    c.FillEllipse(new Ellipse(new Vector2(cx, mid), size / 2, size / 2), white);
                });
            }

            if (ring.A > 0)
            {
                using var ringBrush = BoardPaint.Brush(context, ring);
                context.FillEllipse(new Ellipse(new Vector2(cx, mid), size / 2 + 1.5f, size / 2 + 1.5f), ringBrush);
            }

            using var brush = BoardPaint.Brush(context, color.MultiplyAlpha(0.85));
            context.FillEllipse(new Ellipse(new Vector2(cx, mid), size / 2, size / 2), brush);
        }

        return 3 * size - 2 * overlap;
    }

    /// <summary>The phase badge in its 10 pt box at (<paramref name="x"/>, centered on <paramref name="mid"/>).</summary>
    protected static void Badge(ID2D1DeviceContext context, AgentPhase phase, Rgba source, Rgba idle, float x, float mid, bool neon, bool glows)
    {
        switch (phase)
        {
            case AgentPhase.Generating when neon:
            {
                if (glows)
                {
                    BoardPaint.Glow(context, source, 12, c => Dot(c, x + 5, mid, 3, Rgba.White));
                }

                Dot(context, x + 5, mid, 3, source);
                break;
            }

            case AgentPhase.Generating:
                // Three 3 pt dots (the shimmer's resting frame).
                for (var i = 0; i < 3; i++)
                {
                    Dot(context, x - 1 + 1.5f + i * 4.5f, mid + 0.5f, 1.5f, source.MultiplyAlpha(0.9));
                }

                break;
            case AgentPhase.Tools:
            {
                var transform = context.Transform;
                context.Transform = Matrix3x2.CreateRotation(MathF.PI / 4, new Vector2(x + 5, mid)) * transform;
                if (neon && glows)
                {
                    BoardPaint.Glow(context, source, 6, c => BoardPaint.StrokeRound(c, x + 2.12f, mid - 2.88f, 5.76f, 5.76f, 2.2f, Rgba.White, 1.1f));
                }

                BoardPaint.StrokeRound(context, x + 2.12f, mid - 2.88f, 5.76f, 5.76f, 2.2f, source.MultiplyAlpha(0.9), 1.1f);
                context.Transform = transform;
                break;
            }

            default:
                Dot(context, x + 5, mid, 2, idle);
                break;
        }
    }

    protected static void Dot(ID2D1DeviceContext context, float cx, float cy, float radius, Rgba color)
    {
        using var brush = BoardPaint.Brush(context, color);
        context.FillEllipse(new Ellipse(new Vector2(cx, cy), radius, radius), brush);
    }

    /// <summary>One glyph of the hero number, its line centered half an em below the box's top (CSS <c>line-height: 1</c>).</summary>
    protected void DrawGlyph(ID2D1DeviceContext context, BoardScene scene, char glyph, Rgba color, Action<ID2D1DeviceContext, Action<ID2D1DeviceContext>>? glow)
    {
        var style = NumberStyle(scene);
        var pad = (float)BoardHeroNumber.GlyphPad;
        var text = glyph.ToString();
        var mid = pad + style.Size / 2;
        glow?.Invoke(context, c =>
        {
            using var white = BoardPaint.Brush(c, Rgba.White);
            Text.Draw(c, text, style, pad, mid, white);
        });
        using var brush = BoardPaint.Brush(context, color);
        Text.Draw(context, text, style, pad, mid, brush);
    }

    /// <summary>The baseline of the hero number in hero coordinates (the unit sits on it).</summary>
    protected float NumberBaseline(BoardScene scene)
    {
        var style = NumberStyle(scene);
        using var layout = Text.Layout("0", style);
        return BoardText.Baseline(layout, (float)scene.Hero.NumberTop + style.Size / 2);
    }

    /// <summary>The number's advance width as the scene composes it.</summary>
    protected float NumberWidth(BoardScene scene, string text)
    {
        var style = NumberStyle(scene);
        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            width += Text.Measure(text[i].ToString(), style) + (i > 0 ? (float)scene.Hero.Tracking : 0);
        }

        return width;
    }

    /// <summary>
    /// The row's content from the tick (cached per tick: the capsule column and the fastest agent).
    /// </summary>
    protected BoardRowContent Content(BoardScene scene, BoardTick tick, BoardEntry entry, double live)
    {
        if (!ReferenceEquals(tick, _columnTick))
        {
            _columnTick = tick;
            _column = Column(scene, tick);
            _fastest = Fastest(tick);
        }

        var palette = scene.Palette;
        var agent = entry.Kind == BoardEntryKind.Group ? entry.Group.Root : entry.Agent;
        var capsule = agent is null || entry.Kind is BoardEntryKind.MoreSubs or BoardEntryKind.MoreGroups ? new EffortCapsule(null, null)
            : EffortCapsule.Of(agent.Modes);
        string name;
        var detail = string.Empty;
        var phase = AgentPhase.Idle;
        var interval = string.Empty;
        switch (entry.Kind)
        {
            case BoardEntryKind.Group:
            {
                var root = entry.Group.Root;
                name = root.Project;
                var subs = entry.Group.Subs.Count;
                detail = subs > 0 ? string.Join(" · ", new[] { root.Model, Loc.Format("boards.subCount", subs) }.Where(s => !string.IsNullOrEmpty(s)))
                    : root.Model ?? string.Empty;
                phase = entry.Group.BadgePhase;
                interval = Interval(tick, entry);
                break;
            }

            case BoardEntryKind.Sub when agent is not null:
                // A sub-agent shows what tells it apart: its own project, else its model.
                name = !string.Equals(agent.Project, entry.Group.Root.Project, StringComparison.Ordinal) ? agent.Project : agent.Model ?? agent.Project;
                phase = agent.Phase;
                break;
            default:
                name = string.Empty;
                break;
        }

        return new BoardRowContent(scene, tick, palette, entry, live, name, detail, phase, agent?.Source ?? entry.Group.Root.Source, capsule,
            _column, agent is not null && string.Equals(agent.Id, _fastest, StringComparison.Ordinal), interval);
    }

    private string Theme(BoardScene scene) => string.Join('|', scene.Style, scene.Size, scene.Dark ? "d" : "l",
        scene.Palette.HighContrast ? "hc" : string.Empty, Loc.LanguageCode);

    private float Column(BoardScene scene, BoardTick tick)
    {
        var width = 0f;
        foreach (var entry in tick.Entries)
        {
            var agent = entry.Kind switch
            {
                BoardEntryKind.Group => entry.Group.Root,
                BoardEntryKind.Sub => entry.Agent,
                _ => null,
            };
            if (agent is null || agent.Modes.IsEmpty)
            {
                continue;
            }

            Capsule.Look = CapsuleLook(scene, entry.Kind == BoardEntryKind.Sub);
            width = Math.Max(width, Capsule.Measure(EffortCapsule.Of(agent.Modes)));
        }

        return MathF.Ceiling(width);
    }

    private static string? Fastest(BoardTick tick)
    {
        AgentStatus? fastest = null;
        var best = 0.0;
        foreach (var entry in tick.Entries)
        {
            if (entry.Kind != BoardEntryKind.Group)
            {
                continue;
            }

            foreach (var agent in entry.Group.Subs.Prepend(entry.Group.Root))
            {
                var live = agent.Live.ValueFor(tick.Metric);
                if (live > best)
                {
                    best = live;
                    fastest = agent;
                }
            }
        }

        return fastest?.Id;
    }

    // Timing's interval column: the gap to the group above (−29 when behind by 29, +20 when ahead inside the 10 % band).
    private string Interval(BoardTick tick, BoardEntry entry)
    {
        if (entry.Rank == 1)
        {
            return Loc["boards.leader"];
        }

        foreach (var other in tick.Entries)
        {
            if (other.Kind == BoardEntryKind.Group && other.Rank == entry.Rank - 1)
            {
                var gap = other.Group.Live - entry.Group.Live;
                return (gap >= 0 ? "−" : "+") + SpeedFormat.Speed(Math.Abs(gap));
            }
        }

        return string.Empty;
    }

    private static int HistoryKey(IReadOnlyList<double> history)
    {
        var hash = new HashCode();
        hash.Add(history.Count);
        for (var i = Math.Max(0, history.Count - 60); i < history.Count; i++)
        {
            hash.Add(Math.Round(history[i], 1));
        }

        return hash.ToHashCode();
    }
}
