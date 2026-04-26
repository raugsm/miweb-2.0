namespace AriadGSM.Hands.Perception;

public sealed class PerceptionContext
{
    public string SourcePerceptionEventId { get; init; } = "";

    public DateTimeOffset ObservedAt { get; init; }

    public IReadOnlyList<VisibleConversation> Conversations { get; init; } = [];

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
}
