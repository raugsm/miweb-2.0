using System.Text;
using System.Text.Json;

namespace AriadGSM.Perception.Conversation;

public sealed class ConversationEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;

    public ConversationEventWriter(string path)
    {
        _path = path;
    }

    public async ValueTask AppendAsync(ConversationEvent conversationEvent, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(conversationEvent, JsonOptions);
        await AppendLineSharedAsync(_path, json, cancellationToken).ConfigureAwait(false);
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
}
