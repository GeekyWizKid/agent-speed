using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>A band of light crossing one row (canvas points).</summary>
public struct BoardSweep
{
    public float Left;
    public float Right;
    public float CenterY;
    public float HalfHeight;
    public float Age;
    public float Life;
    public Rgba Color;
}

/// <summary>
/// The light sweep across an overtaking row (DESIGN 2c Overtake; the prototypes' <c>.sweep</c> / <c>.wipe</c>: a soft band
/// from the left edge to the right one with <c>cubic-bezier(.3, .7, .2, 1)</c>), in the climber's source color lifted
/// toward white. Rows move while it plays, so the host passes each sweep's current row top with <see cref="Follow"/>.
/// </summary>
public sealed class BoardSweeps
{
    public const int Capacity = 8;
    private const double BandHalfWidth = 16;

    private readonly BoardSweep[] _items = new BoardSweep[Capacity];
    private readonly string?[] _keys = new string?[Capacity];

    public int Count { get; private set; }

    public void Clear()
    {
        Count = 0;
        Array.Clear(_keys);
    }

    public void Add(string key, double left, double right, double centerY, double height, Rgba color, double seconds)
    {
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
            Life = (float)Math.Max(0.05, seconds),
            Color = Rgba.Lerp(color, Rgba.White, 0.45),
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
            if (sweep.Age < sweep.Life)
            {
                _keys[write] = _keys[read];
                _items[write++] = sweep;
            }
        }

        for (var i = write; i < Count; i++)
        {
            _keys[i] = null;
        }

        Count = write;
    }

    public void Emit(BoardDrawList list)
    {
        for (var i = 0; i < Count; i++)
        {
            ref readonly var sweep = ref _items[i];
            var t = Easing.CubicBezier(0.3, 0.7, 0.2, 1, Math.Clamp(sweep.Age / sweep.Life, 0, 1));
            var x = sweep.Left - BandHalfWidth + (sweep.Right - sweep.Left + 2 * BandHalfWidth) * t;
            var peak = sweep.Color.WithAlpha(0.4 * (1 - t * 0.5));
            var clear = sweep.Color.WithAlpha(0);
            var half = BandHalfWidth / 2;
            list.Add(OrbGpuInstance.RoundedRect(x - half, sweep.CenterY, half, sweep.HalfHeight, 0, 0, clear).LinearGradient(peak, half, 0),
                OrbBlend.Additive);
            list.Add(OrbGpuInstance.RoundedRect(x + half, sweep.CenterY, half, sweep.HalfHeight, 0, 0, peak).LinearGradient(clear, half, 0),
                OrbBlend.Additive);
        }
    }

    private void RemoveAt(int index)
    {
        Array.Copy(_items, index + 1, _items, index, Count - index - 1);
        Array.Copy(_keys, index + 1, _keys, index, Count - index - 1);
        Count--;
        _keys[Count] = null;
    }
}
