using System.Globalization;
using System.Text;

namespace AriadGSM.Hands.Interaction;

public sealed class InteractionContext
{
    public string SourceInteractionEventId { get; init; } = "";

    public string LatestPerceptionEventId { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<InteractionTarget> Targets { get; init; } = [];

    public InteractionTarget? BestChatTarget(string? channelId, string? title)
    {
        var rows = Targets
            .Where(item =>
                item.Actionable
                && IsFreshTarget(item)
                && item.TargetType.Equals("chat_row", StringComparison.OrdinalIgnoreCase)
                && item.ClickX > 0
                && item.ClickY > 0
                && string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
        {
            return null;
        }

        if (IsNoisyTitle(title))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var normalizedTitle = Normalize(title);
            var exact = rows
                .Where(item => Normalize(item.Title).Equals(normalizedTitle, StringComparison.Ordinal))
                .OrderByDescending(item => item.UnreadCount)
                .ThenByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (exact is not null)
            {
                return exact;
            }

            var fuzzy = rows
                .Where(item =>
                    Normalize(item.Title).Contains(normalizedTitle, StringComparison.Ordinal)
                    || normalizedTitle.Contains(Normalize(item.Title), StringComparison.Ordinal))
                .OrderByDescending(item => item.UnreadCount)
                .ThenByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (fuzzy is not null)
            {
                return fuzzy;
            }
        }

        return null;
    }

    private bool IsFreshTarget(InteractionTarget target)
    {
        return string.IsNullOrWhiteSpace(LatestPerceptionEventId)
            || string.Equals(target.SourcePerceptionEventId, LatestPerceptionEventId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoisyTitle(string? value)
    {
        var normalized = Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Equals("whatsapp", StringComparison.Ordinal)
            || normalized.Equals("whatsapp business", StringComparison.Ordinal)
            || normalized.Contains("marcador", StringComparison.Ordinal)
            || normalized.Contains("leer en voz alta", StringComparison.Ordinal)
            || normalized.Contains("informacion del sitio", StringComparison.Ordinal)
            || normalized.Contains("paginas mas", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return string.Join(" ", builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
