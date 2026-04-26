namespace AriadGSM.Perception.Reader;

public sealed record ReaderCoreResult(
    string ChannelId,
    string Source,
    string Status,
    IReadOnlyList<ReaderTextLine> Lines,
    double Confidence,
    string Error)
{
    public static ReaderCoreResult Empty(string channelId, string source, string status, string error = "")
    {
        return new ReaderCoreResult(channelId, source, status, [], 0, error);
    }
}
