namespace AriadGSM.Orchestrator.Pipeline;

public sealed record OrchestratorState(
    string Engine,
    string Status,
    DateTimeOffset UpdatedAt,
    string Phase,
    string Summary,
    IReadOnlyList<OrchestratorChannelState> Channels,
    IReadOnlyList<OrchestratorBlocker> Blockers,
    IReadOnlyList<OrchestratorRecommendation> Recommendations,
    OrchestratorMetrics Metrics,
    string LastError);

public sealed record OrchestratorChannelState(
    string ChannelId,
    string BrowserProcess,
    string Status,
    bool CabinReady,
    bool VisionVisible,
    bool PerceptionSeen,
    bool ActionsAllowed,
    string Detail,
    OrchestratorWindowSnapshot? Window);

public sealed record OrchestratorWindowSnapshot(
    int ProcessId,
    string ProcessName,
    string Title,
    OrchestratorBounds Bounds);

public sealed record OrchestratorBounds(
    int Left,
    int Top,
    int Width,
    int Height);

public sealed record OrchestratorBlocker(
    string Code,
    string Severity,
    string ChannelId,
    string Detail);

public sealed record OrchestratorRecommendation(
    string Code,
    string ChannelId,
    string Detail);

public sealed record OrchestratorMetrics(
    int ExpectedChannels,
    int CabinReadyChannels,
    int VisionWhatsAppWindows,
    int PerceptionChannels,
    int ActionFailures,
    int ActionsExecuted,
    int ActionsVerified,
    int ActionsSkipped,
    int ActionableTargets);

public sealed record OrchestratorCommands(
    DateTimeOffset UpdatedAt,
    bool ActionsAllowed,
    IReadOnlyList<string> PausedChannels,
    string Reason);

public sealed record OrchestratorRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Cycles,
    string Phase,
    string Summary,
    string LastError);
