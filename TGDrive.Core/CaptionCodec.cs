using System;
using System.Text;
using System.Text.Json;

namespace TGDrive.Core;

public sealed record FileMetadata(
    string VersionId,      // vid: base64url GUID (22 chars)
    string ParentVersionId, // pvid: base64url GUID or null for root
    string Sha256Hex,      // h: lowercase hex SHA256
    long Size,             // s: file size in bytes
    long MtimeUnix,        // m: modification time (Unix seconds)
    string DeviceId        // d: device identifier
);

public static class CaptionCodec
{
    public const int MaxCaptionUtf16Length = 1024;
    private const string Prefix = "#tgdrive1 ";

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
            f = meta.VersionId,
            i = meta.ParentVersionId,
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

        var vid = root.GetProperty("f").GetString() ?? throw new FormatException("Missing 'f' (versionId).");
        var pvid = root.GetProperty("i").GetString();
        var hash = root.GetProperty("h").GetString() ?? throw new FormatException("Missing 'h' (sha256).");
        var size = root.GetProperty("s").GetInt64();
        var mtime = root.GetProperty("m").GetInt64();
        var dev = root.GetProperty("d").GetString() ?? throw new FormatException("Missing 'd' (deviceId).");

        var meta = new FileMetadata(vid, pvid ?? "", hash, size, mtime, dev);
        return (path, meta);
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