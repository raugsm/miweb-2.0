using System.Text;
using System.Text.Json;

namespace AriadGSM.Hands.Pipeline;

internal sealed class HandsCursorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;

    public HandsCursorStore(string path)
    {
        _path = path;
    }

    public async ValueTask<HandsCursorSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return new HandsCursorSnapshot([], []);
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var snapshot = await JsonSerializer.DeserializeAsync<HandsCursorSnapshot>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken).ConfigureAwait(false);
            return snapshot ?? new HandsCursorSnapshot([], []);
        }
        catch (JsonException)
        {
            return new HandsCursorSnapshot([], []);
        }
        catch (IOException)
        {
            return new HandsCursorSnapshot([], []);
        }
    }

    public async ValueTask SaveAsync(
        IEnumerable<string> processedDecisionKeys,
        IEnumerable<string> processedDecisionScopes,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new HandsCursorSnapshot(
            Trim(processedDecisionKeys, limit),
            Trim(processedDecisionScopes, limit));
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static IReadOnlyList<string> Trim(IEnumerable<string> values, int limit)
    {
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(Math.Max(100, limit))
            .ToArray();
    }
}

internal sealed record HandsCursorSnapshot(
    IReadOnlyList<string> ProcessedDecisionKeys,
    IReadOnlyList<string> ProcessedDecisionScopes);
