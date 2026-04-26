using System.Text.Json.Serialization;

namespace AriadGSM.Perception.VisionInput;

public sealed record VisionFrameEvidence(
    string FrameId,
    string Path,
    int Width,
    int Height,
    string Hash,
    bool StoredLocallyOnly);

public sealed record VisionRetentionEvidence(
    bool RawFrameUploadedToCloud,
    double RetentionHours,
    double MaxStorageGb);

public sealed record VisionChange(
    string RegionId,
    double Score,
    int X,
    int Y,
    int Width,
    int Height);

public sealed record VisionEventEnvelope(
    string EventType,
    string VisionEventId,
    DateTimeOffset CapturedAt,
    string Source,
    bool VisibleOnly,
    VisionFrameEvidence Frame,
    VisionRetentionEvidence Retention,
    string? ChannelHint = null,
    VisionWindow? Window = null,
    IReadOnlyList<VisionWindow>? Windows = null,
    IReadOnlyList<VisionChange>? Changes = null)
{
    [JsonIgnore]
    public IReadOnlyList<VisionWindow> VisibleWindows => Windows ?? (Window is null ? [] : [Window]);
}
