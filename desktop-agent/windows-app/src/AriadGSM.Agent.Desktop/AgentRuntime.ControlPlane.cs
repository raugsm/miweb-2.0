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
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_control_plane",
                ["architectureVersion"] = "0.6.0",
                ["phase"] = phase,
                ["summary"] = summary,
                ["source"] = source,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["version"] = CurrentVersion,
                ["isRunning"] = IsRunning,
                ["desiredRunning"] = _desiredRunning,
                ["contracts"] = new Dictionary<string, object?>
                {
                    ["windowControlOwner"] = "Cabin Authority",
                    ["mouseKeyboardOwner"] = "Input Arbiter",
                    ["conversationOwner"] = "Reader Core",
                    ["actionOwner"] = "Action Queue",
                    ["memoryOwner"] = "Memory + Accounting"
                }
            };

            WriteAllTextAtomicShared(ControlPlaneStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
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
}
