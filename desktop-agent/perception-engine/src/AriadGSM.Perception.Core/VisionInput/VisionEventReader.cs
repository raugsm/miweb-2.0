using System.Text;
using System.Text.Json;

namespace AriadGSM.Perception.VisionInput;

public sealed class VisionEventReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public VisionEventReader(string path)
    {
        _path = path;
    }

    public async ValueTask<VisionEventEnvelope?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        var lines = await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false);
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return JsonSerializer.Deserialize<VisionEventEnvelope>(line, JsonOptions);
        }

        return null;
    }

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
