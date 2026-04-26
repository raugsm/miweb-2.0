using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Reader;

public sealed record ReaderContext(VisionEventEnvelope? VisionEvent);

public interface IReaderCore
{
    ValueTask<ReaderCoreResult> ReadAsync(ResolvedChannel channel, ReaderContext context, CancellationToken cancellationToken = default);
}
