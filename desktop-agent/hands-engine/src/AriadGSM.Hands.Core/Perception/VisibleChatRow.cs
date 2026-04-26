namespace AriadGSM.Hands.Perception;

public sealed record VisibleChatRow(
    string? ChannelId,
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
