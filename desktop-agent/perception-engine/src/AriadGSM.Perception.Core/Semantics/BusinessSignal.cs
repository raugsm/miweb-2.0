namespace AriadGSM.Perception.Semantics;

public sealed record BusinessSignal(
    string Kind,
    string Value,
    double Confidence);
