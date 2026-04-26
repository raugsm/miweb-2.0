using AriadGSM.Perception.ChannelResolution;

namespace AriadGSM.Perception.Reader;

public sealed class StaticReaderCore : IReaderCore
{
    private readonly IReadOnlyList<ReaderTextLine> _lines;
    private readonly string _source;
    private readonly string _status;
    private readonly double _confidence;
    private readonly string _error;

    public StaticReaderCore(
        IReadOnlyList<ReaderTextLine> lines,
        string source = "test_reader",
        string status = "ok",
        double confidence = 0.9,
        string error = "")
    {
        _lines = lines;
        _source = source;
        _status = status;
        _confidence = confidence;
        _error = error;
    }

    public ValueTask<ReaderCoreResult> ReadAsync(ResolvedChannel channel, ReaderContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new ReaderCoreResult(channel.ChannelId, _source, _status, _lines, _confidence, _error));
    }
}
