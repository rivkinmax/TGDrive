using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TGDrive.Core;

public interface IStore
{
    // === Remote messages (existing) ===
    Task<bool> SaveRemoteMessageAsync(ChannelRecord record, CancellationToken ct = default);
    Task<string?> GetCurrentVersionIdAsync(string fileId, CancellationToken ct = default);
    
    // === Queue (existing) ===
    Task EnqueueAsync(string versionId, string path, string type, CancellationToken ct = default);
    
    // === Local state (new) ===
    Task UpsertLocalAsync(string path, string? fileId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default);
    Task<(string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetLocalAsync(string path, CancellationToken ct = default);
    Task DeleteLocalAsync(string path, CancellationToken ct = default);
    Task<List<(string Path, string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllLocalAsync(CancellationToken ct = default);
    
    // === Base snapshot (new) ===
    Task UpsertBaseAsync(string path, string? fileId, string? versionId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default);
    Task<(string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetBaseAsync(string path, CancellationToken ct = default);
    Task DeleteBaseAsync(string path, CancellationToken ct = default);
    Task<List<(string Path, string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllBaseAsync(CancellationToken ct = default);
}