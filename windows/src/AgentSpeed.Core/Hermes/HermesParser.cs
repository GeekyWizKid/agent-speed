using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Hermes;

internal readonly record struct HermesLine(ParsedResponse? Response, bool Malformed = false);

/// <summary>
/// Hermes CLI and desktop share these completed API-call usage records. The log multiplexes sessions; only usage
/// metadata is retained. Rounded zero latency retains usage without inventing a generation duration.
/// </summary>
internal sealed partial class HermesParser : TranscriptParser<HermesLine>
{
    public const int MaxLineBytes = 64 * 1024;

    private readonly string _logDirectory;
    private readonly TimeZoneInfo _timeZone;
    private readonly Dictionary<string, AgentInfo> _agents = new(StringComparer.Ordinal);

    public HermesParser(string path, IDeduper deduper, TimeZoneInfo? timeZone = null) : base(path, deduper, SourceKind.Hermes)
    {
        _logDirectory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? path;
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public IEnumerable<AgentInfo> Agents => _agents.Values;

    protected override HermesLine Extract(ReadOnlySpan<byte> line)
    {
        if (line.Length > MaxLineBytes || line.IndexOf("API call #"u8) < 0) return default;
        var match = UsagePattern().Match(Encoding.UTF8.GetString(line));
        if (!match.Success) return default;
        if (!DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss,fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local)
            || _timeZone.IsInvalidTime(local)
            || !Count(match, "input", out var input) || !Count(match, "output", out var output)
            || !Count(match, "total", out _)
            || !double.TryParse(match.Groups["latency"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds))
        {
            return new HermesLine(null, Malformed: true);
        }

        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _timeZone)).ToUnixTimeMilliseconds();
        long? start = seconds is > 0 and <= 900 ? end - (long)Math.Round(seconds * 1000) : null;
        var session = match.Groups["session"].Success ? match.Groups["session"].Value : null;
        var agent = _logDirectory + "#" + (session ?? "untagged");
        var suffix = match.Groups["suffix"].Value;
        var read = CacheReadPattern().Match(suffix);
        var write = CacheWritePattern().Match(suffix);
        var cached = read.Success ? Math.Min(input, BoundedCount(read.Groups[1].Value)) : 0;
        var written = write.Success ? Math.Min(input, BoundedCount(write.Groups[1].Value)) : 0;
        var key = "hermes:" + (session ?? _logDirectory) + ":" + end.ToString(CultureInfo.InvariantCulture) + ":" + match.Groups["call"].Value;
        return new HermesLine(new ParsedResponse(key, SourceKind.Hermes, agent, Known(match.Groups["model"].Value), start, end,
            new TokenCounts(Input: input, CachedInput: cached, CacheWriteInput: written, Output: output),
            Provider: Known(match.Groups["provider"].Value)));
    }

    public override void Apply(in HermesLine line, IResponseSink sink)
    {
        if (line.Malformed) CountMalformed();
        if (line.Response is not { } response) return;
        SetSource(SourceKind.Hermes);
        Touch(response.EndMs);
        if (!_agents.TryGetValue(response.AgentId, out var known) || known.LastLineMs <= response.EndMs)
        {
            _agents[response.AgentId] = new AgentInfo(response.AgentId, SourceKind.Hermes, null, response.Model,
                false, AgentPhase.Idle, null, response.EndMs);
            MarkChanged();
        }

        // Claim by file, as required by the history ledger; response/agent identities survive log rotation.
        if (Deduper.TryClaim(response.Key, AgentId, out _)) sink.OnResponse(response, isCompletion: true);
    }

    public override void Reset()
    {
        base.Reset();
        _agents.Clear();
    }

    private static bool Count(Match match, string group, out long value)
    {
        var text = match.Groups[group].Value;
        value = BoundedCount(text);
        return text.Length > 0 && text.All(char.IsAsciiDigit);
    }

    private static long BoundedCount(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
        ? TokenCounts.Clamp(count) : TokenCounts.MaxValue;

    private static string? Known(string value) => value == "unknown" ? null : value;

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) INFO(?:[ \t]*\[(?<session>[^\]\r\n]+)\])?[ \t]+(?:run_agent|agent\.conversation_loop|agent\.turn_usage): API call #(?<call>\d+): model=(?<model>\S+) provider=(?<provider>\S+) in=(?<input>\d+) out=(?<output>\d+) total=(?<total>\d+) latency=(?<latency>\d+(?:\.\d+)?)s(?<suffix>(?: .*)?)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex UsagePattern();

    [GeneratedRegex(@"(?:^| )cache=(\d+)/\d+ \(\d+%\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CacheReadPattern();

    [GeneratedRegex(@"(?:^| )write=(\d+)(?: |$)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CacheWritePattern();
}
