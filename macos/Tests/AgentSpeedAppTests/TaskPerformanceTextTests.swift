import AgentSpeedCore
import CoreGraphics
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 12, *Usage History: task performance*: the mode and the pair, the shown values, the scales, the better
/// rule, the words and the by-model table.
@Suite("Task performance text")
@MainActor
struct TaskPerformanceTextTests {
    let zh = Localizer(preference: .zhHant)
    let en = Localizer(preference: .en)

    static func stats(tasks: Int = 100, scored: Int = 80, median: Double? = 70, q1: Double? = 55, q3: Double? = 82,
                      success: Double? = 0.7, stall: Double? = 0.1, fixFail: Double? = 0.25, attempts: Double? = 1.5,
                      firstPassMs: Double? = 240_000, successes: Int? = nil, stalled: Int? = nil,
                      fixFailures: Int? = nil, fixAttempts: Int? = nil) -> TaskPerformanceStats {
        TaskPerformanceStats(tasks: tasks, scoredTasks: scored, medianScore: median, lowerQuartile: q1, upperQuartile: q3,
                             histogram: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], successRate: success, stallRate: stall,
                             fixFailureRate: fixFail, recoverAttempts: attempts, timeToFirstPassMs: firstPassMs,
                             successes: successes, stalled: stalled, fixFailures: fixFailures, fixAttempts: fixAttempts)
    }

    static func input(_ tasks: [SourceKind: Int], narrowedTo: SourceKind? = nil, analyzing: Bool = false) -> TaskPerformanceInput {
        let bySource = SourceKind.allCases.compactMap { source in
            tasks[source].map { TaskPerformanceSource(source: source, stats: stats(tasks: $0, scored: $0)) }
        }
        return TaskPerformanceInput(comparison: TaskPerformanceComparison(bySource: bySource, byModel: []),
                                    narrowedTo: narrowedTo, analyzing: analyzing)
    }

    static func row(_ model: String, tasks: Int, success: Double? = 0.5, sources: [SourceKind] = [.codex]) -> TaskPerformanceModelRow {
        TaskPerformanceModelRow(model: model, sources: sources, stats: stats(tasks: tasks, scored: tasks, success: success))
    }

    // MARK: Mode and pair

    @Test func modeFollowsRecordsNarrowingAndAnalysis() {
        let mode = { (input: TaskPerformanceInput) in TaskPerformanceResolution(input: input, remembered: nil).mode }
        #expect(mode(Self.input([:], analyzing: true)) == .analyzing)
        #expect(mode(Self.input([:])) == .empty)
        // Records already in: the analysis no longer shows placeholders.
        #expect(mode(Self.input([.codex: 4, .claudeCode: 9], analyzing: true))
            == .pair(TaskPerformancePair(first: .codex, second: .claudeCode)))
        // A source without records counts as none.
        #expect(mode(Self.input([.pi: 0])) == .empty)
        #expect(mode(Self.input([.pi: 3])) == .single(.pi, onlyOne: true))
        #expect(mode(Self.input([.codex: 3], narrowedTo: .codex)) == .single(.codex, onlyOne: false))
        #expect(mode(Self.input([:], narrowedTo: .gemini)) == .empty)
        #expect(mode(Self.input([:], narrowedTo: .codex, analyzing: true)) == .analyzing)
    }

    @Test func defaultPairPrefersCodexAndClaudeCode() {
        let resolution = TaskPerformanceResolution(input: Self.input([.pi: 900, .codex: 2, .claudeCode: 1]), remembered: nil)
        #expect(resolution.mode == .pair(TaskPerformancePair(first: .codex, second: .claudeCode)))
    }

    @Test func defaultPairIsTheTwoWithMostTasksInSourceOrder() {
        let mode = { (tasks: [SourceKind: Int]) in TaskPerformanceResolution(input: Self.input(tasks), remembered: nil).mode }
        // Pi has the most, Claude Desktop the second most: shown in source order.
        #expect(mode([.pi: 50, .claudeDesktop: 20, .omp: 10]) == .pair(TaskPerformancePair(first: .claudeDesktop, second: .pi)))
        // Ties go by source order.
        #expect(mode([.pi: 10, .omp: 10, .claudeDesktop: 10]) == .pair(TaskPerformancePair(first: .claudeDesktop, second: .omp)))
        #expect(mode([.codex: 5, .pi: 9, .omp: 9]) == .pair(TaskPerformancePair(first: .omp, second: .pi)))
    }

    @Test func rememberedPairStaysWhileOneSideHasRecords() {
        let input = Self.input([.codex: 30, .claudeCode: 40])
        let remembered = TaskPerformancePair(first: .pi, second: .codex)
        #expect(TaskPerformanceResolution(input: input, remembered: remembered).mode == .pair(remembered))
        let gone = TaskPerformancePair(first: .pi, second: .omp)
        #expect(TaskPerformanceResolution(input: input, remembered: gone).mode
            == .pair(TaskPerformancePair(first: .codex, second: .claudeCode)))
        let broken = TaskPerformancePair(first: .codex, second: .codex)
        #expect(TaskPerformanceResolution(input: input, remembered: broken).mode
            == .pair(TaskPerformancePair(first: .codex, second: .claudeCode)))
        // Only one source with records: `onlyOne` replaces the pickers, whatever is remembered.
        #expect(TaskPerformanceResolution(input: Self.input([.codex: 3]), remembered: remembered).mode == .single(.codex, onlyOne: true))
    }

    @Test func pickersLeaveOutTheOtherChoice() {
        let resolution = TaskPerformanceResolution(input: Self.input([.pi: 5, .codex: 3, .claudeCode: 4, .omp: 1]), remembered: nil)
        let pair = TaskPerformancePair(first: .codex, second: .claudeCode)
        #expect(resolution.choices(for: .first, in: pair) == [.codex, .omp, .pi])
        #expect(resolution.choices(for: .second, in: pair) == [.claudeCode, .omp, .pi])
        // A side without records keeps its own choice, so the pop-up can show it.
        let empty = TaskPerformancePair(first: .claudeDesktop, second: .codex)
        #expect(resolution.choices(for: .first, in: empty) == [.claudeCode, .claudeDesktop, .omp, .pi])
        #expect(TaskPerformanceResolution.picking(.pi, for: .second, in: pair) == TaskPerformancePair(first: .codex, second: .pi))
        #expect(TaskPerformanceResolution.picking(.omp, for: .first, in: pair) == TaskPerformancePair(first: .omp, second: .claudeCode))
    }

    @Test func widthClasses() {
        #expect(TaskPerformanceWidth(width: 872) == .wide)
        #expect(TaskPerformanceWidth(width: 860) == .wide)
        #expect(TaskPerformanceWidth(width: 859.5) == .medium)
        #expect(TaskPerformanceWidth(width: 672) == .medium)
        #expect(TaskPerformanceWidth(width: 600) == .medium)
        #expect(TaskPerformanceWidth(width: 599) == .narrow)
        #expect(TaskPerformanceWidth.wide.centre == 140)
        #expect(TaskPerformanceWidth.medium.centre == 120)
        #expect(TaskPerformanceWidth.medium.distribution == 96)
    }

    // MARK: Values

    @Test func ratesRoundHalfAwayFromZero() {
        #expect(TaskPerformanceText.rate(0.145) == "15%")
        #expect(TaskPerformanceText.rate(0.785) == "79%")
        #expect(TaskPerformanceText.rate(0.7849) == "78%")
        #expect(TaskPerformanceText.rate(0.0049) == "0%")
        #expect(TaskPerformanceText.rate(1) == "100%")
    }

    @Test func attemptsShowOneDecimal() {
        #expect(TaskPerformanceText.attempts(1.45, zh) == "1.5 次")
        #expect(TaskPerformanceText.attempts(2, zh) == "2.0 次")
        #expect(TaskPerformanceText.attempts(1.96774, en) == "2.0")
        #expect(TaskPerformanceText.attempts(1.04, en) == "1.0")
    }

    @Test func timesUseSecondsMinutesAndHours() {
        #expect(TaskPerformanceText.duration(ms: 0, zh) == "0 秒")
        #expect(TaskPerformanceText.duration(ms: 59_400, zh) == "59 秒")
        #expect(TaskPerformanceText.duration(ms: 59_500, zh) == "1 分鐘")
        #expect(TaskPerformanceText.duration(ms: 59_600, zh) == "1 分鐘")
        #expect(TaskPerformanceText.duration(ms: 89_900, zh) == "1 分鐘")
        #expect(TaskPerformanceText.duration(ms: 90_000, zh) == "2 分鐘")
        #expect(TaskPerformanceText.duration(ms: 3_569_000, zh) == "59 分鐘")
        #expect(TaskPerformanceText.duration(ms: 3_570_000, zh) == "1 小時")
        #expect(TaskPerformanceText.duration(ms: 5_400_000, zh) == "1 小時 30 分鐘")
        #expect(TaskPerformanceText.duration(ms: 5_400_000, en) == "1 hr 30 min")
        #expect(TaskPerformanceText.duration(ms: 42_000, en) == "42 sec")
    }

    @Test func valuesAreAbsentWithoutRecordsOrData() {
        let stats = Self.stats(attempts: nil)
        #expect(TaskPerformanceText.value(.attempts, stats, zh) == nil)
        #expect(TaskPerformanceText.shown(.attempts, stats, zh) == "–")
        #expect(TaskPerformanceText.value(.success, nil, zh) == nil)
        #expect(TaskPerformanceText.value(.success, Self.stats(tasks: 0, scored: 0), zh) == nil)
        #expect(TaskPerformanceText.shown(.firstPass, stats, zh) == "4 分鐘")
        #expect(TaskPerformanceText.median(Self.stats(median: 70.5)) == "71")
        #expect(TaskPerformanceText.median(Self.stats(median: 70.4999)) == "70")
        #expect(TaskPerformanceText.median(Self.stats(scored: 0, median: nil)) == "–")
    }

    @Test func scales() {
        #expect(TaskPerformanceText.scale(.success, 0.2, 0.3) == 1)
        #expect(TaskPerformanceText.scale(.attempts, 2, 1.4) == 3)
        #expect(TaskPerformanceText.scale(.attempts, 4, 2) == 5)
        #expect(TaskPerformanceText.scale(.attempts, nil, nil) == 3)
        #expect(TaskPerformanceText.scale(.firstPass, 180_000, 300_000) == 375_000)
        #expect(TaskPerformanceText.scale(.firstPass, 60_000) == 300_000)
        #expect(TaskPerformanceText.fraction(0.78, scale: 1) == 0.78)
        #expect(TaskPerformanceText.fraction(2, scale: 3) == 2.0 / 3)
        #expect(TaskPerformanceText.fraction(nil, scale: 3) == 0)
        #expect(TaskPerformanceText.fraction(9, scale: 3) == 1)
    }

    // MARK: Better

    @Test func betterNeedsTenScoredTasksOnBothSides() {
        let a = Self.stats(scored: 10, success: 0.9)
        #expect(TaskPerformanceText.better(.success, a, Self.stats(scored: 10, success: 0.5), zh) == .first)
        #expect(TaskPerformanceText.better(.success, a, Self.stats(scored: 9, success: 0.5), zh) == nil)
        #expect(TaskPerformanceText.better(.success, Self.stats(scored: 9, success: 0.9), Self.stats(success: 0.5), zh) == nil)
        #expect(TaskPerformanceText.better(.success, a, nil, zh) == nil)
        #expect(TaskPerformanceText.better(.attempts, Self.stats(attempts: nil), Self.stats(attempts: 2), zh) == nil)
    }

    @Test func ratesDifferByOnePercentagePoint() {
        let better = { (a: Double, b: Double, metric: TaskPerformanceMetric) -> TaskPerformanceSide? in
            let first = Self.stats(success: a, stall: a, fixFail: a)
            let second = Self.stats(success: b, stall: b, fixFail: b)
            return TaskPerformanceText.better(metric, first, second, zh)
        }
        #expect(better(0.78, 0.77, .success) == .first)
        #expect(better(0.785, 0.7751, .success) == nil)
        // Lower is better for the stall and fix-failure rates.
        #expect(better(0.10, 0.16, .stall) == .first)
        #expect(better(0.33, 0.25, .fixFail) == .second)
        #expect(better(0.25, 0.2401, .fixFail) == nil)
    }

    @Test func attemptsAndTimesDifferByFivePercentOfTheLarger() {
        let attempts = { (a: Double, b: Double) in
            TaskPerformanceText.better(.attempts, Self.stats(attempts: a), Self.stats(attempts: b), zh)
        }
        #expect(attempts(2.0, 1.9) == .second)
        #expect(attempts(2.0, 1.91) == nil)
        #expect(attempts(1.4, 2.0) == .first)
        let times = { (a: Double, b: Double) in
            TaskPerformanceText.better(.firstPass, Self.stats(firstPassMs: a), Self.stats(firstPassMs: b), zh)
        }
        #expect(times(180_000, 300_000) == .first)
        #expect(times(300_000, 314_000) == nil)
        // 5 % apart, but both read `5 分鐘`: values that look the same are never told apart.
        #expect(times(300_000, 316_000) == nil)
        #expect(times(300_000, 330_000) == .first)
    }

    // MARK: Tooltips and words

    @Test func tooltipsGiveCountsOrTheReason() {
        let counted = Self.stats(tasks: 128, scored: 96, successes: 75, stalled: 13, fixFailures: 23, fixAttempts: 93)
        #expect(TaskPerformanceText.tooltip(.success, counted, zh) == "75 / 96 個已評分的任務")
        #expect(TaskPerformanceText.tooltip(.stall, counted, zh) == "13 / 128 個任務")
        #expect(TaskPerformanceText.tooltip(.fixFail, counted, en) == "23 of 93 runs after a failure")
        #expect(TaskPerformanceText.tooltip(.success, Self.stats(), zh) == nil)
        #expect(TaskPerformanceText.tooltip(.attempts, Self.stats(attempts: nil), zh) == "這段期間沒有出錯後恢復的任務")
        #expect(TaskPerformanceText.tooltip(.firstPass, Self.stats(firstPassMs: nil), en) == "No task passed a check in this period")
        #expect(TaskPerformanceText.tooltip(.attempts, Self.stats(), zh) == nil)
        #expect(TaskPerformanceText.tooltip(.stall, nil, zh) == "這段期間沒有任務")
        #expect(TaskPerformanceText.spoken(.attempts, Self.stats(attempts: nil), zh) == "這段期間沒有出錯後恢復的任務")
        #expect(TaskPerformanceText.spoken(.success, Self.stats(), zh) == "70%")
    }

    @Test func compareWordsWrapLowerIsBetterAndTheBetterSide() {
        let pair = TaskPerformancePair(first: .codex, second: .claudeCode)
        let codex = Self.stats(stall: 0.10)
        let claude = Self.stats(stall: 0.16)
        #expect(TaskPerformanceText.compareWords(.stall, pair: pair, first: codex, second: claude, zh)
            == "卡住率，越低越好：Codex 10%，Claude Code 16%，Codex 較好")
        #expect(TaskPerformanceText.compareWords(.success, pair: pair, first: codex, second: Self.stats(scored: 6), zh)
            == "成功率：Codex 70%，Claude Code 70%")
        #expect(TaskPerformanceText.compareWords(.attempts, pair: pair, first: codex, second: nil, en)
            == "Mean attempts to recover, lower is better: Codex 1.5, Claude Code No tasks in this period")
        #expect(TaskPerformanceText.singleWords(.success, codex, zh) == "成功率：70%")
    }

    @Test func sideAndDistributionWords() {
        let stats = Self.stats(tasks: 128, scored: 96, median: 70.5, q1: 57.5, q3: 81.49)
        #expect(TaskPerformanceText.sideWords(.codex, stats, zh) == "Codex：128 個任務，96 個已評分，中位數 71")
        #expect(TaskPerformanceText.sideWords(.pi, nil, zh) == "Pi：這段期間沒有任務")
        #expect(TaskPerformanceText.distributionWords(stats, zh) == "分數分布：四分之一的任務低於 58，一半低於 71，四分之三低於 81")
        #expect(TaskPerformanceText.distributionWords(Self.stats(scored: 0, median: nil, q1: nil, q3: nil), zh) == nil)
        #expect(TaskPerformanceText.iqrWords(stats, zh) == "中間一半的任務在 58 到 81 分之間")
        #expect(TaskPerformanceText.counts(stats, zh) == "96 個已評分 · 共 128 個任務")
        #expect(TaskPerformanceText.modelSources([.codex, .pi], zh) == "來源：Codex、Pi")
        #expect(TaskPerformanceText.modelSources([.codex, .pi], en) == "Sources: Codex, Pi")
        #expect(TaskPerformanceText.onlyOne(.claudeCode, zh) == "這段期間只有 Claude Code 有任務")
    }

    @Test func lowSampleBelowTenScoredTasks() {
        #expect(TaskPerformanceText.isLowSample(Self.stats(scored: 9)))
        #expect(!TaskPerformanceText.isLowSample(Self.stats(scored: 10)))
        #expect(TaskPerformanceText.sampleHint(Self.stats(scored: 6), zh) == "只有 6 個已評分的任務，數字容易波動；雙方都有 10 個以上才會標出較好的一方。")
    }

    @Test func binsCountTheEdgesReached() {
        #expect(TaskPerformanceText.bin(of: 0) == 0)
        #expect(TaskPerformanceText.bin(of: 9.999) == 0)
        #expect(TaskPerformanceText.bin(of: 10) == 1)
        #expect(TaskPerformanceText.bin(of: 89.99) == 8)
        #expect(TaskPerformanceText.bin(of: 90) == 9)
        #expect(TaskPerformanceText.bin(of: 100) == 9)
        #expect(TaskPerformanceText.medianBin(Self.stats(median: 70)) == 7)
    }

    // MARK: By model

    @Test func modelsSortByTasksThenIdBytes() {
        let rows = [Self.row("b", tasks: 5), Self.row("a", tasks: 5), Self.row("Z", tasks: 5), Self.row("c", tasks: 9)]
        #expect(TaskModelTable.sort(rows, by: .standard).map(\.model) == ["c", "Z", "a", "b"])
        let ascending = TaskModelSort(column: .tasks, descending: false)
        #expect(TaskModelTable.sort(rows, by: ascending).map(\.model) == ["Z", "a", "b", "c"])
    }

    @Test func sortedColumnsKeepMissingValuesLast() {
        let rows = [
            Self.row("low", tasks: 3, success: 0.2), Self.row("none", tasks: 8, success: nil),
            Self.row("high", tasks: 4, success: 0.9), Self.row("", tasks: 20, success: 0.5),
        ]
        let success = TaskModelSort.standard.toggled(.success)
        #expect(success == TaskModelSort(column: .success, descending: true))
        #expect(TaskModelTable.sort(rows, by: success).map(\.model) == ["high", "", "low", "none"])
        #expect(TaskModelTable.sort(rows, by: success.toggled(.success)).map(\.model) == ["low", "", "high", "none"])
        // Model ids sort A→Z first; the unknown model has no id and comes last.
        let model = TaskModelSort.standard.toggled(.model)
        #expect(model == TaskModelSort(column: .model, descending: false))
        #expect(TaskModelTable.sort(rows, by: model).map(\.model) == ["high", "low", "none", ""])
        #expect(TaskModelTable.sort(rows, by: model.toggled(.model)).map(\.model) == ["none", "low", "high", ""])
    }

    @Test func columnsByWidth() {
        #expect(TaskModelColumn.visible(.wide) == [.model, .tasks, .scored, .median, .success, .stall, .fixFail, .attempts, .firstPass])
        #expect(TaskModelColumn.visible(.medium) == [.model, .scoredOfTasks, .median, .success, .stall, .fixFail, .attempts, .firstPass])
        #expect(TaskModelColumn.scored.headerKey == "history.tasks.metric.scored.short")
        #expect(TaskModelColumn.scored.fullKey == "history.tasks.metric.scored")
        #expect(TaskModelColumn.attempts.headerKey == "history.tasks.metric.attempts.short")
        #expect(TaskModelColumn.success.headerKey == "history.tasks.metric.success")
    }

    @Test func columnWidthsFillTheRowAndShrinkBeforeTheModelDoes() {
        for (width, available) in [(TaskPerformanceWidth.wide, CGFloat(820)), (.medium, 620), (.medium, 548)] {
            let columns = TaskModelColumn.visible(width)
            let layout = TaskModelColumnLayout.layout(columns, width: width, available: available)
            let used = layout.widths.reduce(0, +) + layout.gap * CGFloat(columns.count - 1)
            #expect(used <= available)
            #expect(available - used < 1)
            #expect(layout.widths[0] >= TaskModelColumnLayout.minModel)
        }
        let roomy = TaskModelColumnLayout.layout(TaskModelColumn.visible(.medium), width: .medium, available: 620)
        #expect(roomy.gap == 8)
        let tight = TaskModelColumnLayout.layout(TaskModelColumn.visible(.medium), width: .medium, available: 548)
        #expect(tight.gap == 6)
        #expect(tight.widths[1] < TaskModelColumnLayout.base(.scoredOfTasks, .medium))
    }

    @Test func cellsAndCopiedText() {
        var row = Self.row("gpt-6.1-sol", tasks: 74, success: 0.81)
        row.stats.scoredTasks = 58
        row.stats.recoverAttempts = nil
        #expect(TaskModelTable.cell(row, .scoredOfTasks, zh) == "58 / 74")
        #expect(TaskModelTable.cell(row, .attempts, zh) == "–")
        #expect(TaskModelTable.spoken(row, .attempts, zh) == "這段期間沒有出錯後恢復的任務")
        #expect(TaskModelTable.spoken(row, .median, zh) == "70，中間一半的任務在 55 到 82 分之間")
        #expect(TaskModelTable.cell(Self.row("", tasks: 3), .model, zh) == "無模型中繼資料")
        #expect(TaskModelTable.tooltip(row, .model, zh) == "gpt-6.1-sol\n來源：Codex")
        let lines = TaskModelTable.copyText([row], en).components(separatedBy: "\n")
        #expect(lines.count == 2)
        #expect(lines[0] == ["Model", "Tasks", "Scored tasks", "Median score", "Success rate", "Stall rate",
                             "Fix-failure rate", "Mean attempts to recover", "Median time to first pass"].joined(separator: "\t"))
        #expect(lines[1] == ["gpt-6.1-sol", "74", "58", "70", "81%", "10%", "25%", "–", "4 min"].joined(separator: "\t"))
    }

    @Test func accessibleRowsNameEveryColumnInFull() {
        var row = Self.row("claude-fable-5-1", tasks: 9, sources: [.claudeDesktop])
        row.stats.scoredTasks = 6
        #expect(TaskModelTable.accessibleLabel(row, zh) == "claude-fable-5-1，來源：Claude Desktop，樣本少")
        let value = TaskModelTable.accessibleValue(row, columns: TaskModelColumn.visible(.medium), zh)
        #expect(value.hasPrefix("已評分 / 任務：6 / 9，中位數分數：70，中間一半的任務在 55 到 82 分之間，成功率：50%"))
        #expect(value.hasSuffix("首次通過時間（中位數）：4 分鐘"))
    }

    @Test func flowPartsBreakOnlyAfterSeparators() {
        #expect(TaskFlowText.parts("96 個已評分 · 共 128 個任務") == ["96 個已評分 ·\u{00A0}", "共 128 個任務"])
        #expect(TaskFlowText.parts("採点済み 96・全 128 タスク") == ["採点済み 96・", "全 128 タスク"])
        #expect(TaskFlowText.parts("96 scored") == ["96 scored"])
    }
}
