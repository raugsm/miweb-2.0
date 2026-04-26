using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Reader;
using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.ChatRows;

public sealed partial class ChatRowExtractor
{
    public ChatRowExtractionResult Extract(ReaderCoreResult readerResult, ResolvedChannel channel)
    {
        var window = channel.Candidate.Window.Bounds;
        var chatListRight = window.Left + (int)(window.Width * 0.31);
        var minimumTop = window.Top + Math.Max(84, (int)(window.Height * 0.07));
        var maximumBottom = window.Top + window.Height - Math.Max(64, (int)(window.Height * 0.05));
        var candidates = new List<RowLine>();
        var rejected = 0;

        foreach (var line in readerResult.Lines)
        {
            var text = Clean(line.Text);
            if (line.Bounds is null || string.IsNullOrWhiteSpace(text))
            {
                rejected++;
                continue;
            }

            var bounds = line.Bounds;
            var centerX = bounds.Left + (bounds.Width / 2);
            var bottom = bounds.Top + bounds.Height;
            if (bounds.Top < minimumTop
                || bottom > maximumBottom
                || centerX > chatListRight
                || bounds.Left < window.Left
                || bounds.Width < 10
                || bounds.Height < 8)
            {
                rejected++;
                continue;
            }

            if (LooksLikeNavigation(text))
            {
                rejected++;
                continue;
            }

            candidates.Add(new RowLine(text, bounds, line.Confidence));
        }

        var groups = new List<List<RowLine>>();
        foreach (var line in candidates.OrderBy(item => item.Bounds.Top))
        {
            var centerY = line.Bounds.Top + (line.Bounds.Height / 2);
            var group = groups.FirstOrDefault(existing =>
            {
                var existingCenter = existing.Average(item => item.Bounds.Top + (item.Bounds.Height / 2.0));
                return Math.Abs(existingCenter - centerY) <= 42;
            });
            if (group is null)
            {
                group = [];
                groups.Add(group);
            }

            group.Add(line);
        }

        var rows = groups
            .Select(group => BuildRow(group, channel, readerResult.Confidence, window, chatListRight))
            .Where(row => row is not null)
            .Select(row => row!)
            .OrderBy(row => row.Bounds.Top)
            .Take(30)
            .ToArray();

        return new ChatRowExtractionResult(rows, candidates.Count, rejected);
    }

    private static ChatRow? BuildRow(
        IReadOnlyList<RowLine> group,
        ResolvedChannel channel,
        double readerConfidence,
        VisionBounds window,
        int chatListRight)
    {
        var lines = group
            .OrderBy(item => item.Bounds.Top)
            .ThenBy(item => item.Bounds.Left)
            .Select(item => item.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var useful = lines
            .Where(line => !IsTime(line) && !IsUnreadOnly(line))
            .Where(line => !string.Equals(line, "Sticker", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (useful.Length == 0)
        {
            return null;
        }

        var (title, preview) = DeriveTitleAndPreview(useful);
        if (title.Length < 2)
        {
            return null;
        }

        var unread = lines
            .Select(ParseUnreadCount)
            .FirstOrDefault(count => count > 0);
        var bounds = UnionBounds(group.Select(item => item.Bounds));
        var rowHeight = Math.Max(bounds.Height, 54);
        var rowTop = Math.Max(window.Top, bounds.Top - Math.Max(0, (rowHeight - bounds.Height) / 2));
        var rowBounds = new VisionBounds(
            window.Left,
            rowTop,
            Math.Max(64, chatListRight - window.Left),
            rowHeight);
        var clickX = window.Left + Math.Min(96, Math.Max(48, rowBounds.Width / 2));
        var clickY = rowBounds.Top + (rowBounds.Height / 2);
        var confidence = Math.Clamp(group.Average(item => item.Confidence) * readerConfidence, 0.35, 0.98);
        return new ChatRow(
            CreateRowId(channel.ChannelId, title, preview, rowBounds.Top),
            channel.ChannelId,
            title,
            preview,
            unread,
            rowBounds,
            clickX,
            clickY,
            confidence,
            lines);
    }

    private static VisionBounds UnionBounds(IEnumerable<VisionBounds> bounds)
    {
        var items = bounds.ToArray();
        var left = items.Min(item => item.Left);
        var top = items.Min(item => item.Top);
        var right = items.Max(item => item.Left + item.Width);
        var bottom = items.Max(item => item.Top + item.Height);
        return new VisionBounds(left, top, right - left, bottom - top);
    }

    private static string CreateRowId(string channelId, string title, string preview, int top)
    {
        var raw = Encoding.UTF8.GetBytes($"{channelId}|{title}|{preview}|{top}");
        var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()[..16];
        return $"chatrow-{channelId}-{hash}";
    }

    private static string Clean(string text)
    {
        return string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static bool LooksLikeNavigation(string text)
    {
        var lowered = text.ToLowerInvariant();
        if (FilterOnlyRegex().IsMatch(text))
        {
            return true;
        }

        var tokens = new[]
        {
            "whatsapp",
            "wordmark",
            "whatsapp business",
            "buscar",
            "search",
            "chats",
            "canales",
            "channels",
            "filters",
            "filtros",
            "estados",
            "status",
            "llamadas",
            "calls",
            "comunidades",
            "communities",
            "herramientas",
            "tools",
            "archivados",
            "archived"
        };
        return tokens.Any(lowered.Contains);
    }

    private static bool IsTime(string text)
    {
        return TimeRegex().IsMatch(text);
    }

    private static bool IsUnreadOnly(string text)
    {
        return UnreadRegex().IsMatch(text);
    }

    private static (string Title, string Preview) DeriveTitleAndPreview(IReadOnlyList<string> useful)
    {
        var first = StripUnreadPrefix(useful[0]);
        var match = ChatCompositeRegex().Match(first);
        if (useful.Count > 1)
        {
            var derivedTitle = match.Success ? match.Groups["title"].Value.Trim() : first;
            return (string.IsNullOrWhiteSpace(derivedTitle) ? first : derivedTitle, StripUnreadPrefix(useful[^1]));
        }

        if (!match.Success)
        {
            return (first, string.Empty);
        }

        var title = match.Groups["title"].Value.Trim();
        var preview = match.Groups["preview"].Value.Trim();
        return (string.IsNullOrWhiteSpace(title) ? first : title, preview);
    }

    private static string StripUnreadPrefix(string value)
    {
        return UnreadPrefixRegex().Replace(value, string.Empty).Trim();
    }

    private static int ParseUnreadCount(string text)
    {
        var match = UnreadRegex().Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : 0;
    }

    private sealed record RowLine(string Text, VisionBounds Bounds, double Confidence);

    [GeneratedRegex(@"^\d{1,2}:\d{2}(\s?(a\.?\s?m\.?|p\.?\s?m\.?|AM|PM))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^(\d+)\s+mensajes?\s+no\s+le[ií]dos?$", RegexOptions.IgnoreCase)]
    private static partial Regex UnreadRegex();

    [GeneratedRegex(@"^(todos|all|favoritos|favorites|grupos|groups|no le[ií]dos|unread)$", RegexOptions.IgnoreCase)]
    private static partial Regex FilterOnlyRegex();

    [GeneratedRegex(@"^\d+\s+mensajes?\s+no\s+le[ií]dos?\s+", RegexOptions.IgnoreCase)]
    private static partial Regex UnreadPrefixRegex();

    [GeneratedRegex(@"^(?<title>.+?)\s+(?<stamp>Ayer|Hoy|\d{1,2}:\d{2}\s*(?:a\.?\s*m\.?|p\.?\s*m\.?|AM|PM)?)\s+(?<preview>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChatCompositeRegex();
}
