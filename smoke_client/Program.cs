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
        Console.WriteLine("Starting TdClient smoke test...");

        var keys = ReadKeys(@"c:\work\TGDrive\input.txt");
        Console.WriteLine($"Loaded keys: api_id={keys.ContainsKey("api_id")}, api_hash={keys.ContainsKey("api_hash")}, phone={keys.ContainsKey("PhoneNumber")}");

        using var client = new TdClient();
        client.DebugOutput = true;

        // Test 1: getOption (should return first, before auth)
        Console.WriteLine("Test 1: getOption version...");
        var version = await client.SendAsync(new Dictionary<string, object>
        {
            ["@type"] = "getOption",
            ["name"] = "version"
        }, "get_version");
        Console.WriteLine($"TDLib version: {version.GetProperty("value").GetString()}");

        // Test 2: setTdlibParameters (flat, not nested)
        Console.WriteLine("Test 2: setTdlibParameters...");
        var paramsResult = await client.SendAsync(new Dictionary<string, object>
        {
            ["@type"] = "setTdlibParameters",
            ["use_test_dc"] = false,
            ["database_directory"] = @"c:\work\TGDrive\data\td",
            ["files_directory"] = @"c:\work\TGDrive\data\files",
            ["database_encryption_key"] = "dGdkcml2ZQ==",
            ["use_file_database"] = true,
            ["use_chat_info_database"] = true,
            ["use_message_database"] = true,
            ["use_secret_chats"] = false,
            ["system_language_code"] = "ru",
            ["device_model"] = "Desktop",
            ["system_version"] = "Windows",
            ["application_version"] = "0.1.0-smoke",
            ["api_id"] = int.Parse(keys["api_id"]),
            ["api_hash"] = keys["api_hash"]
        }, "set_params");
        var paramsType = paramsResult.GetProperty("@type").GetString();
        Console.WriteLine($"setTdlibParameters response type: {paramsType}");
        if (paramsType == "error")
        {
            Console.WriteLine($"ERROR: {paramsResult.GetProperty("message").GetString()}");
            return;
        }

        // After setTdlibParameters, the first response is authorizationStateWaitTdlibParameters (skipped as update),
        // but TDLib then proceeds to authorizationStateReady automatically if session exists.
        // We call getMe — it will be queued after auth completes.
        Console.WriteLine("Test 3: getMe...");
        var me = await client.SendAsync(new Dictionary<string, object>
        {
            ["@type"] = "getMe"
        }, "get_me");
        Console.WriteLine($"User ID: {me.GetProperty("id").GetInt64()}");

        // Test 4: getChat for target channel
        Console.WriteLine("Test 4: getChat for target channel...");
        var chat = await client.SendAsync(new Dictionary<string, object>
        {
            ["@type"] = "getChat",
            ["chat_id"] = -1004366704743L
        }, "get_chat");
        Console.WriteLine($"Chat title: {chat.GetProperty("title").GetString()}");

        // Cleanup
        Console.WriteLine("Closing TDLib...");
        await client.SendAsync(new Dictionary<string, object>
        {
            ["@type"] = "close"
        }, "close");

        Console.WriteLine("Smoke test completed successfully!");
    }
}
