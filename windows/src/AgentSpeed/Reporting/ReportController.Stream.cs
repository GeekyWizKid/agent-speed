using System.Windows.Threading;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Reporting;

/// <summary>
/// Live reporting over WebSocket (METRICS 11.9, DESIGN 11 item 2b), chosen per service by the user: every snapshot and every
/// wake-up the stream scheduler asks for drive one transport session; its actions open and end the connection, build and
/// write reports and take the reactions of REPORTING 8.3. Nothing of this runs while HTTP is chosen, and HTTP is never used
/// instead.
/// </summary>
internal sealed partial class ReportController
{
    private static readonly TimeSpan CloseFrameGrace = TimeSpan.FromMilliseconds(150);

    private readonly IReportStreamTransport _streamTransport;
    private ReportStreamRun? _stream;
    private DispatcherTimer? _wakeTimer;
    private long? _wakeAt;
    private long? _observation;
    private bool _quitting;
    private bool _quitFlushing;
    private StreamSignature _streamSignature;

    /// <summary>The selected service's transport (DESIGN 11, Transport): the user's choice, HTTP by default.</summary>
    public ReportTransportKind Transport => Preferences.TransportOf(SelectedService);

    public bool UsesWebSocket => Transport == ReportTransportKind.WebSocket;

    /// <summary>The WebSocket transport session (counters, latency, connection), or null while none runs.</summary>
    public ReportStreamScheduler? StreamSession => _stream?.Scheduler;

    /// <summary>The stream's own status line (DESIGN 11, Status), or null when the shared statuses apply.</summary>
    public ReportStreamStatusKind? StreamStatus => !UsesWebSocket ? null
        : _stream is { Scheduler: var s } ? ReportStreamStatus.Of(s.Available, s.Stop, s.Connection, s.Failures, s.Mode)
        : Discovery is { WebSocket: null } ? ReportStreamStatusKind.Unsupported
        : null;

    /// <summary>
    /// Switches the selected service's transport. It applies at once: the old transport stops without a leaving report (its
    /// unanswered reports are dropped) and a new run starts on the new one.
    /// </summary>
    public void SetTransport(ReportTransportKind transport)
    {
        if (SelectedService is not { } service || transport == Transport)
        {
            return;
        }

        EndRun();
        UpdatePreferences(p => p.WithTransport(service.Origin, transport));
        RetryReason = null;
        RateLimitedUntil = null;
        LastDeliveryAt = null;
        ClearRejection();
        ReportLog.Write($"transport for {service.Host}: {(transport == ReportTransportKind.WebSocket ? "websocket" : "http")}");
        Tick();
        Notify();
    }

    /// <summary><i>Try Again</i> after <c>report.status.wsMismatch</c> or <c>report.status.wsReplaced</c>: clears the stop and reconnects.</summary>
    public void RetryStream()
    {
        if (_stream is not { } run || !NetworkEnabled)
        {
            return;
        }

        var sample = LastSample;
        ApplyStream(run, run.Scheduler.Retry(MonotonicMs), sample);
        StreamTick(sample, MonotonicMs, wake: false);
        Notify();
    }

    /// <summary>At quit with an open connection: one <c>leaving</c> report, then the close, within a second (METRICS 11.9).</summary>
    private void PrepareStreamToQuit(Action reply)
    {
        _quitting = true;
        if (_stream is not { Scheduler: var s } || s.Connection != ReportStreamConnection.Open || s.Mode == ReportMode.Quiet || !IsReady()
            || !NetworkEnabled)
        {
            EndStream();
            reply();
            return;
        }

        _quitReply = reply;
        After(TimeSpan.FromSeconds(1), FinishQuit);
        Post(ReportEvent.Stop);
    }

    /// <summary>One tick of the stream scheduler: a snapshot, a user event or (<paramref name="wake"/>) a wake-up it asked for.</summary>
    private void StreamTick(ReportSample sample, long now, bool wake)
    {
        if (_stream is { } stale && stale.Origin != SelectedService?.Origin)
        {
            EndStream();
        }

        StartStreamIfNeeded(sample);
        var events = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        var observed = wake ? null : _observation;
        if (!wake)
        {
            _observation = null;
        }

        if (_stream is not { } run)
        {
            return;
        }

        var active = wake ? run.LastActive : sample.IsWorking || sample.Live.Output > 0 || run.Window.HasTokenIncrement(sample);
        run.LastActive = active;
        var changed = !wake && (run.Window.HasIncrement(sample, EffectiveSections.Contains(ReportSection.Traffic))
            || ReportStreamValues.Of(sample) != run.LastBuilt);
        var settings = _settings.Current;
        run.Title.Update(sample.Value(settings.Metric), settings.Redline, now / 1000.0);
        if (run.Scheduler.Stop is { } stop && ReportStreamStatus.ProblemOf(stop) is not null && Problem is null)
        {
            // The user fixed what the stop stood for (a key, a section, a reset): automatic attempts resume.
            ApplyStream(run, run.Scheduler.Retry(now), sample);
        }

        ApplyStream(run, run.Scheduler.Tick(now, active, changed, observed, blocked: !IsReady(), events), sample);
        AfterStream();
    }

    private void StartStreamIfNeeded(ReportSample sample)
    {
        if (_stream is not null || _quitting || !Preferences.Enabled || !_monitorRunning || _signer is null
            || Identity is not { IsRegistered: true, Discovery: { } document } identity)
        {
            return;
        }

        var scheduler = new ReportStreamScheduler(document.WebSocket, document.Limits.HeartbeatSeconds, IsPaused, !NetworkEnabled);
        _stream = new ReportStreamRun(identity.Id, scheduler, new ReportWindow(sample));
        _renamedForMismatch = false;
        ReportLog.Write($"websocket session for {identity.Service.Host}{(document.WebSocket is null ? " (unavailable)" : string.Empty)}");
    }

    /// <summary>Ends the session: an open connection is closed (a leaving report already written goes first), any other dropped.</summary>
    private void EndStream()
    {
        if (_stream is not { } run)
        {
            return;
        }

        _stream = null;
        CancelWake();
        if (run.Socket is { } socket)
        {
            if (run.Scheduler.Connection is ReportStreamConnection.Open or ReportStreamConnection.Closing)
            {
                socket.Close();
            }
            else
            {
                socket.Abort();
            }

            run.Socket = null;
        }

        if (Mode != ReportMode.Quiet)
        {
            Mode = ReportMode.Quiet;
        }

        Notify();
    }

    private void ApplyStream(ReportStreamRun run, IReadOnlyList<ReportStreamAction> actions, ReportSample sample)
    {
        var windowStarted = false;
        foreach (var action in actions)
        {
            switch (action)
            {
                case ReportStreamAction.Discard discard:
                    run.Window.Discard(sample, discard.KeepingTick);
                    windowStarted = true;
                    break;
                case ReportStreamAction.Connect:
                    Connect(run);
                    break;
                case ReportStreamAction.Abort:
                    run.Socket?.Abort();
                    run.Socket = null;
                    break;
                case ReportStreamAction.Close:
                    run.Socket?.Close();
                    run.Socket = null;
                    break;
                case ReportStreamAction.Build build:
                    Build(run, build.Ticket, sample);
                    windowStarted = true;
                    break;
                case ReportStreamAction.Write write:
                    Write(run, write.Seq, sample);
                    break;
                case ReportStreamAction.Delivered delivered:
                    Delivered(run, delivered.Seq);
                    break;
                case ReportStreamAction.Dropped dropped:
                    run.Reports.Remove(dropped.Seq);
                    break;
                case ReportStreamAction.React react:
                    React(run, react.Reaction, sample);
                    break;
                case ReportStreamAction.Stop stop:
                    Halted(stop.Reason);
                    break;
                case ReportStreamAction.Wait wait:
                    var seconds = Math.Max(0, (wait.Until - MonotonicMs) / 1000.0);
                    RateLimitedUntil = DateTimeOffset.UtcNow.AddSeconds(seconds);
                    After(TimeSpan.FromSeconds(seconds + 1), Notify);
                    break;
            }
        }

        run.Window.Observe(sample, peaks: !windowStarted);
    }

    /// <summary>After any step: the mode, the wake-up timer, the end of a finished session, the quit reply and the status.</summary>
    private void AfterStream()
    {
        if (_stream is { } run)
        {
            Mode = run.Scheduler.Mode;
            ScheduleWake(run.Scheduler.WakeAt);
            if ((run.Scheduler.Ended || !Preferences.Enabled) && run.Scheduler.Connection == ReportStreamConnection.Closed)
            {
                EndStream();
            }
        }

        if (_quitReply is not null && !_quitFlushing && _stream?.Scheduler.Connection is null or ReportStreamConnection.Closed)
        {
            // The close frame after the leaving report is queued; give it a moment within the one-second cap.
            _quitFlushing = true;
            After(CloseFrameGrace, FinishQuit);
        }

        var signature = StreamSignature.Of(_stream?.Scheduler, Mode, LastDeliveryAt is null, RateLimitedUntil is null);
        if (signature != _streamSignature)
        {
            _streamSignature = signature;
            Notify();
        }
    }

    private void ScheduleWake(long? at)
    {
        if (at == _wakeAt)
        {
            return;
        }

        CancelWake();
        if (at is not { } target)
        {
            return;
        }

        _wakeAt = target;
        if (_wakeTimer is null)
        {
            _wakeTimer = new DispatcherTimer(DispatcherPriority.Normal, _ui);
            _wakeTimer.Tick += (_, _) =>
            {
                CancelWake();
                if (_stream is not null)
                {
                    StreamTick(LastSample, MonotonicMs, wake: true);
                }
            };
        }

        _wakeTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, target - MonotonicMs));
        _wakeTimer.Start();
    }

    private void CancelWake()
    {
        _wakeTimer?.Stop();
        _wakeAt = null;
    }

    /// <summary>What the pane shows of the session; a change of it notifies, an unchanged tick does not.</summary>
    private readonly record struct StreamSignature(
        bool Open, ReportStreamStop? Stop, bool Available, bool Failing, ReportMode Mode, bool NeverDelivered, bool NotLimited, int Settled)
    {
        public static StreamSignature Of(ReportStreamScheduler? s, ReportMode mode, bool neverDelivered, bool notLimited) => new(
            s?.Connection == ReportStreamConnection.Open, s?.Stop, s?.Available ?? true, s?.Failures > 0, mode, neverDelivered, notLimited,
            s is null ? 0 : s.Counters.Delivered + s.Counters.NotDelivered > 0 ? 2 : 1);
    }
}
