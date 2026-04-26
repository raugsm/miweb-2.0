namespace AriadGSM.Hands.Pipeline;

public sealed record HandsHealthState(
    string Status,
    DateTimeOffset UpdatedAt,
    int DecisionsRead,
    int ActionsPlanned,
    int ActionsWritten,
    int ActionsBlocked,
    int ActionsExecuted,
    int ActionsVerified,
    int ActionsSkipped,
    string CognitiveDecisionEventsFile,
    string OperatingDecisionEventsFile,
    string PerceptionEventsFile,
    string ActionEventsFile,
    string LastActionId,
    string LastSummary,
    string LastError);

public sealed record HandsRunSummary(
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int Cycles,
    int IdleCycles,
    int DecisionsRead,
    int ActionsPlanned,
    int ActionsWritten,
    int ActionsBlocked,
    int ActionsExecuted,
    int ActionsVerified,
    int ActionsSkipped,
    string LastActionId,
    string LastSummary,
    string LastError);
