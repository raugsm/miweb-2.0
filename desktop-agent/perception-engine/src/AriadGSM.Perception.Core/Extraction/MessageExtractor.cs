using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Reader;

namespace AriadGSM.Perception.Extraction;

public sealed partial class MessageExtractor
{
    private readonly PerceptionOptions _options;

    public MessageExtractor(PerceptionOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<ExtractedMessage> Extract(ReaderCoreResult readerResult, ResolvedChannel channel)
    {
        var messages = new List<ExtractedMessage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var line in readerResult.Lines)
        {
            var cleaned = Clean(line.Text);
            if (!IsInsideConversationArea(line, channel) || !IsUsefulMessage(cleaned))
            {
                continue;
            }

            var (direction, text) = DetectDirection(cleaned);
            if (!IsUsefulMessage(text))
            {
                continue;
            }

            var confidence = Math.Clamp(line.Confidence * readerResult.Confidence, 0, 1);
            if (confidence < _options.MinimumMessageConfidence)
            {
                confidence = _options.MinimumMessageConfidence;
            }

            var dedupeKey = $"{channel.ChannelId}|{direction}|{text}";
            if (!seen.Add(dedupeKey))
            {
                continue;
            }

            messages.Add(new ExtractedMessage(
                CreateMessageId(channel.ChannelId, text, index),
                text,
                direction,
                null,
                null,
                confidence,
                line.Bounds,
                line.Source));
            index++;
        }

        return messages;
    }

    private static (string Direction, string Text) DetectDirection(string text)
    {
        var agentPrefixes = new[] { "Tu:", "Tú:", "You:", "Yo:" };
        foreach (var prefix in agentPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return ("agent", text[prefix.Length..].Trim());
            }
        }

        return ("unknown", text);
    }

    private static bool IsUsefulMessage(string text)
    {
        if (text.Length < 3)
        {
            return false;
        }
        if (TimeOnlyRegex().IsMatch(text) || CounterOnlyRegex().IsMatch(text))
        {
            return false;
        }
        if (NoiseRegex().IsMatch(text))
        {
            return false;
        }
        if (LooksLikeUiText(text))
        {
            return false;
        }

        return text.Any(char.IsLetterOrDigit);
    }

    private static bool IsInsideConversationArea(ReaderTextLine line, ResolvedChannel channel)
    {
        if (line.Bounds is null)
        {
            return true;
        }

        var window = channel.Candidate.Window.Bounds;
        var minimumTop = window.Top + Math.Max(96, (int)(window.Height * 0.08));
        var minimumLeft = window.Left + (int)(window.Width * 0.28);
        var maximumBottom = window.Top + window.Height;
        var bottom = line.Bounds.Top + line.Bounds.Height;
        return line.Bounds.Top >= minimumTop
            && bottom <= maximumBottom
            && line.Bounds.Left >= minimumLeft
            && line.Bounds.Width >= 12
            && line.Bounds.Height >= 8;
    }

    private static bool LooksLikeUiText(string text)
    {
        var lowered = text.ToLowerInvariant();
        var tokens = new[]
        {
            "whatsapp business",
            "google chrome",
            "microsoft edge",
            "mozilla firefox",
            "codex",
            "telegram",
            "buscar",
            "search",
            "adjuntar",
            "attach",
            "emoji",
            "micrófono",
            "microphone",
            "notificaciones",
            "notifications",
            "perfil",
            "profile",
            "archivados",
            "archived"
        };
        return tokens.Any(lowered.Contains);
    }

    private static string Clean(string text)
    {
        var clean = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean.Trim('-', '–', '—', '|', ':', ';', ',', '.', ' ');
        return clean;
    }

    private static string CreateMessageId(string channelId, string text, int index)
    {
        var raw = Encoding.UTF8.GetBytes($"{channelId}|{index}|{text}");
        var hash = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()[..16];
        return $"msg-{channelId}-{hash}";
    }

    [GeneratedRegex(@"^\d{1,2}:\d{2}(\s?(a\.?\s?m\.?|p\.?\s?m\.?|AM|PM))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimeOnlyRegex();

    [GeneratedRegex(@"^\(?\d+\)?$")]
    private static partial Regex CounterOnlyRegex();

    [GeneratedRegex(@"^(whatsapp|whatsapp business|buscar|search|chats|estados|status|calls|llamadas|comunidades|communities|nuevo chat|new chat|escribe un mensaje|type a message|mensaje|message|foto|photo|audio|chat fijado|pinned chat|cifrado|encrypted|hoy|ayer|today|yesterday|google chrome|microsoft edge|mozilla firefox|codex|telegram)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();
}
