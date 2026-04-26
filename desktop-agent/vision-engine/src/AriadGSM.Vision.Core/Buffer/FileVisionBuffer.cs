using AriadGSM.Vision.Capture;

namespace AriadGSM.Vision.Buffer;

public sealed class FileVisionBuffer : IVisionBuffer
{
    private readonly string _root;
    private readonly RetentionPolicy _policy;

    public FileVisionBuffer(string root, RetentionPolicy policy)
    {
        _root = string.IsNullOrWhiteSpace(root) ? VisionDefaults.StorageRoot : root;
        _policy = policy;
    }

    public async ValueTask<SavedFrame> SaveAsync(ScreenFrame frame, CancellationToken cancellationToken = default)
    {
        var dayDir = Path.Combine(_root, "frames", frame.CapturedAt.ToString("yyyyMMdd"));
        Directory.CreateDirectory(dayDir);
        var path = Path.Combine(dayDir, $"{frame.FrameId}{GetExtension(frame)}");
        await File.WriteAllBytesAsync(path, frame.Data, cancellationToken).ConfigureAwait(false);
        return new SavedFrame(frame.FrameId, path, frame.CapturedAt, frame.Hash, true);
    }

    public int Cleanup(DateTimeOffset now)
    {
        if (!Directory.Exists(_root))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (_policy.ShouldDelete(new DateTimeOffset(lastWrite, TimeSpan.Zero), now))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch
            {
                // Cleanup must never stop the live eye.
            }
        }
        return deleted;
    }

    private static string GetExtension(ScreenFrame frame)
    {
        return frame.Source is "gdi" or "screen_capture" or "windows_graphics" or "dxgi"
            ? ".bmp"
            : ".bin";
    }
}
