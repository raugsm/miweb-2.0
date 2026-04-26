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
    int FramesCaptured,
    int EventsWritten,
    int CleanupDeleted,
    string StorageRoot,
    string EventsFile,
    string StateFile,
    string CaptureMode,
    int ScreenWidth,
    int ScreenHeight,
    string LastFramePath,
    int VisibleWindowCount,
    IReadOnlyList<VisibleWindowState> VisibleWindows,
    string LastError);
