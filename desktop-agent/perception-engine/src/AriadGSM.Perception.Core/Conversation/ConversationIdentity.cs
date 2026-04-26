using AriadGSM.Perception.ChatRows;

namespace AriadGSM.Perception.Conversation;

public sealed record ConversationIdentity(
    string ConversationId,
    string Title,
    string Source,
    double Confidence,
    ChatRow? MatchedChatRow);
