import Foundation
import SwiftUI

extension EnvironmentValues {
    /// Reduce Motion as the app sees it (`AccessibilityState`, which a harness may force), set by each surface's host.
    @Entry var taskReduceMotion = false
}

/// How a shown score moves when it changes (DESIGN 12, *Motion*). A score changes only when an event is read, so
/// these are moments: nothing about 智力 animates continuously.
enum TaskScoreMotion {
    /// A rise of at least this many points climbs; smaller rises replace the digits in place.
    static let climbThreshold = 3
    static let fallSeconds = 0.2
    static let reducedSeconds = 0.15
    /// Live ↔ resting crossfades the color, kept with Reduce Motion.
    static let formSeconds = 0.2
    static let rollSeconds = 0.24

    /// What a surface does with a climb: the orb readout, the board hero and the card count up; the dashboard row rolls
    /// its changed digits; board rows and the menu bar replace them in place.
    enum Style: Sendable {
        case countUp, roll, inPlace
    }

    enum Change: Equatable, Sendable {
        case none
        /// Counts up through every intermediate value with easeOutCubic timing.
        case countUp(seconds: Double)
        /// The changed digits roll upward (0.24 s, the board hero's curve, blurred while entering).
        case roll
        case replace
        /// An opacity crossfade: every fall, `–` ↔ a number, and every change under Reduce Motion.
        case crossfade(seconds: Double)
    }

    /// `min(0.8, 0.3 + 0.012 Δ)` seconds: a test pass of +40 lands in 0.78 s.
    static func climbSeconds(_ delta: Int) -> Double {
        min(0.8, 0.3 + 0.012 * Double(delta))
    }

    static func change(from old: Int?, to new: Int?, style: Style, reduceMotion: Bool) -> Change {
        guard old != new else { return .none }
        if reduceMotion { return .crossfade(seconds: reducedSeconds) }
        guard let old, let new else { return .crossfade(seconds: fallSeconds) }
        if new < old { return .crossfade(seconds: fallSeconds) }
        if new - old < climbThreshold { return .replace }
        switch style {
        case .countUp: return .countUp(seconds: climbSeconds(new - old))
        case .roll: return .roll
        case .inPlace: return .replace
        }
    }

    static func easeOutCubic(_ progress: Double) -> Double {
        let t = progress.clamped(to: 0...1)
        return 1 - pow(1 - t, 3)
    }

    /// The integer a count-up shows `elapsed` seconds in: for surfaces that draw their own frames (orbs at 30 fps,
    /// boards at 60 fps for the count-up's duration).
    static func countUpValue(from old: Int, to new: Int, elapsed: Double) -> Int {
        let seconds = climbSeconds(new - old)
        let eased = easeOutCubic(seconds > 0 ? elapsed / seconds : 1)
        return TaskScoreText.rounded(Double(old) + Double(new - old) * eased)
    }

    /// easeOutCubic as a curve SwiftUI interpolates with.
    static func countUpAnimation(seconds: Double) -> Animation {
        .timingCurve(0.33, 1, 0.68, 1, duration: seconds)
    }

    /// The board hero's roll curve.
    static let rollAnimation = Animation.timingCurve(0.2, 0.8, 0.2, 1, duration: rollSeconds)
}

/// A score number that follows DESIGN 12's motion: climbs count up (or roll), small rises replace the digits, falls and
/// `–` ↔ a number crossfade, Reduce Motion crossfades everything in 0.15 s.
struct TaskScoreNumber: View {
    var value: Int?
    var style: TaskScoreMotion.Style = .countUp
    var font: Font
    var absent = "–"

    @Environment(\.taskReduceMotion) private var reduceMotion
    @State private var shown: Double?
    @State private var identity = 0

    var body: some View {
        let current = shown ?? value.map(Double.init)
        ZStack(alignment: .leading) {
            Group {
                if style == .roll {
                    // The digits themselves must not interpolate: the system's numeric transition rolls them.
                    Text(current.map { String(TaskScoreText.rounded($0)) } ?? absent)
                        .contentTransition(.numericText(countsDown: false))
                } else {
                    CountingText(value: current, absent: absent)
                }
            }
            .font(font)
            .monospacedDigit()
            .id(identity)
            .transition(.opacity)
        }
        .onChange(of: value) { old, new in
            switch TaskScoreMotion.change(from: old, to: new, style: style, reduceMotion: reduceMotion) {
            case .none:
                break
            case .countUp(let seconds):
                if shown == nil { shown = old.map(Double.init) }
                withAnimation(TaskScoreMotion.countUpAnimation(seconds: seconds)) { shown = new.map(Double.init) }
            case .roll:
                withAnimation(TaskScoreMotion.rollAnimation) { shown = new.map(Double.init) }
            case .replace:
                var transaction = Transaction()
                transaction.disablesAnimations = true
                withTransaction(transaction) { shown = new.map(Double.init) }
            case .crossfade(let seconds):
                withAnimation(.easeOut(duration: seconds)) {
                    identity += 1
                    shown = new.map(Double.init)
                }
            }
        }
    }
}

/// The interpolated value as the nearest integer, so a count-up passes through every intermediate number.
private struct CountingText: View, Animatable {
    var value: Double?
    var absent: String

    nonisolated var animatableData: Double {
        get { value ?? 0 }
        set { if value != nil { value = newValue } }
    }

    var body: some View {
        Text(value.map { String(TaskScoreText.rounded($0)) } ?? absent)
    }
}
