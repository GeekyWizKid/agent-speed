using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AgentSpeed.App;
using AgentSpeed.Boards.Gpu;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// Developer harness <c>AGENT_SPEED_BOARD_FRAMES=DIR</c>: renders each board style with the Direct3D renderer on a
/// simulated 60 fps clock fed by the demo, into PNG sheets <c>board-STYLE.png</c> (rows S, M, L; columns calm, cruise,
/// redline, overtake, new leader, Reduce Motion, effects off, high contrast) and <c>board-frames.txt</c> (instances, layers,
/// texture, CPU and GPU time per frame), then exits. Deterministic (fixed seeds and steps); no window, so it runs next to
/// the installed app. <c>AGENT_SPEED_BOARD_FRAMES_STYLES</c> limits the styles, <c>AGENT_SPEED_BOARD_FRAMES_SCALE</c> sets the
/// pixel scale (default 2).
/// </summary>
internal static class BoardFrameHarness
{
    private const double Step = 1 / 60.0;
    private const double Start = 1000;

    private enum Moment
    {
        AtTime,
        AfterOvertake,
        AfterNewLeader,
    }

    private sealed record Column(string Name, BoardSimScenario Scenario, double? Target, double Seconds, Moment Moment, BoardEffectsPolicy Policy);

    private static readonly Column[] Columns =
    [
        new("calm", BoardSimScenario.Calm, null, 6, Moment.AtTime, new BoardEffectsPolicy()),
        new("cruise", BoardSimScenario.Team, 700, 6, Moment.AtTime, new BoardEffectsPolicy()),
        new("redline", BoardSimScenario.Swarm, 3200, 8, Moment.AtTime, new BoardEffectsPolicy()),
        new("overtake", BoardSimScenario.Duel, 2400, 3, Moment.AfterOvertake, new BoardEffectsPolicy()),
        new("new leader", BoardSimScenario.Duel, 2400, 3, Moment.AfterNewLeader, new BoardEffectsPolicy()),
        new("reduce motion", BoardSimScenario.Swarm, 3200, 8, Moment.AtTime, new BoardEffectsPolicy(ReduceMotion: true)),
        new("effects off", BoardSimScenario.Swarm, 3200, 8, Moment.AtTime, new BoardEffectsPolicy(EffectsIntensity.Off)),
        new("high contrast", BoardSimScenario.Swarm, 3200, 8, Moment.AtTime, new BoardEffectsPolicy(HighContrast: true)),
    ];

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "board-frames.txt");
        if (!GpuDevice.Initialize(composition: false) || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(reportPath, "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var scale = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_FRAMES_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var s) && s > 0 ? s : 2;
        var styles = (Environment.GetEnvironmentVariable("AGENT_SPEED_BOARD_FRAMES_STYLES") ?? "glass,timing,neon")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(BoardStyles.Parse).Distinct().ToArray();
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"adapter: {device.AdapterName}, scale {scale}");
        BoardGpuRenderer.Timer = new GpuTimer();
        using var target = new OffscreenTarget();
        foreach (var style in styles)
        {
            var rows = new List<List<BitmapSource>>();
            foreach (var size in BoardSizes.All)
            {
                var row = new List<BitmapSource>();
                foreach (var column in Columns)
                {
                    var (bitmap, line) = Capture(device, target, style, size, column, scale, loc);
                    report.AppendLine(CultureInfo.InvariantCulture, $"{BoardStyles.Key(style)} {BoardSizes.Key(size)} {column.Name}: {line}");
                    if (bitmap is not null)
                    {
                        row.Add(bitmap);
                    }
                }

                rows.Add(row);
                File.WriteAllText(reportPath, report.ToString());
            }

            WriteSheet(rows, Path.Combine(directory, $"board-{BoardStyles.Key(style)}.png"));
        }

        File.WriteAllText(reportPath, report.ToString());
        return 0;
    }

    private static (BitmapSource? Bitmap, string Line) Capture(GpuDevice device, OffscreenTarget target, BoardStyle style, BoardSize size, Column column,
        double scale, Localizer loc)
    {
        using var rig = new BoardRig(style, size, column.Scenario, column.Policy, loc);
        rig.Feed.Sim.TargetTotal = column.Target;
        var pixels = (int)Math.Round(rig.Scene.Canvas.Extent * scale);
        var now = Start;
        var end = Start + column.Seconds;
        var limit = Start + 40;
        var frames = 0;
        var buildTicks = 0L;
        double? captureAt = column.Moment == Moment.AtTime ? end : null;
        while (now < limit)
        {
            if (rig.Advance(now) && captureAt is null && now >= end && rig.LastTick is { } tick)
            {
                var wanted = column.Moment == Moment.AfterNewLeader ? BoardRankEventKind.NewLeader : BoardRankEventKind.Overtake;
                if (tick.Events.Any(e => e.Kind == wanted))
                {
                    captureAt = now + (column.Moment == Moment.AfterNewLeader ? 0.3 : 0.15);
                }
            }

            var start = Stopwatch.GetTimestamp();
            if (!rig.Frame.Build(device, now, scale))
            {
                return (null, "the device failed");
            }

            buildTicks += Stopwatch.GetTimestamp() - start;
            frames++;
            if (captureAt is { } at && now >= at)
            {
                break;
            }

            now += Step;
        }

        if (captureAt is null)
        {
            return (null, "no such moment within 40 s");
        }

        BoardGpuRenderer.Timer?.Drain();
        byte[]? bytes = null;
        for (var i = 0; i < 4; i++)
        {
            // A few draws so the timestamp queries of the first ones are read back.
            bytes = target.Render(device, view => rig.Frame.Draw(device, view, pixels), pixels, pixels);
            device.Current.Context.Flush();
            Thread.Sleep(5);
        }

        var gpu = BoardGpuRenderer.Timer?.Drain() ?? (0.0, 0);
        var list = rig.Frame.List;
        var atlas = rig.Frame.Layers.Atlas;
        var build = frames > 0 ? buildTicks * 1000.0 / Stopwatch.Frequency / frames : 0;
        var line = string.Create(CultureInfo.InvariantCulture,
            $"t {now - Start:0.00} s, {list.Count} instances, {list.LayerCount} layers, {list.BatchCount} batches, demand {rig.Scene.Demand(now)}, layer texture {atlas.Width}×{atlas.Height} ({atlas.Count} layers, {atlas.Rasterized} rasterized over {rig.Ticks} ticks), build {build:0.000} ms/frame, gpu {gpu.Milliseconds:0.000} ms/frame ({gpu.Frames} timed), {pixels}×{pixels} px");
        return (bytes is null ? null : OffscreenTarget.ToBitmap(bytes, pixels, pixels), line);
    }

    /// <summary>The cells on a dark desktop-like backdrop, each cell as wide as the widest.</summary>
    private static void WriteSheet(List<List<BitmapSource>> rows, string path)
    {
        var cell = rows.SelectMany(r => r).Select(b => b.PixelWidth).DefaultIfEmpty(0).Max();
        var columns = rows.Select(r => r.Count).DefaultIfEmpty(0).Max();
        var heights = rows.Select(r => r.Select(b => b.PixelHeight).DefaultIfEmpty(0).Max()).ToArray();
        var width = cell * columns;
        var height = heights.Sum();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(46, 58, 89), Color.FromRgb(16, 18, 28), 90), null, new Rect(0, 0, width, height));
            var y = 0.0;
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var bitmap = rows[r][c];
                    dc.DrawImage(bitmap, new Rect(c * cell + (cell - bitmap.PixelWidth) / 2.0, y + (heights[r] - bitmap.PixelHeight) / 2.0,
                        bitmap.PixelWidth, bitmap.PixelHeight));
                }

                y += heights[r];
            }
        }

        var render = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = File.Create(path);
        encoder.Save(stream);
        AppLog.Info($"Board frames: wrote {Path.GetFileName(path)} ({width} × {height}).");
    }
}
