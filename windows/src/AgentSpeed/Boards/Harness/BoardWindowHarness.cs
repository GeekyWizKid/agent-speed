using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AgentSpeed.Boards.Gpu;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Interop;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Orb.Render;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// Developer harness <c>AGENT_SPEED_BOARD_WINDOW=FILE</c>: one real board window (click-through, bottom-right of the primary
/// work area) on the orbs' clock and the compositor ticker, presenting through its composition swap chain while the demo
/// runs the bench story (idle, climb, redline, overtakes, new leaders), then hidden for 2 s, then closed. Reports what was
/// presented per second and per frame class, frames skipped as unchanged or refused by a busy compositor, the frames drawn
/// while hidden (must be 0), the process CPU and a capture of the last frame (<c>FILE.png</c>). Runs next to the installed
/// app. <c>AGENT_SPEED_BOARD_WINDOW_STYLE</c>, <c>_SIZE</c> (default S) and <c>_SECONDS</c> (default 8) adjust it.
/// </summary>
internal static class BoardWindowHarness
{
    private const double HiddenSeconds = 2;

    public static int Run(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (!GpuDevice.Initialize() || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(path, "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var style = BoardStyles.Parse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_STYLE") ?? "neon");
        var size = BoardSizes.Parse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_SIZE") ?? "S");
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_WINDOW_SECONDS"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var s) && s > 0 ? Math.Min(s, 60) : 8;
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        var watch = Stopwatch.StartNew();
        var clock = new OrbClock(dispatcher, () => 1000 + watch.Elapsed.TotalSeconds);
        clock.UseCompositorTicker();
        var monitor = Monitors.Primary();
        var work = monitor?.WorkRect ?? new RectD(0, 0, 1920, 1080);
        var scale = monitor?.Scale ?? 1;
        var half = BoardPlacement.HalfSide(size, scale);
        var inset = (OrbGeometry.DefaultInset + BoardCanvas.Margin) * scale;
        var window = new GpuBoardWindow("harness", device, clock, style, size, new PlaceholderBoardPainter(loc));
        window.Configure(style, size, new BoardEffectsPolicy(EffectsIntensity.Standard), new BoardWindowOptions(AlwaysOnTop: true, ClickThrough: true));
        window.MoveTo(new PointD(work.Right - inset - half, work.Bottom - inset - half));

        var feed = new BoardDemoFeed(BoardSimScenario.Team);
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"adapter: {device.AdapterName}, {BoardStyles.Key(style)} {BoardSizes.Key(size)}, scale {scale}, clock {clock.Source} at {clock.RefreshHz:0} Hz");
        var events = 0;
        var ticker = new DispatcherTimer(TimeSpan.FromSeconds(BoardSim.TickSeconds), DispatcherPriority.Normal, (_, _) =>
        {
            var t = watch.Elapsed.TotalSeconds;
            feed.Sim.TargetTotal = t < 2 ? 0 : t < 6 ? 2400 * (t - 2) / 4 : 3200;
            var tick = feed.Step(style, size);
            events += tick.Events.Count;
            window.Apply(tick);
        }, dispatcher);

        var cycles = ProcessCpu.Cycles();
        var presented = BoardSurface.FramesPresented;
        var skipped = BoardSurface.FramesSkipped;
        var busy = BoardSurface.FramesBusy;
        var byClass = (long[])BoardSurface.FramesByClass.Clone();
        var started = watch.Elapsed.TotalSeconds;
        _ = ProcessCpu.CycleRate();
        window.Show();
        ticker.Start();

        After(dispatcher, seconds, () =>
        {
            var wall = watch.Elapsed.TotalSeconds - started;
            var cpu = ProcessCpu.Percent(cycles, ProcessCpu.Cycles(), wall);
            string Fps(FrameClass c) => ((BoardSurface.FramesByClass[(int)c] - byClass[(int)c]) / wall).ToString("0.0", CultureInfo.InvariantCulture);
            report.AppendLine(CultureInfo.InvariantCulture,
                $"shown {wall:0.0} s: cpu {cpu:0.00}% of one core, {(BoardSurface.FramesPresented - presented) / wall:0.0} presented/s, built per class 60: {Fps(FrameClass.Fps60)}/s, 30: {Fps(FrameClass.Fps30)}/s, none: {Fps(FrameClass.None)}/s; {BoardSurface.FramesSkipped - skipped} unchanged, {BoardSurface.FramesBusy - busy} busy, {events} events, {window.Surface.Frame.Layers.Atlas.Rasterized} layers rasterized");
            Save(window, clock, path + ".png", report);
            presented = BoardSurface.FramesPresented + BoardSurface.FramesSkipped;
            window.Hide();
            After(dispatcher, HiddenSeconds, () =>
            {
                report.AppendLine(CultureInfo.InvariantCulture,
                    $"hidden {HiddenSeconds:0} s: {BoardSurface.FramesPresented + BoardSurface.FramesSkipped - presented} frames built");
                ticker.Stop();
                window.Dispose();
                File.WriteAllText(path, report.ToString());
                dispatcher.InvokeShutdown();
            });
        });

        File.WriteAllText(path, report.ToString());
        Dispatcher.Run();
        return 0;
    }

    private static void After(Dispatcher dispatcher, double seconds, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private static void Save(GpuBoardWindow window, OrbClock clock, string path, System.Text.StringBuilder report)
    {
        using var target = new OffscreenTarget();
        if (window.Surface.Capture(clock.Now, target) is not { } bitmap)
        {
            report.AppendLine("capture failed");
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
