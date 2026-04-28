using System.Globalization;
using System.Text;
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

        if (execution.Status.Equals("planned", StringComparison.OrdinalIgnoreCase))
        {
            return new ActionVerification(false, "Dry-run planned only; live verification needs execution or a fresh Perception pass.", 0.35);
        }

        var channelId = GetTargetString(plan, "channelId");
        var conversationId = GetTargetString(plan, "conversationId");

        if (plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyOpenChat(plan, context, channelId, conversationId);
        }

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

        return new ActionVerification(false, "Perception has not confirmed the requested target yet.", 0.25);
    }

    private static ActionVerification VerifyOpenChat(
        ActionPlan plan,
        PerceptionContext context,
        string? channelId,
        string? conversationId)
    {
        var expectedTitles = new[]
            {
                GetTargetString(plan, "conversationTitle"),
                GetTargetString(plan, "chatRowTitle")
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (context.ContainsConversation(conversationId))
        {
            return new ActionVerification(true, $"Perception confirms conversation '{conversationId}' is open.", 0.95);
        }

        var visible = context.BestForChannel(channelId);
        if (visible is null)
        {
            return new ActionVerification(
                false,
                $"No veo una conversacion activa en {channelId ?? "el canal"} despues del intento de abrir chat.",
                0.2);
        }

        if (expectedTitles.Length == 0)
        {
            return new ActionVerification(
                false,
                $"Veo {channelId ?? "el canal"} activo como '{visible.Title ?? "sin titulo"}', pero la accion no trae titulo esperado para confirmar el chat correcto.",
                0.35);
        }

        foreach (var expectedTitle in expectedTitles)
        {
            if (TitlesMatch(expectedTitle, visible.Title))
            {
                return new ActionVerification(
                    true,
                    $"Perception confirmo chat correcto en {channelId ?? "el canal"}: '{visible.Title}' coincide con '{expectedTitle}'.",
                    Math.Max(0.88, visible.Confidence));
            }
        }

        var expected = string.Join("' o '", expectedTitles);
        return new ActionVerification(
            false,
            $"Abri {channelId ?? "el canal"}, pero Perception ve '{visible.Title ?? "sin titulo"}' y esperaba '{expected}'. No continuo con acciones dependientes.",
            0.55);
    }

    private static string? GetTargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool TitlesMatch(string? expected, string? actual)
    {
        var normalizedExpected = NormalizeTitle(expected);
        var normalizedActual = NormalizeTitle(actual);
        if (normalizedExpected.Length == 0 || normalizedActual.Length == 0)
        {
            return false;
        }

        if (normalizedExpected.Equals(normalizedActual, StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedActual.Contains(normalizedExpected, StringComparison.Ordinal)
            || normalizedExpected.Contains(normalizedActual, StringComparison.Ordinal))
        {
            return Math.Min(normalizedExpected.Length, normalizedActual.Length) >= 4;
        }

        var expectedTokens = UsefulTokens(normalizedExpected).ToArray();
        var actualTokens = UsefulTokens(normalizedActual).ToHashSet(StringComparer.Ordinal);
        if (expectedTokens.Length == 0 || actualTokens.Count == 0)
        {
            return false;
        }

        var matches = expectedTokens.Count(actualTokens.Contains);
        return matches / (double)expectedTokens.Length >= 0.75;
    }

    private static IEnumerable<string> UsefulTokens(string normalizedTitle)
    {
        var stopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "el",
            "la",
            "los",
            "las",
            "de",
            "del",
            "un",
            "una",
            "por",
            "para",
            "whatsapp",
            "business"
        };

        return normalizedTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2 && !stopWords.Contains(token));
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(' ');
            }
        }

        return string.Join(" ", builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
