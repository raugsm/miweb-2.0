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
        await File.AppendAllTextAsync(_path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }
}
