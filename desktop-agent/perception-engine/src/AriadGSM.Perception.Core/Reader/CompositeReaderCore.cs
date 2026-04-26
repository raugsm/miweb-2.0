using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;

namespace AriadGSM.Perception.Reader;

public sealed class CompositeReaderCore : IReaderCore
{
    private readonly IReaderCore _primary;
    private readonly IReaderCore _fallback;
    private readonly PerceptionOptions _options;

    public CompositeReaderCore(IReaderCore primary, IReaderCore fallback, PerceptionOptions options)
    {
        _primary = primary;
        _fallback = fallback;
        _options = options;
    }

    public async ValueTask<ReaderCoreResult> ReadAsync(
        ResolvedChannel channel,
        ReaderContext context,
        CancellationToken cancellationToken = default)
    {
        var primary = await _primary.ReadAsync(channel, context, cancellationToken).ConfigureAwait(false);
        if (!_options.EnableOcrFallback || IsGoodRead(primary))
        {
            return primary;
        }

        var fallback = await _fallback.ReadAsync(channel, context, cancellationToken).ConfigureAwait(false);
        if (fallback.Lines.Count == 0)
        {
            var error = string.Join(" | ", new[] { primary.Error, $"ocr:{fallback.Status}:{fallback.Error}" }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return primary with
            {
                Status = primary.Lines.Count > 0 ? primary.Status : $"fallback_unavailable:{fallback.Status}",
                Error = error
            };
        }

        var lines = primary.Lines
            .Concat(fallback.Lines)
            .GroupBy(line => $"{line.Source}|{line.Text}|{line.Bounds?.Left}|{line.Bounds?.Top}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new ReaderCoreResult(
            channel.ChannelId,
            $"{primary.Source}+{fallback.Source}",
            primary.Lines.Count > 0 ? "ok_with_ocr_fallback" : "ok_from_ocr_fallback",
            lines,
            Math.Clamp(Math.Max(primary.Confidence, fallback.Confidence), 0, 1),
            string.Empty);
    }

    private bool IsGoodRead(ReaderCoreResult result)
    {
        return result.Status.StartsWith("ok", StringComparison.OrdinalIgnoreCase)
            && result.Lines.Count >= Math.Max(1, _options.MinimumReaderLinesForGoodRead);
    }
}
