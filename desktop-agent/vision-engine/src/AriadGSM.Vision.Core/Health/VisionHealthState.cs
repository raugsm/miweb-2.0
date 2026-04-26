namespace AriadGSM.Vision.Health;

public sealed record VisionHealthState(
    string Status,
    DateTimeOffset UpdatedAt,
    int FramesCaptured,
    int EventsWritten,
    int CleanupDeleted,
    string StorageRoot,
    string LastError);

