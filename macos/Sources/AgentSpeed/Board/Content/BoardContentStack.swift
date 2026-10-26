import AgentSpeedOrbModel
import AppKit
import QuartzCore

/// The board's content as cached Core Animation layers (DESIGN 2c Two layers, Rows): each item is rasterized once per
/// revision; moves, entrances and exits are layer animations the render server runs, so the app draws no frames for
/// them. The container is the board's rounded square (y up, as Core Animation), clipped to its corners.
@MainActor
final class BoardContentStack {
    /// The row spring per style (DESIGN 2c: glass critically damped, settle ≈ 0.45 s; timing damping 0.72; neon 0.9).
    struct RowSpring: Equatable {
        var duration: Double
        var bounce: Double

        static func of(_ style: BoardStyle) -> RowSpring {
            switch style {
            case .glass: RowSpring(duration: 0.45, bounce: 0)
            case .timing: RowSpring(duration: 0.45, bounce: 0.28)
            case .neon: RowSpring(duration: 0.45, bounce: 0.1)
            }
        }
    }

    static let fadeSeconds = 0.3
    static let enterDrop: CGFloat = 10

    let layer = CALayer()
    var spring = RowSpring.of(.glass)
    var reduceMotion = false {
        didSet { if reduceMotion != oldValue { refreshAmbience() } }
    }
    /// Style ambience (aurora drift, tube breath, live pulse); off with Effects intensity Off.
    var ambience = true {
        didSet { if ambience != oldValue { refreshAmbience() } }
    }
    /// `Celebrate new peaks`: off leaves out the new-peak and new-leader decals.
    var celebrations = true
    /// Pixels per point of the images; a change redraws every item.
    var scale: CGFloat = 2 {
        didSet { if scale != oldValue { invalidate() } }
    }
    /// The window's color space: images drawn in it reach the render server as they are, without a per-commit copy
    /// and color conversion of every changed row. A change redraws every item.
    var colorSpace: CGColorSpace? {
        didSet { if colorSpace != oldValue { invalidate() } }
    }
    /// Images rasterized since launch (benchmarks: data changes only, never per frame).
    private(set) var rasterCount = 0

    private struct Entry {
        let layer: CALayer
        var revision: AnyHashable
        var frame: CGRect
        var image: CGImage?
        var ambient: BoardContentAmbient?
    }

    private var entries: [String: Entry] = [:]
    private var order: [String] = []
    /// The motion of every item shown, for the way it leaves.
    private var leaving: [String: BoardContentItem.Motion] = [:]

    init() {
        layer.masksToBounds = true
        layer.anchorPoint = .zero
    }

    func setBoard(side: CGFloat, corner: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.bounds = CGRect(x: 0, y: 0, width: side, height: side)
        layer.cornerRadius = corner
        CATransaction.commit()
    }

    private var side: CGFloat { layer.bounds.height }

    private func refreshAmbience() {
        for entry in entries.values { setAmbient(entry.ambient, on: entry.layer) }
    }

    private func invalidate() {
        for key in entries.keys { entries[key]?.revision = AnyHashable(UUID()) }
    }

    /// Shows `items`: new ones fade in from 10 pt below, missing ones fade out, moved ones spring to their place
    /// (Reduce Motion: a crossfade instead). `animated` false places everything at once.
    func apply(_ items: [BoardContentItem], animated: Bool) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        var seen = Set<String>()
        var moved = false
        // New layers go right above the previous item's, so the stack keeps the items' order (back to front).
        var below: CALayer?
        for item in items {
            defer { below = entries[item.key]?.layer }
            seen.insert(item.key)
            let target = layerFrame(item.frame)
            if var entry = entries[item.key] {
                Self.setPlate(item.plate, on: entry.layer)
                if entry.layer.masksToBounds != item.clips { entry.layer.masksToBounds = item.clips }
                if entry.revision != item.revision || entry.frame.size != item.frame.size {
                    let old = entry.image
                    entry.image = rasterize(item)
                    entry.layer.contents = entry.image
                    entry.revision = item.revision
                    if animated { change(entry.layer, from: old, to: entry.image, item: item) }
                }
                if entry.frame != item.frame {
                    let from = entry.layer.presentation()?.position ?? entry.layer.position
                    entry.layer.bounds = CGRect(origin: .zero, size: target.size)
                    entry.layer.position = target.origin
                    if animated, item.motion == .row {
                        if reduceMotion {
                            moved = true
                        } else {
                            let animation = CASpringAnimation(perceptualDuration: spring.duration, bounce: spring.bounce)
                            animation.keyPath = "position"
                            animation.fromValue = NSValue(point: from)
                            animation.toValue = NSValue(point: target.origin)
                            animation.duration = animation.settlingDuration
                            entry.layer.add(animation, forKey: "move")
                        }
                    }
                    entry.frame = item.frame
                }
                if animated { play(item, on: entry.layer) }
                if entry.ambient != item.ambient {
                    entry.ambient = item.ambient
                    setAmbient(item.ambient, on: entry.layer)
                }
                entries[item.key] = entry
            } else {
                let child = CALayer()
                child.anchorPoint = .zero
                child.bounds = CGRect(origin: .zero, size: target.size)
                child.position = target.origin
                child.contentsScale = scale
                child.contentsGravity = .resize
                child.masksToBounds = item.clips
                Self.setPlate(item.plate, on: child)
                let image = rasterize(item)
                child.contents = image
                if let below { layer.insertSublayer(child, above: below) } else { layer.insertSublayer(child, at: 0) }
                entries[item.key] = Entry(layer: child, revision: item.revision, frame: item.frame, image: image,
                                          ambient: item.ambient)
                setAmbient(item.ambient, on: child)
                if animated, item.motion == .row { enter(child, at: target.origin) }
                if animated { play(item, on: child) }
            }
        }
        var motions: [String: BoardContentItem.Motion] = [:]
        for item in items { motions[item.key] = item.motion }
        for key in order where !seen.contains(key) {
            guard let entry = entries.removeValue(forKey: key) else { continue }
            // Only rows fade out; a digit or a decal-free piece of the hero is replaced at once.
            leave(entry.layer, animated: animated && (leaving[key] ?? .row) == .row)
        }
        leaving = motions
        order = items.map(\.key)
        if moved {
            let fade = CATransition()
            fade.type = .fade
            fade.duration = 0.25
            layer.add(fade, forKey: "crossfade")
        }
        CATransaction.commit()
    }

    /// The cached images at their resting frames (board points, y down), back to front, for offscreen frames.
    var snapshot: [(image: CGImage, frame: CGRect)] {
        order.compactMap { key in entries[key].flatMap { entry in entry.image.map { ($0, entry.frame) } } }
    }

    private static func setPlate(_ plate: BoardContentItem.Plate?, on layer: CALayer) {
        if layer.backgroundColor != plate?.color { layer.backgroundColor = plate?.color }
        let radius = plate?.cornerRadius ?? 0
        if layer.cornerRadius != radius { layer.cornerRadius = radius }
    }

    private func layerFrame(_ frame: CGRect) -> CGRect {
        CGRect(x: frame.minX, y: side - frame.maxY, width: frame.width, height: frame.height)
    }

    private func enter(_ child: CALayer, at position: CGPoint) {
        let group = CAAnimationGroup()
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = 0
        fade.toValue = 1
        group.animations = [fade]
        if !reduceMotion {
            let rise = CABasicAnimation(keyPath: "position")
            rise.fromValue = NSValue(point: CGPoint(x: position.x, y: position.y - Self.enterDrop))
            rise.toValue = NSValue(point: position)
            group.animations?.append(rise)
        }
        group.duration = Self.fadeSeconds
        group.timingFunction = CAMediaTimingFunction(name: .easeOut)
        child.add(group, forKey: "enter")
    }

    private func leave(_ child: CALayer, animated: Bool) {
        guard animated else {
            child.removeFromSuperlayer()
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + Self.fadeSeconds) { [weak child] in
            MainActor.assumeIsolated { child?.removeFromSuperlayer() }
        }
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = child.presentation()?.opacity ?? 1
        fade.toValue = 0
        fade.duration = Self.fadeSeconds
        child.opacity = 0
        child.add(fade, forKey: "leave")
    }

    private func rasterize(_ item: BoardContentItem) -> CGImage? {
        rasterCount += 1
        return BoardRaster.image(size: item.frame.size, scale: item.rasterScale ?? scale, space: colorSpace, draw: item.draw)
    }
}

/// Offscreen bitmaps for the board's cached content.
enum BoardRaster {
    /// A premultiplied image of `size` points at `scale` in `space` (sRGB by default), drawn by `draw` in a y-down
    /// context.
    static func image(size: CGSize, scale: CGFloat, space: CGColorSpace? = nil, draw: (CGContext, CGSize) -> Void) -> CGImage? {
        let width = max(1, Int((size.width * scale).rounded(.up)))
        let height = max(1, Int((size.height * scale).rounded(.up)))
        guard let space = space ?? CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                                          | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }
        context.scaleBy(x: scale, y: scale)
        context.translateBy(x: 0, y: size.height)
        context.scaleBy(x: 1, y: -1)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        draw(context, size)
        NSGraphicsContext.restoreGraphicsState()
        return context.makeImage()
    }
}


