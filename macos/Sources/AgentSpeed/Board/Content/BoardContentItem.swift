import AgentSpeedCore
import CoreGraphics

/// One cached piece of a board's content (DESIGN 2c Two layers): the hero, a ranking row, the footer. Laid out and
/// rasterized only when `revision` changes; frames only move the cached layer.
struct BoardContentItem {
    struct Plate: Equatable {
        var color: CGColor
        var cornerRadius: CGFloat
    }

    enum Motion {
        /// Stays put (hero, footer).
        case none
        /// Springs to new places, fades in from 10 pt below and out when it leaves (rows).
        case row
    }

    /// Identity across updates (a row's plan key, `hero`, `footer`).
    var key: String
    /// Board points, y down.
    var frame: CGRect
    var motion: Motion = .row
    /// A flat rounded fill behind the drawing, composited by the render server (a row's plate is never rasterized,
    /// so a changing value redraws only its text).
    var plate: Plate?
    /// Whatever the drawing depends on; equal revisions reuse the cached image.
    var revision: AnyHashable
    /// Draws the item in a context of `size` points (y down, origin at the item's top-left).
    var draw: (CGContext, CGSize) -> Void
    /// One-shot animations played when the layout carrying them is applied (DESIGN 2c Motion and effects).
    var accents: [BoardContentAccent] = []
    /// The white-hot variant the `roll(rising: true)` and `warm` accents start from.
    var hot: BoardContentDrawing?
    /// A continuous motion while the board has something going on; nil is still.
    var ambient: BoardContentAmbient?
    /// Clips the drawing to the frame (digits and ranks rolling in their slots).
    var clips = false
    /// Pixels per point of the image when not the window's (soft lights are kept at a fraction of it).
    var rasterScale: CGFloat?
    /// Grows or shrinks the image along x (a share underline, a heat rail) with a continuous transition instead of a
    /// redraw: the fraction of the frame it fills, from `stretchAnchor` points into the frame.
    var stretch: Double?
    /// The point of the frame a stretched item grows from, points from its left edge (a bar's start, after the glow's
    /// room).
    var stretchAnchor: CGFloat = 0
}

/// A drawing in a context of `size` points (y down).
struct BoardContentDrawing {
    var draw: (CGContext, CGSize) -> Void
}

/// The content's one-shot motions, all run by the render server.
enum BoardContentAccent {
    /// The new image rolls in from below (rising) or above in 0.24 s while the old one rolls out; rising, it comes in
    /// white-hot (`hot`) and cools to its color in 0.8 s.
    case roll(rising: Bool)
    /// White-hot (`hot`), then its own color, 0.8 s.
    case warm
    /// Dims briefly and settles, 0.8 s.
    case dim
    /// Scales 1 → 1.06 → 1 in 0.42 s about `anchor` (board points).
    case punch(anchor: CGPoint)
    /// A decal shown beside the item for a while (chips, sweeps, banners).
    case decal(BoardDecal)
}

/// A temporary drawing over an item: an overtake chip, a light sweep, a banner, a sheen.
struct BoardDecal {
    enum Motion {
        /// Pops in (scale 0.6 → 1), holds, fades out (`seconds` in all).
        case pop(seconds: Double)
        /// A band crossing `frame` from left to right in `seconds`, clipped to it (or to `mask`).
        case sweep(seconds: Double)
        /// Wipes in from the left, holds, wipes out to the right (timing's new-leader banner).
        case banner(seconds: Double)
        /// Shows at once and fades out.
        case fade(seconds: Double)
    }

    /// Item points, y down; may reach outside the item.
    var frame: CGRect
    var motion: Motion
    /// Celebrations (new peak, new leader) are left out with `Celebrate new peaks` off.
    var celebration = false
    /// Clip of a sweep (decal points, y down), e.g. the digits' outlines for the new-peak sheen.
    var mask: CGPath?
    /// The decal (a sweep's band: drawn in a box as tall as `frame` and `bandWidth` wide).
    var bandWidth: CGFloat = 0
    var draw: (CGContext, CGSize) -> Void
}

/// A continuous motion of the content (DESIGN 2c Style ambience).
enum BoardContentAmbient: Equatable {
    /// Opacity between `low` and 1 and back over `period` seconds (neon's tube).
    case breathe(period: Double, low: Float)
    /// A slow drift by (`dx`, `dy`) points and `scale`, there and back over `period` seconds (glass's aurora).
    case drift(dx: CGFloat, dy: CGFloat, scale: CGFloat, period: Double)
    /// A ring that swells and fades from the item every `period` seconds (timing's live dot).
    case pulse(period: Double)
}

/// The effort and speed capsule of an agent (DESIGN 4 Agents) drawn into a row, so board rows and the dashboard match.
@MainActor
protocol BoardCapsuleDrawing: AnyObject {
    /// The capsule's size for `modes`, nil when it shows nothing (`compact`: S boards, without its word).
    func capsuleSize(for modes: ExecutionModes, compact: Bool) -> CGSize?
    /// Draws the capsule with its top-left at `origin` (y down).
    func drawCapsule(for modes: ExecutionModes, compact: Bool, dark: Bool, in context: CGContext, at origin: CGPoint)
    /// Draws the capsule stretched to `width` (the rows' shared column: meters start on one line, bolts end on
    /// another); nil draws it at its own width.
    func drawCapsule(for modes: ExecutionModes, compact: Bool, dark: Bool, highContrast: Bool, width: CGFloat?,
                     in context: CGContext, at origin: CGPoint)
}

extension BoardCapsuleDrawing {
    func drawCapsule(for modes: ExecutionModes, compact: Bool, dark: Bool, in context: CGContext, at origin: CGPoint) {
        drawCapsule(for: modes, compact: compact, dark: dark, highContrast: false, width: nil, in: context, at: origin)
    }
}
