import AppKit
import SwiftUI

/// Items left to right on first-baseline-aligned lines, wrapping between items; an item wider than a whole line is
/// offered the line's width, so its text wraps instead of running past the edge.
struct TaskFlowLayout: Layout {
    var spacing: CGFloat = 0
    var lineSpacing: CGFloat = 2

    private struct Line {
        var items: [(index: Int, size: CGSize, baseline: CGFloat)] = []
        var width: CGFloat = 0
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var height: CGFloat { ascent + descent }
    }

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let lines = arrange(width: proposal.width ?? .infinity, subviews: subviews)
        let width = lines.map(\.width).max() ?? 0
        let height = lines.reduce(0) { $0 + $1.height } + lineSpacing * CGFloat(max(lines.count - 1, 0))
        return CGSize(width: proposal.width.map { min(width, $0) } ?? width, height: height)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for line in arrange(width: bounds.width, subviews: subviews) {
            var x = bounds.minX
            for item in line.items {
                subviews[item.index].place(at: CGPoint(x: x, y: y + line.ascent - item.baseline),
                                           proposal: ProposedViewSize(item.size))
                x += item.size.width + spacing
            }
            y += line.height + lineSpacing
        }
    }

    private func arrange(width: CGFloat, subviews: Subviews) -> [Line] {
        var lines: [Line] = []
        var current = Line()
        for index in subviews.indices {
            let ideal = subviews[index].sizeThatFits(.unspecified)
            let proposal = ProposedViewSize(width: min(ideal.width, width), height: nil)
            let size = ideal.width > width ? subviews[index].sizeThatFits(proposal) : ideal
            let baseline = subviews[index].dimensions(in: proposal)[VerticalAlignment.firstTextBaseline]
            let needed = current.items.isEmpty ? size.width : current.width + spacing + size.width
            if needed > width + 0.5, !current.items.isEmpty {
                lines.append(current)
                current = Line()
            }
            current.width = current.items.isEmpty ? size.width : current.width + spacing + size.width
            current.items.append((index, size, baseline))
            current.ascent = max(current.ascent, baseline)
            current.descent = max(current.descent, size.height - baseline)
        }
        if !current.items.isEmpty { lines.append(current) }
        return lines
    }
}

enum TaskFlowText {
    /// A localized run split after its separators (` · `, `・`), each part keeping the separator it ends with, so a
    /// line can only break between parts.
    static func parts(_ text: String) -> [String] {
        var parts: [String] = []
        var rest = Substring(text)
        while let range = rest.range(of: " · ") ?? rest.range(of: "・") {
            parts.append(String(rest[..<range.upperBound]).replacingOccurrences(of: " · ", with: " ·\u{00A0}"))
            rest = rest[range.upperBound...]
        }
        if !rest.isEmpty { parts.append(String(rest)) }
        return parts
    }
}

/// The width at which a text wraps onto its lines evenly instead of leaving a lone word on the last one: before its
/// parenthetical qualifier when there is one, else the narrowest width (within `available`) that needs no more lines
/// than `available` does. Measured with AppKit's text layout and a
/// little slack, then applied as a plain frame, so the view's layout never depends on a second measuring pass.
@MainActor
enum TaskTextBalance {
    private static var cache: [String: CGFloat] = [:]

    static func width(_ text: String, font: NSFont, within available: CGFloat) -> CGFloat {
        let key = "\(text)|\(font.pointSize)|\(font.fontName)|\(available)"
        if let width = cache[key] { return width }
        let measure = { (part: String) in ceil((part as NSString).size(withAttributes: [.font: font]).width) }
        let single = measure(text)
        var result = available
        // A parenthetical qualifier (`首次通過時間（中位數）`) takes the second line whole when both parts fit.
        if single > available, let open = text.range(of: "（") ?? text.range(of: " (") {
            let head = String(text[..<open.lowerBound])
            let tail = String(text[open.lowerBound...]).trimmingCharacters(in: .whitespaces)
            let width = max(measure(head), measure(tail)) + 2
            if !head.isEmpty, width <= available {
                cache[key] = width
                return width
            }
        }
        if single > available {
            let target = lines(text, font: font, width: available)
            var width = ceil(single / CGFloat(max(target, 1)))
            while width < available {
                if lines(text, font: font, width: width) <= target {
                    result = min(available, width + 2)
                    break
                }
                width += 2
            }
        }
        cache[key] = result
        return result
    }

    static func lines(_ text: String, font: NSFont, width: CGFloat) -> Int {
        let storage = NSTextStorage(string: text, attributes: [.font: font])
        let container = NSTextContainer(size: CGSize(width: width, height: .greatestFiniteMagnitude))
        container.lineFragmentPadding = 0
        let manager = NSLayoutManager()
        manager.addTextContainer(container)
        storage.addLayoutManager(manager)
        manager.ensureLayout(for: container)
        var count = 0
        var index = 0
        while index < manager.numberOfGlyphs {
            var range = NSRange()
            manager.lineFragmentRect(forGlyphAt: index, effectiveRange: &range)
            index = max(NSMaxRange(range), index + 1)
            count += 1
        }
        return count
    }
}
