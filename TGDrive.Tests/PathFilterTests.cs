using Xunit;
using TGDrive.LocalFs;

namespace TGDrive.Tests;

public class PathFilterTests
{
    private readonly PathFilter _filter = new();

    [Theory]
    [InlineData("document.tmp")]
    [InlineData("temp.temp")]
    [InlineData("~$report.docx")]
    [InlineData("~tempfile.xlsx")]
    [InlineData("desktop.ini")]
    [InlineData("Thumbs.db")]
    [InlineData(".DS_Store")]
    [InlineData(".tgdrive-abc123.part")]
    public void ShouldExclude_DefaultPatterns_ReturnsTrue(string fileName)
    {
        var path = $"folder/{fileName}";
        Assert.True(_filter.ShouldExclude(path));
    }

    [Theory]
    [InlineData("document.docx")]
    [InlineData("image.jpg")]
    [InlineData("data.json")]
    [InlineData("readme.txt")]
    public void ShouldExclude_NormalFiles_ReturnsFalse(string fileName)
    {
        var path = $"folder/{fileName}";
        Assert.False(_filter.ShouldExclude(path));
    }

    [Fact]
    public void ShouldExclude_CustomFolder_ReturnsTrue()
    {
        _filter.AddFolderName("node_modules");
        Assert.True(_filter.ShouldExclude("node_modules/package.json"));
        Assert.True(_filter.ShouldExclude("project/node_modules/file.js"));
    }

    [Fact]
    public void ShouldExclude_RemovedFolder_ReturnsFalse()
    {
        _filter.AddFolderName("temp");
        Assert.True(_filter.ShouldExclude("temp/file.txt"));
        
        _filter.RemoveFolderName("temp");
        Assert.False(_filter.ShouldExclude("temp/file.txt"));
    }

    [Fact]
    public void ShouldExclude_RemovedPattern_ReturnsFalse()
    {
        Assert.True(_filter.ShouldExclude("file.tmp"));
        
        _filter.RemoveFileNamePattern("*.tmp");
        Assert.False(_filter.ShouldExclude("file.tmp"));
    }

    [Fact]
    public void ShouldExclude_CaseInsensitiveForFolders()
    {
        _filter.AddFolderName("Cache");
        Assert.True(_filter.ShouldExclude("cache/file.txt"));
        Assert.True(_filter.ShouldExclude("CACHE/file.txt"));
    }

    [Fact]
    public void ShouldExclude_CaseInsensitiveForPatterns()
    {
        Assert.True(_filter.ShouldExclude("THUMBS.DB"));
        Assert.True(_filter.ShouldExclude("Desktop.Ini"));
    }

    [Fact]
    public void ShouldExclude_EmptyPath_ReturnsTrue()
    {
        Assert.True(_filter.ShouldExclude(""));
        Assert.True(_filter.ShouldExclude("   "));
        Assert.True(_filter.ShouldExclude(null!));
    }

    [Fact]
    public void ShouldExclude_NestedPath_ChecksAllParts()
    {
        _filter.AddFolderName(".git");
        Assert.True(_filter.ShouldExclude(".git/objects/abc123"));
        Assert.True(_filter.ShouldExclude("project/.git/config"));
    }
}