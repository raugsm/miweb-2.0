using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Reader;

public sealed record ReaderTextLine(
    string Text,
    string Role,
    VisionBounds? Bounds,
    double Confidence,
    string Source);
