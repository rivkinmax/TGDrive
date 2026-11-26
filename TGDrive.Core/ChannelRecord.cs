using System.Text.Json;

namespace TGDrive.Core;

/// <summary>
/// Запись, распарсенная из сообщения канала.
/// RecordType: "f" (file), "d" (folder), "x" (tombstone), "r" (rebind), "s" (bind marker).
/// FileMeta задаётся для файлов; TdFileId — внутренний ID файла TDLib для скачивания.
/// </summary>
public class ChannelRecord
{
    public long MessageId { get; set; }
    public string RecordType { get; set; } = "";
    public string Path { get; set; } = "";
    public FileMetadata? FileMeta { get; set; }
    public int? TdFileId { get; set; }
    public JsonElement RawJson { get; set; }
}
