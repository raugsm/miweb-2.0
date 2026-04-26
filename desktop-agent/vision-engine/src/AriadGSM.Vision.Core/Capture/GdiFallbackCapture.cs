namespace AriadGSM.Vision.Capture;

public sealed class GdiFallbackCapture : IScreenCapture
{
    public ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("GDI fallback is reserved for compatibility capture after the core skeleton is compiled.");
    }
}

