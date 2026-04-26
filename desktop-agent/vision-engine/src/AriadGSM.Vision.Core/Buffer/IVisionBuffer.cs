using AriadGSM.Vision.Capture;

namespace AriadGSM.Vision.Buffer;

public interface IVisionBuffer
{
    ValueTask<SavedFrame> SaveAsync(ScreenFrame frame, CancellationToken cancellationToken = default);

    int Cleanup(DateTimeOffset now);
}

