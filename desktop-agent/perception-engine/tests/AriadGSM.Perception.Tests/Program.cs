using System.IO;
using System.Text.Json;
using AriadGSM.Perception.ChannelResolution;
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
await TestOcrFallbackCommandReader();
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
            new ReaderTextLine("Cliente Uno", "Text", new VisionBounds(420, 74, 180, 26), 0.94, "test"),
            new ReaderTextLine("Cliente Uno", "Text", new VisionBounds(72, 164, 180, 26), 0.92, "test"),
            new ReaderTextLine("2 mensajes no leídos", "Text", new VisionBounds(72, 194, 150, 24), 0.9, "test"),
            new ReaderTextLine("Cuanto cuesta liberar Samsung", "Text", new VisionBounds(72, 222, 238, 24), 0.91, "test"),
            new ReaderTextLine("chat fijado", "Text", new VisionBounds(420, 220, 220, 32), 0.9, "test"),
            new ReaderTextLine("Cuanto cuesta liberar iPhone 14?", "Text", new VisionBounds(420, 300, 420, 44), 0.9, "test"),
            new ReaderTextLine("T\u00C3\u00BA: Te sale 80 dolares y demora 30 minutos", "Text", new VisionBounds(680, 380, 380, 44), 0.9, "test"),
            new ReaderTextLine("Ya hice pago de 25 usdt para liberar Samsung urgente en Mexico", "Text", new VisionBounds(420, 460, 560, 44), 0.9, "test"),
            new ReaderTextLine("foto", "Text", null, 0.9, "test")
        ]);
        var pipeline = new PerceptionPipeline(options, reader);
        var state = await pipeline.RunOnceAsync();
        Assert(state.Status == "ok", "reader pipeline should finish ok");
        Assert(state.ReaderLinesObserved == 11, "reader should observe eleven lines");
        Assert(state.MessagesExtracted == 3, "extractor should keep three useful messages");
        Assert(state.LastExtractionSummary.Contains("not_message_text", StringComparison.OrdinalIgnoreCase), "extractor diagnostics should explain rejected UI lines");
        Assert(state.ConversationEventsWritten == 1, "conversation builder should write one conversation");

        var conversationJson = await File.ReadAllTextAsync(conversationEvents);
        var conversation = JsonSerializer.Deserialize<ConversationEvent>(conversationJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert(conversation is not null, "conversation event should deserialize");
        Assert(ConversationContractValidator.Validate(conversation!).Count == 0, "conversation should satisfy contract");
        Assert(conversation!.ConversationTitle == "Cliente Uno", "conversation title should come from the active WhatsApp chat header");
        Assert(conversation.ConversationId.StartsWith("wa-1-", StringComparison.OrdinalIgnoreCase), "conversation id should stay channel-scoped");
        Assert(conversation!.Messages.Count == 3, "conversation should contain three messages");
        Assert(conversation.Messages.Any(message => message.Direction == "agent"), "agent direction should be detected from prefix");
        Assert(conversation.Messages.Any(message => message.Direction == "client"), "client direction should be inferred from bubble position");
        Assert(conversation.Messages.Any(message => message.Signals?.Any(signal => signal.Kind == "payment") == true), "payment semantic signal should be attached");
        Assert(conversation.Messages.Any(message => message.Signals?.Any(signal => signal.Kind == "country" && signal.Value == "MX") == true), "country semantic signal should be attached");

        var perceptionJson = await File.ReadAllTextAsync(perceptionEvents);
        using var perceptionDocument = JsonDocument.Parse(perceptionJson);
        var chatRow = perceptionDocument.RootElement.GetProperty("objects")
            .EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("objectType").GetString() == "chat_row");
        Assert(chatRow.ValueKind == JsonValueKind.Object, "perception should emit clickable chat_row objects");
        var metadata = chatRow.GetProperty("metadata");
        Assert(metadata.GetProperty("title").GetString() == "Cliente Uno", "chat row title should be preserved");
        Assert(metadata.GetProperty("clickX").GetInt32() > 0, "chat row should expose clickX");
        Assert(metadata.GetProperty("clickY").GetInt32() > 0, "chat row should expose clickY");

        var conversationObject = perceptionDocument.RootElement.GetProperty("objects")
            .EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("objectType").GetString() == "conversation");
        Assert(conversationObject.ValueKind == JsonValueKind.Object, "perception should emit an active conversation object");
        var conversationMetadata = conversationObject.GetProperty("metadata");
        Assert(conversationMetadata.GetProperty("conversationTitleSource").GetString() == "header_chat_row_match", "conversation should be matched to a visible chat row");
        Assert(conversationMetadata.GetProperty("matchedChatRowTitle").GetString() == "Cliente Uno", "matched row title should be exposed for hands targeting");
        Assert(conversationMetadata.GetProperty("matchedChatRowClickX").GetInt32() > 0, "matched row clickX should be exposed");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task TestOcrFallbackCommandReader()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-perception-ocr-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var frame = Path.Combine(root, "frame.bmp");
    var script = Path.Combine(root, "fake-ocr.cmd");
    try
    {
        await File.WriteAllTextAsync(frame, "fake image");
        await File.WriteAllTextAsync(script, "@echo OCR pago 33 usdt^|400^|300^|300^|40^|0.91\r\n");
        var options = new PerceptionOptions
        {
            OcrCommand = "cmd.exe",
            OcrArguments = $"/c \"\"{script}\"\"",
            OcrTimeoutMs = 2000
        };
        var reader = new OcrFallbackReaderCore(options);
        var window = new VisionWindow(300, "msedge", "(2) WhatsApp Business - Microsoft Edge", new VisionBounds(0, 0, 1140, 1390));
        var channel = new ResolvedChannel(
            "wa-1",
            new WhatsAppWindowCandidate(window, 0.95, "test"),
            0.95,
            "test");
        var visionEvent = new VisionEventEnvelope(
            "vision_event",
            "vision-ocr-test",
            DateTimeOffset.UtcNow,
            "screen_capture",
            true,
            new VisionFrameEvidence("frame-ocr", frame, 1140, 1390, "hash", true),
            new VisionRetentionEvidence(false, 1, 40),
            Windows: [window]);
        var result = await reader.ReadAsync(channel, new ReaderContext(visionEvent));
        Assert(result.Status == "ok", "OCR command fallback should return ok");
        Assert(result.Lines.Count == 1, "OCR fallback should parse one text line");
        Assert(result.Lines[0].Text.Contains("33 usdt", StringComparison.OrdinalIgnoreCase), "OCR fallback should preserve text");
        Assert(result.Lines[0].Bounds is not null, "OCR fallback should parse bounds");
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
