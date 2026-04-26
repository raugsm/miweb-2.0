using System.Text.Json;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Events;
using AriadGSM.Perception.Health;
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
    private int _visionEventsRead;
    private int _perceptionEventsWritten;
    private int _idleCycles;
    private string _lastProcessedVisionEventId = string.Empty;
    private int _lastWhatsAppWindowsDetected;
    private IReadOnlyList<string> _lastChannelIds = [];
    private string _lastError = string.Empty;

    public PerceptionPipeline(PerceptionOptions options)
    {
        _options = options;
        _visionReader = new VisionEventReader(options.VisionEventsFile);
        _channelResolver = new ChannelResolver(options.ChannelMappings);
        _writer = new PerceptionEventWriter(options.PerceptionEventsFile);
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
            cycles,
            _idleCycles,
            _lastWhatsAppWindowsDetected,
            _lastChannelIds,
            _lastProcessedVisionEventId,
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
                    _lastWhatsAppWindowsDetected,
                    _lastChannelIds,
                    _options.VisionEventsFile,
                    _options.PerceptionEventsFile,
                    visionEvent.VisionEventId,
                    string.Empty);
                return await WriteStateAsync(idleState, cancellationToken).ConfigureAwait(false);
            }

            _visionEventsRead++;
            var candidates = _windowDetector.Detect(visionEvent.VisibleWindows, _options.MinimumWhatsAppConfidence);
            var channels = _channelResolver.Resolve(candidates);
            var perceptionEvent = CreatePerceptionEvent(visionEvent, channels);
            var errors = PerceptionContractValidator.Validate(perceptionEvent);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", errors));
            }

            await _writer.AppendAsync(perceptionEvent, cancellationToken).ConfigureAwait(false);
            _perceptionEventsWritten++;
            _lastProcessedVisionEventId = visionEvent.VisionEventId;
            _lastWhatsAppWindowsDetected = candidates.Count;
            _lastChannelIds = channels.Select(channel => channel.ChannelId).ToArray();
            _lastError = string.Empty;
            var state = new PerceptionHealthState(
                "ok",
                DateTimeOffset.UtcNow,
                _visionEventsRead,
                _perceptionEventsWritten,
                candidates.Count,
                channels.Select(channel => channel.ChannelId).ToArray(),
                _options.VisionEventsFile,
                _options.PerceptionEventsFile,
                visionEvent.VisionEventId,
                string.Empty);
            return await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return await WriteStateAsync(CreateErrorState(exception.Message, string.Empty, [], 0), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private PerceptionEvent CreatePerceptionEvent(VisionEventEnvelope visionEvent, IReadOnlyList<ResolvedChannel> channels)
    {
        var objects = new List<PerceptionObject>();
        foreach (var channel in channels)
        {
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
            channels.Count == 1 ? channels[0].ChannelId : null,
            objects);
    }

    private PerceptionHealthState CreateErrorState(string error, string sourceVisionEventId, IReadOnlyList<string> channelIds, int windowCount)
    {
        return new PerceptionHealthState(
            "error",
            DateTimeOffset.UtcNow,
            _visionEventsRead,
            _perceptionEventsWritten,
            windowCount,
            channelIds,
            _options.VisionEventsFile,
            _options.PerceptionEventsFile,
            sourceVisionEventId,
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
}
