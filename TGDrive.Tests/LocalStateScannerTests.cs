using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TGDrive.Core;
using TGDrive.LocalFs;
using Xunit;

namespace TGDrive.Tests;

/// <summary>
/// In-memory реализация IStore для изолированных unit-тестов LocalStateScanner.
/// </summary>
internal sealed class FakeStore : IStore
{
    private readonly Dictionary<string, (string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)> _local = new();
    private readonly Dictionary<string, (string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)> _base = new();

    public Task<bool> SaveRemoteMessageAsync(ChannelRecord record, CancellationToken ct = default) => Task.FromResult(true);
    public Task<string?> GetCurrentVersionIdAsync(string fileId, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task EnqueueAsync(string versionId, string path, string type, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpsertLocalAsync(string path, string? fileId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        // Preserve existing FileId if not provided (имитируем SQL ON CONFLICT DO UPDATE, где file_id не обнуляется)
        if (_local.TryGetValue(path, out var existing))
        {
            _local[path] = (fileId ?? existing.FileId, sha256, size, mtime, isFolder);
        }
        else
        {
            _local[path] = (fileId, sha256, size, mtime, isFolder);
        }
        return Task.CompletedTask;
    }

    public Task<(string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetLocalAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult<(string?, string?, long?, long?, bool)?>(
            _local.TryGetValue(path, out var v) ? v : null);
    }

    public Task DeleteLocalAsync(string path, CancellationToken ct = default)
    {
        _local.Remove(path);
        return Task.CompletedTask;
    }

    public Task<List<(string Path, string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllLocalAsync(CancellationToken ct = default)
    {
        var list = new List<(string, string?, string?, long?, long?, bool)>();
        foreach (var kv in _local)
            list.Add((kv.Key, kv.Value.FileId, kv.Value.Sha256, kv.Value.Size, kv.Value.Mtime, kv.Value.IsFolder));
        return Task.FromResult(list);
    }

    public Task UpsertBaseAsync(string path, string? fileId, string? versionId, string? sha256, long? size, long? mtime, bool isFolder, CancellationToken ct = default)
    {
        _base[path] = (fileId, versionId, sha256, size, mtime, isFolder);
        return Task.CompletedTask;
    }

    public Task<(string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)?> GetBaseAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult<(string?, string?, string?, long?, long?, bool)?>(
            _base.TryGetValue(path, out var v) ? v : null);
    }

    public Task DeleteBaseAsync(string path, CancellationToken ct = default)
    {
        _base.Remove(path);
        return Task.CompletedTask;
    }

    public Task<List<(string Path, string? FileId, string? VersionId, string? Sha256, long? Size, long? Mtime, bool IsFolder)>> GetAllBaseAsync(CancellationToken ct = default)
    {
        var list = new List<(string, string?, string?, string?, long?, long?, bool)>();
        foreach (var kv in _base)
            list.Add((kv.Key, kv.Value.FileId, kv.Value.VersionId, kv.Value.Sha256, kv.Value.Size, kv.Value.Mtime, kv.Value.IsFolder));
        return Task.FromResult(list);
    }

    // Для тестов
    public bool HasLocal(string path) => _local.ContainsKey(path);
    public int LocalCount => _local.Count;
    public (string? FileId, string? Sha256, long? Size, long? Mtime, bool IsFolder) GetLocalRaw(string path) => _local[path];
}

public class LocalStateScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeStore _store;
    private readonly PathFilter _filter;
    private readonly LocalStateScanner _scanner;

    public LocalStateScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tgdrive_scanner_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new FakeStore();
        _filter = new PathFilter();
        _scanner = new LocalStateScanner(_store, _filter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsEmptyResult()
    {
        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(0, result.FilesAdded);
        Assert.Equal(0, result.FilesUpdated);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, result.FoldersAdded);
        Assert.Equal(0, result.FoldersDeleted);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ScanAsync_SingleFile_AddsToLocalState()
    {
        var filePath = Path.Combine(_tempDir, "hello.txt");
        File.WriteAllText(filePath, "hello");

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FilesAdded);
        Assert.True(_store.HasLocal("hello.txt"));

        var entry = _store.GetLocalRaw("hello.txt");
        Assert.Equal(5, entry.Size);
        Assert.False(entry.IsFolder);
        Assert.NotNull(entry.Sha256);
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", entry.Sha256);
    }

    [Fact]
    public async Task ScanAsync_NestedFolder_AddsBothFolderAndFile()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file.txt"), "content");

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FoldersAdded);
        Assert.Equal(1, result.FilesAdded);
        Assert.True(_store.HasLocal("sub"));
        Assert.True(_store.HasLocal("sub/file.txt"));

        var folder = _store.GetLocalRaw("sub");
        Assert.True(folder.IsFolder);

        var file = _store.GetLocalRaw("sub/file.txt");
        Assert.False(file.IsFolder);
    }

    [Fact]
    public async Task ScanAsync_ExcludedPattern_SkipsFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(_tempDir, "skip.tmp"), "skip");
        File.WriteAllText(Path.Combine(_tempDir, "Thumbs.db"), "thumbs");

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FilesAdded);
        Assert.Equal(2, result.FilesSkipped);
        Assert.True(_store.HasLocal("keep.txt"));
        Assert.False(_store.HasLocal("skip.tmp"));
        Assert.False(_store.HasLocal("Thumbs.db"));
    }

    [Fact]
    public async Task ScanAsync_ExcludedFolder_SkipsEntireTree()
    {
        _filter.AddFolderName("node_modules");
        var nm = Path.Combine(_tempDir, "node_modules", "pkg");
        Directory.CreateDirectory(nm);
        File.WriteAllText(Path.Combine(nm, "index.js"), "js");

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(0, result.FoldersAdded);
        Assert.Equal(0, result.FilesAdded);
        Assert.False(_store.HasLocal("node_modules"));
    }

        [Fact]
    public async Task ScanAsync_ModifiedFile_UpdatesSha256()
    {
        var path = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(path, "version1");

        await _scanner.ScanAsync(_tempDir);
        var hash1 = _store.GetLocalRaw("data.txt").Sha256;

        // Сначала пишем новый контент, ЗАТЕМ явно выставляем mtime в будущем,
        // чтобы unix-timestamp (секундная гранулярность) гарантированно отличался.
        File.WriteAllText(path, "version2");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(0, result.FilesAdded);
        Assert.Equal(1, result.FilesUpdated);

        var hash2 = _store.GetLocalRaw("data.txt").Sha256;
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ScanAsync_DeletedFile_RemovedFromLocalState()
    {
        var path = Path.Combine(_tempDir, "todelete.txt");
        File.WriteAllText(path, "content");

        await _scanner.ScanAsync(_tempDir);
        Assert.True(_store.HasLocal("todelete.txt"));

        File.Delete(path);

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(_store.HasLocal("todelete.txt"));
    }

    [Fact]
    public async Task ScanAsync_DeletedFolder_RemovedFromLocalState()
    {
        var subDir = Path.Combine(_tempDir, "temp");
        Directory.CreateDirectory(subDir);

        await _scanner.ScanAsync(_tempDir);
        Assert.True(_store.HasLocal("temp"));

        Directory.Delete(subDir);

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FoldersDeleted);
        Assert.False(_store.HasLocal("temp"));
    }

    [Fact]
    public async Task ScanAsync_Idempotent_SecondScanChangesNothing()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        File.WriteAllText(Path.Combine(_tempDir, "sub", "c.txt"), "c");

        await _scanner.ScanAsync(_tempDir);
        var result2 = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(0, result2.FilesAdded);
        Assert.Equal(0, result2.FilesUpdated);
        Assert.Equal(0, result2.FilesDeleted);
        Assert.Equal(0, result2.FoldersAdded);
        Assert.Equal(0, result2.FoldersDeleted);
    }

    [Fact]
    public async Task ScanAsync_UnicodeNames_HandledCorrectly()
    {
        File.WriteAllText(Path.Combine(_tempDir, "документ.txt"), "привет");

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FilesAdded);
        Assert.True(_store.HasLocal("документ.txt"));
    }

    [Fact]
    public async Task ScanAsync_CyrillicSubfolder_NormalizedPath()
    {
        var subDir = Path.Combine(_tempDir, "папка");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "файл.txt"), "тест");

        await _scanner.ScanAsync(_tempDir);

        Assert.True(_store.HasLocal("папка"));
        Assert.True(_store.HasLocal("папка/файл.txt"));
    }

    [Fact]
    public async Task ScanAsync_NonExistentRoot_ThrowsDirectoryNotFoundException()
    {
        var fakeRoot = Path.Combine(_tempDir, "does_not_exist");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _scanner.ScanAsync(fakeRoot));
    }

    [Fact]
    public async Task ScanAsync_PreservesFileId_OnUpdate()
    {
        // Предзаполним local_state с file_id (имитация: файл уже был синхронизирован)
        await _store.UpsertLocalAsync("keep.txt", "FILEID123", "oldhash", 3, 1000, false);
        File.WriteAllText(Path.Combine(_tempDir, "keep.txt"), "new");
        File.SetLastWriteTimeUtc(Path.Combine(_tempDir, "keep.txt"), DateTime.UtcNow.AddSeconds(1));

        var result = await _scanner.ScanAsync(_tempDir);

        Assert.Equal(1, result.FilesUpdated);
        var entry = _store.GetLocalRaw("keep.txt");
        Assert.Equal("FILEID123", entry.FileId); // file_id сохранён
    }

    [Fact]
    public async Task ScanAsync_ProgressCallback_Invoked()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "b");

        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(p => reports.Add(p));

        await _scanner.ScanAsync(_tempDir, progress);

        // Даём время на доставку асинхронных Report
        await Task.Delay(100);

        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.Stage == "Complete");
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        for (int i = 0; i < 50; i++)
            File.WriteAllText(Path.Combine(_tempDir, $"file_{i}.txt"), "data");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _scanner.ScanAsync(_tempDir, null, cts.Token));
    }
}