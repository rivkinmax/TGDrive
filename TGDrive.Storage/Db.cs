using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace TGDrive.Storage;

public sealed class Db : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public Db(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        _connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Pooling=False;");
        _connection.Open();

        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=FULL;");
        Execute("PRAGMA foreign_keys=ON;");

        InitializeSchema();
        MigrateSchema();
    }

    private void InitializeSchema()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                message_id INTEGER PRIMARY KEY,
                version_id TEXT NOT NULL,
                file_id TEXT NOT NULL,
                path TEXT NOT NULL,
                path_lower TEXT NOT NULL,
                type TEXT NOT NULL,
                parent_version_id TEXT,
                sha256 TEXT,
                size INTEGER,
                mtime INTEGER,
                device_id TEXT,
                td_file_id INTEGER,
                applied INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_messages_path_lower ON messages(path_lower);
            CREATE INDEX IF NOT EXISTS idx_messages_version ON messages(version_id);
            CREATE INDEX IF NOT EXISTS idx_messages_file ON messages(file_id);

            CREATE TABLE IF NOT EXISTS queue (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                version_id TEXT NOT NULL,
                path TEXT NOT NULL,
                type TEXT NOT NULL,
                status TEXT DEFAULT 'pending',
                created_at INTEGER NOT NULL,
                payload TEXT,
                retry_count INTEGER DEFAULT 0,
                next_retry_at INTEGER,
                error_message TEXT
            );

            CREATE TABLE IF NOT EXISTS local_state (
                path TEXT PRIMARY KEY,
                path_lower TEXT NOT NULL,
                file_id TEXT,
                sha256 TEXT,
                size INTEGER,
                mtime INTEGER,
                is_folder INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_local_path_lower ON local_state(path_lower);

            CREATE TABLE IF NOT EXISTS base_snapshot (
                path TEXT PRIMARY KEY,
                path_lower TEXT NOT NULL,
                file_id TEXT,
                version_id TEXT,
                sha256 TEXT,
                size INTEGER,
                mtime INTEGER,
                is_folder INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_base_path_lower ON base_snapshot(path_lower);
        ");
    }

    private void MigrateSchema()
    {
        var columnsToAdd = new[] { "file_id TEXT", "path_lower TEXT", "td_file_id INTEGER" };
        foreach (var col in columnsToAdd)
        {
            try { Execute($"ALTER TABLE messages ADD COLUMN {col};"); }
            catch (SqliteException) { }
        }
        
        try { Execute("CREATE UNIQUE INDEX IF NOT EXISTS uq_messages_version ON messages(version_id) WHERE version_id != '';"); }
        catch (SqliteException) { }

        // Additive миграции для таблицы queue (для совместимости со старыми БД)
        var queueColumns = new[]
        {
            "payload TEXT",
            "retry_count INTEGER DEFAULT 0",
            "next_retry_at INTEGER",
            "error_message TEXT"
        };
        foreach (var col in queueColumns)
        {
            try { Execute($"ALTER TABLE queue ADD COLUMN {col};"); }
            catch (SqliteException) { }
        }
    }

    public void Execute(string sql, Dictionary<string, object>? parameters = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            }
        }
        cmd.ExecuteNonQuery();
    }

    public async Task<int> ExecuteAsync(string sql, Dictionary<string, object>? parameters = null, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (parameters != null)
        {
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
            }
        }
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public string? GetMeta(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES (@key, @value);";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    public async Task<string?> GetCurrentVersionIdAsync(string fileId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT version_id FROM messages WHERE file_id = @file_id AND type = 'f' ORDER BY message_id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@file_id", fileId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result == DBNull.Value ? null : result as string;
    }

    // === local_state аксессоры ===

    public async Task UpsertLocalAsync(string path, string? fileId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO local_state (path, path_lower, file_id, sha256, size, mtime, is_folder)
            VALUES (@path, @path_lower, @file_id, @sha256, @size, @mtime, @is_folder)
            ON CONFLICT(path) DO UPDATE SET
                file_id = excluded.file_id,
                sha256 = excluded.sha256,
                size = excluded.size,
                mtime = excluded.mtime,
                is_folder = excluded.is_folder;";

        var parameters = new Dictionary<string, object>
        {
            ["@path"] = path,
            ["@path_lower"] = path.ToLowerInvariant(),
            ["@file_id"] = (object?)fileId ?? DBNull.Value,
            ["@sha256"] = (object?)sha256 ?? DBNull.Value,
            ["@size"] = (object?)size ?? DBNull.Value,
            ["@mtime"] = (object?)mtime ?? DBNull.Value,
            ["@is_folder"] = isFolder ? 1 : 0
        };

        await ExecuteAsync(sql, parameters, ct).ConfigureAwait(false);
    }

    public async Task<(string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetLocalAsync(string path, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_id, sha256, size, mtime, is_folder FROM local_state WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : (long?)reader.GetInt64(2),
            reader.IsDBNull(3) ? null : (long?)reader.GetInt64(3),
            reader.GetInt32(4) == 1
        );
    }

    public async Task DeleteLocalAsync(string path, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM local_state WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<(string Path, string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllLocalAsync(CancellationToken ct = default)
    {
        var result = new List<(string, string?, string?, long?, long?, bool)>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT path, file_id, sha256, size, mtime, is_folder FROM local_state ORDER BY path;";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : (long?)reader.GetInt64(3),
                reader.IsDBNull(4) ? null : (long?)reader.GetInt64(4),
                reader.GetInt32(5) == 1
            ));
        }
        return result;
    }

    // === base_snapshot аксессоры ===

    public async Task UpsertBaseAsync(string path, string? fileId, string? versionId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO base_snapshot (path, path_lower, file_id, version_id, sha256, size, mtime, is_folder)
            VALUES (@path, @path_lower, @file_id, @version_id, @sha256, @size, @mtime, @is_folder)
            ON CONFLICT(path) DO UPDATE SET
                file_id = excluded.file_id,
                version_id = excluded.version_id,
                sha256 = excluded.sha256,
                size = excluded.size,
                mtime = excluded.mtime,
                is_folder = excluded.is_folder;";

        var parameters = new Dictionary<string, object>
        {
            ["@path"] = path,
            ["@path_lower"] = path.ToLowerInvariant(),
            ["@file_id"] = (object?)fileId ?? DBNull.Value,
            ["@version_id"] = (object?)versionId ?? DBNull.Value,
            ["@sha256"] = (object?)sha256 ?? DBNull.Value,
            ["@size"] = (object?)size ?? DBNull.Value,
            ["@mtime"] = (object?)mtime ?? DBNull.Value,
            ["@is_folder"] = isFolder ? 1 : 0
        };

        await ExecuteAsync(sql, parameters, ct).ConfigureAwait(false);
    }

    public async Task<(string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetBaseAsync(string path, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_id, version_id, sha256, size, mtime, is_folder FROM base_snapshot WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : (long?)reader.GetInt64(3),
            reader.IsDBNull(4) ? null : (long?)reader.GetInt64(4),
            reader.GetInt32(5) == 1
        );
    }

    public async Task DeleteBaseAsync(string path, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM base_snapshot WHERE path = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<List<(string Path, string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllBaseAsync(CancellationToken ct = default)
    {
        var result = new List<(string, string?, string?, string?, long?, long?, bool)>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT path, file_id, version_id, sha256, size, mtime, is_folder FROM base_snapshot ORDER BY path;";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            result.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : (long?)reader.GetInt64(4),
                reader.IsDBNull(5) ? null : (long?)reader.GetInt64(5),
                reader.GetInt32(6) == 1
            ));
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _connection.Close();
        _connection.Dispose();
        _disposed = true;
    }
}