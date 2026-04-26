namespace AriadGSM.Perception.Health;

public sealed record PerceptionHealthState(
    string Status,
    DateTimeOffset UpdatedAt,
    int VisionEventsRead,
    int PerceptionEventsWritten,
    int WhatsAppWindowsDetected,
    IReadOnlyList<string> ChannelIds,
    string VisionEventsFile,
    string PerceptionEventsFile,
    string LastSourceVisionEventId,
    string LastError);

public sealed record PerceptionRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int VisionEventsRead,
    int PerceptionEventsWritten,
    int Cycles,
    int IdleCycles,
    int LastWhatsAppWindowsDetected,
    IReadOnlyList<string> LastChannelIds,
    string LastSourceVisionEventId,
    string LastError);
