import AgentSpeedOrbModel
import AppKit

/// The board panel's content, back to front: the style's backdrop (glass: `NSVisualEffectView`; timing and neon a
/// solid slab), the warp canvas, the cached content layers. Clipped to the rounded square; forwards the pointer.
@MainActor
final class BoardHostView: NSView {
    var onMouseDown: ((NSEvent) -> Void)?
    var onMouseDragged: ((NSEvent) -> Void)?
    var onMouseUp: ((NSEvent) -> Void)?
    var onRightMouseDown: ((NSEvent) -> Void)?

    let content = BoardContentStack()
    private let glass = NSVisualEffectView()
    private let slab = NSView()
    private let contentView = NSView()
    private let warp: OrbCanvasView
    private var style = BoardStyle.glass
    private var dark = true

    init(warp: OrbCanvasView) {
        self.warp = warp
        super.init(frame: NSRect(x: 0, y: 0, width: 340, height: 340))
        wantsLayer = true
        layer?.masksToBounds = true
        glass.blendingMode = .behindWindow
        glass.state = .active
        glass.material = .hudWindow
        glass.wantsLayer = true
        glass.layer?.masksToBounds = true
        addSubview(glass)
        slab.wantsLayer = true
        slab.layer?.masksToBounds = true
        addSubview(slab)
        addSubview(warp)
        contentView.layer = CALayer()
        contentView.wantsLayer = true
        contentView.layer?.addSublayer(content.layer)
        addSubview(contentView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override var isOpaque: Bool { false }
    override var mouseDownCanMoveWindow: Bool { false }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    func apply(style: BoardStyle, side: CGFloat, dark: Bool) {
        self.style = style
        self.dark = dark
        let corner = CGFloat(style.cornerRadius)
        let bounds = CGRect(x: 0, y: 0, width: side, height: side)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer?.cornerRadius = corner
        glass.frame = bounds
        glass.layer?.cornerRadius = corner
        glass.isHidden = style != .glass
        glass.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        slab.frame = bounds
        slab.layer?.cornerRadius = corner
        slab.layer?.backgroundColor = Self.slabColor(style, dark: dark).cgColor
        warp.frame = bounds
        warp.layer?.cornerRadius = corner
        contentView.frame = bounds
        content.setBoard(side: side, corner: corner)
        CATransaction.commit()
    }

    /// The prototypes' panel fills: glass tints the system blur (`--glass`), timing `--slab`, neon `--deck`.
    static func slabColor(_ style: BoardStyle, dark: Bool) -> RGBColor {
        switch (style, dark) {
        case (.glass, true): RGBColor(red: 20 / 255, green: 22 / 255, blue: 33 / 255, alpha: 0.58)
        case (.glass, false): RGBColor(red: 252 / 255, green: 252 / 255, blue: 1, alpha: 0.62)
        case (.timing, true): RGBColor(hex: 0x0C0E14)
        case (.timing, false): RGBColor(hex: 0xFBFBFD)
        case (.neon, true): RGBColor(hex: 0x05060C)
        case (.neon, false): RGBColor(hex: 0xF7F8FC)
        }
    }

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = convert(point, from: superview)
        guard bounds.contains(local) else { return nil }
        let corner = CGFloat(style.cornerRadius)
        let path = CGPath(roundedRect: bounds, cornerWidth: corner, cornerHeight: corner, transform: nil)
        return path.contains(local) ? self : nil
    }

    override func mouseDown(with event: NSEvent) { onMouseDown?(event) }
    override func mouseDragged(with event: NSEvent) { onMouseDragged?(event) }
    override func mouseUp(with event: NSEvent) { onMouseUp?(event) }
    override func rightMouseDown(with event: NSEvent) { onRightMouseDown?(event) }

    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties()
        content.scale = window?.backingScaleFactor ?? 2
        content.colorSpace = window?.colorSpace?.cgColorSpace
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        viewDidChangeBackingProperties()
    }
}
