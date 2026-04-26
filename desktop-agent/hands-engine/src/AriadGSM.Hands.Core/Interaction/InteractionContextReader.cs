using System.Text;
using System.Text.Json;

namespace AriadGSM.Hands.Interaction;

public sealed class InteractionContextReader
{
    private readonly string _path;
    private readonly int _limit;

    public InteractionContextReader(string path, int limit)
    {
        _path = path;
        _limit = Math.Max(1, limit);
    }

    public async ValueTask<InteractionContext> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new InteractionContext();
        }

        var lines = (await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(_limit)
            .Reverse();

        foreach (var line in lines)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!TryGetString(root, "eventType").Equals("interaction_event", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targets = new List<InteractionTarget>();
                if (root.TryGetProperty("targets", out var targetArray) && targetArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in targetArray.EnumerateArray())
                    {
                        targets.Add(new InteractionTarget(
                            TryGetString(item, "targetId"),
                            TryGetString(item, "targetType"),
                            TryGetString(item, "channelId"),
                            TryGetString(item, "sourcePerceptionEventId"),
                            TryGetDateTimeOffset(item, "observedAt"),
                            TryGetString(item, "title"),
                            TryGetString(item, "preview"),
                            TryGetInt(item, "unreadCount", 0),
                            TryGetInt(item, "left", 0),
                            TryGetInt(item, "top", 0),
                            TryGetInt(item, "width", 0),
                            TryGetInt(item, "height", 0),
                            TryGetInt(item, "clickX", 0),
                            TryGetInt(item, "clickY", 0),
                            TryGetDouble(item, "confidence", 0),
                            TryGetBool(item, "actionable", false),
                            TryGetString(item, "category"),
                            ReadStringArray(item, "rejectionReasons")));
                    }
                }

                return new InteractionContext
                {
                    SourceInteractionEventId = TryGetString(root, "interactionEventId"),
                    LatestPerceptionEventId = TryGetString(root, "latestPerceptionEventId"),
                    CreatedAt = TryGetDateTimeOffset(root, "createdAt"),
                    Targets = targets
                };
            }
            catch (JsonException)
            {
            }
        }

        return new InteractionContext();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
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

    private static bool TryGetBool(JsonElement element, string name, bool fallback)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : fallback;
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

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
