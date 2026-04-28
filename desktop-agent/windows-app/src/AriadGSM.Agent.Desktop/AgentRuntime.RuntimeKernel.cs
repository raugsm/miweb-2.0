using System.IO;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private string RuntimeKernelStateFile => Path.Combine(_runtimeDir, "runtime-kernel-state.json");
    private string RuntimeKernelReportFile => Path.Combine(_runtimeDir, "runtime-kernel-report.json");

    private void WriteRuntimeKernelState(string source, string trigger)
    {
        try
        {
            var state = BuildRuntimeKernelState(source, trigger);
            var options = new JsonSerializerOptions { WriteIndented = true };
            WriteAllTextAtomicShared(RuntimeKernelStateFile, JsonSerializer.Serialize(state, options));
            WriteAllTextAtomicShared(RuntimeKernelReportFile, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["engine"] = "ariadgsm_runtime_kernel",
                ["status"] = state["status"],
                ["summary"] = state["summary"],
                ["authority"] = state["authority"],
                ["humanReport"] = state["humanReport"]
            }, options));
        }
        catch
        {
            // Runtime Kernel reports must never become another crash source.
        }
    }

    private Dictionary<string, object?> BuildRuntimeKernelState(string source, string trigger)
    {
        var engineStates = BuildRuntimeKernelEngines().ToArray();
        var incidents = BuildRuntimeKernelIncidents(engineStates).ToArray();
        var running = engineStates.Count(item => IsLifecycle(item, "running", "ready"));
        var degraded = engineStates.Count(item => IsLifecycle(item, "degraded"));
        var blocked = engineStates.Count(item => IsLifecycle(item, "blocked"));
        var dead = engineStates.Count(item => IsLifecycle(item, "dead", "restarting"));
        var restartCount = RuntimeKernelRestartCount();
        var operatorHasPriority = RuntimeKernelOperatorHasPriority();
        var cabinReady = RuntimeKernelCabinReady();
        var trustBlocked = RuntimeKernelTrustGateBlocked();
        var canObserve = engineStates.Any(item => IsEngineLifecycle(item, "vision", "running", "ready", "degraded"));
        var canRead = IsRunning && cabinReady && engineStates.Any(item => IsEngineLifecycle(item, "reader_core", "running", "ready", "degraded"));
        var canThink = engineStates.Any(item => IsEngineLifecycle(item, "cognitive", "running", "ready", "degraded"));
        var canAct = IsRunning && dead == 0 && blocked == 0 && cabinReady && !operatorHasPriority && !trustBlocked;
        var canSync = IsRunning && dead == 0 && incidents.All(item => !IsIncidentSeverity(item, "critical"));
        var mainBlocker = RuntimeKernelMainBlocker(incidents, dead, blocked, operatorHasPriority, cabinReady, trustBlocked);
        var status = RuntimeKernelStatus(incidents, dead, blocked);
        var headline = status switch
        {
            "idle" => "IA detenida esperando inicio",
            "ok" => "Runtime estable",
            "blocked" => "IA bloqueada por seguridad o cabina",
            _ => "IA trabajando con incidente explicado"
        };

        return new Dictionary<string, object?>
        {
            ["status"] = status,
            ["engine"] = "ariadgsm_runtime_kernel",
            ["version"] = CurrentVersion,
            ["updatedAt"] = DateTimeOffset.UtcNow,
            ["contract"] = "runtime_kernel_state",
            ["runSessionId"] = CurrentRunSessionIdNoLock(),
            ["source"] = source,
            ["trigger"] = trigger,
            ["authority"] = new Dictionary<string, object?>
            {
                ["truthSource"] = "runtime-kernel-state.json",
                ["sessionTruthSource"] = "control-plane-state.json",
                ["runSessionId"] = CurrentRunSessionIdNoLock(),
                ["desiredRunning"] = _desiredRunning,
                ["isRunning"] = IsRunning,
                ["canObserve"] = canObserve,
                ["canRead"] = canRead,
                ["canThink"] = canThink,
                ["canAct"] = canAct,
                ["canSync"] = canSync,
                ["operatorHasPriority"] = operatorHasPriority,
                ["mainBlocker"] = mainBlocker
            },
            ["summary"] = new Dictionary<string, object?>
            {
                ["enginesTotal"] = engineStates.Length,
                ["enginesRunning"] = running,
                ["enginesDegraded"] = degraded,
                ["enginesBlocked"] = blocked,
                ["enginesDead"] = dead,
                ["incidentsOpen"] = incidents.Length,
                ["restartsRecent"] = restartCount
            },
            ["engines"] = engineStates,
            ["incidents"] = incidents,
            ["recovery"] = new Dictionary<string, object?>
            {
                ["supervisorActive"] = _supervisorTask is { IsCompleted: false },
                ["recentRestartCount"] = restartCount,
                ["lastCheckpointAt"] = DateTimeOffset.UtcNow,
                ["lastRecoveryAction"] = restartCount > 0 ? "supervisor_restart" : "none"
            },
            ["sourceFiles"] = RuntimeKernelSourceFiles(),
            ["outputFiles"] = new Dictionary<string, object?>
            {
                ["state"] = "runtime-kernel-state.json",
                ["report"] = "runtime-kernel-report.json",
                ["diagnosticTimeline"] = "diagnostic-timeline.jsonl"
            },
            ["humanReport"] = new Dictionary<string, object?>
            {
                ["headline"] = headline,
                ["queEstaPasando"] = new[]
                {
                    $"{running}/{engineStates.Length} motores reportan vida util.",
                    string.IsNullOrWhiteSpace(mainBlocker) ? "No hay bloqueo principal." : mainBlocker
                },
                ["queHice"] = new[]
                {
                    "Unifique motores, cabina, supervisor, seguridad e input en una sola verdad.",
                    "Converti errores recientes en incidentes explicables para no ocultarlos en logs."
                },
                ["queNecesitoDeBryams"] = incidents
                    .Where(item => item.TryGetValue("requiresHuman", out var value) && value is true)
                    .Select(item => item.TryGetValue("summary", out var summary) ? summary?.ToString() ?? string.Empty : string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Take(5)
                    .ToArray(),
                ["riesgos"] = new[]
                {
                    "Si un motor no actualiza estado, lo marco degradado aunque el proceso siga vivo.",
                    "Cloud Sync debe consumir este Kernel antes de subir reportes."
                }
            }
        };
    }

    private IEnumerable<Dictionary<string, object?>> BuildRuntimeKernelEngines()
    {
        var specs = new[]
        {
            ("vision", "Vision", "worker", "vision-health.json", "Vision"),
            ("perception", "Perception", "worker", "perception-health.json", "Perception"),
            ("interaction", "Interaction", "worker", "interaction-state.json", "Interaction"),
            ("orchestrator", "Orchestrator", "worker", "orchestrator-state.json", "Orchestrator"),
            ("reader_core", "Reader Core", "python_core", "reader-core-state.json", "PythonCoreLoop"),
            ("window_reality", "Window Reality Resolver", "python_core", "window-reality-state.json", "PythonCoreLoop"),
            ("support_telemetry", "Support & Telemetry", "python_core", "support-telemetry-state.json", "PythonCoreLoop"),
            ("timeline", "Timeline", "python_core", "timeline-state.json", "PythonCoreLoop"),
            ("cognitive", "Cognitive", "python_core", "cognitive-state.json", "PythonCoreLoop"),
            ("operating", "Operating", "python_core", "operating-state.json", "PythonCoreLoop"),
            ("case_manager", "Case Manager", "python_core", "case-manager-state.json", "PythonCoreLoop"),
            ("channel_routing", "Channel Routing", "python_core", "channel-routing-state.json", "PythonCoreLoop"),
            ("accounting_core", "Accounting Core", "python_core", "accounting-core-state.json", "PythonCoreLoop"),
            ("memory", "Memory", "python_core", "memory-state.json", "PythonCoreLoop"),
            ("business_brain", "Business Brain", "python_core", "business-brain-state.json", "PythonCoreLoop"),
            ("tool_registry", "Tool Registry", "python_core", "tool-registry-state.json", "PythonCoreLoop"),
            ("cloud_sync", "Cloud Sync", "python_core", "cloud-sync-state.json", "PythonCoreLoop"),
            ("evaluation_release", "Evaluation + Release", "python_core", "evaluation-release-state.json", "PythonCoreLoop"),
            ("trust_safety", "Trust & Safety", "python_core", "trust-safety-state.json", "PythonCoreLoop"),
            ("hands", "Hands", "worker", "hands-state.json", "Hands"),
            ("input_arbiter", "Input Arbiter", "worker", "input-arbiter-state.json", "Hands"),
            ("supervisor", "Supervisor", "python_core", "supervisor-state.json", "PythonCoreLoop"),
            ("autonomous_cycle", "Autonomous Cycle", "python_core", "autonomous-cycle-state.json", "PythonCoreLoop"),
            ("cabin_authority", "Cabin Authority", "control", "cabin-authority-state.json", "WorkspaceGuardian"),
            ("life_controller", "Life Controller", "control", "life-controller-state.json", "LifeController"),
            ("agent_supervisor", "Agent Supervisor", "control", "agent-supervisor-state.json", "ReliabilitySupervisor")
        };

        foreach (var (engineId, name, kind, fileName, processName) in specs)
        {
            yield return BuildRuntimeKernelEngine(engineId, name, kind, fileName, processName);
        }
    }

    private Dictionary<string, object?> BuildRuntimeKernelEngine(string engineId, string name, string kind, string fileName, string processName)
    {
        using var document = ReadJsonStatus(fileName);
        var state = document?.RootElement;
        var status = state is null ? "missing" : TryString(state.Value, "status", "Status") ?? "ok";
        var updatedAt = state is null ? null : TryDate(state.Value, "updatedAt", "UpdatedAt", "observedAt", "ObservedAt");
        var lastError = state is null ? string.Empty : TryString(state.Value, "lastError", "LastError", "error", "Error") ?? string.Empty;
        var summary = state is null ? "Sin estado publicado." : BuildStateDetail(state.Value, lastError);
        var processRunning = IsManagedProcessActive(processName);
        var lifecycle = ClassifyRuntimeLifecycle(status, lastError, updatedAt, processRunning);
        return new Dictionary<string, object?>
        {
            ["engineId"] = engineId,
            ["name"] = name,
            ["kind"] = kind,
            ["lifecycle"] = lifecycle,
            ["status"] = status,
            ["sourceFile"] = fileName,
            ["updatedAt"] = updatedAt,
            ["ageMs"] = updatedAt is null ? null : Math.Max(0, (int)(DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime()).TotalMilliseconds),
            ["processRunning"] = processRunning,
            ["summary"] = summary,
            ["lastError"] = lastError
        };
    }

    private string ClassifyRuntimeLifecycle(string status, string lastError, DateTimeOffset? updatedAt, bool processRunning)
    {
        if (status.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked";
        }

        if (!string.IsNullOrWhiteSpace(lastError) || status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return processRunning ? "degraded" : "dead";
        }

        if (processRunning && updatedAt is not null && DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime() > RunningStateStaleAfter)
        {
            return "degraded";
        }

        if (processRunning)
        {
            return "running";
        }

        if (_desiredRunning)
        {
            return "restarting";
        }

        return status.Equals("missing", StringComparison.OrdinalIgnoreCase) ? "unknown" : "stopped";
    }

    private IReadOnlyList<Dictionary<string, object?>> BuildRuntimeKernelIncidents(IReadOnlyList<Dictionary<string, object?>> engines)
    {
        var incidents = new List<Dictionary<string, object?>>();
        foreach (var engine in engines.Where(item => IsLifecycle(item, "degraded", "blocked", "dead", "restarting")).TakeLast(10))
        {
            var engineId = engine["engineId"]?.ToString() ?? "engine";
            var lifecycle = engine["lifecycle"]?.ToString() ?? "unknown";
            incidents.Add(RuntimeKernelIncident(
                engineId,
                $"engine_{lifecycle}",
                $"{engine["name"]} esta en {lifecycle}.",
                engine["lastError"]?.ToString() ?? engine["summary"]?.ToString() ?? string.Empty,
                lifecycle is "dead" or "restarting" ? "error" : "warning",
                lifecycle is "dead" or "restarting" ? "supervisor_restart" : "observe_and_report",
                engineId == "cabin_authority"));
        }

        foreach (var line in RecentProblemLines(80).TakeLast(20))
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("unauthorizedaccessexception", StringComparison.Ordinal) || lower.Contains("access to the path is denied", StringComparison.Ordinal))
            {
                incidents.Add(RuntimeKernelIncident("vision", "state_write_denied", "Windows nego escritura a un estado local.", line, "error", "retry_and_fallback_state", false));
            }
            else if (lower.Contains("was not running. restarting from reliability supervisor", StringComparison.Ordinal))
            {
                incidents.Add(RuntimeKernelIncident("agent_supervisor", "engine_restart", "Un motor cayo y el supervisor lo reinicio.", line, "warning", "supervisor_restart", false));
            }
            else if (lower.Contains("zone_covered", StringComparison.Ordinal) || lower.Contains("covered", StringComparison.Ordinal) && lower.Contains("cabina", StringComparison.Ordinal))
            {
                incidents.Add(RuntimeKernelIncident("cabin_authority", "workspace_covered", "Una zona WhatsApp esta cubierta por otra ventana.", line, "warning", "report_without_closing_windows", true));
            }
        }

        return incidents
            .GroupBy(item => $"{item["source"]}|{item["code"]}|{item["detail"]}")
            .Select(group => group.Last())
            .TakeLast(16)
            .ToArray();
    }

    private static Dictionary<string, object?> RuntimeKernelIncident(
        string source,
        string code,
        string summary,
        string detail,
        string severity,
        string recoveryAction,
        bool requiresHuman)
    {
        return new Dictionary<string, object?>
        {
            ["incidentId"] = $"{source}-{code}-{Math.Abs(HashCode.Combine(source, code, detail))}",
            ["severity"] = severity,
            ["source"] = source,
            ["code"] = code,
            ["summary"] = summary,
            ["detail"] = detail,
            ["detectedAt"] = DateTimeOffset.UtcNow,
            ["recoveryAction"] = recoveryAction,
            ["requiresHuman"] = requiresHuman
        };
    }

    private int RuntimeKernelRestartCount()
    {
        using var document = ReadJsonStatus("agent-supervisor-state.json");
        if (document is null)
        {
            return 0;
        }

        return int.TryParse(Number(document, "restartCount"), out var parsed) ? parsed : 0;
    }

    private bool RuntimeKernelOperatorHasPriority()
    {
        using var document = ReadJsonStatus("input-arbiter-state.json");
        if (document is null)
        {
            return false;
        }

        var phase = Text(document, "phase");
        return phase.Equals("operator_control", StringComparison.OrdinalIgnoreCase)
            || (document.RootElement.TryGetProperty("operatorHasPriority", out var priority) && priority.ValueKind == JsonValueKind.True);
    }

    private bool RuntimeKernelCabinReady()
    {
        using var document = ReadJsonStatus("cabin-authority-state.json");
        if (document is null)
        {
            return false;
        }

        var status = Text(document, "status");
        return status.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || status.Equals("ready", StringComparison.OrdinalIgnoreCase);
    }

    private bool RuntimeKernelTrustGateBlocked()
    {
        using var document = ReadJsonStatus("trust-safety-state.json");
        if (document is null
            || !document.RootElement.TryGetProperty("permissionGate", out var gate)
            || gate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var decision = TryString(gate, "decision") ?? string.Empty;
        return decision is "BLOCK" or "PAUSE_FOR_OPERATOR";
    }

    private static bool IsLifecycle(Dictionary<string, object?> engine, params string[] values)
    {
        var lifecycle = engine.TryGetValue("lifecycle", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        return values.Any(item => item.Equals(lifecycle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEngineLifecycle(Dictionary<string, object?> engine, string engineId, params string[] values)
    {
        return engine.TryGetValue("engineId", out var value)
            && engineId.Equals(value?.ToString(), StringComparison.OrdinalIgnoreCase)
            && IsLifecycle(engine, values);
    }

    private static bool IsIncidentSeverity(Dictionary<string, object?> incident, string severity)
    {
        return incident.TryGetValue("severity", out var value)
            && severity.Equals(value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string RuntimeKernelMainBlocker(
        IReadOnlyList<Dictionary<string, object?>> incidents,
        int dead,
        int blocked,
        bool operatorHasPriority,
        bool cabinReady,
        bool trustBlocked)
    {
        if (dead > 0)
        {
            return "Hay motores caidos o reiniciando.";
        }
        if (blocked > 0 || trustBlocked)
        {
            return "Trust & Safety bloqueo acciones hasta revisar riesgo.";
        }
        if (operatorHasPriority)
        {
            return "Bryams tiene prioridad sobre mouse y teclado.";
        }
        if (!cabinReady)
        {
            using var cabin = ReadJsonStatus("cabin-authority-state.json");
            return Text(cabin, "summary");
        }
        return incidents.LastOrDefault()?["summary"]?.ToString() ?? string.Empty;
    }

    private string RuntimeKernelStatus(IReadOnlyList<Dictionary<string, object?>> incidents, int dead, int blocked)
    {
        if (!_desiredRunning && !IsRunning)
        {
            return "idle";
        }
        if (blocked > 0 || incidents.Any(item => IsIncidentSeverity(item, "critical")))
        {
            return "blocked";
        }
        if (dead > 0 || incidents.Any(item => IsIncidentSeverity(item, "error") || IsIncidentSeverity(item, "warning")))
        {
            return "attention";
        }
        return "ok";
    }

    private static Dictionary<string, object?> RuntimeKernelSourceFiles()
    {
        return new Dictionary<string, object?>
        {
            ["vision"] = "vision-health.json",
            ["perception"] = "perception-health.json",
            ["interaction"] = "interaction-state.json",
            ["orchestrator"] = "orchestrator-state.json",
            ["cabinAuthority"] = "cabin-authority-state.json",
            ["windowReality"] = "window-reality-state.json",
            ["supportTelemetry"] = "support-telemetry-state.json",
            ["lifeController"] = "life-controller-state.json",
            ["agentSupervisor"] = "agent-supervisor-state.json",
            ["trustSafety"] = "trust-safety-state.json",
            ["inputArbiter"] = "input-arbiter-state.json",
            ["cloudSync"] = "cloud-sync-state.json"
        };
    }

    private HealthItem RuntimeKernelHealth()
    {
        using var document = ReadJsonStatus("runtime-kernel-state.json");
        if (document is null)
        {
            return new HealthItem("Runtime Kernel", "SIN ESTADO", HealthSeverity.Warning, null, "Aun no hay verdad central publicada.");
        }

        var root = document.RootElement;
        var status = TryString(root, "status") ?? "unknown";
        var updatedAt = TryDate(root, "updatedAt");
        var detail = BuildStateDetail(root, string.Empty);
        if (root.TryGetProperty("authority", out var authority) && authority.ValueKind == JsonValueKind.Object)
        {
            detail = TryString(authority, "mainBlocker") ?? detail;
        }

        var severity = status switch
        {
            "ok" => HealthSeverity.Ok,
            "idle" => HealthSeverity.Info,
            "blocked" or "error" => HealthSeverity.Error,
            _ => HealthSeverity.Warning
        };
        return new HealthItem("Runtime Kernel", status.ToUpperInvariant(), severity, updatedAt, string.IsNullOrWhiteSpace(detail) ? "Verdad central recibida." : detail);
    }

    private static string RuntimeKernelAuthorityText(JsonDocument? document)
    {
        if (document is null)
        {
            return "aun no publico verdad central";
        }

        var root = document.RootElement;
        if (!root.TryGetProperty("authority", out var authority) || authority.ValueKind != JsonValueKind.Object)
        {
            return BuildStateDetail(root, string.Empty);
        }

        var blocker = TryString(authority, "mainBlocker") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(blocker))
        {
            return blocker;
        }

        var observe = TryBool(authority, "canObserve") == true ? "observa" : "no observa";
        var think = TryBool(authority, "canThink") == true ? "piensa" : "no piensa";
        var act = TryBool(authority, "canAct") == true ? "puede actuar" : "manos pausadas";
        var sync = TryBool(authority, "canSync") == true ? "puede sincronizar" : "sin sincronizar";
        return $"{observe}, {think}, {act}, {sync}";
    }
}
