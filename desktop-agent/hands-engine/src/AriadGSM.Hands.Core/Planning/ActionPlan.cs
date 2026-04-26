using AriadGSM.Hands.Decisions;

namespace AriadGSM.Hands.Planning;

public sealed record ActionPlan(
    string ActionId,
    string ActionType,
    IReadOnlyDictionary<string, object?> Target,
    int RequiredAutonomyLevel,
    bool RequiresHumanConfirmation,
    string Reason,
    DecisionEvent SourceDecision);
