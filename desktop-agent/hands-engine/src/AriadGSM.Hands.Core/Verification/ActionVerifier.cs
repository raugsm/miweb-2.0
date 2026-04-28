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

        if (plan.ActionType.Equals("focus_window", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyVisibleChannel(context, channelId, "Ventana enfocada");
        }

        if (plan.ActionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyScrollHistory(context, channelId);
        }

        if (plan.ActionType.Equals("capture_conversation", StringComparison.OrdinalIgnoreCase))
        {
            return VerifyConversationCapture(context, channelId, conversationId);
        }

        if (plan.ActionType.Equals("record_accounting", StringComparison.OrdinalIgnoreCase))
        {
            return execution.Status.Equals("verified", StringComparison.OrdinalIgnoreCase)
                ? new ActionVerification(true, execution.Summary, execution.Confidence)
                : new ActionVerification(false, "La contabilidad queda como borrador/verificacion externa; Hands no la confirma por UI.", 0.3);
        }

        if (plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase))
        {
            return ReadTargetBool(plan, "textDraftVerified")
                ? new ActionVerification(true, "Borrador confirmado por verificacion de caja de texto.", 0.9)
                : new ActionVerification(false, "No confirme que el texto quedara escrito como borrador correcto; no envio nada.", 0.2);
        }

        if (plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase))
        {
            return ReadTargetBool(plan, "messageSentVerified")
                ? new ActionVerification(true, "Envio confirmado por verificacion posterior.", 0.9)
                : new ActionVerification(false, "No confirmo envio automatico sin aprobacion y verificacion explicita.", 0.1);
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

    private static ActionVerification VerifyVisibleChannel(PerceptionContext context, string? channelId, string actionLabel)
    {
        if (context.ContainsChannel(channelId))
        {
            var visible = context.BestForChannel(channelId);
            return new ActionVerification(
                true,
                $"{actionLabel}: Perception ve {channelId ?? "el canal"} como '{visible?.Title ?? "WhatsApp"}'.",
                Math.Max(0.78, visible?.Confidence ?? 0));
        }

        return new ActionVerification(false, $"{actionLabel}: Perception no confirmo el canal {channelId ?? "solicitado"}.", 0.25);
    }

    private static ActionVerification VerifyScrollHistory(PerceptionContext context, string? channelId)
    {
        if (!context.ContainsChannel(channelId))
        {
            return new ActionVerification(false, $"Scroll detenido: no confirme que {channelId ?? "el canal"} siga visible.", 0.25);
        }

        var visible = context.BestForChannel(channelId);
        return new ActionVerification(
            true,
            $"Scroll historico verificado de forma segura: {channelId ?? "canal"} sigue visible como '{visible?.Title ?? "WhatsApp"}'.",
            Math.Max(0.74, visible?.Confidence ?? 0));
    }

    private static ActionVerification VerifyConversationCapture(PerceptionContext context, string? channelId, string? conversationId)
    {
        if (context.ContainsConversation(conversationId))
        {
            return new ActionVerification(true, $"Captura confirmada: Perception ve conversacion '{conversationId}'.", 0.92);
        }

        if (context.ContainsChannel(channelId))
        {
            var visible = context.BestForChannel(channelId);
            return new ActionVerification(
                true,
                $"Captura confirmada por canal: Perception ve {channelId ?? "el canal"} activo como '{visible?.Title ?? "WhatsApp"}'.",
                Math.Max(0.78, visible?.Confidence ?? 0));
        }

        return new ActionVerification(false, "Captura no confirmada: Perception no devolvio canal ni conversacion esperada.", 0.25);
    }

    private static string? GetTargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool ReadTargetBool(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value)
            && value is bool boolean
            && boolean;
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
