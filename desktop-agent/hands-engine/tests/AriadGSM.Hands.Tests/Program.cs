using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Input;
using AriadGSM.Hands.Pipeline;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;

TestPlannerInfersChannelFromEvidence();
TestSafetyBlocksSend();
TestTextDraftRequiresTrustSafetyApproval();
await TestPipelineWritesAndDedupes();
await TestBusinessBrainDecisionsReachHands();
await TestInteractionNavigatorOpensVerifiedRows();
await TestMissingChatTargetSuspendsDecisionChain();
await TestOpenChatWaitsForFreshPerceptionVerification();
await TestOpenChatFailsWhenPerceptionShowsDifferentChat();
await TestTrustSafetyGateBlocksHandsBeforeExecutor();
await TestInputArbiterYieldsMouseWithoutStoppingEyesOrMemory();
TestContractValidator();

Console.WriteLine("AriadGSM Hands tests OK");
return 0;

static void TestPlannerInfersChannelFromEvidence()
{
    var decision = new AriadGSM.Hands.Decisions.DecisionEvent
    {
        EventType = "decision_event",
        DecisionId = "decision-planner-1",
        CreatedAt = DateTimeOffset.UtcNow,
        Goal = "operate",
        Intent = "price_request",
        Confidence = 0.9,
        AutonomyLevel = 2,
        ProposedAction = "prepare_price_response",
        RequiresHumanConfirmation = true,
        ReasoningSummary = "price",
        Evidence = ["msg-wa-2-abc"]
    };

    var plans = new ActionPlanner().Plan(decision);
    Assert(plans.Count >= 2, "planner should create actionable plans");
    Assert(plans[0].Target["channelId"]?.ToString() == "wa-2", "planner should infer channel from evidence ids");
    Assert(plans.Any(plan => plan.ActionType == "capture_conversation"), "planner should request conversation capture");
}

static void TestSafetyBlocksSend()
{
    var decision = new AriadGSM.Hands.Decisions.DecisionEvent
    {
        EventType = "decision_event",
        DecisionId = "decision-send-1",
        CreatedAt = DateTimeOffset.UtcNow,
        Goal = "operate",
        Intent = "customer_waiting",
        Confidence = 0.9,
        AutonomyLevel = 6,
        ProposedAction = "send_message",
        RequiresHumanConfirmation = false,
        ReasoningSummary = "send",
        Evidence = ["msg-wa-1-abc"]
    };
    var plan = new ActionPlanner().Plan(decision).First(item => item.ActionType == "send_message");
    var safety = new HandsSafetyPolicy(new HandsOptions { AutonomyLevel = 6, AllowSendMessage = false }).Evaluate(plan);
    Assert(safety.Blocked, "send_message must be blocked when AllowSendMessage is false");
}

static void TestTextDraftRequiresTrustSafetyApproval()
{
    var decision = new AriadGSM.Hands.Decisions.DecisionEvent
    {
        EventType = "decision_event",
        DecisionId = "decision-draft-approval-1",
        CreatedAt = DateTimeOffset.UtcNow,
        Goal = "operate",
        Intent = "price_request",
        Confidence = 0.9,
        AutonomyLevel = 5,
        ProposedAction = "draft",
        RequiresHumanConfirmation = false,
        ReasoningSummary = "draft",
        Evidence = ["msg-wa-1-abc"]
    };
    var plan = new ActionPlanner().Plan(decision).First(item => item.ActionType == "write_text");
    var policy = new HandsSafetyPolicy(new HandsOptions
    {
        AutonomyLevel = 5,
        AllowTextInput = true,
        RequireSafetyApprovalForTextDraft = true
    });
    var blocked = policy.Evaluate(plan);
    Assert(blocked.Blocked, "write_text must be blocked without per-action Trust & Safety approval");

    var approvedTarget = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
    {
        ["trustSafetyApproved"] = true,
        ["trustSafetyApprovalId"] = "approval-draft-1"
    };
    var approved = policy.Evaluate(plan with { Target = approvedTarget });
    Assert(!approved.Blocked, "write_text can pass safety only after approval metadata is attached");
}

static async Task TestPipelineWritesAndDedupes()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var verificationState = Path.Combine(root, "hands-verification-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-pipeline-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "operate",
            intent = "price_request",
            confidence = 0.88,
            autonomyLevel = 2,
            proposedAction = "prepare_price_response",
            requiresHumanConfirmation = true,
            reasoningSummary = "price",
            conversationTitle = "Cliente",
            evidence = new[] { "msg-wa-1-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, JsonSerializer.Serialize(new
        {
            eventType = "perception_event",
            perceptionEventId = "perception-test-1",
            observedAt = DateTimeOffset.UtcNow,
            sourceVisionEventId = "vision-test-1",
            channelId = "wa-1",
            objects = new object[]
            {
                new
                {
                    objectType = "conversation",
                    confidence = 0.9,
                    text = "Cliente",
                    role = "active_conversation",
                    metadata = new
                    {
                        channelId = "wa-1",
                        conversationId = "wa-1-cliente"
                    }
                },
                new
                {
                    objectType = "chat_row",
                    confidence = 0.91,
                    bounds = new
                    {
                        left = 0,
                        top = 150,
                        width = 350,
                        height = 72
                    },
                    text = "Cliente",
                    role = "visible_chat_row",
                    metadata = new
                    {
                        channelId = "wa-1",
                        chatRowId = "chatrow-wa-1-test",
                        title = "Cliente",
                        preview = "Cuanto cuesta?",
                        unreadCount = 1,
                        clickX = 92,
                        clickY = 186
                    }
                }
            }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-test-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-test-1",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "interaction-target-client",
                    targetType = "chat_row",
                    channelId = "wa-1",
                    sourcePerceptionEventId = "perception-test-1",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente",
                    preview = "Cuanto cuesta?",
                    unreadCount = 1,
                    left = 0,
                    top = 150,
                    width = 350,
                    height = 72,
                    clickX = 92,
                    clickY = 186,
                    confidence = 0.94,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            },
            summary = new
            {
                targetsObserved = 1,
                targetsAccepted = 1,
                targetsRejected = 0,
                actionableTargets = 1,
                bestTargetTitle = "Cliente",
                lastRejectionReason = ""
            }
        }) + Environment.NewLine);

        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            HandsVerificationStateFile = verificationState,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var pipeline = new HandsPipeline(options);
        var first = await pipeline.RunOnceAsync();
        Assert(first.Status == "ok", "first pipeline run should be ok");
        Assert(first.ActionsWritten >= 2, "pipeline should write planned action events");
        Assert(File.Exists(actions), "action events should exist");
        Assert(File.Exists(verificationState), "hands verification state should be published");
        using (var verificationDocument = JsonDocument.Parse(await File.ReadAllTextAsync(verificationState)))
        {
            Assert(verificationDocument.RootElement.GetProperty("contractVersion").GetString() == "0.8.15", "hands verification state should expose the stage 12 contract");
            Assert(verificationDocument.RootElement.GetProperty("verificationGate").GetProperty("unverifiedPhysicalActionsBecomeFailed").GetBoolean(), "verification gate should fail unverified physical actions");
        }

        var actionLines = await File.ReadAllLinesAsync(actions);
        Assert(actionLines.Length == first.ActionsWritten, "all written actions should be persisted");
        var openChatHasCoordinates = false;
        foreach (var line in actionLines)
        {
            var actionEvent = JsonSerializer.Deserialize<ActionEvent>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert(actionEvent is not null, "action event should deserialize");
            Assert(ActionContractValidator.Validate(actionEvent!).Count == 0, "action event should satisfy contract");
            using var actionDocument = JsonDocument.Parse(line);
            var actionRoot = actionDocument.RootElement;
            if (actionRoot.GetProperty("actionType").GetString() == "open_chat")
            {
                var target = actionRoot.GetProperty("target");
                openChatHasCoordinates = target.TryGetProperty("clickX", out var clickX)
                    && clickX.GetInt32() == 92
                    && target.TryGetProperty("clickY", out var clickY)
                    && clickY.GetInt32() == 186;
            }
        }
        Assert(openChatHasCoordinates, "open_chat action should carry Perception click coordinates");

        var second = await pipeline.RunOnceAsync();
        Assert(second.Status == "idle", "second pipeline run should dedupe existing actions");
        Assert(second.ActionsSkipped == 0, "second run should ignore completed decisions without replay noise");

        var executor = new RecordingExecutor();
        var executeOptions = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            HandsVerificationStateFile = verificationState,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = false,
            RequireCabinAuthorityForWindowActions = false,
            InputArbiterEnabled = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var executePipeline = new HandsPipeline(executeOptions, executor);
        var execute = await executePipeline.RunOnceAsync();
        Assert(execute.Status == "ok", "execute mode should not be deduped by previous dry-run events");
        Assert(executor.Count > 0, "execute mode should call the executor");
        var executeLines = await File.ReadAllLinesAsync(actions);
        Assert(executeLines.Any(line => line.Contains("\"executionMode\":\"execute\"", StringComparison.Ordinal)),
            "execute events should be audited with executionMode=execute");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestBusinessBrainDecisionsReachHands()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-business-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var business = Path.Combine(root, "business-decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, string.Empty);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(business, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "business-decision-hands-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "attend_customer",
            intent = "learning_navigation",
            confidence = 0.91,
            autonomyLevel = 3,
            proposedAction = "open_visible_chat_for_learning",
            requiresHumanConfirmation = false,
            reasoningSummary = "Business Brain picked a customer chat.",
            channelId = "wa-2",
            conversationTitle = "Cliente Business",
            evidence = new[] { "msg-wa-2-business" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-business-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-business-1",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "target-business-1",
                    targetType = "chat_row",
                    channelId = "wa-2",
                    sourcePerceptionEventId = "perception-business-1",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente Business",
                    preview = "precio?",
                    unreadCount = 1,
                    left = 500,
                    top = 160,
                    width = 320,
                    height = 72,
                    clickX = 590,
                    clickY = 196,
                    confidence = 0.95,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            }
        }) + Environment.NewLine);

        var pipeline = new HandsPipeline(new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            BusinessDecisionEventsFile = business,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        });
        var result = await pipeline.RunOnceAsync();
        Assert(result.Status == "ok", "business decisions should feed hands in plan mode");
        var lines = await File.ReadAllLinesAsync(actions);
        Assert(lines.Any(line => line.Contains("business-decision-hands-1", StringComparison.Ordinal)), "action target should preserve Business Brain source decision id");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestInteractionNavigatorOpensVerifiedRows()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-navigator-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, string.Empty);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-nav-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-nav-1",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "interaction-target-nav-client",
                    targetType = "chat_row",
                    channelId = "wa-2",
                    sourcePerceptionEventId = "perception-nav-1",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente Navegable",
                    preview = "Necesito precio",
                    unreadCount = 2,
                    left = 500,
                    top = 190,
                    width = 330,
                    height = 72,
                    clickX = 590,
                    clickY = 226,
                    confidence = 0.96,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            },
            summary = new
            {
                targetsObserved = 1,
                targetsAccepted = 1,
                targetsRejected = 0,
                actionableTargets = 1,
                bestTargetTitle = "Cliente Navegable",
                lastRejectionReason = ""
            }
        }) + Environment.NewLine);

        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = true,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10,
            NavigatorMinimumSecondsBetweenClicks = 1
        };

        var pipeline = new HandsPipeline(options);
        var stateResult = await pipeline.RunOnceAsync();
        Assert(stateResult.Status == "ok", "navigator should write an action without waiting for cognitive decisions");

        var line = (await File.ReadAllLinesAsync(actions)).Single();
        using var document = JsonDocument.Parse(line);
        var rootElement = document.RootElement;
        Assert(rootElement.GetProperty("actionType").GetString() == "open_chat", "navigator should emit open_chat");
        var target = rootElement.GetProperty("target");
        Assert(target.GetProperty("sourceDecisionId").GetString()!.StartsWith("navigator-", StringComparison.Ordinal), "navigator action should be auditable");
        Assert(target.GetProperty("interactionTargetStatus").GetString() == "ready", "navigator should use verified interaction coordinates");
        Assert(target.GetProperty("clickX").GetInt32() == 590, "navigator should preserve clickX");
        Assert(target.GetProperty("clickY").GetInt32() == 226, "navigator should preserve clickY");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestMissingChatTargetSuspendsDecisionChain()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-suspend-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-missing-target-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "operate",
            intent = "price_request",
            confidence = 0.88,
            autonomyLevel = 3,
            proposedAction = "prepare_price_response",
            requiresHumanConfirmation = true,
            reasoningSummary = "price",
            conversationTitle = "Cliente Sin Fila",
            evidence = new[] { "msg-wa-1-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, string.Empty);

        var executor = new RecordingExecutor();
        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = false,
            InputArbiterEnabled = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var pipeline = new HandsPipeline(options, executor);
        var result = await pipeline.RunOnceAsync();
        Assert(result.Status == "ok", "missing target should be audited, not crash the pipeline");
        Assert(executor.Count == 0, "executor must not focus or capture when the chat row is missing");

        var lines = await File.ReadAllLinesAsync(actions);
        Assert(lines.Length == 1, "missing open_chat target should suspend dependent actions in the same cycle");
        using var document = JsonDocument.Parse(lines[0]);
        Assert(document.RootElement.GetProperty("actionType").GetString() == "open_chat", "the audited blocked action should be open_chat");
        Assert(document.RootElement.GetProperty("status").GetString() == "blocked", "open_chat should be blocked without verified coordinates");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestOpenChatWaitsForFreshPerceptionVerification()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-verify-wait-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-open-chat-verify-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "learn",
            intent = "learning_navigation",
            confidence = 0.9,
            autonomyLevel = 3,
            proposedAction = "open_visible_chat_for_learning",
            requiresHumanConfirmation = false,
            reasoningSummary = "open",
            channelId = "wa-2",
            conversationTitle = "Cliente Verificado",
            evidence = new[] { "msg-wa-2-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-open-chat-verify-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-open-chat-verify-0",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "target-open-chat-verify-1",
                    targetType = "chat_row",
                    channelId = "wa-2",
                    sourcePerceptionEventId = "perception-open-chat-verify-0",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente Verificado",
                    preview = "Necesito precio",
                    unreadCount = 1,
                    left = 450,
                    top = 180,
                    width = 320,
                    height = 72,
                    clickX = 520,
                    clickY = 216,
                    confidence = 0.96,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            }
        }) + Environment.NewLine);

        var executor = new FreshPerceptionExecutor(perception, "wa-2", "Cliente Verificado");
        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = false,
            RequireCabinAuthorityForWindowActions = false,
            InputArbiterEnabled = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            OpenChatVerificationTimeoutMs = 500,
            OpenChatVerificationPollMs = 50,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var pipeline = new HandsPipeline(options, executor);
        var result = await pipeline.RunOnceAsync();
        Assert(result.Status == "ok", "fresh perception verification should complete the cycle");
        Assert(executor.Count > 0, "executor should run the verified click");

        var openChatLine = (await File.ReadAllLinesAsync(actions))
            .First(line => line.Contains("\"actionType\":\"open_chat\"", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(openChatLine);
        var rootElement = document.RootElement;
        Assert(rootElement.GetProperty("status").GetString() == "verified", "open_chat should be verified before continuing");
        Assert(rootElement.GetProperty("verification").GetProperty("verified").GetBoolean(), "verification flag should be true");
        var target = rootElement.GetProperty("target");
        Assert(target.GetProperty("verifiedBeforeContinue").GetBoolean(), "target should audit verified-before-continue");
        Assert(target.GetProperty("verificationPerceptionEventId").GetString() == "perception-fresh-cliente-verificado", "fresh perception id should be recorded");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestOpenChatFailsWhenPerceptionShowsDifferentChat()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-verify-fail-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-open-chat-wrong-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "operate",
            intent = "price_request",
            confidence = 0.9,
            autonomyLevel = 3,
            proposedAction = "prepare_price_response",
            requiresHumanConfirmation = false,
            reasoningSummary = "price",
            channelId = "wa-1",
            conversationTitle = "Cliente Correcto",
            evidence = new[] { "msg-wa-1-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, JsonSerializer.Serialize(new
        {
            eventType = "perception_event",
            perceptionEventId = "perception-wrong-chat-1",
            observedAt = DateTimeOffset.UtcNow,
            channelId = "wa-1",
            objects = new object[]
            {
                new
                {
                    objectType = "conversation",
                    confidence = 0.9,
                    text = "Cliente Equivocado",
                    role = "active_conversation",
                    metadata = new
                    {
                        channelId = "wa-1",
                        conversationId = "wa-1-wrong"
                    }
                }
            }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-open-chat-wrong-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-wrong-chat-1",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "target-open-chat-wrong-1",
                    targetType = "chat_row",
                    channelId = "wa-1",
                    sourcePerceptionEventId = "perception-wrong-chat-1",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente Correcto",
                    preview = "Cuanto sale?",
                    unreadCount = 1,
                    left = 0,
                    top = 160,
                    width = 320,
                    height = 72,
                    clickX = 90,
                    clickY = 196,
                    confidence = 0.95,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            }
        }) + Environment.NewLine);

        var executor = new RecordingExecutor();
        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = false,
            RequireCabinAuthorityForWindowActions = false,
            InputArbiterEnabled = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            OpenChatVerificationTimeoutMs = 0,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var pipeline = new HandsPipeline(options, executor);
        var result = await pipeline.RunOnceAsync();
        Assert(result.Status == "ok", "wrong chat should be audited without crashing");
        Assert(executor.Count == 1, "executor should attempt only the open_chat action");

        var lines = await File.ReadAllLinesAsync(actions);
        Assert(lines.Length == 1, "failed open_chat should suspend dependent actions");
        using var document = JsonDocument.Parse(lines[0]);
        Assert(document.RootElement.GetProperty("status").GetString() == "failed", "open_chat should fail when perception shows another chat");
        Assert(!document.RootElement.GetProperty("verification").GetProperty("verified").GetBoolean(), "verification should be false for wrong chat");
        Assert(document.RootElement.GetProperty("verification").GetProperty("summary").GetString()!.Contains("Cliente Equivocado", StringComparison.Ordinal), "failure should explain the visible wrong chat");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestTrustSafetyGateBlocksHandsBeforeExecutor()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-trust-gate-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    var trust = Path.Combine(root, "trust-safety-state.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-trust-gate-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "learn",
            intent = "learning_navigation",
            confidence = 0.9,
            autonomyLevel = 3,
            proposedAction = "open_visible_chat_for_learning",
            requiresHumanConfirmation = false,
            reasoningSummary = "open",
            channelId = "wa-1",
            conversationTitle = "Cliente Trust",
            evidence = new[] { "msg-wa-1-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, string.Empty);
        await File.WriteAllTextAsync(trust, JsonSerializer.Serialize(new
        {
            status = "attention",
            updatedAt = DateTimeOffset.UtcNow,
            permissionGate = new
            {
                decision = "ASK_HUMAN",
                reason = "Bryams debe aprobar antes de tocar manos.",
                canHandsRun = false
            }
        }));

        var executor = new RecordingExecutor();
        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            TrustSafetyStateFile = trust,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = true,
            RequireCabinAuthorityForWindowActions = false,
            InputArbiterEnabled = false,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var pipeline = new HandsPipeline(options, executor);
        var result = await pipeline.RunOnceAsync();
        Assert(result.Status == "idle", "Trust & Safety gate should pause hands before execution");
        Assert(result.LastSummary.Contains("Trust & Safety", StringComparison.Ordinal), "state should explain Trust & Safety gate");
        Assert(executor.Count == 0, "executor must not run when Trust & Safety denies hands");
        Assert(!File.Exists(actions) || (await File.ReadAllTextAsync(actions)).Length == 0, "denied hands should not emit physical actions");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestInputArbiterYieldsMouseWithoutStoppingEyesOrMemory()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-hands-arbiter-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var cognitive = Path.Combine(root, "cognitive-decision-events.jsonl");
    var operating = Path.Combine(root, "decision-events.jsonl");
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interaction = Path.Combine(root, "interaction-events.jsonl");
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
    var cursor = Path.Combine(root, "hands-cursor.json");
    var arbiter = Path.Combine(root, "input-arbiter-state.json");
    try
    {
        await File.WriteAllTextAsync(cognitive, JsonSerializer.Serialize(new
        {
            eventType = "decision_event",
            decisionId = "decision-arbiter-1",
            createdAt = DateTimeOffset.UtcNow,
            goal = "operate",
            intent = "price_request",
            confidence = 0.9,
            autonomyLevel = 3,
            proposedAction = "prepare_price_response",
            requiresHumanConfirmation = false,
            reasoningSummary = "price",
            channelId = "wa-3",
            conversationTitle = "Cliente Operador",
            evidence = new[] { "msg-wa-3-abc" }
        }) + Environment.NewLine);
        await File.WriteAllTextAsync(operating, string.Empty);
        await File.WriteAllTextAsync(perception, string.Empty);
        await File.WriteAllTextAsync(interaction, JsonSerializer.Serialize(new
        {
            eventType = "interaction_event",
            interactionEventId = "interaction-arbiter-1",
            createdAt = DateTimeOffset.UtcNow,
            source = "ariadgsm_interaction_engine",
            latestPerceptionEventId = "perception-arbiter-1",
            perceptionEventsRead = 1,
            targets = new object[]
            {
                new
                {
                    targetId = "target-arbiter-1",
                    targetType = "chat_row",
                    channelId = "wa-3",
                    sourcePerceptionEventId = "perception-arbiter-1",
                    observedAt = DateTimeOffset.UtcNow,
                    title = "Cliente Operador",
                    preview = "Necesito servicio",
                    unreadCount = 1,
                    left = 900,
                    top = 160,
                    width = 320,
                    height = 72,
                    clickX = 980,
                    clickY = 196,
                    confidence = 0.95,
                    actionable = true,
                    category = "customer_chat_candidate",
                    rejectionReasons = Array.Empty<string>()
                }
            }
        }) + Environment.NewLine);

        var executor = new RecordingExecutor();
        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            InteractionEventsFile = interaction,
            ActionEventsFile = actions,
            StateFile = state,
            CursorFile = cursor,
            InputArbiterStateFile = arbiter,
            AutonomyLevel = 3,
            ExecuteActions = true,
            RequireTrustSafetyGate = false,
            RequireCabinAuthorityForWindowActions = false,
            InputArbiterEnabled = true,
            OperatorOverrideActive = true,
            OperatorIdleRequiredMs = int.MaxValue,
            RespectOrchestratorCommands = false,
            EnableInteractionNavigator = false,
            DecisionLimit = 10,
            PerceptionLimit = 10,
            InteractionLimit = 10
        };

        var sourceDecision = new AriadGSM.Hands.Decisions.DecisionEvent
        {
            EventType = "decision_event",
            DecisionId = "decision-arbiter-direct",
            CreatedAt = DateTimeOffset.UtcNow,
            Goal = "operate",
            Intent = "price_request",
            Confidence = 0.9,
            AutonomyLevel = 3,
            ProposedAction = "open_chat",
            RequiresHumanConfirmation = false,
            ReasoningSummary = "open",
            ChannelId = "wa-3",
            ConversationTitle = "Cliente Operador",
            Evidence = ["msg-wa-3-abc"]
        };
        var plan = new ActionPlan(
            "action-arbiter-direct",
            "open_chat",
            new Dictionary<string, object?> { ["channelId"] = "wa-3", ["conversationTitle"] = "Cliente Operador" },
            3,
            false,
            "test",
            sourceDecision);
        var lease = new InputArbiter(options).Acquire(plan);
        Assert(!lease.Granted, "arbiter must not grant mouse while operator override is active");

        using var arbiterDocument = JsonDocument.Parse(await File.ReadAllTextAsync(arbiter));
        Assert(arbiterDocument.RootElement.GetProperty("phase").GetString() == "operator_control", "arbiter state should show operator control");
        Assert(arbiterDocument.RootElement.GetProperty("decision").GetString() == "PAUSE_FOR_OPERATOR", "arbiter state should publish pause decision");
        Assert(arbiterDocument.RootElement.GetProperty("activeOwner").GetString() == "operator", "arbiter state should name the active owner");
        Assert(arbiterDocument.RootElement.GetProperty("handsPausedOnly").GetBoolean(), "arbiter state should pause only hands");
        Assert(arbiterDocument.RootElement.GetProperty("eyesContinue").GetBoolean(), "arbiter state should keep eyes on");
        Assert(arbiterDocument.RootElement.GetProperty("memoryContinue").GetBoolean(), "arbiter state should keep memory on");
        Assert(arbiterDocument.RootElement.GetProperty("businessBrainContinue").GetBoolean(), "arbiter state should keep business brain on");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void TestContractValidator()
{
    var actionEvent = new ActionEvent(
        "action_event",
        "hands-test",
        DateTimeOffset.UtcNow,
        "focus_window",
        new Dictionary<string, object?> { ["channelId"] = "wa-1" },
        "planned",
        new ActionVerification(false, "planned", 0.5));
    Assert(ActionContractValidator.Validate(actionEvent).Count == 0, "valid action event should pass");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class RecordingExecutor : IHandsExecutor
{
    public int Count { get; private set; }

    public ValueTask<ExecutionResult> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(new ExecutionResult("executed", $"Recorded {plan.ActionType}.", 0.95));
    }
}

internal sealed class FreshPerceptionExecutor : IHandsExecutor
{
    private readonly string _perceptionPath;
    private readonly string _channelId;
    private readonly string _title;

    public FreshPerceptionExecutor(string perceptionPath, string channelId, string title)
    {
        _perceptionPath = perceptionPath;
        _channelId = channelId;
        _title = title;
    }

    public int Count { get; private set; }

    public ValueTask<ExecutionResult> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken = default)
    {
        Count++;
        if (plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase))
        {
            File.AppendAllText(_perceptionPath, JsonSerializer.Serialize(new
            {
                eventType = "perception_event",
                perceptionEventId = "perception-fresh-cliente-verificado",
                observedAt = DateTimeOffset.UtcNow,
                channelId = _channelId,
                objects = new object[]
                {
                    new
                    {
                        objectType = "conversation",
                        confidence = 0.97,
                        text = _title,
                        role = "active_conversation",
                        metadata = new
                        {
                            channelId = _channelId,
                            conversationId = $"{_channelId}-fresh"
                        }
                    }
                }
            }) + Environment.NewLine);
        }

        return ValueTask.FromResult(new ExecutionResult("executed", $"Recorded {plan.ActionType}.", 0.95));
    }
}
