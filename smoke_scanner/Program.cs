using System.Security.Cryptography;
using System.Text.Json;
using TGDrive.Core;
using TGDrive.Telegram;

class Program
{
    const long ChatId = -1004366704743L;

    static Dictionary<string, string> ReadKeys(string path)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
            var idx = trimmed.IndexOf('=');
            if (idx <= 0) continue;
            result[trimmed.Substring(0, idx).Trim()] = trimmed.Substring(idx + 1).Trim();
        }
        return result;
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting round-trip test (encode -> send -> scan)...");
        var keys = ReadKeys(@"c:\work\TGDrive\input.txt");

        await using var gateway = new TdGateway();
        gateway.Configure(int.Parse(keys["api_id"]), keys["api_hash"],
            @"c:\work\TGDrive\data\td", @"c:\work\TGDrive\data\files");
        gateway.AuthStateChanged += (s) => Console.WriteLine($"[AUTH] {s}");

        await gateway.StartAsync();
        var state = await gateway.WaitForAuthReadyAsync(TimeSpan.FromSeconds(15));
        if (state != "authorizationStateReady")
        {
            Console.WriteLine($"Cannot proceed: auth state {state}");
            return;
        }

        // 1. Prepare test file and compute metadata
        var filePath = @"c:\work\TGDrive\data\test_upload.txt";
        if (!File.Exists(filePath)) File.WriteAllText(filePath, "roundtrip test content");
        var bytes = File.ReadAllBytes(filePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var size = bytes.LongLength;
        var mtime = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeSeconds();

        var fid = CaptionCodec.GenerateNewId();
        var vid = CaptionCodec.GenerateNewId();
        var meta = new FileMetadata(fid, vid, "", hash, size, mtime, "smoke-dev");
        var path = "smoke/roundtrip.txt";
        var caption = CaptionCodec.EncodeFile(path, meta);
        Console.WriteLine($"Encoded caption: {caption.Length} UTF-16 chars, fid={fid}, vid={vid}");

        // 2. Send document with tgdrive caption (three-level nesting)
        var sendDoc = await gateway.SendRequestAsync(new Dictionary<string, object>
        {
            ["@type"] = "sendMessage",
            ["chat_id"] = ChatId,
            ["input_message_content"] = new Dictionary<string, object>
            {
                ["@type"] = "inputMessageDocument",
                ["document"] = new Dictionary<string, object>
                {
                    ["@type"] = "inputDocument",
                    ["document"] = new Dictionary<string, object>
                    {
                        ["@type"] = "inputFileLocal",
                        ["path"] = filePath
                    },
                    ["disable_content_type_detection"] = true
                },
                ["caption"] = new Dictionary<string, object>
                {
                    ["@type"] = "formattedText",
                    ["text"] = caption
                }
            }
        });
        var docType = sendDoc.GetProperty("@type").GetString();
        Console.WriteLine($"sendMessage(document) -> {docType}");
        if (docType == "error")
        {
            Console.WriteLine($"ERROR: {sendDoc.GetProperty("message").GetString()}");
            return;
        }
        Console.WriteLine($"document message id={sendDoc.GetProperty("id").GetInt64()}");

        Console.WriteLine("Cautious pause 10s...");
        await Task.Delay(10000);

        // 3. Send folder service record (t="d")
        var folderText = "#tgdrive1 {\"v\":1,\"t\":\"d\",\"p\":\"smoke\"}";
        var sendFolder = await gateway.SendRequestAsync(new Dictionary<string, object>
        {
            ["@type"] = "sendMessage",
            ["chat_id"] = ChatId,
            ["input_message_content"] = new Dictionary<string, object>
            {
                ["@type"] = "inputMessageText",
                ["text"] = new Dictionary<string, object>
                {
                    ["@type"] = "formattedText",
                    ["text"] = folderText
                }
            }
        });
        var folderType = sendFolder.GetProperty("@type").GetString();
        Console.WriteLine($"sendMessage(folder) -> {folderType}");
        if (folderType == "error")
        {
            Console.WriteLine($"ERROR: {sendFolder.GetProperty("message").GetString()}");
            return;
        }
        Console.WriteLine($"folder message id={sendFolder.GetProperty("id").GetInt64()}");

        Console.WriteLine("Settle pause 4s...");
        await Task.Delay(4000);

        // 4. Scan channel
        Console.WriteLine("Scanning channel history...");
        var scanner = new ChannelScanner(gateway, ChatId);
        var records = await scanner.ScanHistoryAsync(200);
        Console.WriteLine($"Found {records.Count} records with metadata");
        foreach (var r in records)
        {
            var f = r.FileMeta?.FileId ?? "(none)";
            var v = r.FileMeta?.VersionId ?? "(none)";
            var td = r.TdFileId?.ToString() ?? "(none)";
            Console.WriteLine($"  msg={r.MessageId} type={r.RecordType} path={r.Path} fid={f} vid={v} tdfile={td}");
        }

        // 5. Verify round-trip
        var fileRec = records.FirstOrDefault(r => r.RecordType == "f" && r.Path == path && r.FileMeta?.VersionId == vid);
        var folderRec = records.FirstOrDefault(r => r.RecordType == "d" && r.Path == "smoke");

        Console.WriteLine("=== ROUND-TRIP RESULTS ===");
        if (fileRec != null)
        {
            Console.WriteLine("FILE: FOUND");
            Console.WriteLine($"  fid match:  {fileRec.FileMeta!.FileId == fid}");
            Console.WriteLine($"  vid match:  {fileRec.FileMeta!.VersionId == vid}");
            Console.WriteLine($"  pvid empty: {fileRec.FileMeta!.ParentVersionId == ""}");
            Console.WriteLine($"  hash match: {fileRec.FileMeta!.Sha256Hex == hash}");
            Console.WriteLine($"  size match: {fileRec.FileMeta!.Size == size}");
            Console.WriteLine($"  tdfile id:  {fileRec.TdFileId}");
        }
        else
        {
            Console.WriteLine("FILE: NOT FOUND (FAIL)");
        }
        Console.WriteLine(folderRec != null ? "FOLDER: FOUND" : "FOLDER: NOT FOUND (FAIL)");

        Console.WriteLine("Done. Disposing gateway...");
    }
}
