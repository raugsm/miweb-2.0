using AriadGSM.Hands.Events;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Perception;
using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Verification;

public sealed class ActionVerifier
{
    public ActionVerification Verify(ActionPlan plan, ExecutionResult execution, PerceptionContext context)
    {
        if (execution.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionVerification(false, execution.Summary, execution.Confidence);
        }

        if (execution.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionVerification(false, execution.Summary, 0);
        }

        if (plan.ActionType.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionVerification(true, "No action was required.", 1);
        }

        var channelId = GetTargetString(plan, "channelId");
        var conversationId = GetTargetString(plan, "conversationId");

        if (context.ContainsConversation(conversationId))
        {
            return new ActionVerification(true, $"Perception confirms conversation '{conversationId}' is visible.", 0.92);
        }

        if (context.ContainsChannel(channelId))
        {
            var visible = context.BestForChannel(channelId);
            return new ActionVerification(
                plan.ActionType is "focus_window" or "capture_conversation" or "scroll_history",
                $"Perception sees channel '{channelId}' visible as '{visible?.Title ?? "WhatsApp"}'.",
                0.78);
        }

        if (execution.Status.Equals("planned", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionVerification(false, "Dry-run planned only; live verification needs execution or a fresh Perception pass.", 0.35);
        }

        return new ActionVerification(false, "Perception has not confirmed the requested target yet.", 0.25);
    }

    private static string? GetTargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
