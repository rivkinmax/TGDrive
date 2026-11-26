using System;
using System.IO;
using System.Threading.Tasks;
using TGDrive.Core;
using TGDrive.Storage;
using Xunit;

namespace TGDrive.Tests;

public class IngestorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Db _db;
    private readonly Store _store;
    private readonly Ingestor _ingestor;

    public IngestorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tgdrive_ingest_test_{Guid.NewGuid():N}.db");
        _db = new Db(_dbPath);
        _store = new Store(_db);
        _ingestor = new Ingestor(_store);
    }

    [Fact]
    public async Task ApplyAsync_Idempotent_ReturnsAppliedThenDuplicate()
    {
        // Arrange
        var fileId = Guid.NewGuid().ToString("N");
        var versionId = Guid.NewGuid().ToString("N");
        var record = new ChannelRecord
        {
            MessageId = 12345,
            RecordType = "f",
            Path = "test/file.txt",
            FileMeta = new FileMetadata(
                FileId: fileId,
                VersionId: versionId,
                ParentVersionId: "",
                Sha256Hex: "abc123def456",
                Size: 1024,
                MtimeUnix: 1700000000,
                DeviceId: "dev1"
            ),
            TdFileId = 999
        };

        // Act 1: First application should succeed
        var result1 = await _ingestor.ApplyAsync(record);
        
        // Act 2: Second application of the same record should be a duplicate
        var result2 = await _ingestor.ApplyAsync(record);

        // Assert
        Assert.Equal(IngestResult.Applied, result1);
        Assert.Equal(IngestResult.Duplicate, result2);
    }

    [Fact]
    public async Task ApplyBatchAsync_ProcessesMultipleRecordsAndTracksLastId()
    {
        // Arrange
        var record1 = new ChannelRecord { MessageId = 100, RecordType = "d", Path = "folder1" };
        var record2 = new ChannelRecord { MessageId = 200, RecordType = "f", Path = "folder1/file.txt", FileMeta = new FileMetadata("f1", "v1", "", "hash", 100, 1700000000, "dev1"), TdFileId = 1 };
        var record3 = new ChannelRecord { MessageId = 100, RecordType = "d", Path = "folder1" }; // Duplicate of record1

        // Act
        var summary = await _ingestor.ApplyBatchAsync(new[] { record1, record2, record3 });

        // Assert
        Assert.Equal(2, summary.Applied);
        Assert.Equal(1, summary.Duplicate);
        Assert.Equal(0, summary.Conflicts);
        Assert.Equal(0, summary.Ignored);
        Assert.Equal(200, summary.LastMessageId);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
            File.Delete(_dbPath + "-wal");
            File.Delete(_dbPath + "-shm");
        }
    }
}
