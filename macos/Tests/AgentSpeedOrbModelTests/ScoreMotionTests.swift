import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeedOrbModel

/// DESIGN 12, *Motion*, on the orb readout: climbs count up at the orb's 30 fps for their duration only, small rises
/// replace the digits, falls and `–` ↔ a number crossfade, live ↔ resting crossfades the color, the proof mark fades
/// with the state, Reduce Motion crossfades every change.
@Suite("Orb score motion")
struct ScoreMotionTests {
    static func near(_ a: Double, _ b: Double) -> Bool { abs(a - b) < 1e-9 }

    static func score(_ form: TaskValueForm, _ shown: Int?, _ state: TaskState? = .passing) -> TaskSetReading {
        LineQuietTests.score(form, shown, state)
    }

    static func seconds(_ change: OrbScoreMotion.Change) -> Double? {
        switch change {
        case .countUp(let seconds), .crossfade(let seconds): seconds
        case .none, .replace: nil
        }
    }

    // MARK: Decisions

    @Test func risesOfThreeOrMoreCountUpSmallerOnesReplaceAndFallsCrossfade() {
        #expect(OrbScoreMotion.change(from: 40, to: 40, reduceMotion: false) == .none)
        #expect(OrbScoreMotion.change(from: 40, to: 41, reduceMotion: false) == .replace)
        #expect(OrbScoreMotion.change(from: 40, to: 42, reduceMotion: false) == .replace)
        #expect(OrbScoreMotion.change(from: 40, to: 43, reduceMotion: false) == .countUp(seconds: OrbScoreMotion.climbSeconds(3)))
        #expect(OrbScoreMotion.change(from: 40, to: 39, reduceMotion: false) == .crossfade(seconds: 0.2))
        #expect(OrbScoreMotion.change(from: 80, to: 0, reduceMotion: false) == .crossfade(seconds: 0.2))
        #expect(OrbScoreMotion.change(from: nil, to: 12, reduceMotion: false) == .crossfade(seconds: 0.2))
        #expect(OrbScoreMotion.change(from: 12, to: nil, reduceMotion: false) == .crossfade(seconds: 0.2))
        for (old, new) in [(40, 41), (40, 80), (40, 0), (12, 13)] as [(Int?, Int?)] + [(nil, 12)] {
            #expect(OrbScoreMotion.change(from: old, to: new, reduceMotion: true) == .crossfade(seconds: 0.15))
        }
        #expect(OrbScoreMotion.change(from: nil, to: nil, reduceMotion: true) == .none)
    }

    @Test func aClimbTakesAtMostEightTenthsOfASecond() {
        #expect(Self.near(OrbScoreMotion.climbSeconds(40), 0.78))
        #expect(Self.near(OrbScoreMotion.climbSeconds(3), 0.336))
        #expect(OrbScoreMotion.climbSeconds(42) == 0.8)
        #expect(OrbScoreMotion.climbSeconds(100) == 0.8)
    }

    // MARK: Count-up

    @Test func aCountUpPassesThroughEveryIntegerWithEaseOutCubicTiming() {
        let seconds = OrbScoreMotion.climbSeconds(40)
        var seen = Set<Int>()
        var last = 40
        for step in 0...Int(seconds * 10_000) {
            let value = OrbScoreMotion.countUpValue(from: 40, to: 80, elapsed: Double(step) / 10_000)
            #expect(value >= last)
            last = value
            seen.insert(value)
        }
        #expect(seen == Set(40...80))
        #expect(OrbScoreMotion.countUpValue(from: 40, to: 80, elapsed: 0) == 40)
        #expect(OrbScoreMotion.countUpValue(from: 40, to: 80, elapsed: seconds) == 80)
        // Quick at first, settling into the final number: half way in time is 7/8 of the way.
        #expect(OrbScoreMotion.countUpValue(from: 40, to: 80, elapsed: seconds / 2) == 75)
    }

    @Test func aClimbRendersAtTheOrbsRateForItsDurationOnly() {
        var motion = OrbScoreMotion(Self.score(.live, 40, .failing))
        #expect(!motion.isMoving(at: 0, reduceMotion: false))
        motion.update(Self.score(.live, 80), at: 10)
        let end = 10 + OrbScoreMotion.climbSeconds(40)
        #expect(motion.frame(at: 10, reduceMotion: false).text == "40")
        #expect(motion.frame(at: 10.39, reduceMotion: false).text == "75")
        #expect(motion.frame(at: end, reduceMotion: false).text == "80")
        #expect(motion.frame(at: 99, reduceMotion: false).text == "80")
        #expect(motion.frame(at: 10.2, reduceMotion: false).alpha == 1)
        #expect(motion.frame(at: 10.2, reduceMotion: false).outgoing == nil)
        #expect(motion.isMoving(at: 10, reduceMotion: false))
        #expect(motion.isMoving(at: end - 0.01, reduceMotion: false))
        #expect(!motion.isMoving(at: end, reduceMotion: false))
        #expect(!motion.isMoving(at: 9.99, reduceMotion: false))
    }

    @Test func aSmallRiseReplacesTheDigitsWithoutFrames() {
        var motion = OrbScoreMotion(Self.score(.live, 80))
        motion.update(Self.score(.live, 82), at: 5)
        #expect(motion.change(reduceMotion: false) == .replace)
        #expect(motion.frame(at: 5, reduceMotion: false) == OrbScoreMotion.Frame(text: "82", alpha: 1, outgoing: nil,
                                                                                 outgoingAlpha: 0, pale: 0, proof: 0))
        #expect(!motion.isMoving(at: 5, reduceMotion: false))
    }

    // MARK: Crossfades

    @Test func aFallCrossfadesInPlace() {
        var motion = OrbScoreMotion(Self.score(.live, 80))
        motion.update(Self.score(.live, 62), at: 5)
        let frame = motion.frame(at: 5.05, reduceMotion: false)
        #expect(frame.text == "62" && frame.outgoing == "80")
        #expect(Self.near(frame.alpha, 0.25) && Self.near(frame.outgoingAlpha, 0.75))
        #expect(motion.isMoving(at: 5.19, reduceMotion: false))
        #expect(!motion.isMoving(at: 5.2, reduceMotion: false))
        #expect(motion.frame(at: 5.2, reduceMotion: false).outgoing == nil)
    }

    @Test func theAbsentMarkCrossfadesWithANumberInThePaleColor() {
        var motion = OrbScoreMotion(.none)
        #expect(motion.frame(at: 0, reduceMotion: false).text == "\u{2013}")
        #expect(motion.frame(at: 0, reduceMotion: false).pale == 1)
        motion.update(Self.score(.live, 12, .unverified), at: 1)
        let frame = motion.frame(at: 1.1, reduceMotion: false)
        #expect(frame.text == "12" && frame.outgoing == "\u{2013}")
        #expect(Self.near(frame.pale, 0.5))
        motion.update(.none, at: 5)
        #expect(motion.frame(at: 5.1, reduceMotion: false).text == "\u{2013}")
        #expect(motion.frame(at: 5.1, reduceMotion: false).outgoing == "12")
    }

    @Test func liveAndRestingCrossfadeTheColorEvenUnderReduceMotion() {
        var motion = OrbScoreMotion(Self.score(.live, 80))
        #expect(motion.frame(at: 0, reduceMotion: false).pale == 0)
        motion.update(Self.score(.resting, 80), at: 3)
        for reduceMotion in [false, true] {
            #expect(Self.near(motion.frame(at: 3.05, reduceMotion: reduceMotion).pale, 0.25))
            #expect(motion.frame(at: 3.2, reduceMotion: reduceMotion).pale == 1)
            #expect(motion.isMoving(at: 3.1, reduceMotion: reduceMotion))
            #expect(!motion.isMoving(at: 3.2, reduceMotion: reduceMotion))
        }
        // The text itself does not change.
        #expect(motion.frame(at: 3.1, reduceMotion: false).outgoing == nil)
        // Back to live part way: from where the color stands.
        var back = OrbScoreMotion(Self.score(.live, 80))
        back.update(Self.score(.resting, 80), at: 3)
        back.update(Self.score(.live, 80), at: 3.05)
        #expect(Self.near(back.frame(at: 3.05, reduceMotion: false).pale, 0.25))
        #expect(back.frame(at: 3.1, reduceMotion: false).pale == 0)
    }

    @Test func reduceMotionCrossfadesEveryChangeFromTheValueThatWasShown() {
        var motion = OrbScoreMotion(Self.score(.live, 40))
        motion.update(Self.score(.live, 80), at: 1)
        let frame = motion.frame(at: 1.075, reduceMotion: true)
        #expect(frame.text == "80" && frame.outgoing == "40")
        #expect(Self.near(frame.alpha, 0.5))
        #expect(motion.isMoving(at: 1.14, reduceMotion: true))
        #expect(!motion.isMoving(at: 1.15, reduceMotion: true))
        // A change while a climb would still run: Reduce Motion showed 80 by then, so it fades from 80.
        motion.update(Self.score(.live, 70), at: 1.5)
        #expect(motion.frame(at: 1.55, reduceMotion: true).outgoing == "80")
    }

    @Test func aChangeDuringACountUpStartsFromTheIntegerItReached() {
        var motion = OrbScoreMotion(Self.score(.live, 40))
        motion.update(Self.score(.live, 80), at: 0)
        let reached = OrbScoreMotion.countUpValue(from: 40, to: 80, elapsed: 0.2)
        motion.update(Self.score(.live, 90), at: 0.2)
        #expect(motion.change(reduceMotion: false) == .countUp(seconds: OrbScoreMotion.climbSeconds(90 - reached)))
        #expect(motion.frame(at: 0.2, reduceMotion: false).text == String(reached))
        // A fall instead fades out the integer that was drawn.
        var fall = OrbScoreMotion(Self.score(.live, 40))
        fall.update(Self.score(.live, 80), at: 0)
        fall.update(Self.score(.live, 30), at: 0.2)
        #expect(fall.frame(at: 0.25, reduceMotion: false).outgoing == String(reached))
    }

    // MARK: Proof mark

    @Test func theProofMarkFadesWithTheStateAndAtOnceUnderReduceMotion() {
        var motion = OrbScoreMotion(Self.score(.live, 80))
        #expect(motion.frame(at: 0, reduceMotion: false).proof == 0)
        motion.update(Self.score(.live, 80, .pending), at: 2)
        #expect(Self.near(motion.frame(at: 2.125, reduceMotion: false).proof, 0.5))
        #expect(motion.frame(at: 2.25, reduceMotion: false).proof == 1)
        #expect(motion.isMoving(at: 2.2, reduceMotion: false) && !motion.isMoving(at: 2.25, reduceMotion: false))
        #expect(motion.frame(at: 2, reduceMotion: true).proof == 1)
        // Reduce Motion: the content change's own frame draws it.
        #expect(!motion.isMoving(at: 2, reduceMotion: true))
        // Verified again part way out: from where the mark stands.
        var reversal = OrbScoreMotion(Self.score(.live, 80))
        reversal.update(Self.score(.live, 80, .unverified), at: 0)
        reversal.update(Self.score(.live, 80, .passing), at: 0.1)
        #expect(Self.near(reversal.frame(at: 0.1, reduceMotion: false).proof, 0.4))
        #expect(reversal.frame(at: 0.2, reduceMotion: false).proof == 0)
    }

    @Test func onlyUnverifiedAndPendingCarryTheMarkAndNoneNever() {
        for (state, marked) in [(TaskState.unverified, true), (.pending, true), (.passing, false), (.failing, false), (.none, false)] {
            for form in [TaskValueForm.live, .resting] {
                #expect(OrbScoreMotion(Self.score(form, 50, state)).frame(at: 0, reduceMotion: false).proof == (marked ? 1 : 0))
            }
        }
        #expect(OrbScoreMotion(TaskSetReading(form: .none, shown: nil, state: .unverified, count: 0, agentId: nil))
            .frame(at: 0, reduceMotion: false).proof == 0)
    }

    @Test func aNewMotionIsStill() {
        for score in [Self.score(.live, 86), Self.score(.resting, 72, .pending), .none] {
            let motion = OrbScoreMotion(score)
            for time in [-1e9, 0, 1e9] {
                #expect(!motion.isMoving(at: time, reduceMotion: false))
                #expect(!motion.isMoving(at: time, reduceMotion: true))
            }
            #expect(motion.frame(at: 0, reduceMotion: false).alpha == 1)
            #expect(motion.frame(at: 0, reduceMotion: false).pale == (score.form == .live ? 0 : 1))
        }
    }

    @Test func theReadingsFramePolicyIncludesTheScoreButNotOnceGone() {
        var motion = OrbScoreMotion(Self.score(.live, 40))
        motion.update(Self.score(.live, 80), at: 20)
        let reading = OrbLineReading(content: .intelligence(score: Self.score(.live, 80), thinking: "12"), shownAt: 0,
                                     quietAt: 30, scoreMotion: motion)
        #expect(reading.motion(at: 20.5, reduceMotion: false).frames)
        #expect(!reading.motion(at: 21, reduceMotion: false).frames)
        let gone = OrbLineReading(content: reading.content, shownAt: 0, quietAt: 10, scoreMotion: motion)
        #expect(!gone.motion(at: 20.5, reduceMotion: false).frames)
    }
}
