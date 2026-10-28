import Foundation

extension HistoryDatabase {
    /// Additive, like execution modes: older builds can continue using the same history store.
    static let agentTableSQL = """
        CREATE TABLE IF NOT EXISTS contribution_agents (
            file_id INTEGER NOT NULL,
            day TEXT NOT NULL,
            source TEXT NOT NULL,
            agent_hash INTEGER NOT NULL,
            PRIMARY KEY (file_id, day, source, agent_hash)
        ) WITHOUT ROWID;
        """

    func loadAgents(into entries: inout [String: Entry], paths: [Int64: String]) throws {
        let query = try connection.prepare("SELECT file_id, day, source, agent_hash FROM contribution_agents")
        try query.forEachRow { row in
            guard let path = paths[row.int64(0)], let day = row.string(1), row.string(2) == SourceKind.hermes.rawValue else { return }
            let key = UsageKey(day: day, source: .hermes)
            entries[path]?.days[key]?.agentHashes.insert(UInt64(bitPattern: row.int64(3)))
        }
    }

    func deleteAgents(fileId: Int64) throws {
        let query = try connection.prepare("DELETE FROM contribution_agents WHERE file_id = ?1")
        query.bind(1, fileId)
        try query.run()
    }

    func saveAgents(_ hashes: Set<UInt64>, fileId: Int64, day: String, source: SourceKind) throws {
        guard source == .hermes, !hashes.isEmpty else { return }
        let query = try connection.prepare("INSERT INTO contribution_agents(file_id, day, source, agent_hash) VALUES (?1, ?2, ?3, ?4)")
        for hash in hashes.sorted() {
            query.bind(1, fileId)
            query.bind(2, day)
            query.bind(3, source.rawValue)
            query.bind(4, Int64(bitPattern: hash))
            try query.run()
        }
    }
}
