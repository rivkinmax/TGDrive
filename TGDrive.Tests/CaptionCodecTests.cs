using System;
using System.Text.Json;
using Xunit;
using TGDrive.Core;

namespace TGDrive.Tests;

public class CaptionCodecTests
{
    private const string Hash = "da40d46abbd778384d27e91b37328e4b103ec516996db7031c0b5239dae72526";

    [Fact]
    public void GenerateNewId_Returns22Chars()
    {
        var id = CaptionCodec.GenerateNewId();
        Assert.Equal(22, id.Length);
        Assert.DoesNotContain("=", id);
        Assert.DoesNotContain("+", id);
        Assert.DoesNotContain("/", id);
    }

    [Fact]
    public void EncodeDecodeFile_RoundTrip()
    {
        var path = "folder/file.txt";
        var meta = new FileMetadata(
            FileId: CaptionCodec.GenerateNewId(),
            VersionId: CaptionCodec.GenerateNewId(),
            ParentVersionId: CaptionCodec.GenerateNewId(),
            Sha256Hex: Hash,
            Size: 25,
            MtimeUnix: 1700000000,
            DeviceId: "dev1"
        );

        var caption = CaptionCodec.EncodeFile(path, meta);
        var (decodedPath, decodedMeta) = CaptionCodec.DecodeFile(caption);

        Assert.Equal(path, decodedPath);
        Assert.Equal(meta, decodedMeta);
    }

    [Fact]
    public void EncodeFile_JsonFieldMapping_MatchesSpec()
    {
        var fid = CaptionCodec.GenerateNewId();
        var vid = CaptionCodec.GenerateNewId();
        var pvid = CaptionCodec.GenerateNewId();
        var meta = new FileMetadata(fid, vid, pvid, Hash, 25, 1700000000, "dev1");

        var caption = CaptionCodec.EncodeFile("folder/file.txt", meta);
        var lines = caption.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("folder/file.txt", lines[0]);
        Assert.StartsWith("#tgdrive1 ", lines[1]);

        using var doc = JsonDocument.Parse(lines[1].Substring("#tgdrive1 ".Length));
        var r = doc.RootElement;
        Assert.Equal(1, r.GetProperty("v").GetInt32());
        Assert.Equal("f", r.GetProperty("t").GetString());
        Assert.Equal(fid, r.GetProperty("f").GetString());
        Assert.Equal(vid, r.GetProperty("i").GetString());
        Assert.Equal(pvid, r.GetProperty("p").GetString());
        Assert.Equal(Hash, r.GetProperty("h").GetString());
        Assert.Equal(25, r.GetProperty("s").GetInt64());
        Assert.Equal(1700000000, r.GetProperty("m").GetInt64());
        Assert.Equal("dev1", r.GetProperty("d").GetString());
    }

    [Fact]
    public void EncodeDecodeFile_RootVersion_EmptyPvid()
    {
        var meta = new FileMetadata(
            CaptionCodec.GenerateNewId(), CaptionCodec.GenerateNewId(), "",
            Hash, 10, 1700000000, "dev1");

        var caption = CaptionCodec.EncodeFile("root.txt", meta);
        var (_, decoded) = CaptionCodec.DecodeFile(caption);
        Assert.Equal("", decoded.ParentVersionId);
    }

    [Fact]
    public void EncodeFile_ExceedsLimit_Throws()
    {
        var longPath = new string('a', 1000);
        var meta = new FileMetadata(
            CaptionCodec.GenerateNewId(), CaptionCodec.GenerateNewId(), "",
            Hash, 25, 1700000000, "dev1");

        var ex = Assert.Throws<InvalidOperationException>(() => CaptionCodec.EncodeFile(longPath, meta));
        Assert.Equal("Caption exceeds 1024 UTF-16 limit.", ex.Message);
    }

    [Fact]
    public void DecodeFile_InvalidFormat_Throws()
    {
        Assert.Throws<FormatException>(() => CaptionCodec.DecodeFile("single line"));
        Assert.Throws<FormatException>(() => CaptionCodec.DecodeFile("path\ninvalid prefix"));
    }

    [Fact]
    public void TryDecodeServiceRecord_Folder_OK()
    {
        var rec = CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {\"v\":1,\"t\":\"d\",\"p\":\"docs/reports\"}");
        Assert.NotNull(rec);
        Assert.Equal("d", rec!.Type);
        Assert.Equal("docs/reports", rec.Path);
    }

    [Fact]
    public void TryDecodeServiceRecord_Rebind_OK()
    {
        var rec = CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {\"v\":1,\"t\":\"r\",\"p\":\"new.txt\",\"o\":\"old.txt\",\"k\":\"f\"}");
        Assert.NotNull(rec);
        Assert.Equal("r", rec!.Type);
        Assert.Equal("new.txt", rec.Path);
        Assert.Equal("old.txt", rec.Metadata.GetProperty("o").GetString());
    }

    [Fact]
    public void TryDecodeServiceRecord_ForeignOrInvalid_Null()
    {
        Assert.Null(CaptionCodec.TryDecodeServiceRecord("hello"));
        Assert.Null(CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {bad json"));
        Assert.Null(CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {\"v\":2,\"t\":\"d\",\"p\":\"x\"}"));
        Assert.Null(CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {\"v\":1,\"t\":\"f\",\"p\":\"x\"}"));
        Assert.Null(CaptionCodec.TryDecodeServiceRecord("#tgdrive1 {\"v\":1,\"t\":\"d\"}"));
    }
}
