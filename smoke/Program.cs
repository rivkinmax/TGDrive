using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace smoke;

internal static class Program
{
    private const string KeysPath = @"c:\work\TGDrive\input.txt";
    private const string DbDir = @"c:\work\TGDrive\data\td";
    private const string FilesDir = @"c:\work\TGDrive\data\files";

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern int td_create_client_id();

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_send(int clientId, IntPtr request);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_receive(double timeout);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_set_log_verbosity_level(int level);

    private static void Send(int clientId, Dictionary<string, object> request)
    {
        var json = JsonSerializer.Serialize(request);
        var ptr = Marshal.StringToCoTaskMemUTF8(json);
        try { td_send(clientId, ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }

    private static string? Receive(double timeout)
    {
        var ptr = td_receive(timeout);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }

    private sealed class Keys
    {
        public string ApiId = "";
        public string ApiHash = "";
        public string? PhoneNumber;
    }

    private static Keys ReadKeys()
    {
        if (!File.Exists(KeysPath))
            throw new FileNotFoundException("Keys file not found: " + KeysPath);

        var result = new Keys();

        foreach (var rawLine in File.ReadAllLines(KeysPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line.Substring(0, eq).Trim().ToLowerInvariant().Replace("_", "");
            var value = line.Substring(eq + 1).Trim().Trim('"');
            if (value.Length == 0) continue;

            switch (key)
            {
                case "apiid": result.ApiId = value; break;
                case "apihash": result.ApiHash = value; break;
                case "phonenumber":
                case "phone": result.PhoneNumber = value; break;
            }
        }

        if (result.ApiId.Length == 0) throw new InvalidDataException("api_id not found in " + KeysPath);
        if (result.ApiHash.Length == 0) throw new InvalidDataException("api_hash not found in " + KeysPath);

        return result;
    }

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "updateOption", "updateConnectionState", "updateDefaultBackground",
        "updateFileDownloads", "updateAnimationSearchParameters",
        "updateGroupCallMessageLevels", "updateTrustedMiniAppBots",
        "updateAllowedBotUsernamePremiumSuffixes"
    };

    private static int Main()
    {
        td_set_log_verbosity_level(0);
        Console.WriteLine("TDLib smoke test. Keys: " + KeysPath);

        var keys = ReadKeys();
        if (!int.TryParse(keys.ApiId, out var apiId))
            throw new InvalidDataException("api_id is not an integer: '" + keys.ApiId + "'");

        var phone = keys.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phone))
        {
            Console.Write("PhoneNumber not found in input.txt. Enter TEST account phone: ");
            phone = Console.ReadLine()?.Trim();
        }
        if (string.IsNullOrWhiteSpace(phone))
        {
            Console.WriteLine("FAIL: empty phone number.");
            return 1;
        }
        Console.WriteLine("Phone source: " + (keys.PhoneNumber != null ? "input.txt" : "console"));

        Directory.CreateDirectory(DbDir);
        Directory.CreateDirectory(FilesDir);
        Console.WriteLine("TDLib database: " + DbDir);
        Console.WriteLine("TDLib files:    " + FilesDir);

        var clientId = td_create_client_id();

        Send(clientId, new Dictionary<string, object>
        {
            ["@type"] = "setTdlibParameters",
            ["use_test_dc"] = false,
            ["database_directory"] = DbDir,
            ["files_directory"] = FilesDir,
            ["database_encryption_key"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("tgdrive")),
            ["use_file_database"] = true,
            ["use_chat_info_database"] = true,
            ["use_message_database"] = true,
            ["use_secret_chats"] = false,
            ["api_id"] = apiId,
            ["api_hash"] = keys.ApiHash,
            ["system_language_code"] = "ru",
            ["device_model"] = "Desktop",
            ["system_version"] = "Windows",
            ["application_version"] = "0.1.0-smoke"
        });

        Console.WriteLine("Waiting for authorization state...");

        var deadline = DateTime.UtcNow.AddSeconds(180);
        var success = false;
        var closing = false;
        var exitCode = 1;

        while (DateTime.UtcNow < deadline)
        {
            var raw = Receive(1.0);
            if (raw == null) continue;

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var kind = root.GetProperty("@type").GetString() ?? "unknown";

            if (Noise.Contains(kind)) continue;

            if (kind == "error")
            {
                Console.WriteLine("TDLIB ERROR " + root.GetProperty("code").GetInt32()
                                  + ": " + root.GetProperty("message").GetString());
                continue;
            }

            if (kind != "updateAuthorizationState")
            {
                Console.WriteLine("event: " + kind);
                continue;
            }

            var stateType = root.GetProperty("authorization_state").GetProperty("@type").GetString() ?? "unknown";
            Console.WriteLine("auth state: " + stateType);

            switch (stateType)
            {
                case "authorizationStateWaitTdlibParameters":
                    // Initial state of the client. Parameters are processed after this.
                    // Just keep waiting for the next state.
                    break;

                case "authorizationStateWaitPhoneNumber":
                    Console.WriteLine("Sending phone number...");
                    Send(clientId, new Dictionary<string, object>
                    {
                        ["@type"] = "setAuthenticationPhoneNumber",
                        ["phone_number"] = phone!,
                        ["settings"] = new Dictionary<string, object>
                        {
                            ["@type"] = "phoneNumberAuthenticationSettings",
                            ["allow_flash_call"] = false,
                            ["allow_missed_call"] = false,
                            ["allow_sms_retriever"] = false,
                            ["authentication_token"] = ""
                        }
                    });
                    break;

                case "authorizationStateWaitCode":
                    Console.Write("Enter confirmation code (code is not logged): ");
                    var code = Console.ReadLine()?.Trim();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        Console.WriteLine("FAIL: empty code.");
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        closing = true;
                        break;
                    }
                    Console.WriteLine("Sending confirmation code...");
                    Send(clientId, new Dictionary<string, object>
                    {
                        ["@type"] = "checkAuthenticationCode",
                        ["code"] = code
                    });
                    break;

                case "authorizationStateWaitPassword":
                    Console.WriteLine("STOP: 2FA password required. Not implemented in this smoke step.");
                    exitCode = 3;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    closing = true;
                    break;

                case "authorizationStateReady":
                    Console.WriteLine("SUCCESS: account is authorized. Session stored in TDLib database.");
                    success = true;
                    exitCode = 0;
                    Console.WriteLine("Closing TDLib, waiting for authorizationStateClosed...");
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    closing = true;
                    break;

                case "authorizationStateClosing":
                    closing = true;
                    break;

                case "authorizationStateClosed":
                    Console.WriteLine("TDLib closed cleanly. Exit code " + exitCode + ".");
                    return exitCode;
            }
        }

        Console.WriteLine(closing
            ? "FAIL: timeout while waiting for authorizationStateClosed."
            : (success ? "FAIL: closed state not received." : "FAIL: timeout before authorization."));
        return 1;
    }
}