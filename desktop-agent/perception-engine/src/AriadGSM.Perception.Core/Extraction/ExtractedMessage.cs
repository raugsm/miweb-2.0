using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Extraction;

public sealed record ExtractedMessage(
    string MessageId,
    string Text,
    string Direction,
    string? SenderName,
    DateTimeOffset? SentAt,
    double Confidence,
    VisionBounds? Bounds,
    string Source);
