using System.Text.Json;

namespace AriadGSM.Perception.Events;

public sealed class PerceptionEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;

    public PerceptionEventWriter(string path)
    {
        _path = path;
    }

    public async ValueTask AppendAsync(PerceptionEvent perceptionEvent, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(perceptionEvent, JsonOptions);
        await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }
}
