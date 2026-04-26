using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.WindowIdentity;

public sealed record WhatsAppWindowCandidate(
    VisionWindow Window,
    double Confidence,
    string Reason);
