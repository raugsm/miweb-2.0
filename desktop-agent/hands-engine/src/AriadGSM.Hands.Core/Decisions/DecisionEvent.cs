using System.Text.Json;
using System.Text.Json.Serialization;

namespace AriadGSM.Hands.Decisions;

public sealed class DecisionEvent
{
    public string EventType { get; init; } = "";

    public string DecisionId { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public string Goal { get; init; } = "";

    public string Intent { get; init; } = "";

    public double Confidence { get; init; }

    public int AutonomyLevel { get; init; }

    public string ProposedAction { get; init; } = "";

    public bool RequiresHumanConfirmation { get; init; }

    public string ReasoningSummary { get; init; } = "";

    public IReadOnlyList<string> Evidence { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; init; } = [];

    public string? ConversationId => TryExtraString("conversationId");

    public string? ChannelId => TryExtraString("channelId");

    public string? ConversationTitle => TryExtraString("conversationTitle");

    private string? TryExtraString(string key)
    {
        return Extra.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
