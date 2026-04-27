using System.Text;
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
        await AppendLineSharedAsync(_path, json, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<HashSet<string>> ReadExistingActionIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return ids;
        }

        var lines = await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask<HashSet<string>> ReadProcessedDecisionKeysAsync(string executionMode, CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_path))
        {
            return ids;
        }

        var lines = await ReadAllLinesSharedAsync(_path, cancellationToken).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var status = root.TryGetProperty("status", out var statusElement)
                    ? statusElement.GetString() ?? string.Empty
                    : string.Empty;
                if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!root.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var mode = target.TryGetProperty("executionMode", out var modeElement)
                    ? modeElement.GetString() ?? string.Empty
                    : string.Empty;
                if (!mode.Equals(executionMode, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sourceDecisionId = target.TryGetProperty("sourceDecisionId", out var decisionElement)
                    ? decisionElement.GetString() ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(sourceDecisionId))
                {
                    ids.Add($"{sourceDecisionId}|{executionMode}");
                }
            }
            catch (JsonException)
            {
            }
        }

        return ids;
    }

    private static async ValueTask AppendLineSharedAsync(string path, string line, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                await Task.Delay(25 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<string[]> ReadAllLinesSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }
}
