namespace AriadGSM.Perception.VisionInput;

public sealed record VisionWindow(
    int ProcessId,
    string ProcessName,
    string Title,
    VisionBounds Bounds,
    bool IsVisible = true,
    DateTimeOffset? CapturedAt = null);
