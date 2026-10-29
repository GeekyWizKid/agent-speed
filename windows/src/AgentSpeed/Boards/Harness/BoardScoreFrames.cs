using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using AgentSpeed.Boards.Gpu;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.TaskScore;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// <c>AGENT_SPEED_BOARD_FRAMES_SCORES=1</c> with the frame harness: the task score on the boards (DESIGN 12; 2c, Live metrics) —
/// per style and appearance a sheet <c>scores-STYLE-THEME.png</c> (rows S, M, L; columns <see cref="States"/>) and each cell as
/// <c>scores-STYLE-SIZE-THEME-STATE.png</c>, after the team story's 40 ticks with the hero's 智力 set to the state (the rows keep
/// the demo's own scores: those below 50 are unverified and carry the proof mark).
/// </summary>
internal static class BoardScoreFrames
{
    private const double Step = 1 / 60.0;
    private const double Start = 1000;

    /// <summary>The hero states; <c>climb</c> is caught 0.3 s into a count-up from 40 to 80.</summary>
    private static readonly (string Name, TaskSetReading Score, bool HighContrast)[] States =
    [
        ("live", new(TaskForm.Live, 86, TaskState.Passing, 3, null), false),
        ("unverified", new(TaskForm.Live, 9, TaskState.Unverified, 1, null), false),
        ("pending", new(TaskForm.Live, 64, TaskState.Pending, 2, null), false),
        ("resting", new(TaskForm.Resting, 72, TaskState.Passing, 2, null), false),
        ("resting-pending", new(TaskForm.Resting, 40, TaskState.Pending, 1, null), false),
        ("none", TaskSetReading.None, false),
        ("climb", new(TaskForm.Live, 80, TaskState.Passing, 1, null), false),
        ("hc-resting", new(TaskForm.Resting, 72, TaskState.Passing, 2, null), true),
        ("hc-pending", new(TaskForm.Live, 64, TaskState.Pending, 2, null), true),
    ];

    public static int Run(string directory, string reportPath, GpuDevice device, OffscreenTarget target, BoardStyle[] styles, string[] themes,
        double scale, Localizer loc, System.Text.StringBuilder report, Action<List<List<BitmapSource>>, string, bool> sheet,
        Action<BitmapSource, string> save)
    {
        foreach (var style in styles)
        {
            foreach (var theme in themes)
            {
                var dark = !string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
                var rows = new List<List<BitmapSource>>();
                foreach (var size in BoardSizes.All)
                {
                    var row = new List<BitmapSource>();
                    foreach (var state in States)
                    {
                        var name = $"{BoardStyles.Key(style)} {(dark ? "dark" : "light")} {BoardSizes.Key(size)} {state.Name}";
                        var policy = new BoardEffectsPolicy(EffectsIntensity.Off, HighContrast: state.HighContrast);
                        using var rig = new BoardRig(style, size, BoardSimScenario.Team, policy, loc, dark: dark);
                        var pixels = (int)Math.Round(rig.Scene.Canvas.Extent * scale);
                        var now = Start;
                        while (rig.Ticks < 40)
                        {
                            rig.Advance(now);
                            now += Step;
                        }

                        var last = rig.LastTick! with { Events = [], NewPeak = false };
                        var climb = state.Name == "climb";
                        var from = climb ? state.Score with { Shown = 40 } : state.Score;
                        rig.Scene.Apply(last with { Gauge = last.Gauge with { Score = from } }, now);
                        rig.Scene.Settle();
                        if (climb)
                        {
                            rig.Scene.Apply(last with { Gauge = last.Gauge with { Score = state.Score } }, now);
                            now += 0.3;
                        }

                        rig.Frame.Invalidate();
                        if (!rig.Frame.Build(device, now, scale))
                        {
                            report.AppendLine(CultureInfo.InvariantCulture, $"{name}: the device failed");
                            continue;
                        }

                        byte[]? bytes = null;
                        for (var i = 0; i < 2; i++)
                        {
                            bytes = target.Render(device, view => rig.Frame.Draw(device, view, pixels), pixels, pixels);
                            device.Current.Context.Flush();
                        }

                        var gauge = rig.Scene.Gauge;
                        report.AppendLine(CultureInfo.InvariantCulture,
                            $"{name}: shown {gauge.Score.Text(now)} ({state.Score.Form}, {state.Score.State}, N {state.Score.Count}), proof {gauge.Score.Proof(now):0.00}, demand {rig.Scene.Demand(now)}, {rig.Frame.List.Count} instances, {rig.Frame.List.LayerCount} layers");
                        if (bytes is null)
                        {
                            continue;
                        }

                        var bitmap = OffscreenTarget.ToBitmap(bytes, pixels, pixels);
                        row.Add(bitmap);
                        save(bitmap, Path.Combine(directory,
                            $"scores-{BoardStyles.Key(style)}-{BoardSizes.Key(size)}-{(dark ? "dark" : "light")}-{state.Name}.png"));
                    }

                    rows.Add(row);
                    File.WriteAllText(reportPath, report.ToString());
                }

                sheet(rows, Path.Combine(directory, $"scores-{BoardStyles.Key(style)}-{(dark ? "dark" : "light")}.png"), dark);
            }
        }

        File.WriteAllText(reportPath, report.ToString());
        return 0;
    }
}
