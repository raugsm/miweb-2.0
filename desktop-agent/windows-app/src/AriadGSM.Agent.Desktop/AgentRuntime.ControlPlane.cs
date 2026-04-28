using System.IO;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private static readonly string[] BootProtocolPhaseIds =
    [
        "operator_authorized",
        "update_check",
        "workspace_bootstrap",
        "preflight",
        "runtime_governor",
        "workspace_guardian",
        "workers",
        "python_core",
        "supervisor",
        "readiness"
    ];

    private readonly List<RuntimeCommandRecord> _controlPlaneLedger = [];
    private readonly Dictionary<string, BootPhaseRecord> _bootProtocol = new(StringComparer.OrdinalIgnoreCase);
    private string? _runSessionId;
    private string? _lastRunSessionId;
    private RuntimeCommandRecord? _lastRuntimeCommand;
    private RuntimeStopCause _lastStopCause = new("none", "constructor", "constructor", DateTimeOffset.UtcNow, "La IA aun no recibio orden de apagado.");

    private string ControlPlaneStateFile => Path.Combine(_runtimeDir, "control-plane-state.json");
    private string ControlPlaneCommandLedgerFile => Path.Combine(_runtimeDir, "control-plane-command-ledger.jsonl");
    private string ControlPlaneCheckpointFile => Path.Combine(_runtimeDir, "control-plane-checkpoints.jsonl");
    private string ArchitectureStateFile => Path.Combine(_runtimeDir, "architecture-0.6-state.json");
    private string DiagnosticTimelineFile => Path.Combine(_runtimeDir, "diagnostic-timeline.jsonl");

    public string RequestControlPlaneStart(string source, string reason)
    {
        var command = BeginControlPlaneCommand("start", source, reason, startsSession: true);
        MarkBootPhase("operator_authorized", "ok", "Bryams autorizo encender la IA local.");
        CompleteControlPlaneCommand(command, "accepted", "La orden de inicio quedo registrada antes de revisar update, cabina y motores.");
        return command.RunSessionId;
    }

    public bool IsStartSessionActive(string runSessionId, out string reason)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(runSessionId))
            {
                reason = "No hay sesion de arranque registrada.";
                return false;
            }

            if (!string.Equals(_runSessionId, runSessionId, StringComparison.Ordinal))
            {
                reason = "La orden de inicio ya fue cancelada o reemplazada; no encendere motores con una sesion vieja.";
                return false;
            }

            if (_stopping)
            {
                reason = "La IA esta pausandose; cancelo el arranque pendiente.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private RuntimeCommandRecord EnsureStartSession(string source, string reason, string expectedRunSessionId)
    {
        if (!IsStartSessionActive(expectedRunSessionId, out var inactiveReason))
        {
            throw new OperationCanceledException(inactiveReason);
        }

        return BeginControlPlaneCommand("start_engines", source, reason);
    }

    private RuntimeCommandRecord BeginControlPlaneCommand(
        string commandType,
        string source,
        string reason,
        bool startsSession = false,
        bool endsSession = false)
    {
        var now = DateTimeOffset.UtcNow;
        RuntimeCommandRecord command;
        lock (_gate)
        {
            if (startsSession)
            {
                _runSessionId = $"run-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..33];
                _stopping = false;
                ResetBootProtocolNoLock(now);
            }

            command = new RuntimeCommandRecord
            {
                CommandId = $"cmd-{now:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..33],
                CommandType = commandType,
                Source = source,
                Reason = reason,
                RunSessionId = CurrentRunSessionIdNoLock(),
                Status = "accepted",
                Accepted = true,
                CreatedAt = now,
                EndsSession = endsSession
            };

            _lastRuntimeCommand = command;
            _controlPlaneLedger.Add(command);
            while (_controlPlaneLedger.Count > 60)
            {
                _controlPlaneLedger.RemoveAt(0);
            }
        }

        AppendControlPlaneLedger(command);
        WriteControlPlaneCheckpoint("command", command.CommandType, $"{source}: {reason}");
        WriteControlPlaneState("command", "command_received", $"Orden recibida: {HumanCommand(command)}.", "control_plane");
        return command;
    }

    private void CompleteControlPlaneCommand(RuntimeCommandRecord command, string status, string result, bool accepted = true)
    {
        command.Status = status;
        command.Result = result;
        command.Accepted = accepted;
        command.CompletedAt = DateTimeOffset.UtcNow;
        AppendControlPlaneLedger(command);
        WriteControlPlaneCheckpoint("command_result", command.CommandType, result);
    }

    private void RegisterControlPlaneStopCause(string reason, string source, string detail)
    {
        lock (_gate)
        {
            _lastStopCause = new(reason, source, CurrentRunSessionIdNoLock(), DateTimeOffset.UtcNow, detail);
        }
    }

    private void EndRunSession(string reason, string source)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_runSessionId))
            {
                _lastRunSessionId = _runSessionId;
                _runSessionId = null;
            }

            _lastStopCause = new(reason, source, CurrentRunSessionIdNoLock(), DateTimeOffset.UtcNow, $"Sesion cerrada por {source}: {reason}.");
        }

        WriteControlPlaneCheckpoint("session_closed", reason, $"Sesion cerrada por {source}.");
        WriteControlPlaneState("stopped", "session_closed", $"Sesion finalizada: {reason}.", "control_plane");
    }

    private void MarkBootPhase(string phaseId, string status, string summary)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_bootProtocol.Count == 0)
            {
                ResetBootProtocolNoLock(now);
            }

            _bootProtocol[phaseId] = new BootPhaseRecord(
                phaseId,
                status,
                summary,
                now,
                status.Equals("ok", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("ready", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("attention", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("blocked", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("error", StringComparison.OrdinalIgnoreCase)
                    ? now
                    : null);
        }

        WriteControlPlaneCheckpoint("boot_phase", phaseId, $"{status}: {summary}");
        WriteControlPlaneState(status, phaseId, summary, "control_plane");
    }

    private string? CurrentRunSessionIdOrNull()
    {
        lock (_gate)
        {
            return _runSessionId;
        }
    }

    private string CurrentRunSessionIdNoLock()
    {
        return _runSessionId ?? _lastRunSessionId ?? "no-active-session";
    }

    private void ResetBootProtocolNoLock(DateTimeOffset now)
    {
        _bootProtocol.Clear();
        foreach (var phaseId in BootProtocolPhaseIds)
        {
            _bootProtocol[phaseId] = new BootPhaseRecord(phaseId, "pending", "Pendiente.", now, null);
        }
    }

    private void WriteArchitectureState()
    {
        try
        {
            var state = new Dictionary<string, object?>
            {
                ["architectureVersion"] = "final-ai-8-layers",
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["authorityDocument"] = "docs/ARIADGSM_FINAL_AI_ARCHITECTURE.md",
                ["principles"] = new[]
                {
                    "Operator Product Shell is the human cockpit.",
                    "AI Runtime Control Plane owns every run session and lifecycle command.",
                    "Cabin Reality Authority owns WhatsApp window reality.",
                    "Perception and Reader Core output structured messages, not loose OCR lines.",
                    "Event, Timeline and Durable State Backbone preserve causality.",
                    "Living Memory and Business Brain reason over AriadGSM as a business.",
                    "Action, Tools and Verification own physical action after permission.",
                    "Trust, Telemetry, Evaluation and Cloud explain, protect, test and sync."
                },
                ["layers"] = new[]
                {
                    "operator_product_shell",
                    "ai_runtime_control_plane",
                    "cabin_reality_authority",
                    "perception_reader_core",
                    "event_timeline_durable_state_backbone",
                    "living_memory_business_brain",
                    "action_tools_verification",
                    "trust_telemetry_evaluation_cloud"
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
                ? "Control Plane vigila sesion, cabina, motores, lectura, pensamiento y manos."
                : "Control Plane listo; IA detenida hasta inicio autorizado.";
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
        var reader = ReadStateSummary("reader-core-state.json");
        var memory = ReadStateSummary("memory-state.json");
        var operating = ReadStateSummary("operating-state.json");
        var cognitive = ReadStateSummary("cognitive-state.json");
        var business = ReadStateSummary("business-brain-state.json");
        var cloud = ReadStateSummary("cloud-sync-state.json");
        var trust = ReadStateSummary("trust-safety-state.json");
        var governor = ReadStateSummary("runtime-governor-state.json");
        var workspace = ReadStateSummary("workspace-setup-state.json");
        var update = ReadStateSummary("update-state.json");
        var readiness = BuildControlPlaneReadiness(liveCabin, reader, cognitive, operating, memory, business, hands, input, trust, cloud);
        var conflicts = DetectControlPlaneConflicts(liveCabin, authority, reader).ToArray();
        var operationalStatus = conflicts.Length > 0
            ? "attention"
            : liveCabin.ReadyChannels == liveCabin.ExpectedChannels && liveCabin.ExpectedChannels > 0
                ? "ready"
                : liveCabin.ReadyChannels > 0 ? "degraded" : "attention";

        RuntimeCommandRecord? lastCommand;
        RuntimeStopCause stopCause;
        BootPhaseRecord[] bootPhases;
        RuntimeCommandRecord[] ledger;
        string runSessionId;
        lock (_gate)
        {
            lastCommand = _lastRuntimeCommand;
            stopCause = _lastStopCause;
            bootPhases = BootProtocolPhaseIds
                .Select(phaseId => _bootProtocol.TryGetValue(phaseId, out var record)
                    ? record
                    : new BootPhaseRecord(phaseId, "pending", "Pendiente.", now, null))
                .ToArray();
            ledger = _controlPlaneLedger.TakeLast(20).ToArray();
            runSessionId = CurrentRunSessionIdNoLock();
        }

        return new Dictionary<string, object?>
        {
            ["status"] = status,
            ["engine"] = "ariadgsm_control_plane",
            ["contract"] = "control_plane_state",
            ["architectureVersion"] = "final-ai-8-layers",
            ["authorityLayer"] = "Capa 2: AI Runtime Control Plane",
            ["phase"] = phase,
            ["summary"] = summary,
            ["source"] = source,
            ["updatedAt"] = now,
            ["version"] = CurrentVersion,
            ["runSessionId"] = runSessionId,
            ["isRunning"] = IsRunning,
            ["desiredRunning"] = _desiredRunning,
            ["operationalStatus"] = operationalStatus,
            ["lastStopCause"] = StopCauseToDictionary(stopCause),
            ["lastCommand"] = lastCommand is null ? null : CommandToDictionary(lastCommand),
            ["commandLedger"] = ledger.Select(CommandToDictionary).ToArray(),
            ["bootProtocol"] = new Dictionary<string, object?>
            {
                ["protocolVersion"] = "ai-runtime-control-plane-v1",
                ["currentPhase"] = phase,
                ["phases"] = bootPhases.Select(BootPhaseToDictionary).ToArray()
            },
            ["readiness"] = readiness,
            ["owners"] = new Dictionary<string, object?>
            {
                ["sessionOwner"] = "AI Runtime Control Plane",
                ["lifeController"] = "Control Plane only records lifecycle; it no longer owns session truth alone.",
                ["runtimeKernel"] = "Reports truth to Control Plane and cloud.",
                ["runtimeGovernor"] = "Owns AriadGSM child processes only.",
                ["workspaceGuardian"] = "Maintains cabin constraints under Control Plane session.",
                ["updater"] = "Runs only as a command with exact update cause.",
                ["ui"] = "Requests commands; does not own runtime session."
            },
            ["contracts"] = new Dictionary<string, object?>
            {
                ["windowControlOwner"] = "Cabin Reality Authority",
                ["mouseKeyboardOwner"] = "Input Arbiter",
                ["conversationOwner"] = "Reader Core",
                ["actionOwner"] = "Action Queue + Hands Verification",
                ["memoryOwner"] = "Living Memory + Business Brain",
                ["stateOwner"] = "AI Runtime Control Plane"
            },
            ["cabin"] = new Dictionary<string, object?>
            {
                ["status"] = liveCabin.Status,
                ["readyChannels"] = liveCabin.ReadyChannels,
                ["expectedChannels"] = liveCabin.ExpectedChannels,
                ["summary"] = liveCabin.Summary,
                ["channels"] = liveCabin.Channels
            },
            ["lifeController"] = ReadStateSummary("life-controller-state.json"),
            ["runtimeKernel"] = ReadStateSummary("runtime-kernel-state.json"),
            ["runtimeGovernor"] = governor,
            ["workspaceGuardian"] = authority,
            ["workspaceSetup"] = workspace,
            ["updater"] = update,
            ["hands"] = hands,
            ["input"] = input,
            ["reader"] = reader,
            ["memory"] = memory,
            ["operating"] = operating,
            ["cognitive"] = cognitive,
            ["businessBrain"] = business,
            ["cloudSync"] = cloud,
            ["conflicts"] = conflicts,
            ["humanReport"] = BuildControlPlaneHumanReport(status, phase, readiness, stopCause, conflicts, liveCabin)
        };
    }

    private Dictionary<string, object?> BuildControlPlaneReadiness(
        ControlPlaneCabinSnapshot cabin,
        Dictionary<string, object?> reader,
        Dictionary<string, object?> cognitive,
        Dictionary<string, object?> operating,
        Dictionary<string, object?> memory,
        Dictionary<string, object?> business,
        Dictionary<string, object?> hands,
        Dictionary<string, object?> input,
        Dictionary<string, object?> trust,
        Dictionary<string, object?> cloud)
    {
        var isRunning = IsRunning;
        var readerBlocked = StateStatusIs(reader, "blocked", "error");
        var thoughtBlocked = StateStatusIs(cognitive, "blocked", "error")
            || StateStatusIs(operating, "blocked", "error")
            || StateStatusIs(memory, "blocked", "error")
            || StateStatusIs(business, "blocked", "error");
        var inputPhase = input.TryGetValue("phase", out var inputPhaseValue) ? inputPhaseValue?.ToString() ?? string.Empty : string.Empty;
        var operatorHasPriority = inputPhase.Equals("operator_control", StringComparison.OrdinalIgnoreCase);
        var trustBlocked = StateStatusIs(trust, "blocked", "error");
        var handsBlocked = StateStatusIs(hands, "blocked", "error", "missing");
        var canRead = isRunning && cabin.ReadyChannels > 0 && !readerBlocked;
        var canThink = isRunning && canRead && !thoughtBlocked;
        var canAct = isRunning && canThink && !operatorHasPriority && !trustBlocked && !handsBlocked;
        var canSync = isRunning && canThink && !StateStatusIs(cloud, "blocked", "error");

        return new Dictionary<string, object?>
        {
            ["read"] = new Dictionary<string, object?>
            {
                ["ready"] = canRead,
                ["reason"] = !isRunning
                    ? "IA detenida."
                    : cabin.ReadyChannels == 0
                        ? "No hay WhatsApps listos para leer."
                        : readerBlocked
                            ? SummaryOrStatus(reader)
                            : $"{cabin.ReadyChannels}/{Math.Max(1, cabin.ExpectedChannels)} canales listos para leer."
            },
            ["think"] = new Dictionary<string, object?>
            {
                ["ready"] = canThink,
                ["reason"] = !canRead
                    ? "Pensamiento espera lectura util."
                    : thoughtBlocked
                        ? "Un motor mental reporto bloqueo o error."
                        : "Memoria, operativa y cerebro pueden procesar lo leido."
            },
            ["act"] = new Dictionary<string, object?>
            {
                ["ready"] = canAct,
                ["reason"] = operatorHasPriority
                    ? "Bryams tiene prioridad sobre mouse/teclado."
                    : trustBlocked
                        ? SummaryOrStatus(trust)
                        : handsBlocked
                            ? SummaryOrStatus(hands)
                            : canAct
                                ? "Manos pueden actuar con permisos y verificacion."
                                : "Manos esperan lectura/pensamiento listos."
            },
            ["sync"] = new Dictionary<string, object?>
            {
                ["ready"] = canSync,
                ["reason"] = canSync ? "Cloud Sync puede subir resumen seguro." : SummaryOrStatus(cloud)
            }
        };
    }

    private static bool StateStatusIs(Dictionary<string, object?> state, params string[] statuses)
    {
        var status = state.TryGetValue("status", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        return statuses.Any(item => status.Equals(item, StringComparison.OrdinalIgnoreCase));
    }

    private static string SummaryOrStatus(Dictionary<string, object?> state)
    {
        var summary = state.TryGetValue("summary", out var summaryValue) ? summaryValue?.ToString() ?? string.Empty : string.Empty;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        return state.TryGetValue("status", out var status) ? status?.ToString() ?? "Sin estado publicado." : "Sin estado publicado.";
    }

    private Dictionary<string, object?> BuildControlPlaneHumanReport(
        string status,
        string phase,
        Dictionary<string, object?> readiness,
        RuntimeStopCause stopCause,
        IReadOnlyList<string> conflicts,
        ControlPlaneCabinSnapshot cabin)
    {
        var readReady = NestedReady(readiness, "read");
        var thinkReady = NestedReady(readiness, "think");
        var actReady = NestedReady(readiness, "act");
        var headline = status switch
        {
            "starting" or "command" => "Estoy arrancando la IA con protocolo controlado",
            "running" => "IA encendida con sesion trazable",
            "stopping" => "Estoy apagando motores de forma ordenada",
            "stopped" => "IA detenida con causa registrada",
            "blocked" => "No encendi porque hay un bloqueo base",
            _ => "Control Plane vigilando la cabina"
        };

        return new Dictionary<string, object?>
        {
            ["headline"] = headline,
            ["queEstaPasando"] = new[]
            {
                $"Sesion: {CurrentRunSessionIdNoLock()}.",
                $"Fase actual: {phase}.",
                $"Read/Think/Act: leer={(readReady ? "listo" : "esperando")}, pensar={(thinkReady ? "listo" : "esperando")}, actuar={(actReady ? "listo" : "pausado")}."
            },
            ["queHice"] = new[]
            {
                "Unifique UI, Life Controller, Runtime Kernel, Runtime Governor, Workspace Guardian y Updater bajo una sesion.",
                "Separe lectura, pensamiento y manos para que un bloqueo de mouse no mate los ojos ni la memoria.",
                $"Cabina actual: {cabin.ReadyChannels}/{Math.Max(1, cabin.ExpectedChannels)} WhatsApps listos."
            },
            ["queNecesitoDeBryams"] = conflicts.Count > 0
                ? conflicts.ToArray()
                : Array.Empty<string>(),
            ["ultimoApagado"] = StopCauseToDictionary(stopCause)
        };
    }

    private static bool NestedReady(Dictionary<string, object?> readiness, string key)
    {
        return readiness.TryGetValue(key, out var value)
            && value is Dictionary<string, object?> nested
            && nested.TryGetValue("ready", out var ready)
            && ready is true;
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
                ["architectureVersion"] = "final-ai-8-layers",
                ["runSessionId"] = CurrentRunSessionIdNoLock(),
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

    private void WriteControlPlaneCheckpoint(string checkpointType, string checkpointId, string summary)
    {
        try
        {
            var entry = new Dictionary<string, object?>
            {
                ["checkpointType"] = checkpointType,
                ["checkpointId"] = checkpointId,
                ["runSessionId"] = CurrentRunSessionIdNoLock(),
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["summary"] = summary
            };
            AppendAllTextShared(ControlPlaneCheckpointFile, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private void AppendControlPlaneLedger(RuntimeCommandRecord command)
    {
        try
        {
            AppendAllTextShared(ControlPlaneCommandLedgerFile, JsonSerializer.Serialize(CommandToDictionary(command)) + Environment.NewLine);
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

    private static Dictionary<string, object?> CommandToDictionary(RuntimeCommandRecord command)
    {
        return new Dictionary<string, object?>
        {
            ["commandId"] = command.CommandId,
            ["commandType"] = command.CommandType,
            ["source"] = command.Source,
            ["reason"] = command.Reason,
            ["runSessionId"] = command.RunSessionId,
            ["status"] = command.Status,
            ["accepted"] = command.Accepted,
            ["result"] = command.Result,
            ["createdAt"] = command.CreatedAt,
            ["completedAt"] = command.CompletedAt,
            ["endsSession"] = command.EndsSession
        };
    }

    private static Dictionary<string, object?> BootPhaseToDictionary(BootPhaseRecord phase)
    {
        return new Dictionary<string, object?>
        {
            ["phaseId"] = phase.PhaseId,
            ["status"] = phase.Status,
            ["summary"] = phase.Summary,
            ["updatedAt"] = phase.UpdatedAt,
            ["completedAt"] = phase.CompletedAt
        };
    }

    private static Dictionary<string, object?> StopCauseToDictionary(RuntimeStopCause cause)
    {
        return new Dictionary<string, object?>
        {
            ["reason"] = cause.Reason,
            ["source"] = cause.Source,
            ["runSessionId"] = cause.RunSessionId,
            ["at"] = cause.At,
            ["detail"] = cause.Detail
        };
    }

    private static string HumanCommand(RuntimeCommandRecord command)
    {
        return command.CommandType switch
        {
            "start" => "encender IA",
            "start_engines" => "encender motores",
            "stop" => "pausar IA",
            "update" => "actualizar aplicacion",
            "dispose" => "liberar recursos",
            _ => command.CommandType
        };
    }

    private sealed record ControlPlaneCabinSnapshot(
        string Status,
        int ReadyChannels,
        int ExpectedChannels,
        string Summary,
        IReadOnlyList<Dictionary<string, object?>> Channels);

    private sealed record BootPhaseRecord(
        string PhaseId,
        string Status,
        string Summary,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? CompletedAt);

    private sealed record RuntimeStopCause(
        string Reason,
        string Source,
        string RunSessionId,
        DateTimeOffset At,
        string Detail);

    private sealed class RuntimeCommandRecord
    {
        public string CommandId { get; init; } = string.Empty;
        public string CommandType { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string RunSessionId { get; init; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Accepted { get; set; }
        public string Result { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? CompletedAt { get; set; }
        public bool EndsSession { get; init; }
    }
}
