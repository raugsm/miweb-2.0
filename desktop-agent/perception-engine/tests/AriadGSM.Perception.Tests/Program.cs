using System.Text.Json;
using System.IO;
using AriadGSM.Perception.Conversation;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Events;
using AriadGSM.Perception.Pipeline;
using AriadGSM.Perception.Reader;
using AriadGSM.Perception.VisionInput;
using AriadGSM.Perception.WindowIdentity;

TestWhatsAppIdentity();
await TestPipelineContract();
await TestReaderExtractorConversationPipeline();
await TestContinuousDedupe();

Console.WriteLine("AriadGSM Perception tests OK");
return 0;

static void TestWhatsAppIdentity()
{
    var detector = new WhatsAppWindowDetector();
    var windows = new[]
    {
        new VisionWindow(10, "chrome", "Codex - Google Chrome", new VisionBounds(0, 0, 1000, 900)),
        new VisionWindow(11, "msedge", "(47) WhatsApp Business - Perfil 1: Microsoft Edge", new VisionBounds(0, 0, 1000, 900)),
        new VisionWindow(12, "Telegram", "(6) Official Group", new VisionBounds(0, 0, 1000, 900))
    };

    var candidates = detector.Detect(windows, 0.75);
    Assert(candidates.Count == 1, "only the real browser WhatsApp window should pass");
    Assert(candidates[0].Window.ProcessName == "msedge", "Edge WhatsApp should be detected");
}

static async Task TestPipelineContract()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-perception-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var visionEvents = Path.Combine(root, "vision-events.jsonl");
    var perceptionEvents = Path.Combine(root, "perception-events.jsonl");
    var stateFile = Path.Combine(root, "perception-health.json");
    try
    {
        var visionEvent = new VisionEventEnvelope(
            "vision_event",
            "vision-test-1",
            DateTimeOffset.UtcNow,
            "screen_capture",
            true,
            new VisionFrameEvidence("frame-1", @"D:\AriadGSM\vision-buffer\frame.bmp", 3440, 1440, "hash", true),
            new VisionRetentionEvidence(false, 1, 40),
            Windows:
            [
                new VisionWindow(100, "firefox", "(8) WhatsApp Business - Mozilla Firefox", new VisionBounds(2200, 0, 1140, 1390)),
                new VisionWindow(101, "chrome", "Codex - Google Chrome", new VisionBounds(1100, 0, 1140, 1390))
            ]);
        var json = JsonSerializer.Serialize(visionEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(visionEvents, json + Environment.NewLine);

        var options = new PerceptionOptions
        {
            VisionEventsFile = visionEvents,
            PerceptionEventsFile = perceptionEvents,
            StateFile = stateFile
        };
        var pipeline = new PerceptionPipeline(options);
        var state = await pipeline.RunOnceAsync();
        Assert(state.Status == "ok", "pipeline should finish ok");
        Assert(state.WhatsAppWindowsDetected == 1, "pipeline should detect one WhatsApp window");
        Assert(state.ChannelIds.Contains("wa-3"), "Firefox should resolve to wa-3");
        Assert(File.Exists(perceptionEvents), "perception events file should exist");

        var perceptionJson = await File.ReadAllTextAsync(perceptionEvents);
        var perceptionEvent = JsonSerializer.Deserialize<PerceptionEvent>(perceptionJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert(perceptionEvent is not null, "perception event should deserialize");
        Assert(PerceptionContractValidator.Validate(perceptionEvent!).Count == 0, "perception event should satisfy contract");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestReaderExtractorConversationPipeline()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-perception-reader-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var visionEvents = Path.Combine(root, "vision-events.jsonl");
    var perceptionEvents = Path.Combine(root, "perception-events.jsonl");
    var conversationEvents = Path.Combine(root, "conversation-events.jsonl");
    var stateFile = Path.Combine(root, "perception-health.json");
    try
    {
        var visionEvent = new VisionEventEnvelope(
            "vision_event",
            "vision-test-reader",
            DateTimeOffset.UtcNow,
            "screen_capture",
            true,
            new VisionFrameEvidence("frame-reader", @"D:\AriadGSM\vision-buffer\frame.bmp", 3440, 1440, "hash", true),
            new VisionRetentionEvidence(false, 1, 40),
            Windows:
            [
                new VisionWindow(200, "msedge", "(12) WhatsApp Business - Microsoft Edge", new VisionBounds(0, 0, 1140, 1390))
            ]);
        var json = JsonSerializer.Serialize(visionEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(visionEvents, json + Environment.NewLine);

        var options = new PerceptionOptions
        {
            VisionEventsFile = visionEvents,
            PerceptionEventsFile = perceptionEvents,
            ConversationEventsFile = conversationEvents,
            StateFile = stateFile
        };
        var reader = new StaticReaderCore(
        [
            new ReaderTextLine("WhatsApp Business", "Text", null, 0.9, "test"),
            new ReaderTextLine("10:42", "Text", null, 0.9, "test"),
            new ReaderTextLine("Cuanto cuesta liberar iPhone 14?", "Text", new VisionBounds(420, 300, 420, 44), 0.9, "test"),
            new ReaderTextLine("Tú: Te sale 80 dolares y demora 30 minutos", "Text", new VisionBounds(680, 380, 380, 44), 0.9, "test"),
            new ReaderTextLine("foto", "Text", null, 0.9, "test")
        ]);
        var pipeline = new PerceptionPipeline(options, reader);
        var state = await pipeline.RunOnceAsync();
        Assert(state.Status == "ok", "reader pipeline should finish ok");
        Assert(state.ReaderLinesObserved == 5, "reader should observe five lines");
        Assert(state.MessagesExtracted == 2, "extractor should keep two useful messages");
        Assert(state.ConversationEventsWritten == 1, "conversation builder should write one conversation");

        var conversationJson = await File.ReadAllTextAsync(conversationEvents);
        var conversation = JsonSerializer.Deserialize<ConversationEvent>(conversationJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert(conversation is not null, "conversation event should deserialize");
        Assert(ConversationContractValidator.Validate(conversation!).Count == 0, "conversation should satisfy contract");
        Assert(conversation!.Messages.Count == 2, "conversation should contain two messages");
        Assert(conversation.Messages.Any(message => message.Direction == "agent"), "agent direction should be detected from prefix");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestContinuousDedupe()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-perception-dedupe-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var visionEvents = Path.Combine(root, "vision-events.jsonl");
    var perceptionEvents = Path.Combine(root, "perception-events.jsonl");
    var stateFile = Path.Combine(root, "perception-health.json");
    try
    {
        var visionEvent = new VisionEventEnvelope(
            "vision_event",
            "vision-test-dedupe",
            DateTimeOffset.UtcNow,
            "screen_capture",
            true,
            new VisionFrameEvidence("frame-dedupe", @"D:\AriadGSM\vision-buffer\frame.bmp", 3440, 1440, "hash", true),
            new VisionRetentionEvidence(false, 1, 40),
            Windows:
            [
                new VisionWindow(100, "msedge", "(47) WhatsApp Business - Microsoft Edge", new VisionBounds(0, 0, 1140, 1390))
            ]);
        var json = JsonSerializer.Serialize(visionEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(visionEvents, json + Environment.NewLine);

        var options = new PerceptionOptions
        {
            VisionEventsFile = visionEvents,
            PerceptionEventsFile = perceptionEvents,
            StateFile = stateFile,
            PollIntervalMs = 10
        };
        var pipeline = new PerceptionPipeline(options);
        var summary = await pipeline.RunContinuousAsync(maxCycles: 3);
        Assert(summary.Cycles == 3, "continuous run should complete three cycles");
        Assert(summary.PerceptionEventsWritten == 1, "duplicate source vision event should be emitted once");
        Assert(summary.IdleCycles == 2, "duplicate cycles should be idle");
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
