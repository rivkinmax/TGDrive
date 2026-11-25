using System;
using System.IO;
using Xunit;
using TGDrive.Storage;

namespace TGDrive.Tests;

public class DbTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly Db _db;

    public DbTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");
        _db = new Db(_tempDbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_tempDbPath)) File.Delete(_tempDbPath);
        
        var walPath = _tempDbPath + "-wal";
        if (File.Exists(walPath)) File.Delete(walPath);
        
        var shmPath = _tempDbPath + "-shm";
        if (File.Exists(shmPath)) File.Delete(shmPath);
    }

    [Fact]
    public void Db_CreatesTablesAndSetsMeta()
    {
        _db.SetMeta("test_key", "test_value");
        var value = _db.GetMeta("test_key");
        Assert.Equal("test_value", value);
    }

    [Fact]
    public void Db_GetMeta_ReturnsNullForMissingKey()
    {
        var value = _db.GetMeta("missing_key");
        Assert.Null(value);
    }
}