using AriadGSM.Vision.Buffer;
using AriadGSM.Vision.Capture;
using AriadGSM.Vision.ChangeDetection;
using AriadGSM.Vision.Config;

namespace AriadGSM.Vision.Events;

public static class VisionEventFactory
{
    public static VisionEvent Create(ScreenFrame frame, SavedFrame savedFrame, VisionOptions options, IReadOnlyList<ChangedRegion>? changes = null)
    {
        return new VisionEvent(
            "vision_event",
            $"vision-{frame.FrameId}",
            frame.CapturedAt,
            frame.Source == "synthetic" ? "screen_capture" : frame.Source,
            true,
            new FrameEvidence(frame.FrameId, savedFrame.Path, frame.Width, frame.Height, frame.Hash, savedFrame.StoredLocallyOnly),
            new RetentionEvidence(false, options.RetentionHours, options.MaxStorageGb),
            null,
            null,
            changes ?? Array.Empty<ChangedRegion>());
    }
}

