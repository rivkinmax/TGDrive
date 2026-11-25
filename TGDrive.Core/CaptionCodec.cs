using System;
using System.Text;
using System.Text.Json;

namespace TGDrive.Core;

/// <summary>
/// File version metadata. Three identifiers per REQUIREMENTS section 10:
/// FileId (f) = stable file identity, survives renames;
/// VersionId (i) = identity of this specific version;
/// ParentVersionId (p) = previous version id (empty for root).
/// </summary>
public sealed record FileMetadata(
    string FileId,
    string VersionId,
    string ParentVersionId,
    string Sha256Hex,
    long Size,
    long MtimeUnix,
    string DeviceId
);

public sealed record ServiceRecord(
    string Type,       // "d", "x", "r", "s"
    string Path,
    JsonElement Metadata
);

public static class CaptionCodec
{
    public const int MaxCaptionUtf16Length = 1024;
    public const string Prefix = "#tgdrive1 ";

    public static string GenerateNewId()
    {
        return Guid.NewGuid().ToBase64Url();
    }

    public static string EncodeFile(string normalizedPath, FileMetadata meta)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(normalizedPath));

        var json = JsonSerializer.Serialize(new
        {
            v = 1,
            t = "f",
            f = meta.FileId,
            i = meta.VersionId,
            p = meta.ParentVersionId,
            h = meta.Sha256Hex,
            s = meta.Size,
            m = meta.MtimeUnix,
            d = meta.DeviceId
        });

        var caption = normalizedPath + "\n" + Prefix + json;

        if (caption.Length > MaxCaptionUtf16Length)
            throw new InvalidOperationException("Caption exceeds 1024 UTF-16 limit.");

        return caption;
    }

    public static (string Path, FileMetadata Meta) DecodeFile(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("Caption cannot be null or whitespace.", nameof(caption));

        var lines = caption.Split('\n');
        if (lines.Length != 2)
            throw new FormatException("Caption must have exactly 2 lines.");

        var path = lines[0].Trim();
        var secondLine = lines[1].Trim();

        if (!secondLine.StartsWith(Prefix))
            throw new FormatException("Second line must start with '#tgdrive1 '.");

        var jsonStr = secondLine.Substring(Prefix.Length);
        using var doc = JsonDocument.Parse(jsonStr);
        var root = doc.RootElement;

        var version = root.GetProperty("v").GetInt32();
        if (version != 1)
            throw new FormatException("Unsupported version: " + version);

        var type = root.GetProperty("t").GetString();
        if (type != "f")
            throw new FormatException("Expected type 'f' (file), got: " + type);

        var fid = root.GetProperty("f").GetString() ?? throw new FormatException("Missing 'f' (fileId).");
        var vid = root.GetProperty("i").GetString() ?? throw new FormatException("Missing 'i' (versionId).");
        var pvid = root.TryGetProperty("p", out var pEl) && pEl.ValueKind == JsonValueKind.String
            ? pEl.GetString() ?? ""
            : "";
        var hash = root.GetProperty("h").GetString() ?? throw new FormatException("Missing 'h' (sha256).");
        var size = root.GetProperty("s").GetInt64();
        var mtime = root.GetProperty("m").GetInt64();
        var dev = root.GetProperty("d").GetString() ?? throw new FormatException("Missing 'd' (deviceId).");

        var meta = new FileMetadata(fid, vid, pvid, hash, size, mtime, dev);
        return (path, meta);
    }

    /// <summary>
    /// Try to parse a service record (folder/tombstone/rebind/bind-marker).
    /// Input is the full message text: "#tgdrive1 {json}". Path is in field "p".
    /// Returns null if not a valid service record.
    /// </summary>
    public static ServiceRecord? TryDecodeServiceRecord(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith(Prefix)) return null;

        var jsonStr = trimmed.Substring(Prefix.Length);
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.Number) return null;
            if (vEl.GetInt32() != 1) return null;

            if (!root.TryGetProperty("t", out var tEl) || tEl.ValueKind != JsonValueKind.String) return null;
            var type = tEl.GetString() ?? "";
            if (type != "d" && type != "x" && type != "r" && type != "s") return null;

            if (!root.TryGetProperty("p", out var pEl) || pEl.ValueKind != JsonValueKind.String) return null;
            var path = pEl.GetString() ?? "";

            return new ServiceRecord(type, path, root.Clone());
        }
        catch
        {
            return null;
        }
    }
}

public static class GuidExtensions
{
    public static string ToBase64Url(this Guid guid)
    {
        var bytes = guid.ToByteArray();
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static Guid FromBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        return new Guid(bytes);
    }
}
