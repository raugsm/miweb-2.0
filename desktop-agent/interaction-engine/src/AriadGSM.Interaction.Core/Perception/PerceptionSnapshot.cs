namespace AriadGSM.Interaction.Perception;

public sealed record PerceptionSnapshot(
    string LatestPerceptionEventId,
    DateTimeOffset ObservedAt,
    IReadOnlyList<PerceptionChatRow> ChatRows,
    IReadOnlyList<PerceptionConversation> Conversations,
    int EventsRead);

public sealed record PerceptionChatRow(
    string SourcePerceptionEventId,
    DateTimeOffset ObservedAt,
    string ChannelId,
    string ChatRowId,
    string Title,
    string Preview,
    int UnreadCount,
    int Left,
    int Top,
    int Width,
    int Height,
    int ClickX,
    int ClickY,
    double Confidence);

public sealed record PerceptionConversation(
    string SourcePerceptionEventId,
    DateTimeOffset ObservedAt,
    string ChannelId,
    string ConversationId,
    string Title,
    string Role,
    double Confidence);
