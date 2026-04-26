namespace AriadGSM.Hands.Execution;

public sealed record ExecutionResult(
    string Status,
    string Summary,
    double Confidence);
