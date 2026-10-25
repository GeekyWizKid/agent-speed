import AgentSpeedOrbModel
import AppKit

/// Runs a board's `BoardEffects` on the shared frame clock (DESIGN 2c Frame policy): the overlay canvas advances the
/// simulation and draws it, the backdrop canvas draws the warp under the rows. Each canvas asks for the effects'
/// frame class while the board can be seen and for nothing otherwise, so an idle, hidden or occluded board renders
/// nothing.
@MainActor
final class BoardEffectsDriver {
    let overlay = OrbCanvasView(frame: .zero)
    let backdrop = OrbCanvasView(frame: .zero)
    private(set) var effects: BoardEffects
    /// Visible, not occluded and not faded out.
    var canRender = true {
        didSet {
            guard canRender != oldValue else { return }
            if canRender { lastTime = nil }
            updateRequests()
            if canRender { refresh() }
        }
    }
    var highContrast = false {
        didSet { if highContrast != oldValue { refresh() } }
    }
    /// Frames rendered by the overlay (benchmarks).
    var framesDrawn: Int { overlay.framesDrawn }

    private var lastTime: TimeInterval?

    init(style: BoardStyle, side: Double) {
        effects = BoardEffects(style: style, side: side) { HeatPalette.color($0).simd }
        overlay.sceneProvider = { [unowned self] scene, _, _, time in
            advance(to: time)
            BoardEffectsRenderer.overlay(effects, into: &scene, highContrast: highContrast)
        }
        overlay.onFrame = { [unowned self] in updateRequests() }
        backdrop.sceneProvider = { [unowned self] scene, _, _, _ in
            BoardEffectsRenderer.backdrop(effects, into: &scene)
        }
        backdrop.layer?.masksToBounds = true
        updateRequests()
    }

    /// Changes the effects (heat, events, settings) and wakes the canvases when they need frames or one redraw.
    func update(_ change: (inout BoardEffects) -> Void) {
        let warpWasOn = effects.starCount > 0
        change(&effects)
        updateRequests()
        refresh(warp: warpWasOn)
    }

    func setBoard(side: Double, corner: CGFloat) {
        effects.setSide(side)
        backdrop.layer?.cornerRadius = corner
        refresh()
    }

    private func advance(to time: TimeInterval) {
        let dt = lastTime.map { time - $0 } ?? 0
        lastTime = time
        effects.advance(dt)
    }

    private func updateRequests() {
        let frameClass = canRender ? effects.frameClass : .none
        overlay.request = OrbFrameRequest(frameClass: frameClass)
        let warping = backdrop.request.frameClass != .none
        backdrop.request = OrbFrameRequest(frameClass: canRender && effects.warpOn ? frameClass : .none)
        // The last warp frame stays on screen: clear it once.
        if warping, backdrop.request.frameClass == .none { backdrop.setNeedsFrame() }
        if frameClass == .none { lastTime = nil }
    }

    /// One frame of the current state when nothing animates (a static board keeps its last frame).
    private func refresh(warp warpWasOn: Bool = true) {
        guard canRender else { return }
        if overlay.request.frameClass == .none { overlay.setNeedsFrame() }
        if backdrop.request.frameClass == .none, warpWasOn || effects.starCount > 0 { backdrop.setNeedsFrame() }
    }
}
