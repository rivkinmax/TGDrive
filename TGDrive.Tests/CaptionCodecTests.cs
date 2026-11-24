using System;
using Xunit;
using TGDrive.Core;

namespace TGDrive.Tests;

public class CaptionCodecTests
{
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
            VersionId: CaptionCodec.GenerateNewId(),
            ParentVersionId: CaptionCodec.GenerateNewId(),
            Sha256Hex: "da40d46abbd778384d27e91b37328e4b103ec516996db7031c0b5239dae72526",
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
    public void EncodeFile_ExceedsLimit_Throws()
    {
        var longPath = new string('a', 1000);
        var meta = new FileMetadata(
            VersionId: CaptionCodec.GenerateNewId(),
            ParentVersionId: "",
            Sha256Hex: "da40d46abbd778384d27e91b37328e4b103ec516996db7031c0b5239dae72526",
            Size: 25,
            MtimeUnix: 1700000000,
            DeviceId: "dev1"
        );

        var ex = Assert.Throws<InvalidOperationException>(() => CaptionCodec.EncodeFile(longPath, meta));
        Assert.Equal("Caption exceeds 1024 UTF-16 limit.", ex.Message);
    }

    [Fact]
    public void DecodeFile_InvalidFormat_Throws()
    {
        Assert.Throws<FormatException>(() => CaptionCodec.DecodeFile("single line"));
        Assert.Throws<FormatException>(() => CaptionCodec.DecodeFile("path\ninvalid prefix"));
    }
}