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
    private const long TargetChatId = -1004366704743;

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
        WaitingChat,
        WaitingSupergroup,
        WaitingFullInfo,
        Closing
    }

    private static int Main()
    {
        td_set_log_verbosity_level(0);
        Console.WriteLine("TDLib R2 check. Keys: " + KeysPath);

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

        Directory.CreateDirectory(DbDir);
        Directory.CreateDirectory(FilesDir);

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

        Console.WriteLine("Waiting for authorization...");
        var deadline = DateTime.UtcNow.AddSeconds(300);
        var state = State.Init;
        var exitCode = 1;
        long supergroupId = 0;
        string? supergroupUsernames = null;
        string? supergroupStatus = null;
        bool? canPost = null, canEdit = null, canDelete = null;

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
                exitCode = 2;
                Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                state = State.Closing;
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
                        Console.Write("Enter code: ");
                        var code = Console.ReadLine()?.Trim();
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                            state = State.Closing;
                            break;
                        }
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
                            Console.WriteLine("Authorized. Fetching chat info...");
                            Send(clientId, new Dictionary<string, object>
                            {
                                ["@type"] = "getChat",
                                ["@extra"] = "get_chat",
                                ["chat_id"] = TargetChatId
                            });
                            state = State.WaitingChat;
                        }
                        break;

                    case "authorizationStateClosing":
                        state = State.Closing;
                        break;

                    case "authorizationStateClosed":
                        Console.WriteLine("TDLib closed. Exit code " + exitCode + ".");
                        return exitCode;
                }
                continue;
            }

            if (extra == "get_chat" && state == State.WaitingChat)
            {
                if (kind == "chat")
                {
                    var title = root.GetProperty("title").GetString();
                    var type = root.GetProperty("type");
                    var chatType = type.GetProperty("@type").GetString();
                    Console.WriteLine("Chat title: " + title);
                    Console.WriteLine("Chat type: " + chatType);

                    if (chatType != "chatTypeSupergroup")
                    {
                        Console.WriteLine("FAIL: target chat is not a supergroup/channel.");
                        exitCode = 4;
                        Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                        state = State.Closing;
                        continue;
                    }

                    supergroupId = type.GetProperty("supergroup_id").GetInt64();
                    Console.WriteLine("supergroup_id: " + supergroupId);

                    Send(clientId, new Dictionary<string, object>
                    {
                        ["@type"] = "getSupergroup",
                        ["@extra"] = "get_supergroup",
                        ["supergroup_id"] = supergroupId
                    });
                    state = State.WaitingSupergroup;
                }
                else
                {
                    Console.WriteLine("FAIL: unexpected response to getChat: " + kind);
                    exitCode = 5;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                continue;
            }

            if (extra == "get_supergroup" && state == State.WaitingSupergroup)
            {
                if (kind == "supergroup")
                {
                    var hasUsername = false;
                    if (root.TryGetProperty("usernames", out var usernamesObj))
                    {
                        if (usernamesObj.TryGetProperty("active_usernames", out var activeArr) && activeArr.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var u in activeArr.EnumerateArray())
                                list.Add(u.GetString() ?? "");
                            supergroupUsernames = string.Join(",", list);
                            hasUsername = list.Count > 0;
                        }
                    }
                    else if (root.TryGetProperty("username", out var usernameProp))
                    {
                        var u = usernameProp.GetString();
                        if (!string.IsNullOrEmpty(u))
                        {
                            supergroupUsernames = u;
                            hasUsername = true;
                        }
                    }

                    Console.WriteLine("Public username(s): " + (supergroupUsernames ?? "(none)") + (hasUsername ? " [PUBLIC]" : " [PRIVATE]"));

                    var statusObj = root.GetProperty("status");
                    var statusType = statusObj.GetProperty("@type").GetString();
                    supergroupStatus = statusType;
                    Console.WriteLine("Member status: " + statusType);

                    if (statusType == "chatMemberStatusAdministrator")
                    {
                        canPost = statusObj.GetProperty("can_post_messages").GetBoolean();
                        canEdit = statusObj.GetProperty("can_edit_messages").GetBoolean();
                        canDelete = statusObj.GetProperty("can_delete_messages").GetBoolean();
                        Console.WriteLine("Admin rights: post=" + canPost + ", edit=" + canEdit + ", delete=" + canDelete);
                    }
                    else if (statusType == "chatMemberStatusCreator")
                    {
                        Console.WriteLine("Creator: full rights.");
                    }
                    else
                    {
                        Console.WriteLine("WARNING: not creator/admin, may lack permissions.");
                    }

                    Send(clientId, new Dictionary<string, object>
                    {
                        ["@type"] = "getSupergroupFullInfo",
                        ["@extra"] = "get_full_info",
                        ["supergroup_id"] = supergroupId
                    });
                    state = State.WaitingFullInfo;
                }
                else
                {
                    Console.WriteLine("FAIL: unexpected response to getSupergroup: " + kind);
                    exitCode = 6;
                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                continue;
            }

            if (extra == "get_full_info" && state == State.WaitingFullInfo)
            {
                if (kind == "supergroupFullInfo")
                {
                    var memberCount = root.GetProperty("member_count").GetInt32();
                    Console.WriteLine("member_count: " + memberCount);

                    if (memberCount > 1)
                    {
                        Console.WriteLine("R2 WARNING: member_count > 1. Needs confirmation.");
                    }
                    else
                    {
                        Console.WriteLine("R2 OK: member_count <= 1.");
                    }

                    var isPrivate = string.IsNullOrEmpty(supergroupUsernames);
                    var hasRights = supergroupStatus == "chatMemberStatusCreator" ||
                                    (canPost == true && canEdit == true && canDelete == true);

                    Console.WriteLine("=== R2 SUMMARY ===");
                    Console.WriteLine("Private (no username): " + isPrivate);
                    Console.WriteLine("Has admin rights: " + hasRights);
                    Console.WriteLine("Member count <= 1: " + (memberCount <= 1));

                    if (isPrivate && hasRights && memberCount <= 1)
                    {
                        Console.WriteLine("R2 CHECK PASSED.");
                        exitCode = 0;
                    }
                    else
                    {
                        Console.WriteLine("R2 CHECK FAILED OR NEEDS ATTENTION.");
                        exitCode = 7;
                    }

                    Send(clientId, new Dictionary<string, object> { ["@type"] = "close" });
                    state = State.Closing;
                }
                else
                {
                    Console.WriteLine("FAIL: unexpected response to getSupergroupFullInfo: " + kind);
                    exitCode = 8;
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