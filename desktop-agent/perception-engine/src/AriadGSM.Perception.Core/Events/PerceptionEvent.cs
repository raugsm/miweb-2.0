using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Events;

public sealed record PerceptionObject(
    string ObjectType,
    double Confidence,
    VisionBounds? Bounds = null,
    string? Text = null,
    string? Role = null,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record PerceptionEvent(
    string EventType,
    string PerceptionEventId,
    DateTimeOffset ObservedAt,
    string SourceVisionEventId,
    string? ChannelId,
    IReadOnlyList<PerceptionObject> Objects)
{
    public static string ContractName => "perception_event";
}
