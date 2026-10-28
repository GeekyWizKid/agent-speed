using System.Buffers;
using System.Text;
using System.Text.Json;
using AgentSpeed.Core.IO.Sqlite;
using AgentSpeed.Core.Tasks;

namespace AgentSpeed.Core.History;

internal sealed partial class HistoryStore
{
    /// <summary>
    /// The task records of every stored file (METRICS 6.11 <i>Store</i>), created whenever the store is opened; the schema
    /// version does not change for it, so older builds still open the file and ignore it.
    /// </summary>
    private const string TaskRecordsSchema = """
        CREATE TABLE IF NOT EXISTS task_records (
            file_id INTEGER NOT NULL, ordinal INTEGER NOT NULL, source TEXT NOT NULL, model TEXT, effort TEXT,
            start_ms INTEGER NOT NULL, end_ms INTEGER NOT NULL, day TEXT NOT NULL, complete INTEGER NOT NULL,
            state TEXT NOT NULL, score REAL, v REAL NOT NULL, c REAL NOT NULL, r REAL NOT NULL, e REAL NOT NULL,
            k REAL NOT NULL, counts TEXT NOT NULL, first_pass_ms INTEGER, success INTEGER NOT NULL,
            stalled INTEGER NOT NULL, snapshots TEXT NOT NULL, fix_rows TEXT NOT NULL, version INTEGER NOT NULL,
            PRIMARY KEY (file_id, ordinal)) WITHOUT ROWID;
        """;

    /// <summary>The keys of <c>counts</c>, in the order of <see cref="TaskCounts"/>.</summary>
    private static readonly byte[][] CountKeys =
    [
        "actions"u8.ToArray(), "counted"u8.ToArray(), "edits"u8.ToArray(), "oscillations"u8.ToArray(), "loopActions"u8.ToArray(),
        "runs"u8.ToArray(), "failingRuns"u8.ToArray(), "episodes"u8.ToArray(), "recovered"u8.ToArray(),
        "recoverAttempts"u8.ToArray(), "fixAttempts"u8.ToArray(), "fixFailures"u8.ToArray(),
    ];

    /// <summary>
    /// Reads the records whose version is their file's (METRICS 6.11 <i>Reading</i>), in ordinal order; the agent id is
    /// the file's path. A malformed row is left out.
    /// </summary>
    private void LoadTaskRecords(Dictionary<long, StoredFile> files)
    {
        var byFile = new Dictionary<long, List<TaskRecord>>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var rows = _connection.Prepare(
            "SELECT t.file_id, t.ordinal, t.source, t.model, t.effort, t.start_ms, t.end_ms, t.day, t.complete, t.state, t.score, " +
            "t.v, t.c, t.r, t.e, t.k, t.counts, t.first_pass_ms, t.success, t.stalled, t.snapshots, t.fix_rows " +
            "FROM task_records t JOIN files f ON f.id = t.file_id WHERE t.version = f.version ORDER BY t.file_id, t.ordinal"))
        {
            while (rows.Step())
            {
                if (!files.TryGetValue(rows.GetInt64(0), out var file) || ReadTaskRecord(rows, file.Path, names) is not { } record)
                {
                    continue;
                }

                if (!byFile.TryGetValue(file.Id, out var list))
                {
                    byFile[file.Id] = list = [];
                }

                list.Add(record);
            }
        }

        foreach (var (id, list) in byFile)
        {
            files[id].TaskRecords = [.. list];
        }
    }

    /// <summary>Replaces a file's task records by its new ones, inside the caller's transaction (METRICS 6.11 <i>Writes</i>).</summary>
    private void SaveTaskRecords(StoredFile file)
    {
        using var delete = _connection.Prepare("DELETE FROM task_records WHERE file_id = ?1");
        delete.Bind(1, file.Id).Run();
        if (file.TaskRecords.Count == 0)
        {
            return;
        }

        using var insert = _connection.Prepare(
            "INSERT INTO task_records (file_id, ordinal, source, model, effort, start_ms, end_ms, day, complete, state, score, " +
            "v, c, r, e, k, counts, first_pass_ms, success, stalled, snapshots, fix_rows, version) " +
            "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14, ?15, ?16, ?17, ?18, ?19, ?20, ?21, ?22, ?23)");
        var json = new ArrayBufferWriter<byte>(256);
        foreach (var record in file.TaskRecords)
        {
            if (!IsStorable(record))
            {
                continue;
            }

            insert.Bind(1, file.Id).Bind(2, record.Ordinal).Bind(3, SourceNames.Of(record.Source)).Bind(4, record.Model)
                .Bind(5, record.Effort).Bind(6, record.StartMs).Bind(7, record.EndMs).Bind(8, record.Day)
                .Bind(9, record.Complete ? 1L : 0L).Bind(10, TaskStateNames.Name(record.State))
                .Bind(12, record.Dims.V).Bind(13, record.Dims.C).Bind(14, record.Dims.R).Bind(15, record.Dims.E).Bind(16, record.Dims.K)
                .Bind(17, CountsJson(record.Counts, json)).Bind(19, record.Success ? 1L : 0L).Bind(20, record.Stalled ? 1L : 0L)
                .Bind(21, RowsJson(record.Snapshots, json)).Bind(22, RowsJson(record.FixRows, json)).Bind(23, file.Version);
            BindNullable(insert, 11, record.Score);
            BindNullable(insert, 18, record.FirstPassMs);
            insert.Run();
        }
    }

    private static TaskRecord? ReadTaskRecord(SqliteStatement row, string agentId, Dictionary<string, string> names)
    {
        var ordinal = row.GetInt64(1);
        if (ordinal is < 0 or > int.MaxValue
            || SourceNames.Parse(row.GetString(2)) is not { } source
            || TaskStateNames.Parse(row.GetString(9)) is not { } state
            || row.GetString(7) is not { } day
            || ParseCounts(row.GetUtf8(16)) is not { } counts
            || ParseRows(row.GetUtf8(20)) is not { } snapshots
            || ParseRows(row.GetUtf8(21)) is not { } fixRows)
        {
            return null;
        }

        return new TaskRecord
        {
            AgentId = agentId,
            Source = source,
            Day = Shared(day, names),
            Ordinal = (int)ordinal,
            StartMs = row.GetInt64(5),
            EndMs = row.GetInt64(6),
            Complete = row.GetInt64(8) != 0,
            Model = row.GetString(3) is { } model ? Shared(model, names) : null,
            Effort = row.GetString(4) is { } effort ? Shared(effort, names) : null,
            State = state,
            Score = row.IsNull(10) ? null : row.GetDouble(10),
            Dims = new TaskDims(row.GetDouble(11), row.GetDouble(12), row.GetDouble(13), row.GetDouble(14), row.GetDouble(15)),
            Counts = counts,
            FirstPassMs = row.GetNullableInt64(17),
            Success = row.GetInt64(18) != 0,
            Stalled = row.GetInt64(19) != 0,
            Snapshots = snapshots,
            FixRows = fixRows,
        };
    }

    private static void BindNullable(SqliteStatement statement, int index, double? value)
    {
        if (value is { } number)
        {
            statement.Bind(index, number);
        }
        else
        {
            statement.Bind(index, (string?)null);
        }
    }

    private static void BindNullable(SqliteStatement statement, int index, long? value)
    {
        if (value is { } number)
        {
            statement.Bind(index, number);
        }
        else
        {
            statement.Bind(index, (string?)null);
        }
    }

    /// <summary>One instance per distinct model, effort and day text, so many records share them.</summary>
    private static string Shared(string text, Dictionary<string, string> names)
    {
        if (names.TryGetValue(text, out var shared))
        {
            return shared;
        }

        names[text] = text;
        return text;
    }

    /// <summary>Every number finite: SQLite and JSON have no other doubles.</summary>
    private static bool IsStorable(TaskRecord record)
    {
        if (record.Score is { } score && !double.IsFinite(score))
        {
            return false;
        }

        var dims = record.Dims;
        if (!double.IsFinite(dims.V) || !double.IsFinite(dims.C) || !double.IsFinite(dims.R) || !double.IsFinite(dims.E)
            || !double.IsFinite(dims.K))
        {
            return false;
        }

        return AllFinite(record.Snapshots) && AllFinite(record.FixRows);

        static bool AllFinite(IReadOnlyList<double[]> rows)
        {
            foreach (var row in rows)
            {
                foreach (var value in row ?? [])
                {
                    if (!double.IsFinite(value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    private static string CountsJson(TaskCounts counts, ArrayBufferWriter<byte> buffer)
    {
        int[] values =
        [
            counts.Actions, counts.Counted, counts.Edits, counts.Oscillations, counts.LoopActions, counts.Runs, counts.FailingRuns,
            counts.Episodes, counts.Recovered, counts.RecoverAttempts, counts.FixAttempts, counts.FixFailures,
        ];
        buffer.ResetWrittenCount();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            for (var i = 0; i < values.Length; i++)
            {
                writer.WriteNumber(CountKeys[i], values[i]);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A JSON array of number arrays; doubles in their shortest round-trip form.</summary>
    private static string RowsJson(IReadOnlyList<double[]> rows, ArrayBufferWriter<byte> buffer)
    {
        buffer.ResetWrittenCount();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartArray();
                foreach (var value in row ?? [])
                {
                    writer.WriteNumberValue(value);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The twelve counts; null unless the text is one object holding each of them as a 32-bit integer.</summary>
    private static TaskCounts? ParseCounts(ReadOnlySpan<byte> json)
    {
        Span<int> values = stackalloc int[CountKeys.Length];
        var seen = 0;
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var index = -1;
                for (var i = 0; i < CountKeys.Length && index < 0; i++)
                {
                    if (reader.ValueTextEquals(CountKeys[i]))
                    {
                        index = i;
                    }
                }

                if (!reader.Read())
                {
                    return null;
                }

                if (index < 0)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out values[index]))
                {
                    return null;
                }

                seen |= 1 << index;
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen != (1 << CountKeys.Length) - 1)
            {
                return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return new TaskCounts(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8],
            values[9], values[10], values[11]);
    }

    /// <summary>The rows of an array of number arrays; null for any other text.</summary>
    private static double[][]? ParseRows(ReadOnlySpan<byte> json)
    {
        var rows = new List<double[]>();
        var row = new List<double>(9);
        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
            {
                return null;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
            {
                row.Clear();
                while (reader.Read() && reader.TokenType == JsonTokenType.Number)
                {
                    if (!reader.TryGetDouble(out var value) || !double.IsFinite(value))
                    {
                        return null;
                    }

                    row.Add(value);
                }

                if (reader.TokenType != JsonTokenType.EndArray)
                {
                    return null;
                }

                rows.Add([.. row]);
            }

            if (reader.TokenType != JsonTokenType.EndArray || reader.Read())
            {
                return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return rows.Count == 0 ? [] : [.. rows];
    }
}
