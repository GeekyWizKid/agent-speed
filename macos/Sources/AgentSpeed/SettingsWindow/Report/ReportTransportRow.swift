import AgentSpeedCore
import SwiftUI

/// `report.transport` (DESIGN 11, item 2b): HTTP or WebSocket for the selected service, HTTP by default and never
/// switched automatically. The caption says what each means; with WebSocket chosen it says where the data comes from,
/// and amber notes say when the service lacks the capability or throttles it below the live level.
struct ReportTransportRow: View {
    static let liveIntervalMs = 50

    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let websocket = report.transport == .websocket
        let document = report.discovery
        let unsupported = websocket && document.map { $0.websocket == nil } == true
        let interval = websocket ? document?.websocket?.minIntervalMs : nil
        let throttled = interval.flatMap { $0 > Self.liveIntervalMs ? $0 : nil }
        let caption = websocket ? l10n("report.transport.source") : l10n("report.transport.hint")
        VStack(alignment: .leading, spacing: 6) {
            LabeledContent {
                Picker(l10n("report.transport"), selection: Binding(
                    get: { report.transport }, set: { report.setTransport($0) })) {
                    Text(l10n("report.transport.http")).tag(ReportTransportKind.http)
                    Text(l10n("report.transport.websocket")).tag(ReportTransportKind.websocket)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .fixedSize()
                .accessibilityHint(unsupported ? l10n("report.status.wsUnsupported") : caption)
            } label: {
                RowTitle(l10n("report.transport"))
            }
            if unsupported {
                Notice(text: l10n("report.status.wsUnsupported"), symbol: "exclamationmark.triangle.fill", tint: .orange)
                    .transition(.opacity)
            } else {
                Footnote(caption)
                    .contentTransition(.opacity)
            }
            if let throttled {
                Notice(text: l10n("report.transport.throttled", throttled), symbol: "tortoise.fill", tint: .orange)
                    .monospacedDigit()
                    .transition(.opacity)
            }
        }
        .animation(model.accessibility.reduceMotion ? nil : Motion.fade, value: websocket)
        .animation(model.accessibility.reduceMotion ? nil : Motion.fade, value: unsupported)
    }
}
