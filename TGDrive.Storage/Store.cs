using System;
using System.Collections.Generic;

namespace TGDrive.Storage;

public sealed class Store : IDisposable
{
    private readonly Db _db;
    private bool _disposed;

    public Store(string dbPath)
    {
        _db = new Db(dbPath);
    }

    public void SaveMessage(long messageId, string versionId, string path, string type, 
        string? parentVersionId, string? sha256, long? size, long? mtime, string? deviceId)
    {
        _db.Execute(@"
            INSERT OR REPLACE INTO messages 
            (message_id, version_id, path, type, parent_version_id, sha256, size, mtime, device_id, applied)
            VALUES (@message_id, @version_id, @path, @type, @parent_version_id, @sha256, @size, @mtime, @device_id, 0);",
            new Dictionary<string, object>
            {
                ["@message_id"] = messageId,
                ["@version_id"] = versionId,
                ["@path"] = path,
                ["@type"] = type,
                ["@parent_version_id"] = parentVersionId ?? (object)DBNull.Value,
                ["@sha256"] = sha256 ?? (object)DBNull.Value,
                ["@size"] = size ?? (object)DBNull.Value,
                ["@mtime"] = mtime ?? (object)DBNull.Value,
                ["@device_id"] = deviceId ?? (object)DBNull.Value
            });
    }

    public void Enqueue(string versionId, string path, string type)
    {
        _db.Execute(@"
            INSERT INTO queue (version_id, path, type, status, created_at)
            VALUES (@version_id, @path, @type, 'pending', @created_at);",
            new Dictionary<string, object>
            {
                ["@version_id"] = versionId,
                ["@path"] = path,
                ["@type"] = type,
                ["@created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _db.Dispose();
        _disposed = true;
    }
}