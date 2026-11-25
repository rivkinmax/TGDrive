using System;
using System.IO;
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
                path TEXT NOT NULL,
                type TEXT NOT NULL,
                parent_version_id TEXT,
                sha256 TEXT,
                size INTEGER,
                mtime INTEGER,
                device_id TEXT,
                applied INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_messages_path ON messages(path);
            CREATE INDEX IF NOT EXISTS idx_messages_version ON messages(version_id);

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

    public void Dispose()
    {
        if (_disposed) return;
        _connection.Close();
        _connection.Dispose();
        _disposed = true;
    }
}