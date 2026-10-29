import Foundation

/// Rounded half away from zero as `task_score.py` rounds (`int(value + 0.5)`, or `-int(-value + 0.5)` below zero): the
/// addition happens in `Double` and the sum is truncated toward zero. Non-finite or out-of-range values saturate.
public func taskRoundHalfAway(_ value: Double) -> Int {
    value >= 0 ? taskTruncated(value + 0.5) : -taskTruncated(-value + 0.5)
}

/// Python `int(float)`: truncation toward zero, saturated instead of raising.
func taskTruncated(_ value: Double) -> Int {
    guard value.isFinite else { return value > 0 ? Int.max : value < 0 ? Int.min : 0 }
    if value >= 9.223372036854775807e18 { return Int.max }
    if value <= -9.223372036854775808e18 { return Int.min }
    return Int(value)
}

/// Python's `int / int`: the correctly rounded quotient (half to even), also for magnitudes beyond 2^53 where
/// converting both operands to `Double` first would round twice. `denominator` must not be 0.
func taskDivide(_ numerator: Int, _ denominator: Int) -> Double {
    let limit: UInt = 1 << 53
    if numerator.magnitude <= limit && denominator.magnitude <= limit {
        return Double(numerator) / Double(denominator)
    }
    let negative = (numerator < 0) != (denominator < 0)
    let magnitude = exactQuotient(UInt64(numerator.magnitude), UInt64(denominator.magnitude))
    return negative ? -magnitude : magnitude
}

/// `n / d` for 64-bit magnitudes, correctly rounded: 55 significant quotient bits plus a sticky bit, rounded to 53.
private func exactQuotient(_ n: UInt64, _ d: UInt64) -> Double {
    if n == 0 { return 0 }
    var quotient = n / d
    var remainder = n % d
    var exponent = 0
    while quotient < (1 << 54) {
        let doubled = remainder << 1
        let bit: UInt64 = doubled >= d ? 1 : 0
        remainder = doubled - bit * d
        quotient = quotient << 1 | bit
        exponent -= 1
    }
    let width = UInt64.bitWidth - quotient.leadingZeroBitCount
    let extra = width - 53
    let low = quotient & ((1 << UInt64(extra)) - 1)
    let half: UInt64 = 1 << UInt64(extra - 1)
    var mantissa = quotient >> UInt64(extra)
    let sticky = remainder != 0
    if low > half || (low == half && (sticky || mantissa & 1 == 1)) { mantissa += 1 }
    return Double(sign: .plus, exponent: exponent + extra, significand: Double(mantissa))
}
