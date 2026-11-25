using System.Text.Json;
using TGDrive.Telegram;

var cfg = File.ReadAllLines(@"c:\work\TGDrive\input.txt")
    .Select(l => l.Split('=', 2))
    .Where(p => p.Length == 2)
    .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

await using var gw = new TdGateway();
gw.Configure(int.Parse(cfg["api_id"]), cfg["api_hash"],
    @"c:\work\TGDrive\data\td", @"c:\work\TGDrive\data\files");
await gw.StartAsync();
var state = await gw.WaitForAuthReadyAsync(TimeSpan.FromSeconds(30));
Console.WriteLine($"Auth state: {state}");
if (state != "authorizationStateReady")
{
    Console.WriteLine("ABORT: session is not ready.");
    return;
}

long chatId = -1004366704743;
long anchorId = 1048576;
long[] expected = { 1048594, 1048586, 1048578 };

async Task<List<(long Id, string Type)>> ListMessages()
{
    var resp = await gw.SendRequestAsync(new Dictionary<string, object>
    {
        ["@type"] = "getChatHistory",
        ["chat_id"] = chatId,
        ["from_message_id"] = 0,
        ["offset"] = 0,
        ["limit"] = 50,
        ["only_local"] = false
    }, TimeSpan.FromSeconds(30));
    if (resp.GetProperty("@type").GetString() != "messages")
        throw new InvalidOperationException("getChatHistory failed: " + resp);
    var list = new List<(long, string)>();
    foreach (var m in resp.GetProperty("messages").EnumerateArray())
        list.Add((m.GetProperty("id").GetInt64(),
                  m.GetProperty("content").GetProperty("@type").GetString() ?? "?"));
    return list;
}

async Task<List<(long Id, string Type)>> ListFullHistory()
{
    for (int attempt = 1; attempt <= 10; attempt++)
    {
        var list = await ListMessages();
        if (list.Any(m => m.Id == anchorId))
        {
            Console.WriteLine($"History fully loaded on attempt {attempt} ({list.Count} messages).");
            return list;
        }
        Console.WriteLine($"Attempt {attempt}: only {list.Count} message(s), history not loaded yet; retrying...");
        await Task.Delay(500);
    }
    throw new InvalidOperationException("History not fully loaded after 10 attempts.");
}

List<(long Id, string Type)> before;
try { before = await ListFullHistory(); }
catch (Exception ex)
{
    Console.WriteLine("ABORT: " + ex.Message);
    return;
}
Console.WriteLine("--- BEFORE ---");
foreach (var (id, t) in before) Console.WriteLine($"msg {id}: {t}");

var unsupported = before.Where(m => m.Type == "messageUnsupported").Select(m => m.Id).ToList();
var toDelete = expected.Where(unsupported.Contains).ToArray();
Console.WriteLine($"To delete (expected AND unsupported): {string.Join(", ", toDelete)}");

if (toDelete.Length > 0)
{
    var del = await gw.SendRequestAsync(new Dictionary<string, object>
    {
        ["@type"] = "deleteMessages",
        ["chat_id"] = chatId,
        ["message_ids"] = toDelete,
        ["revoke"] = true
    }, TimeSpan.FromSeconds(30));
    Console.WriteLine($"deleteMessages: {del.GetProperty("@type").GetString()}");
}
else
{
    Console.WriteLine("Nothing to delete.");
}

var after = await ListFullHistory();
Console.WriteLine("--- AFTER ---");
foreach (var (id, t) in after) Console.WriteLine($"msg {id}: {t}");
var still = toDelete.Where(id => after.Any(m => m.Id == id)).ToArray();
if (toDelete.Length == 0)
    Console.WriteLine("RESULT: nothing was deleted.");
else
    Console.WriteLine(still.Length == 0 ? "CLEANUP VERIFIED" : "WARNING: still present: " + string.Join(", ", still));

await gw.CloseAsync(TimeSpan.FromSeconds(10));
Console.WriteLine("Closed.");
