import Foundation

/// `edit_hunks` (TASK-PARSERS 6): per edited file, the digests of the changed lines without their context.
enum EditHunks {
    static func hunks(_ source: SourceKind, tool: String?, input: JSONValue?, cwd: String?) -> [TaskEditHunk] {
        guard let family = TaskFamily(source), let tool else { return [] }
        let name = family.toolName(tool)
        let kind = family.kind(of: tool)
        if kind == .command {
            guard let text = TaskInput.commandText(family, tool: tool, input: input) else { return [] }
            return ShellCommand(text, cwd: cwd).patches.flatMap { CodexPatch.hunks($0, cwd: cwd) }
        }
        guard kind == .edit else { return [] }
        let args = TaskInput.object(input)
        if family == .codex {
            if case .string(let text)? = input, args == nil { return CodexPatch.hunks(Bytes(utf8: text), cwd: cwd) }
            let text = args?["input"]?.string ?? args?["patch"]?.string
            return text.map { CodexPatch.hunks(Bytes(utf8: $0), cwd: cwd) } ?? []
        }
        guard let args else { return [] }
        switch family {
        case .droid:
            return droid(name, args, cwd: cwd)
        case .claude:
            return claude(name, args, cwd: cwd)
        default:
            return piFamily(family, name, args, cwd: cwd)
        }
    }

    private static func claude(_ name: String, _ args: [String: JSONValue], cwd: String?) -> [TaskEditHunk] {
        let path = args["file_path"]?.string
        if name == "Edit", let path, !path.isEmpty, let old = args["old_string"]?.string,
           let new = args["new_string"]?.string {
            return [hunk(path, old, new, cwd: cwd)]
        }
        if name == "MultiEdit", let path, !path.isEmpty {
            return (args["edits"]?.array ?? []).compactMap { entry in
                guard let old = entry.object?["old_string"]?.string, let new = entry.object?["new_string"]?.string else {
                    return nil
                }
                return hunk(path, old, new, cwd: cwd)
            }
        }
        if name == "Write", let path, !path.isEmpty, let content = args["content"]?.string {
            return [whole(path, content, cwd: cwd)]
        }
        if name == "NotebookEdit", let notebook = args["notebook_path"]?.string {
            let cell = args["cell_id"]?.string ?? args["cell_number"]?.int64.map { String($0) } ?? ""
            let source = args["new_source"]?.string ?? ""
            switch args["edit_mode"] {
            case .string("insert")?:
                return [hunk(notebook, "", source, cwd: cwd)]
            case .string("delete")?:
                return [deleted(notebook + "#" + cell, cwd: cwd)]
            default:
                return [whole(notebook + "#" + cell, source, cwd: cwd)]
            }
        }
        return []
    }

    /// `old_str` / `new_str`, else `old_string` / `new_string`.
    private static func droidPair(_ entry: [String: JSONValue]) -> (String, String)? {
        for (oldKey, newKey) in [("old_str", "new_str"), ("old_string", "new_string")] {
            if let old = entry[oldKey]?.string, let new = entry[newKey]?.string { return (old, new) }
        }
        return nil
    }

    private static func droid(_ name: String, _ args: [String: JSONValue], cwd: String?) -> [TaskEditHunk] {
        guard let path = args["file_path"]?.string else { return [] }
        if name == "Create" {
            return args["content"]?.string.map { [whole(path, $0, cwd: cwd)] } ?? []
        }
        if name == "Edit" {
            return droidPair(args).map { [hunk(path, $0.0, $0.1, cwd: cwd)] } ?? []
        }
        return (args["edits"]?.array ?? []).compactMap { entry in
            entry.object.flatMap(droidPair).map { hunk(path, $0.0, $0.1, cwd: cwd) }
        }
    }

    private static func piFamily(_ family: TaskFamily, _ name: String, _ args: [String: JSONValue],
                                 cwd: String?) -> [TaskEditHunk] {
        let path = args["path"]?.string
        if name == "write" {
            guard let path, !path.isEmpty, let content = args["content"]?.string else { return [] }
            return [whole(path, content, cwd: cwd)]
        }
        if name == "apply_patch" {
            return args["input"]?.string.map { CodexPatch.hunks(Bytes(utf8: $0), cwd: cwd) } ?? []
        }
        guard name == "edit" else { return [] }
        if let input = args["input"]?.string { return patchInput(Bytes(utf8: input), cwd: cwd) }
        guard let path, !path.isEmpty else { return [] }
        if family == .omp, let old = args["old_string"]?.string, let new = args["new_string"]?.string {
            return [hunk(path, old, new, cwd: cwd)]
        }
        if family == .omp, let edits = args["edits"]?.array,
           edits.contains(where: { $0.object.map { $0["diff"] != nil || $0["op"] != nil } ?? false }) {
            return ompPatchMode(path, edits, cwd: cwd)
        }
        return piEdits(args).map { hunk(path, $0.0, $0.1, cwd: cwd) }
    }

    /// Pi and OpenClaw `edit`: `edits` (an array, a JSON string or one object) plus the legacy `oldText` / `newText`.
    private static func piEdits(_ args: [String: JSONValue]) -> [(String, String)] {
        var edits = args["edits"]
        if case .string(let text)? = edits { edits = TaskInput.parse(text) }
        if case .object? = edits { edits = .array([edits!]) }
        var pairs: [(String, String)] = []
        for entry in edits?.array ?? [] {
            if let old = entry.object?["oldText"]?.string, let new = entry.object?["newText"]?.string {
                pairs.append((old, new))
            }
        }
        if let old = args["oldText"]?.string, let new = args["newText"]?.string,
           !pairs.contains(where: { Bytes(utf8: $0.0) == Bytes(utf8: old) && Bytes(utf8: $0.1) == Bytes(utf8: new) }) {
            pairs.append((old, new))
        }
        return pairs
    }

    private static func ompPatchMode(_ path: String, _ edits: [JSONValue], cwd: String?) -> [TaskEditHunk] {
        var hunks: [TaskEditHunk] = []
        for entry in edits {
            guard let entry = entry.object else { continue }
            let diff = Bytes(utf8: entry["diff"]?.string ?? "")
            switch entry["op"] {
            case .string("delete")?:
                hunks.append(deleted(path, cwd: cwd))
            case .string("create")?:
                var body = diff.lfNormalized().split(byte: Byte.lf)
                if body.allSatisfy({ $0.isEmpty || $0.first == 0x2B }) { body = body.map { Bytes($0.dropFirst()) } }
                hunks.append(whole(path, Bytes.joined(body, separator: [Byte.lf]), cwd: cwd))
            default:
                var target = path
                if let rename = entry["rename"]?.string, !rename.isEmpty {
                    hunks.append(deleted(path, cwd: cwd))
                    target = rename
                }
                for (old, new) in CodexPatch.updateChunks(diff.lfNormalized().split(byte: Byte.lf)) {
                    hunks.append(hunk(target, lines: old, new, cwd: cwd))
                }
            }
        }
        return hunks
    }

    private static func patchInput(_ text: Bytes, cwd: String?) -> [TaskEditHunk] {
        let lines = text.lfNormalized().split(byte: Byte.lf)
        if lines.contains(where: { CodexPatch.header.fullMatches($0.trimmedRight()) }) {
            return CodexPatch.hunks(text, cwd: cwd)
        }
        if lines.contains(where: { hashlineHeader.fullMatches($0.trimmedRight()) }) {
            return hashline(lines, cwd: cwd)
        }
        return []
    }

    private static let hashlineHeader = Pattern(#"\[(.+)#([0-9A-Za-z]{4})\]"#)
    private static let hashlineInsert = Pattern(#"PUT (?:<[0-9]+|>[0-9]+\*?|>\$):"#)
    private static let hashlineMove = Pattern(#"MV (.+)"#)

    /// omp hashline edits: only insertions and file deletions carry both sides (6.4).
    private static func hashline(_ lines: [Bytes], cwd: String?) -> [TaskEditHunk] {
        var hunks: [TaskEditHunk] = []
        var path: Bytes?
        var k = 0
        while k < lines.count {
            let line = lines[k].trimmedRight()
            k += 1
            if let match = hashlineHeader.fullMatch(line) {
                path = match.text(1).trimmed()
                continue
            }
            guard let current = path else { continue }
            if hashlineInsert.fullMatches(line) {
                var body: [Bytes] = []
                while k < lines.count, lines[k].first == 0x2B {
                    body.append(Bytes(lines[k].dropFirst()))
                    k += 1
                }
                if !body.isEmpty { hunks.append(hunk(current, lines: [], body, cwd: cwd)) }
            } else if line == Bytes(utf8: "REM") {
                hunks.append(deleted(current, cwd: cwd))
            } else if let match = hashlineMove.fullMatch(line) {
                hunks.append(deleted(current, cwd: cwd))
                path = match.text(1).trimmed()
            }
        }
        return hunks
    }

    // MARK: Normalisation (6.1)

    static func normalizedPath(_ path: Bytes, cwd: String?) -> Bytes {
        var p = path.replacing(byte: Byte.backslash, with: Byte.slash)
        if p.hasPrefix(Bytes(utf8: "file://")) { p.removeFirst(7) }
        p = TaskPaths.cwdRelative(p, cwd: cwd).0
        while p.contains(Bytes(utf8: "//")) { p = p.replacing(Bytes(utf8: "//"), with: [Byte.slash]) }
        while p.hasPrefix(Bytes(utf8: "./")) { p.removeFirst(2) }
        while p.contains(Bytes(utf8: "/./")) { p = p.replacing(Bytes(utf8: "/./"), with: [Byte.slash]) }
        return p
    }

    /// LF-normalised, split at LF, right-trimmed, empty lines removed from both ends.
    static func textLines(_ text: Bytes) -> [Bytes] {
        var lines = text.lfNormalized().split(byte: Byte.lf).map { $0.trimmedRight() }
        let first = lines.firstIndex { !$0.isEmpty } ?? lines.count
        let last = lines.lastIndex { !$0.isEmpty } ?? -1
        lines = first <= last ? Array(lines[first...last]) : []
        return lines
    }

    private static func hunkTexts(_ old: [Bytes], _ new: [Bytes]) -> (Bytes, Bytes) {
        var start = 0
        while start < old.count, start < new.count, old[start] == new[start] { start += 1 }
        var oldEnd = old.count
        var newEnd = new.count
        while oldEnd > start, newEnd > start, old[oldEnd - 1] == new[newEnd - 1] {
            oldEnd -= 1
            newEnd -= 1
        }
        return (Bytes.joined(Array(old[start..<oldEnd]), separator: [Byte.lf]),
                Bytes.joined(Array(new[start..<newEnd]), separator: [Byte.lf]))
    }

    private static func file(_ path: Bytes, cwd: String?) -> String {
        TaskText.sha16(normalizedPath(path, cwd: cwd))
    }

    static func hunk(_ path: String, _ old: String, _ new: String, cwd: String?) -> TaskEditHunk {
        let (o, n) = hunkTexts(textLines(Bytes(utf8: old)), textLines(Bytes(utf8: new)))
        return TaskEditHunk(file: file(Bytes(utf8: path), cwd: cwd), old: TaskText.sha16(o), new: TaskText.sha16(n))
    }

    static func hunk(_ path: Bytes, lines old: [Bytes], _ new: [Bytes], cwd: String?) -> TaskEditHunk {
        let (o, n) = hunkTexts(textLines(Bytes.joined(old, separator: [Byte.lf])),
                               textLines(Bytes.joined(new, separator: [Byte.lf])))
        return TaskEditHunk(file: file(path, cwd: cwd), old: TaskText.sha16(o), new: TaskText.sha16(n))
    }

    static func hunk(_ path: String, lines old: [Bytes], _ new: [Bytes], cwd: String?) -> TaskEditHunk {
        hunk(Bytes(utf8: path), lines: old, new, cwd: cwd)
    }

    static func whole(_ path: String, _ content: String, cwd: String?) -> TaskEditHunk {
        whole(Bytes(utf8: path), Bytes(utf8: content), cwd: cwd)
    }

    static func whole(_ path: String, _ content: Bytes, cwd: String?) -> TaskEditHunk {
        whole(Bytes(utf8: path), content, cwd: cwd)
    }

    static func whole(_ path: Bytes, _ content: Bytes, cwd: String?) -> TaskEditHunk {
        TaskEditHunk(file: file(path, cwd: cwd), old: nil,
                     new: TaskText.sha16(Bytes.joined(textLines(content), separator: [Byte.lf])))
    }

    static func deleted(_ path: String, cwd: String?) -> TaskEditHunk {
        deleted(Bytes(utf8: path), cwd: cwd)
    }

    static func deleted(_ path: Bytes, cwd: String?) -> TaskEditHunk {
        TaskEditHunk(file: file(path, cwd: cwd), old: nil, new: TaskText.emptyDigest)
    }
}

/// Codex patches (TASK-PARSERS 6.3).
enum CodexPatch {
    static let header = Pattern(#"\*\*\* (Add File|Delete File|Update File|Move to): (.+)"#)

    private enum Operation {
        case add, delete, update
    }

    private struct Section {
        var operation: Operation
        var path: Bytes
        var moveTo: Bytes?
        var body: [Bytes] = []
    }

    static func hunks(_ text: Bytes, cwd: String?) -> [TaskEditHunk] {
        let lines = text.lfNormalized().split(byte: Byte.lf)
        let start = lines.firstIndex { $0.trimmed() == Bytes(utf8: "*** Begin Patch") }.map { $0 + 1 } ?? 0
        var sections: [Section] = []
        for line in lines[start...] {
            if line.trimmed() == Bytes(utf8: "*** End Patch") { break }
            if let match = header.fullMatch(line.trimmedRight()) {
                let kind = match.text(1)
                if kind == Bytes(utf8: "Move to") {
                    if let last = sections.last, last.operation == .update {
                        sections[sections.count - 1].moveTo = match.text(2).trimmed()
                        continue
                    }
                } else {
                    let operation: Operation = kind == Bytes(utf8: "Add File") ? .add
                        : kind == Bytes(utf8: "Delete File") ? .delete : .update
                    sections.append(Section(operation: operation, path: match.text(2).trimmed()))
                    continue
                }
            }
            if !sections.isEmpty { sections[sections.count - 1].body.append(line) }
        }
        var hunks: [TaskEditHunk] = []
        for section in sections {
            switch section.operation {
            case .add:
                let added = section.body.filter { $0.first == 0x2B }.map { Bytes($0.dropFirst()) }
                hunks.append(EditHunks.whole(section.path, Bytes.joined(added, separator: [Byte.lf]), cwd: cwd))
            case .delete:
                hunks.append(EditHunks.deleted(section.path, cwd: cwd))
            case .update:
                var target = section.path
                if let moveTo = section.moveTo, !moveTo.isEmpty {
                    hunks.append(EditHunks.deleted(section.path, cwd: cwd))
                    target = moveTo
                }
                for (old, new) in updateChunks(section.body) {
                    hunks.append(EditHunks.hunk(target, lines: old, new, cwd: cwd))
                }
            }
        }
        return hunks
    }

    /// The old and new lines of each `@@` chunk of an Update File section or a diff.
    static func updateChunks(_ lines: [Bytes]) -> [([Bytes], [Bytes])] {
        var chunks: [([Bytes], [Bytes])] = []
        var old: [Bytes] = []
        var new: [Bytes] = []
        var changed = false
        for line in lines {
            if line.hasPrefix(Bytes(utf8: "@@")) {
                if changed { chunks.append((old, new)) }
                old = []
                new = []
                changed = false
            } else if line == Bytes(utf8: "*** End of File") {
                continue
            } else if line.first == Byte.dash {
                old.append(Bytes(line.dropFirst()))
                changed = true
            } else if line.first == 0x2B {
                new.append(Bytes(line.dropFirst()))
                changed = true
            } else {
                let text = line.first == Byte.space ? Bytes(line.dropFirst()) : line
                old.append(text)
                new.append(text)
            }
        }
        if changed { chunks.append((old, new)) }
        return chunks
    }
}
