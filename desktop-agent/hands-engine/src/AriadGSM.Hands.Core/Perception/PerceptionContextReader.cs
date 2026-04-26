using System.Text.Json;

namespace AriadGSM.Hands.Perception;

public sealed class PerceptionContextReader
{
    private readonly string _path;
    private readonly int _limit;

    public PerceptionContextReader(string path, int limit)
    {
        _path = path;
        _limit = Math.Max(1, limit);
    }

    public async ValueTask<PerceptionContext> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new PerceptionContext();
        }

        var lines = (await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(_limit)
            .Reverse();

        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetString(root, "eventType").Equals("perception_event", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rootChannelId = TryGetString(root, "channelId");
                var items = new List<VisibleConversation>();
                var chatRows = new List<VisibleChatRow>();
                if (root.TryGetProperty("objects", out var objects) && objects.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in objects.EnumerateArray())
                    {
                        var objectType = TryGetString(item, "objectType");
                        var metadata = item.TryGetProperty("metadata", out var metadataValue)
                            ? metadataValue
                            : default;
                        var channelId = TryGetString(metadata, "channelId");
                        if (string.IsNullOrWhiteSpace(channelId))
                        {
                            channelId = rootChannelId;
                        }

                        if (objectType.Equals("chat_row", StringComparison.OrdinalIgnoreCase))
                        {
                            var bounds = item.TryGetProperty("bounds", out var boundsValue) ? boundsValue : default;
                            var row = new VisibleChatRow(
                                string.IsNullOrWhiteSpace(channelId) ? null : channelId,
                                TryGetString(metadata, "chatRowId"),
                                FirstUseful(TryGetString(metadata, "title"), TryGetString(item, "text")),
                                TryGetString(metadata, "preview"),
                                TryGetInt(metadata, "unreadCount", 0),
                                TryGetInt(bounds, "left", 0),
                                TryGetInt(bounds, "top", 0),
                                TryGetInt(bounds, "width", 0),
                                TryGetInt(bounds, "height", 0),
                                TryGetInt(metadata, "clickX", 0),
                                TryGetInt(metadata, "clickY", 0),
                                TryGetDouble(item, "confidence", 0));
                            if (!string.IsNullOrWhiteSpace(row.ChannelId)
                                && !string.IsNullOrWhiteSpace(row.ChatRowId)
                                && row.ClickX > 0
                                && row.ClickY > 0)
                            {
                                chatRows.Add(row);
                            }

                            continue;
                        }

                        if (!IsUsefulObject(objectType))
                        {
                            continue;
                        }

                        var conversationId = TryGetString(metadata, "conversationId");
                        var title = TryGetString(item, "text");
                        var role = TryGetString(item, "role");
                        var confidence = TryGetDouble(item, "confidence", 0);

                        if (string.IsNullOrWhiteSpace(channelId) && string.IsNullOrWhiteSpace(conversationId))
                        {
                            continue;
                        }

                        items.Add(new VisibleConversation(
                            string.IsNullOrWhiteSpace(channelId) ? null : channelId,
                            string.IsNullOrWhiteSpace(conversationId) ? null : conversationId,
                            string.IsNullOrWhiteSpace(title) ? null : title,
                            string.IsNullOrWhiteSpace(role) ? objectType : role,
                            confidence));
                    }
                }

                return new PerceptionContext
                {
                    SourcePerceptionEventId = TryGetString(root, "perceptionEventId"),
                    ObservedAt = TryGetDateTimeOffset(root, "observedAt"),
                    Conversations = items
                        .GroupBy(item => $"{item.ChannelId}|{item.ConversationId}|{item.Role}", StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.OrderByDescending(item => item.Confidence).First())
                        .OrderBy(item => item.ChannelId)
                        .ThenByDescending(item => item.Confidence)
                        .ToArray(),
                    ChatRows = chatRows
                        .GroupBy(item => item.ChatRowId, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.OrderByDescending(item => item.Confidence).First())
                        .OrderBy(item => item.ChannelId)
                        .ThenBy(item => item.Top)
                        .ToArray()
                };
            }
            catch (JsonException)
            {
            }
        }

        return new PerceptionContext();
    }

    private static bool IsUsefulObject(string objectType)
    {
        return objectType.Equals("conversation", StringComparison.OrdinalIgnoreCase)
            || objectType.Equals("window", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static double TryGetDouble(JsonElement element, string name, double fallback)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var result)
                ? result
                : fallback;
    }

    private static int TryGetInt(JsonElement element, string name, int fallback)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var result)
                ? result
                : fallback;
    }

    private static string FirstUseful(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static DateTimeOffset TryGetDateTimeOffset(JsonElement element, string name)
    {
        var raw = TryGetString(element, name);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }
}
