import Foundation

/// C and C++ compilers, linkers and make (TASK-PARSERS 5.3).
final class CcRecognizer: LineRecognizer {
    private static let ext = #"(?:c|cc|cpp|cxx|c\+\+|C|h|hh|hpp|hxx|h\+\+|m|mm|cu|cuh|ino|S|s|ipp|tpp|inl)"#
    private static let error = Pattern(#"(.+?\."# + ext + #"):([0-9]+)(?::([0-9]+))?: (?:fatal )?error: (.+?)(?: \[([^\]]+)\])?"#)
    private static let warning = Pattern(#"(.+?\."# + ext + #"):([0-9]+)(?::([0-9]+))?: (?:warning|note): .*"#)
    private static let context = Pattern(#"In file included from .*|[ \t]+from .*|.+?: In (?:function|member function|constructor|destructor|instantiation of|static member function) .*:"#)
    private static let generated = Pattern(#"([0-9]+) errors? generated\."#)
    private static let undefinedArch = Pattern(#"Undefined symbols for architecture [^ ]+:"#)
    private static let symbol = Pattern(#"[ \t]+\"(.+)\", referenced from:"#)
    private static let reference = Pattern(#"undefined reference to [`'‘](.+?)['’]"#)
    private static let linkerNoise = Pattern(#"(?:[^ ]*/)?ld(?:\.[a-z]+)?: .*|collect2: error: ld returned [0-9]+ exit status|clang(?:\+\+)?: error: linker command failed.*|[ \t]+(?:_?[A-Za-z0-9_]+(?:\(.*\))? in .+\.o|in function .*)"#)
    private static let makeNoise = Pattern(#"g?make(?:\[[0-9]+\])?: \*\*\* .*|g?make(?:\[[0-9]+\])?: (?:Entering|Leaving) directory .*"#)
    private static let ld = Bytes(utf8: "ld")

    private let found: FailureCollector
    private var symbols = false

    init(_ found: FailureCollector) {
        self.found = found
    }

    /// The bracketed flag with `-Werror,` removed and `-Werror=` read as `-W`.
    static func code(_ flag: Bytes?) -> Bytes {
        guard var flag, !flag.isEmpty else { return [] }
        if flag.hasPrefix(Bytes(utf8: "-Werror,")) { flag.removeFirst(8) }
        if flag.hasPrefix(Bytes(utf8: "-Werror=")) { flag = Bytes(utf8: "-W") + flag.dropFirst(8) }
        return flag
    }

    func step(_ lines: [TextLine], _ i: Int) -> Int {
        let line = lines[i]
        if symbols {
            if let match = Self.symbol.fullMatch(line) {
                found.error("cc", file: [], code: Self.ld, message: Bytes(utf8: "undefined symbol ") + match.text(1))
                return 1
            }
            if line.isIndented { return 1 }
            symbols = false
        }
        if let match = Self.error.fullMatch(line) {
            found.error("cc", file: match.text(1), code: Self.code(match[5]), message: match.text(4))
            return 1
        }
        if Self.warning.fullMatches(line) || Self.context.fullMatches(line) { return 1 }
        if let match = Self.generated.fullMatch(line) {
            found.summary("cc", .error, failed: TaskText.number(match.text(1)), passed: nil, mode: .sum)
            return 1
        }
        if Self.undefinedArch.fullMatches(line) {
            symbols = true
            return 1
        }
        if let match = Self.reference.search(line) {
            found.error("cc", file: [], code: Self.ld, message: Bytes(utf8: "undefined reference to ") + match.text(1))
            return 1
        }
        if Self.linkerNoise.fullMatches(line) || Self.makeNoise.fullMatches(line) { return 1 }
        return 0
    }
}
