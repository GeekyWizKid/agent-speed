using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>Counters of a composer, for dumps and diagnostics only.</summary>
public sealed class TaskComposerStats
{
    public int Lines { get; internal set; }

    public int Prompts { get; internal set; }

    public int Withdrawn { get; internal set; }

    public int Calls { get; internal set; }

    public int Results { get; internal set; }

    public int OrphanResults { get; internal set; }

    public int Stitched { get; internal set; }

    public int ContinuationResults { get; internal set; }

    public int Scripts { get; internal set; }

    public int ScriptsMapped { get; internal set; }
}

/// <summary>
/// The composer of one agent (METRICS 6.9 <i>Composing the stream</i>), fed every line right after the source handler,
/// with the line's <see cref="TaskLine"/> and whether the handler left the line's key to another file: it withdraws
/// copied prompts, keeps one action per call id, matches results to calls anywhere in the agent, stitches running
/// sessions back to their command and notes the model and effort after each line. Not thread-safe (the monitor or
/// history thread). A live composer keeps only the current task's events and the calls that can still change them.
/// </summary>
public sealed class TaskComposer
{
    private readonly bool _keepAll;
    private readonly bool _concatenates;
    private readonly List<TaskEvent> _events = [];
    private readonly List<TaskModelNote> _notes = [];
    private readonly Dictionary<string, Entry> _calls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Running> _sessions = new(StringComparer.Ordinal);
    private readonly List<string> _stale = [];
    private (bool Command, TaskPrompt Prompt)? _pending;

    /// <param name="keepAll">Keep every task's events (history, dumps); otherwise only the current task's.</param>
    internal TaskComposer(SourceKind source, bool keepAll)
    {
        Source = source;
        _keepAll = keepAll;
        _concatenates = source == SourceKind.Codex;
    }

    public SourceKind Source { get; }

    /// <summary>The event stream in file order (only the current task's for a live composer).</summary>
    public IReadOnlyList<TaskEvent> Events => _events;

    /// <summary>The model and effort notes (METRICS 6.9 <i>Model and effort</i>); kept only when every event is.</summary>
    public IReadOnlyList<TaskModelNote> Notes => _notes;

    public TaskComposerStats Stats { get; } = new();

    /// <summary>Model and effort notes are kept (every task's events are: the history and the dumps).</summary>
    public bool KeepsNotes => _keepAll;

    /// <summary>Changes whenever an event is added, removed or completed.</summary>
    public int Revision { get; private set; }

    /// <summary>Forgets everything (the file was replaced and is read again from its start).</summary>
    public void Reset()
    {
        _events.Clear();
        _notes.Clear();
        _calls.Clear();
        _sessions.Clear();
        _pending = null;
        Revision++;
    }

    /// <summary>
    /// One line the composer counts, at <paramref name="t"/>: its facts (null for a line that adds nothing), whether its
    /// assistant entry is a duplicate, whether its calls are suppressed (an OpenClaw mirror), and the agent's model and
    /// effort after it.
    /// </summary>
    internal void Feed(TaskLine? line, long t, bool assistant, bool duplicate, bool suppressCalls, string? model, string? effort)
    {
        Stats.Lines++;
        if (line is not null || assistant)
        {
            Compose(line, t, assistant, duplicate, suppressCalls);
        }

        if (_keepAll)
        {
            Note(t, model, effort);
        }
    }

    private void Compose(TaskLine? line, long t, bool assistant, bool duplicate, bool suppressCalls)
    {
        if (line?.Prompt is { } prompt)
        {
            OnPrompt(prompt, t);
        }

        if (line?.Notice is { } notice)
        {
            Finish(notice.Session, new TaskOutcome(notice.ExitCode, notice.ExitCode != 0, null), null, notice.T);
        }

        foreach (var result in line?.Results ?? [])
        {
            OnResult(result, t);
        }

        if (assistant)
        {
            if (_pending is not null && duplicate)
            {
                Withdraw();
            }

            _pending = null;
        }

        if (line?.Calls is { } calls && !duplicate && !suppressCalls)
        {
            foreach (var call in calls)
            {
                OnCall(call);
            }
        }

        TrimToCurrentTask();
    }

    // -- prompts and withdrawal (6.9 Tasks)
    private void OnPrompt(PromptLine prompt, long t)
    {
        var at = prompt.T ?? t;
        switch (prompt.Kind)
        {
            case PromptLineKind.Prompt or PromptLineKind.Command:
                var added = new TaskPrompt(at, prompt.Kind == PromptLineKind.Prompt && prompt.Continuation);
                Add(added);
                Stats.Prompts++;
                _pending = Source == SourceKind.Codex ? null : (prompt.Kind == PromptLineKind.Command, added);
                break;
            case PromptLineKind.CommandOutput:
                if (_pending is { Command: true })
                {
                    Withdraw();
                }

                _pending = null;
                break;
        }
    }

    private void Withdraw()
    {
        var prompt = _pending!.Value.Prompt;
        var index = _events.LastIndexOf(prompt);
        if (index >= 0)
        {
            _events.RemoveAt(index);
            Revision++;
        }

        Stats.Withdrawn++;
        Stats.Prompts--;
    }

    // -- calls and results (6.10 Actions, TASK-PARSERS 8)
    private void OnCall(TaskCall call)
    {
        if (_calls.ContainsKey(call.Id))
        {
            return;
        }

        if (call.Script is { } script)
        {
            var subs = new Entry[script.Count];
            for (var i = 0; i < subs.Length; i++)
            {
                subs[i] = new Entry(script[i]);
                Add(script[i].Action!);
                Stats.Calls++;
            }

            _calls[call.Id] = new Entry(call) { Subs = subs };
            Stats.Scripts++;
            return;
        }

        _calls[call.Id] = new Entry(call);
        Add(call.Action!);
        Stats.Calls++;
    }

    private void OnResult(TaskResult result, long t)
    {
        if (!_calls.TryGetValue(result.Id, out var entry))
        {
            Stats.OrphanResults++;
            return;
        }

        if (entry.Subs is { } subs)
        {
            _calls.Remove(result.Id);
            foreach (var sub in subs)
            {
                sub.Answered = true;
            }

            if (result.ScriptAnswers is { Count: > 0 } answers)
            {
                Stats.ScriptsMapped++;
                foreach (var (index, answer) in answers)
                {
                    if (index < subs.Length)
                    {
                        Answer(subs[index], answer, t);
                    }
                }
            }

            return;
        }

        if (entry.Answered || result.Answer is not { } single)
        {
            Stats.OrphanResults++;
            return;
        }

        Answer(entry, single, t);
    }

    private void Answer(Entry entry, TaskAnswer answer, long t)
    {
        entry.Answered = true;
        Stats.Results++;
        var call = entry.Call!;
        var action = call.Action!;

        // Only the id's mark stays for repeats: the call's command analysis and cwd go once it is answered.
        entry.Call = null;
        action.Outcome = answer.Outcome;
        if (answer.Session is { } session)
        {
            _sessions[session] = new Running(action, call.Info, call.Cwd, answer.Masked, answer.Text ?? string.Empty);
        }
        else if (call.Info is not null)
        {
            action.Failures = answer.Failures;
        }

        Revision++;
        if (call.Continues is { } key && _sessions.ContainsKey(key))
        {
            Stats.ContinuationResults++;
            Finish(key, answer.Outcome, answer.Text ?? string.Empty, t);
        }
    }

    /// <summary>A continuation's result or a notice for a running session: output joins, an exit code ends it (4.3).</summary>
    private void Finish(string key, TaskOutcome outcome, string? text, long t)
    {
        if (!_sessions.TryGetValue(key, out var running))
        {
            return;
        }

        if (text is not null)
        {
            running.Texts.Add(text);
        }

        if (outcome.ExitCode is not { } code || outcome.Failed is not { } failed)
        {
            return;
        }

        var final = new TaskOutcome(code, running.Masked ? null : failed, t);
        running.Action.Outcome = final;
        if (running.Info is { } info)
        {
            var joined = string.Join(_concatenates ? string.Empty : "\n", running.Texts);
            running.Action.Failures = TaskParser.Failures(info, joined, running.Cwd, final.Failed);
        }

        _sessions.Remove(key);
        Stats.Stitched++;
        Revision++;
    }

    private void Add(TaskEvent e)
    {
        _events.Add(e);
        Revision++;
    }

    /// <summary>A live composer drops the events before the current task once no withdrawal can bring them back.</summary>
    private void TrimToCurrentTask()
    {
        if (_keepAll || _pending is not null || _events.Count < 2)
        {
            return;
        }

        var start = -1;
        for (var i = _events.Count - 1; i > 0; i--)
        {
            if (_events[i] is TaskPrompt { Continuation: false })
            {
                start = i;
                break;
            }
        }

        if (start <= 0)
        {
            return;
        }

        _events.RemoveRange(0, start);

        // A finished task's answered calls only guard against repeats of their ids, which come with the call's own
        // message; their results are in. Calls still waiting stay, since their results complete the earlier task.
        var current = new HashSet<TaskEvent>(_events, ReferenceEqualityComparer.Instance);
        _stale.Clear();
        foreach (var (id, entry) in _calls)
        {
            if (entry.Answered && entry.Action is { } action && !current.Contains(action))
            {
                _stale.Add(id);
            }
        }

        foreach (var id in _stale)
        {
            _calls.Remove(id);
        }
    }

    // -- model and effort after each line (6.11 Task records)
    private void Note(long t, string? model, string? effort)
    {
        if (_notes.Count > 0)
        {
            var last = _notes[^1];
            if (string.Equals(last.Model, model, StringComparison.Ordinal) && string.Equals(last.Effort, effort, StringComparison.Ordinal)
                && t >= last.T)
            {
                return;
            }
        }

        _notes.Add(new TaskModelNote(t, model, effort));
    }

    private sealed class Entry(TaskCall call)
    {
        /// <summary>The call until it is answered.</summary>
        public TaskCall? Call { get; set; } = call;

        public TaskAction? Action { get; } = call.Action;

        public bool Answered { get; set; }

        public Entry[]? Subs { get; init; }
    }

    private sealed class Running(TaskAction action, CommandInfo? info, string? cwd, bool masked, string text)
    {
        public TaskAction Action { get; } = action;

        public CommandInfo? Info { get; } = info;

        public string? Cwd { get; } = cwd;

        public bool Masked { get; } = masked;

        public List<string> Texts { get; } = [text];
    }
}
