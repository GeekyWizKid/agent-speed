namespace AgentSpeed.Core.TaskParsers;

/// <summary>A summary's family (TASK-PARSERS 5.3): tests, or errors of a compiler or checker.</summary>
internal enum FailureFamily
{
    Test,
    Error,
}

/// <summary>How a framework's summaries combine (TASK-PARSERS 5.4): only the last counts, or all add up.</summary>
internal enum FailureMode
{
    Last,
    Sum,
}

/// <summary>One summary line's counts (TASK-PARSERS 5.3); a null passed count means unknown.</summary>
internal readonly record struct FailureSummary(string Framework, FailureFamily Family, long Failed, long? Passed, FailureMode Mode);

/// <summary>
/// What the recognisers of one output emit (TASK-PARSERS 5.3): the canonical strings of failures in output order
/// (5.7) and the summaries.
/// </summary>
internal sealed class FailureFound(string? cwd)
{
    public List<string> Canonical { get; } = [];

    public List<FailureSummary> Summaries { get; } = [];

    /// <summary>A failing test: its masked name is the code, the message is empty.</summary>
    public void Test(string framework, string name, string file = "") =>
        Add(framework, "test", file, FailureMask.MaskName(name, cwd), "");

    public void Error(string framework, string file, string code, string message) =>
        Add(framework, "error", file, code, message);

    public void Summary(string framework, FailureFamily family, long failed, long? passed, FailureMode mode = FailureMode.Last) =>
        Summaries.Add(new FailureSummary(framework, family, failed, passed, mode));

    private void Add(string framework, string kind, string file, string code, string message)
    {
        var normFile = file.Length > 0 ? FailureMask.NormFile(file, cwd) : "";
        var masked = message.Length > 0 ? FailureMask.Mask(message, cwd) : "";
        Canonical.Add(string.Join('|', framework, kind, normFile, code, masked));
    }
}
