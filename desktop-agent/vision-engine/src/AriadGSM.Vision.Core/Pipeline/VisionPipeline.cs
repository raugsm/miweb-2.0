using AriadGSM.Vision.Buffer;
using AriadGSM.Vision.Capture;
using AriadGSM.Vision.ChangeDetection;
using AriadGSM.Vision.Config;
using AriadGSM.Vision.Events;
using AriadGSM.Vision.Health;
using AriadGSM.Vision.Windows;
using System.Text.Json;

namespace AriadGSM.Vision.Pipeline;

public sealed class VisionPipeline
{
    private readonly VisionOptions _options;
    private readonly IScreenCapture _capture;
    private readonly IVisionBuffer _buffer;
    private readonly VisionEventWriter _writer;
    private readonly IWindowEnumerator _windowEnumerator;
    private readonly FrameDiffer _frameDiffer = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private ScreenFrame? _previousFrame;
    private DateTimeOffset _lastEventAt = DateTimeOffset.MinValue;
    private string _lastFramePath = string.Empty;
    private int _framesCaptured;
    private int _eventsWritten;
    private int _framesSkipped;
    private int _cleanupDeleted;

    public VisionPipeline(VisionOptions options)
    {
        _options = options;
        _capture = CreateCapture(options.CaptureMode);
        _buffer = new FileVisionBuffer(options.StorageRoot, options.ToRetentionPolicy());
        _writer = new VisionEventWriter(options.EventsFile);
        _windowEnumerator = new Win32WindowEnumerator();
    }

    public async ValueTask<VisionHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        return await RunCycleAsync(forceEvent: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<VisionRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        VisionHealthState? lastState = null;
        var cycles = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunCycleAsync(forceEvent: cycles == 1, cancellationToken).ConfigureAwait(false);
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
                await Task.Delay(Math.Max(20, _options.CaptureIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new VisionRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            _framesCaptured,
            _eventsWritten,
            _framesSkipped,
            _cleanupDeleted,
            _lastFramePath,
            lastState?.LastChangeScore ?? 0,
            lastState?.VisibleWindowCount ?? 0,
            lastState?.LastError ?? string.Empty);
    }

    private async ValueTask<VisionHealthState> RunCycleAsync(bool forceEvent, CancellationToken cancellationToken)
    {
        try
        {
            var frame = await _capture.CaptureAsync(cancellationToken).ConfigureAwait(false);
            var windows = _windowEnumerator.GetVisibleWindows();
            var changeScore = _frameDiffer.Compare(_previousFrame, frame);
            var changed = forceEvent || changeScore >= _options.ChangeThreshold;
            var canWriteEvent = DateTimeOffset.UtcNow - _lastEventAt >= TimeSpan.FromMilliseconds(Math.Max(0, _options.MinEventIntervalMs));
            var writeEvent = changed && canWriteEvent;
            var cleanupDeleted = 0;

            if (writeEvent)
            {
                var saved = await _buffer.SaveAsync(frame, cancellationToken).ConfigureAwait(false);
                var changes = new[]
                {
                    new ChangedRegion("screen", changeScore, 0, 0, frame.Width, frame.Height)
                };
                var visionEvent = VisionEventFactory.Create(frame, saved, _options, changes);
                var errors = ContractValidator.Validate(visionEvent);
                if (errors.Count > 0)
                {
                    throw new InvalidOperationException(string.Join("; ", errors));
                }

                await _writer.AppendAsync(visionEvent, cancellationToken).ConfigureAwait(false);
                _eventsWritten++;
                _lastEventAt = DateTimeOffset.UtcNow;
                _lastFramePath = saved.Path;
            }
            else
            {
                _framesSkipped++;
            }

            cleanupDeleted = _buffer.Cleanup(DateTimeOffset.UtcNow);
            _cleanupDeleted += cleanupDeleted;
            _framesCaptured++;
            _previousFrame = frame;
            var state = new VisionHealthState(
                "ok",
                DateTimeOffset.UtcNow,
                _startedAt,
                _framesCaptured,
                _eventsWritten,
                _framesSkipped,
                _cleanupDeleted,
                _options.StorageRoot,
                _options.EventsFile,
                _options.StateFile,
                _options.CaptureMode,
                frame.Width,
                frame.Height,
                _lastFramePath,
                changed,
                changeScore,
                _options.ChangeThreshold,
                _options.CaptureIntervalMs,
                _options.MinEventIntervalMs,
                windows.Count,
                windows.Select(window => new VisibleWindowState(window.ProcessId, window.ProcessName, window.Title, window.Bounds)).ToArray(),
                string.Empty);
            await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
            return state;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var windows = GetVisibleWindowsSafely();
            var state = new VisionHealthState(
                "error",
                DateTimeOffset.UtcNow,
                _startedAt,
                _framesCaptured,
                _eventsWritten,
                _framesSkipped,
                _cleanupDeleted,
                _options.StorageRoot,
                _options.EventsFile,
                _options.StateFile,
                _options.CaptureMode,
                0,
                0,
                _lastFramePath,
                false,
                0,
                _options.ChangeThreshold,
                _options.CaptureIntervalMs,
                _options.MinEventIntervalMs,
                windows.Count,
                windows.Select(window => new VisibleWindowState(window.ProcessId, window.ProcessName, window.Title, window.Bounds)).ToArray(),
                exception.Message);
            await WriteStateAsync(state, CancellationToken.None).ConfigureAwait(false);
            return state;
        }
    }

    private IReadOnlyList<WindowSnapshot> GetVisibleWindowsSafely()
    {
        try
        {
            return _windowEnumerator.GetVisibleWindows();
        }
        catch
        {
            return Array.Empty<WindowSnapshot>();
        }
    }

    private static IScreenCapture CreateCapture(string captureMode)
    {
        return captureMode.Trim().ToLowerInvariant() switch
        {
            "synthetic" => new SyntheticScreenCapture(),
            "gdi" or "screen" or "screen_capture" => new GdiFallbackCapture(),
            "windows_graphics" => new WindowsGraphicsCapture(),
            "dxgi" => new DxgiScreenCapture(),
            _ => new GdiFallbackCapture()
        };
    }

    private async ValueTask WriteStateAsync(VisionHealthState state, CancellationToken cancellationToken)
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
    }
}
