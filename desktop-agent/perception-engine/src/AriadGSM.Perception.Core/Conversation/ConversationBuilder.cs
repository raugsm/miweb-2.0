using System.Security.Cryptography;
using System.Text;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Extraction;
using AriadGSM.Perception.Reader;

namespace AriadGSM.Perception.Conversation;

public sealed class ConversationBuilder
{
    private readonly PerceptionOptions _options;

    public ConversationBuilder(PerceptionOptions options)
    {
        _options = options;
    }

    public ConversationEvent Build(
        ResolvedChannel channel,
        ReaderCoreResult readerResult,
        IReadOnlyList<ExtractedMessage> messages,
        ConversationIdentity identity)
    {
        var unique = messages
            .GroupBy(message => message.MessageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(message => new ConversationMessage(
                message.MessageId,
                message.Text,
                message.Direction,
                message.SenderName,
                message.SentAt,
                message.Confidence,
                message.Signals))
            .ToArray();

        return new ConversationEvent(
            "conversation_event",
            $"conversation-{readerResult.ChannelId}-{ShortHash(readerResult.Source + readerResult.Status + string.Join("|", unique.Select(item => item.MessageId)))}",
            identity.ConversationId,
            channel.ChannelId,
            DateTimeOffset.UtcNow,
            identity.Title,
            "live",
            unique,
            new ConversationTimeline(_options.HistoryLimitDays, false, null, "message_id_hash"));
    }

    private static string ShortHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..12];
    }
}
