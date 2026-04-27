using System.Text;
using System.Text.Json;

namespace AriadGSM.Orchestrator.Runtime;

internal static class RuntimeJson
{
    public static JsonDocument? ReadDocument(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(ReadAllTextShared(path));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static IReadOnlyList<JsonDocument> ReadJsonlTail(string path, int tailLines)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var documents = new List<JsonDocument>();
        foreach (var line in ReadTailLinesShared(path, Math.Max(1, tailLines)).Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            try
            {
                documents.Add(JsonDocument.Parse(line));
            }
            catch (JsonException)
            {
            }
        }

        return documents;
    }

    public static string String(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public static bool Bool(JsonElement element, bool fallback, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
        }

        return fallback;
    }

    public static int Int(JsonElement element, int fallback, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var result))
            {
                return result;
            }
        }

        return fallback;
    }

    public static DateTimeOffset? Date(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var raw = String(element, name);
            if (DateTimeOffset.TryParse(raw, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    public static void DisposeAll(IEnumerable<JsonDocument> documents)
    {
        foreach (var document in documents)
        {
            document.Dispose();
        }
    }

    public static async ValueTask WriteTextAtomicAsync(string path, string text, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 7)
                {
                    await Task.Delay(25 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<string> ReadTailLinesShared(string path, int maxLines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(maxLines)
            .ToArray();
    }
}
