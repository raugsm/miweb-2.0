using System.Security.Cryptography;
using System.Text;

namespace AriadGSM.Vision.Capture;

public sealed class SyntheticScreenCapture : IScreenCapture
{
    public ValueTask<ScreenFrame> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var frameId = $"synthetic-{capturedAt:yyyyMMddHHmmssfff}";
        var data = Encoding.UTF8.GetBytes($"AriadGSM synthetic vision frame {capturedAt:O}");
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        return ValueTask.FromResult(new ScreenFrame(frameId, capturedAt, 1, 1, data, hash, "synthetic"));
    }
}

