namespace AgentSpeed.Core.Work;

/// <summary>One action of an agent (METRICS 6.10): when, which tool, the digest of its input and whether it failed.</summary>
/// <param name="Failed">Null while the outcome is unknown, which is not a failure.</param>
internal readonly record struct WorkAction(long AtMs, string Tool, ulong Digest, bool? Failed = null)
{
    public bool SameCall(in WorkAction other) => Digest == other.Digest && string.Equals(Tool, other.Tool, StringComparison.Ordinal);
}
