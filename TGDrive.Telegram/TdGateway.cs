using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TGDrive.Telegram;

public sealed class TdGateway : IAsyncDisposable
{
    private int _clientId;
    private readonly object _sendLock = new();
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoopTask;
    private long _requestCounter;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, Action<JsonElement>> _updateHandlers = new();
    private bool _disposed;
    private volatile string? _lastAuthState;

    private int _apiId;
    private string _apiHash = "";
    private string _databaseDirectory = "";
    private string _filesDirectory = "";
    private string _databaseEncryptionKey = "dGdkcml2ZQ==";

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern int td_create_client_id();

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_send(int clientId, IntPtr request);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_receive(double timeout);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_set_log_verbosity_level(int level);

    public event Action<string>? AuthStateChanged;
    public event Action<string, JsonElement>? UpdateReceived;
    public event Action<string, JsonElement>? ResponseReceived;

    private volatile bool _debugOutput;
    // Optional debug console output; default off. RAW lines may contain sensitive data.
    public bool DebugOutput { get => _debugOutput; set => _debugOutput = value; }

    public TdGateway()
    {
        td_set_log_verbosity_level(0);
        _clientId = td_create_client_id();
    }

    public void Configure(int apiId, string apiHash, string databaseDirectory, string filesDirectory)
    {
        _apiId = apiId;
        _apiHash = apiHash;
        _databaseDirectory = databaseDirectory;
        _filesDirectory = filesDirectory;
    }

    public void OnUpdate(string updateType, Action<JsonElement> handler)
    {
        _updateHandlers[updateType] = handler;
    }

    public async Task StartAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TdGateway));

        _loopCts = new CancellationTokenSource();
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_loopCts.Token));

        // Send parameters immediately; TDLib accepts them before any update is emitted
        if (DebugOutput) Console.WriteLine("[DBG] Sending setTdlibParameters...");
        var paramsResp = await SendRequestAsync(new Dictionary<string, object>
        {
            ["@type"] = "setTdlibParameters",
            ["use_test_dc"] = false,
            ["database_directory"] = _databaseDirectory,
            ["files_directory"] = _filesDirectory,
            ["database_encryption_key"] = _databaseEncryptionKey,
            ["use_file_database"] = true,
            ["use_chat_info_database"] = true,
            ["use_message_database"] = true,
            ["use_secret_chats"] = false,
            ["system_language_code"] = "ru",
            ["device_model"] = "Desktop",
            ["system_version"] = "Windows",
            ["application_version"] = "0.1.0-smoke",
            ["api_id"] = _apiId,
            ["api_hash"] = _apiHash
        }, TimeSpan.FromSeconds(30));

        var ptype = paramsResp.GetProperty("@type").GetString();
        if (DebugOutput) Console.WriteLine($"[DBG] setTdlibParameters response: {ptype}");
        if (ptype == "error")
        {
            var msg = paramsResp.GetProperty("message").GetString();
            throw new InvalidOperationException($"setTdlibParameters failed: {msg}");
        }
    }

    public async Task<string> WaitForAuthReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var s = _lastAuthState;
            if (s == "authorizationStateReady" || s == "authorizationStateClosing" || s == "authorizationStateClosed")
                return s;
            if (s == "authorizationStateWaitPhoneNumber" || s == "authorizationStateWaitCode")
                return s;
            await Task.Delay(50);
        }
        return _lastAuthState ?? "timeout";
    }

    public async Task<JsonElement> SendRequestAsync(Dictionary<string, object> request, TimeSpan? timeout = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TdGateway));

        var extra = $"req_{Interlocked.Increment(ref _requestCounter)}";
        request["@extra"] = extra;

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[extra] = tcs;

        var json = JsonSerializer.Serialize(request);
        var ptr = Marshal.StringToCoTaskMemUTF8(json);
        lock (_sendLock)
        {
            td_send(_clientId, ptr);
        }
        Marshal.FreeCoTaskMem(ptr);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(effectiveTimeout);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _pendingRequests.TryRemove(extra, out _);
            throw new TimeoutException($"TDLib request timeout for @type={request["@type"]}");
        }
    }

    public async Task CloseAsync(TimeSpan timeout)
    {
        if (_disposed) return;

        try
        {
            await SendRequestAsync(new Dictionary<string, object> { ["@type"] = "close" }, timeout).ConfigureAwait(false);
        }
        catch (TimeoutException) { }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_lastAuthState == "authorizationStateClosed") break;
            await Task.Delay(50);
        }

        _loopCts?.Cancel();
        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask.ConfigureAwait(false); } catch { }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IntPtr ptr;
            try
            {
                lock (_sendLock)
                {
                    ptr = td_receive(1.0);
                }
            }
            catch { break; }

            if (ptr == IntPtr.Zero)
            {
                await Task.Delay(5, ct).ConfigureAwait(false);
                continue;
            }

            var raw = Marshal.PtrToStringUTF8(ptr);
            if (string.IsNullOrEmpty(raw)) continue;

            if (DebugOutput) Console.WriteLine($"[DBG] RAW: {(raw.Length > 120 ? raw.Substring(0, 120) + "..." : raw)}");

            JsonDocument doc;
            try { doc = JsonDocument.Parse(raw); }
            catch { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                var type = root.GetProperty("@type").GetString();
                if (string.IsNullOrEmpty(type)) continue;

                if (type == "updateAuthorizationState")
                {
                    var state = root.GetProperty("authorization_state").GetProperty("@type").GetString();
                    if (!string.IsNullOrEmpty(state))
                    {
                        _lastAuthState = state;
                        if (DebugOutput) Console.WriteLine($"[DBG] AuthState: {state}");
                        AuthStateChanged?.Invoke(state);
                    }
                    continue;
                }

                if (root.TryGetProperty("@extra", out var extraEl))
                {
                    var extra = extraEl.GetString();
                    if (!string.IsNullOrEmpty(extra) && _pendingRequests.TryRemove(extra, out var tcs))
                    {
                        tcs.TrySetResult(root.Clone());
                        ResponseReceived?.Invoke(type, root.Clone());
                        continue;
                    }
                }

                if (type.StartsWith("update"))
                {
                    var cloned = root.Clone();
                    UpdateReceived?.Invoke(type, cloned);
                    if (_updateHandlers.TryGetValue(type, out var handler))
                    {
                        try { handler(cloned); } catch { }
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try { await CloseAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
        catch { }

        _loopCts?.Cancel();
        _loopCts?.Dispose();

        foreach (var kvp in _pendingRequests)
            kvp.Value.TrySetCanceled();
        _pendingRequests.Clear();
    }
}
