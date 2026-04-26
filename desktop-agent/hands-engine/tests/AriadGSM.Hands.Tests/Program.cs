using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Pipeline;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;

TestPlannerInfersChannelFromEvidence();
TestSafetyBlocksSend();
await TestPipelineWritesAndDedupes();
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
    var actions = Path.Combine(root, "action-events.jsonl");
    var state = Path.Combine(root, "hands-state.json");
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
                }
            }
        }) + Environment.NewLine);

        var options = new HandsOptions
        {
            CognitiveDecisionEventsFile = cognitive,
            OperatingDecisionEventsFile = operating,
            PerceptionEventsFile = perception,
            ActionEventsFile = actions,
            StateFile = state,
            AutonomyLevel = 3,
            ExecuteActions = false,
            DecisionLimit = 10,
            PerceptionLimit = 10
        };

        var pipeline = new HandsPipeline(options);
        var first = await pipeline.RunOnceAsync();
        Assert(first.Status == "ok", "first pipeline run should be ok");
        Assert(first.ActionsWritten >= 2, "pipeline should write planned action events");
        Assert(File.Exists(actions), "action events should exist");

        var actionLines = await File.ReadAllLinesAsync(actions);
        Assert(actionLines.Length == first.ActionsWritten, "all written actions should be persisted");
        foreach (var line in actionLines)
        {
            var actionEvent = JsonSerializer.Deserialize<ActionEvent>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert(actionEvent is not null, "action event should deserialize");
            Assert(ActionContractValidator.Validate(actionEvent!).Count == 0, "action event should satisfy contract");
        }

        var second = await pipeline.RunOnceAsync();
        Assert(second.Status == "idle", "second pipeline run should dedupe existing actions");
        Assert(second.ActionsSkipped >= first.ActionsWritten, "second run should skip known action ids");
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
