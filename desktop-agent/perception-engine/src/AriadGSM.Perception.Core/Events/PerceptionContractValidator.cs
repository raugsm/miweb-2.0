namespace AriadGSM.Perception.Events;

public static class PerceptionContractValidator
{
    public static IReadOnlyList<string> Validate(PerceptionEvent perceptionEvent)
    {
        var errors = new List<string>();
        if (perceptionEvent.EventType != "perception_event")
        {
            errors.Add("eventType must be perception_event");
        }
        if (string.IsNullOrWhiteSpace(perceptionEvent.PerceptionEventId))
        {
            errors.Add("perceptionEventId is required");
        }
        if (string.IsNullOrWhiteSpace(perceptionEvent.SourceVisionEventId))
        {
            errors.Add("sourceVisionEventId is required");
        }
        if (perceptionEvent.Objects.Count == 0)
        {
            errors.Add("objects must not be empty");
        }
        return errors;
    }
}
