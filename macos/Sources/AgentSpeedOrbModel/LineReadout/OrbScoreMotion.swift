import AgentSpeedCore
import Foundation

/// How 智力 moves on the orb readout (DESIGN 12, *Motion*), kept as moments so any frame can be drawn at its time and a
/// still orb renders nothing. A rise of 3 or more counts up through every integer with easeOutCubic timing over
/// `min(0.8, 0.3 + 0.012 Δ)` s; a rise of 1 or 2 replaces the digits; a fall and `–` ↔ a number crossfade (0.2 s);
/// live ↔ resting crossfades the color (0.2 s, kept with Reduce Motion); the proof mark fades with the state (0.25 s;
/// Reduce Motion: at once). Under Reduce Motion every number change is a 0.15 s crossfade. The timings are the app's
/// `TaskScoreMotion`, which a test holds them to. Times are monotonic seconds (the app clock).
public struct OrbScoreMotion: Equatable, Sendable {
    public static let climbThreshold = 3
    public static let fall: TimeInterval = 0.2
    public static let reduced: TimeInterval = 0.15
    public static let form: TimeInterval = 0.2
    public static let proof: TimeInterval = 0.25
    /// The absent mark (U+2013), never `0`: 0 is a real score.
    public static let absent = "\u{2013}"

    public enum Change: Equatable, Sendable {
        case none
        case countUp(seconds: TimeInterval)
        case replace
        case crossfade(seconds: TimeInterval)
    }

    /// One frame of the number.
    public struct Frame: Equatable, Sendable {
        /// The number as drawn (mid count-up its current integer), `–` while none.
        public var text: String
        public var alpha: Double
        /// The text a crossfade fades out, and its opacity.
        public var outgoing: String?
        public var outgoingAlpha: Double
        /// 0 in the live number color, 1 in the pale one.
        public var pale: Double
        /// The proof mark's opacity.
        public var proof: Double
    }

    /// The number shown now; nil while none.
    public private(set) var shown: Int?
    /// Before the last change: as drawn at that moment (mid count-up, its current integer), and the value then shown,
    /// which Reduce Motion fades from.
    public private(set) var previous: Int?
    public private(set) var previousShown: Int?
    public private(set) var changedAt = -TimeInterval.infinity
    /// Resting or none: the pale color.
    public private(set) var pale: Bool
    public private(set) var paleFrom = -TimeInterval.infinity
    /// `unverified` or `pending`: the proof mark.
    public private(set) var unproven: Bool
    public private(set) var proofFrom = -TimeInterval.infinity

    public init(_ score: TaskSetReading = .none) {
        shown = score.shown
        previous = score.shown
        previousShown = score.shown
        pale = score.form != .live
        unproven = score.isUnproven
    }

    public static func climbSeconds(_ delta: Int) -> TimeInterval { min(0.8, 0.3 + 0.012 * Double(delta)) }

    public static func change(from old: Int?, to new: Int?, reduceMotion: Bool) -> Change {
        guard old != new else { return .none }
        if reduceMotion { return .crossfade(seconds: reduced) }
        guard let old, let new, new > old else { return .crossfade(seconds: fall) }
        return new - old < climbThreshold ? .replace : .countUp(seconds: climbSeconds(new - old))
    }

    /// The integer a count-up from `old` to `new` shows `elapsed` seconds in.
    public static func countUpValue(from old: Int, to new: Int, elapsed: TimeInterval) -> Int {
        let seconds = climbSeconds(new - old)
        let progress = min(max(seconds > 0 ? elapsed / seconds : 1, 0), 1)
        let eased = 1 - pow(1 - progress, 3)
        let value = Double(old) + Double(new - old) * eased
        return value >= 0 ? Int(value + 0.5) : -Int(-value + 0.5)
    }

    public static func text(_ value: Int?) -> String { value.map(String.init) ?? absent }

    /// Applies the set's value at `time`. A reversal part-way starts the color or the mark from where it stands; a
    /// change during a count-up starts from the integer it has reached.
    public mutating func update(_ score: TaskSetReading, at time: TimeInterval) {
        if score.shown != shown {
            previous = drawn(at: time)
            previousShown = shown
            shown = score.shown
            changedAt = time
        }
        let pale = score.form != .live
        if pale != self.pale {
            self.pale = pale
            paleFrom = Self.reversal(at: time, from: paleFrom, over: Self.form)
        }
        let unproven = score.isUnproven
        if unproven != self.unproven {
            self.unproven = unproven
            proofFrom = Self.reversal(at: time, from: proofFrom, over: Self.proof)
        }
    }

    /// The last number change as a surface draws it.
    public func change(reduceMotion: Bool) -> Change {
        Self.change(from: reduceMotion ? previousShown : previous, to: shown, reduceMotion: reduceMotion)
    }

    public func frame(at time: TimeInterval, reduceMotion: Bool) -> Frame {
        let pale = Self.progress(time, from: paleFrom, over: Self.form)
        let proof = Self.progress(time, from: proofFrom, over: reduceMotion ? 0 : Self.proof)
        var frame = Frame(text: Self.text(shown), alpha: 1, outgoing: nil, outgoingAlpha: 0,
                          pale: self.pale ? pale : 1 - pale, proof: unproven ? proof : 1 - proof)
        let elapsed = max(0, time - changedAt)
        switch change(reduceMotion: reduceMotion) {
        case .countUp(let seconds) where elapsed < seconds:
            if let previous, let shown { frame.text = String(Self.countUpValue(from: previous, to: shown, elapsed: elapsed)) }
        case .crossfade(let seconds) where elapsed < seconds:
            frame.alpha = elapsed / seconds
            frame.outgoing = Self.text(reduceMotion ? previousShown : previous)
            frame.outgoingAlpha = 1 - frame.alpha
        default:
            break
        }
        return frame
    }

    /// A count-up, a crossfade, the color or the mark moves at `time` (frames at the orb's 30 fps). Replacing digits
    /// and Reduce Motion's mark render the one frame of the content change.
    public func isMoving(at time: TimeInterval, reduceMotion: Bool) -> Bool {
        let number: TimeInterval = switch change(reduceMotion: reduceMotion) {
        case .countUp(let seconds), .crossfade(let seconds): seconds
        case .none, .replace: 0
        }
        return Self.within(time, from: changedAt, over: number) || Self.within(time, from: paleFrom, over: Self.form)
            || (!reduceMotion && Self.within(time, from: proofFrom, over: Self.proof))
    }

    /// The integer drawn at `time` without Reduce Motion: mid count-up its current value, else the shown one.
    private func drawn(at time: TimeInterval) -> Int? {
        guard case .countUp(let seconds) = change(reduceMotion: false), let previous, let shown,
              time - changedAt < seconds else { return shown }
        return Self.countUpValue(from: previous, to: shown, elapsed: max(0, time - changedAt))
    }

    private static func within(_ time: TimeInterval, from start: TimeInterval, over duration: TimeInterval) -> Bool {
        time >= start && time < start + duration
    }

    private static func progress(_ time: TimeInterval, from start: TimeInterval, over duration: TimeInterval) -> Double {
        guard duration > 0 else { return time >= start ? 1 : 0 }
        return min(max((time - start) / duration, 0), 1)
    }

    /// The start of a motion toward the other side that begins where the current one stands at `time`.
    private static func reversal(at time: TimeInterval, from start: TimeInterval, over duration: TimeInterval) -> TimeInterval {
        time - (1 - progress(time, from: start, over: duration)) * duration
    }
}
