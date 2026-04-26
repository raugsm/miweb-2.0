namespace AriadGSM.Hands.Interaction;

public sealed record InteractionTarget(
    string TargetId,
    string TargetType,
    string ChannelId,
    string SourcePerceptionEventId,
    DateTimeOffset ObservedAt,
    string Title,
    string Preview,
    int UnreadCount,
    int Left,
    int Top,
    int Width,
    int Height,
    int ClickX,
    int ClickY,
    double Confidence,
    bool Actionable,
    string Category,
    IReadOnlyList<string> RejectionReasons);
