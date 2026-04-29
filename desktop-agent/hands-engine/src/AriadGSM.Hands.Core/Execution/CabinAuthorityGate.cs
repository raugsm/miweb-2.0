using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Execution;

public sealed class CabinAuthorityGate
{
    private readonly HandsOptions _options;

    public CabinAuthorityGate(HandsOptions options)
    {
        _options = options;
    }

    public CabinAuthorityDecision Evaluate(ActionPlan plan)
    {
        if (!_options.RequireCabinAuthorityForWindowActions
            || !_options.ExecuteActions
            || !RequiresWindowControl(plan.ActionType))
        {
            return CabinAuthorityDecision.Allow("Cabin Authority not required for this action.");
        }

        var channelId = TargetString(plan, "channelId");
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return CabinAuthorityDecision.Block("Cabin Authority blocked the action: no channelId was provided.");
        }

        if (!File.Exists(_options.CabinAuthorityStateFile))
        {
            return CabinAuthorityDecision.Block("Cabin Authority state is missing; Hands will not touch browser windows.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_options.CabinAuthorityStateFile));
            var root = document.RootElement;
            var updatedAt = ReadDate(root, "updatedAt");
            if (updatedAt is null)
            {
                return CabinAuthorityDecision.Block("Cabin Authority state has no timestamp; Hands will not trust it.");
            }

            var ageMs = (DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime()).TotalMilliseconds;
            if (ageMs > Math.Max(500, _options.CabinAuthorityMaxAgeMs))
            {
                return CabinAuthorityDecision.Block($"Cabin Authority state is stale ({ageMs:0}ms old); Hands will wait for a fresh cabin check.");
            }

            if (TryReadBool(root, "handsMayFocus") is false)
            {
                return CabinAuthorityDecision.Block("Cabin Authority says Hands may not focus windows right now.");
            }

            if (!TryFindChannel(root, channelId, out var channel))
            {
                return CabinAuthorityDecision.Block($"Cabin Authority has no registered ready channel for {channelId}.");
            }

            var status = ReadString(channel, "status");
            var handsMayAct = TryReadBool(channel, "handsMayAct") ?? IsActionReadyStatus(status);
            var remainingBlockers = ReadInt(channel, "remainingBlockers");
            if (!handsMayAct || !IsActionReadyStatus(status))
            {
                return CabinAuthorityDecision.Block($"Cabin Authority blocked {channelId}: channel status is {status}.");
            }

            if (remainingBlockers > 0)
            {
                return CabinAuthorityDecision.Block($"Cabin Authority blocked {channelId}: {remainingBlockers} window blocker(s) remain.");
            }

            if (TryFindBlocker(root, channelId, out var blocker))
            {
                var detail = ReadString(blocker, "detail");
                return CabinAuthorityDecision.Block($"Cabin Authority blocked {channelId}: {detail}");
            }

            var windowReality = EvaluateWindowReality(channelId);
            if (!windowReality.Allowed)
            {
                return windowReality;
            }

            return CabinAuthorityDecision.Allow($"Cabin Authority cleared {channelId} for Hands.");
        }
        catch (Exception exception)
        {
            return CabinAuthorityDecision.Block($"Cabin Authority state could not be read: {exception.Message}");
        }
    }

    private CabinAuthorityDecision EvaluateWindowReality(string channelId)
    {
        if (!_options.RequireWindowRealityForWindowActions)
        {
            return CabinAuthorityDecision.Allow("Window Reality not required for this action.");
        }

        if (!File.Exists(_options.WindowRealityStateFile))
        {
            return CabinAuthorityDecision.Block("Window Reality state is missing; Hands will not touch browser windows.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_options.WindowRealityStateFile));
            var root = document.RootElement;
            var updatedAt = ReadDate(root, "updatedAt");
            if (updatedAt is null)
            {
                return CabinAuthorityDecision.Block("Window Reality state has no timestamp; Hands will not trust it.");
            }

            var ageMs = (DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime()).TotalMilliseconds;
            if (ageMs > Math.Max(500, _options.WindowRealityMaxAgeMs))
            {
                return CabinAuthorityDecision.Block($"Window Reality state is stale ({ageMs:0}ms old); Hands will wait for a fresh reality check.");
            }

            if (!TryFindChannel(root, channelId, out var channel))
            {
                return CabinAuthorityDecision.Block($"Window Reality has no channel evidence for {channelId}.");
            }

            var status = ReadString(channel, "status");
            var structuralReady = TryReadBool(channel, "structuralReady") == true;
            var actionReady = TryReadBool(channel, "actionReady") == true;
            var handsMayAct = TryReadBool(channel, "handsMayAct") == true;
            var isOperational = TryReadBool(channel, "isOperational") == true;
            if (!structuralReady || !isOperational || !actionReady || !handsMayAct)
            {
                var reason = ReadDecisionReason(channel);
                return CabinAuthorityDecision.Block(
                    $"Window Reality blocked {channelId}: status={status}; structural={structuralReady}; operational={isOperational}; actionReady={actionReady}; handsMayAct={handsMayAct}. {reason}");
            }

            return CabinAuthorityDecision.Allow($"Window Reality cleared {channelId} for Hands.");
        }
        catch (Exception exception)
        {
            return CabinAuthorityDecision.Block($"Window Reality state could not be read: {exception.Message}");
        }
    }

    private static bool RequiresWindowControl(string actionType)
    {
        return actionType.Equals("focus_window", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("capture_conversation", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActionReadyStatus(string status)
    {
        return status.Equals("ready", StringComparison.OrdinalIgnoreCase)
            || status.Equals("action_ready", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindChannel(JsonElement root, string channelId, out JsonElement channel)
    {
        channel = default;
        if (!root.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in channels.EnumerateArray())
        {
            if (ReadString(item, "channelId").Equals(channelId, StringComparison.OrdinalIgnoreCase))
            {
                channel = item;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindBlocker(JsonElement root, string channelId, out JsonElement blocker)
    {
        blocker = default;
        if (!root.TryGetProperty("blockers", out var blockers) || blockers.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in blockers.EnumerateArray())
        {
            if (ReadString(item, "channelId").Equals(channelId, StringComparison.OrdinalIgnoreCase))
            {
                blocker = item;
                return true;
            }
        }

        return false;
    }

    private static string ReadDecisionReason(JsonElement channel)
    {
        if (channel.TryGetProperty("decision", out var decision)
            && decision.ValueKind == JsonValueKind.Object)
        {
            return ReadString(decision, "reason");
        }

        return ReadString(channel, "actionabilityReason");
    }

    private static string TargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }

    private static bool? TryReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(property.GetString(), out var value))
        {
            return value;
        }

        return null;
    }
}

public sealed record CabinAuthorityDecision(bool Allowed, string Reason, double Confidence)
{
    public static CabinAuthorityDecision Allow(string reason)
    {
        return new CabinAuthorityDecision(true, reason, 0.98);
    }

    public static CabinAuthorityDecision Block(string reason)
    {
        return new CabinAuthorityDecision(false, reason, 0.98);
    }
}
