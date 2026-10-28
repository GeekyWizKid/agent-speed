using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>The sub-gauge's text layers (labels, units, <c>≥</c>, the tag), described and drawn by the style's painter.</summary>
public enum BoardGaugePart
{
    LabelIntelligence,
    LabelThinking,
    LabelCost,
    UnitThinking,
    UnitCost,
    AtLeast,
    Tag,
}

/// <summary>
/// The hero's sub-gauge of layout B (DESIGN 2c, Live metrics): 智力 large — glass's pearl arc, neon's LED digits and 12-segment
/// meter, timing's data box — with 思考 and 費用 smaller beneath it. Its numbers roll with the hero's (0.24 s), the arc's end
/// moves and the meter's segments crossfade in the same 0.24 s; 思考 folds away after reading 0 for 3 s. Labels, units,
/// <c>≥</c> and the tag are cached text layers; numbers are value glyphs; the arc, meter and box are instanced shapes.
/// Nothing allocates per frame.
/// </summary>
public sealed class BoardGauge
{
    /// <summary>Room around a gauge text layer's text.</summary>
    public const double TextPad = 2;

    /// <summary>The number cell's sample: the table never moves for values up to this wide.</summary>
    public const string NumberSample = "0.00088";

    /// <summary>A wider value widens its cell at once and it narrows this long after the last value that needed it.</summary>
    public const double WideHoldSeconds = 6;

    public const double ArcSeconds = 0.24;
    public const double MeterSeconds = 0.24;

    private static readonly string[] Keys = ["#g.li", "#g.lt", "#g.lc", "#g.ut", "#g.uc", "#g.ge", "#g.tag"];

    private readonly BoardReadoutNumber _main = new();
    private readonly BoardReadoutNumber _thinking = new();
    private readonly BoardReadoutNumber _cost = new();
    private readonly BoardThinkingFold _fold = new();
    private double _arcFrom;
    private double _arcTo;
    private double _arcStart = double.NegativeInfinity;
    private int _litFrom;
    private int _litTo;
    private double _litStart = double.NegativeInfinity;
    private bool _tag;
    private double _tagStart = double.NegativeInfinity;
    private double _wide;
    private double _wideAt = double.NegativeInfinity;
    private bool _shown;
    private BoardHeroReadouts _readouts = BoardHeroReadouts.Empty;

    /// <summary>The layer key of a part.</summary>
    public static string KeyOf(BoardGaugePart part) => Keys[(int)part];

    public static IReadOnlyList<BoardGaugePart> Parts { get; } = Enum.GetValues<BoardGaugePart>();

    /// <summary>The readouts on display.</summary>
    public BoardHeroReadouts Readouts => _readouts;

    /// <summary>The width the sub-gauge takes beyond its measured cells while a wider value holds its cell (the hero's left column gives way).</summary>
    public double Extra { get; private set; }

    /// <summary>Applies a tick's readouts at <paramref name="now"/>.</summary>
    public void Apply(BoardTick tick, double now, bool animate, bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var readouts = tick.Gauge;
        _readouts = readouts;
        _main.Set(readouts.Intelligence, now, animate, reduceMotion);
        _thinking.Set(readouts.Thinking, now, animate, reduceMotion);
        var tag = readouts.Cost.State == BoardMoneyState.Tag;
        _cost.Set(tag ? string.Empty : readouts.Cost.Number, now, animate && !tag && !_tag, reduceMotion);
        if (tag != _tag)
        {
            _tag = tag;
            _tagStart = animate ? now : double.NegativeInfinity;
        }

        var f = readouts.Shown is { } shown ? Math.Clamp((shown - BoardGaugeSpec.IndexFloor) / BoardGaugeSpec.IndexSpan, 0, 1) : 0;
        if (Math.Abs(f - _arcTo) > 1e-9)
        {
            _arcFrom = animate && !reduceMotion ? Arc(now) : f;
            _arcTo = f;
            _arcStart = animate && !reduceMotion ? now : double.NegativeInfinity;
        }

        var lit = readouts.Shown is { } index
            ? Math.Clamp((int)Math.Floor((index - BoardGaugeSpec.IndexFloor) / 8.0 + 0.5), 0, BoardGaugeSpec.Segments)
            : 0;
        if (lit != _litTo)
        {
            _litFrom = _litTo;
            _litTo = lit;
            _litStart = animate && !reduceMotion ? now : double.NegativeInfinity;
        }

        _shown = readouts.Shown is not null;
        _fold.Set(tick.ThinkingHidden && tick.Readouts.Thinking, now, animate && !reduceMotion);
    }

    public void Settle()
    {
        _main.Settle();
        _thinking.Settle();
        _cost.Settle();
        _fold.Settle();
        _arcStart = double.NegativeInfinity;
        _litStart = double.NegativeInfinity;
        _tagStart = double.NegativeInfinity;
    }

    /// <summary>A roll, the arc, the meter, the tag's crossfade or the fold runs (60 fps; they share the hero roll's frames).</summary>
    public bool Animating(double now) =>
        _main.Animating(now) || _thinking.Animating(now) || _cost.Animating(now) || _fold.Moving(now) || now - _arcStart < ArcSeconds ||
        now - _litStart < MeterSeconds || now - _tagStart < BoardReadoutNumber.FadeSeconds;

    /// <summary>The arc's value fraction on display.</summary>
    public double Arc(double now)
    {
        var t = (now - _arcStart) / ArcSeconds;
        return t is >= 0 and < 1 ? _arcFrom + (_arcTo - _arcFrom) * Easing.CubicBezier(0.2, 0.8, 0.2, 1, t) : _arcTo;
    }

    /// <summary>The cells measured from the layers (labels, <c>≥</c>, units) and the number glyphs, the wider value held 6 s.</summary>
    public BoardGaugeCells Cells(BoardStyle style, BoardSize size, BoardReadouts readouts, IBoardLayers layers, double now)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var spec = BoardGaugeSpec.For(style, size);
        var timing = style == BoardStyle.Timing;
        var label = Math.Max(readouts.Thinking ? Text(layers, BoardGaugePart.LabelThinking) : 0, readouts.Cost ? Text(layers, BoardGaugePart.LabelCost) : 0);
        if (timing && readouts.Intelligence)
        {
            label = Math.Max(label, Text(layers, BoardGaugePart.LabelIntelligence));
        }

        var unit = Math.Max(readouts.Thinking ? Text(layers, BoardGaugePart.UnitThinking) : 0, readouts.Cost ? Text(layers, BoardGaugePart.UnitCost) : 0);
        var tracking = TableTracking(style, spec);
        var sample = BoardValueGlyphs.Measure(layers, NumberSample, BoardValueSet.GaugeTable, tracking);
        var need = Math.Max(readouts.Thinking ? BoardValueGlyphs.Measure(layers, _thinking.Text, BoardValueSet.GaugeTable, tracking) : 0,
            readouts.Cost ? _tag ? Text(layers, BoardGaugePart.Tag) : BoardValueGlyphs.Measure(layers, _cost.Text, BoardValueSet.GaugeTable, tracking) : 0);
        if (need > Math.Max(sample, _wide) + 0.25)
        {
            _wide = need;
            _wideAt = now;
        }
        else if (need > sample + 0.25)
        {
            _wideAt = now;
        }
        else if (_wide > 0 && now - _wideAt >= WideHoldSeconds)
        {
            _wide = 0;
        }

        var number = Math.Max(sample, _wide);
        Extra = Math.Max(0, number - sample);
        return new BoardGaugeCells(label, Text(layers, BoardGaugePart.AtLeast), number, unit);
    }

    /// <summary>
    /// Draws the sub-gauge in a hero whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>) in canvas points, at
    /// <paramref name="alpha"/> (a switch change's crossfade).
    /// </summary>
    public void Emit(BoardDrawList list, IBoardLayers layers, BoardStyle style, BoardSize size, BoardReadouts readouts, BoardHeroLayout hero,
        BoardGaugePalette palette, double x, double y, double alpha, double now)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(hero);
        ArgumentNullException.ThrowIfNull(palette);
        if (!readouts.Hero || alpha <= 0.002)
        {
            return;
        }

        var cells = Cells(style, size, readouts, layers, now);
        var p = BoardGaugeLayout.Place(style, size, readouts, cells, hero.Width, hero.Height, _fold.Glide(now), _fold.Alpha(now));
        var spec = BoardGaugeSpec.For(style, size);
        switch (style)
        {
            case BoardStyle.Timing:
                Timing(list, layers, readouts, spec, p, palette, x, y, alpha, now);
                break;
            default:
                Table(list, layers, style, readouts, spec, p, palette, x, y, alpha, now);
                break;
        }
    }

    // Glass and neon: the dial or the digits and meter, then the table rows.
    private void Table(BoardDrawList list, IBoardLayers layers, BoardStyle style, BoardReadouts readouts, BoardGaugeSpec spec, in BoardGaugePlacement p,
        BoardGaugePalette palette, double x, double y, double alpha, double now)
    {
        var cx = x + p.CenterX;
        var neon = style == BoardStyle.Neon;
        if (readouts.Intelligence)
        {
            if (!neon)
            {
                Dial(list, spec, cx, y + p.DialCenterY, palette, alpha, now);
            }

            Place(list, layers, BoardGaugePart.LabelIntelligence, cx, 0, y + p.MainLabelBaseline, palette.Quiet, alpha);
            var quiet = !_shown;
            var mainTracking = (neon ? -0.03 : -0.02) * spec.MainSize;
            if (neon)
            {
                if (palette.Glows || palette.Ghost.A > 0)
                {
                    Ghost(list, layers, cx, y + p.MainBaseline, mainTracking, palette.Ghost, alpha);
                }

                _main.Emit(list, layers, quiet || !palette.Glows ? BoardValueSet.GaugeMainPlain : BoardValueSet.GaugeMain, cx, 0, y + p.MainBaseline,
                    spec.MainSize, mainTracking, quiet ? palette.Quiet : palette.Number, quiet || !palette.Glows ? 1 : 0, alpha, now);
                Meter(list, spec, cx, y + p.MeterTop, palette, alpha, now);
            }
            else
            {
                _main.Emit(list, layers, BoardValueSet.GaugeMain, cx, 0, y + p.MainBaseline, spec.MainSize, mainTracking,
                    quiet ? palette.Quiet : palette.Number, 1, alpha, now);
            }
        }

        var tracking = TableTracking(style, spec);
        if (readouts.Thinking)
        {
            var a = alpha * p.ThinkingAlpha;
            var thinkingQuiet = !_shown || _readouts.ThinkingZero;
            Place(list, layers, BoardGaugePart.LabelThinking, x + p.TableLeft, -1, y + p.ThinkingBaseline, palette.Quiet, a);
            TableNumber(list, layers, _thinking, neon, palette, thinkingQuiet, x + p.NumberRight, y + p.ThinkingBaseline, spec.TableSize, tracking, a, now);
            Place(list, layers, BoardGaugePart.UnitThinking, x + p.UnitLeft, -1, y + p.ThinkingBaseline, palette.Quiet, a);
        }

        if (readouts.Cost)
        {
            var money = _readouts.Cost;
            var baseline = y + p.CostBaseline;
            Place(list, layers, BoardGaugePart.LabelCost, x + p.TableLeft, -1, baseline, palette.Quiet, alpha);
            if (money.AtLeast)
            {
                Place(list, layers, BoardGaugePart.AtLeast, x + p.AtLeastLeft, -1, baseline, palette.Quiet, alpha);
            }

            Cost(list, layers, neon, palette, x + p.NumberRight, baseline, spec.TableSize, tracking, alpha, now);
            if (money.HasUnit)
            {
                Place(list, layers, BoardGaugePart.UnitCost, x + p.UnitLeft, -1, baseline, palette.Quiet, alpha);
            }
        }
    }

    // Timing's data box: the slab, the tab column, the separators and the inner stroke as shapes; tab labels centred in their
    // rows; every value on one right edge, units and ≥ on the number's baseline.
    private void Timing(BoardDrawList list, IBoardLayers layers, BoardReadouts readouts, BoardGaugeSpec spec, in BoardGaugePlacement p,
        BoardGaugePalette palette, double x, double y, double alpha, double now)
    {
        var left = x + p.Left;
        var top = y + p.Top;
        var right = left + p.Width;
        if (palette.Box.A > 0)
        {
            list.Add(OrbGpuInstance.RoundedRect(left + p.Width / 2, top + p.Height / 2, p.Width / 2, p.Height / 2, 0, 5, palette.Box.MultiplyAlpha(alpha)));
        }

        if (palette.Tab.A > 0 && p.TabWidth > 0)
        {
            var tab = palette.Tab.MultiplyAlpha(alpha);
            list.Add(OrbGpuInstance.RoundedRect(left + p.TabWidth / 2, top + p.Height / 2, p.TabWidth / 2, p.Height / 2, 0, Math.Min(5, p.TabWidth / 2), tab));
            var square = Math.Min(5, p.TabWidth / 2);
            list.Add(OrbGpuInstance.RoundedRect(left + p.TabWidth - square / 2, top + p.Height / 2, square / 2, p.Height / 2, 0, 0, tab));
        }

        var tracking = -0.03 * spec.TableSize;
        var tabX = left + p.TabWidth / 2;
        if (readouts.Intelligence)
        {
            Place(list, layers, BoardGaugePart.LabelIntelligence, tabX, 0, top + p.MainRow / 2, palette.TabMainInk, alpha, centred: true);
            var quiet = !_shown;
            _main.Emit(list, layers, BoardValueSet.GaugeMain, right - spec.RightPad - spec.UnitGap, 1, y + p.MainBaseline, spec.MainSize,
                -0.04 * spec.MainSize, quiet ? palette.Quiet : palette.Number, 1, alpha, now);
        }

        if (readouts.Thinking)
        {
            var a = alpha * p.ThinkingAlpha;
            var rowTop = y + p.ThinkingTop;
            var separator = Math.Abs(p.ThinkingTop - p.Top) > 0.01;
            if (separator)
            {
                Separator(list, left, right, rowTop, palette.BoxLine, a);
            }

            var sep = separator ? 1 : 0;
            Place(list, layers, BoardGaugePart.LabelThinking, tabX, 0, rowTop + sep + (p.SmallRow - sep) / 2, palette.TabInk, a, centred: true);
            var quiet = !_shown || _readouts.ThinkingZero;
            _thinking.Emit(list, layers, BoardValueSet.GaugeTable, x + p.NumberRight, 1, y + p.ThinkingBaseline, spec.TableSize, tracking,
                quiet ? palette.Quiet : palette.Number, 1, a, now);
            Place(list, layers, BoardGaugePart.UnitThinking, x + p.UnitLeft, -1, y + p.ThinkingBaseline, palette.Quiet, a);
        }

        if (readouts.Cost)
        {
            var rowTop = y + p.CostTop;
            var separator = Math.Abs(p.CostTop - p.Top) > 0.01;
            if (separator)
            {
                Separator(list, left, right, rowTop, palette.BoxLine, alpha);
            }

            var sep = separator ? 1 : 0;
            Place(list, layers, BoardGaugePart.LabelCost, tabX, 0, rowTop + sep + (p.SmallRow - sep) / 2, palette.TabInk, alpha, centred: true);
            var baseline = y + p.CostBaseline;
            var width = Cost(list, layers, false, palette, x + p.NumberRight, baseline, spec.TableSize, tracking, alpha, now);
            var money = _readouts.Cost;
            if (money.AtLeast && layers.TryGet(KeyOf(BoardGaugePart.AtLeast), out var ge))
            {
                // ≥ sits right before the number (flex gap 3 and its 1 pt margin).
                Place(list, layers, BoardGaugePart.AtLeast, x + p.NumberRight - width - 4 - (ge.Width - 2 * TextPad), -1, baseline, palette.Quiet, alpha);
            }

            if (money.HasUnit)
            {
                Place(list, layers, BoardGaugePart.UnitCost, x + p.UnitLeft, -1, baseline, palette.Quiet, alpha);
            }
        }

        list.Add(OrbGpuInstance.RoundedRing(left + p.Width / 2, top + p.Height / 2, p.Width / 2 - 0.5, p.Height / 2 - 0.5, 4.5, 1,
            palette.BoxLine.MultiplyAlpha(alpha)));
    }

    // The cost number, or the tag (crossfading with the number).
    private double Cost(BoardDrawList list, IBoardLayers layers, bool neon, BoardGaugePalette palette, double right, double baseline, double size,
        double tracking, double alpha, double now)
    {
        var t = Math.Clamp((now - _tagStart) / BoardReadoutNumber.FadeSeconds, 0, 1);
        var tagAlpha = _tag ? t : 1 - t;
        var width = 0.0;
        if (tagAlpha > 0.002 && layers.TryGet(KeyOf(BoardGaugePart.Tag), out var tag))
        {
            width = tag.Width - 2 * TextPad;
            Place(list, layers, BoardGaugePart.Tag, right, 1, baseline, palette.Quiet, alpha * tagAlpha);
        }

        if (!_tag || tagAlpha < 1)
        {
            var quiet = _readouts.Cost.Quiet;
            width = Math.Max(width, TableNumber(list, layers, _cost, neon, palette, quiet, right, baseline, size, tracking, alpha * (1 - tagAlpha), now));
        }

        return width;
    }

    private static double TableNumber(BoardDrawList list, IBoardLayers layers, BoardReadoutNumber number, bool neon, BoardGaugePalette palette, bool quiet,
        double right, double baseline, double size, double tracking, double alpha, double now)
    {
        var baked = neon && palette.Glows && !quiet;
        var set = neon && !baked ? BoardValueSet.GaugeTablePlain : BoardValueSet.GaugeTable;
        return number.Emit(list, layers, set, right, 1, baseline, size, tracking, quiet ? palette.Quiet : palette.Number, baked ? 0 : 1, alpha, now);
    }

    // Glass's pearl arc: the track over the whole sweep, the value's first f in the pearl gradient with its glow, and the tip.
    private void Dial(BoardDrawList list, BoardGaugeSpec spec, double cx, double cy, BoardGaugePalette palette, double alpha, double now)
    {
        var r = spec.ArcRadius;
        var stroke = spec.ArcStroke;
        list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, BoardGaugeSpec.ArcStart, BoardGaugeSpec.ArcSweep, palette.Track.MultiplyAlpha(alpha),
            roundCaps: true));
        if (!_shown)
        {
            return;
        }

        var f = Arc(now);
        var sweep = BoardGaugeSpec.ArcSweep * f;
        var tipAngle = BoardGaugeSpec.ArcStart + sweep;
        var (tx, ty) = (cx + r * Math.Cos(tipAngle * Math.PI / 180), cy + r * Math.Sin(tipAngle * Math.PI / 180));
        if (sweep > 0.05)
        {
            if (palette.Glows)
            {
                list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, BoardGaugeSpec.ArcStart, sweep, palette.PearlB.WithAlpha(1), roundCaps: true, core: 0,
                    glow: OrbGpuInstance.Glow(1.5), glowAlpha: 0.6 * alpha));
            }

            // The gradient across the dial box (lower left → upper right) in pieces of at most 60°, butt-joined, round caps at both ends.
            var pieces = Math.Max(1, (int)Math.Ceiling(sweep / 60));
            var step = sweep / pieces;
            for (var i = 0; i < pieces; i++)
            {
                var a0 = BoardGaugeSpec.ArcStart + i * step;
                list.Add(OrbGpuInstance.BandArc(cx, cy, r, stroke, a0, step, Pearl(palette, a0 + step, alpha), Pearl(palette, a0, alpha)));
            }

            var (sx, sy) = (cx + r * Math.Cos(BoardGaugeSpec.ArcStart * Math.PI / 180), cy + r * Math.Sin(BoardGaugeSpec.ArcStart * Math.PI / 180));
            list.Add(OrbGpuInstance.Circle(sx, sy, stroke / 2, Pearl(palette, BoardGaugeSpec.ArcStart, alpha)));
            list.Add(OrbGpuInstance.Circle(tx, ty, stroke / 2, Pearl(palette, tipAngle, alpha)));
        }

        var tip = spec.TipRadius;
        if (palette.Glows && palette.Dark)
        {
            list.Add(OrbGpuInstance.Circle(tx, ty, tip, palette.PearlB, 0, OrbGpuInstance.Glow(1.5), alpha));
            list.Add(OrbGpuInstance.Circle(tx, ty, tip, Rgba.White.WithAlpha(alpha)));
        }
        else
        {
            list.Add(OrbGpuInstance.Circle(tx, ty, tip, (palette.Glows ? palette.PearlB : palette.Number).MultiplyAlpha(alpha)));
        }
    }

    // The pearl gradient at an angle of the arc: the SVG's objectBoundingBox gradient from the lower left to the upper right of
    // the arc's box (x ± r, y from −r to +r/2).
    private static Rgba Pearl(BoardGaugePalette palette, double degrees, double alpha)
    {
        if (!palette.Glows)
        {
            return palette.PearlA.MultiplyAlpha(alpha);
        }

        var radians = degrees * Math.PI / 180;
        var u = (Math.Cos(radians) + 1) / 2;
        var v = (Math.Sin(radians) + 1) / 1.5;
        var t = Math.Clamp((u + 1 - v) / 2, 0, 1);
        var color = t < 0.5 ? Rgba.Lerp(palette.PearlC, palette.PearlA, t * 2) : Rgba.Lerp(palette.PearlA, palette.PearlB, (t - 0.5) * 2);
        return color.WithAlpha(alpha);
    }

    // Neon's 12 segments: lit from the left in the mind color (glowing on dark), the change crossfading in 0.24 s.
    private void Meter(BoardDrawList list, BoardGaugeSpec spec, double cx, double top, BoardGaugePalette palette, double alpha, double now)
    {
        var width = spec.Dial;
        var segment = (width - (BoardGaugeSpec.Segments - 1) * BoardGaugeSpec.SegmentGap) / BoardGaugeSpec.Segments;
        var left = cx - width / 2;
        var cy = top + BoardGaugeSpec.SegmentHeight / 2;
        var t = Math.Clamp((now - _litStart) / MeterSeconds, 0, 1);
        for (var i = 0; i < BoardGaugeSpec.Segments; i++)
        {
            var sx = left + i * (segment + BoardGaugeSpec.SegmentGap) + segment / 2;
            var was = i < _litFrom ? 1.0 : 0;
            var now1 = i < _litTo ? 1.0 : 0;
            var lit = was + (now1 - was) * t;
            list.Add(OrbGpuInstance.RoundedRect(sx, cy, segment / 2, BoardGaugeSpec.SegmentHeight / 2, 0, 1, palette.Unlit.MultiplyAlpha(alpha * (1 - lit))));
            if (lit > 0.002)
            {
                list.Add(OrbGpuInstance.RoundedRect(sx, cy, segment / 2, BoardGaugeSpec.SegmentHeight / 2, 0, 1, palette.Mind.MultiplyAlpha(alpha * lit), 1,
                    palette.Glows ? OrbGpuInstance.Glow(2.5) : 0, palette.Glows ? 0.8 * alpha * lit : 0));
            }
        }
    }

    // As many ghost 8s as the number has digits, behind it.
    private void Ghost(BoardDrawList list, IBoardLayers layers, double cx, double baseline, double tracking, Rgba ghost, double alpha)
    {
        var digits = 0;
        foreach (var c in _main.Text)
        {
            digits += c is >= '0' and <= '9' ? 1 : 0;
        }

        if (digits == 0 || ghost.A <= 0 || !BoardValueGlyphs.TryGet('8', BoardValueSet.GaugeMainPlain, layers, out var slot))
        {
            return;
        }

        var pad = BoardValueGlyphs.PadOf(BoardValueSet.GaugeMainPlain);
        var advance = slot.Width - 2 * pad;
        var width = digits * advance + (digits - 1) * tracking;
        var at = cx - width / 2;
        for (var i = 0; i < digits; i++)
        {
            list.AddLayer(BoardLayerInstance.Create(slot, at - pad, baseline - BoardValueGlyphs.Baseline(slot), alpha * ghost.A,
                ghost.WithAlpha(1), 1));
            at += advance + tracking;
        }
    }

    private static void Separator(BoardDrawList list, double left, double right, double top, Rgba color, double alpha) =>
        list.Add(OrbGpuInstance.RoundedRect((left + right) / 2, top + 0.5, (right - left) / 2, 0.5, 0, 0, color.MultiplyAlpha(alpha)));

    // A text layer anchored at x (align −1 left, 0 centre, 1 right) with its baseline at y (or, centred, its middle at y), tinted.
    private static void Place(BoardDrawList list, IBoardLayers layers, BoardGaugePart part, double x, int align, double y, Rgba tint, double alpha,
        bool centred = false)
    {
        if (alpha <= 0.002 || !layers.TryGet(KeyOf(part), out var slot))
        {
            return;
        }

        var width = slot.Width - 2 * TextPad;
        var left = align < 0 ? x : align == 0 ? x - width / 2 : x - width;
        var top = centred ? y - slot.Height / 2 : y - (double.IsFinite(slot.Baseline) ? slot.Baseline : slot.Height / 2);
        list.AddLayer(BoardLayerInstance.Create(slot, left - TextPad, top, alpha * tint.A, tint.WithAlpha(1), 1));
    }

    private static double Text(IBoardLayers layers, BoardGaugePart part) =>
        layers.TryGet(KeyOf(part), out var slot) ? Math.Max(0, slot.Width - 2 * TextPad) : 0;

    /// <summary>The table numbers' letter spacing (glass none, timing −0.03 em, neon −0.02 em).</summary>
    public static double TableTracking(BoardStyle style, BoardGaugeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return style switch
        {
            BoardStyle.Timing => -0.03 * spec.TableSize,
            BoardStyle.Neon => -0.02 * spec.TableSize,
            _ => 0,
        };
    }
}
