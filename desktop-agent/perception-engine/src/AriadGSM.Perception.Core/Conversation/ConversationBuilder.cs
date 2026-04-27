using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Extraction;
using AriadGSM.Perception.Reader;

namespace AriadGSM.Perception.Conversation;

public sealed class ConversationBuilder
{
    private readonly PerceptionOptions _options;

    public ConversationBuilder(PerceptionOptions options)
    {
        _options = options;
    }

    public ConversationEvent Build(
        ResolvedChannel channel,
        ReaderCoreResult readerResult,
        IReadOnlyList<ExtractedMessage> messages,
        ConversationIdentity identity)
    {
        var unique = messages
            .GroupBy(message => message.MessageId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(message => new ConversationMessage(
                message.MessageId,
                message.Text,
                message.Direction,
                message.SenderName,
                message.SentAt,
                message.Confidence,
                message.Signals))
            .ToArray();

        return new ConversationEvent(
            "conversation_event",
            $"conversation-{readerResult.ChannelId}-{ShortHash(readerResult.Source + readerResult.Status + string.Join("|", unique.Select(item => item.MessageId)))}",
            identity.ConversationId,
            channel.ChannelId,
            DateTimeOffset.UtcNow,
            identity.Title,
            "live",
            unique,
            new ConversationTimeline(_options.HistoryLimitDays, false, null, "message_id_hash"),
            BuildQuality(identity));
    }

    private static ConversationQuality BuildQuality(ConversationIdentity identity)
    {
        var reasons = new List<string>();
        var normalizedTitle = NormalizeTitle(identity.Title);

        if (identity.Confidence < 0.6)
        {
            reasons.Add("low_identity_confidence");
        }

        if (identity.Source.Equals("window_title", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("window_title_only");
        }

        if (LooksLikeBrowserUiOrGenericTitle(normalizedTitle))
        {
            reasons.Add("browser_or_generic_title");
        }

        return new ConversationQuality(
            reasons.Count == 0,
            Math.Clamp(identity.Confidence, 0, 1),
            identity.Source,
            reasons);
    }

    private static bool LooksLikeBrowserUiOrGenericTitle(string normalizedTitle)
    {
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return true;
        }

        var blocked = new[]
        {
            "whatsapp",
            "whatsapp business",
            "paginas mas",
            "perfil 1",
            "anadir esta pagina a marcadores",
            "editar marcador",
            "editar favorito",
            "marcadores",
            "favorito",
            "leer en voz alta",
            "informacion del sitio",
            "ver informacion del sitio",
            "ctrl d",
            "google chrome",
            "microsoft edge",
            "mozilla firefox",
            "http",
            "drive google"
        };
        return blocked.Any(token =>
            normalizedTitle.Equals(token, StringComparison.Ordinal)
            || normalizedTitle.Contains(token, StringComparison.Ordinal));
    }

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string ShortHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..12];
    }
}
