import CryptoKit
import Foundation

/// The first 16 hex digits of SHA-256 over a tool call's input as the file stores it (METRICS 6.10).
struct ToolDigest: Hashable, Sendable {
    let value: UInt64

    init(_ text: UnsafeRawBufferPointer) {
        var hasher = SHA256()
        hasher.update(bufferPointer: text)
        value = hasher.finalize().withUnsafeBytes { $0.prefix(8).reduce(0) { $0 << 8 | UInt64($1) } }
    }

    init?(hex: String) {
        guard hex.utf8.count == 16, let value = UInt64(hex, radix: 16) else { return nil }
        self.value = value
    }

    var hex: String {
        let digits = String(value, radix: 16)
        return String(repeating: "0", count: 16 - digits.count) + digits
    }

    /// An absent or `null` input hashes as empty text.
    static let empty = ToolDigest(UnsafeRawBufferPointer(start: nil, count: 0))
}

/// What a tool call did, without its input: the tool and the digest of the input.
struct ToolCall: Hashable, Sendable {
    var tool: String
    var digest: ToolDigest
}

/// One action of an agent (METRICS 6.10). Only these fields are kept, never a tool's input or output.
struct ToolAction: Equatable, Sendable {
    var atMs: Int64
    var call: ToolCall
    /// nil while the outcome is unknown, which is not a failure.
    var failed: Bool?
}

/// What one line says about tools; a result finds its call by id.
enum ToolEvent: Equatable, Sendable {
    case call(id: String, ToolCall)
    case result(id: String, failed: Bool?)

    var isCall: Bool {
        if case .call = self { return true }
        return false
    }
}

/// The last `capacity` actions of one file.
struct ActionLog: Equatable, Sendable {
    static let capacity = 64

    private(set) var actions: [ToolAction] = []
    /// The call id of each action, in step with `actions`; a call repeated on a later line is one action.
    private var callIds: [String] = []

    mutating func record(_ events: [ToolEvent], at timestampMs: Int64) {
        for event in events {
            switch event {
            case .call(let id, let call):
                guard !callIds.contains(id) else { continue }
                actions.append(ToolAction(atMs: timestampMs, call: call))
                callIds.append(id)
                if actions.count > Self.capacity {
                    actions.removeFirst()
                    callIds.removeFirst()
                }
            case .result(let id, let failed?):
                if let index = callIds.firstIndex(of: id) { actions[index].failed = failed }
            case .result(_, nil):
                continue
            }
        }
    }
}

extension JSONScanner {
    /// The digest of a tool input: a string's text, any other value's raw JSON text; `null` hashes as absent.
    mutating func readToolDigest() throws(JSONScanError) -> ToolDigest {
        if try readNull() { return .empty }
        return try withStoredText(ToolDigest.init)
    }
}
