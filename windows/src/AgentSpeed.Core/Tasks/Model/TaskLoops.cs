namespace AgentSpeed.Core.Tasks;

/// <summary>Loops and stalls over a task's counted actions (METRICS 6.9 <i>E</i>, 6.11 <c>stalled</c>).</summary>
internal static class TaskLoops
{
    private const int MaxBlock = 3;
    private const int Repeats = 3;
    private const int StallWindow = 8;

    /// <summary>
    /// <c>loop_marks</c> over the first <paramref name="count"/> counted keys, as the number of marked positions: every
    /// position covered by a block of 1–3 keys repeated at least 3 times back to back, the block's first occurrence
    /// excluded. Linear: the block at <c>i</c> repeats <c>r</c> times exactly when <c>keys[p] == keys[p − b]</c> for
    /// every <c>p</c> in <c>[i + b, i + r·b)</c>, so one backward pass per block size gives every maximal <c>r</c>, and
    /// the marked ranges are summed with a difference array — the same marks as the reference's direct scan.
    /// </summary>
    public static int MarkedCount(List<int> keys, int count)
    {
        if (count < Repeats)
        {
            return 0;
        }

        var depth = new int[count + 1];
        var run = new int[count + 1];
        for (var b = 1; b <= MaxBlock && count >= Repeats * b; b++)
        {
            run[count] = 0;
            for (var p = count - 1; p >= b; p--)
            {
                run[p] = keys[p] == keys[p - b] ? run[p + 1] + 1 : 0;
            }

            for (var i = 0; i <= count - (Repeats * b); i++)
            {
                var matched = run[i + b];
                if (matched < (Repeats - 1) * b)
                {
                    continue;
                }

                depth[i + b]++;
                depth[i + b + (matched / b * b)]--;
            }
        }

        var marked = 0;
        var open = 0;
        for (var p = 0; p < count; p++)
        {
            open += depth[p];
            if (open > 0)
            {
                marked++;
            }
        }

        return marked;
    }

    /// <summary>
    /// <c>stall_ends</c>: 1-based counted indices that end a window of 8 counted actions whose
    /// <c>Σ w·ΔI ≤ 0.25 × Σ w</c> (integers, so compared as <c>4·Σ w·ΔI ≤ Σ w</c>).
    /// </summary>
    public static List<int> StallEnds(List<TaskAct> counted)
    {
        var ends = new List<int>();
        var weighted = 0L;
        var total = 0L;
        for (var i = 0; i < counted.Count; i++)
        {
            weighted += counted[i].Weight * counted[i].Gain;
            total += counted[i].Weight;
            if (i >= StallWindow)
            {
                weighted -= counted[i - StallWindow].Weight * counted[i - StallWindow].Gain;
                total -= counted[i - StallWindow].Weight;
            }

            if (i >= StallWindow - 1 && 4 * weighted <= total)
            {
                ends.Add(i + 1);
            }
        }

        return ends;
    }
}
