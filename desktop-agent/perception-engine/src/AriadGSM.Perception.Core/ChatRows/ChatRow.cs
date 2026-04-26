using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.ChatRows;

public sealed record ChatRow(
    string ChatRowId,
    string ChannelId,
    string Title,
    string Preview,
    int UnreadCount,
    VisionBounds Bounds,
    int ClickX,
    int ClickY,
    double Confidence,
    IReadOnlyList<string> SourceLines);
