using System.Text.Json;
using AriadGSM.Hands.Config;

namespace AriadGSM.Hands.Safety;

public sealed class TrustSafetyGate
{
    private readonly HandsOptions _options;

    public TrustSafetyGate(HandsOptions options)
    {
        _options = options;
    }

    public TrustSafetyGateDecision Evaluate()
    {
        if (!_options.RequireTrustSafetyGate || !_options.ExecuteActions)
        {
            return new TrustSafetyGateDecision(true, "ALLOW", "Trust & Safety gate no bloquea modo plan.", 0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        if (!File.Exists(_options.TrustSafetyStateFile))
        {
            return new TrustSafetyGateDecision(false, "ASK_HUMAN", "Trust & Safety aun no publico permiso para manos.", int.MaxValue, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_options.TrustSafetyStateFile));
            var root = document.RootElement;
            var updatedAt = ReadDate(root, "updatedAt");
            var ageMs = updatedAt is null
                ? int.MaxValue
                : Math.Max(0, (int)(DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime()).TotalMilliseconds);
            if (ageMs > Math.Max(500, _options.TrustSafetyMaxAgeMs))
            {
                return new TrustSafetyGateDecision(false, "ASK_HUMAN", $"Trust & Safety esta viejo ({ageMs} ms); no muevo manos.", ageMs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            if (!root.TryGetProperty("permissionGate", out var permissionGate))
            {
                return new TrustSafetyGateDecision(false, "ASK_HUMAN", "Trust & Safety no trae permissionGate.", ageMs, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            }

            var approvedSources = ReadApprovedSources(root);
            var decision = ReadString(permissionGate, "decision");
            var reason = ReadString(permissionGate, "reason");
            var canHandsRun = ReadBool(permissionGate, "canHandsRun");
            if (!canHandsRun)
            {
                return new TrustSafetyGateDecision(false, decision, string.IsNullOrWhiteSpace(reason) ? "Trust & Safety pauso manos." : reason, ageMs, approvedSources);
            }

            return new TrustSafetyGateDecision(true, decision, string.IsNullOrWhiteSpace(reason) ? "Trust & Safety autorizo manos." : reason, ageMs, approvedSources);
        }
        catch (Exception exception)
        {
            return new TrustSafetyGateDecision(false, "ASK_HUMAN", $"No pude leer Trust & Safety: {exception.Message}", int.MaxValue, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IReadOnlyDictionary<string, string> ReadApprovedSources(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("approvalLedger", out var ledger)
            || !ledger.TryGetProperty("applied", out var applied)
            || applied.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in applied.EnumerateArray())
        {
            var sourceId = ReadString(item, "sourceId");
            var approvalId = ReadString(item, "approvalId");
            if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(approvalId))
            {
                result[sourceId] = approvalId;
            }
        }

        return result;
    }

    private static string ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool ReadBool(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }
}

public sealed record TrustSafetyGateDecision(
    bool HandsAllowed,
    string Decision,
    string Reason,
    int AgeMs,
    IReadOnlyDictionary<string, string> ApprovedSources);
