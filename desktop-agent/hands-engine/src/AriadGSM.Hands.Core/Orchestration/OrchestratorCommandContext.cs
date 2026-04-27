using System.Text;
using System.Text.Json;

namespace AriadGSM.Hands.Orchestration;

public sealed class OrchestratorCommandContext
{
    public bool ActionsAllowed { get; init; } = true;

    public IReadOnlySet<string> PausedChannels { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string Reason { get; init; } = string.Empty;

    public bool IsChannelPaused(string? channelId)
    {
        return string.IsNullOrWhiteSpace(channelId)
            || !ActionsAllowed
            || PausedChannels.Contains(channelId);
    }
}

public sealed class OrchestratorCommandReader
{
    private readonly string _path;
    private readonly bool _enabled;

    public OrchestratorCommandReader(string path, bool enabled)
    {
        _path = path;
        _enabled = enabled;
    }

    public async ValueTask<OrchestratorCommandContext> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled || !File.Exists(_path))
        {
            return new OrchestratorCommandContext();
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var pausedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("pausedChannels", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        pausedChannels.Add(item.GetString()!);
                    }
                }
            }

            return new OrchestratorCommandContext
            {
                ActionsAllowed = !root.TryGetProperty("actionsAllowed", out var actionsAllowed)
                    || actionsAllowed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                    || actionsAllowed.GetBoolean(),
                PausedChannels = pausedChannels,
                Reason = root.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String
                    ? reason.GetString() ?? string.Empty
                    : string.Empty
            };
        }
        catch (JsonException)
        {
            return new OrchestratorCommandContext();
        }
        catch (IOException)
        {
            return new OrchestratorCommandContext();
        }
    }
}
