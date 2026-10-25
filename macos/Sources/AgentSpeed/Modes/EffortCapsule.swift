import AgentSpeedCore
import AppKit

/// What one effort capsule draws (DESIGN 4, Agents): the effort's meter and word, and the speed's bolts.
struct EffortCapsule: Equatable, Hashable, Sendable {
    /// Native effort token, nil when unknown.
    var effort: String?
    /// Speed token, nil when unknown.
    var speed: String?

    init(effort: String?, speed: String?) {
        self.effort = effort
        self.speed = speed
    }

    init(_ values: EffortAndSpeed) {
        self.init(effort: values.effort?.mode, speed: values.speed?.mode)
    }

    init(_ modes: ExecutionModes) {
        self.init(modes.effortAndSpeed)
    }

    /// Lit bars 0–5; nil when there is no meter (unknown effort, or a token without a level).
    var level: Int? { effort.flatMap(ModeDisplay.effortLevel) }
    var isMax: Bool { effort == "max" }
    var isTopRung: Bool { effort.map(ModeDisplay.isTopRung) ?? false }
    var bolts: Int { speed.map(ModeDisplay.bolts) ?? 0 }

    /// A capsule shows for a known effort or a speed with bolts; never for unknown values or a standard speed.
    var isVisible: Bool { effort != nil || bolts > 0 }

    /// Whether the word is drawn: it is dropped whole in compact mode, unless it is all the capsule would show.
    func showsWord(compact: Bool) -> Bool {
        guard effort != nil else { return false }
        return !compact || (level == nil && bolts == 0)
    }

    // MARK: Geometry

    static let height: CGFloat = 18
    static let padding: CGFloat = 6
    static let partSpacing: CGFloat = 5
    static let barWidth: CGFloat = 2.5
    static let barSpacing: CGFloat = 1.5
    static let barHeights: [CGFloat] = [4, 5.5, 7, 8.5, 10]
    static let meterWidth = barWidth * 5 + barSpacing * 4
    static let wordFontSize: CGFloat = 11
    static let boltPointSize: CGFloat = 9
    static let boltOverlap: CGFloat = 3

    static let boltSize: CGSize = {
        let configuration = NSImage.SymbolConfiguration(pointSize: boltPointSize, weight: .regular)
        let image = NSImage(systemSymbolName: "bolt.fill", accessibilityDescription: nil)?
            .withSymbolConfiguration(configuration)
        return image.map { CGSize(width: ceil($0.size.width), height: ceil($0.size.height)) } ?? CGSize(width: 7, height: 10)
    }()

    static func wordWidth(_ word: String) -> CGFloat {
        let font = NSFont.systemFont(ofSize: wordFontSize, weight: .medium)
        return ceil((word as NSString).size(withAttributes: [.font: font]).width)
    }

    static func boltsWidth(_ count: Int) -> CGFloat {
        count <= 0 ? 0 : boltSize.width * CGFloat(count) - boltOverlap * CGFloat(count - 1)
    }

    /// Intrinsic width; 0 when nothing is drawn.
    func width(compact: Bool) -> CGFloat {
        var parts: [CGFloat] = []
        if level != nil { parts.append(Self.meterWidth) }
        if showsWord(compact: compact), let effort { parts.append(Self.wordWidth(effort)) }
        if bolts > 0 { parts.append(Self.boltsWidth(bolts)) }
        guard !parts.isEmpty else { return 0 }
        return Self.padding * 2 + parts.reduce(0, +) + Self.partSpacing * CGFloat(parts.count - 1)
    }

    /// Width of the shared column: the widest capsule among the rows, 0 when none shows.
    static func columnWidth(_ capsules: [EffortCapsule], compact: Bool) -> CGFloat {
        capsules.map { $0.width(compact: compact) }.max() ?? 0
    }

    // MARK: Text

    /// The tooltip lines: effort and speed, each spelled as its token, and the top rung's explanation.
    @MainActor
    func tooltipLines(_ l10n: Localizer) -> [String] {
        var lines = [
            l10n("effort.tooltip", effort ?? l10n("modes.unrecorded")),
            l10n("speed.tooltip", speed.map { Self.speedValue($0, l10n) } ?? l10n("modes.unrecorded")),
        ]
        if effort == "ultra" { lines.append(l10n("effort.ultra.detail")) }
        if effort == "ultracode" { lines.append(l10n("effort.ultracode.detail")) }
        return lines
    }

    /// The end of a row's accessibility label: the effort and speed lines.
    @MainActor
    func accessibilityText(_ l10n: Localizer) -> String {
        tooltipLines(l10n).prefix(2).joined(separator: l10n.language.usesLatinCase ? ", " : "，")
    }

    @MainActor
    static func speedValue(_ speed: String, _ l10n: Localizer) -> String {
        ["standard", "fast", "ultrafast", "highspeed"].contains(speed) ? l10n("speed.value." + speed) : speed
    }
}
