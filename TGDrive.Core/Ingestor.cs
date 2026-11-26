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

    /// <summary>
    /// Идемпотентная свёртка одной записи.
    /// При сбое БД исключение пробрасывается наружу (fail-fast), чтобы избежать молчаливой потери данных (требование 17).
    /// </summary>
    public async Task<IngestResult> ApplyAsync(ChannelRecord record, CancellationToken ct = default)
    {
        if (record == null) return IngestResult.Ignored;
        if (string.IsNullOrEmpty(record.RecordType)) return IngestResult.Ignored;

        bool isNew = await _store.SaveRemoteMessageAsync(record, ct).ConfigureAwait(false);
        return isNew ? IngestResult.Applied : IngestResult.Duplicate;
    }

    /// <summary>
    /// Пакетная свёртка. Сортирует записи по MessageId ASC для корректного построения цепочек версий.
    /// </summary>
    public async Task<IngestSummary> ApplyBatchAsync(IReadOnlyList<ChannelRecord> records, CancellationToken ct = default)
    {
        int applied = 0, duplicate = 0, conflicts = 0, ignored = 0;
        long lastMessageId = 0;

        // Хронологический порядок критичен для будущей валидации parent_version_id
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
