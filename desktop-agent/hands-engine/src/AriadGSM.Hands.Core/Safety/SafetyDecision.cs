namespace AriadGSM.Hands.Safety;

public sealed record SafetyDecision(
    bool Blocked,
    string Reason);
