using System.Text;
using System.Text.Json;

namespace AriadGSM.Interaction.Perception;

public sealed class PerceptionEventReader
{
    private readonly string _path;
    private readonly int _limit;

    public PerceptionEventReader(string path, int limit)
    {
        _path = path;
        _limit = Math.Max(1, limit);
    }

    public async ValueTask<PerceptionSnapshot> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new PerceptionSnapshot(string.Empty, DateTimeOffset.MinValue, [], [], 0);
        }

        var rows = new List<PerceptionChatRow>();
        var conversations = new List<PerceptionConversation>();
        var latestId = string.Empty;
        var latestObservedAt = DateTimeOffset.MinValue;
        var eventsRead = 0;

        var lines = (await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(_limit);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetString(root, "eventType").Equals("perception_event", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                eventsRead++;
                var perceptionEventId = TryGetString(root, "perceptionEventId");
                var observedAt = TryGetDateTimeOffset(root, "observedAt");
                var rootChannelId = TryGetString(root, "channelId");
                if (!string.IsNullOrWhiteSpace(perceptionEventId))
                {
                    latestId = perceptionEventId;
                    latestObservedAt = observedAt;
                }

                if (!root.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in objects.EnumerateArray())
                {
                    var objectType = TryGetString(item, "objectType");
                    var metadata = item.TryGetProperty("metadata", out var metadataValue)
                        ? metadataValue
                        : default;
                    var channelId = FirstUseful(TryGetString(metadata, "channelId"), rootChannelId);

                    if (objectType.Equals("chat_row", StringComparison.OrdinalIgnoreCase))
                    {
                        var bounds = item.TryGetProperty("bounds", out var boundsValue) ? boundsValue : default;
                        rows.Add(new PerceptionChatRow(
                            perceptionEventId,
                            observedAt,
                            channelId,
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
                            TryGetDouble(item, "confidence", 0)));
                        continue;
                    }

                    if (objectType.Equals("conversation", StringComparison.OrdinalIgnoreCase)
                        || objectType.Equals("window", StringComparison.OrdinalIgnoreCase))
                    {
                        conversations.Add(new PerceptionConversation(
                            perceptionEventId,
                            observedAt,
                            channelId,
                            TryGetString(metadata, "conversationId"),
                            TryGetString(item, "text"),
                            FirstUseful(TryGetString(item, "role"), objectType),
                            TryGetDouble(item, "confidence", 0)));
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        var latestSourceByChannel = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ChannelId))
            .GroupBy(row => row.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(row => row.ObservedAt).First().SourcePerceptionEventId,
                StringComparer.OrdinalIgnoreCase);

        var latestRows = rows
            .Where(row => latestSourceByChannel.TryGetValue(row.ChannelId, out var sourceId)
                && string.Equals(row.SourcePerceptionEventId, sourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var latestConversations = conversations
            .Where(item => string.IsNullOrWhiteSpace(item.ChannelId)
                || !latestSourceByChannel.TryGetValue(item.ChannelId, out var sourceId)
                || string.Equals(item.SourcePerceptionEventId, sourceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var bestRows = latestRows
            .Where(row => !string.IsNullOrWhiteSpace(row.ChannelId) && !string.IsNullOrWhiteSpace(row.ChatRowId))
            .GroupBy(row => $"{row.ChannelId}|{row.ChatRowId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.ObservedAt).ThenByDescending(row => row.Confidence).First())
            .OrderBy(row => row.ChannelId)
            .ThenBy(row => row.Top)
            .ToArray();

        var bestConversations = latestConversations
            .Where(item => !string.IsNullOrWhiteSpace(item.ChannelId) || !string.IsNullOrWhiteSpace(item.ConversationId))
            .GroupBy(item => $"{item.ChannelId}|{item.ConversationId}|{item.Role}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.ObservedAt).ThenByDescending(item => item.Confidence).First())
            .OrderBy(item => item.ChannelId)
            .ThenByDescending(item => item.Confidence)
            .ToArray();

        return new PerceptionSnapshot(latestId, latestObservedAt, bestRows, bestConversations, eventsRead);
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

    private static DateTimeOffset TryGetDateTimeOffset(JsonElement element, string name)
    {
        var raw = TryGetString(element, name);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : DateTimeOffset.MinValue;
    }

    private static string FirstUseful(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }
}
