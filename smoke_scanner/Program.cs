using System.Text.Json;
using TGDrive.Telegram;

class Program
{
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
        Console.WriteLine("Starting ChannelScanner smoke test...");
        var keys = ReadKeys(@"c:\work\TGDrive\input.txt");

        await using var gateway = new TdGateway();
        gateway.Configure(int.Parse(keys["api_id"]), keys["api_hash"],
            @"c:\work\TGDrive\data\td", @"c:\work\TGDrive\data\files");
        gateway.AuthStateChanged += (state) => Console.WriteLine($"[AUTH] {state}");

        await gateway.StartAsync();
        var state = await gateway.WaitForAuthReadyAsync(TimeSpan.FromSeconds(15));
        if (state != "authorizationStateReady")
        {
            Console.WriteLine($"Cannot proceed: auth state {state}");
            return;
        }

        Console.WriteLine("Scanning channel history...");
        var scanner = new ChannelScanner(gateway, -1004366704743L);
        var records = await scanner.ScanHistoryAsync(200);

        Console.WriteLine($"Found {records.Count} records with metadata");
        foreach (var rec in records.Take(10))
        {
            var fid = rec.FileMeta?.FileId ?? "(none)";
            var vid = rec.FileMeta?.VersionId ?? "(none)";
            var td = rec.TdFileId?.ToString() ?? "(none)";
            Console.WriteLine($"  msg={rec.MessageId} type={rec.RecordType} path={rec.Path} fid={fid} vid={vid} tdfile={td}");
        }

        Console.WriteLine("Done. Disposing gateway...");
    }
}
