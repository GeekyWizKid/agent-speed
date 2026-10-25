import AgentSpeedOrbModel
import AppKit

/// The board's own window (DESIGN 2c Interaction and window): borderless, non-activating and transparent like the
/// orb panel, exactly the size of the rounded square, so it takes the pointer only over the board. Never key or main.
final class BoardPanel: NSPanel {
    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 340, height: 340),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: true
        )
        Self.configure(self)
        acceptsMouseMovedEvents = true
        setAccessibilitySubrole(.floatingWindow)
    }

    /// Shared by the board and its effects window.
    static func configure(_ panel: NSPanel) {
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isMovable = false
        panel.isMovableByWindowBackground = false
        panel.animationBehavior = .none
        panel.isReleasedWhenClosed = false
        panel.isExcludedFromWindowsMenu = true
        panel.tabbingMode = .disallowed
        panel.becomesKeyOnlyIfNeeded = true
        panel.worksWhenModal = true
        panel.canHide = false
    }

    func apply(alwaysOnTop: Bool, allSpaces: Bool) {
        Self.apply(self, alwaysOnTop: alwaysOnTop, allSpaces: allSpaces)
    }

    static func apply(_ panel: NSPanel, alwaysOnTop: Bool, allSpaces: Bool) {
        let nextLevel: NSWindow.Level = alwaysOnTop ? .floating : .normal
        if panel.level != nextLevel { panel.level = nextLevel }
        var behavior: NSWindow.CollectionBehavior = [.fullScreenAuxiliary, .ignoresCycle, .transient]
        behavior.insert(allSpaces ? .canJoinAllSpaces : .moveToActiveSpace)
        if panel.collectionBehavior != behavior { panel.collectionBehavior = behavior }
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    /// Placement is managed by `BoardWindowController`; AppKit must not nudge the frame.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        frameRect
    }
}

/// The effects layer's window: `BoardEffects.margin` larger than the board on every side (sparks, waves and the
/// bloom reach outside it), a child of the board panel ordered above it, and never hit by the pointer.
final class BoardEffectsPanel: NSPanel {
    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 420),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: true
        )
        BoardPanel.configure(self)
        ignoresMouseEvents = true
        setAccessibilityElement(false)
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        frameRect
    }
}
