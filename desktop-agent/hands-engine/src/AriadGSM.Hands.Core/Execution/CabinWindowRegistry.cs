using System.Text.Json;

namespace AriadGSM.Hands.Execution;

public sealed class CabinWindowRegistry
{
    private readonly string _path;

    public CabinWindowRegistry(string path)
    {
        _path = path;
    }

    public CabinWindowIdentity? Find(string? channelId)
    {
        if (string.IsNullOrWhiteSpace(channelId) || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_path));
            var root = document.RootElement;
            if (!root.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in channels.EnumerateArray())
            {
                if (!StringValue(item, "channelId").Equals(channelId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!item.TryGetProperty("window", out var window) || window.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var bounds = window.TryGetProperty("bounds", out var boundsValue) && boundsValue.ValueKind == JsonValueKind.Object
                    ? new CabinWindowBounds(
                        IntValue(boundsValue, "left"),
                        IntValue(boundsValue, "top"),
                        IntValue(boundsValue, "width"),
                        IntValue(boundsValue, "height"))
                    : null;

                return new CabinWindowIdentity(
                    channelId,
                    StringValue(item, "browser"),
                    StringValue(item, "status"),
                    BoolValue(item, "isReady"),
                    IntValue(window, "processId"),
                    StringValue(window, "processName"),
                    StringValue(window, "title"),
                    bounds);
            }
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string StringValue(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static int IntValue(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : 0;
    }

    private static bool BoolValue(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }
}

public sealed record CabinWindowIdentity(
    string ChannelId,
    string Browser,
    string Status,
    bool IsReady,
    int ProcessId,
    string ProcessName,
    string Title,
    CabinWindowBounds? Bounds);

public sealed record CabinWindowBounds(int Left, int Top, int Width, int Height);
