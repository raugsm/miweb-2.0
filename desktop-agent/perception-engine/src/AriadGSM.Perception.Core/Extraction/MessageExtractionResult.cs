namespace AriadGSM.Perception.Extraction;

public sealed record ExtractionDiagnostics(
    int TotalLines,
    int AcceptedMessages,
    int RejectedLines,
    IReadOnlyDictionary<string, int> RejectionReasons)
{
    public string Summary()
    {
        var reasons = RejectionReasons.Count == 0
            ? "none"
            : string.Join(",", RejectionReasons.Select(item => $"{item.Key}:{item.Value}"));
        return $"accepted={AcceptedMessages}; rejected={RejectedLines}; reasons={reasons}";
    }
}

public sealed record MessageExtractionResult(
    IReadOnlyList<ExtractedMessage> Messages,
    ExtractionDiagnostics Diagnostics);
