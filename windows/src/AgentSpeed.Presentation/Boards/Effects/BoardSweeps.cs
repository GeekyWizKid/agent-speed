using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>One stop of a sweep's gradient: where it lies in the CSS image (0–1) and its color, or the climber's color at that alpha.</summary>
public readonly record struct BoardSweepStop(double At, Rgba Color, bool Source = false);

/// <summary>
/// A style's sweep (the prototypes' <c>.sweep</c> / <c>.wipe</c>): a gradient image <paramref name="Size"/> rows wide slid across
/// the row from CSS background position <paramref name="From"/> to <paramref name="To"/> with <c>cubic-bezier(.3, .7, .2, 1)</c>,
/// added to the row (screen) or laid over it (timing's multiply on light).
/// </summary>
public sealed record BoardSweepLook(double Size, double From, double To, double Seconds, IReadOnlyList<BoardSweepStop> Stops, bool Additive = true);

/// <summary>A band of light crossing one row (canvas points).</summary>
public struct BoardSweep
{
    public float Left;
    public float Right;
    public float CenterY;
    public float HalfHeight;
    public float Age;
    public BoardSweepLook Look;
    public Rgba Source;
}

/// <summary>
/// The light sweeps across overtaking rows (DESIGN 2c Overtake), each in its style's gradient and the climber's source color,
/// clipped to the row. Rows move while they play, so the host passes each sweep's current row center with <see cref="Follow"/>.
/// </summary>
public sealed class BoardSweeps
{
    public const int Capacity = 8;

    private readonly BoardSweep[] _items = new BoardSweep[Capacity];
    private readonly string?[] _keys = new string?[Capacity];

    public int Count { get; private set; }

    public void Clear()
    {
        Count = 0;
        Array.Clear(_items);
        Array.Clear(_keys);
    }

    public void Add(string key, double left, double right, double centerY, double height, Rgba source, BoardSweepLook look)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (Count == Capacity)
        {
            RemoveAt(0);
        }

        _keys[Count] = key;
        _items[Count++] = new BoardSweep
        {
            Left = (float)left,
            Right = (float)right,
            CenterY = (float)centerY,
            HalfHeight = (float)Math.Max(1, height / 2 - 1),
            Look = look,
            Source = source,
        };
    }

    /// <summary>Moves the sweeps of <paramref name="key"/> with their row.</summary>
    public void Follow(string key, double centerY)
    {
        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                _items[i].CenterY = (float)centerY;
            }
        }
    }

    public void Step(double dt)
    {
        var write = 0;
        for (var read = 0; read < Count; read++)
        {
            var sweep = _items[read];
            sweep.Age += (float)dt;
            if (sweep.Age < sweep.Look.Seconds)
            {
                _keys[write] = _keys[read];
                _items[write++] = sweep;
            }
        }

        for (var i = write; i < Count; i++)
        {
            _keys[i] = null;
            _items[i] = default;
        }

        Count = write;
    }

    public void Emit(BoardDrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        for (var i = 0; i < Count; i++)
        {
            ref readonly var sweep = ref _items[i];
            var look = sweep.Look;
            var t = Easing.CubicBezier(0.3, 0.7, 0.2, 1, Math.Clamp(sweep.Age / look.Seconds, 0, 1));
            var width = (double)sweep.Right - sweep.Left;
            // CSS background-position p puts the image's left edge at (1 − size) × p row widths.
            var origin = sweep.Left + width * (1 - look.Size) * (look.From + (look.To - look.From) * t);
            var blend = look.Additive ? OrbBlend.Additive : OrbBlend.Over;
            var stops = look.Stops;
            for (var s = 1; s < stops.Count; s++)
            {
                var a = stops[s - 1];
                var b = stops[s];
                var x0 = origin + width * look.Size * a.At;
                var x1 = origin + width * look.Size * b.At;
                var c0 = Resolve(a, sweep.Source);
                var c1 = Resolve(b, sweep.Source);
                if (x1 <= sweep.Left || x0 >= sweep.Right || x1 - x0 < 0.01)
                {
                    continue;
                }

                // Clipped to the row: the colors at the clipped ends.
                var l = Math.Max(x0, sweep.Left);
                var r = Math.Min(x1, sweep.Right);
                var from = Rgba.Lerp(c0, c1, (l - x0) / (x1 - x0));
                var to = Rgba.Lerp(c0, c1, (r - x0) / (x1 - x0));
                var half = (r - l) / 2;
                list.Add(OrbGpuInstance.RoundedRect(l + half, sweep.CenterY, half, sweep.HalfHeight, 0, 0, from).LinearGradient(to, half, 0), blend);
            }
        }
    }

    private static Rgba Resolve(in BoardSweepStop stop, Rgba source) => stop.Source ? source.WithAlpha(stop.Color.A) : stop.Color;

    private void RemoveAt(int index)
    {
        Array.Copy(_items, index + 1, _items, index, Count - index - 1);
        Array.Copy(_keys, index + 1, _keys, index, Count - index - 1);
        Count--;
        _keys[Count] = null;
    }
}
