using System.IO;
using System.Linq;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private const string WhatsAppWebUrl = "https://web.whatsapp.com/";

    private void WriteCabinManagerState(string status, string phase, IReadOnlyList<ChannelReadiness> readiness, string summary)
    {
        try
        {
            var channels = readiness.Select(item => new Dictionary<string, object?>
            {
                ["channelId"] = item.ChannelId,
                ["browser"] = item.Mapping.BrowserProcess,
                ["expectedUrl"] = WhatsAppWebUrl,
                ["dedicatedProfileDirectory"] = CabinProfileDirectory(item.Mapping),
                ["status"] = item.Status,
                ["structuralReady"] = item.IsReady,
                ["semanticFresh"] = false,
                ["actionReady"] = false,
                ["isReady"] = item.IsReady,
                ["requiresHuman"] = item.RequiresHuman,
                ["canLaunch"] = item.CanLaunch,
                ["detail"] = item.Detail,
                ["window"] = SerializeCabinWindow(item.Window),
                ["evidence"] = item.Evidence.Take(8).ToArray()
            }).ToArray();

            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_cabin_manager",
                ["phase"] = phase,
                ["summary"] = summary,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["readyChannels"] = readiness.Count(item => item.IsReady),
                ["expectedChannels"] = readiness.Count,
                ["canStartDegraded"] = CanStartWithDegradedCabin(readiness),
                ["requiresHuman"] = readiness.Any(item => item.RequiresHuman),
                ["channels"] = channels,
                ["rules"] = new[]
                {
                    "wa-1=Edge WhatsApp 1",
                    "wa-2=Chrome WhatsApp 2",
                    "wa-3=Firefox WhatsApp 3",
                    "No cierro navegadores del operador",
                    "Primero busco ventanas o pestanas existentes",
                    "Solo abro web.whatsapp.com en el navegador asignado si no encuentro una existente",
                    "No uso WhatsApp instalado ni ventanas PWA",
                    "Acomodo la cabina una sola vez por alistamiento",
                    "Si un canal falla, explico el bloqueo y no lo marco accionable"
                }
            };

            WriteAllTextAtomicShared(_cabinManagerStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            WriteCabinChannelRegistry(readiness.Select(item => item.Mapping).Distinct().ToArray());
            WriteStatusBusState(status, phase, summary, readiness.Where(item => !item.IsReady).Select(item => $"{item.ChannelId}: {item.Status} - {item.Detail}").ToArray());
            WriteControlPlaneState(status, phase, summary, "cabin_workspace_manager");
            WriteDiagnosticTimelineEvent(
                "cabin_workspace_manager",
                status,
                summary,
                string.Join(" | ", readiness.Select(item => $"{item.ChannelId}:{item.Status}")),
                readiness.Any(item => item.RequiresHuman) ? "warning" : "info");
        }
        catch
        {
        }
    }

    private void WriteCabinChannelRegistry(IReadOnlyList<ChannelMapping> mappings)
    {
        try
        {
            var registry = new Dictionary<string, object?>
            {
                ["registryVersion"] = "ariadgsm_cabin_channels_v1",
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["expectedUrl"] = WhatsAppWebUrl,
                ["channels"] = mappings.Select(mapping => new Dictionary<string, object?>
                {
                    ["channelId"] = mapping.ChannelId,
                    ["browserProcess"] = mapping.BrowserProcess,
                    ["titleContains"] = mapping.TitleContains,
                    ["legacyProfileDirectory"] = mapping.ProfileDirectory,
                    ["dedicatedProfileDirectory"] = CabinProfileDirectory(mapping),
                    ["launchMode"] = "reuse_existing_or_open_explicit_browser_url",
                    ["profilePinningDefault"] = false,
                    ["allowOperatorBrowserReuse"] = true,
                    ["neverCloseOperatorBrowsers"] = true
                }).ToArray()
            };
            WriteAllTextAtomicShared(_cabinChannelRegistryFile, JsonSerializer.Serialize(registry, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private void WriteStatusBusState(string status, string phase, string summary, IReadOnlyList<string> blockers)
    {
        try
        {
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_status_bus",
                ["phase"] = phase,
                ["summary"] = summary,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["blockers"] = blockers,
                ["version"] = CurrentVersion,
                ["visibleToOperator"] = true
            };
            WriteAllTextAtomicShared(_statusBusStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            WriteControlPlaneState(status, phase, summary, "status_bus");
        }
        catch
        {
        }
    }

    private void PublishCabinSetup(
        IProgress<CabinSetupProgress>? progress,
        string status,
        string phase,
        int percent,
        IReadOnlyList<ChannelReadiness> readiness,
        string summary)
    {
        var blockers = readiness
            .Where(item => !item.IsReady)
            .Select(item => $"{item.ChannelId}: {item.Status} - {item.Detail}")
            .ToArray();

        WriteCabinReadinessState(status, readiness);
        WriteCabinManagerState(status, phase, readiness, summary);
        WriteWorkspaceSetupState(status, summary, percent, blockers);
        progress?.Report(new CabinSetupProgress(
            Math.Clamp(percent, 0, 100),
            phase,
            summary,
            readiness.Select(item => new CabinSetupChannelProgress(
                item.ChannelId,
                item.Mapping.BrowserProcess,
                item.Status,
                item.Detail)).ToArray(),
            readiness.Count > 0 && readiness.All(item => item.IsReady),
            CanStartWithDegradedCabin(readiness)));
    }

    private static bool CanStartWithDegradedCabin(IReadOnlyList<ChannelReadiness> readiness)
    {
        return readiness.Any(item => item.IsReady);
    }

    private static string CabinProfileDirectory(ChannelMapping mapping)
    {
        var safeBrowser = NormalizeProcessName(mapping.BrowserProcess);
        var safeChannel = mapping.ChannelId.Replace("-", "_", StringComparison.OrdinalIgnoreCase);
        return Path.Combine("desktop-agent", "runtime", "browser-profiles", $"{safeChannel}_{safeBrowser}");
    }

    private string ResolveCabinProfileDirectory(ChannelMapping mapping)
    {
        return Path.Combine(_repoRoot, CabinProfileDirectory(mapping));
    }

    private static string CabinSummary(IReadOnlyList<ChannelReadiness> readiness)
    {
        var ready = readiness.Count(item => item.IsReady);
        var total = readiness.Count;
        var states = string.Join(" | ", readiness.Select(item => $"{item.ChannelId}:{item.Status}"));
        return ready == total
            ? $"Cabina lista {ready}/{total}: {states}."
            : $"Cabina parcial {ready}/{total}: {states}.";
    }
}

internal sealed record CabinSetupProgress(
    int Percent,
    string Phase,
    string Summary,
    IReadOnlyList<CabinSetupChannelProgress> Channels,
    bool IsReady,
    bool CanStart);

internal sealed record CabinSetupChannelProgress(
    string ChannelId,
    string Browser,
    string Status,
    string Detail);
