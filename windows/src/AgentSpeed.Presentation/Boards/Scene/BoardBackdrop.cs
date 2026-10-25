using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// The rounded square's fill and edge per style (the prototypes' <c>.panel</c>, dark theme). Windows draws glass as a tinted
/// translucent fill (DESIGN 2c: an acrylic blur only if it costs nothing per frame), so its fill is denser than the
/// prototype's 0.58 over a 30 px backdrop blur. Phase 2 adds each style's own layers (aurora, rail, grid, tube) and light.
/// </summary>
public static class BoardBackdrop
{
    public static Rgba Fill(BoardStyle style) => style switch
    {
        BoardStyle.Timing => Rgba.FromHex("#0C0E14"),
        BoardStyle.Neon => Rgba.FromHex("#05060C"),
        _ => new Rgba(20 / 255.0, 22 / 255.0, 33 / 255.0, 0.86),
    };

    public static Rgba Edge(BoardStyle style) => style switch
    {
        BoardStyle.Timing => Rgba.White.WithAlpha(0.09),
        BoardStyle.Neon => new Rgba(140 / 255.0, 170 / 255.0, 1, 0.10),
        _ => Rgba.White.WithAlpha(0.13),
    };

    public static void Emit(BoardDrawList list, in BoardCanvas canvas, BoardStyle style, bool highContrast)
    {
        var center = canvas.Center;
        var fill = Fill(style);
        list.Clip = BoardClip.None;
        list.Add(OrbGpuInstance.RoundedRect(center, center, canvas.Half, canvas.Half, 0, canvas.Radius, highContrast ? fill.WithAlpha(1) : fill));
        list.Add(OrbGpuInstance.RoundedRing(center, center, canvas.Half - 0.5, canvas.Half - 0.5, canvas.Radius - 0.5, 1,
            highContrast ? Rgba.White : Edge(style)));
    }
}
