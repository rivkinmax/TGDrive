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
                created_at INTEGER NOT NULL
            );
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

    public void Dispose()
    {
        if (_disposed) return;
        _connection.Close();
        _connection.Dispose();
        _disposed = true;
    }
}
