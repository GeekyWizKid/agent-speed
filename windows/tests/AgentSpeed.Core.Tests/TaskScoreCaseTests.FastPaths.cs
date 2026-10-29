using AgentSpeed.Core.TaskParsers;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The port's fast paths against literal readings of the reference on seeded random input: the linear loop marks
/// against <c>loop_marks</c>' direct scan, and prefix evaluations (6.11 snapshots and fix rows share the task's acts)
/// against evaluations of the prefix's own events.
/// </summary>
public sealed partial class TaskScoreCaseTests
{
    [Fact]
    public void LoopMarksMatchTheDirectScan()
    {
        var random = new Random(61);
        for (var round = 0; round < 3000; round++)
        {
            var keys = new List<int>();
            var alphabet = random.Next(1, 5);
            var length = random.Next(0, 40);
            for (var i = 0; i < length; i++)
            {
                keys.Add(random.Next(alphabet));
            }

            for (var count = 0; count <= length; count++)
            {
                Assert.Equal(DirectLoopMarks(keys.GetRange(0, count)), TaskLoops.MarkedCount(keys, count));
            }
        }
    }

    [Fact]
    public void PrefixEvaluationsMatchFreshOnes()
    {
        var random = new Random(29);
        for (var round = 0; round < 300; round++)
        {
            var events = RandomStream(random, random.Next(1, 70));
            long? now = random.Next(3) == 0 ? null : events[^1].T - random.Next(0, 20_000);
            var full = TaskEvaluation.Of(events, now);
            var actEvents = new List<int>();
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i] is TaskAction && (now is null || events[i].T <= now))
                {
                    actEvents.Add(i);
                }
            }

            for (var k = 1; k <= actEvents.Count; k++)
            {
                var fresh = TaskEvaluation.Of(events.GetRange(0, actEvents[k - 1] + 1), now);
                var expected = TaskResult(fresh).ToJsonString();
                Assert.Equal(expected, TaskResult(full.Prefix(k)).ToJsonString());
            }
        }
    }

    /// <summary><c>loop_marks</c> of the reference, scanned directly, as the number of marked positions.</summary>
    private static int DirectLoopMarks(List<int> keys)
    {
        var n = keys.Count;
        var marked = new bool[n];
        for (var i = 0; i < n; i++)
        {
            for (var b = 1; b <= 3; b++)
            {
                if (i + (3 * b) > n)
                {
                    continue;
                }

                var repeats = 1;
                while (i + ((repeats + 1) * b) <= n && keys.GetRange(i + (repeats * b), b).SequenceEqual(keys.GetRange(i, b)))
                {
                    repeats++;
                }

                if (repeats >= 3)
                {
                    for (var p = i + b; p < i + (repeats * b); p++)
                    {
                        marked[p] = true;
                    }
                }
            }
        }

        return marked.Count(m => m);
    }

    /// <summary>A stream over small pools of keys, files, texts and fingerprints, so loops, reverts and repeats occur.</summary>
    private static List<TaskEvent> RandomStream(Random random, int length)
    {
        string Hex(int value) => value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        string[] tools = ["Read", "Grep", "Edit", "Bash"];
        string?[] suites = [null, "cargo test", "pytest"];
        ActionKind[] kinds = [ActionKind.Read, ActionKind.Search, ActionKind.Edit, ActionKind.Edit, ActionKind.Command, ActionKind.Command, ActionKind.Other];
        CommandClass[] classes = [CommandClass.Test, CommandClass.Test, CommandClass.Build, CommandClass.Check, CommandClass.Other];
        var digests = random.Next(1, 4);
        var events = new List<TaskEvent>();
        var t = 1_000_000_000_000L;
        for (var i = 0; i < length; i++)
        {
            t += random.Next(0, 4000);
            if (random.Next(14) == 0)
            {
                events.Add(new TaskPrompt(t, random.Next(2) == 0));
                continue;
            }

            var kind = kinds[random.Next(kinds.Length)];
            var command = kind == ActionKind.Command
                ? new TaskCommand(classes[random.Next(classes.Length)], suites[random.Next(suites.Length)], random.Next(4) == 0, Hex(300 + random.Next(3)))
                : null;
            List<EditHunk>? hunks = null;
            if (kind == ActionKind.Edit && random.Next(8) > 0)
            {
                hunks = [];
                for (var h = random.Next(0, 3); h > 0; h--)
                {
                    hunks.Add(new EditHunk(Hex(500 + random.Next(2)), random.Next(4) == 0 ? null : Hex(900 + random.Next(4)), Hex(900 + random.Next(4))));
                }
            }

            var action = new TaskAction(t, kind, tools[random.Next(tools.Length)], Hex(random.Next(digests)), command, hunks);
            if (random.Next(7) > 0)
            {
                bool?[] failed = [true, false, false, null];
                action.Outcome = new TaskOutcome(null, failed[random.Next(failed.Length)], random.Next(20) == 0 ? null : t + random.Next(0, 15_000));
                if (command is not null && random.Next(5) > 0)
                {
                    var fingerprints = Enumerable.Range(0, random.Next(0, 4)).Select(_ => Hex(700 + random.Next(5))).Distinct().Order(StringComparer.Ordinal).ToList();
                    long?[] counts = [null, 0, 1, 2, 5, 40];
                    long?[] passed = [null, 0, 3];
                    action.Failures = new FailureReport(fingerprints, counts[random.Next(counts.Length)], passed[random.Next(passed.Length)], null);
                }
            }

            events.Add(action);
        }

        return events;
    }
}
