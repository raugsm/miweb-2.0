namespace AriadGSM.Vision.Events;

public static class ContractValidator
{
    public static IReadOnlyList<string> Validate(VisionEvent visionEvent)
    {
        var errors = new List<string>();
        if (visionEvent.EventType != "vision_event")
        {
            errors.Add("eventType must be vision_event");
        }
        if (string.IsNullOrWhiteSpace(visionEvent.VisionEventId))
        {
            errors.Add("visionEventId is required");
        }
        if (!visionEvent.VisibleOnly)
        {
            errors.Add("visibleOnly must be true");
        }
        if (visionEvent.Frame is null)
        {
            errors.Add("frame is required");
        }
        if (visionEvent.Retention is null)
        {
            errors.Add("retention is required");
        }
        else if (visionEvent.Retention.RawFrameUploadedToCloud)
        {
            errors.Add("raw frames must not be uploaded to cloud by default");
        }
        return errors;
    }
}

