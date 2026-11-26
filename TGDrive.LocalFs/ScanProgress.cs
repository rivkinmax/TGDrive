namespace TGDrive.LocalFs;

/// <summary>
/// Прогресс сканирования локальной ФС для UI.
/// </summary>
public sealed class ScanProgress
{
    public string CurrentPath { get; init; } = "";
    public int ProcessedCount { get; init; }
    public string Stage { get; init; } = "";
}