using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.ChatRows;
using AriadGSM.Perception.Reader;
using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Conversation;

public sealed partial class ConversationIdentityResolver
{
    public ConversationIdentity Resolve(
        ResolvedChannel channel,
        ReaderCoreResult readerResult,
        ChatRowExtractionResult chatRows)
    {
        var window = channel.Candidate.Window.Bounds;
        var headerCandidates = HeaderCandidates(readerResult.Lines, window).ToArray();
        var matched = MatchHeaderToChatRow(headerCandidates, chatRows.Rows);
        if (matched is not null)
        {
            var title = CleanTitle(matched.Title);
            return new ConversationIdentity(
                CreateConversationId(channel.ChannelId, title),
                title,
                "header_chat_row_match",
                Math.Clamp(matched.Confidence, 0.65, 0.98),
                matched);
        }

        var headerTitle = headerCandidates.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerTitle))
        {
            var title = CleanTitle(headerTitle);
            return new ConversationIdentity(
                CreateConversationId(channel.ChannelId, title),
                title,
                "header_text",
                0.68,
                null);
        }

        var fallback = CleanBrowserTitle(channel.Candidate.Window.Title);
        return new ConversationIdentity(
            CreateConversationId(channel.ChannelId, fallback),
            fallback,
            "window_title",
            0.35,
            null);
    }

    public static string CreateConversationId(string channelId, string title)
    {
        return $"{channelId}-{ShortHash(title)}";
    }

    public static string CleanBrowserTitle(string title)
    {
        var clean = title
            .Replace(" - Google Chrome", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Microsoft Edge", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" - Mozilla Firefox", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" \u2014 Mozilla Firefox", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" \u2013 Mozilla Firefox", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" \u00E2\u20AC\u201D Mozilla Firefox", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return string.IsNullOrWhiteSpace(clean) ? "WhatsApp" : CleanTitle(clean);
    }

    private static IEnumerable<string> HeaderCandidates(IReadOnlyList<ReaderTextLine> lines, VisionBounds window)
    {
        var rightPaneLeft = window.Left + (int)(window.Width * 0.31);
        var headerTop = window.Top + Math.Max(42, (int)(window.Height * 0.03));
        var headerBottom = window.Top + Math.Max(118, (int)(window.Height * 0.14));

        return lines
            .Where(line => line.Bounds is not null)
            .Where(line =>
            {
                var bounds = line.Bounds!;
                var centerX = bounds.Left + (bounds.Width / 2);
                var centerY = bounds.Top + (bounds.Height / 2);
                return centerX >= rightPaneLeft
                    && centerY >= headerTop
                    && centerY <= headerBottom
                    && bounds.Width >= 16
                    && bounds.Height >= 8;
            })
            .Select(line => CleanTitle(line.Text))
            .Where(IsUsefulTitle)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(title => title.Length);
    }

    private static ChatRow? MatchHeaderToChatRow(IReadOnlyList<string> headerCandidates, IReadOnlyList<ChatRow> rows)
    {
        if (headerCandidates.Count == 0 || rows.Count == 0)
        {
            return null;
        }

        foreach (var candidate in headerCandidates)
        {
            var normalizedCandidate = NormalizeForMatch(candidate);
            var exact = rows
                .Where(row => NormalizeForMatch(row.Title).Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(row => row.Confidence)
                .FirstOrDefault();
            if (exact is not null)
            {
                return exact;
            }

            var fuzzy = rows
                .Where(row =>
                {
                    var normalizedRow = NormalizeForMatch(row.Title);
                    return normalizedRow.Length >= 3
                        && (normalizedCandidate.Contains(normalizedRow, StringComparison.OrdinalIgnoreCase)
                            || normalizedRow.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(row => row.Confidence)
                .FirstOrDefault();
            if (fuzzy is not null)
            {
                return fuzzy;
            }
        }

        return null;
    }

    private static bool IsUsefulTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2 || text.Length > 90)
        {
            return false;
        }

        if (TimeRegex().IsMatch(text) || DigitsOnlyRegex().IsMatch(text))
        {
            return false;
        }

        var normalized = NormalizeForMatch(text);
        var blocked = new[]
        {
            "whatsapp",
            "whatsapp business",
            "buscar",
            "search",
            "chats",
            "nuevo chat",
            "new chat",
            "filtros",
            "filters",
            "todos",
            "all",
            "favoritos",
            "favorites",
            "archivados",
            "archived",
            "en linea",
            "online",
            "escribiendo",
            "typing",
            "ultima vez",
            "last seen",
            "haz clic para ver info",
            "click here for contact info",
            "codex",
            "google chrome",
            "microsoft edge",
            "mozilla firefox"
        };
        return !blocked.Any(token => normalized.Equals(token, StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanTitle(string text)
    {
        var clean = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean
            .Replace("T\u00C3\u00BA:", "Tu:", StringComparison.OrdinalIgnoreCase)
            .Replace("\u2014", "-", StringComparison.Ordinal)
            .Replace("\u2013", "-", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u201D", "-", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u201C", "-", StringComparison.Ordinal)
            .Trim('-', '|', ':', ';', ',', '.', ' ');
        return string.IsNullOrWhiteSpace(clean) ? "WhatsApp" : clean;
    }

    private static string NormalizeForMatch(string value)
    {
        var normalized = (value ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string ShortHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..12];
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}(\s?(a\.?\s?m\.?|p\.?\s?m\.?|AM|PM))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnlyRegex();
}
