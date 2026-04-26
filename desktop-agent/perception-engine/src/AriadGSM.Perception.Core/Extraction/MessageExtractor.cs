using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Reader;
using AriadGSM.Perception.Semantics;

namespace AriadGSM.Perception.Extraction;

public sealed partial class MessageExtractor
{
    private readonly PerceptionOptions _options;
    private readonly BusinessSemanticAnalyzer _semanticAnalyzer = new();

    public MessageExtractor(PerceptionOptions options)
    {
        _options = options;
    }

    public MessageExtractionResult Extract(ReaderCoreResult readerResult, ResolvedChannel channel)
    {
        var messages = new List<ExtractedMessage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var line in readerResult.Lines)
        {
            var cleaned = Clean(line.Text);
            if (!IsInsideConversationArea(line, channel))
            {
                Reject(reasons, "outside_conversation_area");
                continue;
            }
            if (!IsUsefulMessage(cleaned))
            {
                Reject(reasons, "not_message_text");
                continue;
            }

            var (direction, text) = DetectDirection(cleaned, line, channel);
            if (!IsUsefulMessage(text))
            {
                Reject(reasons, "not_message_text_after_direction");
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
                Reject(reasons, "duplicate");
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
                line.Source,
                _semanticAnalyzer.Analyze(text)));
            index++;
        }

        return new MessageExtractionResult(
            messages,
            new ExtractionDiagnostics(
                readerResult.Lines.Count,
                messages.Count,
                reasons.Values.Sum(),
                reasons));
    }

    private static (string Direction, string Text) DetectDirection(string text, ReaderTextLine line, ResolvedChannel channel)
    {
        var agentPrefixes = new[] { "Tu:", "T\u00C3\u00BA:", "You:", "Yo:" };
        foreach (var prefix in agentPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return ("agent", text[prefix.Length..].Trim());
            }
        }

        if (line.Bounds is not null)
        {
            var window = channel.Candidate.Window.Bounds;
            var bubbleCenter = line.Bounds.Left + (line.Bounds.Width / 2);
            var agentThreshold = window.Left + (int)(window.Width * 0.68);
            var clientThreshold = window.Left + (int)(window.Width * 0.58);
            if (bubbleCenter >= agentThreshold)
            {
                return ("agent", text);
            }
            if (bubbleCenter <= clientThreshold)
            {
                return ("client", text);
            }
        }

        return ("unknown", text);
    }

    private bool IsUsefulMessage(string text)
    {
        if (text.Length < Math.Max(1, _options.MinimumUsefulTextLength))
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
            "micr\u00C3\u00B3fono",
            "microphone",
            "notificaciones",
            "notifications",
            "perfil",
            "profile",
            "archivados",
            "archived",
            "copiar ruta",
            "copy link",
            "seleccionar",
            "select messages",
            "mensajes no leidos",
            "unread messages",
            "chat fijado",
            "pinned chat"
        };
        return tokens.Any(lowered.Contains);
    }

    private static string Clean(string text)
    {
        var clean = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean
            .Replace("\u00E2\u20AC\u201C", "-", StringComparison.Ordinal)
            .Replace("\u00E2\u20AC\u201D", "-", StringComparison.Ordinal)
            .Replace("T\u00C3\u00BA:", "Tu:", StringComparison.OrdinalIgnoreCase);
        clean = clean.Trim('-', '|', ':', ';', ',', '.', ' ');
        return clean;
    }

    private static void Reject(IDictionary<string, int> reasons, string reason)
    {
        reasons.TryGetValue(reason, out var count);
        reasons[reason] = count + 1;
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

    [GeneratedRegex(@"^(whatsapp|whatsapp business|buscar|search|chats|estados|status|calls|llamadas|comunidades|communities|nuevo chat|new chat|escribe un mensaje|type a message|mensaje|message|foto|photo|audio|chat fijado|pinned chat|cifrado|encrypted|hoy|ayer|today|yesterday|google chrome|microsoft edge|mozilla firefox|codex|telegram|copiar ruta|copy link)$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();
}
