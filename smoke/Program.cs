using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace smoke;

internal static class Program
{
    private const string KeysPath = @"c:\work\TGDrive\input.txt";
    private const string DbDir = @"c:\work\TGDrive\data\td";
    private const string FilesDir = @"c:\work\TGDrive\data\files";
    private const string TestDataDir = @"c:\work\TGDrive\data";
    private const long TargetChatId = -1004366704743;
    private const string TestFileName = "test_upload.txt";
    private const string TestFileContent = "Hello TGDrive smoke test!";

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

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "updateOption", "updateConnectionState", "updateDefaultBackground",
        "updateFileDownloads", "updateAnimationSearchParameters",
        "updateGroupCallMessageLevels", "updateTrustedMiniAppBots",
        "updateAllowedBotUsernamePremiumSuffixes", "updateSpeechRecognitionTrial",
        "updateUser", "updateAttachmentMenuBots", "updateDiceEmojis",
        "updateDefaultPaidReactionType", "updateTextCompositionStyles",
        "updateWebBrowserSettings", "updateChatFolders",
        "updateReactionNotificationSettings", "updateStoryStealthMode",
        "updateHavePendingNotifications", "updateDefaultReactionType",
        "updateProfileAccentColors", "updateScopeNotificationSettings",
        "updateAccentColors", "updateEmojiChatThemes", "updateUserStatus",
        "updateActiveEmojiReactions", "updateSupergroup", "updateNewChat",
        "updateChatNotificationSettings", "updateChatLastMessage",
        "updateChatReadInbox", "updateChatReadOutbox", "updateChatAddedToList",
        "updateChatPosition", "updateUnreadChatCount", "updateAvailableMessageEffects",
        "updateChatAvailableReactions", "updateSupergroupFullInfo", "updateUserFullInfo",
        "updateNewMessage", "updateMessageContent", "updateMessageSendSucceeded",
        "updateMessageSendAcknowledged"
    };

    private enum State
    {
        Init,
        Ready,
        WaitingSendResult,
        WaitingDownloadResult,
        Closing
    }

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
        Directory.CreateDirectory(TestDataDir);

        var testFilePath = Path.Combine(TestDataDir, TestFileName);
        File.WriteAllText(testFilePath, TestFileContent, Encoding.UTF8);
        var originalHash = ComputeSha256(testFilePath);
        Console.WriteLine("Created test file: " + testFilePath);
        Console.WriteLine("Original SHA256: " + originalHash);

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

        var deadline = DateTime.UtcNow.AddSeconds(300);
        var state = State.Init;
        var exitCode = 1;

        while (DateTime.UtcNow < deadline)
        {
            var raw = Receive(1.0);
            if (raw == null) continue;

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var kind = root.GetProperty("@type").GetString() ?? "unknown";
            var extra = root.TryGetProperty("@extra", out var extraProp) ? extraProp.GetString() : null;

            if (Noise.Contains(kind)) continue;

            if (kind == "error")
            {
                Console.WriteLine("TDLIB ERROR " + root.GetProperty("code").GetInt32()
                                  + " (extra=" + extra + "): " + root.GetProperty("message").GetString());
                if (extra == "send_msg" || extra == "download_file")
                {
                    exitCode = 2;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                continue;
            }

            if (kind == "updateAuthorizationState")
            {
                var stateType = root.GetProperty("authorization_state").GetProperty("@type").GetString() ?? "unknown";
                Console.WriteLine("auth state: " + stateType);

                switch (stateType)
                {
                    case "authorizationStateWaitTdlibParameters":
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
                            state = State.Closing;
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
                        Console.WriteLine("STOP: 2FA password required.");
                        exitCode = 3;
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        state = State.Closing;
                        break;

                    case "authorizationStateReady":
                        if (state == State.Init)
                        {
                            Console.WriteLine("SUCCESS: account is authorized.");
                            Console.WriteLine("Uploading test file to chat " + TargetChatId + "...");
                            
                            Send(clientId, new Dictionary<string, object>
                            {
                                ["@type"] = "sendMessage",
                                ["@extra"] = "send_msg",
                                ["chat_id"] = TargetChatId,
                                ["input_message_content"] = new Dictionary<string, object>
{
    ["@type"] = "inputMessageDocument",
    ["document"] = new Dictionary<string, object>
    {
        ["@type"] = "inputDocument",
        ["document"] = new Dictionary<string, object>
        {
            ["@type"] = "inputFileLocal",
            ["path"] = testFilePath
        }
    },
    ["caption"] = new Dictionary<string, object>
    {
        ["@type"] = "formattedText",
        ["text"] = "smoke test upload"
    }
}
                            });
                            state = State.WaitingSendResult;
                        }
                        break;

                    case "authorizationStateClosing":
                        state = State.Closing;
                        break;

                    case "authorizationStateClosed":
                        Console.WriteLine("TDLib closed cleanly. Exit code " + exitCode + ".");
                        return exitCode;
                }
                continue;
            }

            if (extra == "send_msg")
            {
                if (kind == "message")
                {
                    Console.WriteLine("Message sent successfully. Extracting file_id...");
                    var content = root.GetProperty("content");
                    var contentType = content.GetProperty("@type").GetString();
                    if (contentType != "messageDocument")
                    {
                        Console.WriteLine("FAIL: unexpected content type: " + contentType);
                        exitCode = 4;
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        state = State.Closing;
                        continue;
                    }
                    
                    var document = content.GetProperty("document");
                    var fileObj = document.GetProperty("document");
                    var fileId = fileObj.GetProperty("id").GetInt32();
                    Console.WriteLine("Extracted file_id: " + fileId);
                    
                    Console.WriteLine("Downloading file synchronously...");
                    Send(clientId, new Dictionary<string, object>
                    {
                        ["@type"] = "downloadFile",
                        ["@extra"] = "download_file",
                        ["file_id"] = fileId,
                        ["priority"] = 1,
                        ["offset"] = 0,
                        ["limit"] = 0,
                        ["synchronous"] = true
                    });
                    state = State.WaitingDownloadResult;
                }
                else
                {
                    Console.WriteLine("FAIL: unexpected response to sendMessage: " + kind);
                    exitCode = 5;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                continue;
            }

            if (extra == "download_file")
            {
                if (kind == "file")
                {
                    var local = root.GetProperty("local");
                    var isCompleted = local.GetProperty("is_downloading_completed").GetBoolean();
                    if (!isCompleted)
                    {
                        Console.WriteLine("FAIL: download not completed.");
                        exitCode = 6;
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        state = State.Closing;
                        continue;
                    }
                    
                    var downloadedPath = local.GetProperty("path").GetString();
                    Console.WriteLine("Downloaded to: " + downloadedPath);
                    
                    if (!File.Exists(downloadedPath))
                    {
                        Console.WriteLine("FAIL: downloaded file not found on disk.");
                        exitCode = 7;
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        state = State.Closing;
                        continue;
                    }
                    
                    var downloadedHash = ComputeSha256(downloadedPath);
                    Console.WriteLine("Downloaded SHA256: " + downloadedHash);
                    
                    if (originalHash == downloadedHash)
                    {
                        Console.WriteLine("SUCCESS: file hashes match!");
                        exitCode = 0;
                    }
                    else
                    {
                        Console.WriteLine("FAIL: file hashes DO NOT match.");
                        exitCode = 8;
                    }
                    
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                else
                {
                    Console.WriteLine("FAIL: unexpected response to downloadFile: " + kind);
                    exitCode = 9;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                continue;
            }

            Console.WriteLine("event: " + kind + (extra != null ? " (extra=" + extra + ")" : ""));
        }

        Console.WriteLine("FAIL: timeout.");
        return 1;
    }
}