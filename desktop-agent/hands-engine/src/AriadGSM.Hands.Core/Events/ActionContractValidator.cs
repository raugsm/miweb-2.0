namespace AriadGSM.Hands.Events;

public static class ActionContractValidator
{
    private static readonly HashSet<string> ActionTypes =
    [
        "focus_window",
        "open_chat",
        "scroll_history",
        "capture_conversation",
        "write_text",
        "send_message",
        "record_accounting",
        "noop"
    ];

    private static readonly HashSet<string> Statuses =
    [
        "planned",
        "executed",
        "failed",
        "blocked",
        "verified"
    ];

    public static IReadOnlyList<string> Validate(ActionEvent actionEvent)
    {
        var errors = new List<string>();
        if (actionEvent.EventType != "action_event")
        {
            errors.Add("eventType must be action_event");
        }
        if (string.IsNullOrWhiteSpace(actionEvent.ActionId))
        {
            errors.Add("actionId is required");
        }
        if (!ActionTypes.Contains(actionEvent.ActionType))
        {
            errors.Add("actionType is invalid");
        }
        if (!Statuses.Contains(actionEvent.Status))
        {
            errors.Add("status is invalid");
        }
        if (actionEvent.Target is null)
        {
            errors.Add("target is required");
        }
        if (actionEvent.Verification is null)
        {
            errors.Add("verification is required");
        }
        if (actionEvent.Verification is not null && (actionEvent.Verification.Confidence < 0 || actionEvent.Verification.Confidence > 1))
        {
            errors.Add("verification confidence must be between 0 and 1");
        }
        return errors;
    }
}
