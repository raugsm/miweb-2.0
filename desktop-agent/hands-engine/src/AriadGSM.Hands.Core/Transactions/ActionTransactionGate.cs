using System.Globalization;
using System.Text;
using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Interaction;
using AriadGSM.Hands.Perception;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;

namespace AriadGSM.Hands.Transactions;

public sealed class ActionTransactionGate
{
    private readonly HandsOptions _options;

    public ActionTransactionGate(HandsOptions options)
    {
        _options = options;
    }

    public ActionTransactionDecision Begin(
        ActionPlan plan,
        string scopedActionId,
        TrustSafetyGateDecision trustGate,
        PerceptionContext perception,
        InteractionContext interaction)
    {
        var now = DateTimeOffset.UtcNow;
        var transactionId = $"txn-{StableHash($"{scopedActionId}|{now.UtcTicks}")[..24]}";
        var traceId = StableHash($"{transactionId}|{plan.ActionType}|{TargetString(plan, "channelId")}")[
            ..32];
        var channelId = TargetString(plan, "channelId") ?? string.Empty;

        var baseTarget = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["actionTransactionId"] = transactionId,
            ["actionTraceId"] = traceId,
            ["actionTransactionGate"] = _options.ActionTransactionsEnabled ? "enabled" : "disabled",
            ["actionTransactionLayer"] = "Capa 7: Action, Tools & Verification",
            ["freshPerceptionEventId"] = perception.SourcePerceptionEventId,
            ["freshInteractionEventId"] = interaction.SourceInteractionEventId,
            ["transactionStartedAt"] = now
        };

        if (!_options.ActionTransactionsEnabled || !_options.ExecuteActions)
        {
            var planOnly = plan with { Target = baseTarget };
            return Allow(planOnly, transactionId, traceId, channelId, "Modo plan: se audita sin tocar pantalla.", now);
        }

        var destructive = DestructiveBrowserPolicyViolation(plan);
        if (!string.IsNullOrWhiteSpace(destructive))
        {
            return Block(plan with { Target = baseTarget }, transactionId, traceId, channelId, destructive, now);
        }

        if (IsPhysicalAction(plan.ActionType) && string.IsNullOrWhiteSpace(channelId))
        {
            return Block(plan with { Target = baseTarget }, transactionId, traceId, channelId, "Accion fisica sin canal; no toco pantalla.", now);
        }

        var activeLease = ReadActiveLease(now);
        if (activeLease is not null)
        {
            return Block(
                plan with { Target = baseTarget },
                transactionId,
                traceId,
                channelId,
                $"Ya hay una accion fisica viva en {activeLease.ChannelId}; espero verificacion antes de tocar otro canal.",
                now);
        }

        var freshness = ValidateFreshness(plan, perception, interaction, now);
        if (!freshness.Allowed)
        {
            return Block(plan with { Target = baseTarget }, transactionId, traceId, channelId, freshness.Reason, now);
        }

        var enrichedTarget = new Dictionary<string, object?>(baseTarget, StringComparer.OrdinalIgnoreCase)
        {
            ["actionabilityVisible"] = freshness.Visible,
            ["actionabilityStable"] = freshness.Stable,
            ["actionabilityReceivesEvents"] = freshness.ReceivesEvents,
            ["actionabilityEnabled"] = freshness.Enabled,
            ["perceptionAgeMs"] = freshness.PerceptionAgeMs,
            ["interactionAgeMs"] = freshness.InteractionAgeMs,
            ["singleChannelLease"] = channelId,
            ["trustSafetyDecisionAtTransaction"] = trustGate.Decision,
            ["nonDestructiveBrowserPolicy"] = "close-tab/window/new-tab/shell-url-launch forbidden"
        };
        var allowedPlan = plan with { Target = enrichedTarget };
        WriteState("running", transactionId, traceId, channelId, plan.ActionType, "Accion fisica en curso.", now, null);
        WriteJournal("begin", "running", "Transaccion autorizada para una sola accion fisica.", allowedPlan, scopedActionId, null);
        return Allow(allowedPlan, transactionId, traceId, channelId, "Transaccion autorizada para una sola accion fisica.", now);
    }

    public void Complete(ActionTransactionDecision transaction, ActionEvent actionEvent)
    {
        if (!_options.ActionTransactionsEnabled || string.IsNullOrWhiteSpace(transaction.TransactionId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var status = actionEvent.Status.Equals("verified", StringComparison.OrdinalIgnoreCase)
            ? "verified"
            : actionEvent.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase)
                ? "blocked"
                : actionEvent.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                    ? "failed"
                    : "completed";
        WriteState(
            status,
            transaction.TransactionId,
            transaction.TraceId,
            transaction.ChannelId,
            actionEvent.ActionType,
            actionEvent.Verification.Summary,
            now,
            actionEvent);
        WriteJournal("complete", status, actionEvent.Verification.Summary, transaction.Plan, actionEvent.ActionId, actionEvent);
    }

    public void Block(ActionTransactionDecision transaction, string scopedActionId, ActionEvent actionEvent)
    {
        if (!_options.ActionTransactionsEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        WriteState(
            "blocked",
            transaction.TransactionId,
            transaction.TraceId,
            transaction.ChannelId,
            actionEvent.ActionType,
            actionEvent.Verification.Summary,
            now,
            actionEvent);
        WriteJournal("blocked", "blocked", actionEvent.Verification.Summary, transaction.Plan, scopedActionId, actionEvent);
    }

    private FreshnessDecision ValidateFreshness(ActionPlan plan, PerceptionContext perception, InteractionContext interaction, DateTimeOffset now)
    {
        if (!IsPhysicalAction(plan.ActionType))
        {
            return FreshnessDecision.Allow(0, 0, true, true, true, true);
        }

        if (string.IsNullOrWhiteSpace(perception.SourcePerceptionEventId) || perception.ObservedAt == default)
        {
            return FreshnessDecision.Block("No hay percepcion fresca para actuar.");
        }

        var perceptionAge = Math.Max(0, (int)(now - perception.ObservedAt.ToUniversalTime()).TotalMilliseconds);
        if (perceptionAge > _options.FreshPerceptionMaxAgeMs)
        {
            return FreshnessDecision.Block($"Percepcion vieja ({perceptionAge} ms); espero una lectura nueva antes de tocar.");
        }

        if (plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase))
        {
            var interactionAge = interaction.CreatedAt == default
                ? int.MaxValue
                : Math.Max(0, (int)(now - interaction.CreatedAt.ToUniversalTime()).TotalMilliseconds);
            if (interactionAge > _options.FreshInteractionMaxAgeMs)
            {
                return FreshnessDecision.Block($"Objetivo de chat viejo ({interactionAge} ms); espero Interaction nuevo.");
            }

            var interactionPerceptionId = TargetString(plan, "interactionSourcePerceptionEventId") ?? string.Empty;
            if (!interactionPerceptionId.Equals(perception.SourcePerceptionEventId, StringComparison.OrdinalIgnoreCase))
            {
                return FreshnessDecision.Block("Interaction y Perception no apuntan a la misma lectura; no hago click con realidad mezclada.");
            }

            var channelId = TargetString(plan, "channelId");
            var title = TargetString(plan, "chatRowTitle") ?? TargetString(plan, "conversationTitle");
            var row = perception.BestChatRow(channelId, title);
            if (row is null)
            {
                return FreshnessDecision.Block("Perception no confirma la fila de chat actual antes del click.");
            }

            if (row.Confidence < _options.MinimumActionabilityConfidence)
            {
                return FreshnessDecision.Block($"Fila de chat con confianza baja ({row.Confidence:0.00}); no hago click.");
            }

            if (!TryGetInt(plan, "clickX", out var clickX)
                || !TryGetInt(plan, "clickY", out var clickY)
                || clickX < row.Left
                || clickX > row.Left + row.Width
                || clickY < row.Top
                || clickY > row.Top + row.Height)
            {
                return FreshnessDecision.Block("El punto de click ya no cae dentro de la fila confirmada.");
            }

            if (row.Width <= 0 || row.Height <= 0)
            {
                return FreshnessDecision.Block("La fila de chat no tiene caja visible.");
            }

            return FreshnessDecision.Allow(perceptionAge, interactionAge, true, true, true, true);
        }

        var actionChannel = TargetString(plan, "channelId");
        if (!perception.ContainsChannel(actionChannel))
        {
            return FreshnessDecision.Block($"Perception no confirma canal activo {actionChannel ?? "desconocido"} antes de {plan.ActionType}.");
        }

        return FreshnessDecision.Allow(perceptionAge, 0, true, true, true, true);
    }

    private ActiveLease? ReadActiveLease(DateTimeOffset now)
    {
        if (!File.Exists(_options.ActionTransactionStateFile))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_options.ActionTransactionStateFile));
            var root = document.RootElement;
            if (!StringValue(root, "status").Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var expiresAtRaw = StringValue(root, "expiresAt");
            if (!DateTimeOffset.TryParse(expiresAtRaw, out var expiresAt) || expiresAt <= now)
            {
                return null;
            }

            return new ActiveLease(StringValue(root, "transactionId"), StringValue(root, "channelId"), expiresAt);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteState(
        string status,
        string transactionId,
        string traceId,
        string channelId,
        string actionType,
        string summary,
        DateTimeOffset now,
        ActionEvent? actionEvent)
    {
        var payload = new Dictionary<string, object?>
        {
            ["contractVersion"] = "0.9.6",
            ["engine"] = "ariadgsm_action_transaction_gate",
            ["status"] = status,
            ["updatedAt"] = now,
            ["transactionId"] = transactionId,
            ["traceId"] = traceId,
            ["channelId"] = channelId,
            ["actionType"] = actionType,
            ["expiresAt"] = status.Equals("running", StringComparison.OrdinalIgnoreCase)
                ? now.AddMilliseconds(Math.Max(500, _options.ActionLeaseTtlMs))
                : now,
            ["policy"] = new Dictionary<string, object?>
            {
                ["singlePhysicalAction"] = true,
                ["singleChannelLease"] = true,
                ["freshPerceptionRequired"] = true,
                ["postVerificationRequired"] = _options.RequirePostActionVerification,
                ["destructiveBrowserActionsForbidden"] = true
            },
            ["lastActionId"] = actionEvent?.ActionId ?? string.Empty,
            ["lastActionStatus"] = actionEvent?.Status ?? string.Empty,
            ["summary"] = summary,
            ["humanReport"] = new Dictionary<string, object?>
            {
                ["headline"] = HumanHeadline(status),
                ["queEstaPasando"] = summary,
                ["canal"] = channelId,
                ["accion"] = actionType
            }
        };

        WriteJsonAtomic(_options.ActionTransactionStateFile, payload);
    }

    private void WriteJournal(
        string phase,
        string status,
        string summary,
        ActionPlan plan,
        string scopedActionId,
        ActionEvent? actionEvent)
    {
        var directory = Path.GetDirectoryName(_options.ActionJournalFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new Dictionary<string, object?>
        {
            ["eventType"] = "action_transaction_event",
            ["createdAt"] = DateTimeOffset.UtcNow,
            ["phase"] = phase,
            ["status"] = status,
            ["transactionId"] = TargetString(plan, "actionTransactionId"),
            ["traceId"] = TargetString(plan, "actionTraceId"),
            ["actionId"] = actionEvent?.ActionId ?? scopedActionId,
            ["actionType"] = plan.ActionType,
            ["channelId"] = TargetString(plan, "channelId"),
            ["conversationTitle"] = Redact(TargetString(plan, "conversationTitle")),
            ["sourceDecisionId"] = TargetString(plan, "sourceDecisionId"),
            ["summary"] = summary,
            ["verification"] = actionEvent?.Verification
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.AppendAllText(_options.ActionJournalFile, json + Environment.NewLine, Encoding.UTF8);
    }

    private static ActionTransactionDecision Allow(ActionPlan plan, string transactionId, string traceId, string channelId, string reason, DateTimeOffset now)
    {
        return new ActionTransactionDecision(true, reason, transactionId, traceId, channelId, now, plan);
    }

    private static ActionTransactionDecision Block(ActionPlan plan, string transactionId, string traceId, string channelId, string reason, DateTimeOffset now)
    {
        return new ActionTransactionDecision(false, reason, transactionId, traceId, channelId, now, plan);
    }

    private static bool IsPhysicalAction(string actionType)
    {
        return actionType.Equals("focus_window", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("capture_conversation", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    private static string DestructiveBrowserPolicyViolation(ActionPlan plan)
    {
        var action = plan.ActionType.ToLowerInvariant();
        if (action.Contains("close", StringComparison.Ordinal)
            || action.Contains("kill", StringComparison.Ordinal)
            || action.Contains("terminate", StringComparison.Ordinal)
            || action.Contains("new_tab", StringComparison.Ordinal)
            || action.Contains("shell_launch", StringComparison.Ordinal))
        {
            return "Politica no destructiva: Capa 7 no puede cerrar/terminar navegador ni abrir tabs por shell.";
        }

        var proposed = (TargetString(plan, "proposedAction") ?? string.Empty).ToLowerInvariant();
        if (proposed.Contains("close_browser", StringComparison.Ordinal)
            || proposed.Contains("close_tab", StringComparison.Ordinal)
            || proposed.Contains("kill_browser", StringComparison.Ordinal)
            || proposed.Contains("open_new_tab", StringComparison.Ordinal))
        {
            return "Politica no destructiva: la accion propuesta intenta manipular navegador fuera de Cabin Authority.";
        }

        return string.Empty;
    }

    private static bool TryGetInt(ActionPlan plan, string key, out int value)
    {
        value = 0;
        if (!plan.Target.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is int integer)
        {
            value = integer;
            return true;
        }

        return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string? TargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string StringValue(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= 80 ? value : value[..80] + "...";
    }

    private static string HumanHeadline(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "running" => "Manos ejecutando una accion verificable",
            "verified" => "Accion verificada",
            "blocked" => "Accion bloqueada antes de tocar",
            "failed" => "Accion fallida y detenida",
            _ => "Accion cerrada"
        };
    }

    private static void WriteJsonAtomic(string path, object payload)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(
            tempPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }),
            Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
    }

    private static string StableHash(string raw)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed record ActiveLease(string TransactionId, string ChannelId, DateTimeOffset ExpiresAt);

    private sealed record FreshnessDecision(
        bool Allowed,
        string Reason,
        int PerceptionAgeMs,
        int InteractionAgeMs,
        bool Visible,
        bool Stable,
        bool ReceivesEvents,
        bool Enabled)
    {
        public static FreshnessDecision Allow(int perceptionAgeMs, int interactionAgeMs, bool visible, bool stable, bool receivesEvents, bool enabled)
        {
            return new FreshnessDecision(true, string.Empty, perceptionAgeMs, interactionAgeMs, visible, stable, receivesEvents, enabled);
        }

        public static FreshnessDecision Block(string reason)
        {
            return new FreshnessDecision(false, reason, 0, 0, false, false, false, false);
        }
    }
}

public sealed record ActionTransactionDecision(
    bool Allowed,
    string Reason,
    string TransactionId,
    string TraceId,
    string ChannelId,
    DateTimeOffset StartedAt,
    ActionPlan Plan);
