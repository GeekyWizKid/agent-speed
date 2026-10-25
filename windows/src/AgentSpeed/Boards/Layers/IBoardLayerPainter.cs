using AgentSpeed.Presentation.Boards.Scene;
using Vortice.Direct2D1;

namespace AgentSpeed.Boards.Layers;

/// <summary>The cached layers of a board (DESIGN 2c, Two layers).</summary>
internal enum BoardLayerKind
{
    /// <summary>A row without its value: rank, badge, project, model, capsule, share.</summary>
    RowBody,

    /// <summary>A row's value, a layer of its own so it can flash and change without redrawing the row.</summary>
    RowValue,

    /// <summary>The hero without its number: unit, title, counts and history.</summary>
    Hero,

    Footer,

    /// <summary>The static pattern behind the content (timing's hatch and footer slab, neon's grid); the whole square.</summary>
    Backdrop,

    /// <summary>One glyph of the hero number (<see cref="BoardLayerRequest.Glyph"/>), padded by <c>BoardHeroNumber.GlyphPad</c>.</summary>
    Glyph,

    /// <summary>A short-lived callout (<see cref="BoardLayerRequest.Callout"/>): a chip, timing's block or banner.</summary>
    Callout,
}

/// <summary>Which layer a painter describes or draws: the row, glyph or callout it is for, and the scene's current tick.</summary>
internal readonly record struct BoardLayerRequest(BoardLayerKind Kind, BoardScene Scene, BoardRowMotion? Row, char Glyph = '\0',
    BoardCallout? Callout = null);

/// <summary>A layer's size in points and its content identity: an unchanged identity is never rasterized again.</summary>
internal readonly record struct BoardLayerSpec(double Width, double Height, string Signature);

/// <summary>
/// Draws a board's layers with Direct2D and DirectWrite, only when their content changes: the styles' painters
/// (<see cref="BoardPainters.Create"/>) or the neutral <see cref="PlaceholderBoardPainter"/>.
/// </summary>
internal interface IBoardLayerPainter
{
    /// <summary>The layer's size and identity, or null when the board shows no such layer.</summary>
    BoardLayerSpec? Describe(in BoardLayerRequest request);

    /// <summary>Draws the layer in points from its top-left corner; the box is already clear.</summary>
    void Paint(ID2D1DeviceContext context, in BoardLayerRequest request, in BoardLayerSpec spec);
}
