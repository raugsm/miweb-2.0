using AriadGSM.Hands.Config;
using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Safety;

public sealed class HandsSafetyPolicy
{
    private readonly HandsOptions _options;

    public HandsSafetyPolicy(HandsOptions options)
    {
        _options = options;
    }

    public SafetyDecision Evaluate(ActionPlan plan)
    {
        if (_options.AutonomyLevel < plan.RequiredAutonomyLevel)
        {
            return new SafetyDecision(true, $"Autonomy level {_options.AutonomyLevel} is below required level {plan.RequiredAutonomyLevel}.");
        }

        if (plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase) && !_options.AllowTextInput)
        {
            return new SafetyDecision(true, "Text input is disabled by Hands policy.");
        }

        if (plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
            && _options.RequireSafetyApprovalForTextDraft
            && !HasTrustSafetyApproval(plan))
        {
            return new SafetyDecision(true, "Text draft blocked: missing per-action Trust & Safety approval.");
        }

        if (plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase) && !_options.AllowSendMessage)
        {
            return new SafetyDecision(true, "Sending messages is disabled by Hands policy.");
        }

        if (plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase)
            && _options.RequireSafetyApprovalForSend
            && !HasTrustSafetyApproval(plan))
        {
            return new SafetyDecision(true, "Message send blocked: missing explicit Trust & Safety approval.");
        }

        if (plan.ActionType.Equals("record_accounting", StringComparison.OrdinalIgnoreCase) && plan.RequiresHumanConfirmation)
        {
            return new SafetyDecision(true, "Accounting record requires human confirmation.");
        }

        if (plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase) && !HasVerifiedInteractionCoordinates(plan))
        {
            return new SafetyDecision(true, "Open chat blocked: no verified Interaction target coordinates.");
        }

        if ((plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
                || plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase))
            && plan.RequiresHumanConfirmation)
        {
            return new SafetyDecision(true, "Customer-facing action requires human confirmation.");
        }

        return new SafetyDecision(false, _options.ExecuteActions ? "Action allowed for execution." : "Action allowed as dry-run plan.");
    }

    private static bool HasVerifiedInteractionCoordinates(ActionPlan plan)
    {
        return plan.Target.TryGetValue("interactionTargetStatus", out var status)
            && string.Equals(status?.ToString(), "ready", StringComparison.OrdinalIgnoreCase)
            && TryGetPositiveInt(plan, "clickX")
            && TryGetPositiveInt(plan, "clickY");
    }

    private static bool TryGetPositiveInt(ActionPlan plan, string key)
    {
        if (!plan.Target.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        if (value is int integer)
        {
            return integer > 0;
        }

        return int.TryParse(value.ToString(), out var parsed) && parsed > 0;
    }

    private static bool HasTrustSafetyApproval(ActionPlan plan)
    {
        return plan.Target.TryGetValue("trustSafetyApproved", out var approved)
            && approved is bool approvedBool
            && approvedBool
            && plan.Target.TryGetValue("trustSafetyApprovalId", out var approvalId)
            && !string.IsNullOrWhiteSpace(approvalId?.ToString());
    }
}
