namespace AriadGSM.Vision.Capture;

public sealed record ScreenFrame(
    string FrameId,
    DateTimeOffset CapturedAt,
    int Width,
    int Height,
    byte[] Data,
    string Hash,
    string Source);

