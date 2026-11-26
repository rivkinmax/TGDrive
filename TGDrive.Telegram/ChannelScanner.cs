using System.Text.Json;
using TGDrive.Core;

namespace TGDrive.Telegram;

public class ChannelScanner
{
    private readonly TdGateway _gateway;
    private readonly long _chatId;

    public ChannelScanner(TdGateway gateway, long chatId)
    {
        _gateway = gateway;
        _chatId = chatId;
    }

    public async Task<List<ChannelRecord>> ScanHistoryAsync(int limit = 200, Action<int, int>? progress = null)
    {
        var results = new List<ChannelRecord>();
        long fromMessageId = 0;
        int fetched = 0;

        while (fetched < limit)
        {
            var batch = await _gateway.SendRequestAsync(new Dictionary<string, object>
            {
                ["@type"] = "getChatHistory",
                ["chat_id"] = _chatId,
                ["from_message_id"] = fromMessageId,
                ["offset"] = 0,
                ["limit"] = Math.Min(100, limit - fetched),
                ["only_local"] = false
            });

            var type = batch.GetProperty("@type").GetString();
            if (type == "error")
            {
                var msg = batch.GetProperty("message").GetString();
                throw new InvalidOperationException($"getChatHistory failed: {msg}");
            }

            if (type != "messages" || !batch.TryGetProperty("messages", out var messagesElement))
                break;

            var messages = messagesElement.EnumerateArray().ToList();
            if (messages.Count == 0) break;

            foreach (var msg in messages)
            {
                var parsed = TryParseMessage(msg);
                if (parsed != null) results.Add(parsed);
                fetched++;
                progress?.Invoke(fetched, limit);
            }

            var lastMessage = messages.Last();
            if (lastMessage.TryGetProperty("id", out var idEl))
                fromMessageId = idEl.GetInt64();
            else
                break;
        }

        results.Sort((a, b) => b.MessageId.CompareTo(a.MessageId));
        return results;
    }

    private ChannelRecord? TryParseMessage(JsonElement msg)
    {
        if (!msg.TryGetProperty("id", out var idEl)) return null;
        var messageId = idEl.GetInt64();

        if (!msg.TryGetProperty("content", out var content)) return null;
        if (!content.TryGetProperty("@type", out var contentType)) return null;
        var ctype = contentType.GetString();

        if (ctype == "messageDocument")
        {
            if (!content.TryGetProperty("caption", out var caption)) return null;
            if (!caption.TryGetProperty("text", out var captionText)) return null;
            var text = captionText.GetString();
            if (string.IsNullOrEmpty(text)) return null;

            try
            {
                var (path, meta) = CaptionCodec.DecodeFile(text);

                int? tdFileId = null;
                if (content.TryGetProperty("document", out var docEl) &&
                    docEl.TryGetProperty("document", out var innerDoc) &&
                    innerDoc.TryGetProperty("id", out var fidEl))
                {
                    tdFileId = (int)fidEl.GetInt64();
                }

                var lines = text.Split('\n');
                var jsonStr = lines[1].Trim().Substring(CaptionCodec.Prefix.Length);
                using var metaDoc = JsonDocument.Parse(jsonStr);

                return new ChannelRecord
                {
                    MessageId = messageId,
                    RecordType = "f",
                    Path = path,
                    FileMeta = meta,
                    TdFileId = tdFileId,
                    RawJson = metaDoc.RootElement.Clone()
                };
            }
            catch
            {
                return null;
            }
        }

        if (ctype == "messageText")
        {
            if (!content.TryGetProperty("text", out var textEl)) return null;
            if (!textEl.TryGetProperty("text", out var rawText)) return null;
            var text = rawText.GetString();
            if (string.IsNullOrEmpty(text)) return null;

            var service = CaptionCodec.TryDecodeServiceRecord(text);
            if (service == null) return null;

            return new ChannelRecord
            {
                MessageId = messageId,
                RecordType = service.Type,
                Path = service.Path,
                FileMeta = null,
                TdFileId = null,
                RawJson = service.Metadata
            };
        }

        return null;
    }
}
