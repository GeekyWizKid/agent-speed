import AgentSpeedCore
import SwiftUI

/// Draws the dashboard's effort capsule (`EffortCapsuleView`, DESIGN 4 Agents) into board rows: rendered once per
/// capsule, width, appearance and scale, then reused as an image (a row redraws only when its data changes, so the top
/// rung's shimmer is left out of the cached rows).
@MainActor
final class BoardEffortCapsules: BoardCapsuleDrawing {
    static let shared = BoardEffortCapsules()

    private struct Key: Hashable {
        var capsule: EffortCapsule
        var compact: Bool
        var dark: Bool
        var highContrast: Bool
        var width: CGFloat
        var scale: CGFloat
    }

    private var images: [Key: CGImage] = [:]
    private static let limit = 96

    func capsuleSize(for modes: ExecutionModes, compact: Bool) -> CGSize? {
        let capsule = EffortCapsule(modes)
        guard capsule.isVisible else { return nil }
        return CGSize(width: capsule.width(compact: compact), height: EffortCapsule.height)
    }

    func drawCapsule(for modes: ExecutionModes, compact: Bool, dark: Bool, highContrast: Bool, width: CGFloat?,
                     in context: CGContext, at origin: CGPoint) {
        let capsule = EffortCapsule(modes)
        guard capsule.isVisible else { return }
        let size = CGSize(width: (width ?? capsule.width(compact: compact)).rounded(.up), height: EffortCapsule.height)
        let scale = max(1, abs(context.userSpaceToDeviceSpaceTransform.d))
        let key = Key(capsule: capsule, compact: compact, dark: dark, highContrast: highContrast, width: size.width, scale: scale)
        guard let image = images[key] ?? render(key) else { return }
        context.saveGState()
        context.translateBy(x: origin.x, y: origin.y + size.height)
        context.scaleBy(x: 1, y: -1)
        context.draw(image, in: CGRect(origin: .zero, size: size))
        context.restoreGState()
    }

    private func render(_ key: Key) -> CGImage? {
        let view = EffortCapsuleView(capsule: key.capsule, compact: key.compact, width: key.width, reduceMotion: true,
                                     shimmers: false)
            .environment(\.colorScheme, key.dark ? .dark : .light)
            .environment(\._colorSchemeContrast, key.highContrast ? .increased : .standard)
        let renderer = ImageRenderer(content: view)
        renderer.scale = key.scale
        renderer.isOpaque = false
        guard let image = renderer.cgImage else { return nil }
        if images.count >= Self.limit { images.removeAll() }
        images[key] = image
        return image
    }
}
