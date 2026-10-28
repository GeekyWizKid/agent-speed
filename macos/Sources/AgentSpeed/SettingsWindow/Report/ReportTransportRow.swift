import AgentSpeedCore
import SwiftUI

/// `report.transport` (DESIGN 11, item 2b): WebSocket or HTTP for the selected service, WebSocket by default. The
/// caption says what each means; with WebSocket chosen it says where the data comes from. While the service lacks the
/// capability a secondary note takes the caption's place (reports go over HTTP meanwhile, the choice stays), and an
/// amber note says when the service throttles WebSocket below the live level.
struct ReportTransportRow: View {
    static let liveIntervalMs = 50

    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        let report = model.report
        let websocket = report.transport == .websocket
        let unsupported = report.webSocketUnavailable
        let interval = websocket ? report.discovery?.websocket?.minIntervalMs : nil
        let throttled = interval.flatMap { $0 > Self.liveIntervalMs ? $0 : nil }
        let caption = unsupported ? l10n("report.status.wsUnsupported")
            : websocket ? l10n("report.transport.source") : l10n("report.transport.hint")
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
                .accessibilityHint(caption)
            } label: {
                RowTitle(l10n("report.transport"))
            }
            if unsupported {
                // A note, not a warning: reports still go out, over HTTP.
                Notice(text: caption, symbol: "info.circle")
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
