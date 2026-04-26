namespace AriadGSM.Vision.Windows;

public sealed record WindowSnapshot(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    WindowBounds Bounds,
    bool IsVisible,
    DateTimeOffset CapturedAt);

