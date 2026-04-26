using System.Text;
using System.Text.Json;

namespace AriadGSM.Interaction.Events;

public sealed class InteractionEventWriter
{
    private readonly string _path;

    public InteractionEventWriter(string path)
    {
        _path = path;
    }

    public async ValueTask<ISet<string>> ReadExistingEventIdsAsync(CancellationToken cancellationToken = default)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return result;
        }

        var lines = await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines.TakeLast(2500))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("interactionEventId", out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    var id = value.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        result.Add(id);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return result;
    }

    public async ValueTask AppendAsync(InteractionEvent interactionEvent, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(interactionEvent, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await AppendAllTextSharedAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static async ValueTask AppendAllTextSharedAsync(string path, string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}
