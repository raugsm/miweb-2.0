using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AriadGSM.Interaction.Config;
using AriadGSM.Interaction.Events;
using AriadGSM.Interaction.Perception;

namespace AriadGSM.Interaction.Pipeline;

public sealed class InteractionPipeline
{
    private readonly InteractionOptions _options;
    private readonly PerceptionEventReader _reader;
    private readonly InteractionEventWriter _writer;
    private int _idleCycles;
    private int _perceptionEventsRead;
    private int _interactionEventsWritten;
    private int _targetsObserved;
    private int _targetsAccepted;
    private int _targetsRejected;
    private int _actionableTargets;
    private string _latestPerceptionEventId = string.Empty;
    private string _lastAcceptedTargetTitle = string.Empty;
    private string _lastRejectionReason = string.Empty;
    private string _lastSummary = string.Empty;
    private string _lastError = string.Empty;

    public InteractionPipeline(InteractionOptions options)
    {
        _options = options;
        _reader = new PerceptionEventReader(options.PerceptionEventsFile, options.PerceptionLimit);
        _writer = new InteractionEventWriter(options.InteractionEventsFile);
    }

    public async ValueTask<InteractionHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _reader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            _perceptionEventsRead = snapshot.EventsRead;
            _latestPerceptionEventId = snapshot.LatestPerceptionEventId;

            if (snapshot.EventsRead == 0)
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", "No perception events available.", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            var targets = BuildTargets(snapshot);
            var summary = Summarize(targets);
            _targetsObserved = summary.TargetsObserved;
            _targetsAccepted = summary.TargetsAccepted;
            _targetsRejected = summary.TargetsRejected;
            _actionableTargets = summary.ActionableTargets;
            _lastAcceptedTargetTitle = summary.BestTargetTitle;
            _lastRejectionReason = summary.LastRejectionReason;
            _lastSummary = $"targets={summary.TargetsObserved}, actionable={summary.ActionableTargets}, accepted={summary.TargetsAccepted}, rejected={summary.TargetsRejected}";

            if (targets.Count == 0)
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", "Perception had no usable interaction targets.", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            var interactionEvent = new InteractionEvent(
                "interaction_event",
                StableInteractionEventId(snapshot, targets),
                DateTimeOffset.UtcNow,
                "ariadgsm_interaction_engine",
                snapshot.LatestPerceptionEventId,
                snapshot.EventsRead,
                targets,
                summary);

            var existingIds = await _writer.ReadExistingEventIdsAsync(cancellationToken).ConfigureAwait(false);
            if (existingIds.Contains(interactionEvent.InteractionEventId))
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", $"Interaction already emitted: {_lastSummary}", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            await _writer.AppendAsync(interactionEvent, cancellationToken).ConfigureAwait(false);
            _interactionEventsWritten++;
            return await WriteStateAsync(CreateState("ok", $"Interaction emitted: {_lastSummary}", string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return await WriteStateAsync(CreateState("error", _lastSummary, exception.Message), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask<InteractionRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        InteractionHealthState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            if (maxCycles > 0 && cycles >= maxCycles)
            {
                break;
            }

            if (duration is not null && DateTimeOffset.UtcNow - started >= duration.Value)
            {
                break;
            }

            try
            {
                await Task.Delay(Math.Max(50, _options.PollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new InteractionRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            cycles,
            _idleCycles,
            _perceptionEventsRead,
            _interactionEventsWritten,
            _targetsObserved,
            _targetsAccepted,
            _targetsRejected,
            _actionableTargets,
            _lastSummary,
            _lastError);
    }

    private IReadOnlyList<InteractionTarget> BuildTargets(PerceptionSnapshot snapshot)
    {
        var targets = new List<InteractionTarget>();

        foreach (var row in snapshot.ChatRows)
        {
            targets.Add(ClassifyChatRow(row));
        }

        foreach (var conversation in snapshot.Conversations)
        {
            var normalized = NormalizeText(conversation.Title);
            var reasons = new List<string>();
            if (string.IsNullOrWhiteSpace(conversation.Title))
            {
                reasons.Add("missing_title");
            }

            if (IsBrowserUiText(normalized) || IsGenericWhatsAppTitle(normalized))
            {
                reasons.Add("browser_or_generic_ui_title");
            }

            targets.Add(new InteractionTarget(
                StableTargetId("active_conversation", conversation.ChannelId, conversation.ConversationId, conversation.Title),
                "active_conversation",
                conversation.ChannelId,
                conversation.SourcePerceptionEventId,
                conversation.ObservedAt,
                conversation.Title,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Math.Clamp(conversation.Confidence, 0, 1),
                false,
                reasons.Count == 0 ? "visible_conversation" : "rejected_context",
                reasons.Count == 0 ? ["no_click_coordinates"] : reasons,
                new Dictionary<string, object?>
                {
                    ["conversationId"] = conversation.ConversationId,
                    ["role"] = conversation.Role
                }));
        }

        return targets
            .OrderByDescending(target => target.Actionable)
            .ThenBy(target => target.ChannelId)
            .ThenBy(target => target.Top)
            .ToArray();
    }

    private InteractionTarget ClassifyChatRow(PerceptionChatRow row)
    {
        var confidence = Math.Clamp(row.Confidence
            + (row.UnreadCount > 0 ? 0.05 : 0)
            + (!string.IsNullOrWhiteSpace(row.Preview) ? 0.03 : 0), 0, 1);
        var normalizedTitle = NormalizeText(row.Title);
        var normalizedPreview = NormalizeText(row.Preview);
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(row.ChannelId))
        {
            reasons.Add("missing_channel");
        }

        if (string.IsNullOrWhiteSpace(row.Title))
        {
            reasons.Add("missing_title");
        }

        if (row.ClickX <= 0 || row.ClickY <= 0 || row.Width <= 0 || row.Height <= 0)
        {
            reasons.Add("missing_click_coordinates");
        }

        if (IsBrowserUiText(normalizedTitle) || IsBrowserUiText(normalizedPreview) || IsGenericWhatsAppTitle(normalizedTitle))
        {
            reasons.Add("browser_or_generic_ui_text");
        }

        if (_options.RejectBusinessAdminGroups && IsLowValueBusinessGroup(normalizedTitle))
        {
            reasons.Add("low_value_payment_group");
        }

        if (confidence < _options.MinimumActionableConfidence)
        {
            reasons.Add("low_confidence");
        }

        var actionable = reasons.Count == 0;
        return new InteractionTarget(
            StableTargetId("chat_row", row.ChannelId, row.ChatRowId, row.Title),
            "chat_row",
            row.ChannelId,
            row.SourcePerceptionEventId,
            row.ObservedAt,
            row.Title,
            row.Preview,
            row.UnreadCount,
            row.Left,
            row.Top,
            row.Width,
            row.Height,
            row.ClickX,
            row.ClickY,
            confidence,
            actionable,
            actionable ? "customer_chat_candidate" : "rejected_chat_row",
            reasons,
            new Dictionary<string, object?>
            {
                ["chatRowId"] = row.ChatRowId,
                ["rawConfidence"] = row.Confidence
            });
    }

    private InteractionSummary Summarize(IReadOnlyList<InteractionTarget> targets)
    {
        var accepted = targets.Where(target => target.Actionable).ToArray();
        var rejected = targets.Where(target => !target.Actionable).ToArray();
        var best = accepted
            .OrderByDescending(target => target.UnreadCount)
            .ThenByDescending(target => target.Confidence)
            .ThenBy(target => target.Top)
            .FirstOrDefault();
        var lastReason = rejected
            .SelectMany(target => target.RejectionReasons)
            .GroupBy(reason => reason, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? string.Empty;

        return new InteractionSummary(
            targets.Count,
            accepted.Length,
            rejected.Length,
            accepted.Length,
            best?.Title ?? string.Empty,
            lastReason);
    }

    private bool IsLowValueBusinessGroup(string normalizedTitle)
    {
        return _options.LowValueChatTitlePatterns
            .Select(NormalizeText)
            .Any(pattern => !string.IsNullOrWhiteSpace(pattern) && normalizedTitle.Contains(pattern, StringComparison.Ordinal));
    }

    private bool IsBrowserUiText(string normalizedText)
    {
        return _options.BrowserUiTitlePatterns
            .Select(NormalizeText)
            .Any(pattern => !string.IsNullOrWhiteSpace(pattern) && normalizedText.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool IsGenericWhatsAppTitle(string normalizedTitle)
    {
        return normalizedTitle.Equals("whatsapp", StringComparison.Ordinal)
            || normalizedTitle.Equals("whatsapp business", StringComparison.Ordinal)
            || normalizedTitle.EndsWith(" whatsapp business", StringComparison.Ordinal)
            || normalizedTitle.Contains(" paginas mas", StringComparison.Ordinal)
            || normalizedTitle.Contains(" perfil 1", StringComparison.Ordinal);
    }

    private static string StableInteractionEventId(PerceptionSnapshot snapshot, IReadOnlyList<InteractionTarget> targets)
    {
        var raw = $"{snapshot.LatestPerceptionEventId}|{string.Join("|", targets.Select(target => $"{target.TargetId}:{target.Actionable}:{target.ClickX}:{target.ClickY}"))}";
        return $"interaction-{StableHash(raw)[..24]}";
    }

    private static string StableTargetId(string type, string channelId, string objectId, string title)
    {
        return $"{type}-{StableHash($"{type}|{channelId}|{objectId}|{title}")[..20]}";
    }

    private static string StableHash(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeText(string value)
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

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim();
    }

    private InteractionHealthState CreateState(string status, string summary, string error)
    {
        return new InteractionHealthState(
            "ariadgsm_interaction_engine",
            status,
            DateTimeOffset.UtcNow,
            _perceptionEventsRead,
            _interactionEventsWritten,
            _targetsObserved,
            _targetsAccepted,
            _targetsRejected,
            _actionableTargets,
            _latestPerceptionEventId,
            _lastAcceptedTargetTitle,
            _lastRejectionReason,
            summary,
            error);
    }

    private async ValueTask<InteractionHealthState> WriteStateAsync(InteractionHealthState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_options.StateFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await WriteTextAtomicAsync(_options.StateFile, json, cancellationToken).ConfigureAwait(false);
        return state;
    }

    private static async ValueTask WriteTextAtomicAsync(string path, string text, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, text, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    File.Move(tempPath, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 7)
                {
                    await Task.Delay(25 * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
