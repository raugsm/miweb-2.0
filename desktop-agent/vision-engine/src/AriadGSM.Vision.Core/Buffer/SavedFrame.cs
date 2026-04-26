namespace AriadGSM.Vision.Buffer;

public sealed record SavedFrame(string FrameId, string Path, DateTimeOffset CapturedAt, string Hash, bool StoredLocallyOnly);

