namespace AgentSpeed.Core.Tasks;

/// <summary>The kinds of line the withdrawal rule reads.</summary>
public enum SegmentLineKind
{
    Prompt,
    Command,
    CommandOutput,

    /// <summary>An assistant line or entry; <see cref="SegmentLine.Duplicate"/> when 4.1 or 4.7 step 2 ignored it.</summary>
    Assistant,
}

/// <summary>One classified line of an agent, with its time.</summary>
public readonly record struct SegmentLine(long T, SegmentLineKind Kind, bool Continuation = false, bool Duplicate = false);

/// <summary>
/// <c>segment</c> (METRICS 6.9 <i>Withdrawal</i>): a prompt or command takes effect at once and is withdrawn when the
/// first assistant line after it is a duplicate; a command is also withdrawn by a command output before that line.
/// </summary>
public static class Segmentation
{
    /// <summary>The prompt events of the lines, in order. A command is never a continuation.</summary>
    public static List<TaskPrompt> Segment(IEnumerable<SegmentLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var output = new List<TaskPrompt>();
        SegmentLineKind? pending = null;
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case SegmentLineKind.Prompt or SegmentLineKind.Command:
                    output.Add(new TaskPrompt(line.T, line.Kind == SegmentLineKind.Prompt && line.Continuation));
                    pending = line.Kind;
                    break;
                case SegmentLineKind.CommandOutput:
                    if (pending == SegmentLineKind.Command)
                    {
                        output.RemoveAt(output.Count - 1);
                    }

                    pending = null;
                    break;
                case SegmentLineKind.Assistant when pending is not null:
                    if (line.Duplicate)
                    {
                        output.RemoveAt(output.Count - 1);
                    }

                    pending = null;
                    break;
            }
        }

        return output;
    }
}
