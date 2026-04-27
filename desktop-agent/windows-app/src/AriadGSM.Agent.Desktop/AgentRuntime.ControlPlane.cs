using System.IO;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private string ControlPlaneStateFile => Path.Combine(_runtimeDir, "control-plane-state.json");
    private string ArchitectureStateFile => Path.Combine(_runtimeDir, "architecture-0.6-state.json");
    private string DiagnosticTimelineFile => Path.Combine(_runtimeDir, "diagnostic-timeline.jsonl");

    private void WriteArchitectureState()
    {
        try
        {
            var state = new Dictionary<string, object?>
            {
                ["architectureVersion"] = "0.6.0",
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["principles"] = new[]
                {
                    "Control Center is the cockpit, not the business brain.",
                    "Runtime Orchestrator owns lifecycle.",
                    "Cabin Workspace Manager owns WhatsApp windows.",
                    "Cabin Authority gates every window action.",
                    "Input Arbiter gives operator priority over mouse and keyboard.",
                    "Reader Core outputs structured conversations, not loose OCR lines.",
                    "Action Queue audits plan, safety, execution and verification.",
                    "Memory and Accounting store evidence-first business knowledge.",
                    "Diagnostic Timeline explains what happened in human language."
                },
                ["layers"] = new[]
                {
                    "control_center",
                    "runtime_orchestrator",
                    "cabin_workspace_manager",
                    "cabin_authority",
                    "input_arbiter",
                    "reader_core",
                    "action_queue",
                    "memory_accounting",
                    "diagnostic_timeline"
                }
            };

            WriteAllTextAtomicShared(ArchitectureStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private void WriteControlPlaneState(string status, string phase, string summary, string source)
    {
        try
        {
            WriteAllTextAtomicShared(ControlPlaneStateFile, JsonSerializer.Serialize(BuildControlPlaneState(status, phase, summary, source), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    public void RefreshControlPlaneSnapshot(string source = "control_center")
    {
        try
        {
            var status = IsRunning ? "running" : "stopped";
            var phase = IsRunning ? "monitoring" : "idle";
            var summary = IsRunning
                ? "Control Plane vigilando cabina, manos, lectura, memoria y contabilidad."
                : "Control Plane listo; motores detenidos hasta inicio manual.";
            WriteAllTextAtomicShared(ControlPlaneStateFile, JsonSerializer.Serialize(BuildControlPlaneState(status, phase, summary, source), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private Dictionary<string, object?> BuildControlPlaneState(string status, string phase, string summary, string source)
    {
        var now = DateTimeOffset.UtcNow;
        var liveCabin = LiveCabinSnapshot();
        var authority = ReadStateSummary("cabin-authority-state.json");
        var hands = ReadStateSummary("hands-state.json");
        var input = ReadStateSummary("input-arbiter-state.json");
        var perception = ReadStateSummary("perception-health.json");
        var memory = ReadStateSummary("memory-state.json");
        var operating = ReadStateSummary("operating-state.json");
        var conflicts = DetectControlPlaneConflicts(liveCabin, authority, perception).ToArray();
        var operationalStatus = conflicts.Length > 0
            ? "attention"
            : liveCabin.ReadyChannels == liveCabin.ExpectedChannels && liveCabin.ExpectedChannels > 0
                ? "ready"
                : liveCabin.ReadyChannels > 0 ? "degraded" : "attention";

        return new Dictionary<string, object?>
        {
            ["status"] = status,
            ["engine"] = "ariadgsm_control_plane",
            ["architectureVersion"] = "0.6.0",
            ["phase"] = phase,
            ["summary"] = summary,
            ["source"] = source,
            ["updatedAt"] = now,
            ["version"] = CurrentVersion,
            ["isRunning"] = IsRunning,
            ["desiredRunning"] = _desiredRunning,
            ["operationalStatus"] = operationalStatus,
            ["contracts"] = new Dictionary<string, object?>
            {
                ["windowControlOwner"] = "Cabin Authority",
                ["mouseKeyboardOwner"] = "Input Arbiter",
                ["conversationOwner"] = "Reader Core",
                ["actionOwner"] = "Action Queue",
                ["memoryOwner"] = "Memory + Accounting",
                ["stateOwner"] = "Control Plane"
            },
            ["cabin"] = new Dictionary<string, object?>
            {
                ["status"] = liveCabin.Status,
                ["readyChannels"] = liveCabin.ReadyChannels,
                ["expectedChannels"] = liveCabin.ExpectedChannels,
                ["summary"] = liveCabin.Summary,
                ["channels"] = liveCabin.Channels
            },
            ["authority"] = authority,
            ["hands"] = hands,
            ["input"] = input,
            ["reader"] = perception,
            ["memory"] = memory,
            ["operating"] = operating,
            ["conflicts"] = conflicts
        };
    }

    private ControlPlaneCabinSnapshot LiveCabinSnapshot()
    {
        var windows = VisibleWindows();
        var channels = ReadChannelMappings()
            .Select(mapping => EvaluateChannelReadiness(mapping, windows))
            .Select(readiness => new Dictionary<string, object?>
            {
                ["channelId"] = readiness.ChannelId,
                ["browser"] = readiness.Mapping.BrowserProcess,
                ["status"] = readiness.Status,
                ["isReady"] = readiness.IsReady,
                ["requiresHuman"] = readiness.RequiresHuman,
                ["detail"] = readiness.Detail,
                ["window"] = SerializeCabinWindow(readiness.Window)
            })
            .ToArray();
        var expected = channels.Length;
        var ready = channels.Count(channel => channel.TryGetValue("isReady", out var value) && value is true);
        var status = ready == expected && expected > 0 ? "ready" : ready > 0 ? "degraded" : "attention";
        var summary = $"Cabina viva {ready}/{Math.Max(1, expected)}: {string.Join(" | ", channels.Select(channel => $"{channel["channelId"]}:{channel["status"]}"))}.";
        return new ControlPlaneCabinSnapshot(status, ready, expected, summary, channels);
    }

    private IEnumerable<string> DetectControlPlaneConflicts(
        ControlPlaneCabinSnapshot liveCabin,
        Dictionary<string, object?> authority,
        Dictionary<string, object?> reader)
    {
        var authorityStatus = authority.TryGetValue("status", out var status) ? status?.ToString() ?? string.Empty : string.Empty;
        if (IsRunning && authorityStatus.Equals("stopped", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Cabin Authority esta detenida mientras los motores siguen corriendo.";
        }

        var readerSummary = reader.TryGetValue("summary", out var summary) ? summary?.ToString() ?? string.Empty : string.Empty;
        if (liveCabin.ReadyChannels == liveCabin.ExpectedChannels
            && readerSummary.Contains("2/3", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Reader/Perception ve menos canales que la cabina viva.";
        }
    }

    private Dictionary<string, object?> ReadStateSummary(string fileName)
    {
        try
        {
            var path = Path.Combine(_runtimeDir, fileName);
            if (!File.Exists(path))
            {
                return new Dictionary<string, object?>
                {
                    ["status"] = "missing",
                    ["summary"] = "Sin estado publicado.",
                    ["updatedAt"] = null
                };
            }

            using var document = JsonDocument.Parse(ReadAllTextShared(path));
            var root = document.RootElement;
            var updatedAt = TryDate(root, "updatedAt", "UpdatedAt");
            return new Dictionary<string, object?>
            {
                ["status"] = TryString(root, "status", "Status") ?? "ok",
                ["phase"] = TryString(root, "phase", "Phase") ?? string.Empty,
                ["summary"] = TryString(root, "summary", "lastSummary", "detail") ?? "Estado recibido.",
                ["updatedAt"] = updatedAt,
                ["ageMs"] = updatedAt is null ? null : Math.Max(0, (int)(DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime()).TotalMilliseconds)
            };
        }
        catch (Exception exception)
        {
            return new Dictionary<string, object?>
            {
                ["status"] = "error",
                ["summary"] = exception.Message,
                ["updatedAt"] = null
            };
        }
    }

    private void WriteDiagnosticTimelineEvent(string source, string status, string summary, string detail = "", string severity = "info")
    {
        try
        {
            Directory.CreateDirectory(_runtimeDir);
            RotateDiagnosticTimelineIfNeeded();
            var entry = new Dictionary<string, object?>
            {
                ["eventType"] = "diagnostic_timeline_event",
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["architectureVersion"] = "0.6.0",
                ["source"] = source,
                ["status"] = status,
                ["severity"] = severity,
                ["summary"] = summary,
                ["detail"] = detail
            };
            AppendAllTextShared(DiagnosticTimelineFile, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void RotateDiagnosticTimelineIfNeeded()
    {
        try
        {
            if (!File.Exists(DiagnosticTimelineFile))
            {
                return;
            }

            var info = new FileInfo(DiagnosticTimelineFile);
            if (info.Length <= 4L * 1024 * 1024)
            {
                return;
            }

            var archive = Path.Combine(_runtimeDir, $"diagnostic-timeline-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jsonl");
            File.Move(DiagnosticTimelineFile, archive, overwrite: true);
        }
        catch
        {
        }
    }

    private sealed record ControlPlaneCabinSnapshot(
        string Status,
        int ReadyChannels,
        int ExpectedChannels,
        string Summary,
        IReadOnlyList<Dictionary<string, object?>> Channels);
}
