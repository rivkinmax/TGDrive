using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TGDrive.Telegram;

public class TdClient : IDisposable
{
    private int _clientId;
    private bool _disposed;
    private readonly object _lock = new();
    public bool DebugOutput { get; set; } = false;

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern int td_create_client_id();

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_send(int clientId, IntPtr request);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_receive(double timeout);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_set_log_verbosity_level(int level);

    public TdClient()
    {
        td_set_log_verbosity_level(0);
        _clientId = td_create_client_id();
    }

    public async Task<JsonElement> SendAsync(object request, string? extra = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TdClient));

        var dict = request switch
        {
            Dictionary<string, object> d => d,
            _ => throw new ArgumentException("Request must be Dictionary<string, object>")
        };

        if (!string.IsNullOrEmpty(extra))
        {
            dict["@extra"] = extra;
        }

        var json = JsonSerializer.Serialize(dict);
        var requestPtr = Marshal.StringToCoTaskMemUTF8(json);

        lock (_lock)
        {
            td_send(_clientId, requestPtr);
        }

        Marshal.FreeCoTaskMem(requestPtr);

        return await ReceiveAsync(extra, 30.0).ConfigureAwait(false);
    }

    private async Task<JsonElement> ReceiveAsync(string? expectedExtra, double timeout)
    {
        while (true)
        {
            IntPtr responsePtr;
            lock (_lock)
            {
                responsePtr = td_receive(timeout);
            }

            if (responsePtr == IntPtr.Zero)
            {
                await Task.Delay(10).ConfigureAwait(false);
                continue;
            }

            var response = Marshal.PtrToStringUTF8(responsePtr);
            if (string.IsNullOrEmpty(response))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var type = root.GetProperty("@type").GetString();

            if (DebugOutput)
            {
                Console.WriteLine($"[DEBUG] {type}{(root.TryGetProperty("@extra", out var e) ? $" @{e.GetString()}" : "")}");
            }

            // Skip all updates, they are not responses to our requests
            if (type?.StartsWith("update") == true)
            {
                continue;
            }

            // If we expect a specific @extra, filter by it
            if (!string.IsNullOrEmpty(expectedExtra))
            {
                if (root.TryGetProperty("@extra", out var extraElement))
                {
                    var extra = extraElement.GetString();
                    if (extra == expectedExtra)
                    {
                        return root.Clone();
                    }
                }
                // Response has wrong @extra, skip it
                continue;
            }

            return root.Clone();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
