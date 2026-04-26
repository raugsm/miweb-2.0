using System.Diagnostics;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Reader;

public sealed class OcrFallbackReaderCore : IReaderCore
{
    private readonly PerceptionOptions _options;

    public OcrFallbackReaderCore(PerceptionOptions options)
    {
        _options = options;
    }

    public async ValueTask<ReaderCoreResult> ReadAsync(
        ResolvedChannel channel,
        ReaderContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OcrCommand))
        {
            return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "not_configured", "No OCR command configured.");
        }

        var framePath = context.VisionEvent?.Frame.Path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(framePath) || !File.Exists(framePath))
        {
            return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "frame_missing", framePath);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Math.Max(500, _options.OcrTimeoutMs));
            var bounds = channel.Candidate.Window.Bounds;
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.OcrCommand,
                Arguments = BuildArguments(framePath, bounds),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "start_failed", "OCR process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "error", stderr.Trim());
            }

            var lines = ParseLines(stdout);
            return new ReaderCoreResult(
                channel.ChannelId,
                "ocr",
                lines.Count > 0 ? "ok" : "empty",
                lines,
                lines.Count > 0 ? 0.62 : 0,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "timeout", $"OCR exceeded {_options.OcrTimeoutMs}ms.");
        }
        catch (Exception exception)
        {
            return ReaderCoreResult.Empty(channel.ChannelId, "ocr", "error", exception.Message);
        }
    }

    private string BuildArguments(string framePath, VisionBounds bounds)
    {
        return _options.OcrArguments
            .Replace("{image}", Quote(framePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{left}", bounds.Left.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{top}", bounds.Top.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", bounds.Width.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", bounds.Height.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ReaderTextLine> ParseLines(string stdout)
    {
        var result = new List<ReaderTextLine>();
        foreach (var rawLine in stdout.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length >= 6
                && int.TryParse(parts[^5], out var left)
                && int.TryParse(parts[^4], out var top)
                && int.TryParse(parts[^3], out var width)
                && int.TryParse(parts[^2], out var height)
                && double.TryParse(parts[^1], out var confidence))
            {
                result.Add(new ReaderTextLine(
                    string.Join("|", parts[..^5]).Trim(),
                    "Text",
                    new VisionBounds(left, top, width, height),
                    Math.Clamp(confidence, 0, 1),
                    "ocr"));
                continue;
            }

            result.Add(new ReaderTextLine(line, "Text", null, 0.58, "ocr"));
        }

        return result;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}
