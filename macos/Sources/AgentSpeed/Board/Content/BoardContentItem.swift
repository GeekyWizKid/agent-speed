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
