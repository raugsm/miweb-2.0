using System.Text;
using System.Text.Json;

namespace AriadGSM.Hands.Decisions;

public sealed class DecisionEventReader
{
    private readonly IReadOnlyList<string> _paths;
    private readonly int _limit;

    public DecisionEventReader(IReadOnlyList<string> paths, int limit)
    {
        _paths = paths;
        _limit = Math.Max(1, limit);
    }

    public async ValueTask<IReadOnlyList<DecisionEvent>> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        var events = new List<DecisionEvent>();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var path in _paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var lines = (await ReadAllLinesSharedAsync(path, cancellationToken).ConfigureAwait(false))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(_limit);
            foreach (var line in lines)
            {
                try
                {
                    var item = JsonSerializer.Deserialize<DecisionEvent>(line, options);
                    if (item is not null && item.EventType == "decision_event" && !string.IsNullOrWhiteSpace(item.DecisionId))
                    {
                        events.Add(item);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }

        return events
            .GroupBy(item => item.DecisionId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.CreatedAt).First())
            .OrderBy(item => item.CreatedAt)
            .ToArray();
    }

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
