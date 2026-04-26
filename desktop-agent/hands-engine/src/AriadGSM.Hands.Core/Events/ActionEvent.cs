using System.Text.Json.Serialization;

namespace AriadGSM.Hands.Events;

public sealed record ActionVerification(
    bool Verified,
    string Summary = "",
    double Confidence = 0);

public sealed record ActionEvent(
    string EventType,
    string ActionId,
    DateTimeOffset CreatedAt,
    string ActionType,
    IReadOnlyDictionary<string, object?> Target,
    string Status,
    ActionVerification Verification)
{
    [JsonIgnore]
    public static string ContractName => "action_event";
}
