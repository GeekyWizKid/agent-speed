import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 12 on the orb readout (DESIGN 10, Intelligence readout): live in the number token at 80 %, resting and `–` in
/// the unit token, never `0` for none; the proof mark under an unproven score; the score's motion as drawn; the score
/// words read by VoiceOver.
@Suite("Orb score readout")
@MainActor
struct OrbScoreReadoutTests {
    typealias Lines = OrbLineReadoutTests

    static let palette = OrbLineReadout.Palette.standard(dark: true, increaseContrast: false)

    static func near(_ color: SIMD4<Float>, _ expected: RGBColor) -> Bool {
        let difference = color - expected.simd
        return max(abs(difference.x), abs(difference.y), abs(difference.z), abs(difference.w)) < 1e-4
    }

    static func frame(_ score: TaskSetReading, thinking: String = "128", style: OrbStyleKind = .led, motion: OrbScoreMotion? = nil,
                      reduceMotion: Bool = false, increaseContrast: Bool = false) -> OrbFrame {
        Lines.frame(style, diameter: 148, language: .zhHant, format: .intelligence, .intelligence(score: score, thinking: thinking),
                    scoreMotion: motion, reduceMotion: reduceMotion, increaseContrast: increaseContrast)
    }

    /// The intelligence line: its glyph, then the score's pieces.
    static func scoreLine(_ frame: OrbFrame, style: OrbStyleKind = .led) throws -> [OrbLineReadoutTests.Piece] {
        let lines = Lines.lines(frame, placement: .of(style))
        return try #require(lines.first)
    }

    // MARK: Forms

    @Test(.enabled(if: OrbGPU.shared != nil))
    func liveIsInTheNumberColorRestingAndAbsentInTheUnitColor() throws {
        let cases: [(TaskSetReading, RGBColor, Int)] = [
            (Lines.live(86), Self.palette.number, 2),
            (Lines.score(.resting, 72), Self.palette.secondary, 2),
            (.none, Self.palette.secondary, 1),
        ]
        for (score, color, digits) in cases {
            let line = try Self.scoreLine(Self.frame(score))
            try #require(line.count == 1 + digits)
            #expect(Self.near(line[0].color, Self.palette.secondary))
            for digit in line.dropFirst() { #expect(Self.near(digit.color, color), "\(score)") }
        }
        // Light, and the network readout's Increase Contrast tokens.
        #expect(OrbLineReadout.Palette.standard(dark: false, increaseContrast: false).secondary == RGBColor.black.opacity(0.5))
        #expect(OrbLineReadout.Palette.standard(dark: true, increaseContrast: true).number == RGBColor.white.opacity(0.92))
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func noneIsTheAbsentMarkNeverZero() throws {
        let none = try Self.scoreLine(Self.frame(.none, thinking: "22"))
        let zero = try Self.scoreLine(Self.frame(Lines.live(0)))
        try #require(none.count == 2 && zero.count == 2)
        #expect(none[1].texture !== zero[1].texture)
        // Centred by what it shows: the line is narrower than with two digits.
        let two = try Self.scoreLine(Self.frame(Lines.live(86)))
        #expect(none[0].x > two[0].x)
    }

    // MARK: Proof mark

    @Test(.enabled(if: OrbGPU.shared != nil))
    func onlyAnUnprovenScoreSitsOnTheProofMark() {
        let cases: [(TaskState?, Bool)] = [(.unverified, true), (.pending, true), (.passing, false), (.failing, false), (TaskState.none, false)]
        for style in OrbStyleKind.allCases {
            for (state, marked) in cases {
                for form in [TaskValueForm.live, .resting] {
                    let frame = Self.frame(Lines.score(form, 34, state), style: style)
                    #expect(Lines.pieces(frame, placement: .of(style)).marks.count == (marked ? 1 : 0), "\(style) \(form) \(String(describing: state))")
                }
            }
            #expect(Lines.pieces(Self.frame(.none, style: style), placement: .of(style)).marks.isEmpty)
        }
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func theMarkSpansTheDigitsBelowTheBaselineAndTakesNoRoom() throws {
        for style in OrbStyleKind.allCases {
            let placement = OrbLinePlacement.of(style)
            let t = placement.textSize(diameter: 148)
            let p = 1.25 * t
            let frame = Self.frame(Lines.score(.live, 34, .unverified), style: style)
            let (text, marks) = Lines.pieces(frame, placement: placement)
            let mark = try #require(marks.first)
            let line = try Self.scoreLine(frame, style: style)
            let digits = Array(line.dropFirst())
            try #require(digits.count == 2)
            // From the left edge of the number's advance to its right edge.
            #expect(abs(mark.x - digits[0].x) < 0.01)
            #expect(abs((mark.right - mark.x) - (digits[1].right - digits[0].x)) <= 0.5)
            // Centred 0.22 p below the baseline, on a device pixel; clear of the thinking line's cap top.
            let diameter = ProofMark.dotDiameter(textSize: p, scale: 2, increasedContrast: false)
            let centre = mark.baseline + diameter / 2
            #expect(abs(centre - (digits[0].baseline + 0.22 * p)) <= 0.26, "\(style)")
            let thinking = try #require(Lines.lines(frame, placement: placement).last)
            #expect(thinking[0].baseline - 0.70 * t - centre >= 0.34 * t - 0.26)
            // The number's color (its 70 % is in the dots).
            #expect(mark.color == digits[0].color)
            // No room taken: the line is laid out exactly as a verified score.
            let verified = Lines.pieces(Self.frame(Lines.score(.live, 34, .passing), style: style), placement: placement).text
            #expect(text.map(\.x) == verified.map(\.x))
            #expect(text.map(\.baseline) == verified.map(\.baseline))
        }
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func aRestingMarkIsPaleAndIncreaseContrastMakesTheDotsLarger() throws {
        let resting = Lines.pieces(Self.frame(Lines.score(.resting, 34, .pending)), placement: .led)
        let restingMark = try #require(resting.marks.first)
        #expect(Self.near(restingMark.color, Self.palette.secondary))
        let normal = try #require(Lines.pieces(Self.frame(Lines.score(.live, 34, .pending)), placement: .led).marks.first)
        let contrast = try #require(Lines.pieces(Self.frame(Lines.score(.live, 34, .pending), increaseContrast: true),
                                                 placement: .led).marks.first)
        #expect(contrast.height > normal.height)
        #expect(contrast.texture !== normal.texture)
    }

    // MARK: Motion as drawn

    @Test(.enabled(if: OrbGPU.shared != nil))
    func aClimbDrawsTheIntegerItHasReached() throws {
        var motion = OrbScoreMotion(Lines.score(.live, 40, .failing))
        motion.update(Lines.live(80), at: 100 - OrbScoreMotion.climbSeconds(40) / 2)
        let counting = try Self.scoreLine(Self.frame(Lines.live(80), motion: motion))
        let still = try Self.scoreLine(Self.frame(Lines.live(75)))
        try #require(counting.count == 3 && still.count == 3)
        #expect(counting[1].texture === still[1].texture && counting[2].texture === still[2].texture)
        // Reduce Motion: its crossfade ended long ago.
        let reduced = try Self.scoreLine(Self.frame(Lines.live(80), motion: motion, reduceMotion: true))
        let eighty = try Self.scoreLine(Self.frame(Lines.live(80)))
        #expect(reduced[1].texture === eighty[1].texture && reduced[2].texture === eighty[2].texture)
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func aFallCrossfadesBothNumbersInPlace() throws {
        var motion = OrbScoreMotion(Lines.live(80))
        motion.update(Lines.live(62), at: 99.95)
        let line = try Self.scoreLine(Self.frame(Lines.live(62), motion: motion))
        try #require(line.count == 5)
        let full = try Self.scoreLine(Self.frame(Lines.live(62)))[1].alpha
        let alphas = line.dropFirst().map { $0.alpha / full }.sorted()
        for (alpha, expected) in zip(alphas, [0.25, 0.25, 0.75, 0.75]) { #expect(abs(alpha - expected) < 1e-3) }
        // Both start at the number's left edge.
        #expect(Set(line.dropFirst().map(\.x)).count == 2)
    }

    @Test(.enabled(if: OrbGPU.shared != nil))
    func goingIdleCrossfadesTheColor() throws {
        var motion = OrbScoreMotion(Lines.live(80))
        motion.update(Lines.score(.resting, 80), at: 99.9)
        let line = try Self.scoreLine(Self.frame(Lines.score(.resting, 80), motion: motion))
        try #require(line.count == 3)
        #expect(Self.near(line[1].color, Self.palette.number.mix(Self.palette.secondary, 0.5)))
    }

    // MARK: Runtime

    @Test func theRuntimeFeedsTheSetsHighestScoreWithItsCount() throws {
        let suite = "OrbScoreReadoutTests"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let settings = AppSettings(defaults: try #require(UserDefaults(suiteName: suite)))
        defer { UserDefaults.standard.removePersistentDomain(forName: suite) }
        let runtime = OrbRuntime(config: OrbConfig(id: "orb", traffic: .intelligence))
        let snapshot = PreviewData.snapshot(speed: 120, redline: 400)
        runtime.ingest(snapshot, settings: settings, cost: nil, isPreviewing: false, now: 10)
        let lines = try #require(runtime.feed.lines)
        #expect(lines.format == .intelligence)
        let score = try #require(lines.reading?.content.score)
        let live = snapshot.agents.filter { $0.intelligence.score.form == .live }
        #expect(score.form == .live)
        #expect(score.shown == live.compactMap(\.intelligence.score.shown).max())
        #expect(score.count == live.count && score.count >= 2)
        #expect(score == TaskSetReading(score: snapshot.intelligence.score, agents: snapshot.agents))
        // A network orb never carries a score.
        let network = OrbRuntime(config: OrbConfig(id: "net", traffic: .machine))
        network.ingest(snapshot, settings: settings, cost: nil, isPreviewing: false, now: 10)
        #expect(network.feed.lines == nil)
    }

    // MARK: Accessibility

    @Test func theLabelCarriesTheScoreWords() {
        let zh = Localizer(preference: .zhHant)
        func sentence(_ score: TaskSetReading, _ thinking: String?, _ l10n: Localizer) -> String {
            OrbAccessibilityReporter.intelligenceSentence(TaskScoreValue(score), thinking: thinking, l10n)
        }
        #expect(sentence(Lines.score(.live, 86, .passing, count: 4), "128", zh) == "智力 86，4 個 agent 中最高，思考每秒 128 個 token")
        #expect(sentence(Lines.score(.live, 9, .unverified), "64", zh) == "智力 9，尚未驗證，思考每秒 64 個 token")
        #expect(sentence(Lines.score(.live, 34, .pending), "41", zh) == "智力 34，待重新驗證，思考每秒 41 個 token")
        #expect(sentence(Lines.score(.resting, 72, .passing, count: 2), nil, zh) == "智力 72，閒置中，2 個 agent 中最高")
        #expect(sentence(Lines.score(.resting, 9, .unverified, count: 3), nil, zh) == "智力 9，尚未驗證，閒置中，3 個 agent 中最高")
        #expect(sentence(.none, "22", zh) == "智力：尚無評分，思考每秒 22 個 token")
        #expect(sentence(.none, nil, zh) == "智力：尚無評分")
        let en = Localizer(preference: .en)
        #expect(sentence(Lines.score(.live, 86, .passing, count: 4), "128", en)
            == "intelligence 86, the highest of 4 agents, thinking 128 tokens per second")
        #expect(sentence(.none, "22", en) == "intelligence: no score yet, thinking 22 tokens per second")
        for preference in [LanguagePreference.zhHans, .ja] {
            let l10n = Localizer(preference: preference)
            let words = sentence(.none, "22", l10n)
            #expect(words.hasPrefix(l10n("intel.none")))
            #expect(words.hasSuffix(String(l10n("a11y.orb.intel", "86", "22").dropFirst(l10n("a11y.orb.intel.only", "86").count))))
        }
    }

    // MARK: Timing

    /// The orb keeps its own copy of DESIGN 12's timings for its frame policy; it must match the app's.
    @Test func theOrbsTimingsAreTheTaskScoreMotions() {
        #expect(OrbScoreMotion.climbThreshold == TaskScoreMotion.climbThreshold)
        #expect(OrbScoreMotion.fall == TaskScoreMotion.fallSeconds)
        #expect(OrbScoreMotion.reduced == TaskScoreMotion.reducedSeconds)
        #expect(OrbScoreMotion.form == TaskScoreMotion.formSeconds)
        #expect(OrbScoreMotion.proof == ProofMark.fadeSeconds)
        let values: [Int?] = [nil] + Array(0...100).map(Optional.some)
        for old in values {
            for new in values {
                for reduceMotion in [false, true] {
                    let orb = OrbScoreMotion.change(from: old, to: new, reduceMotion: reduceMotion)
                    let app = TaskScoreMotion.change(from: old, to: new, style: .countUp, reduceMotion: reduceMotion)
                    let same = switch (orb, app) {
                    case (.none, .none), (.replace, .replace): true
                    case (.countUp(let a), .countUp(let b)), (.crossfade(let a), .crossfade(let b)): a == b
                    default: false
                    }
                    #expect(same, "\(String(describing: old)) → \(String(describing: new)) \(reduceMotion)")
                }
            }
        }
        for (old, new) in [(0, 3), (40, 80), (12, 100), (0, 100)] {
            for step in 0...40 {
                let elapsed = Double(step) / 40 * TaskScoreMotion.climbSeconds(new - old)
                #expect(OrbScoreMotion.countUpValue(from: old, to: new, elapsed: elapsed)
                    == TaskScoreMotion.countUpValue(from: old, to: new, elapsed: elapsed))
            }
        }
    }
}
