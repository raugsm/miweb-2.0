using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Pipeline;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;

TestPlannerInfersChannelFromEvidence();
TestSafetyBlocksSend();
await TestPipelineWritesAndDedupes();
await TestInteractionNavigatorOpensVerifiedRows();
await TestMissingChatTargetSuspendsDecisionChain();
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
            CursorFile = cursor,
            AutonomyLevel = 3,
            ExecuteActions = true,
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
