namespace AriadGSM.Interaction.Pipeline;

public sealed record InteractionHealthState(
    string Engine,
    string Status,
    DateTimeOffset UpdatedAt,
    int PerceptionEventsRead,
    int InteractionEventsWritten,
    int TargetsObserved,
    int TargetsAccepted,
    int TargetsRejected,
    int ActionableTargets,
    string LatestPerceptionEventId,
    string LastAcceptedTargetTitle,
    string LastRejectionReason,
    string LastSummary,
    string LastError);

public sealed record InteractionRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Cycles,
    int IdleCycles,
    int PerceptionEventsRead,
    int InteractionEventsWritten,
    int TargetsObserved,
    int TargetsAccepted,
    int TargetsRejected,
    int ActionableTargets,
    string LastSummary,
    string LastError);
