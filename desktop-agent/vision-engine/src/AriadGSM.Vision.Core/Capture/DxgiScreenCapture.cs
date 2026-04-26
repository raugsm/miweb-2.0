namespace AriadGSM.Vision.Capture;

public sealed class DxgiScreenCapture : IScreenCapture
{
    public ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("DXGI capture is reserved for high-performance Vision Engine v2.");
    }
}

