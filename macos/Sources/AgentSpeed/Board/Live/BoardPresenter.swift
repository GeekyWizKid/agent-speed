import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// One board on the desktop: its window, fed by its runtime (DESIGN 2c). Lays its content out once per tick and only
/// while it can be seen, plays the tick on the effects, and handles the pointer — a click toggles a group's
/// expansion, hovering a group row lifts its tint and shows its tooltip after 0.35 s — and accessibility.
@MainActor
final class BoardPresenter {
    private struct Look: Equatable {
        var style: BoardStyle
        var size: BoardSize
        var dark: Bool
        var language: AppLanguage
    }

    let window: BoardWindowController
    private let model: AppModel
    private let runtime: BoardRuntime
    private let accessibility: BoardAccessibilityReporter
    private let tooltip = OrbChipPanel(interactive: false)
    private var look: Look
    private var builder: BoardContentBuilding
    private var layout: BoardContentLayout?
    /// The tick `layout` was made from: an equal tick (an idle board) lays out and applies nothing.
    private var layoutTick: BoardTick?
    private var layoutStale = true
    private var hovered: String?
    private var tooltipTimer: Timer?
    private var tooltipGroup: String?

    var id: String { runtime.id }
    var config: BoardConfig { window.config }

    init(model: AppModel, runtime: BoardRuntime, center: CGPoint, dark: Bool) {
        self.model = model
        self.runtime = runtime
        look = Look(style: runtime.config.style, size: runtime.config.size, dark: dark, language: model.l10n.language)
        builder = BoardContentBuilders.make(style: look.style, size: look.size, dark: dark, l10n: model.l10n, capsule: nil)
        window = BoardWindowController(config: runtime.config, center: center, dark: dark)
        accessibility = BoardAccessibilityReporter(host: window.host)
        window.contentProvider = { [unowned self] in items() }
        window.onClick = { [weak self] point in self?.click(at: point) }
        window.host.onMouseMoved = { [weak self] event in self?.pointerMoved(event) }
        window.host.onMouseExited = { [weak self] _ in self?.pointerLeft() }
        let down = window.host.onMouseDown
        window.host.onMouseDown = { [weak self] event in
            self?.hideTooltip()
            down?(event)
        }
        runtime.onTick = { [weak self] _ in self?.tickArrived() }
        if runtime.tick != nil { tickArrived() }
    }

    func close() {
        runtime.onTick = nil
        tooltipTimer?.invalidate()
        tooltip.close()
        window.close()
    }

    // MARK: Settings

    /// A changed configuration: the window applies it, a new style or size takes a new content builder.
    func apply(_ config: BoardConfig) {
        guard config != window.config else { return }
        window.apply(config)
        setLook(style: config.style, size: config.size, dark: look.dark, language: look.language)
    }

    func setLook(style: BoardStyle? = nil, size: BoardSize? = nil, dark: Bool, language: AppLanguage) {
        let next = Look(style: style ?? look.style, size: size ?? look.size, dark: dark, language: language)
        guard next != look else { return }
        look = next
        window.dark = dark
        builder = BoardContentBuilders.make(style: next.style, size: next.size, dark: dark, l10n: model.l10n, capsule: nil)
        layoutStale = true
        layoutTick = nil
        window.contentChanged(animated: false)
        publishAccessibility(force: true)
    }

    func setEffects(_ intensity: BoardEffectsIntensity, celebrations: Bool, reduceMotion: Bool, highContrast: Bool) {
        if window.reduceMotion != reduceMotion { window.reduceMotion = reduceMotion }
        window.driver.highContrast = highContrast
        window.host.content.ambience = intensity != .off
        window.host.content.celebrations = celebrations
        window.driver.update { fx in
            if fx.intensity != intensity { fx.setIntensity(intensity) }
            if fx.celebrations != celebrations { fx.celebrations = celebrations }
        }
    }

    // MARK: Ticks

    private func items() -> [BoardContentItem] {
        guard let tick = runtime.tick else { return [] }
        if layoutStale || layout == nil {
            layout = builder.layout(tick)
            layoutTick = tick
            layoutStale = false
        }
        guard var items = layout?.takeItems() else { return [] }
        if let hovered, let index = items.firstIndex(where: { $0.key == hovered }) {
            items[index].plate = Self.lifted(items[index].plate, dark: look.dark)
        }
        return items
    }

    private func tickArrived() {
        guard let tick = runtime.tick else { return }
        if layoutStale || tick != layoutTick {
            layoutStale = true
            window.contentChanged()
        }
        let laidOut = window.isRendering && !layoutStale ? layout : nil
        let punch = runtime.frame.punch
        window.driver.update { fx in BoardEffectsPlayer.play(tick, layout: laidOut, punch: punch, on: &fx) }
        publishAccessibility()
        if let tooltipGroup, tooltip.isShown { showTooltip(for: tooltipGroup) }
    }

    private func publishAccessibility(force: Bool = false) {
        accessibility.update(model: model, runtime: runtime, layout: layoutStale ? nil : layout,
                             frameInScreen: window.panel.frame, now: Clock.now, force: force)
    }

    // MARK: Pointer

    private func group(at point: CGPoint) -> String? {
        guard let layout else { return nil }
        return layout.groupRows.first { $0.value.row.contains(point) }?.key
    }

    private func boardPoint(_ event: NSEvent) -> CGPoint {
        let local = window.host.convert(event.locationInWindow, from: nil)
        return CGPoint(x: local.x, y: window.side - local.y)
    }

    private func click(at point: CGPoint) {
        guard let id = group(at: point) else { return }
        runtime.toggleExpansion(of: id, model: model, now: Clock.now)
    }

    private func pointerMoved(_ event: NSEvent) {
        runtime.touch(now: Clock.now)
        let id = group(at: boardPoint(event))
        guard id != hovered else { return }
        hovered = id
        window.contentChanged(animated: false)
        hideTooltip()
        guard let id else { return }
        let timer = Timer(timeInterval: BoardTooltip.delay, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.showTooltip(for: id) }
        }
        RunLoop.main.add(timer, forMode: .common)
        tooltipTimer = timer
    }

    private func pointerLeft() {
        guard hovered != nil else { return }
        hovered = nil
        window.contentChanged(animated: false)
        hideTooltip()
    }

    private func showTooltip(for id: String) {
        guard let tick = runtime.tick, let group = tick.groups.first(where: { $0.id == id }),
              let row = layout?.groupRows[id]?.row, window.panel.isVisible else {
            hideTooltip()
            return
        }
        tooltipGroup = id
        let lines = BoardTooltip.lines(group, metric: tick.metric, l10n: model.l10n)
        let board = window.panel.frame
        let screen = (window.panel.screen ?? NSScreen.main)?.visibleFrame ?? board
        tooltip.show(BoardTooltipView(lines: lines), level: NSWindow.Level(rawValue: window.panel.level.rawValue + 1),
                     appearance: NSAppearance(named: look.dark ? .darkAqua : .aqua)) { size in
            let rowTop = board.maxY - row.minY
            var x = board.maxX + 4
            if x + size.width > screen.maxX { x = board.minX - 4 - size.width }
            let y = min(max(rowTop - size.height + 8, screen.minY), screen.maxY - size.height)
            return CGRect(x: max(screen.minX, x), y: y, width: size.width, height: size.height)
        }
    }

    private func hideTooltip() {
        tooltipTimer?.invalidate()
        tooltipTimer = nil
        tooltipGroup = nil
        tooltip.hide()
    }

    /// Hover lifts the row's tint only (DESIGN 2c Interaction).
    static func lifted(_ plate: BoardContentItem.Plate?, dark: Bool) -> BoardContentItem.Plate {
        let lift = NSColor(white: dark ? 1 : 0, alpha: dark ? 0.07 : 0.045)
        guard let plate, let base = NSColor(cgColor: plate.color)?.usingColorSpace(.sRGB) else {
            return BoardContentItem.Plate(color: lift.cgColor, cornerRadius: 8)
        }
        let blended = base.blended(withFraction: 0.1, of: dark ? .white : .black) ?? base
        let alpha = min(1, base.alphaComponent + lift.alphaComponent)
        return BoardContentItem.Plate(color: blended.withAlphaComponent(alpha).cgColor, cornerRadius: plate.cornerRadius)
    }
}
