using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;

var requestJson = Console.In.ReadToEnd();
if (string.IsNullOrWhiteSpace(requestJson))
{
    Console.WriteLine("""{"ok":false,"state":"invalid_request","message":"No recibi solicitud"}""");
    return;
}

var request = JsonNode.Parse(requestJson)?.AsObject() ?? new JsonObject();
var action = request["action"]?.GetValue<string>() ?? "status";
var tdjsonDllPath = request["tdjsonDllPath"]?.GetValue<string>() ?? "";
var dataDir = request["dataDir"]?.GetValue<string>() ?? "";
var apiId = request["apiId"]?.GetValue<string>() ?? "";
var apiHash = request["apiHash"]?.GetValue<string>() ?? "";
var phoneNumber = request["phoneNumber"]?.GetValue<string>() ?? "";

if (string.IsNullOrWhiteSpace(tdjsonDllPath) || !File.Exists(tdjsonDllPath))
{
    WriteJson(new
    {
        ok = false,
        state = "missing_tdjson",
        message = "No encontre tdjson.dll en la ruta indicada"
    });
    return;
}

if (!string.IsNullOrWhiteSpace(dataDir))
{
    Directory.CreateDirectory(dataDir);
}

NativeMethods.SetDllDirectory(Path.GetDirectoryName(tdjsonDllPath)!);

using var client = new TdClient(apiId, apiHash, phoneNumber, dataDir);
try
{
    var result = action switch
    {
        "status" => client.GetStatus(),
        "start-auth" => client.StartAuth(),
        "submit-code" => client.SubmitCode(request["code"]?.GetValue<string>() ?? ""),
        "submit-password" => client.SubmitPassword(request["password"]?.GetValue<string>() ?? ""),
        "list-chats" => client.ListChats(),
        "fetch-history" => client.FetchHistory(request["chatId"]?.ToString() ?? "", request["limit"]?.GetValue<int>() ?? 50),
        _ => new JsonObject
        {
            ["ok"] = false,
            ["state"] = "unknown_action",
            ["message"] = $"Accion no soportada: {action}"
        }
    };

    WriteJson(result);
}
catch (Exception ex)
{
    WriteJson(new
    {
        ok = false,
        state = "bridge_error",
        message = ex.Message
    });
}

static void WriteJson(object payload)
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = false
    };
    Console.WriteLine(JsonSerializer.Serialize(payload, options));
}

internal static class NativeMethods
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetDllDirectory(string lpPathName);
}

internal sealed class TdClient : IDisposable
{
    private readonly string _apiId;
    private readonly string _apiHash;
    private readonly string _phoneNumber;
    private readonly string _dataDir;
    private readonly IntPtr _client;
    private long _requestId = 0;

    internal TdClient(string apiId, string apiHash, string phoneNumber, string dataDir)
    {
        _apiId = apiId;
        _apiHash = apiHash;
        _phoneNumber = phoneNumber;
        _dataDir = string.IsNullOrWhiteSpace(dataDir) ? Path.Combine(AppContext.BaseDirectory, "tdlib-data") : dataDir;
        Directory.CreateDirectory(_dataDir);

        NativeTd.Execute("""{"@type":"setLogVerbosityLevel","new_verbosity_level":1}""");
        _client = NativeTd.Create();
        if (_client == IntPtr.Zero)
        {
            throw new InvalidOperationException("No pude crear el cliente TDLib");
        }
    }

    internal JsonObject GetStatus()
    {
        var state = WaitForAuthorizationState();
        return BuildStatus(state);
    }

    internal JsonObject StartAuth()
    {
        var state = WaitForAuthorizationState();
        var stateType = state?["@type"]?.GetValue<string>() ?? "";

        if (stateType == "authorizationStateWaitTdlibParameters")
        {
            Send(new JsonObject
            {
                ["@type"] = "setTdlibParameters",
                ["parameters"] = new JsonObject
                {
                    ["use_test_dc"] = false,
                    ["database_directory"] = _dataDir,
                    ["files_directory"] = Path.Combine(_dataDir, "files"),
                    ["use_file_database"] = false,
                    ["use_chat_info_database"] = true,
                    ["use_message_database"] = true,
                    ["use_secret_chats"] = false,
                    ["api_id"] = int.TryParse(_apiId, out var parsedId) ? parsedId : 0,
                    ["api_hash"] = _apiHash,
                    ["system_language_code"] = "es",
                    ["device_model"] = "Windows",
                    ["system_version"] = Environment.OSVersion.VersionString,
                    ["application_version"] = "1.0",
                }
            });
            state = WaitForAuthorizationState();
            stateType = state?["@type"]?.GetValue<string>() ?? "";
        }

        if (stateType == "authorizationStateWaitEncryptionKey")
        {
            Send(new JsonObject
            {
                ["@type"] = "checkDatabaseEncryptionKey",
                ["encryption_key"] = ""
            });
            state = WaitForAuthorizationState();
            stateType = state?["@type"]?.GetValue<string>() ?? "";
        }

        if (stateType == "authorizationStateWaitPhoneNumber")
        {
            if (string.IsNullOrWhiteSpace(_phoneNumber))
            {
                return new JsonObject
                {
                    ["ok"] = false,
                    ["state"] = "authorizationStateWaitPhoneNumber",
                    ["message"] = "Falta el numero de telefono en la configuracion"
                };
            }

            Send(new JsonObject
            {
                ["@type"] = "setAuthenticationPhoneNumber",
                ["phone_number"] = _phoneNumber,
                ["settings"] = new JsonObject
                {
                    ["allow_flash_call"] = false,
                    ["allow_missed_call"] = false,
                    ["is_current_phone_number"] = true,
                    ["allow_sms_retriever_api"] = false
                }
            });
            state = WaitForAuthorizationState();
        }

        return BuildStatus(state);
    }

    internal JsonObject SubmitCode(string code)
    {
        Send(new JsonObject
        {
            ["@type"] = "checkAuthenticationCode",
            ["code"] = code
        });

        var state = WaitForAuthorizationState();
        return BuildStatus(state);
    }

    internal JsonObject SubmitPassword(string password)
    {
        Send(new JsonObject
        {
            ["@type"] = "checkAuthenticationPassword",
            ["password"] = password
        });

        var state = WaitForAuthorizationState();
        return BuildStatus(state);
    }

    internal JsonObject ListChats()
    {
        EnsureReady();
        Send(new JsonObject
        {
            ["@type"] = "loadChats",
            ["chat_list"] = new JsonObject { ["@type"] = "chatListMain" },
            ["limit"] = 100
        });

        var chatsResponse = SendAndWait(new JsonObject
        {
            ["@type"] = "getChats",
            ["chat_list"] = new JsonObject { ["@type"] = "chatListMain" },
            ["limit"] = 100
        });

        var chatIds = chatsResponse?["chat_ids"]?.AsArray() ?? new JsonArray();
        var chats = new JsonArray();

        foreach (var chatIdNode in chatIds)
        {
            var chatId = chatIdNode?.ToString();
            if (string.IsNullOrWhiteSpace(chatId))
            {
                continue;
            }

            var chat = SendAndWait(new JsonObject
            {
                ["@type"] = "getChat",
                ["chat_id"] = long.Parse(chatId)
            });

            var type = chat?["type"]?["@type"]?.GetValue<string>() ?? "unknown";
            var username = chat?["usernames"]?["editable_username"]?.GetValue<string>()
                ?? chat?["usernames"]?["active_usernames"]?.AsArray()?.FirstOrDefault()?.ToString()
                ?? "";

            chats.Add(new JsonObject
            {
                ["chatId"] = chatId,
                ["title"] = chat?["title"]?.GetValue<string>() ?? $"Chat {chatId}",
                ["chatType"] = type,
                ["username"] = username
            });
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["state"] = "ready",
            ["message"] = $"Descubri {chats.Count} chats",
            ["chats"] = chats
        };
    }

    internal JsonObject FetchHistory(string chatId, int limit)
    {
        EnsureReady();
        if (!long.TryParse(chatId, out var parsedChatId))
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["state"] = "invalid_chat",
                ["message"] = "chatId invalido"
            };
        }

        var response = SendAndWait(new JsonObject
        {
            ["@type"] = "getChatHistory",
            ["chat_id"] = parsedChatId,
            ["from_message_id"] = 0,
            ["offset"] = 0,
            ["limit"] = Math.Clamp(limit, 1, 100),
            ["only_local"] = false
        });

        var messages = new JsonArray();
        foreach (var messageNode in response?["messages"]?.AsArray() ?? new JsonArray())
        {
            var message = messageNode?.AsObject();
            if (message is null)
            {
                continue;
            }

            var contentType = message["content"]?["@type"]?.GetValue<string>() ?? "";
            var text = contentType == "messageText"
                ? message["content"]?["text"]?["text"]?.GetValue<string>() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            messages.Add(new JsonObject
            {
                ["messageId"] = message["id"]?.ToString() ?? "",
                ["senderName"] = ResolveSenderName(message),
                ["text"] = text,
                ["sentAt"] = ToIsoTimestamp(message["date"]?.GetValue<long>() ?? 0),
            });
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["state"] = "ready",
            ["chatId"] = chatId,
            ["messages"] = messages
        };
    }

    private void EnsureReady()
    {
        var status = GetStatus();
        if (status["state"]?.GetValue<string>() != "authorizationStateReady")
        {
            throw new InvalidOperationException(status["message"]?.GetValue<string>() ?? "Telegram todavia no esta listo");
        }
    }

    private string ResolveSenderName(JsonObject message)
    {
        var sender = message["sender_id"]?.AsObject();
        var senderType = sender?["@type"]?.GetValue<string>() ?? "";
        if (senderType == "messageSenderChat")
        {
            return $"chat:{sender?["chat_id"]}";
        }
        if (senderType == "messageSenderUser")
        {
            return $"user:{sender?["user_id"]}";
        }
        return "desconocido";
    }

    private JsonObject BuildStatus(JsonObject? state)
    {
        var stateType = state?["@type"]?.GetValue<string>() ?? "unknown";
        var friendlyMessage = stateType switch
        {
            "authorizationStateReady" => "Telegram listo para sincronizar",
            "authorizationStateWaitPhoneNumber" => "Falta enviar el numero de telefono",
            "authorizationStateWaitCode" => "Telegram espera el codigo que te enviaron",
            "authorizationStateWaitPassword" => "Telegram espera la contrasena de dos pasos",
            "authorizationStateWaitTdlibParameters" => "TDLib espera parametros de inicio",
            "authorizationStateWaitEncryptionKey" => "TDLib espera confirmar la base local",
            _ => $"Estado actual: {stateType}"
        };

        return new JsonObject
        {
            ["ok"] = stateType == "authorizationStateReady",
            ["state"] = stateType,
            ["message"] = friendlyMessage
        };
    }

    private JsonObject? WaitForAuthorizationState(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var update = Receive(1.0);
            if (update is null)
            {
                continue;
            }

            var updateType = update["@type"]?.GetValue<string>() ?? "";
            if (updateType == "updateAuthorizationState")
            {
                return update["authorization_state"]?.AsObject();
            }
        }

        return null;
    }

    private JsonObject? SendAndWait(JsonObject request, int timeoutMs = 20000)
    {
        var extra = Interlocked.Increment(ref _requestId).ToString();
        request["@extra"] = extra;
        Send(request);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var update = Receive(1.0);
            if (update is null)
            {
                continue;
            }

            if (update["@extra"]?.GetValue<string>() == extra)
            {
                return update;
            }

            var updateType = update["@type"]?.GetValue<string>() ?? "";
            if (updateType == "error")
            {
                throw new InvalidOperationException(update["message"]?.GetValue<string>() ?? "TDLib devolvio un error");
            }
        }

        throw new TimeoutException("TDLib no respondio a tiempo");
    }

    private void Send(JsonObject request)
    {
        NativeTd.Send(_client, request.ToJsonString());
    }

    private JsonObject? Receive(double timeoutSeconds)
    {
        var raw = NativeTd.Receive(_client, timeoutSeconds);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        return JsonNode.Parse(raw)?.AsObject();
    }

    public void Dispose()
    {
        if (_client != IntPtr.Zero)
        {
            NativeTd.Destroy(_client);
        }
    }

    private static string ToIsoTimestamp(long unixSeconds)
    {
        if (unixSeconds <= 0)
        {
          return DateTime.UtcNow.ToString("O");
        }
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString("O");
    }
}

internal static class NativeTd
{
    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_json_client_create();

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_json_client_send(IntPtr client, [MarshalAs(UnmanagedType.LPUTF8Str)] string request);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_json_client_receive(IntPtr client, double timeout);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr td_json_client_execute([MarshalAs(UnmanagedType.LPUTF8Str)] string request);

    [DllImport("tdjson", CallingConvention = CallingConvention.Cdecl)]
    private static extern void td_json_client_destroy(IntPtr client);

    internal static IntPtr Create() => td_json_client_create();
    internal static void Send(IntPtr client, string request) => td_json_client_send(client, request);
    internal static string? Receive(IntPtr client, double timeout)
    {
        var pointer = td_json_client_receive(client, timeout);
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }
    internal static string? Execute(string request)
    {
        var pointer = td_json_client_execute(request);
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }
    internal static void Destroy(IntPtr client) => td_json_client_destroy(client);
}
