using System;
using System.Collections.Generic;
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
        if (record == null) return IngestResult.Ignored;

        if (string.IsNullOrEmpty(record.RecordType))
        {
            return IngestResult.Ignored;
        }

        try
        {
            bool isNew = await _store.SaveRemoteMessageAsync(record, ct).ConfigureAwait(false);
            
            if (!isNew)
            {
                return IngestResult.Duplicate;
            }

            // В будущем здесь будет проверка вилок (conflict) через сравнение parent_version_id 
            // с текущей головой в БД. Пока помечаем как Applied, так как запись успешно добавлена.
            return IngestResult.Applied;
        }
        catch (Exception)
        {
            // Консервативная модель: не падаем из-за одного сообщения, чтобы не блокировать очередь.
            return IngestResult.Ignored;
        }
    }

    public async Task<IngestSummary> ApplyBatchAsync(IReadOnlyList<ChannelRecord> records, CancellationToken ct = default)
    {
        int applied = 0, duplicate = 0, conflicts = 0, ignored = 0;
        long lastMessageId = 0;

        foreach (var record in records)
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
