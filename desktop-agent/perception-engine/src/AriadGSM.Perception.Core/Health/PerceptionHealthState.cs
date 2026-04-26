namespace AriadGSM.Perception.Health;

public sealed record PerceptionHealthState(
    string Status,
    DateTimeOffset UpdatedAt,
    int VisionEventsRead,
    int PerceptionEventsWritten,
    int ConversationEventsWritten,
    int WhatsAppWindowsDetected,
    int ReaderLinesObserved,
    int MessagesExtracted,
    IReadOnlyList<string> ChannelIds,
    string VisionEventsFile,
    string PerceptionEventsFile,
    string ConversationEventsFile,
    string LastSourceVisionEventId,
    string LastReaderStatus,
    string LastError);

public sealed record PerceptionRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int VisionEventsRead,
    int PerceptionEventsWritten,
    int ConversationEventsWritten,
    int Cycles,
    int IdleCycles,
    int LastWhatsAppWindowsDetected,
    int LastReaderLinesObserved,
    int LastMessagesExtracted,
    IReadOnlyList<string> LastChannelIds,
    string LastSourceVisionEventId,
    string LastReaderStatus,
    string LastError);
