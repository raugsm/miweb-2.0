using AriadGSM.Perception.WindowIdentity;

namespace AriadGSM.Perception.ChannelResolution;

public sealed record ResolvedChannel(
    string ChannelId,
    WhatsAppWindowCandidate Candidate,
    double Confidence,
    string Method);
