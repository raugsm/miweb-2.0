namespace AriadGSM.Hands.Perception;

public sealed class PerceptionContext
{
    public string SourcePerceptionEventId { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    public IReadOnlyList<VisibleConversation> Conversations { get; init; } = [];

    public IReadOnlyList<VisibleChatRow> ChatRows { get; init; } = [];

    public bool HasAnyConversation => Conversations.Count > 0;

    public bool ContainsChannel(string? channelId)
    {
        return !string.IsNullOrWhiteSpace(channelId)
            && Conversations.Any(item => string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase));
    }

    public bool ContainsConversation(string? conversationId)
    {
        return !string.IsNullOrWhiteSpace(conversationId)
            && Conversations.Any(item => string.Equals(item.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase));
    }

    public VisibleConversation? BestForChannel(string? channelId)
    {
        return Conversations
            .Where(item => string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Confidence)
            .FirstOrDefault();
    }

    public VisibleChatRow? BestChatRow(string? channelId, string? title)
    {
        var rows = ChatRows
            .Where(item => string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalizedTitle = Normalize(title);
            var exact = rows
                .Where(item => Normalize(item.Title).Equals(normalizedTitle, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (exact is not null)
            {
                return exact;
            }

            var fuzzy = rows
                .Where(item =>
                    Normalize(item.Title).Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase)
                    || normalizedTitle.Contains(Normalize(item.Title), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (fuzzy is not null)
            {
                return fuzzy;
            }
        }

        return null;
    }

    private static string Normalize(string? value)
    {
        return string.Join(" ", (value ?? string.Empty).ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
