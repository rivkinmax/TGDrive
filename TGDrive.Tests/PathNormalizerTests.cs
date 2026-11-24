using System;
using Xunit;
using TGDrive.Core;

namespace TGDrive.Tests;

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_ValidPaths_ReturnsNormalized()
    {
        Assert.Equal("folder/file.txt", PathNormalizer.Normalize("folder\\file.txt"));
        Assert.Equal("folder/file.txt", PathNormalizer.Normalize("/folder/file.txt"));
        Assert.Equal("folder/file.txt", PathNormalizer.Normalize("folder/./file.txt"));
        Assert.Equal("file.txt", PathNormalizer.Normalize("folder/../file.txt"));
        Assert.Equal("a/b/c.txt", PathNormalizer.Normalize("a/./b/../b/c.txt"));
    }

    [Fact]
    public void Normalize_TraversesAboveRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathNormalizer.Normalize("../file.txt"));
        Assert.Throws<ArgumentException>(() => PathNormalizer.Normalize("folder/../../file.txt"));
    }

    [Fact]
    public void Normalize_ExceedsBudget_Throws()
    {
        var longPath = new string('a', 701) + "/file.txt";
        var ex = Assert.Throws<InvalidOperationException>(() => PathNormalizer.Normalize(longPath));
        Assert.Equal("PathTooLongForCaption", ex.Message);
    }

    [Fact]
    public void GetDbKey_ReturnsLowerInvariant()
    {
        Assert.Equal("folder/file.txt", PathNormalizer.GetDbKey("Folder/File.TXT"));
        Assert.Equal("привет/мир.txt", PathNormalizer.GetDbKey("ПРИВЕТ/МИР.TXT")); // Кириллица останется как есть, но в нижнем регистре
    }
}