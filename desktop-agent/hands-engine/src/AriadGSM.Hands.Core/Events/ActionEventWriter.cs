using System.Text.Json;

namespace AriadGSM.Hands.Events;

public sealed class ActionEventWriter
{
    private readonly string _path;

    public ActionEventWriter(string path)
    {
        _path = path;
    }

    public async ValueTask AppendAsync(ActionEvent actionEvent, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(actionEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<HashSet<string>> ReadExistingActionIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return ids;
        }

        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("actionId", out var actionId))
                {
                    ids.Add(actionId.GetString() ?? string.Empty);
                }
            }
            catch (JsonException)
            {
            }
        }

        ids.Remove(string.Empty);
        return ids;
    }
}
