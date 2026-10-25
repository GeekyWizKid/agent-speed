import SwiftUI

/// The speed's bolts (DESIGN 4, Agents): one for `fast` and `highspeed`, two overlapping for `ultrafast`, the rear one
/// at 60 %.
struct SpeedBolts: View {
    let count: Int

    @Environment(\.self) private var environment

    var body: some View {
        HStack(spacing: -EffortCapsule.boltOverlap) {
            ForEach(0..<count, id: \.self) { index in
                Image(systemName: "bolt.fill")
                    .font(.system(size: EffortCapsule.boltPointSize))
                    .frame(width: EffortCapsule.boltSize.width, height: EffortCapsule.boltSize.height)
                    .opacity(count > 1 && index < count - 1 ? 0.6 : 1)
            }
        }
        .foregroundStyle(EffortCapsulePalette(environment).bolt)
        .accessibilityHidden(true)
    }
}
