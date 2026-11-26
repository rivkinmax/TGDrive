using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TGDrive.Core;

namespace TGDrive.Storage;

public sealed class Store : IStore
{
    private readonly Db _db;

    public Store(Db db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<bool> SaveRemoteMessageAsync(ChannelRecord record, CancellationToken ct = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        string pathLower = record.Path.ToLowerInvariant();
        long? tdFileId = record.TdFileId;
        
        string? fileId = null;
        string? versionId = null;
        string? parentVersionId = null;
        string? sha256 = null;
        long? size = null;
        long? mtime = null;
        string? deviceId = null;

        if (record.FileMeta != null)
        {
            fileId = record.FileMeta.FileId;
            versionId = record.FileMeta.VersionId;
            parentVersionId = record.FileMeta.ParentVersionId;
            sha256 = record.FileMeta.Sha256Hex;
            size = record.FileMeta.Size;
            mtime = record.FileMeta.MtimeUnix;
            deviceId = record.FileMeta.DeviceId;
        }

        const string sql = @"
            INSERT OR IGNORE INTO messages 
            (message_id, version_id, file_id, path, path_lower, type, parent_version_id, sha256, size, mtime, device_id, td_file_id, applied)
            VALUES 
            (@message_id, @version_id, @file_id, @path, @path_lower, @type, @parent_version_id, @sha256, @size, @mtime, @device_id, @td_file_id, 0);";

        var parameters = new Dictionary<string, object>
        {
            ["@message_id"] = record.MessageId,
            ["@version_id"] = versionId ?? "",
            ["@file_id"] = fileId ?? "",
            ["@path"] = record.Path,
            ["@path_lower"] = pathLower,
            ["@type"] = record.RecordType,
            ["@parent_version_id"] = parentVersionId ?? "",
            ["@sha256"] = sha256 ?? "",
            ["@size"] = size ?? 0,
            ["@mtime"] = mtime ?? 0,
            ["@device_id"] = deviceId ?? "",
            ["@td_file_id"] = tdFileId ?? 0
        };

        int rowsAffected = await _db.ExecuteAsync(sql, parameters, ct).ConfigureAwait(false);
        return rowsAffected > 0;
    }

    public async Task<string?> GetCurrentVersionIdAsync(string fileId, CancellationToken ct = default)
    {
        return await _db.GetCurrentVersionIdAsync(fileId, ct).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(string versionId, string path, string type, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO queue (version_id, path, type, status, created_at)
            VALUES (@version_id, @path, @type, 'pending', @created_at);";
        
        var parameters = new Dictionary<string, object>
        {
            ["@version_id"] = versionId,
            ["@path"] = path,
            ["@type"] = type,
            ["@created_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await _db.ExecuteAsync(sql, parameters, ct).ConfigureAwait(false);
    }
    // === Local state ===

    public async Task UpsertLocalAsync(string path, string? fileId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        await _db.UpsertLocalAsync(path, fileId, sha256, size, mtime, isFolder, ct).ConfigureAwait(false);
    }

    public async Task<(string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetLocalAsync(string path, CancellationToken ct = default)
    {
        return await _db.GetLocalAsync(path, ct).ConfigureAwait(false);
    }

    public async Task DeleteLocalAsync(string path, CancellationToken ct = default)
    {
        await _db.DeleteLocalAsync(path, ct).ConfigureAwait(false);
    }

    public async Task<List<(string Path, string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllLocalAsync(CancellationToken ct = default)
    {
        return await _db.GetAllLocalAsync(ct).ConfigureAwait(false);
    }

    // === Base snapshot ===

    public async Task UpsertBaseAsync(string path, string? fileId, string? versionId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        await _db.UpsertBaseAsync(path, fileId, versionId, sha256, size, mtime, isFolder, ct).ConfigureAwait(false);
    }

    public async Task<(string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetBaseAsync(string path, CancellationToken ct = default)
    {
        return await _db.GetBaseAsync(path, ct).ConfigureAwait(false);
    }

    public async Task DeleteBaseAsync(string path, CancellationToken ct = default)
    {
        await _db.DeleteBaseAsync(path, ct).ConfigureAwait(false);
    }

    public async Task<List<(string Path, string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllBaseAsync(CancellationToken ct = default)
    {
        return await _db.GetAllBaseAsync(ct).ConfigureAwait(false);
    }
}
