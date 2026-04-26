using AriadGSM.Vision.Buffer;
using AriadGSM.Vision.Capture;
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
        var frame = await _capture.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var windows = _windowEnumerator.GetVisibleWindows();
        var saved = await _buffer.SaveAsync(frame, cancellationToken).ConfigureAwait(false);
        var visionEvent = VisionEventFactory.Create(frame, saved, _options);
        var errors = ContractValidator.Validate(visionEvent);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }

        await _writer.AppendAsync(visionEvent, cancellationToken).ConfigureAwait(false);
        var deleted = _buffer.Cleanup(DateTimeOffset.UtcNow);
        var state = new VisionHealthState(
            "ok",
            DateTimeOffset.UtcNow,
            1,
            1,
            deleted,
            _options.StorageRoot,
            _options.EventsFile,
            _options.StateFile,
            _options.CaptureMode,
            frame.Width,
            frame.Height,
            saved.Path,
            windows.Count,
            windows.Select(window => new VisibleWindowState(window.ProcessId, window.ProcessName, window.Title, window.Bounds)).ToArray(),
            string.Empty);
        await WriteStateAsync(state, cancellationToken).ConfigureAwait(false);
        return state;
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
