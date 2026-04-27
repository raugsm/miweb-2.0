using System.Text;
using System.Text.Json;
using AriadGSM.Perception.ChatRows;
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
    private readonly ChatRowExtractor _chatRowExtractor = new();
    private readonly ConversationIdentityResolver _conversationIdentityResolver = new();
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
    private string _lastExtractionSummary = string.Empty;
    private string _lastError = string.Empty;

    public PerceptionPipeline(PerceptionOptions options, IReaderCore? readerCore = null)
    {
        _options = options;
        _visionReader = new VisionEventReader(options.VisionEventsFile);
        _channelResolver = new ChannelResolver(options.ChannelMappings);
        _writer = new PerceptionEventWriter(options.PerceptionEventsFile);
        _conversationWriter = new ConversationEventWriter(options.ConversationEventsFile);
        _readerCore = readerCore ?? new CompositeReaderCore(
            new AccessibilityReaderCore(options),
            new OcrFallbackReaderCore(options),
            options);
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
            _lastExtractionSummary,
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
                    _lastExtractionSummary,
                    string.Empty);
                return await WriteStateAsync(idleState, cancellationToken).ConfigureAwait(false);
            }

            _visionEventsRead++;
            var candidates = _windowDetector.Detect(visionEvent.VisibleWindows, _options.MinimumWhatsAppConfidence);
            var channels = _channelResolver.Resolve(candidates);
            var reads = new List<ChannelReadResult>();
            var readerContext = new ReaderContext(visionEvent);
            foreach (var channel in channels)
            {
                var readerResult = await _readerCore.ReadAsync(channel, readerContext, cancellationToken).ConfigureAwait(false);
                var extraction = _messageExtractor.Extract(readerResult, channel);
                var chatRows = _chatRowExtractor.Extract(readerResult, channel);
                var identity = _conversationIdentityResolver.Resolve(channel, readerResult, chatRows);
                var conversation = _conversationBuilder.Build(channel, readerResult, extraction.Messages, identity);
                reads.Add(new ChannelReadResult(channel, readerResult, extraction, chatRows, identity, conversation));
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
                if (read.Extraction.Messages.Count == 0
                    || (_options.RequireReliableConversationIdentity && !read.Conversation.Quality.IsReliable))
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
            _lastMessagesExtracted = reads.Sum(read => read.Extraction.Messages.Count);
            _lastReaderStatus = reads.Count == 0 ? "no_whatsapp_channels" : string.Join(",", reads.Select(read => $"{read.Channel.ChannelId}:{read.ReaderResult.Status}"));
            _lastExtractionSummary = reads.Count == 0
                ? "no_whatsapp_channels"
                : string.Join(" | ", reads.Select(read => $"{read.Channel.ChannelId}:{read.Extraction.Diagnostics.Summary()}"));
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
                _lastExtractionSummary,
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

            foreach (var row in read.ChatRows.Rows)
            {
                objects.Add(new PerceptionObject(
                    "chat_row",
                    row.Confidence,
                    row.Bounds,
                    row.Title,
                    "visible_chat_row",
                    new Dictionary<string, object?>
                    {
                        ["channelId"] = row.ChannelId,
                        ["chatRowId"] = row.ChatRowId,
                        ["title"] = row.Title,
                        ["preview"] = row.Preview,
                        ["unreadCount"] = row.UnreadCount,
                        ["clickX"] = row.ClickX,
                        ["clickY"] = row.ClickY,
                        ["sourceLines"] = row.SourceLines,
                        ["readerLayer"] = "chat_row_coordinates"
                    }));
            }

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
                    ["chatRows"] = read.ChatRows.Rows.Count,
                    ["chatRowCandidateLines"] = read.ChatRows.CandidateLines,
                    ["conversationTitleSource"] = read.Identity.Source,
                    ["conversationTitleConfidence"] = read.Identity.Confidence,
                    ["matchedChatRowId"] = read.Identity.MatchedChatRow?.ChatRowId,
                    ["matchedChatRowTitle"] = read.Identity.MatchedChatRow?.Title,
                    ["matchedChatRowClickX"] = read.Identity.MatchedChatRow?.ClickX,
                    ["matchedChatRowClickY"] = read.Identity.MatchedChatRow?.ClickY,
                    ["messageCount"] = read.Extraction.Messages.Count,
                    ["historyLimitDays"] = _options.HistoryLimitDays,
                    ["extractionDiagnostics"] = read.Extraction.Diagnostics,
                    ["signalKinds"] = read.Extraction.Messages
                        .SelectMany(message => message.Signals)
                        .Select(signal => signal.Kind)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                }));

            foreach (var message in read.Extraction.Messages.Take(80))
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
                        ["source"] = message.Source,
                        ["signals"] = message.Signals,
                        ["signalKinds"] = message.Signals.Select(signal => signal.Kind).ToArray()
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
            _lastExtractionSummary,
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
        await WriteTextAtomicAsync(_options.StateFile, json, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private static async ValueTask WriteTextAtomicAsync(string path, string text, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 7)
                {
                    await Task.Delay(25 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed record ChannelReadResult(
        ResolvedChannel Channel,
        ReaderCoreResult ReaderResult,
        MessageExtractionResult Extraction,
        ChatRowExtractionResult ChatRows,
        ConversationIdentity Identity,
        ConversationEvent Conversation);
}
