using System.Text.Json;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Conversation;
using AriadGSM.Perception.Events;
using AriadGSM.Perception.Extraction;
using AriadGSM.Perception.Health;
using AriadGSM.Perception.Reader;
using AriadGSM.Perception.VisionInput;
using AriadGSM.Perception.WindowIdentity;

namespace AriadGSM.Perception.Pipeline;

public sealed class PerceptionPipeline
{
    private readonly PerceptionOptions _options;
    private readonly VisionEventReader _visionReader;
    private readonly WhatsAppWindowDetector _windowDetector = new();
    private readonly ChannelResolver _channelResolver;
    private readonly PerceptionEventWriter _writer;
    private readonly ConversationEventWriter _conversationWriter;
    private readonly IReaderCore _readerCore;
    private readonly MessageExtractor _messageExtractor;
    private readonly ConversationBuilder _conversationBuilder;
    private int _visionEventsRead;
    private int _perceptionEventsWritten;
    private int _conversationEventsWritten;
    private int _idleCycles;
    private string _lastProcessedVisionEventId = string.Empty;
    private int _lastWhatsAppWindowsDetected;
    private int _lastReaderLinesObserved;
    private int _lastMessagesExtracted;
    private IReadOnlyList<string> _lastChannelIds = [];
    private string _lastReaderStatus = string.Empty;
    private string _lastError = string.Empty;

    public PerceptionPipeline(PerceptionOptions options, IReaderCore? readerCore = null)
    {
        _options = options;
        _visionReader = new VisionEventReader(options.VisionEventsFile);
        _channelResolver = new ChannelResolver(options.ChannelMappings);
        _writer = new PerceptionEventWriter(options.PerceptionEventsFile);
        _conversationWriter = new ConversationEventWriter(options.ConversationEventsFile);
        _readerCore = readerCore ?? new AccessibilityReaderCore(options);
        _messageExtractor = new MessageExtractor(options);
        _conversationBuilder = new ConversationBuilder(options);
    }

    public async ValueTask<PerceptionHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return await RunCycleAsync(skipDuplicate: false, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PerceptionRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        PerceptionHealthState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunCycleAsync(skipDuplicate: true, cancellationToken).ConfigureAwait(false);
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

        return new PerceptionRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            _visionEventsRead,
            _perceptionEventsWritten,
            _conversationEventsWritten,
            cycles,
            _idleCycles,
            _lastWhatsAppWindowsDetected,
            _lastReaderLinesObserved,
            _lastMessagesExtracted,
            _lastChannelIds,
            _lastProcessedVisionEventId,
            _lastReaderStatus,
            _lastError);
    }

    private async ValueTask<PerceptionHealthState> RunCycleAsync(bool skipDuplicate, CancellationToken cancellationToken)
    {
        try
        {
            var visionEvent = await _visionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            if (visionEvent is null)
            {
                return await WriteStateAsync(CreateErrorState("no_vision_event_available", string.Empty, [], 0), cancellationToken).ConfigureAwait(false);
            }

            if (skipDuplicate && visionEvent.VisionEventId == _lastProcessedVisionEventId)
            {
                _idleCycles++;
                var idleState = new PerceptionHealthState(
                    "idle",
                    DateTimeOffset.UtcNow,
                    _visionEventsRead,
                    _perceptionEventsWritten,
                    _conversationEventsWritten,
                    _lastWhatsAppWindowsDetected,
                    _lastReaderLinesObserved,
                    _lastMessagesExtracted,
                    _lastChannelIds,
                    _options.VisionEventsFile,
                    _options.PerceptionEventsFile,
                    _options.ConversationEventsFile,
                    visionEvent.VisionEventId,
                    _lastReaderStatus,
                    string.Empty);
                return await WriteStateAsync(idleState, cancellationToken).ConfigureAwait(false);
            }

            _visionEventsRead++;
            var candidates = _windowDetector.Detect(visionEvent.VisibleWindows, _options.MinimumWhatsAppConfidence);
            var channels = _channelResolver.Resolve(candidates);
            var reads = new List<ChannelReadResult>();
            foreach (var channel in channels)
            {
                var readerResult = await _readerCore.ReadAsync(channel, cancellationToken).ConfigureAwait(false);
                var messages = _messageExtractor.Extract(readerResult, channel);
                var conversation = _conversationBuilder.Build(channel, readerResult, messages);
                reads.Add(new ChannelReadResult(channel, readerResult, messages, conversation));
            }

            var perceptionEvent = CreatePerceptionEvent(visionEvent, reads);
            var errors = PerceptionContractValidator.Validate(perceptionEvent);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            await _writer.AppendAsync(perceptionEvent, cancellationToken).ConfigureAwait(false);
            foreach (var read in reads)
            {
                if (read.Messages.Count == 0)
                {
                    continue;
                }

                var conversationErrors = ConversationContractValidator.Validate(read.Conversation);
                if (conversationErrors.Count > 0)
                {
                    throw new InvalidOperationException(string.Join("; ", conversationErrors));
                }

                await _conversationWriter.AppendAsync(read.Conversation, cancellationToken).ConfigureAwait(false);
                _conversationEventsWritten++;
            }

            _perceptionEventsWritten++;
            _lastProcessedVisionEventId = visionEvent.VisionEventId;
            _lastWhatsAppWindowsDetected = candidates.Count;
            _lastChannelIds = channels.Select(channel => channel.ChannelId).ToArray();
            _lastReaderLinesObserved = reads.Sum(read => read.ReaderResult.Lines.Count);
            _lastMessagesExtracted = reads.Sum(read => read.Messages.Count);
            _lastReaderStatus = reads.Count == 0 ? "no_whatsapp_channels" : string.Join(",", reads.Select(read => $"{read.Channel.ChannelId}:{read.ReaderResult.Status}"));
            _lastError = string.Empty;
            var state = new PerceptionHealthState(
                "ok",
                DateTimeOffset.UtcNow,
                _visionEventsRead,
                _perceptionEventsWritten,
                _conversationEventsWritten,
                candidates.Count,
                _lastReaderLinesObserved,
                _lastMessagesExtracted,
                channels.Select(channel => channel.ChannelId).ToArray(),
                _options.VisionEventsFile,
                _options.PerceptionEventsFile,
                _options.ConversationEventsFile,
                visionEvent.VisionEventId,
                _lastReaderStatus,
                string.Empty);
            return await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return await WriteStateAsync(CreateErrorState(exception.Message, string.Empty, [], 0), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private PerceptionEvent CreatePerceptionEvent(VisionEventEnvelope visionEvent, IReadOnlyList<ChannelReadResult> reads)
    {
        var objects = new List<PerceptionObject>();
        foreach (var read in reads)
        {
            var channel = read.Channel;
            var window = channel.Candidate.Window;
            objects.Add(new PerceptionObject(
                "window",
                channel.Confidence,
                window.Bounds,
                window.Title,
                "whatsapp_web",
                new Dictionary<string, object?>
                {
                    ["channelId"] = channel.ChannelId,
                    ["processName"] = window.ProcessName,
                    ["processId"] = window.ProcessId,
                    ["identityReason"] = channel.Candidate.Reason,
                    ["channelMethod"] = channel.Method,
                    ["readerLayer"] = "window_identity"
                }));

            objects.Add(new PerceptionObject(
                "conversation",
                read.ReaderResult.Confidence,
                window.Bounds,
                read.Conversation.ConversationTitle,
                "active_conversation",
                new Dictionary<string, object?>
                {
                    ["channelId"] = channel.ChannelId,
                    ["conversationId"] = read.Conversation.ConversationId,
                    ["readerSource"] = read.ReaderResult.Source,
                    ["readerStatus"] = read.ReaderResult.Status,
                    ["readerLines"] = read.ReaderResult.Lines.Count,
                    ["messageCount"] = read.Messages.Count,
                    ["historyLimitDays"] = _options.HistoryLimitDays
                }));

            foreach (var message in read.Messages.Take(80))
            {
                objects.Add(new PerceptionObject(
                    "message_bubble",
                    message.Confidence,
                    message.Bounds,
                    message.Text,
                    message.Direction,
                    new Dictionary<string, object?>
                    {
                        ["channelId"] = channel.ChannelId,
                        ["messageId"] = message.MessageId,
                        ["conversationId"] = read.Conversation.ConversationId,
                        ["source"] = message.Source
                    }));
            }
        }

        if (objects.Count == 0)
        {
            objects.Add(new PerceptionObject(
                "error",
                1,
                null,
                "No supported visible WhatsApp Web window was found in this vision event.",
                "diagnostic",
                new Dictionary<string, object?>
                {
                    ["expectedBrowsers"] = "chrome,msedge,firefox",
                    ["rule"] = "positive_whatsapp_window_identity"
                }));
        }

        return new PerceptionEvent(
            "perception_event",
            $"perception-{visionEvent.VisionEventId}",
            DateTimeOffset.UtcNow,
            visionEvent.VisionEventId,
            reads.Count == 1 ? reads[0].Channel.ChannelId : null,
            objects);
    }

    private PerceptionHealthState CreateErrorState(string error, string sourceVisionEventId, IReadOnlyList<string> channelIds, int windowCount)
    {
        return new PerceptionHealthState(
            "error",
            DateTimeOffset.UtcNow,
            _visionEventsRead,
            _perceptionEventsWritten,
            _conversationEventsWritten,
            windowCount,
            _lastReaderLinesObserved,
            _lastMessagesExtracted,
            channelIds,
            _options.VisionEventsFile,
            _options.PerceptionEventsFile,
            _options.ConversationEventsFile,
            sourceVisionEventId,
            _lastReaderStatus,
            error);
    }

    private async ValueTask<PerceptionHealthState> WriteStateAsync(PerceptionHealthState state, CancellationToken cancellationToken)
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
        await File.WriteAllTextAsync(_options.StateFile, json, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private sealed record ChannelReadResult(
        ResolvedChannel Channel,
        ReaderCoreResult ReaderResult,
        IReadOnlyList<ExtractedMessage> Messages,
        ConversationEvent Conversation);
}
