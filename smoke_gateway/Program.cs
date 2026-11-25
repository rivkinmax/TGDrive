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
            var key = trimmed.Substring(0, idx).Trim();
            var value = trimmed.Substring(idx + 1).Trim();
            result[key] = value;
        }
        return result;
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting TdGateway smoke test...");

        var keys = ReadKeys(@"c:\work\TGDrive\input.txt");

        await using var gateway = new TdGateway();
        gateway.Configure(
            int.Parse(keys["api_id"]),
            keys["api_hash"],
            @"c:\work\TGDrive\data\td",
            @"c:\work\TGDrive\data\files"
        );

        gateway.AuthStateChanged += (state) => Console.WriteLine($"[AUTH] {state}");

        Console.WriteLine("Starting gateway...");
        await gateway.StartAsync();

        Console.WriteLine("Waiting for auth ready...");
        var state = await gateway.WaitForAuthReadyAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine($"Final auth state: {state}");

        if (state != "authorizationStateReady")
        {
            Console.WriteLine($"Cannot proceed: auth state {state}");
            return;
        }

        Console.WriteLine("Test: getMe...");
        var me = await gateway.SendRequestAsync(new Dictionary<string, object>
        {
            ["@type"] = "getMe"
        });
        Console.WriteLine($"User ID: {me.GetProperty("id").GetInt64()}");

        Console.WriteLine("Test: getChat...");
        var chat = await gateway.SendRequestAsync(new Dictionary<string, object>
        {
            ["@type"] = "getChat",
            ["chat_id"] = -1004366704743L
        });
        Console.WriteLine($"Chat title: {chat.GetProperty("title").GetString()}");

        Console.WriteLine("Done. Disposing gateway...");
    }
}
