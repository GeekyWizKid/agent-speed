using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.OpenClaw;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Tasks;

/// <summary>One log to read whole for its task streams: a file of a family, or an OpenClaw agent database.</summary>
internal readonly record struct TaskLog(string Path, SourceKind Source, string Root = "");

/// <summary>
/// Reads logs whole, in the given order, with one dedupe set and every task's events kept (METRICS 6.9 <i>End to end</i>,
/// the dumps of 6.11): the agents of the scored sources with their composers. Responses are discarded.
/// </summary>
internal static class TaskAgentsReader
{
    public static List<TranscriptParser> Read(IEnumerable<TaskLog> logs, IDeduper deduper, CancellationToken cancellationToken = default)
    {
        var agents = new List<TranscriptParser>();
        var sink = new NullSink();
        foreach (var log in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (log.Source == SourceKind.OpenClaw)
            {
                var windows = OpenClawSource.ReadAll(log.Path, deduper, _ => sink, cancellationToken: cancellationToken);
                agents.AddRange((IEnumerable<TranscriptParser>?)windows ?? []);
                continue;
            }

            if (FamilyOf(log.Source) is not { } family)
            {
                continue;
            }

            var parser = Families.CreateParser(family, log.Path, deduper, log.Root);
            parser.KeepEveryTask();
            var tailer = Families.CreateTailer(family, log.Path);
            try
            {
                tailer.StartAt(0);
                var batch = parser.NewBatch();
                parser.PrepareBatch(batch, 0);
                tailer.ReadAppended(batch, cancellationToken);
                batch.ApplyTo(sink);
            }
            finally
            {
                (tailer as IDisposable)?.Dispose();
            }

            if (parser.Tasks is not null)
            {
                agents.Add(parser);
            }
        }

        return agents;
    }

    /// <summary>6.7 parent links of the agents read (OpenClaw windows link inside their database, 4.14).</summary>
    public static Dictionary<string, string> Parents(IReadOnlyCollection<TranscriptParser> agents)
    {
        var parents = ParentLinks.Resolve([.. agents.Where(a => a is not OpenClawParser).Select(AgentLink.Of)], StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (agent is OpenClawParser { ParentId: { } parent })
            {
                parents[agent.AgentId] = parent;
            }
        }

        return parents;
    }

    public static RootFamily? FamilyOf(SourceKind source) => source switch
    {
        SourceKind.Codex => RootFamily.Codex,
        SourceKind.ClaudeCode or SourceKind.ClaudeDesktop => RootFamily.Claude,
        SourceKind.Omp => RootFamily.Omp,
        SourceKind.Pi => RootFamily.Pi,
        SourceKind.Droid => RootFamily.Droid,
        _ => null,
    };

    private sealed class NullSink : IResponseSink
    {
        public void OnResponse(ParsedResponse response, bool isCompletion)
        {
        }
    }
}
