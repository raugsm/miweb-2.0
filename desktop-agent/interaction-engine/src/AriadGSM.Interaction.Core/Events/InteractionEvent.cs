namespace AriadGSM.Interaction.Events;

public sealed record InteractionEvent(
    string EventType,
    string InteractionEventId,
    DateTimeOffset CreatedAt,
    string Source,
    string LatestPerceptionEventId,
    int PerceptionEventsRead,
    IReadOnlyList<InteractionTarget> Targets,
    InteractionSummary Summary);

public sealed record InteractionSummary(
    int TargetsObserved,
    int TargetsAccepted,
    int TargetsRejected,
    int ActionableTargets,
    string BestTargetTitle,
    string LastRejectionReason);
