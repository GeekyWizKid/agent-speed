import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation

/// How a board's 智力 changes from one fresh tick to the next (DESIGN 12 *Motion*; 2c *Updates, motion and quiet*): the
/// hero counts a rise of 3 or more up through the integers, rows replace their digits in place, every fall and `–` ↔ a
/// number crossfades (0.2 s), live ↔ resting crossfades the color (0.2 s, kept with Reduce Motion), the proof mark fades
/// in and out (0.25 s, at once with Reduce Motion), and Reduce Motion turns every value change into a 0.15 s crossfade.
enum BoardScoreMotion {
    struct Hero: Equatable {
        /// Seconds of a count-up from the shown value to the new one.
        var climb: Double?
        /// The number's image crossfades over this many seconds (its proof mark fades on its own).
        var numberFade: Double?
        /// The arc and tip, or the meter, crossfade over this many seconds (their resting and proof looks).
        var shapeFade: Double?

        static let none = Hero()
    }

    static func hero(from old: TaskSetReading, to new: TaskSetReading, reduceMotion: Bool) -> Hero {
        var motion = Hero()
        switch TaskScoreMotion.change(from: old.shown, to: new.shown, style: .countUp, reduceMotion: reduceMotion) {
        case .countUp(let seconds): motion.climb = seconds
        case .crossfade(let seconds): motion.numberFade = seconds
        case .none, .roll, .replace: break
        }
        if old.form != .none, new.form != .none {
            if old.form != new.form {
                motion.numberFade = max(motion.numberFade ?? 0, TaskScoreMotion.formSeconds)
                motion.shapeFade = TaskScoreMotion.formSeconds
            }
            if old.isUnproven != new.isUnproven, !reduceMotion {
                motion.shapeFade = max(motion.shapeFade ?? 0, ProofMark.fadeSeconds)
            }
        }
        return motion
    }

    /// The crossfade of a row's 智力 cell, nil when its new image replaces the old in place.
    static func row(from old: BoardRowScore, to new: BoardRowScore, reduceMotion: Bool) -> Double? {
        var fade: Double?
        if case .crossfade(let seconds) = TaskScoreMotion.change(from: old.shown, to: new.shown, style: .inPlace,
                                                                 reduceMotion: reduceMotion) {
            fade = seconds
        }
        if old.shown != nil, new.shown != nil {
            if old.live != new.live { fade = max(fade ?? 0, TaskScoreMotion.formSeconds) }
            if old.unproven != new.unproven, !reduceMotion { fade = max(fade ?? 0, ProofMark.fadeSeconds) }
        }
        return fade
    }
}

/// A count-up of the hero's 智力 (DESIGN 12 *Motion*): every integer from `from` to `to` over
/// `min(0.8, 0.3 + 0.012 Δ)` s with easeOutCubic timing; the arc's end and the meter follow the shown value.
struct BoardScoreClimb: Equatable {
    var from: Int
    var to: Int
    var start: TimeInterval

    var seconds: Double { TaskScoreMotion.climbSeconds(to - from) }
    var end: TimeInterval { start + seconds }

    func shown(at now: TimeInterval) -> Int {
        now >= end ? to : TaskScoreMotion.countUpValue(from: from, to: to, elapsed: max(0, now - start))
    }
}

/// The hero's 智力 across layouts: the value it shows now (a count-up passes through the integers between ticks,
/// laid out again on every frame of the roll class) and the motion a fresh tick plays.
struct BoardHeroScore {
    private(set) var climb: BoardScoreClimb?

    /// When a running count-up lands; nil when none runs.
    func climbsUntil(after now: TimeInterval) -> TimeInterval? {
        guard let climb, climb.end > now else { return nil }
        return climb.end
    }

    /// One layout of `reading`. `previous`: what the last fresh tick showed (nil before the first); `fresh`: this
    /// layout is a new tick, so its changes move. Returns the integer to draw and the motion to play.
    mutating func update(_ reading: TaskSetReading, previous: TaskSetReading?, fresh: Bool, now: TimeInterval,
                         reduceMotion: Bool) -> (shown: Int?, motion: BoardScoreMotion.Hero) {
        if let running = climb, now >= running.end || running.to != reading.shown && !fresh { climb = nil }
        guard fresh, let old = previous else { return (climb?.shown(at: now) ?? reading.shown, .none) }
        let displayed = climb?.shown(at: now) ?? old.shown
        var motion = BoardScoreMotion.hero(from: old, to: reading, reduceMotion: reduceMotion)
        if let running = climb, running.to == reading.shown {
            // The same value while climbing: the count-up goes on.
            motion.climb = nil
            return (running.shown(at: now), motion)
        }
        if climb != nil, !reduceMotion, let from = displayed, let to = reading.shown, to > from,
           to - from >= TaskScoreMotion.climbThreshold {
            // A further rise while climbing climbs on from where the number is.
            motion.climb = TaskScoreMotion.climbSeconds(to - from)
        }
        if motion.climb != nil, let from = displayed, let to = reading.shown, to > from {
            climb = BoardScoreClimb(from: from, to: to, start: now)
            return (from, motion)
        }
        climb = nil
        motion.climb = nil
        return (reading.shown, motion)
    }
}
