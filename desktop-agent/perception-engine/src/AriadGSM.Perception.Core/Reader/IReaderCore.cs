using AriadGSM.Perception.ChannelResolution;

namespace AriadGSM.Perception.Reader;

public interface IReaderCore
{
    ValueTask<ReaderCoreResult> ReadAsync(ResolvedChannel channel, CancellationToken cancellationToken = default);
}
