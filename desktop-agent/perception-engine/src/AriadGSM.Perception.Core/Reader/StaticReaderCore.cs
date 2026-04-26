using AriadGSM.Perception.ChannelResolution;

namespace AriadGSM.Perception.Reader;

public sealed class StaticReaderCore : IReaderCore
{
    private readonly IReadOnlyList<ReaderTextLine> _lines;

    public StaticReaderCore(IReadOnlyList<ReaderTextLine> lines)
    {
        _lines = lines;
    }

    public ValueTask<ReaderCoreResult> ReadAsync(ResolvedChannel channel, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new ReaderCoreResult(channel.ChannelId, "test_reader", "ok", _lines, 0.9, string.Empty));
    }
}
