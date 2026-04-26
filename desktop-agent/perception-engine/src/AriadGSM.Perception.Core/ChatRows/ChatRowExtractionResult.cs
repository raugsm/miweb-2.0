namespace AriadGSM.Perception.ChatRows;

public sealed record ChatRowExtractionResult(
    IReadOnlyList<ChatRow> Rows,
    int CandidateLines,
    int RejectedLines);
