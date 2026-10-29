namespace AgentSpeed.Core.Reporting;

public sealed record ReportExtensions(bool ModelHarnessRequests = false, bool ExecutionModes = false, bool ProfileAvatar = false, bool SharedIdentity = false,
    bool ReportWebSocket = false, bool TaskScore = false)
{
    /// <summary>The name of the task score extension (METRICS 11.10).</summary>
    public const string TaskScoreName = "taskScore";

    public bool HasProfile => ProfileAvatar || SharedIdentity;
    public ReportJson Json => ReportJson.Object(new[]
    {
        (Name: "modelHarnessRequests", On: ModelHarnessRequests), (Name: "executionModes", On: ExecutionModes),
        (Name: "profileAvatar", On: ProfileAvatar), (Name: "sharedIdentity", On: SharedIdentity),
        (Name: ReportWebSocketProtocol.Extension, On: ReportWebSocket), (Name: TaskScoreName, On: TaskScore),
    }.Where(e => e.On).Select(e => (e.Name, ReportJson.Of(1))));
}

public readonly record struct ReportModeKey(string ModelId, SourceKind Source, ExecutionModeKind Kind, string Mode, string Evidence, string? Provider);
