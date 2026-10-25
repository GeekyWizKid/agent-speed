using AgentSpeed.Boards.Layers;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;

namespace AgentSpeed.Boards.Gpu;

/// <summary>
/// Keeps a board's layer texture in step with its scene (DESIGN 2c, Two layers): after each tick only the layers whose
/// content changed are rasterized; layers of rows that are gone are dropped; a full texture is cleared, grown when that is
/// not enough, and drawn again.
/// </summary>
internal sealed class BoardLayerCache : IBoardLayers, IDisposable
{
    private const int InitialHeight = 1024;

    private readonly BoardLayerAtlas _atlas = new();
    private readonly HashSet<string> _live = new(StringComparer.Ordinal);
    private readonly List<string> _stale = [];
    private int _height = InitialHeight;

    public BoardLayerCache(IBoardLayerPainter painter)
    {
        Painter = painter;
    }

    public IBoardLayerPainter Painter { get; set; }

    public BoardLayerAtlas Atlas => _atlas;

    /// <summary>Whether the texture must be made current again before the next frame (device, scale or size changed).</summary>
    public bool IsStale(GpuDevice device, double scale, BoardScene scene) =>
        _atlas.View is null || _atlas.Width != WidthFor(scene, scale) || Math.Abs(_atlas.Scale - scale) > 0.001 || Generation != device.Generation;

    private int Generation { get; set; } = -1;

    public bool TryGet(string key, out BoardLayerSlot slot) => _atlas.TryGet(key, out slot);

    /// <summary>Rasterizes what changed since the last call; false when the device was lost.</summary>
    public bool Update(GpuDevice device, BoardScene scene, double scale)
    {
        if (scene.Tick is null)
        {
            return true;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var width = WidthFor(scene, scale);
            _atlas.Ensure(device, scale, width, Math.Max(_height, width));
            Generation = device.Generation;
            switch (Draw(scene))
            {
                case DrawResult.Done:
                    return true;
                case DrawResult.Lost:
                    return false;
            }

            // Full: start over with every live layer, in a taller texture the second time.
            if (attempt > 0)
            {
                _height = Math.Min(BoardLayerAtlas.MaxHeight, _height * 2);
            }

            _atlas.Clear();
        }

        return true;
    }

    public void Dispose()
    {
        _atlas.Dispose();
        (Painter as IDisposable)?.Dispose();
    }

    private DrawResult Draw(BoardScene scene)
    {
        _live.Clear();
        _atlas.BeginDraw();
        var full = false;
        foreach (var row in scene.Rows)
        {
            full |= !Layer(row.Key, new BoardLayerRequest(BoardLayerKind.RowBody, scene, row));
            full |= !Layer(row.ValueKey, new BoardLayerRequest(BoardLayerKind.RowValue, scene, row));
            if (full)
            {
                break;
            }
        }

        if (!full)
        {
            full |= !Layer(BoardScene.HeroKey, new BoardLayerRequest(BoardLayerKind.Hero, scene, null));
            full |= !Layer(BoardScene.FooterKey, new BoardLayerRequest(BoardLayerKind.Footer, scene, null));
            full |= !Layer(BoardScene.BackdropKey, new BoardLayerRequest(BoardLayerKind.Backdrop, scene, null));
            for (var i = 0; i < BoardHeroNumber.Glyphs.Length && !full; i++)
            {
                full |= !Layer(BoardHeroNumber.GlyphKeys[i], new BoardLayerRequest(BoardLayerKind.Glyph, scene, null, BoardHeroNumber.Glyphs[i]));
            }

            foreach (var callout in scene.Callouts)
            {
                full |= !Layer(callout.Key, new BoardLayerRequest(BoardLayerKind.Callout, scene, null, Callout: callout));
            }
        }

        if (!_atlas.EndDraw())
        {
            return DrawResult.Lost;
        }

        if (full)
        {
            return DrawResult.Full;
        }

        _stale.Clear();
        foreach (var key in _atlas.Keys)
        {
            if (!_live.Contains(key))
            {
                _stale.Add(key);
            }
        }

        foreach (var key in _stale)
        {
            _atlas.Remove(key);
        }

        return DrawResult.Done;
    }

    private bool Layer(string key, BoardLayerRequest request)
    {
        if (Painter.Describe(request) is not { } spec)
        {
            return true;
        }

        _live.Add(key);
        if (_atlas.Holds(key, spec.Signature, spec.Width, spec.Height))
        {
            return true;
        }

        var painter = Painter;
        return _atlas.TryDraw(key, spec.Width, spec.Height, spec.Signature, context => painter.Paint(context, request, spec));
    }

    private static int WidthFor(BoardScene scene, double scale)
    {
        var pixels = (int)Math.Ceiling(scene.Canvas.Side * scale) + 2 * BoardAtlasPacker.Gutter;
        return (pixels + 15) / 16 * 16;
    }

    private enum DrawResult
    {
        Done,
        Full,
        Lost,
    }
}
