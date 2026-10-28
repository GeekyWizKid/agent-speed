import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 10, Menu bar and taskbar: the intelligence block's fixed cells, its quiet state and words, and the fourth
/// `settings.bar.traffic` choice.
@Suite("Menu bar intelligence block")
@MainActor
struct StatusItemIntelligenceTests {
    /// `index` is the shown value (a live task score; values above 100 only exercise the three-digit cell).
    static func sums(index: Double?, thinking: Double) -> IntelligenceSums {
        var sums = IntelligenceSums()
        guard let index else { return sums }
        let output = max(thinking * 2, 40)
        sums.output = output
        sums.score = TaskSetValue(form: .live, score: index, shown: Int(index.rounded()), agentId: "a", lastLineMs: nil)
        sums.thinking = thinking
        sums.thinkingBase = output
        return sums
    }

    @Test func theBlockKeepsOneWidthPerLanguageWhateverTheNumbers() {
        // SF Pro 9 pt with every cell rounded up: 73.5 pt in Chinese and Japanese, 68.5 pt in English, plus the gap.
        let expected: [LanguagePreference: CGFloat] = [.zhHant: 73.5, .zhHans: 73.5, .ja: 73.5, .en: 68.5]
        let readings: [(Double?, Double)] = [(nil, 0), (60, 0), (136, 4.2), (128, 1_280), (125, 12_800), (88, 9.8), (133, 412)]
        for (language, width) in expected {
            let l10n = Localizer(preference: language)
            for (index, thinking) in readings {
                let block = StatusItemIntelligence(Self.sums(index: index, thinking: thinking), l10n: l10n)
                #expect(block.width == StatusItemIntelligence.gap + width, "\(language) \(String(describing: index)) \(thinking)")
            }
        }
    }

    @Test func numbersShareOneCellThatOnlyAMillionWidens() {
        let l10n = Localizer(preference: .zhHant)
        let base = StatusItemIntelligence(.zero, l10n: l10n).cell
        for text in ["–", "60", "136", "0", "4.2", "128", "1.28k", "12.8k", "8.88k"] {
            #expect(StatusItemIntelligence(.zero, l10n: l10n).cell >= Self.textWidth(text), "\(text)")
        }
        let million = StatusItemIntelligence(Self.sums(index: 136, thinking: 1_200_000), l10n: l10n)
        #expect(million.thinking.number == "1.20M")
        #expect(million.cell > base)
    }

    private static func textWidth(_ text: String) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: StatusItemIntelligence.font]).width)
    }

    @Test func liveLinesCarryTheIndexAndTheThinkingRate() {
        let l10n = Localizer(preference: .zhHant)
        let block = StatusItemIntelligence(Self.sums(index: 125, thinking: 10), l10n: l10n)
        #expect(block.intelligence == .init(label: "智力", number: "125", dim: false))
        #expect(block.thinking == .init(label: "思考", number: "10", dim: false))
        #expect(block.unit == "tok/s")
        #expect(block.spoken == "智力 125，思考每秒 10 個 token")
        let english = StatusItemIntelligence(Self.sums(index: 133, thinking: 1_280), l10n: Localizer(preference: .en))
        #expect([english.intelligence.label, english.thinking.label] == ["IQ", "TH"])
        #expect(english.spoken == "intelligence 133, thinking 1.28k tokens per second")
    }

    @Test func aZeroThinkingRateUnderALiveValueIsDimmed() {
        let block = StatusItemIntelligence(Self.sums(index: 125, thinking: 0), l10n: Localizer(preference: .zhHant))
        #expect(block.intelligence.dim == false)
        #expect(block.thinking == .init(label: "思考", number: "0", dim: true))
    }

    @Test func quietTheBlockStaysWithDashes() {
        let l10n = Localizer(preference: .zhHant)
        let block = StatusItemIntelligence(.zero, l10n: l10n)
        #expect(block.intelligence == .init(label: "智力", number: "–", dim: true))
        #expect(block.thinking == .init(label: "思考", number: "–", dim: true))
        #expect(block.unit == "tok/s")
        #expect(block.spoken == "智力：目前沒有輸出")
        #expect(StatusItemIntelligence(.zero, l10n: Localizer(preference: .en)).spoken == "intelligence: no output right now")
    }

    // MARK: Thinking hidden at zero

    @Test func afterTheHoldLineOneStandsAloneCentredInTheSameCells() {
        for language in [LanguagePreference.zhHant, .en, .ja] {
            let l10n = Localizer(preference: language)
            let sums = Self.sums(index: 125, thinking: 0)
            let pair = StatusItemIntelligence(sums, l10n: l10n)
            let alone = StatusItemIntelligence(sums, l10n: l10n, thinkingHidden: true)
            #expect(!pair.alone)
            #expect(alone.alone)
            #expect(alone.width == pair.width)
            #expect(alone.cell == pair.cell)
            for height in [22, 24, 37] as [CGFloat] {
                let lines = pair.baselines(height: height)
                #expect(alone.baselines(height: height).intelligence == (lines.intelligence + lines.thinking) / 2)
                #expect(alone.baselines(height: height).intelligence == lines.intelligence + StatusItemIntelligence.pitch / 2)
            }
        }
        #expect(StatusItemIntelligence(Self.sums(index: 125, thinking: 0), l10n: Localizer(preference: .zhHant), thinkingHidden: true)
            .spoken == "智力 125")
        #expect(StatusItemIntelligence(Self.sums(index: 125, thinking: 0), l10n: Localizer(preference: .en), thinkingHidden: true)
            .spoken == "intelligence 125")
    }

    @Test func onlyAZeroUnderALiveValueGoes() {
        let l10n = Localizer(preference: .zhHant)
        // A rate above 0 draws line 2 at once, even before the hold catches up.
        let busy = StatusItemIntelligence(Self.sums(index: 125, thinking: 10), l10n: l10n, thinkingHidden: true)
        #expect(!busy.alone)
        #expect(busy.spoken == "智力 125，思考每秒 10 個 token")
        // Quiet: both dashes stay.
        let quiet = StatusItemIntelligence(.zero, l10n: l10n, thinkingHidden: true)
        #expect(!quiet.alone)
        #expect(quiet.spoken == "智力：目前沒有輸出")
    }

    @Test func theHoldHidesAfterThreeSecondsAndReturnsAtOnce() {
        let hold = StatusItemThinkingHold()
        let zero = Self.sums(index: 125, thinking: 0)
        hold.update(zero, at: 10)
        #expect(!hold.hidden)
        hold.update(zero, at: 12.9)
        #expect(!hold.hidden)
        hold.update(zero, at: 13)
        #expect(hold.hidden)
        hold.update(Self.sums(index: 125, thinking: 4.2), at: 14)
        #expect(!hold.hidden)
        // A pause shorter than the hold never hides it.
        hold.update(zero, at: 15)
        hold.update(Self.sums(index: 125, thinking: 3), at: 17.5)
        hold.update(zero, at: 18)
        hold.update(zero, at: 20.9)
        #expect(!hold.hidden)
        // No value: nothing is held.
        hold.update(.zero, at: 30)
        hold.update(.zero, at: 40)
        #expect(!hold.hidden)
        #expect(StatusItemThinkingHold.readsZero(Self.sums(index: 125, thinking: 0.04)))
        #expect(!StatusItemThinkingHold.readsZero(Self.sums(index: 125, thinking: 0.05)))
        #expect(!StatusItemThinkingHold.readsZero(.zero))
    }

    // MARK: Setting

    @Test func storedChoicesReadBackAndUnknownReadsAsOff() throws {
        let suite = "StatusItemIntelligenceTests.\(UUID().uuidString)"
        let defaults = try #require(UserDefaults(suiteName: suite))
        defer { defaults.removePersistentDomain(forName: suite) }
        #expect(AppSettings(defaults: defaults).barTraffic == .off)
        for (stored, choice) in [("off", BarTraffic.off), ("machine", .machine), ("agents", .agents), ("intelligence", .intelligence),
                                 ("sparkles", .off), ("", .off)] {
            defaults.set(stored, forKey: "barTraffic")
            #expect(AppSettings(defaults: defaults).barTraffic == choice, "\(stored)")
        }
        let settings = AppSettings(defaults: defaults)
        settings.barTraffic = .intelligence
        #expect(defaults.string(forKey: "barTraffic") == "intelligence")
        #expect(AppSettings(defaults: defaults).barTraffic == .intelligence)
    }

    @Test func onlyTheNetworkChoicesNeedMeasuring() {
        #expect(BarTraffic.allCases == [.off, .machine, .agents, .intelligence])
        #expect(BarTraffic.allCases.filter(\.isNetwork) == [.machine, .agents])
        let l10n = Localizer(preference: .zhHant)
        #expect(BarTraffic.allCases.map { l10n($0.titleKey) } == ["不顯示", "網路速度：整台電腦", "網路速度：只看 agent", "即時智力"])
    }
}
