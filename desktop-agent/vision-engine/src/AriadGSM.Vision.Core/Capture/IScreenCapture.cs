namespace AriadGSM.Vision.Capture;

public interface IScreenCapture
{
    ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default);
}

