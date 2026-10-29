using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.Pi;

/// <summary>Hands a Pi or OpenClaw entry to the composer's file side (METRICS 6.9 <i>Composing the stream</i>).</summary>
internal static class PiTaskFacts
{
    public static void Read(TaskLineReader? reader, ReadOnlySpan<byte> raw, ref PiLine line)
    {
        if (reader is not PiFamilyTaskReader pi)
        {
            return;
        }

        var role = line.Role switch
        {
            PiRole.User => PiEntryRole.User,
            PiRole.Assistant => PiEntryRole.Assistant,
            PiRole.ToolResult => PiEntryRole.ToolResult,
            _ => PiEntryRole.Other,
        };
        var facts = new PiEntryFacts(line.Status == LineStatus.Valid, line.Kind == PiEntryKind.Session, line.Kind == PiEntryKind.Message,
            line.Id, line.TimestampMs, line.Cwd, role, line.Call.IsGuardedMessage, line.StartedMs, line.ToolCallIds is { Length: > 0 });
        line.Task = pi.Read(raw, in facts, out var tracked);
        line.TaskTracked = tracked;
    }
}
