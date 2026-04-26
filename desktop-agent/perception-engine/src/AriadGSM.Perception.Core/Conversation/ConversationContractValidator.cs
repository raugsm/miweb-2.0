namespace AriadGSM.Perception.Conversation;

public static class ConversationContractValidator
{
    public static IReadOnlyList<string> Validate(ConversationEvent conversationEvent)
    {
        var errors = new List<string>();
        if (conversationEvent.EventType != "conversation_event")
        {
            errors.Add("eventType must be conversation_event");
        }
        if (string.IsNullOrWhiteSpace(conversationEvent.ConversationEventId))
        {
            errors.Add("conversationEventId is required");
        }
        if (string.IsNullOrWhiteSpace(conversationEvent.ConversationId))
        {
            errors.Add("conversationId is required");
        }
        if (string.IsNullOrWhiteSpace(conversationEvent.ChannelId))
        {
            errors.Add("channelId is required");
        }
        if (conversationEvent.Timeline is null)
        {
            errors.Add("timeline is required");
        }

        return errors;
    }
}
