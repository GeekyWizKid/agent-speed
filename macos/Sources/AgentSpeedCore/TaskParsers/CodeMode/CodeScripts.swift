import Foundation

/// `code_script` and `code_items` (TASK-PARSERS 4.4).
enum CodeScripts {
    static func script(_ source: SourceKind, tool: String?, input: JSONValue?) -> TaskCodeScript? {
        guard TaskFamily(source) == .codex, tool == "exec", case .string(let raw)? = input,
              TaskInput.object(input) == nil else { return nil }
        let text = Array(raw.unicodeScalars)
        guard let tokens = try? ScriptLexer.lex(text).0 else { return nil }
        var calls: [TaskCodeScript.Call] = []
        var spans: [Int: (index: Int, close: Int)] = [:]
        for (k, token) in tokens.enumerated() {
            guard token.isIdentifier("tools"), !(k > 0 && tokens[k - 1].isPunctuator(".")) else { continue }
            guard k + 3 < tokens.count, tokens[k + 1].isPunctuator("."), tokens[k + 2].kind == .identifier,
                  tokens[k + 3].isPunctuator("("), let close = closing(tokens, at: k + 3) else { continue }
            spans[k] = (calls.count, close)
            let slice = String(String.UnicodeScalarView(text[token.start..<tokens[close].end]))
            calls.append(TaskCodeScript.Call(tool: tokens[k + 2].text, input: callInput(tokens, k, close, env: [:]),
                                             digest: TaskText.sha16(slice)))
        }
        guard !calls.isEmpty else { return nil }
        guard let plain = plainStatements(tokens, spans: spans) else {
            return TaskCodeScript(calls: calls, plain: false, prints: [])
        }
        for (index, value) in plain.inputs { calls[index].input = value }
        return TaskCodeScript(calls: calls, plain: true, prints: plain.prints)
    }

    static func items(_ source: SourceKind, result: JSONValue?) -> [String?]? {
        guard TaskFamily(source) == .codex, let object = result?.object else { return nil }
        let out = object["type"] != nil && object["output"] != nil ? object["output"] : result
        guard let list = out?.array, !list.isEmpty else { return nil }
        let texts: [Bytes?] = list.map { item in item.object?["text"]?.string.map { Bytes(utf8: $0).lfNormalized() } }
        guard let first = texts[0], first.firstPart(byte: Byte.lf) == Bytes(utf8: "Script completed") else { return nil }
        return texts.dropFirst().map { $0?.string }
    }

    private static let opening: Set<String> = ["(", "[", "{"]
    private static let closingSet: Set<String> = [")", "]", "}"]

    /// The index of the token that closes the bracket at `start`, counting every bracket kind.
    static func closing(_ tokens: [ScriptToken], at start: Int) -> Int? {
        var depth = 0
        for j in start..<tokens.count where tokens[j].kind == .punctuator {
            if opening.contains(tokens[j].text) {
                depth += 1
            } else if closingSet.contains(tokens[j].text) {
                depth -= 1
                if depth == 0 { return j }
            }
        }
        return nil
    }

    private static func callInput(_ tokens: [ScriptToken], _ k: Int, _ close: Int, env: [String: JSONValue]) -> JSONValue {
        if close == k + 4 { return .null }
        guard let (value, after) = literal(tokens, k + 4, end: close, env: env), after == close else { return .null }
        return value
    }

    /// The literal at `p` before `end` and the index after it, or nil when it is not a literal.
    static func literal(_ tokens: [ScriptToken], _ p: Int, end: Int, env: [String: JSONValue]) -> (JSONValue, Int)? {
        guard p < end else { return nil }
        let token = tokens[p]
        if token.kind == .identifier, let bound = env[token.text] { return (bound, p + 1) }
        switch token.kind {
        case .string, .number:
            return token.value.map { ($0, p + 1) }
        default:
            break
        }
        if token.isPunctuator("-"), p + 1 < end, tokens[p + 1].kind == .number, let value = tokens[p + 1].value {
            return (negated(value), p + 2)
        }
        if token.kind == .identifier {
            switch token.text {
            case "true": return (.bool(true), p + 1)
            case "false": return (.bool(false), p + 1)
            case "null": return (.null, p + 1)
            default: return nil
            }
        }
        if token.isPunctuator("{") {
            guard let close = closing(tokens, at: p), close < end else { return nil }
            var members: [String: JSONValue] = [:]
            var q = p + 1
            while q < close {
                var stop = q
                var depth = 0
                while stop < close, !(depth == 0 && tokens[stop].isPunctuator(",")) {
                    if tokens[stop].kind == .punctuator {
                        if opening.contains(tokens[stop].text) { depth += 1 }
                        if closingSet.contains(tokens[stop].text) { depth -= 1 }
                    }
                    stop += 1
                }
                if let key = memberKey(tokens[q]), q + 1 < stop, tokens[q + 1].isPunctuator(":"),
                   let (value, after) = literal(tokens, q + 2, end: stop, env: env), after == stop {
                    members[key] = value
                }
                q = stop + 1
            }
            return (.object(members), close + 1)
        }
        if token.isPunctuator("[") {
            guard let close = closing(tokens, at: p), close < end else { return nil }
            var items: [JSONValue] = []
            var q = p + 1
            while q < close {
                guard let (value, after) = literal(tokens, q, end: close, env: env),
                      after >= close || tokens[after].isPunctuator(",") else { return nil }
                items.append(value)
                q = after + 1
            }
            return (.array(items), close + 1)
        }
        return nil
    }

    private static func memberKey(_ token: ScriptToken) -> String? {
        switch token.kind {
        case .identifier: return token.text
        case .string: return token.value?.string
        case .number:
            guard let literal = token.value?.literal, literal.isInteger else { return nil }
            return literal.text
        default: return nil
        }
    }

    private static func negated(_ value: JSONValue) -> JSONValue {
        guard let literal = value.literal else { return value }
        if literal.isInteger { return .number(JSONLiteral(literal.text == "0" ? "0" : "-" + literal.text)) }
        guard let double = literal.double else { return value }
        return .number(JSONLiteral("\(-double)"))
    }

    /// Whether the script is plain: its printing statements and the calls' inputs read with the bindings before them.
    private static func plainStatements(_ tokens: [ScriptToken], spans: [Int: (index: Int, close: Int)])
        -> (prints: [TaskCodeScript.Print], inputs: [Int: JSONValue])? {
        var env: [String: JSONValue] = [:]
        var bound: [String: Int] = [:]
        var prints: [TaskCodeScript.Print] = []
        var inputs: [Int: JSONValue] = [:]
        let n = tokens.count
        var p = 0

        func at(_ i: Int, _ text: String, _ kind: ScriptToken.Kind = .punctuator) -> Bool {
            i < n && tokens[i].kind == kind && tokens[i].text == text
        }
        func awaited(_ i: Int) -> (Int, Int)? {
            guard at(i, "await", .identifier), let span = spans[i + 1] else { return nil }
            inputs[span.index] = callInput(tokens, i + 1, span.close, env: env)
            return (span.index, span.close + 1)
        }

        while p < n {
            if at(p, ";") {
                p += 1
                continue
            }
            if tokens[p].kind == .identifier, ["const", "let", "var"].contains(tokens[p].text), p + 2 < n,
               tokens[p + 1].kind == .identifier, at(p + 2, "=") {
                let name = tokens[p + 1].text
                if let (index, q) = awaited(p + 3) {
                    bound[name] = index
                    env[name] = nil
                    p = q
                } else {
                    guard let (value, q) = literal(tokens, p + 3, end: n, env: env) else { return nil }
                    env[name] = value
                    bound[name] = nil
                    p = q
                }
                continue
            }
            if at(p, "text", .identifier), at(p + 1, "(") {
                let q = p + 2
                var mode = TaskCodeScript.Print.Mode.result
                var found = awaited(q)
                if found == nil, at(q, "(") {
                    if let (index, after) = awaited(q + 1), at(after, ")"), at(after + 1, "."),
                       at(after + 2, "output", .identifier) {
                        found = (index, after + 3)
                        mode = .output
                    }
                }
                if found == nil, q < n, tokens[q].kind == .identifier, let index = bound[tokens[q].text] {
                    var after = q + 1
                    if at(after, "."), at(after + 1, "output", .identifier) {
                        mode = .output
                        after += 2
                    }
                    found = (index, after)
                }
                guard let (index, after) = found, at(after, ")") else { return nil }
                prints.append(TaskCodeScript.Print(call: index, mode: mode))
                p = after + 1
                continue
            }
            guard let (_, q) = awaited(p) else { return nil }
            p = q
        }
        return (prints, inputs)
    }
}
