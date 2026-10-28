import Foundation

/// Hermes's provider-confirmed per-call usage log, shared by its desktop app, CLI and gateway.
/// Timestamps are Python logging's local wall clock; latency is rounded to tenths of a second.
final class HermesLine {
    private let formatter: DateFormatter
    private static let expression = try! NSRegularExpression(pattern:
        #"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) INFO(?:[ \t]*\[([^\]\r\n]+)\])?[ \t]+(?:run_agent|agent\.conversation_loop|agent\.turn_usage): API call #(\d+): model=(\S+) provider=(\S+) in=(\d+) out=(\d+) total=\d+ latency=(\d+(?:\.\d+)?)s(?: (.*))?$"#)
    private static let cacheExpression = try! NSRegularExpression(pattern: #"(?:^| )cache=(\d+)/\d+ \([^)]*\)(?: |$)"#)
    private static let writeExpression = try! NSRegularExpression(pattern: #"(?:^| )write=(\d+)(?: |$)"#)

    init(timeZone: TimeZone = .current) {
        formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd HH:mm:ss,SSS"
        formatter.isLenient = false
    }

    func record(_ bytes: UnsafeRawBufferPointer, logDirectory: String) -> ResponseRecord? {
        guard bytes.count <= 64 * 1024 else { return nil }
        let text = String(decoding: bytes, as: UTF8.self)
        guard let match = Self.expression.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) else { return nil }
        func field(_ index: Int) -> String? {
            guard let range = Range(match.range(at: index), in: text) else { return nil }
            return String(text[range])
        }
        guard let timestamp = field(1), let date = formatter.date(from: timestamp), formatter.string(from: date) == timestamp,
              let call = field(3), let model = field(4), let provider = field(5),
              let inputText = field(6), let outputText = field(7),
              let durationText = field(8), let seconds = Double(durationText), seconds.isFinite else { return nil }
        let end = Clock.milliseconds(date)
        guard DayLabeler.hasDay(end) else { return nil }
        let milliseconds = seconds * 1000
        let start = milliseconds > 0 && milliseconds <= Double(ResponseTiming.maxIntervalMs) ? end - Int64(milliseconds.rounded()) : nil
        func count(_ digits: String) -> Int {
            TokenCount.clamp(Int64(digits) ?? TokenCount.ceiling)
        }
        let suffix = field(9) ?? ""
        func suffixCount(_ expression: NSRegularExpression) -> Int {
            guard let match = expression.firstMatch(in: suffix, range: NSRange(suffix.startIndex..., in: suffix)),
                  let range = Range(match.range(at: 1), in: suffix) else { return 0 }
            return count(String(suffix[range]))
        }
        let normalizedInput = count(inputText)
        let session = field(2) ?? logDirectory
        return ResponseRecord(key: "hermes:\(session):\(end):\(call)", source: .hermes,
                              agentId: logDirectory + "#" + (field(2) ?? "untagged"), model: model == "unknown" ? nil : model,
                              startMs: start, endMs: end,
                              usage: TokenUsage(input: normalizedInput,
                                                cachedInput: min(normalizedInput, suffixCount(Self.cacheExpression)),
                                                cacheWriteInput: min(normalizedInput, suffixCount(Self.writeExpression)),
                                                output: count(outputText)),
                              provider: provider == "unknown" ? nil : provider)
    }
}
