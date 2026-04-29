using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Globalization;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Decisions;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Input;
using AriadGSM.Hands.Interaction;
using AriadGSM.Hands.Orchestration;
using AriadGSM.Hands.Perception;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;
using AriadGSM.Hands.Transactions;
using AriadGSM.Hands.Verification;

namespace AriadGSM.Hands.Pipeline;

public sealed class HandsPipeline
{
    private readonly HandsOptions _options;
    private readonly DecisionEventReader _decisionReader;
    private readonly PerceptionContextReader _perceptionReader;
    private readonly InteractionContextReader _interactionReader;
    private readonly OrchestratorCommandReader _orchestratorReader;
    private readonly ActionPlanner _planner = new();
    private readonly HandsSafetyPolicy _safety;
    private readonly IHandsExecutor _executor;
    private readonly ActionVerifier _verifier = new();
    private readonly ActionEventWriter _writer;
    private readonly HandsCursorStore _cursorStore;
    private readonly InputArbiter _inputArbiter;
    private readonly TrustSafetyGate _trustSafetyGate;
    private readonly CabinAuthorityGate _cabinAuthorityGate;
    private readonly ActionTransactionGate _transactionGate;
    private readonly HashSet<string> _processedDecisionKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _processedDecisionScopes = new(StringComparer.OrdinalIgnoreCase);
    private bool _cursorLoaded;
    private int _idleCycles;
    private int _decisionsRead;
    private int _actionsPlanned;
    private int _actionsWritten;
    private int _actionsBlocked;
    private int _actionsExecuted;
    private int _actionsVerified;
    private int _actionsSkipped;
    private string _lastActionId = string.Empty;
    private string _lastSummary = string.Empty;
    private string _lastError = string.Empty;
    private DateTimeOffset _lastNavigatorClickAt = DateTimeOffset.MinValue;

    public HandsPipeline(HandsOptions options, IHandsExecutor? executor = null)
    {
        _options = options;
        _decisionReader = new DecisionEventReader(
            [options.CognitiveDecisionEventsFile, options.OperatingDecisionEventsFile, options.BusinessDecisionEventsFile],
            options.DecisionLimit);
        _perceptionReader = new PerceptionContextReader(options.PerceptionEventsFile, options.PerceptionLimit);
        _interactionReader = new InteractionContextReader(options.InteractionEventsFile, options.InteractionLimit);
        _orchestratorReader = new OrchestratorCommandReader(options.OrchestratorCommandsFile, options.RespectOrchestratorCommands);
        _safety = new HandsSafetyPolicy(options);
        _executor = executor ?? (options.ExecuteActions ? new Win32HandsExecutor(options) : new DryRunHandsExecutor());
        _writer = new ActionEventWriter(options.ActionEventsFile);
        _cursorStore = new HandsCursorStore(options.CursorFile);
        _inputArbiter = new InputArbiter(options);
        _trustSafetyGate = new TrustSafetyGate(options);
        _cabinAuthorityGate = new CabinAuthorityGate(options);
        _transactionGate = new ActionTransactionGate(options);
    }

    public async ValueTask<HandsHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _actionsSkipped = 0;
            await EnsureCursorLoadedAsync(cancellationToken).ConfigureAwait(false);

            var decisions = await _decisionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            _decisionsRead = decisions.Count;

            var perception = await _perceptionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            var interaction = await _interactionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            var orchestrator = await _orchestratorReader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var existingIds = await _writer.ReadExistingActionIdsAsync(cancellationToken).ConfigureAwait(false);
            var suspendedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var writtenThisCycle = 0;
            var cursorDirty = false;
            _inputArbiter.WriteHeartbeat("Hands ciclo vivo: refresco prioridad de mouse antes de Trust & Safety.");

            if (!orchestrator.ActionsAllowed)
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", $"Orchestrator pauso las manos: {orchestrator.Reason}", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            var trustGate = _trustSafetyGate.Evaluate();
            if (!trustGate.HandsAllowed)
            {
                _idleCycles++;
                return await WriteStateAsync(
                    CreateState("idle", $"Trust & Safety pauso manos: {trustGate.Reason}", string.Empty),
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var decision in decisions)
            {
                var decisionKey = DecisionCursorKey(decision.DecisionId);
                if (_processedDecisionKeys.Contains(decisionKey))
                {
                    continue;
                }

                if (!IsActionableDecision(decision))
                {
                    cursorDirty |= _processedDecisionKeys.Add(decisionKey);
                    continue;
                }

                var decisionScope = $"{decision.DecisionId}|{interaction.SourceInteractionEventId}|{ExecutionMode()}";
                var plans = _planner.Plan(decision);
                if (!_processedDecisionScopes.Add(decisionScope))
                {
                    continue;
                }

                var touchedDecision = false;
                foreach (var plan in plans)
                {
                    var enrichedPlan = EnrichPlanWithInteraction(plan, interaction);
                    var channelId = TargetString(enrichedPlan, "channelId");
                    if (orchestrator.IsChannelPaused(channelId) || suspendedChannels.Contains(channelId ?? string.Empty))
                    {
                        _actionsSkipped++;
                        continue;
                    }

                    touchedDecision = true;
                    var scopedActionId = ScopedActionId(enrichedPlan);
                    if (await TryWritePlanAsync(enrichedPlan, scopedActionId, trustGate, perception, interaction, existingIds, suspendedChannels, cancellationToken).ConfigureAwait(false))
                    {
                        writtenThisCycle++;
                    }

                    if (CycleTransactionLimitReached(writtenThisCycle))
                    {
                        break;
                    }
                }

                if (touchedDecision)
                {
                    cursorDirty |= _processedDecisionKeys.Add(decisionKey);
                }

                cursorDirty = true;
                if (CycleTransactionLimitReached(writtenThisCycle))
                {
                    break;
                }
            }

            if (!CycleTransactionLimitReached(writtenThisCycle))
            {
                writtenThisCycle += await RunInteractionNavigatorAsync(interaction, perception, orchestrator, trustGate, suspendedChannels, existingIds, cancellationToken).ConfigureAwait(false);
            }
            if (cursorDirty)
            {
                await SaveCursorAsync(cancellationToken).ConfigureAwait(false);
            }

            if (writtenThisCycle == 0)
            {
                _idleCycles++;
                var idleReason = decisions.Count == 0
                    ? "No decision events available and no new navigator target ready."
                    : "No new actions; all known action ids were already emitted.";
                return await WriteStateAsync(CreateState("idle", idleReason, string.Empty), cancellationToken).ConfigureAwait(false);
            }

            return await WriteStateAsync(CreateState("ok", _lastSummary, string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return await WriteStateAsync(CreateState("error", _lastSummary, exception.Message), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask EnsureCursorLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cursorLoaded)
        {
            return;
        }

        var snapshot = await _cursorStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        foreach (var item in snapshot.ProcessedDecisionKeys)
        {
            _processedDecisionKeys.Add(item);
        }

        foreach (var item in snapshot.ProcessedDecisionScopes)
        {
            _processedDecisionScopes.Add(item);
        }

        var seededKeys = await _writer.ReadProcessedDecisionKeysAsync(
            ExecutionMode(),
            cancellationToken).ConfigureAwait(false);
        foreach (var item in seededKeys)
        {
            _processedDecisionKeys.Add(item);
        }

        _cursorLoaded = true;
    }

    private ValueTask SaveCursorAsync(CancellationToken cancellationToken)
    {
        return _cursorStore.SaveAsync(
            _processedDecisionKeys,
            _processedDecisionScopes,
            _options.ProcessedDecisionCursorLimit,
            cancellationToken);
    }

    private bool IsActionableDecision(DecisionEvent decision)
    {
        if (_options.MaxDecisionAgeMinutes > 0
            && decision.CreatedAt != default
            && DateTimeOffset.UtcNow - decision.CreatedAt.ToUniversalTime() > TimeSpan.FromMinutes(_options.MaxDecisionAgeMinutes))
        {
            return false;
        }

        return !IsNoisyConversationTitle(decision.ConversationTitle);
    }

    private static bool IsNoisyConversationTitle(string? title)
    {
        var normalized = NormalizeTitle(title);
        if (normalized.Length == 0)
        {
            return true;
        }

        var blocked = new[]
        {
            "whatsapp business",
            "paginas mas",
            "perfil 1",
            "marcador",
            "favorito",
            "leer en voz alta",
            "informacion del sitio",
            "ctrl+d",
            "ctrl d",
            "http://",
            "https://"
        };
        return blocked.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeTitle(string? title)
    {
        var normalized = (title ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static ActionPlan EnrichPlanWithInteraction(ActionPlan plan, InteractionContext interaction)
    {
        if (!plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase))
        {
            return plan;
        }

        var channelId = TargetString(plan, "channelId");
        var title = TargetString(plan, "conversationTitle");
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["interactionEventId"] = interaction.SourceInteractionEventId,
            ["interactionTargetStatus"] = "missing"
        };

        var row = interaction.BestChatTarget(channelId, title);
        if (row is null)
        {
            return plan with { Target = target };
        }

        target["interactionTargetStatus"] = "ready";
        target["interactionTargetId"] = row.TargetId;
        target["interactionSourcePerceptionEventId"] = row.SourcePerceptionEventId;
        target["chatRowTitle"] = row.Title;
        target["chatRowPreview"] = row.Preview;
        target["chatRowUnreadCount"] = row.UnreadCount;
        target["clickX"] = row.ClickX;
        target["clickY"] = row.ClickY;
        target["chatRowBounds"] = new Dictionary<string, object?>
        {
            ["left"] = row.Left,
            ["top"] = row.Top,
            ["width"] = row.Width,
            ["height"] = row.Height
        };
        target["chatRowConfidence"] = row.Confidence;
        target["interactionCategory"] = row.Category;

        return plan with { Target = target };
    }

    private async ValueTask<int> RunInteractionNavigatorAsync(
        InteractionContext interaction,
        PerceptionContext perception,
        OrchestratorCommandContext orchestrator,
        TrustSafetyGateDecision trustGate,
        HashSet<string> suspendedChannels,
        HashSet<string> existingIds,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableInteractionNavigator
            || _options.NavigatorMaxChatsPerCycle <= 0
            || interaction.Targets.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var minimumGap = TimeSpan.FromSeconds(Math.Max(1, _options.NavigatorMinimumSecondsBetweenClicks));
        if (_lastNavigatorClickAt != DateTimeOffset.MinValue && now - _lastNavigatorClickAt < minimumGap)
        {
            return 0;
        }

        var written = 0;
        foreach (var target in NavigatorCandidates(interaction, orchestrator, suspendedChannels))
        {
            var plan = CreateNavigatorOpenChatPlan(target, interaction, now);
            var scopedActionId = ScopedActionId(plan);
            if (await TryWritePlanAsync(plan, scopedActionId, trustGate, perception, interaction, existingIds, suspendedChannels, cancellationToken).ConfigureAwait(false))
            {
                written++;
                _lastNavigatorClickAt = DateTimeOffset.UtcNow;
            }

            if (written >= _options.NavigatorMaxChatsPerCycle)
            {
                break;
            }
        }

        return written;
    }

    private static IEnumerable<InteractionTarget> NavigatorCandidates(
        InteractionContext interaction,
        OrchestratorCommandContext orchestrator,
        HashSet<string> suspendedChannels)
    {
        return interaction.Targets
            .Where(target =>
                target.Actionable
                && !orchestrator.IsChannelPaused(target.ChannelId)
                && !suspendedChannels.Contains(target.ChannelId ?? string.Empty)
                && IsFreshInteractionTarget(interaction, target)
                && target.TargetType.Equals("chat_row", StringComparison.OrdinalIgnoreCase)
                && target.ClickX > 0
                && target.ClickY > 0
                && target.Width > 0
                && target.Height > 0
                && !string.IsNullOrWhiteSpace(target.ChannelId)
                && !string.IsNullOrWhiteSpace(target.Title))
            .OrderByDescending(target => target.UnreadCount)
            .ThenByDescending(target => target.Confidence)
            .ThenBy(target => ChannelOrder(target.ChannelId))
            .ThenBy(target => target.Top);
    }

    private static bool IsFreshInteractionTarget(InteractionContext interaction, InteractionTarget target)
    {
        return string.IsNullOrWhiteSpace(interaction.LatestPerceptionEventId)
            || string.Equals(target.SourcePerceptionEventId, interaction.LatestPerceptionEventId, StringComparison.OrdinalIgnoreCase);
    }

    private ActionPlan CreateNavigatorOpenChatPlan(InteractionTarget target, InteractionContext interaction, DateTimeOffset now)
    {
        var visitBucket = VisitBucket(now, _options.NavigatorRevisitMinutes);
        var decisionId = $"navigator-{StableHash($"{interaction.SourceInteractionEventId}|{target.TargetId}|{visitBucket}")[..20]}";
        var decision = new DecisionEvent
        {
            EventType = "decision_event",
            DecisionId = decisionId,
            CreatedAt = now,
            Goal = "learn_visible_whatsapp_chats",
            Intent = "learning_navigation",
            Confidence = target.Confidence,
            AutonomyLevel = _options.AutonomyLevel,
            ProposedAction = "open_visible_chat_for_learning",
            RequiresHumanConfirmation = false,
            ReasoningSummary = "Hands navigator selected a verified Interaction chat row.",
            Evidence = [target.SourcePerceptionEventId],
            ChannelId = target.ChannelId,
            ConversationTitle = target.Title
        };
        var targetData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceDecisionId"] = decisionId,
            ["channelId"] = target.ChannelId,
            ["conversationId"] = string.Empty,
            ["conversationTitle"] = target.Title,
            ["intent"] = decision.Intent,
            ["proposedAction"] = decision.ProposedAction,
            ["decisionConfidence"] = decision.Confidence,
            ["requiresHumanConfirmation"] = false,
            ["plannerReason"] = "Open a verified visible WhatsApp chat row so Perception can learn the conversation.",
            ["interactionEventId"] = interaction.SourceInteractionEventId,
            ["interactionTargetStatus"] = "ready",
            ["interactionTargetId"] = target.TargetId,
            ["interactionSourcePerceptionEventId"] = target.SourcePerceptionEventId,
            ["chatRowTitle"] = target.Title,
            ["chatRowPreview"] = target.Preview,
            ["chatRowUnreadCount"] = target.UnreadCount,
            ["clickX"] = target.ClickX,
            ["clickY"] = target.ClickY,
            ["chatRowBounds"] = new Dictionary<string, object?>
            {
                ["left"] = target.Left,
                ["top"] = target.Top,
                ["width"] = target.Width,
                ["height"] = target.Height
            },
            ["chatRowConfidence"] = target.Confidence,
            ["interactionCategory"] = target.Category,
            ["navigatorVisitBucket"] = visitBucket
        };

        var actionId = $"hands-nav-{StableHash($"{target.TargetId}|{visitBucket}")[..24]}";
        return new ActionPlan(
            actionId,
            "open_chat",
            targetData,
            3,
            false,
            "Navigator opens a verified visible chat row for learning.",
            decision);
    }

    private async ValueTask<bool> TryWritePlanAsync(
        ActionPlan plan,
        string actionId,
        TrustSafetyGateDecision trustGate,
        PerceptionContext perception,
        InteractionContext interaction,
        HashSet<string> existingIds,
        HashSet<string> suspendedChannels,
        CancellationToken cancellationToken)
    {
        _actionsPlanned++;
        if (existingIds.Contains(actionId))
        {
            _actionsSkipped++;
            return false;
        }

        var safetyPlan = EnrichPlanWithTrustSafety(plan, actionId, trustGate);
        var safety = _safety.Evaluate(safetyPlan);
        ActionEvent actionEvent;
        if (safety.Blocked)
        {
            _actionsBlocked++;
            actionEvent = CreateActionEvent(
                safetyPlan,
                actionId,
                "blocked",
                new ActionVerification(false, safety.Reason, 0),
                safety.Reason,
                executionSummary: string.Empty);
        }
        else
        {
            var authority = _cabinAuthorityGate.Evaluate(safetyPlan);
            var authorityPlan = EnrichPlanWithCabinAuthority(safetyPlan, authority);
            if (!authority.Allowed)
            {
                _actionsBlocked++;
                var auditActionId = $"{actionId}-authority-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 5}";
                if (existingIds.Contains(auditActionId))
                {
                    _actionsSkipped++;
                    return false;
                }

                actionEvent = CreateActionEvent(
                    authorityPlan,
                    auditActionId,
                    "blocked",
                    new ActionVerification(false, authority.Reason, authority.Confidence),
                    safety.Reason,
                    executionSummary: "Cabin Authority no autorizo tocar ventanas.");
            }
            else
            {
                var transaction = _transactionGate.Begin(authorityPlan, actionId, trustGate, perception, interaction);
                var transactionPlan = transaction.Plan;
                if (!transaction.Allowed)
                {
                    _actionsBlocked++;
                    var auditActionId = $"{actionId}-transaction-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 5}";
                    if (existingIds.Contains(auditActionId))
                    {
                        _actionsSkipped++;
                        return false;
                    }

                    actionEvent = CreateActionEvent(
                        transactionPlan,
                        auditActionId,
                        "blocked",
                        new ActionVerification(false, transaction.Reason, 0.94),
                        safety.Reason,
                        executionSummary: "Action Transaction Gate bloqueo antes de tocar pantalla.");
                    _transactionGate.Block(transaction, auditActionId, actionEvent);
                }
                else
                {
                    var lease = _inputArbiter.Acquire(transactionPlan);
                    var planForExecution = EnrichPlanWithInputArbiter(transactionPlan, lease);
                    if (!lease.Granted)
                    {
                        _actionsBlocked++;
                        var auditActionId = $"{actionId}-arbiter-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 5}";
                        if (existingIds.Contains(auditActionId))
                        {
                            _actionsSkipped++;
                            return false;
                        }

                        actionEvent = CreateActionEvent(
                            planForExecution,
                            auditActionId,
                            "blocked",
                            new ActionVerification(false, lease.Reason, 0.96),
                            safety.Reason,
                            executionSummary: "Input Arbiter cedio el mouse al operador.");
                        _transactionGate.Complete(transaction with { Plan = planForExecution }, actionEvent);
                    }
                    else
                    {
                        var execution = await _executor.ExecuteAsync(planForExecution, cancellationToken).ConfigureAwait(false);
                        _inputArbiter.Complete(lease, planForExecution, execution);
                        if (execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase))
                        {
                            _actionsExecuted++;
                        }

                        var verificationOutcome = await VerifyAfterExecutionAsync(
                            planForExecution,
                            execution,
                            perception,
                            cancellationToken).ConfigureAwait(false);
                        var verifiedPlan = EnrichPlanWithVerificationOutcome(planForExecution, verificationOutcome);
                        var verification = verificationOutcome.Verification;
                        var finalStatus = FinalActionStatus(verifiedPlan, execution, verification);
                        if (finalStatus.Equals("verified", StringComparison.OrdinalIgnoreCase))
                        {
                            _actionsVerified++;
                        }

                        actionEvent = CreateActionEvent(
                            verifiedPlan,
                            actionId,
                            finalStatus,
                            verification,
                            safety.Reason,
                            execution.Summary);
                        _transactionGate.Complete(transaction with { Plan = verifiedPlan }, actionEvent);
                    }
                }
            }
        }

        var errors = ActionContractValidator.Validate(actionEvent);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }

        if (ShouldSuspendChannel(safetyPlan, actionEvent))
        {
            var channelId = TargetString(safetyPlan, "channelId");
            if (!string.IsNullOrWhiteSpace(channelId))
            {
                suspendedChannels.Add(channelId);
            }
        }

        await _writer.AppendAsync(actionEvent, cancellationToken).ConfigureAwait(false);
        existingIds.Add(actionEvent.ActionId);
        _actionsWritten++;
        _lastActionId = actionEvent.ActionId;
        _lastSummary = $"{actionEvent.ActionType}: {actionEvent.Status}. {actionEvent.Verification.Summary}";
        return true;
    }

    private static bool ShouldSuspendChannel(ActionPlan enrichedPlan, ActionEvent actionEvent)
    {
        var text = $"{actionEvent.Status} {actionEvent.Verification.Summary} {actionEvent.Target.GetValueOrDefault("safetyReason")} {actionEvent.Target.GetValueOrDefault("executionSummary")}";
        if (actionEvent.Status.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            if (enrichedPlan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return text.Contains("No visible WhatsApp", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Could not focus", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Cabin registry", StringComparison.OrdinalIgnoreCase);
        }

        return enrichedPlan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            && actionEvent.Status.Equals("blocked", StringComparison.OrdinalIgnoreCase)
            && (text.Contains("no verified Interaction target coordinates", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Cabin Authority", StringComparison.OrdinalIgnoreCase));
    }

    private static ActionPlan EnrichPlanWithTrustSafety(ActionPlan plan, string scopedActionId, TrustSafetyGateDecision gate)
    {
        var sourceDecisionId = TargetString(plan, "sourceDecisionId") ?? string.Empty;
        var approvalId = FirstApproval(gate, sourceDecisionId, plan.ActionId, scopedActionId);
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["trustSafetyGateDecision"] = gate.Decision,
            ["trustSafetyGateReason"] = gate.Reason,
            ["trustSafetyAgeMs"] = gate.AgeMs,
            ["trustSafetyApproved"] = !string.IsNullOrWhiteSpace(approvalId),
            ["trustSafetyApprovalId"] = approvalId ?? string.Empty,
            ["trustSafetyVerifiedSources"] = gate.ApprovedSources.Count
        };

        return plan with { Target = target };
    }

    private static string? FirstApproval(TrustSafetyGateDecision gate, params string[] sourceIds)
    {
        foreach (var sourceId in sourceIds.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (gate.ApprovedSources.TryGetValue(sourceId, out var approvalId)
                && !string.IsNullOrWhiteSpace(approvalId))
            {
                return approvalId;
            }
        }

        return null;
    }

    private static ActionPlan EnrichPlanWithCabinAuthority(ActionPlan plan, CabinAuthorityDecision decision)
    {
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["cabinAuthorityAllowed"] = decision.Allowed,
            ["cabinAuthorityReason"] = decision.Reason,
            ["cabinAuthorityConfidence"] = decision.Confidence
        };

        return plan with { Target = target };
    }

    private static ActionPlan EnrichPlanWithInputArbiter(ActionPlan plan, InputLease lease)
    {
        if (!lease.RequiresInput)
        {
            return plan;
        }

        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["inputArbiterLeaseId"] = lease.LeaseId,
            ["inputArbiterGranted"] = lease.Granted,
            ["inputArbiterReason"] = lease.Reason,
            ["operatorIdleMs"] = lease.OperatorIdleMs,
            ["operatorIdleRequiredMs"] = lease.RequiredIdleMs,
            ["operatorHasPriority"] = !lease.Granted,
            ["handsPausedOnly"] = !lease.Granted,
            ["eyesContinue"] = true,
            ["memoryContinue"] = true,
            ["cognitiveContinue"] = true
        };

        return plan with { Target = target };
    }

    private async ValueTask<VerificationOutcome> VerifyAfterExecutionAsync(
        ActionPlan plan,
        ExecutionResult execution,
        PerceptionContext initialContext,
        CancellationToken cancellationToken)
    {
        var initialVerification = _verifier.Verify(plan, execution, initialContext);
        if (!ShouldWaitForFreshOpenChatVerification(plan, execution, initialVerification))
        {
            return new VerificationOutcome(initialVerification, initialContext, 0);
        }

        var timeout = Math.Max(0, _options.OpenChatVerificationTimeoutMs);
        var poll = Math.Max(50, _options.OpenChatVerificationPollMs);
        var stopwatch = Stopwatch.StartNew();
        var bestVerification = initialVerification;
        var bestContext = initialContext;

        while (stopwatch.ElapsedMilliseconds < timeout)
        {
            await Task.Delay(poll, cancellationToken).ConfigureAwait(false);
            var freshContext = await _perceptionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            var freshVerification = _verifier.Verify(plan, execution, freshContext);
            if (freshVerification.Verified)
            {
                return new VerificationOutcome(freshVerification, freshContext, (int)stopwatch.ElapsedMilliseconds);
            }

            if (freshVerification.Confidence >= bestVerification.Confidence)
            {
                bestVerification = freshVerification;
                bestContext = freshContext;
            }
        }

        var summary = $"No confirme el chat correcto despues de {stopwatch.ElapsedMilliseconds} ms; {bestVerification.Summary}";
        return new VerificationOutcome(
            new ActionVerification(false, summary, bestVerification.Confidence),
            bestContext,
            (int)stopwatch.ElapsedMilliseconds);
    }

    private bool ShouldWaitForFreshOpenChatVerification(ActionPlan plan, ExecutionResult execution, ActionVerification initialVerification)
    {
        return _options.ExecuteActions
            && _options.OpenChatVerificationTimeoutMs > 0
            && plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            && execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase)
            && !initialVerification.Verified;
    }

    private static ActionPlan EnrichPlanWithVerificationOutcome(ActionPlan plan, VerificationOutcome outcome)
    {
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["verifiedBeforeContinue"] = outcome.Verification.Verified,
            ["verificationWaitMs"] = outcome.WaitMs,
            ["verificationPerceptionEventId"] = outcome.Context.SourcePerceptionEventId,
            ["verificationObservedAt"] = outcome.Context.ObservedAt == default ? null : outcome.Context.ObservedAt,
            ["perceptionVerificationSummary"] = outcome.Verification.Summary
        };

        return plan with { Target = target };
    }

    private string FinalActionStatus(ActionPlan plan, ExecutionResult execution, ActionVerification verification)
    {
        if (verification.Verified
            && (execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase)
                || execution.Status.Equals("verified", StringComparison.OrdinalIgnoreCase)))
        {
            return "verified";
        }

        if (_options.RequirePostActionVerification
            && RequiresPostActionVerification(plan.ActionType)
            && execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return execution.Status;
    }

    private static bool RequiresPostActionVerification(string actionType)
    {
        return actionType.Equals("focus_window", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("capture_conversation", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    private bool CycleTransactionLimitReached(int writtenThisCycle)
    {
        return _options.ExecuteActions
            && _options.ActionTransactionsEnabled
            && _options.MaxPhysicalActionsPerCycle > 0
            && writtenThisCycle >= _options.MaxPhysicalActionsPerCycle;
    }

    private static long VisitBucket(DateTimeOffset now, int revisitMinutes)
    {
        var bucketSize = TimeSpan.FromMinutes(Math.Max(1, revisitMinutes)).Ticks;
        return now.UtcTicks / bucketSize;
    }

    private static int ChannelOrder(string channelId)
    {
        return channelId.ToLowerInvariant() switch
        {
            "wa-1" => 1,
            "wa-2" => 2,
            "wa-3" => 3,
            _ => 99
        };
    }

    private static string? TargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    public async ValueTask<HandsRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        HandsHealthState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            if (maxCycles > 0 && cycles >= maxCycles)
            {
                break;
            }

            if (duration is not null && DateTimeOffset.UtcNow - started >= duration.Value)
            {
                break;
            }

            try
            {
                await Task.Delay(Math.Max(50, _options.PollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new HandsRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            cycles,
            _idleCycles,
            _decisionsRead,
            _actionsPlanned,
            _actionsWritten,
            _actionsBlocked,
            _actionsExecuted,
            _actionsVerified,
            _actionsSkipped,
            _lastActionId,
            _lastSummary,
            _lastError);
    }

    private ActionEvent CreateActionEvent(
        ActionPlan plan,
        string actionId,
        string status,
        ActionVerification verification,
        string safetyReason,
        string executionSummary)
    {
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["baseActionId"] = plan.ActionId,
            ["executionMode"] = _options.ExecuteActions ? "execute" : "plan",
            ["requiredAutonomyLevel"] = plan.RequiredAutonomyLevel,
            ["handsAutonomyLevel"] = _options.AutonomyLevel,
            ["executeActions"] = _options.ExecuteActions,
            ["safetyReason"] = safetyReason,
            ["executionSummary"] = executionSummary
        };

        return new ActionEvent(
            "action_event",
            actionId,
            DateTimeOffset.UtcNow,
            plan.ActionType,
            target,
            status,
            verification);
    }

    private string ScopedActionId(ActionPlan plan)
    {
        var interactionTargetId = TargetString(plan, "interactionTargetId");
        var suffix = string.IsNullOrWhiteSpace(interactionTargetId)
            ? string.Empty
            : $"-{StableHash(interactionTargetId)[..8]}";
        return $"{plan.ActionId}-{(_options.ExecuteActions ? "exec" : "plan")}{suffix}";
    }

    private string DecisionCursorKey(string decisionId)
    {
        return $"{decisionId}|{ExecutionMode()}";
    }

    private string ExecutionMode()
    {
        return _options.ExecuteActions ? "execute" : "plan";
    }

    private static string StableHash(string raw)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private HandsHealthState CreateState(string status, string summary, string error)
    {
        return new HandsHealthState(
            status,
            DateTimeOffset.UtcNow,
            _decisionsRead,
            _actionsPlanned,
            _actionsWritten,
            _actionsBlocked,
            _actionsExecuted,
            _actionsVerified,
            _actionsSkipped,
            _options.CognitiveDecisionEventsFile,
            _options.OperatingDecisionEventsFile,
            _options.PerceptionEventsFile,
            _options.InteractionEventsFile,
            _options.ActionEventsFile,
            _lastActionId,
            summary,
            error);
    }

    private async ValueTask<HandsHealthState> WriteStateAsync(HandsHealthState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_options.StateFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await WriteTextAtomicAsync(_options.StateFile, json, cancellationToken).ConfigureAwait(false);
        var queueState = new Dictionary<string, object?>
        {
            ["status"] = state.Status,
            ["engine"] = "ariadgsm_action_queue",
            ["updatedAt"] = state.UpdatedAt,
            ["executionMode"] = _options.ExecuteActions ? "execute" : "plan",
            ["decisionsRead"] = state.DecisionsRead,
            ["actionsPlanned"] = state.ActionsPlanned,
            ["actionsWritten"] = state.ActionsWritten,
            ["actionsBlocked"] = state.ActionsBlocked,
            ["actionsExecuted"] = state.ActionsExecuted,
            ["actionsVerified"] = state.ActionsVerified,
            ["actionsSkipped"] = state.ActionsSkipped,
            ["lastActionId"] = state.LastActionId,
            ["lastSummary"] = state.LastSummary,
            ["decisionSources"] = new[]
            {
                _options.CognitiveDecisionEventsFile,
                _options.OperatingDecisionEventsFile,
                _options.BusinessDecisionEventsFile
            },
            ["contract"] = new[]
            {
                "decision -> plan",
                "plan -> safety",
                "safety -> cabin_authority",
                "cabin_authority -> input_arbiter",
                "input_arbiter -> action_transaction_gate",
                "input_arbiter -> execute",
                "execute -> verify",
                "verify -> audit_event"
            }
        };
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        await WriteTextAtomicAsync(
            _options.ActionQueueStateFile,
            JsonSerializer.Serialize(queueState, jsonOptions),
            cancellationToken).ConfigureAwait(false);
        await WriteTextAtomicAsync(
            _options.HandsVerificationStateFile,
            JsonSerializer.Serialize(CreateVerificationState(state), jsonOptions),
            cancellationToken).ConfigureAwait(false);
        return state;
    }

    private Dictionary<string, object?> CreateVerificationState(HandsHealthState state)
    {
        var status = string.IsNullOrWhiteSpace(state.LastError) ? state.Status : "error";
        var needsHuman = state.ActionsBlocked;
        return new Dictionary<string, object?>
        {
            ["contractVersion"] = "0.9.6",
            ["status"] = status,
            ["engine"] = "ariadgsm_hands_verification",
            ["version"] = "0.9.6",
            ["updatedAt"] = state.UpdatedAt,
            ["executionMode"] = _options.ExecuteActions ? "execute" : "plan",
            ["policy"] = new Dictionary<string, object?>
            {
                ["trustSafetyRequired"] = _options.RequireTrustSafetyGate,
                ["inputArbiterRequired"] = _options.InputArbiterEnabled,
                ["cabinAuthorityRequired"] = _options.RequireCabinAuthorityForWindowActions,
                ["postActionVerificationRequired"] = _options.RequirePostActionVerification,
                ["actionTransactionsEnabled"] = _options.ActionTransactionsEnabled,
                ["singlePhysicalAction"] = _options.MaxPhysicalActionsPerCycle == 1,
                ["freshPerceptionMaxAgeMs"] = _options.FreshPerceptionMaxAgeMs,
                ["freshInteractionMaxAgeMs"] = _options.FreshInteractionMaxAgeMs,
                ["noDestructiveBrowserPolicy"] = true,
                ["textDraftRequiresApproval"] = _options.RequireSafetyApprovalForTextDraft,
                ["sendRequiresApproval"] = _options.RequireSafetyApprovalForSend,
                ["allowTextInput"] = _options.AllowTextInput,
                ["allowSendMessage"] = _options.AllowSendMessage,
                ["physicalActions"] = new[] { "focus_window", "open_chat", "scroll_history", "capture_conversation", "write_text", "send_message" },
                ["historyScrollWheelSteps"] = _options.HistoryScrollWheelSteps
            },
            ["inputs"] = new Dictionary<string, object?>
            {
                ["cognitiveDecisionEventsFile"] = _options.CognitiveDecisionEventsFile,
                ["operatingDecisionEventsFile"] = _options.OperatingDecisionEventsFile,
                ["businessDecisionEventsFile"] = _options.BusinessDecisionEventsFile,
                ["perceptionEventsFile"] = _options.PerceptionEventsFile,
                ["interactionEventsFile"] = _options.InteractionEventsFile,
                ["trustSafetyStateFile"] = _options.TrustSafetyStateFile,
                ["inputArbiterStateFile"] = _options.InputArbiterStateFile,
                ["actionTransactionStateFile"] = _options.ActionTransactionStateFile,
                ["actionJournalFile"] = _options.ActionJournalFile,
                ["actionEventsFile"] = _options.ActionEventsFile
            },
            ["verificationGate"] = new Dictionary<string, object?>
            {
                ["verifiedBeforeContinueRequired"] = _options.RequirePostActionVerification,
                ["unverifiedPhysicalActionsBecomeFailed"] = _options.RequirePostActionVerification,
                ["openChatRequiresChannelTitleAndRow"] = true,
                ["scrollRequiresVisibleChannel"] = true,
                ["captureRequiresPerceptionConfirmation"] = true,
                ["draftsNeverSendAutomatically"] = true,
                ["singleActionBeforeNextRead"] = true,
                ["freshPerceptionBeforePhysicalAction"] = true,
                ["nonDestructiveBrowserPolicy"] = true
            },
            ["summary"] = new Dictionary<string, object?>
            {
                ["decisionsRead"] = state.DecisionsRead,
                ["actionsPlanned"] = state.ActionsPlanned,
                ["actionsWritten"] = state.ActionsWritten,
                ["actionsBlocked"] = state.ActionsBlocked,
                ["actionsExecuted"] = state.ActionsExecuted,
                ["actionsVerified"] = state.ActionsVerified,
                ["actionsSkipped"] = state.ActionsSkipped,
                ["needsHuman"] = needsHuman
            },
            ["lastAction"] = new Dictionary<string, object?>
            {
                ["actionId"] = state.LastActionId,
                ["summary"] = state.LastSummary,
                ["error"] = state.LastError
            },
            ["humanReport"] = new Dictionary<string, object?>
            {
                ["headline"] = HumanHeadline(status, needsHuman),
                ["resumenDecision"] = string.IsNullOrWhiteSpace(state.LastSummary) ? "Hands espera una decision verificable." : state.LastSummary,
                ["permitidas"] = state.ActionsVerified > 0 ? new[] { $"{state.ActionsVerified} accion(es) verificadas antes de continuar." } : Array.Empty<string>(),
                ["necesitanBryams"] = needsHuman > 0 ? new[] { $"{needsHuman} accion(es) bloqueadas o pendientes de aprobacion." } : Array.Empty<string>(),
                ["bloqueadas"] = state.ActionsBlocked > 0 ? new[] { $"{state.ActionsBlocked} accion(es) no pasaron seguridad/verificacion." } : Array.Empty<string>(),
                ["riesgos"] = string.IsNullOrWhiteSpace(state.LastError) ? Array.Empty<string>() : new[] { state.LastError }
            }
        };
    }

    private static string HumanHeadline(string status, int needsHuman)
    {
        if (status.Equals("error", StringComparison.OrdinalIgnoreCase))
        {
            return "Manos con error";
        }

        if (needsHuman > 0)
        {
            return "Manos esperando permiso o verificacion";
        }

        return status.Equals("ok", StringComparison.OrdinalIgnoreCase)
            ? "Manos verificadas"
            : "Manos en espera segura";
    }

    private static async ValueTask WriteTextAtomicAsync(string path, string text, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (IOException exception)
            {
                lastFailure = exception;
                if (attempt < 9)
                {
                    await Task.Delay(30 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                lastFailure = exception;
                if (attempt < 9)
                {
                    await Task.Delay(30 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                TryDeleteTempFile(tempPath);
            }
        }

        if (lastFailure is not null)
        {
            Console.Error.WriteLine($"Hands safe state writer skipped '{path}': {lastFailure.Message}");
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record VerificationOutcome(
        ActionVerification Verification,
        PerceptionContext Context,
        int WaitMs);
}
