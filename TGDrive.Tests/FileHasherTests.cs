using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TGDrive.LocalFs;

namespace TGDrive.Tests;

public class FileHasherTests : IDisposable
{
    private readonly string _tempDir;

    public FileHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tgdrive_hasher_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ComputeSha256Async_EmptyFile_ReturnsKnownHash()
    {
        var path = Path.Combine(_tempDir, "empty.txt");
        await File.WriteAllTextAsync(path, "");
        
        // SHA256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        var hash = await FileHasher.ComputeSha256Async(path);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public async Task ComputeSha256Async_SmallContent_ReturnsCorrectHash()
    {
        var path = Path.Combine(_tempDir, "hello.txt");
        await File.WriteAllTextAsync(path, "hello");
        
        // SHA256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        var hash = await FileHasher.ComputeSha256Async(path);
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", hash);
    }

    [Fact]
    public async Task ComputeSha256Async_LargeFile_ReturnsCorrectHash()
    {
        var path = Path.Combine(_tempDir, "large.bin");
        // Создаём файл 1 МБ (больше размера буфера 64KB, чтобы проверить множественные итерации)
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);
        await File.WriteAllBytesAsync(path, data);

        var hash1 = await FileHasher.ComputeSha256Async(path);
        var hash2 = await FileHasher.ComputeSha256Async(path);
        
        // Идемпотентность: одинаковый контент → одинаковый хэш
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public async Task ComputeSha256Async_DifferentContent_DifferentHash()
    {
        var path1 = Path.Combine(_tempDir, "a.txt");
        var path2 = Path.Combine(_tempDir, "b.txt");
        await File.WriteAllTextAsync(path1, "content A");
        await File.WriteAllTextAsync(path2, "content B");
        
        var hash1 = await FileHasher.ComputeSha256Async(path1);
        var hash2 = await FileHasher.ComputeSha256Async(path2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeSha256Async_UnicodeContent_HandlesCorrectly()
    {
        var path = Path.Combine(_tempDir, "unicode.txt");
        await File.WriteAllTextAsync(path, "Привет, мир! 🌍");
        
        var hash = await FileHasher.ComputeSha256Async(path);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public async Task ComputeSha256Async_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempDir, "does_not_exist.txt");
        await Assert.ThrowsAsync<FileNotFoundException>(() => FileHasher.ComputeSha256Async(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ComputeSha256Async_EmptyPath_ThrowsArgumentException(string? path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => FileHasher.ComputeSha256Async(path!));
    }

    [Fact]
    public async Task ComputeSha256Async_CancelledToken_ThrowsOperationCanceledException()
    {
        var path = Path.Combine(_tempDir, "large.bin");
        var data = new byte[2 * 1024 * 1024]; // 2 MB — чтобы было время для отмены
        new Random(42).NextBytes(data);
        await File.WriteAllBytesAsync(path, data);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => 
            FileHasher.ComputeSha256Async(path, cts.Token));
    }

    [Fact]
    public async Task ComputeSha256Async_ReturnsLowercaseHex()
    {
        var path = Path.Combine(_tempDir, "lower.txt");
        await File.WriteAllTextAsync(path, "test");
        
        var hash = await FileHasher.ComputeSha256Async(path);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }
}