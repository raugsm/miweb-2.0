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

        if (plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase) && !_options.AllowSendMessage)
        {
            return new SafetyDecision(true, "Sending messages is disabled by Hands policy.");
        }

        if (plan.ActionType.Equals("record_accounting", StringComparison.OrdinalIgnoreCase) && plan.RequiresHumanConfirmation)
        {
            return new SafetyDecision(true, "Accounting record requires human confirmation.");
        }

        if ((plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
                || plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase))
            && plan.RequiresHumanConfirmation)
        {
            return new SafetyDecision(true, "Customer-facing action requires human confirmation.");
        }

        return new SafetyDecision(false, _options.ExecuteActions ? "Action allowed for execution." : "Action allowed as dry-run plan.");
    }
}
