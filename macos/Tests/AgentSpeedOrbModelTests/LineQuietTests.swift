import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeedOrbModel

@Suite("Orb line readout quiet rule")
struct LineQuietTests {
    static func traffic(_ reading: OrbLineReading?) -> (rx: TrafficFormat.Parts, tx: TrafficFormat.Parts)? {
        guard case .traffic(let rx, let tx)? = reading?.content else { return nil }
        return (rx, tx)
    }

    @Test func quietSamplesNeverShowTheReadout() {
        var rule = OrbLineQuietRule()
        let changed1 = rule.apply(TrafficRate(rx: 400, tx: 300), bits: false, at: 0)
        #expect(!changed1)
        #expect(rule.reading == nil)
    }

    @Test func loudSampleFadesInAndQuietOneFadesOutAfterTenSeconds() throws {
        var rule = OrbLineQuietRule()
        let changed2 = rule.apply(TrafficRate(rx: 12_400_000, tx: 186_000), bits: false, at: 100)
        #expect(changed2)
        let reading = try #require(rule.reading)
        #expect(Self.traffic(reading)?.rx == .init(number: "12.4", unit: "MB/s"))
        #expect(Self.traffic(reading)?.tx == .init(number: "186", unit: "KB/s"))
        #expect(reading.alpha(at: 100, reduceMotion: false) == 0)
        #expect(reading.alpha(at: 100.25, reduceMotion: false) == 1)
        #expect(reading.isFading(at: 100.1, reduceMotion: false))
        // Quiet samples keep updating the numbers while it is shown, without moving the quiet moment.
        let changed3 = rule.apply(TrafficRate(rx: 200, tx: 100), bits: false, at: 101)
        #expect(changed3)
        #expect(rule.reading?.quietAt == 110)
        #expect(Self.traffic(rule.reading)?.rx.number == "200")
        #expect(abs((rule.reading?.alpha(at: 110.3, reduceMotion: false) ?? 0) - 0.5) < 1e-9)
        // Once gone, quiet samples change nothing (no frames).
        let changed4 = rule.apply(TrafficRate(rx: 300, tx: 100), bits: false, at: 111)
        #expect(!changed4)
    }

    @Test func loudSampleDuringFadeOutReturnsFromCurrentOpacity() throws {
        var rule = OrbLineQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 0)
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 10.3)
        let reading = try #require(rule.reading)
        #expect(abs(reading.alpha(at: 10.3, reduceMotion: false) - 0.5) < 1e-9)
        #expect(reading.quietAt == 20.3)
    }

    @Test func reduceMotionMakesFadesInstant() throws {
        var rule = OrbLineQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: true, at: 0)
        let reading = try #require(rule.reading)
        #expect(Self.traffic(reading)?.rx == .init(number: "40.0", unit: "Kb/s"))
        #expect(reading.alpha(at: 0, reduceMotion: true) == 1)
        #expect(reading.alpha(at: 10, reduceMotion: true) == 0)
        #expect(!reading.isFading(at: 0.1, reduceMotion: true))
    }

    @Test func unavailableClearsTheReadout() {
        var rule = OrbLineQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 0)
        let changed5 = rule.apply(nil, bits: false, at: 1)
        #expect(changed5)
        #expect(rule.reading == nil)
    }

    static func sums(index: Double, thinking: Double) -> IntelligenceSums {
        var sums = IntelligenceSums()
        sums.output = 100
        sums.gradedOutput = 100 * index
        sums.thinking = thinking
        sums.thinkingBase = 100
        return sums
    }

    @Test func intelligenceShowsWhileWorkingAndKeepsItsIndexBetweenBursts() throws {
        var rule = OrbLineQuietRule()
        let idle = rule.apply(.zero, generating: false, at: 0)
        #expect(!idle)
        let working = rule.apply(Self.sums(index: 92.4, thinking: 41.25), generating: true, at: 1)
        #expect(working)
        // 92.4 + 20 × 41.25 / 100
        #expect(rule.reading?.content == .intelligence(index: 101, thinking: "41"))
        // A tool call: nothing in the window, the index holds while the readout waits for its quiet moment.
        rule.apply(.zero, generating: false, at: 4)
        #expect(rule.reading?.content == .intelligence(index: 101, thinking: "0"))
        #expect(rule.reading?.quietAt == 11)
        rule.apply(.zero, generating: false, at: 12)
        #expect(rule.reading.map { $0.isGone(at: 12) } == true)
    }

    @Test func aGeneratingAgentWithoutAnEstimateShowsNothing() {
        var rule = OrbLineQuietRule()
        let changed = rule.apply(.zero, generating: true, at: 0)
        #expect(!changed)
        #expect(rule.reading == nil)
    }

    @Test func switchingKindsStartsOver() {
        var rule = OrbLineQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 0)
        rule.apply(Self.sums(index: 100, thinking: 0), generating: true, at: 2)
        #expect(rule.reading?.shownAt == 2)
        rule.apply(TrafficRate(rx: 10, tx: 0), bits: false, at: 3)
        #expect(rule.reading == nil)
    }
}
