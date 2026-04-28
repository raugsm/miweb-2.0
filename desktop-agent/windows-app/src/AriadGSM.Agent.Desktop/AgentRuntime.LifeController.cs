using System.IO;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private string LifeControllerStateFile => Path.Combine(_runtimeDir, "life-controller-state.json");

    private void WriteLifeState(string status, string phase, string summary, string reason)
    {
        try
        {
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_life_controller",
                ["phase"] = phase,
                ["summary"] = summary,
                ["reason"] = reason,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["desiredRunning"] = _desiredRunning,
                ["isRunning"] = IsRunning,
                ["version"] = CurrentVersion,
                ["executable"] = ExecutableDirectory
            };

            WriteAllTextAtomicShared(LifeControllerStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            WriteControlPlaneState(status, phase, summary, "life_controller");
            WriteDiagnosticTimelineEvent("life_controller", status, summary, $"reason={reason}", status.Equals("blocked", StringComparison.OrdinalIgnoreCase) ? "warning" : "info");
            WriteRuntimeKernelState("life_controller", reason);
        }
        catch
        {
        }
    }
}
