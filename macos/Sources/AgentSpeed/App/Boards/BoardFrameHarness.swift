import AgentSpeedOrbModel
import AppKit

/// `--board-frames DIR`: offscreen stills of the boards (no windows, no screen capture) for comparison with the
/// prototypes: for each style and size, `board-<style>-<size>-<state>[-light].png` over the content of the `team`
/// scenario after 40 ticks (the prototypes' `?steps=40&scenario=team`): `still` at the simulation's own heat with
/// Reduce Motion (the prototypes' settled frame), idle, cruise, redline, 0.15 s into an overtake and 0.3 s into a
/// new-leader celebration. The glass backdrop is its tint without the system blur. Quits when done.
///
/// `AGENT_SPEED_BOARD_STYLES`, `AGENT_SPEED_BOARD_SIZE` (S, M, L; comma separated, default M), `AGENT_SPEED_BOARD_STATES`
/// (comma separated), `AGENT_SPEED_BOARD_LEVEL` (off, soft, standard, max), `AGENT_SPEED_BOARD_THEMES` (dark, light;
/// default dark, or light with `AGENT_SPEED_BOARD_LIGHT=1`), `AGENT_SPEED_BOARD_CONTRAST=1` (Increase Contrast),
/// `AGENT_SPEED_BOARD_LANG`.
@MainActor
enum BoardFrameHarness {
    struct State {
        let name: String
        let heat: Double
        let overdrive: Double
        let active: Bool
        var event: Event?
        var after = 2.0
        /// The simulation's own heat with Reduce Motion, as the prototypes' settled frames.
        var settled = false

        enum Event {
            case overtake
            case newLeader
        }
    }

    static let states = [
        State(name: "still", heat: 0, overdrive: 0, active: true, settled: true),
        State(name: "idle", heat: 0, overdrive: 0, active: false),
        State(name: "cruise", heat: 0.26, overdrive: 0, active: true),
        State(name: "redline", heat: 0.93, overdrive: 0.25, active: true),
        State(name: "overtake", heat: 0.62, overdrive: 0, active: true, event: .overtake, after: 0.15),
        State(name: "newleader", heat: 0.7, overdrive: 0, active: true, event: .newLeader, after: 0.3),
    ]

    static func run(into directory: URL) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let environment = ProcessInfo.processInfo.environment
        func list<T>(_ key: String, _ make: (String) -> T?) -> [T]? {
            environment[key].map { $0.split(separator: ",").compactMap { make(String($0)) } }
        }
        let styles = list("AGENT_SPEED_BOARD_STYLES", BoardStyle.init(rawValue:)) ?? BoardStyle.allCases
        let sizes = list("AGENT_SPEED_BOARD_SIZE", BoardSize.init(rawValue:)) ?? [.medium]
        let names = list("AGENT_SPEED_BOARD_STATES") { $0 }
        let level = environment["AGENT_SPEED_BOARD_LEVEL"].flatMap(BoardEffectsIntensity.init(rawValue:)) ?? .standard
        let themes = list("AGENT_SPEED_BOARD_THEMES") { $0 == "light" ? false : $0 == "dark" ? true : nil }
            ?? [environment["AGENT_SPEED_BOARD_LIGHT"] != "1"]
        let contrast = environment["AGENT_SPEED_BOARD_CONTRAST"] == "1"
        let l10n = BoardSimFeed.harnessLocalizer()
        for style in styles {
            for size in sizes {
                for dark in themes {
                    for state in states where names?.contains(state.name) ?? true {
                        guard let image = render(style: style, size: size, level: level, dark: dark, contrast: contrast,
                                                 l10n: l10n, state: state) else { continue }
                        let name = "board-\(style.rawValue)-\(size.rawValue)-\(state.name)\(dark ? "" : "-light")\(contrast ? "-contrast" : "")"
                        let url = directory.appendingPathComponent(name + ".png")
                        PreviewSnapshot.write(image, to: url)
                        print(url.path)
                    }
                }
            }
        }
    }

    static func render(style: BoardStyle, size: BoardSize, level: BoardEffectsIntensity, dark: Bool, contrast: Bool, l10n: Localizer,
                       state: State) -> CGImage? {
        let side = size.side
        var simulation = BoardSimulation(scenario: "team")
        simulation.paused = !state.active
        let feed = BoardSimFeed(simulation: simulation, style: style, size: size, dark: dark, l10n: l10n, highContrast: contrast)
        for _ in 0..<40 { feed.step() }
        let layout = feed.layout
        var fx = BoardEffects(style: style, side: side, seed: 11) { HeatPalette.color($0).simd }
        fx.setIntensity(level)
        if state.settled {
            fx.setReduceMotion(true)
            fx.setHeat(feed.tick.heat, overdrive: feed.tick.overdrive, active: feed.tick.generating > 0)
        } else {
            fx.setHeat(state.heat, overdrive: state.overdrive, active: state.active)
        }
        fx.settleNow()
        step(&fx, seconds: max(0, 2.0 - state.after))
        let rows = feed.tick.groups.compactMap { layout.groupRows[$0.id] }
        if let event = state.event, rows.count >= 3 {
            let color = RGBColor(hex: 0x34D399).simd
            switch event {
            case .overtake: fx.overtake(row: rows[1].row, rank: rows[1].rank, color: color, passedRankY: rows[2].rank.y)
            case .newLeader: fx.newLeader(row: rows[0].row, rank: rows[0].rank, color: color, passedRankY: rows[1].rank.y)
            }
        }
        step(&fx, seconds: state.after)
        return compose(fx: fx, items: layout.items, style: style, side: side, dark: dark, contrast: contrast)
    }

    static func step(_ fx: inout BoardEffects, seconds: Double) {
        var t = 0.0
        while t < seconds - 1e-9 {
            fx.advance(1.0 / 60)
            t += 1.0 / 60
        }
    }

    private static func compose(fx: BoardEffects, items: [BoardContentItem], style: BoardStyle, side: Double,
                                dark: Bool, contrast: Bool) -> CGImage? {
        let margin = BoardEffects.margin
        let canvasSide = side + 2 * margin
        let background = dark ? NSColor(srgbRed: 0.10, green: 0.11, blue: 0.14, alpha: 1)
            : NSColor(srgbRed: 0.88, green: 0.89, blue: 0.92, alpha: 1)
        guard let canvas = PreviewCanvas(size: CGSize(width: canvasSide, height: canvasSide), background: background) else { return nil }
        let board = CGRect(x: margin, y: margin, width: side, height: side)
        let corner = CGFloat(style.cornerRadius)
        let path = CGPath(roundedRect: board, cornerWidth: corner, cornerHeight: corner, transform: nil)
        canvas.context.addPath(path)
        canvas.context.setFillColor(BoardHostView.slabColor(style, dark: dark).nsColor.cgColor)
        canvas.context.fillPath()

        var scene = OrbScene(capacity: 2048)
        if fx.starCount > 0 {
            BoardEffectsRenderer.backdrop(fx, into: &scene)
            if let warp = OrbSnapshot.image(scene, side: side, scale: 2) {
                canvas.context.saveGState()
                canvas.context.addPath(path)
                canvas.context.clip()
                canvas.draw(warp, at: board.origin, size: board.size)
                canvas.context.restoreGState()
            }
        }
        canvas.context.saveGState()
        canvas.context.addPath(path)
        canvas.context.clip()
        for item in items {
            if let plate = item.plate {
                let rect = item.frame.offsetBy(dx: margin, dy: margin)
                canvas.context.addPath(CGPath(roundedRect: rect, cornerWidth: plate.cornerRadius, cornerHeight: plate.cornerRadius,
                                              transform: nil))
                canvas.context.setFillColor(plate.color)
                canvas.context.fillPath()
            }
            guard let image = BoardRaster.image(size: item.frame.size, scale: 2, draw: item.draw) else { continue }
            canvas.draw(image, at: CGPoint(x: margin + item.frame.minX, y: margin + item.frame.minY), size: item.frame.size)
        }
        canvas.context.restoreGState()

        BoardEffectsRenderer.overlay(fx, into: &scene, highContrast: contrast)
        if let overlay = OrbSnapshot.image(scene, side: canvasSide, scale: 2) {
            canvas.draw(overlay, at: .zero, size: CGSize(width: canvasSide, height: canvasSide))
        }
        return canvas.image
    }
}
