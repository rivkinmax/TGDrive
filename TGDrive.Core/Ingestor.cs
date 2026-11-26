using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TGDrive.Core;

public enum IngestResult { Applied, Duplicate, Conflict, Ignored }

public sealed record IngestSummary(
    int Applied, 
    int Duplicate, 
    int Conflicts, 
    int Ignored,
    long LastMessageId);

public sealed class Ingestor
{
    private readonly IStore _store;

    public Ingestor(IStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IngestResult> ApplyAsync(ChannelRecord record, CancellationToken ct = default)
    {
        if (record == null || string.IsNullOrEmpty(record.RecordType)) 
            return IngestResult.Ignored;

        if (record.RecordType == "f" && record.FileMeta != null)
        {
            var currentVid = await _store.GetCurrentVersionIdAsync(record.FileMeta.FileId, ct).ConfigureAwait(false);
            
            if (currentVid == record.FileMeta.VersionId)
                return IngestResult.Duplicate;
            
            if (currentVid != null && currentVid != record.FileMeta.ParentVersionId)
                return IngestResult.Conflict;
        }

        bool isNew = await _store.SaveRemoteMessageAsync(record, ct).ConfigureAwait(false);
        return isNew ? IngestResult.Applied : IngestResult.Duplicate;
    }

    public async Task<IngestSummary> ApplyBatchAsync(IReadOnlyList<ChannelRecord> records, CancellationToken ct = default)
    {
        int applied = 0, duplicate = 0, conflicts = 0, ignored = 0;
        long lastMessageId = 0;

        var sortedRecords = records.OrderBy(r => r.MessageId).ToList();

        foreach (var record in sortedRecords)
        {
            ct.ThrowIfCancellationRequested();
            
            var result = await ApplyAsync(record, ct).ConfigureAwait(false);
            
            switch (result)
            {
                case IngestResult.Applied: applied++; break;
                case IngestResult.Duplicate: duplicate++; break;
                case IngestResult.Conflict: conflicts++; break;
                case IngestResult.Ignored: ignored++; break;
            }

            if (record.MessageId > lastMessageId)
            {
                lastMessageId = record.MessageId;
            }
        }

        return new IngestSummary(applied, duplicate, conflicts, ignored, lastMessageId);
    }
}
