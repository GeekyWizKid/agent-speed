import Foundation

/// Loops and stalls over a task's counted actions (METRICS 6.9 *E*, 6.11 `stalled`).
enum TaskLoops {
    static let blocks = 1...3
    static let repeats = 3
    static let stallWindow = 8

    /// `loop_marks`: positions covered by a block of 1–3 keys repeated at least 3 times back to back, the block's
    /// first occurrence excluded. Linear: a block at `i` repeats `r` times exactly when `keys[j] == keys[j − b]` for
    /// every `j` in `i + b ..< i + r·b`, so per block size one backward pass gives every maximal `r`, and the marked
    /// ranges are summed with a difference array. The same marks as the reference's direct scan.
    static func marks(_ keys: [Int]) -> [Bool] {
        let n = keys.count
        var depth = [Int](repeating: 0, count: n + 1)
        var run = [Int](repeating: 0, count: n + 1)
        for b in blocks where n >= repeats * b {
            run[n] = 0
            for j in stride(from: n - 1, through: b, by: -1) {
                run[j] = keys[j] == keys[j - b] ? run[j + 1] + 1 : 0
            }
            for i in 0...(n - repeats * b) {
                let matched = run[i + b]
                guard matched >= (repeats - 1) * b else { continue }
                let start = i + b
                depth[start] += 1
                depth[start + (matched / b) * b] -= 1
            }
        }
        var marked = [Bool](repeating: false, count: n)
        var open = 0
        for index in 0..<n {
            open += depth[index]
            marked[index] = open > 0
        }
        return marked
    }

    /// `stall_ends`: 1-based counted indices that end a window of 8 counted actions whose `Σ w·ΔI ≤ 0.25 × Σ w`.
    static func stallEnds(weights: [Int], gains: [Int]) -> [Int] {
        var ends: [Int] = []
        var weighted = 0
        var total = 0
        for index in weights.indices {
            weighted += weights[index] * gains[index]
            total += weights[index]
            if index >= stallWindow {
                weighted -= weights[index - stallWindow] * gains[index - stallWindow]
                total -= weights[index - stallWindow]
            }
            if index >= stallWindow - 1, 4 * weighted <= total { ends.append(index + 1) }
        }
        return ends
    }
}
