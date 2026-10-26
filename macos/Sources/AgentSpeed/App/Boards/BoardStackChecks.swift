import AgentSpeedOrbModel
import AppKit

/// `--board-frames DIR` with `AGENT_SPEED_BOARD_CHECKS=1`: offscreen checks of the content stack the app target has no
/// unit tests for — a layout applied again replays none of its one-shot accents, Reduce Motion crossfades moved rows
/// (the window passes `animated` through), and the ambience and celebration switches. Writes `board-checks.txt`.
@MainActor
enum BoardStackChecks {
    static func run(into directory: URL) {
        var lines: [String] = []
        func check(_ name: String, _ passed: Bool) { lines.append((passed ? "ok    " : "FAIL  ") + name) }
        let l10n = BoardSimFeed.harnessLocalizer()

        // One-shot accents come with the first apply of a layout only.
        let feed = BoardSimFeed(simulation: BoardSimulation(scenario: "team"), style: .glass, size: .medium, dark: true, l10n: l10n)
        var steps = 0
        while steps < 200, !feed.layout.items.contains(where: { !$0.accents.isEmpty }) {
            feed.step()
            steps += 1
        }
        let stack = BoardContentStack()
        stack.setBoard(side: 340, corner: 22)
        let first = feed.takeItems()
        check("a fresh layout carries accents", first.contains { !$0.accents.isEmpty })
        stack.apply(first, animated: true)
        let accentKeys: Set = ["roll", "cool", "dim", "punch", "change"]
        check("the first apply plays them", count(stack.layer, keys: accentKeys) > 0 || decals(stack.layer) > 0)
        clear(stack.layer)
        let decalsBefore = decals(stack.layer)
        let again = feed.takeItems()
        check("the same layout taken again has none", again.allSatisfy { $0.accents.isEmpty })
        stack.apply(again, animated: true)
        check("applying it again plays none", count(stack.layer, keys: accentKeys) == 0 && decals(stack.layer) == decalsBefore)

        // Reduce Motion: a moved row crossfades through the window controller.
        let window = BoardWindowController(config: BoardConfig(id: "checks", style: .glass, size: .medium), center: .zero, dark: true)
        window.reduceMotion = true
        var items = feed.takeItems()
        window.contentProvider = { items }
        window.contentChanged(animated: false)
        window.host.content.layer.removeAllAnimations()
        if let index = items.firstIndex(where: { $0.motion == .row }) { items[index].frame.origin.y += 32 }
        window.contentChanged()
        // Core Animation files a CATransition under "transition" whatever key it is added with.
        let fade = (window.host.content.layer.animationKeys() ?? []).compactMap { window.host.content.layer.animation(forKey: $0) as? CATransition }
        check("Reduce Motion crossfades a moved row", fade.contains { $0.type == .fade })
        check("Reduce Motion springs nothing", count(window.host.content.layer, keys: ["move"]) == 0)

        // Effects Off stops the ambience; Celebrate new peaks off drops the celebration decals.
        let neon = BoardSimFeed(simulation: BoardSimulation(scenario: "team"), style: .neon, size: .medium, dark: true, l10n: l10n)
        for _ in 0..<12 { neon.step() }
        let ambient = BoardContentStack()
        ambient.setBoard(side: 340, corner: 22)
        ambient.apply(neon.takeItems(), animated: false)
        let breathing = count(ambient.layer, keys: ["ambient"])
        ambient.ambience = false
        check("a generating neon board breathes", breathing > 0)
        check("Effects Off stops the ambience", count(ambient.layer, keys: ["ambient"]) == 0)
        let peak = BoardContentStack()
        peak.celebrations = false
        let decal = BoardDecal(frame: CGRect(x: 0, y: 0, width: 20, height: 10), motion: .pop(seconds: 1), celebration: true) { _, _ in }
        var item = BoardContentItem(key: "x", frame: CGRect(x: 0, y: 0, width: 40, height: 20), motion: .none, revision: AnyHashable(0)) { _, _ in }
        item.accents = [.decal(decal)]
        peak.apply([item], animated: true)
        check("Celebrate new peaks off drops celebration decals", decals(peak.layer) == 0)

        window.close()
        let text = lines.joined(separator: "\n") + "\n"
        try? text.write(to: directory.appendingPathComponent("board-checks.txt"), atomically: true, encoding: .utf8)
        print(text, terminator: "")
    }

    private static func count(_ root: CALayer, keys: Set<String>) -> Int {
        var total = (root.animationKeys() ?? []).filter(keys.contains).count
        for child in root.sublayers ?? [] { total += count(child, keys: keys) }
        return total
    }

    /// Decal layers (unnamed sublayers of an item's layer).
    private static func decals(_ root: CALayer) -> Int {
        (root.sublayers ?? []).reduce(0) { $0 + ($1.sublayers?.filter { $0.name == nil }.count ?? 0) }
    }

    private static func clear(_ root: CALayer) {
        root.removeAllAnimations()
        root.sublayers?.forEach(clear)
    }
}
