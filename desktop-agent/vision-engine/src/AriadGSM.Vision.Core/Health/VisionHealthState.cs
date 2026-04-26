using AriadGSM.Vision.Windows;

namespace AriadGSM.Vision.Health;

public sealed record VisibleWindowState(
    int ProcessId,
    string ProcessName,
    string Title,
    WindowBounds Bounds);

public sealed record VisionHealthState(
    string Status,
    DateTimeOffset UpdatedAt,
    DateTimeOffset StartedAt,
    int FramesCaptured,
    int EventsWritten,
    int FramesSkipped,
    int CleanupDeleted,
    string StorageRoot,
    string EventsFile,
    string StateFile,
    string CaptureMode,
    int ScreenWidth,
    int ScreenHeight,
    string LastFramePath,
    bool LastFrameChanged,
    double LastChangeScore,
    double ChangeThreshold,
    int CaptureIntervalMs,
    int MinEventIntervalMs,
    int VisibleWindowCount,
    IReadOnlyList<VisibleWindowState> VisibleWindows,
    string LastError);

public sealed record VisionRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int FramesCaptured,
    int EventsWritten,
    int FramesSkipped,
    int CleanupDeleted,
    string LastFramePath,
    double LastChangeScore,
    int VisibleWindowCount,
    string LastError);
