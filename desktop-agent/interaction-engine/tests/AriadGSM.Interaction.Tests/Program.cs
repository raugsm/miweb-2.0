using System.Text.Json;
using AriadGSM.Interaction.Config;
using AriadGSM.Interaction.Pipeline;

await TestInteractionAcceptsOnlyRealChatRows();

Console.WriteLine("AriadGSM Interaction tests OK");
return 0;

static async Task TestInteractionAcceptsOnlyRealChatRows()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-interaction-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var perception = Path.Combine(root, "perception-events.jsonl");
    var interactions = Path.Combine(root, "interaction-events.jsonl");
    var stateFile = Path.Combine(root, "interaction-state.json");
    try
    {
        await File.WriteAllTextAsync(perception, JsonSerializer.Serialize(new
        {
            eventType = "perception_event",
            perceptionEventId = "perception-interaction-test-1",
            observedAt = DateTimeOffset.UtcNow,
            sourceVisionEventId = "vision-test-1",
            channelId = "wa-2",
            objects = new object[]
            {
                new
                {
                    objectType = "chat_row",
                    confidence = 0.92,
                    bounds = new { left = 0, top = 160, width = 360, height = 70 },
                    text = "Cliente Precio",
                    metadata = new
                    {
                        channelId = "wa-2",
                        chatRowId = "chatrow-wa-2-client",
                        title = "Cliente Precio",
                        preview = "Cuanto sale liberar Samsung?",
                        unreadCount = 2,
                        clickX = 94,
                        clickY = 195
                    }
                },
                new
                {
                    objectType = "chat_row",
                    confidence = 0.95,
                    bounds = new { left = 0, top = 230, width = 360, height = 70 },
                    text = "Pagos Mexico",
                    metadata = new
                    {
                        channelId = "wa-2",
                        chatRowId = "chatrow-wa-2-pagos",
                        title = "Pagos Mexico",
                        preview = "ok",
                        unreadCount = 10,
                        clickX = 94,
                        clickY = 265
                    }
                },
                new
                {
                    objectType = "conversation",
                    confidence = 0.88,
                    text = "Anadir esta pagina a marcadores (Ctrl+D)",
                    role = "active_conversation",
                    metadata = new
                    {
                        channelId = "wa-2",
                        conversationId = "bad-browser-ui"
                    }
                }
            }
        }) + Environment.NewLine);

        var pipeline = new InteractionPipeline(new InteractionOptions
        {
            PerceptionEventsFile = perception,
            InteractionEventsFile = interactions,
            StateFile = stateFile,
            PerceptionLimit = 20
        });
        var state = await pipeline.RunOnceAsync();
        Assert(state.Status == "ok", "interaction run should be ok");
        Assert(state.ActionableTargets == 1, "only the real customer row should be actionable");
        Assert(state.TargetsRejected >= 2, "payment group and browser UI should be rejected");
        Assert(state.LastAcceptedTargetTitle == "Cliente Precio", "best target should be the customer chat row");

        var line = (await File.ReadAllLinesAsync(interactions)).Single();
        using var document = JsonDocument.Parse(line);
        var targets = document.RootElement.GetProperty("targets").EnumerateArray().ToArray();
        var actionable = targets.Single(target => target.GetProperty("actionable").GetBoolean());
        Assert(actionable.GetProperty("clickX").GetInt32() == 94, "actionable target should keep clickX");
        Assert(actionable.GetProperty("clickY").GetInt32() == 195, "actionable target should keep clickY");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
