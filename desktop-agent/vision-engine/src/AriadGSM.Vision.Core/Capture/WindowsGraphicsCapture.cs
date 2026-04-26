namespace AriadGSM.Vision.Capture;

public sealed class WindowsGraphicsCapture : IScreenCapture
{
    public ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("WindowsGraphicsCapture is reserved for the .NET native Vision Engine implementation.");
    }
}

