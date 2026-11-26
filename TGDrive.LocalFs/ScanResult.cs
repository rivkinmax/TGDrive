using System.Collections.Generic;

namespace TGDrive.LocalFs;

/// <summary>
/// Результат сканирования локальной ФС.
/// </summary>
public sealed class ScanResult
{
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesSkipped { get; set; }
    public int FoldersAdded { get; set; }
    public int FoldersDeleted { get; set; }
    public List<ScanError> Errors { get; } = new();
}

public sealed class ScanError
{
    public string Path { get; init; } = "";
    public string Message { get; init; } = "";

    public override string ToString() => $"{Path}: {Message}";
}