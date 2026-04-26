using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriadGSM.Hands.Decisions;

namespace AriadGSM.Hands.Planning;

public sealed partial class ActionPlanner
{
    public IReadOnlyList<ActionPlan> Plan(DecisionEvent decision)
    {
        var intent = Normalize(decision.Intent);
        var proposedAction = Normalize(decision.ProposedAction);
        var channelId = FirstUseful(decision.ChannelId, InferChannelId(decision.Evidence));
        var conversationId = FirstUseful(decision.ConversationId);
        var title = FirstUseful(decision.ConversationTitle);

        if (ShouldObserveOnly(intent, proposedAction))
        {
            return [CreatePlan("noop", decision, channelId, conversationId, title, 1, "Decision only needs observation.")];
        }

        if (string.IsNullOrWhiteSpace(channelId) && string.IsNullOrWhiteSpace(conversationId))
        {
            return [CreatePlan("noop", decision, channelId, conversationId, title, 1, "Decision has no channel or conversation target yet.")];
        }

        var actions = new List<ActionPlan>
        {
            CreatePlan("focus_window", decision, channelId, conversationId, title, 3, "Bring the correct WhatsApp channel to the foreground.")
        };

        if (!string.IsNullOrWhiteSpace(conversationId) || !string.IsNullOrWhiteSpace(title))
        {
            actions.Add(CreatePlan("open_chat", decision, channelId, conversationId, title, 3, "Open the target chat before reading the conversation."));
        }

        if (NeedsHistorySweep(intent, proposedAction))
        {
            actions.Add(CreatePlan("scroll_history", decision, channelId, conversationId, title, 3, "Scroll the chat history for learning within the configured history window."));
        }

        actions.Add(CreatePlan("capture_conversation", decision, channelId, conversationId, title, 3, "Ask Perception to refresh the full visible conversation context."));

        if (NeedsAccountingRecord(intent, proposedAction))
        {
            actions.Add(CreatePlan("record_accounting", decision, channelId, conversationId, title, 4, "Create an accounting record only after evidence is confirmed."));
        }

        if (NeedsPreparedText(proposedAction))
        {
            actions.Add(CreatePlan("write_text", decision, channelId, conversationId, title, 5, "Prepare text in the chat input without sending it."));
        }

        if (NeedsSend(proposedAction))
        {
            actions.Add(CreatePlan("send_message", decision, channelId, conversationId, title, 6, "Send a message only at full execution autonomy."));
        }

        return actions;
    }

    private static ActionPlan CreatePlan(
        string actionType,
        DecisionEvent decision,
        string? channelId,
        string? conversationId,
        string? title,
        int requiredAutonomyLevel,
        string reason)
    {
        var target = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceDecisionId"] = decision.DecisionId,
            ["channelId"] = channelId,
            ["conversationId"] = conversationId,
            ["conversationTitle"] = title,
            ["intent"] = decision.Intent,
            ["proposedAction"] = decision.ProposedAction,
            ["decisionConfidence"] = decision.Confidence,
            ["requiresHumanConfirmation"] = decision.RequiresHumanConfirmation,
            ["plannerReason"] = reason
        };

        var actionId = StableActionId(actionType, decision.DecisionId, channelId, conversationId, title);
        return new ActionPlan(
            actionId,
            actionType,
            target,
            requiredAutonomyLevel,
            decision.RequiresHumanConfirmation,
            reason,
            decision);
    }

    private static string StableActionId(string actionType, string decisionId, string? channelId, string? conversationId, string? title)
    {
        var raw = $"{actionType}|{decisionId}|{channelId}|{conversationId}|{title}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"hands-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static string? InferChannelId(IReadOnlyList<string> evidence)
    {
        foreach (var item in evidence)
        {
            var match = EvidenceChannelRegex().Match(item ?? string.Empty);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static bool ShouldObserveOnly(string intent, string proposedAction)
    {
        return intent is "" or "none" or "resolved" or "ignore"
            || proposedAction.Contains("keep_observing", StringComparison.Ordinal)
            || proposedAction.Contains("observe", StringComparison.Ordinal)
            || proposedAction.Contains("no_action", StringComparison.Ordinal);
    }

    private static bool NeedsHistorySweep(string intent, string proposedAction)
    {
        return intent.Contains("learning", StringComparison.Ordinal)
            || intent.Contains("history", StringComparison.Ordinal)
            || proposedAction.Contains("learn", StringComparison.Ordinal)
            || proposedAction.Contains("history", StringComparison.Ordinal)
            || proposedAction.Contains("scroll", StringComparison.Ordinal);
    }

    private static bool NeedsAccountingRecord(string intent, string proposedAction)
    {
        return intent.Contains("accounting", StringComparison.Ordinal)
            || intent.Contains("payment", StringComparison.Ordinal)
            || intent.Contains("debt", StringComparison.Ordinal)
            || proposedAction.Contains("payment", StringComparison.Ordinal)
            || proposedAction.Contains("debt", StringComparison.Ordinal)
            || proposedAction.Contains("accounting", StringComparison.Ordinal);
    }

    private static bool NeedsPreparedText(string proposedAction)
    {
        return proposedAction.Contains("write", StringComparison.Ordinal)
            || proposedAction.Contains("draft", StringComparison.Ordinal)
            || proposedAction.Contains("prepare_message", StringComparison.Ordinal);
    }

    private static bool NeedsSend(string proposedAction)
    {
        return proposedAction.Contains("send", StringComparison.Ordinal)
            || proposedAction.Contains("reply_now", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    [GeneratedRegex(@"msg-(wa-\d+)-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EvidenceChannelRegex();
}
