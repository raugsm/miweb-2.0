using AriadGSM.Vision.ChangeDetection;
using AriadGSM.Vision.Windows;

namespace AriadGSM.Vision.Events;

public sealed record FrameEvidence(
    string FrameId,
    string Path,
    int Width,
    int Height,
    string Hash,
    bool StoredLocallyOnly);

public sealed record RetentionEvidence(
    bool RawFrameUploadedToCloud,
    double RetentionHours,
    double MaxStorageGb);

public sealed record VisionEvent(
    string EventType,
    string VisionEventId,
    DateTimeOffset CapturedAt,
    string Source,
    bool VisibleOnly,
    FrameEvidence Frame,
    RetentionEvidence Retention,
    string? ChannelHint = null,
    WindowSnapshot? Window = null,
    IReadOnlyList<ChangedRegion>? Changes = null)
{
    public static string ContractName => "vision_event";
}

