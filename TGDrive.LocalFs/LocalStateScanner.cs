using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TGDrive.Core;

namespace TGDrive.LocalFs;

/// <summary>
/// Сканер локальной файловой системы. Строит и поддерживает актуальный снимок
/// в таблице local_state через IStore. Идемпотентен, устойчив к ошибкам доступа.
/// Согласно REQUIREMENTS.txt разделы 7, 8, 10.
/// </summary>
public sealed class LocalStateScanner
{
    private readonly IStore _store;
    private readonly PathFilter _filter;

    public LocalStateScanner(IStore store, PathFilter filter)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }

    /// <summary>
    /// Выполнить полное сканирование корня синхронизации.
    /// </summary>
    /// <param name="rootPath">Абсолютный путь к корневой папке синхронизации.</param>
    /// <param name="progress">Опциональный коллбэк прогресса.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат сканирования со статистикой и ошибками.</returns>
    public async Task<ScanResult> ScanAsync(string rootPath, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be null or whitespace.", nameof(rootPath));

        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Root directory not found: {rootPath}");

        var result = new ScanResult();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        int processed = 0;

        // === Проход 1: сканирование ФС ===

        // Папки (включая корень — но корень пропускаем, он не часть облака)
        try
        {
            foreach (var dirPath in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                var relativePath = PathNormalizer.Normalize(Path.GetRelativePath(rootPath, dirPath));
                var dirName = Path.GetFileName(dirPath);

                if (_filter.ShouldExclude(relativePath))
                {
                    result.FilesSkipped++;
                    continue;
                }

                seenPaths.Add(relativePath);

                var existing = await _store.GetLocalAsync(relativePath, ct).ConfigureAwait(false);
                if (existing == null)
                {
                    await _store.UpsertLocalAsync(relativePath, null, null, 0, 0, isFolder: true, ct).ConfigureAwait(false);
                    result.FoldersAdded++;
                }

                progress?.Report(new ScanProgress
                {
                    CurrentPath = relativePath,
                    ProcessedCount = processed,
                    Stage = "Scanning"
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            result.Errors.Add(new ScanError { Path = rootPath, Message = $"Directory enumeration failed: {ex.Message}" });
        }

        // Файлы
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                string relativePath;
                try
                {
                    relativePath = PathNormalizer.Normalize(Path.GetRelativePath(rootPath, filePath));
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new ScanError { Path = filePath, Message = $"Path normalization failed: {ex.Message}" });
                    continue;
                }

                if (_filter.ShouldExclude(relativePath))
                {
                    result.FilesSkipped++;
                    continue;
                }

                seenPaths.Add(relativePath);

                FileInfo info;
                try
                {
                    info = new FileInfo(filePath);
                    if (!info.Exists)
                    {
                        result.FilesSkipped++;
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Errors.Add(new ScanError { Path = filePath, Message = $"Cannot access file: {ex.Message}" });
                    continue;
                }

                var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                var size = info.Length;

                var existing = await _store.GetLocalAsync(relativePath, ct).ConfigureAwait(false);

                if (existing == null)
                {
                    // Новый файл — вычисляем SHA256
                    string? sha256 = null;
                    try
                    {
                        sha256 = await FileHasher.ComputeSha256Async(filePath, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        result.Errors.Add(new ScanError { Path = filePath, Message = $"Cannot hash file: {ex.Message}" });
                        continue;
                    }

                    await _store.UpsertLocalAsync(relativePath, null, sha256, size, mtime, isFolder: false, ct).ConfigureAwait(false);
                    result.FilesAdded++;
                }
                else
                {
                    // Существующий файл — проверяем изменения по mtime/size
                    if (existing.Value.Mtime != mtime || existing.Value.Size != size)
{
    string? sha256 = null;
    try
    {
        sha256 = await FileHasher.ComputeSha256Async(filePath, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        result.Errors.Add(new ScanError { Path = filePath, Message = $"Cannot hash file: {ex.Message}" });
        continue;
    }

    await _store.UpsertLocalAsync(relativePath, existing.Value.FileId, sha256, size, mtime, isFolder: false, ct).ConfigureAwait(false);
    result.FilesUpdated++;
}
                }

                progress?.Report(new ScanProgress
                {
                    CurrentPath = relativePath,
                    ProcessedCount = processed,
                    Stage = "Scanning"
                });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            result.Errors.Add(new ScanError { Path = rootPath, Message = $"File enumeration failed: {ex.Message}" });
        }

        // === Проход 2: выявление удалённых объектов ===

        progress?.Report(new ScanProgress { Stage = "Detecting deletions", ProcessedCount = processed });

        var allLocal = await _store.GetAllLocalAsync(ct).ConfigureAwait(false);
        foreach (var entry in allLocal)
        {
            ct.ThrowIfCancellationRequested();

            if (!seenPaths.Contains(entry.Path))
            {
                await _store.DeleteLocalAsync(entry.Path, ct).ConfigureAwait(false);
                if (entry.IsFolder)
                    result.FoldersDeleted++;
                else
                    result.FilesDeleted++;
            }
        }

        progress?.Report(new ScanProgress { Stage = "Complete", ProcessedCount = processed });

        return result;
    }
}