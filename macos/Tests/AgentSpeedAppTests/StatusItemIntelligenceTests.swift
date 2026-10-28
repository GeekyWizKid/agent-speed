import AgentSpeedCore
import AppKit
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 10, Menu bar and taskbar, and DESIGN 12: the intelligence block's fixed cells, the task score's forms (live,
/// resting pale, none `–`), the proof mark under an unproven score, the score words, the quiet state, the thinking
/// hold, and the fourth `settings.bar.traffic` choice.
@Suite("Menu bar intelligence block")
@MainActor
struct StatusItemIntelligenceTests {
    /// The sums of all enabled sources: `score` in `form` from agent `a` (nil: none), `output` tok/s generated in the
    /// window (`Σ w`), `thinking` of it reasoning.
    static func sums(score: Double?, form: TaskValueForm = .live, thinking: Double, output: Double? = nil) -> IntelligenceSums {
        var sums = IntelligenceSums()
        sums.output = output ?? max(thinking * 2, 40)
        sums.thinking = thinking
        sums.thinkingBase = sums.output
        if let score {
            sums.score = TaskSetValue(form: form, score: score, shown: TaskScoreText.rounded(score), agentId: "a",
                                      lastLineMs: nil)
        }
        return sums
    }

    /// An agent whose current task has `score` in `form` and `state`.
    static func agent(_ id: String, _ form: TaskValueForm, _ score: Double?, _ state: TaskState?, project: String = "nova",
                      lastLineMs: Int64? = nil) -> AgentStatus {
        var result = TaskResult(TaskEvaluation([TaskEvent](), now: nil))
        result.state = state ?? .none
        result.score = score
        result.shown = score.map(TaskScoreText.rounded)
        var intelligence = IntelligenceSums()
        let value = TaskAgentValue(form: score == nil ? .none : form, score: score, shown: result.shown, state: result.state)
        intelligence.score = TaskSetValue(id: id, lastLineMs: lastLineMs, value: value)
        return AgentStatus(id: id, source: .codex, project: project, model: "gpt-5.5", phase: form == .live ? .generating : .idle,
                           isSubagent: false, live: Rate(), recentSpeed: nil, lastActivity: Date(), intelligence: intelligence,
                           task: AgentTaskStatus(taskStartMs: nil, result: result, predictions: nil, models: .prior))
    }

    /// The block over `agents`, with their highest score and `thinking` tok/s (`output` generated).
    static func block(_ agents: [AgentStatus], thinking: Double, output: Double? = nil, language: LanguagePreference = .zhHant,
                      thinkingHidden: Bool = false) -> StatusItemIntelligence {
        var sums = Self.sums(score: nil, thinking: thinking, output: output)
        sums.score = agents.reduce(TaskSetValue.empty) { $0.merged(with: $1.intelligence.score) }
        return StatusItemIntelligence(sums, agents: agents, l10n: Localizer(preference: language), thinkingHidden: thinkingHidden)
    }

    // MARK: Cells

    @Test func theBlockKeepsOneWidthPerLanguageWhateverTheNumbersAndForms() {
        // SF Pro 9 pt with every cell rounded up: 73.5 pt in Chinese and Japanese, 68.5 pt in English, plus the gap.
        let expected: [LanguagePreference: CGFloat] = [.zhHant: 73.5, .zhHans: 73.5, .ja: 73.5, .en: 68.5]
        let readings: [(Double?, TaskValueForm, Double)] = [
            (nil, .live, 0), (0, .live, 0), (9, .live, 4.2), (100, .live, 1_280), (100, .live, 12_800), (86, .live, 9.8),
            (72, .resting, 0), (61, .resting, 412),
        ]
        for (language, width) in expected {
            let l10n = Localizer(preference: language)
            for (score, form, thinking) in readings {
                let block = StatusItemIntelligence(Self.sums(score: score, form: form, thinking: thinking), l10n: l10n)
                #expect(block.width == StatusItemIntelligence.gap + width, "\(language) \(String(describing: score)) \(thinking)")
            }
        }
    }

    @Test func numbersShareOneCellThatOnlyAMillionWidens() {
        let l10n = Localizer(preference: .zhHant)
        let base = StatusItemIntelligence(.zero, l10n: l10n).cell
        for text in ["–", "0", "9", "86", "100", "888", "4.2", "128", "1.28k", "12.8k", "8.88k"] {
            #expect(base >= Self.textWidth(text), "\(text)")
        }
        let million = StatusItemIntelligence(Self.sums(score: 100, thinking: 1_200_000), l10n: l10n)
        #expect(million.thinking.number == "1.20M")
        #expect(million.cell > base)
    }

    private static func textWidth(_ text: String) -> CGFloat {
        ceil((text as NSString).size(withAttributes: [.font: StatusItemIntelligence.font]).width)
    }

    // MARK: Forms

    @Test func aLiveScoreTakesTheNumberColorWithTheThinkingRate() {
        let block = StatusItemIntelligence(Self.sums(score: 86.4, thinking: 10), l10n: Localizer(preference: .zhHant))
        #expect(block.intelligence == .init(label: "智力", number: "86", dim: false))
        #expect(block.thinking == .init(label: "思考", number: "10", dim: false))
        #expect(block.unit == "tok/s")
        #expect(block.spoken == "智力 86，思考每秒 10 個 token")
        let english = StatusItemIntelligence(Self.sums(score: 99.5, thinking: 1_280), l10n: Localizer(preference: .en))
        #expect([english.intelligence.label, english.thinking.label] == ["IQ", "TH"])
        #expect(english.intelligence.number == "100")
        #expect(english.spoken == "intelligence 100, thinking 1.28k tokens per second")
    }

    @Test func aRestingScoreIsPaleAndSaysIdle() {
        let l10n = Localizer(preference: .zhHant)
        let resting = StatusItemIntelligence(Self.sums(score: 72, form: .resting, thinking: 0, output: 0), l10n: l10n)
        #expect(resting.intelligence == .init(label: "智力", number: "72", dim: true))
        #expect(resting.spoken == "智力 72，閒置中，思考每秒 0 個 token")
        let alone = StatusItemIntelligence(Self.sums(score: 72, form: .resting, thinking: 0, output: 0), l10n: l10n,
                                           thinkingHidden: true)
        #expect(alone.alone)
        #expect(alone.intelligence.dim)
        #expect(alone.spoken == "智力 72，閒置中")
        #expect(StatusItemIntelligence(Self.sums(score: 72, form: .resting, thinking: 0, output: 0), l10n: Localizer(preference: .en),
                                       thinkingHidden: true).spoken == "intelligence 72, idle")
    }

    @Test func noneIsADashNeverAZero() {
        let l10n = Localizer(preference: .zhHant)
        let none = StatusItemIntelligence(.zero, l10n: l10n)
        #expect(none.intelligence == .init(label: "智力", number: "–", dim: true))
        #expect(none.intelligence.number.unicodeScalars.map(\.value) == [0x2013])
        #expect(none.spoken == "智力：尚無評分")
        #expect(StatusItemIntelligence(.zero, l10n: Localizer(preference: .en)).spoken == "intelligence: no score yet")
        // 0 is a real score.
        let zero = StatusItemIntelligence(Self.sums(score: 0, thinking: 4.2), l10n: l10n)
        #expect(zero.intelligence == .init(label: "智力", number: "0", dim: false))
        #expect(zero.spoken == "智力 0，思考每秒 4.2 個 token")
    }

    @Test func quietTheBlockStaysWithDashes() {
        let block = StatusItemIntelligence(.zero, l10n: Localizer(preference: .zhHant))
        #expect(block.intelligence == .init(label: "智力", number: "–", dim: true))
        #expect(block.thinking == .init(label: "思考", number: "–", dim: true))
        #expect(block.unit == "tok/s")
        #expect(!block.alone)
    }

    @Test func theThinkingNumberIsADashOnlyWhileNothingIsGenerated() {
        let l10n = Localizer(preference: .zhHant)
        // Agents read and think before any task has a score: 智力 `–`, 思考 its rate; the words are `intel.none`.
        let reading = StatusItemIntelligence(Self.sums(score: nil, thinking: 128, output: 300), l10n: l10n)
        #expect(reading.intelligence.number == "–")
        #expect(reading.thinking == .init(label: "思考", number: "128", dim: false))
        #expect(reading.spoken == "智力：尚無評分")
        // A test running under a live score generates nothing (`Σ w = 0`).
        let testing = StatusItemIntelligence(Self.sums(score: 45, thinking: 0, output: 0), l10n: l10n)
        #expect(testing.intelligence == .init(label: "智力", number: "45", dim: false))
        #expect(testing.thinking == .init(label: "思考", number: "–", dim: true))
        // Generated without reasoning: a dimmed 0.
        let zero = StatusItemIntelligence(Self.sums(score: 45, thinking: 0, output: 40), l10n: l10n)
        #expect(zero.thinking == .init(label: "思考", number: "0", dim: true))
    }

    // MARK: Proof mark and words

    @Test func theProofMarkFollowsTheSupplyingAgentsState() {
        for state in TaskState.allCases {
            for form in [TaskValueForm.live, .resting] {
                let block = Self.block([Self.agent("a", form, 54, state)], thinking: 4.2)
                #expect(block.intelligence.unproven == (state == .unverified || state == .pending), "\(state) \(form)")
                #expect(block.intelligence.dim == (form == .resting))
                #expect(!block.thinking.unproven)
            }
        }
        // A lower unverified agent does not mark a verified top; an unverified top is marked over a verified one.
        #expect(!Self.block([Self.agent("a", .live, 86, .passing), Self.agent("b", .live, 9, .unverified)], thinking: 64)
            .intelligence.unproven)
        #expect(Self.block([Self.agent("a", .live, 41, .failing), Self.agent("b", .live, 54, .pending)], thinking: 64)
            .intelligence.unproven)
        // A live score beats a higher resting one, and so does its mark.
        let mixed = Self.block([Self.agent("a", .resting, 90, .passing), Self.agent("b", .live, 12, .unverified)], thinking: 64)
        #expect(mixed.intelligence.number == "12")
        #expect(mixed.intelligence.unproven)
        #expect(!mixed.intelligence.dim)
        // None never carries the mark.
        #expect(!Self.block([Self.agent("a", .live, nil, .unverified)], thinking: 64).intelligence.unproven)
    }

    @Test func theScoreWordsWrapStateThenIdleThenTopOfN() {
        let unverified = [Self.agent("a", .live, 8.6, .unverified), Self.agent("b", .live, 4, .failing)]
        #expect(Self.block(unverified, thinking: 64).spoken == "智力 9，尚未驗證，2 個 agent 中最高，思考每秒 64 個 token")
        #expect(Self.block(unverified, thinking: 64, language: .en).spoken
            == "intelligence 9, not verified, the highest of 2 agents, thinking 64 tokens per second")
        let crew = [Self.agent("a", .live, 85.7, .passing), Self.agent("b", .live, 41, .failing),
                    Self.agent("c", .live, 61, .passing), Self.agent("d", .live, 8.6, .unverified)]
        #expect(Self.block(crew, thinking: 128).spoken == "智力 86，4 個 agent 中最高，思考每秒 128 個 token")
        // N counts the agents with a score in the shown form only.
        let resting = [Self.agent("a", .resting, 72.3, .passing), Self.agent("b", .resting, 25, .failing),
                       Self.agent("c", .live, nil, nil)]
        #expect(Self.block(resting, thinking: 0, output: 0, thinkingHidden: true).spoken == "智力 72，閒置中，2 個 agent 中最高")
        let pending = [Self.agent("a", .resting, 61, .pending)]
        #expect(Self.block(pending, thinking: 0, output: 0, thinkingHidden: true).spoken == "智力 61，待重新驗證，閒置中")
        #expect(Self.block(pending, thinking: 0, output: 0, language: .ja, thinkingHidden: true).spoken == "知能 61、再検証待ち、待機中")
    }

    @Test func theTooltipNamesTheSupplyingAgent() {
        let l10n = Localizer(preference: .zhHant)
        let agents = [Self.agent("a", .live, 61, .passing, project: "orbit"), Self.agent("b", .live, 85.7, .passing, project: "nova")]
        let score = agents.reduce(TaskSetValue.empty) { $0.merged(with: $1.intelligence.score) }
        #expect(StatusItemIntelligence.topAgent(score, agents: agents, l10n: l10n) == "智力 86 · 最高：nova")
        #expect(StatusItemIntelligence.topAgent(score, agents: agents, l10n: Localizer(preference: .en)) == "Intelligence 86 · top: nova")
        // Without a project the source names it; nothing while none.
        let unnamed = [Self.agent("a", .resting, 40, .failing, project: "")]
        #expect(StatusItemIntelligence.topAgent(unnamed[0].intelligence.score, agents: unnamed, l10n: l10n) == "智力 40 · 最高：Codex")
        #expect(StatusItemIntelligence.topAgent(.empty, agents: agents, l10n: l10n) == nil)
    }

    // MARK: Drawing

    /// The block drawn on a dark bar into a 2× bitmap, as `StatusItemArtist` draws the item.
    private static func pixels(_ block: StatusItemIntelligence, height: CGFloat = 22, increasedContrast: Bool = false) -> [UInt8] {
        let scale: CGFloat = 2
        let width = Int(ceil(block.width * scale)), rows = Int(height * scale)
        var bytes = [UInt8](repeating: 0, count: width * rows * 4)
        bytes.withUnsafeMutableBytes { buffer in
            guard let context = CGContext(data: buffer.baseAddress, width: width, height: rows, bitsPerComponent: 8,
                                          bytesPerRow: width * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return }
            context.translateBy(x: 0, y: CGFloat(rows))
            context.scaleBy(x: scale, y: -scale)
            NSGraphicsContext.saveGraphicsState()
            NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
            block.draw(at: 0, height: height, palette: StatusItemPalette(isDark: true, increasedContrast: increasedContrast))
            NSGraphicsContext.restoreGraphicsState()
        }
        return bytes
    }

    /// The pixel rows and columns where two drawings of the same block differ.
    private static func difference(_ a: [UInt8], _ b: [UInt8], width: Int) -> (rows: Set<Int>, columns: ClosedRange<Int>?) {
        var rows = Set<Int>(), columns: [Int] = []
        for index in stride(from: 0, to: a.count, by: 4) where a[index..<index + 4] != b[index..<index + 4] {
            rows.insert(index / 4 / width)
            columns.append(index / 4 % width)
        }
        return (rows, columns.min().flatMap { min in columns.max().map { min...$0 } })
    }

    @Test func theProofMarkIsDottedUnderTheNumberAndMovesNothingElse() throws {
        for (sums, thinkingHidden) in [(Self.sums(score: 54, thinking: 4.2), false), (Self.sums(score: 100, thinking: 1_280), false),
                                       (Self.sums(score: 9, thinking: 0), true)] {
            for increasedContrast in [false, true] {
                let l10n = Localizer(preference: .zhHant)
                let plain = StatusItemIntelligence(sums, l10n: l10n, thinkingHidden: thinkingHidden)
                var marked = plain
                marked.intelligence.unproven = true
                #expect(marked.width == plain.width)
                let width = Int(ceil(plain.width * 2))
                let changed = Self.difference(Self.pixels(plain, increasedContrast: increasedContrast),
                                              Self.pixels(marked, increasedContrast: increasedContrast), width: width)
                // 1 pt dots (1.5 pt with Increase Contrast) on whole device pixels, from 1 pt under line 1's baseline:
                // 0.17 × 9 pt = 1.53 pt to their centre.
                let baseline = Int(plain.baselines(height: 22).intelligence * 2)
                let expectedRows = Set(baseline + 2..<baseline + (increasedContrast ? 5 : 4))
                #expect(changed.rows == expectedRows, "\(plain.intelligence.number) \(increasedContrast)")
                // Within the number's advance, which ends on the cell's right edge.
                let columns = try #require(changed.columns)
                let numberWidth = (plain.intelligence.number as NSString).size(withAttributes: [.font: StatusItemIntelligence.font]).width
                let unitStart = Int((plain.width - Self.textWidth(plain.unit) - StatusItemIntelligence.unitGap) * 2)
                #expect(columns.upperBound < unitStart)
                #expect(Double(columns.upperBound - columns.lowerBound + 1) <= Double(numberWidth * 2) + 1)
                #expect(Double(columns.upperBound - columns.lowerBound + 1) >= Double(numberWidth * 2) - 2)
            }
        }
    }

    @Test func theDotsAreRoundEvenAndInTheNumbersColorAt70Percent() {
        let l10n = Localizer(preference: .zhHant)
        for scale in [1, 2, 3] as [CGFloat] {
            #expect(ProofMark.dotDiameter(textSize: StatusItemIntelligence.size, scale: scale, increasedContrast: false) == 1)
        }
        #expect(ProofMark.dotDiameter(textSize: StatusItemIntelligence.size, scale: 2, increasedContrast: true) == 1.5)
        var block = StatusItemIntelligence(Self.sums(score: 54, thinking: 4.2), l10n: l10n)
        block.intelligence.unproven = true
        let bytes = Self.pixels(block)
        let width = Int(ceil(block.width * 2))
        let row = Int(block.baselines(height: 22).intelligence * 2) + 2
        // Each dot covers two whole columns in both of its rows; the strongest pixel is the number's color (white at
        // 85 %) at 70 %, less the dot's anti-aliased corners. The label's ink also reaches below the baseline.
        let numberRight = Int((block.width - Self.textWidth(block.unit) - StatusItemIntelligence.unitGap) * 2)
        let cell = (numberRight - Int(block.cell * 2))..<numberRight
        let alphas = cell.map { bytes[(row * width + $0) * 4 + 3] }
        let lit = alphas.enumerated().filter { $0.element > 0 }.map(\.offset)
        let runs = lit.reduce(into: [[Int]]()) { runs, column in
            if let last = runs.last?.last, last == column - 1 { runs[runs.count - 1].append(column) } else { runs.append([column]) }
        }
        #expect(runs.count == ProofMark.dotCount(width: (block.intelligence.number as NSString)
            .size(withAttributes: [.font: StatusItemIntelligence.font]).width, diameter: 1))
        #expect(runs.allSatisfy { $0.count == 2 })
        let gaps = zip(runs, runs.dropFirst()).map { $1[0] - $0[0] }
        #expect((gaps.max() ?? 0) - (gaps.min() ?? 0) <= 1)
        let strongest = Double(alphas.max() ?? 0) / 255
        #expect(strongest <= 0.85 * ProofMark.opacity + 0.01)
        #expect(strongest >= 0.85 * ProofMark.opacity * 0.7)
        #expect(cell.map { bytes[((row + 1) * width + $0) * 4 + 3] } == alphas)
    }

    // MARK: Thinking hidden at zero

    @Test func afterTheHoldLineOneStandsAloneCentredInTheSameCells() {
        for language in [LanguagePreference.zhHant, .en, .ja] {
            let l10n = Localizer(preference: language)
            for sums in [Self.sums(score: 86, thinking: 0), Self.sums(score: 72, form: .resting, thinking: 0, output: 0)] {
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
        }
        #expect(StatusItemIntelligence(Self.sums(score: 86, thinking: 0), l10n: Localizer(preference: .zhHant), thinkingHidden: true)
            .spoken == "智力 86")
        #expect(StatusItemIntelligence(Self.sums(score: 86, thinking: 0), l10n: Localizer(preference: .en), thinkingHidden: true)
            .spoken == "intelligence 86")
    }

    @Test func onlyAZeroUnderAScoreGoes() {
        let l10n = Localizer(preference: .zhHant)
        // A rate above 0 draws line 2 at once, even before the hold catches up.
        let busy = StatusItemIntelligence(Self.sums(score: 86, thinking: 10), l10n: l10n, thinkingHidden: true)
        #expect(!busy.alone)
        #expect(busy.spoken == "智力 86，思考每秒 10 個 token")
        // Without a score both lines stay.
        let quiet = StatusItemIntelligence(.zero, l10n: l10n, thinkingHidden: true)
        #expect(!quiet.alone)
        #expect(quiet.spoken == "智力：尚無評分")
        #expect(!StatusItemIntelligence(Self.sums(score: nil, thinking: 0, output: 40), l10n: l10n, thinkingHidden: true).alone)
    }

    @Test func theHoldHidesAfterThreeSecondsAndReturnsAtOnce() {
        let hold = StatusItemThinkingHold()
        let zero = Self.sums(score: 86, thinking: 0)
        hold.update(zero, at: 10)
        #expect(!hold.hidden)
        hold.update(zero, at: 12.9)
        #expect(!hold.hidden)
        hold.update(zero, at: 13)
        #expect(hold.hidden)
        hold.update(Self.sums(score: 86, thinking: 4.2), at: 14)
        #expect(!hold.hidden)
        // A pause shorter than the hold never hides it.
        hold.update(zero, at: 15)
        hold.update(Self.sums(score: 86, thinking: 3), at: 17.5)
        hold.update(zero, at: 18)
        hold.update(zero, at: 20.9)
        #expect(!hold.hidden)
        // A resting score after work goes the same way.
        let resting = Self.sums(score: 72, form: .resting, thinking: 0, output: 0)
        hold.update(resting, at: 21)
        hold.update(resting, at: 24)
        #expect(hold.hidden)
        // No score: nothing is held.
        hold.update(.zero, at: 30)
        hold.update(.zero, at: 40)
        #expect(!hold.hidden)
        #expect(StatusItemThinkingHold.readsZero(Self.sums(score: 86, thinking: 0.04)))
        #expect(!StatusItemThinkingHold.readsZero(Self.sums(score: 86, thinking: 0.05)))
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
