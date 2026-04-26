using AriadGSM.Vision.Buffer;
using AriadGSM.Vision.Capture;
using AriadGSM.Vision.Config;
using AriadGSM.Vision.Events;
using AriadGSM.Vision.Health;

namespace AriadGSM.Vision.Pipeline;

public sealed class VisionPipeline
{
    private readonly VisionOptions _options;
    private readonly IScreenCapture _capture;
    private readonly IVisionBuffer _buffer;
    private readonly VisionEventWriter _writer;

    public VisionPipeline(VisionOptions options)
    {
        _options = options;
        _capture = new SyntheticScreenCapture();
        _buffer = new FileVisionBuffer(options.StorageRoot, options.ToRetentionPolicy());
        _writer = new VisionEventWriter(options.EventsFile);
    }

    public async ValueTask<VisionHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var frame = await _capture.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var saved = await _buffer.SaveAsync(frame, cancellationToken).ConfigureAwait(false);
        var visionEvent = VisionEventFactory.Create(frame, saved, _options);
        var errors = ContractValidator.Validate(visionEvent);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", errors));
        }

        await _writer.AppendAsync(visionEvent, cancellationToken).ConfigureAwait(false);
        var deleted = _buffer.Cleanup(DateTimeOffset.UtcNow);
        return new VisionHealthState("ok", DateTimeOffset.UtcNow, 1, 1, deleted, _options.StorageRoot, string.Empty);
    }
}

