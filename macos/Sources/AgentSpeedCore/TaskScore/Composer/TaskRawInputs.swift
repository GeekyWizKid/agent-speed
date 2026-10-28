import Foundation

/// The 6.10 digests of tool inputs, read from the line's bytes: an object's or array's JSON text exactly as the line
/// stores it, a string's text, an absent or null input as empty text (METRICS 6.9 *Calls*).
enum TaskRawInputs {
    /// The digest of `message.content[k][field]` for every content index `k` whose block is an object (the last of
    /// repeated members, as the parsed line reads them).
    static func contentDigests(_ bytes: UnsafeRawBufferPointer, field: StaticString) -> [Int: TaskDigest] {
        var scanner = JSONScanner(bytes)
        var digests: [Int: TaskDigest] = [:]
        do {
            try scanner.beginObject()
            while let key = try scanner.nextMember() {
                if scanner.key(key, is: "message") {
                    digests = try message(&scanner, field: field)
                } else {
                    try scanner.skipValue()
                }
            }
        } catch {
            return [:]
        }
        return digests
    }

    /// The digest of `payload[field]` (Codex inputs that are not strings).
    static func payloadDigest(_ bytes: UnsafeRawBufferPointer, field: StaticString) -> TaskDigest {
        var scanner = JSONScanner(bytes)
        var digest = TaskDigest.empty
        do {
            try scanner.beginObject()
            while let key = try scanner.nextMember() {
                guard scanner.key(key, is: "payload") else {
                    try scanner.skipValue()
                    continue
                }
                digest = .empty
                guard try scanner.beginObjectIfPresent() else { continue }
                while let inner = try scanner.nextMember() {
                    if scanner.key(inner, is: field) {
                        digest = TaskDigest(try scanner.readToolDigest())
                    } else {
                        try scanner.skipValue()
                    }
                }
            }
        } catch {
            return .empty
        }
        return digest
    }

    private static func message(_ scanner: inout JSONScanner, field: StaticString) throws(JSONScanError) -> [Int: TaskDigest] {
        var digests: [Int: TaskDigest] = [:]
        guard try scanner.beginObjectIfPresent() else { return digests }
        while let key = try scanner.nextMember() {
            if scanner.key(key, is: "content") {
                digests = try content(&scanner, field: field)
            } else {
                try scanner.skipValue()
            }
        }
        return digests
    }

    private static func content(_ scanner: inout JSONScanner, field: StaticString) throws(JSONScanError) -> [Int: TaskDigest] {
        var digests: [Int: TaskDigest] = [:]
        guard try scanner.beginArrayIfPresent() else {
            try scanner.skipValue()
            return digests
        }
        var index = 0
        while try scanner.nextElement() {
            defer { index += 1 }
            guard try scanner.beginObjectIfPresent() else { continue }
            var found: TaskDigest?
            while let key = try scanner.nextMember() {
                if scanner.key(key, is: field) {
                    found = TaskDigest(try scanner.readToolDigest())
                } else {
                    try scanner.skipValue()
                }
            }
            if let found { digests[index] = found }
        }
        return digests
    }
}

extension TaskDigest {
    init(_ digest: ToolDigest) {
        self.init(value: digest.value)
    }

    /// The digest of empty text: an absent or null input.
    static let empty = TaskDigest(ToolDigest.empty)

    /// `sha16` of a text's UTF-8 bytes.
    static func of(text: String) -> TaskDigest {
        var copy = text
        return copy.withUTF8 { TaskDigest(ToolDigest(UnsafeRawBufferPointer($0))) }
    }

    /// The digest of a parsed input as 6.10 hashes it, when its raw text is not at hand: a string's text, absent or null
    /// as empty text; `raw` for anything else.
    static func of(input: JSONValue?, raw: TaskDigest?) -> TaskDigest {
        switch input {
        case nil, .null?: .empty
        case .string(let text)?: .of(text: text)
        default: raw ?? .empty
        }
    }
}
