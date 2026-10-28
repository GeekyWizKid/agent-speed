using System.Buffers.Binary;

namespace AgentSpeed.Core.History;

internal sealed partial class HistoryStore
{
    // Additive, like execution-mode contributions: existing stores and other sources keep their scalar agent count.
    private const string HermesAgentsSchema = """
        CREATE TABLE IF NOT EXISTS contribution_hermes_agents (
            file_id INTEGER NOT NULL, day TEXT NOT NULL, hashes BLOB NOT NULL,
            PRIMARY KEY (file_id,day)) WITHOUT ROWID;
        """;

    private void LoadHermesAgents(Dictionary<long, StoredFile> files)
    {
        using var rows = _connection.Prepare("SELECT file_id,day,hashes FROM contribution_hermes_agents");
        while (rows.Step())
        {
            if (!files.TryGetValue(rows.GetInt64(0), out var file) || rows.GetString(1) is not { } day
                || !file.Days.TryGetValue(new DayKey(day, SourceKind.Hermes), out var aggregate)) continue;
            var blob = rows.GetBlob(2);
            if (blob.Length % sizeof(ulong) != 0) continue;
            aggregate.HermesAgentHashes = [];
            for (var offset = 0; offset < blob.Length; offset += sizeof(ulong))
            {
                aggregate.HermesAgentHashes.Add(BinaryPrimitives.ReadUInt64LittleEndian(blob.Slice(offset, sizeof(ulong))));
            }

            aggregate.Agents = aggregate.HermesAgentHashes.Count;
        }
    }

    private void SaveHermesAgents(StoredFile file)
    {
        using var delete = _connection.Prepare("DELETE FROM contribution_hermes_agents WHERE file_id=?1");
        delete.Bind(1, file.Id).Run();
        using var insert = _connection.Prepare("INSERT INTO contribution_hermes_agents(file_id,day,hashes) VALUES(?1,?2,?3)");
        foreach (var (day, aggregate) in file.Days)
        {
            if (day.Source != SourceKind.Hermes || aggregate.HermesAgentHashes is not { } hashes) continue;
            insert.Bind(1, file.Id).Bind(2, day.Day).BindBlob(3, KeyBlob([.. hashes.Order()])).Run();
        }
    }
}
