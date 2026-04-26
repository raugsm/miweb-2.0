namespace AriadGSM.Hands.Perception;

public sealed record VisibleConversation(
    string? ChannelId,
    string? ConversationId,
    string? Title,
    string Role,
    double Confidence);
