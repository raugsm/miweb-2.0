using AriadGSM.Perception.Semantics;

namespace AriadGSM.Perception.Conversation;

public sealed record ConversationMessage(
    string MessageId,
    string Text,
    string Direction,
    string? SenderName = null,
    DateTimeOffset? SentAt = null,
    double Confidence = 0,
    IReadOnlyList<BusinessSignal>? Signals = null);

public sealed record ConversationTimeline(
    int HistoryLimitDays,
    bool Complete,
    DateTimeOffset? OldestLoadedAt,
    string DedupeStrategy);

public sealed record ConversationEvent(
    string EventType,
    string ConversationEventId,
    string ConversationId,
    string ChannelId,
    DateTimeOffset ObservedAt,
    string? ConversationTitle,
    string Source,
    IReadOnlyList<ConversationMessage> Messages,
    ConversationTimeline Timeline)
{
    public static string ContractName => "conversation_event";
}
