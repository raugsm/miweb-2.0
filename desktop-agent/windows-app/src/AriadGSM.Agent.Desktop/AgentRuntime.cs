using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime : IDisposable
{
    private const string DefaultUpdateManifestUrl = "https://raw.githubusercontent.com/raugsm/miweb-2.0/main/desktop-agent/update/ariadgsm-update.json";
    private const int ShowWindowRestore = 9;
    private const int DwmwaCloaked = 14;
    private const long MaxLogBytes = 16L * 1024 * 1024;
    private const int MaxLogArchives = 5;
    private const int MaxRestartsPerWindow = 4;
    private const uint SetWindowPosShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly TimeSpan ToolResolveTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunningStateStaleAfter = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SupervisorInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RestartWindow = TimeSpan.FromMinutes(5);
    private readonly List<ManagedProcess> _processes = [];
    private readonly List<WorkerSpec> _workerSpecs = [];
    private readonly Dictionary<string, RestartTracker> _restartTrackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly string _repoRoot;
    private readonly string _desktopRoot;
    private readonly string _runtimeDir;
    private readonly string _logFile;
    private readonly string _updateStateFile;
    private readonly string _activeVersionFile;
    private readonly string _agentSupervisorStateFile;
    private readonly string _cabinReadinessFile;
    private readonly string _windowRealityStateFile;
    private readonly string _cabinManagerStateFile;
    private readonly string _cabinChannelRegistryFile;
    private readonly string _statusBusStateFile;
    private readonly string _workspaceSetupStateFile;
    private CancellationTokenSource? _coreLoopCts;
    private Task? _coreLoopTask;
    private CancellationTokenSource? _supervisorCts;
    private Task? _supervisorTask;
    private CancellationTokenSource? _workspaceGuardianCts;
    private Task? _workspaceGuardianTask;
    private bool _desiredRunning;
    private bool _stopping;
    private string? _cachedPython;
    private DateTimeOffset _pythonResolvedAt = DateTimeOffset.MinValue;
    private string? _cachedNode;
    private DateTimeOffset _nodeResolvedAt = DateTimeOffset.MinValue;
    private string? _cachedDotnet;
    private DateTimeOffset _dotnetResolvedAt = DateTimeOffset.MinValue;

    public AgentRuntime()
    {
        _repoRoot = LocateRepoRoot();
        _desktopRoot = Path.Combine(_repoRoot, "desktop-agent");
        _runtimeDir = Path.Combine(_desktopRoot, "runtime");
        Directory.CreateDirectory(_runtimeDir);
        _logFile = Path.Combine(_runtimeDir, "windows-app.log");
        _updateStateFile = Path.Combine(_runtimeDir, "update-state.json");
        _activeVersionFile = Path.Combine(_runtimeDir, "active-version.json");
        _agentSupervisorStateFile = Path.Combine(_runtimeDir, "agent-supervisor-state.json");
        _cabinReadinessFile = Path.Combine(_runtimeDir, "cabin-readiness.json");
        _windowRealityStateFile = Path.Combine(_runtimeDir, "window-reality-state.json");
        _cabinManagerStateFile = Path.Combine(_runtimeDir, "cabin-manager-state.json");
        _cabinChannelRegistryFile = Path.Combine(_runtimeDir, "cabin-channel-registry.json");
        _statusBusStateFile = Path.Combine(_runtimeDir, "status-bus-state.json");
        _workspaceSetupStateFile = Path.Combine(_runtimeDir, "workspace-setup-state.json");
        WriteArchitectureState();
        WriteLifeState("idle", "login_wait", "IA detenida esperando login e inicio manual.", "constructor");
        WriteCabinChannelRegistry(ReadChannelMappings());
        WriteStatusBusState("idle", "login_wait", "Cabina esperando login e inicio manual.", []);
        WriteRuntimeKernelState("constructor", "idle");
    }

    public string RepoRoot => _repoRoot;

    public string RuntimeDir => _runtimeDir;

    public string ExecutableDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string VersionSummary => ReadVersionSummary();

    public string CurrentVersion => ReadCurrentVersion();

    public string LogFile => _logFile;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _processes.Any(item => !item.Process.HasExited) || _coreLoopTask is { IsCompleted: false };
            }
        }
    }

    public event Action<string>? LogReceived;

    public async Task StartAsync()
    {
        var startCommand = EnsureStartSession("life_controller.start_async", "start_engines");
        MarkBootPhase("preflight", "running", "Revisando base local antes de encender motores.");
        WriteLifeState("starting", "preflight", "Revisando base local antes de encender motores.", "start");
        if (IsRunning)
        {
            WriteLog("Agent already running.");
            MarkBootPhase("readiness", "ok", "La IA ya estaba encendida; mantengo la misma sesion.");
            WriteLifeState("running", "already_running", "La IA ya estaba encendida.", "start");
            CompleteControlPlaneCommand(startCommand, "already_running", "La IA ya estaba encendida; no duplique motores.");
            return;
        }

        var report = Preflight();
        foreach (var item in report.Items.Where(item => item.Severity != HealthSeverity.Ok && item.Severity != HealthSeverity.Info))
        {
            WriteLog($"Preflight {item.Status}: {item.Name} - {item.Detail}");
        }

        if (report.HasBlockingErrors)
        {
            MarkBootPhase("preflight", "blocked", "No encendi motores porque el diagnostico base tiene errores.");
            WriteLifeState("blocked", "preflight_blocked", "No encendi motores porque el diagnostico base tiene errores.", "start");
            CompleteControlPlaneCommand(startCommand, "blocked", "Preflight bloqueo el encendido.", accepted: false);
            throw new InvalidOperationException("No puedo iniciar: hay errores base en el diagnostico previo.");
        }

        MarkBootPhase("preflight", "ok", "Diagnostico base aprobado.");
        Directory.CreateDirectory(_runtimeDir);
        WriteLog("Starting AriadGSM Agent without PowerShell.");
        StopExternalWorkerProcesses();
        MarkBootPhase("runtime_governor", "running", "Iniciando ownership de procesos AriadGSM.");
        StartRuntimeGovernor();
        MarkBootPhase("runtime_governor", "ok", "Runtime Governor activo para procesos propios AriadGSM.");
        lock (_gate)
        {
            _desiredRunning = true;
            _stopping = false;
            _workerSpecs.Clear();
            _restartTrackers.Clear();
        }

        StartWebPanel();
        MarkBootPhase("workspace_guardian", "running", "Activando guardian de cabina bajo la sesion actual.");
        StartWorkspaceGuardianLoop();
        MarkBootPhase("workspace_guardian", "ok", "Workspace Guardian activo.");
        MarkBootPhase("workers", "running", "Encendiendo ojos y preparando permisos frescos antes de manos.");
        WriteInputArbiterHeartbeatState("startup");
        await PrimeTrustSafetyAsync(CancellationToken.None).ConfigureAwait(false);
        StartWorker(
            "Vision",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "vision", "AriadGSM.Vision.Worker.exe"),
            Path.Combine("desktop-agent", "vision-engine", "src", "AriadGSM.Vision.Worker", "AriadGSM.Vision.Worker.csproj"),
            Path.Combine("desktop-agent", "vision-engine", "config", "vision.example.json"));
        StartWorker(
            "Perception",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "perception", "AriadGSM.Perception.Worker.exe"),
            Path.Combine("desktop-agent", "perception-engine", "src", "AriadGSM.Perception.Worker", "AriadGSM.Perception.Worker.csproj"),
            Path.Combine("desktop-agent", "perception-engine", "config", "perception.example.json"));
        StartWorker(
            "Interaction",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "interaction", "AriadGSM.Interaction.Worker.exe"),
            Path.Combine("desktop-agent", "interaction-engine", "src", "AriadGSM.Interaction.Worker", "AriadGSM.Interaction.Worker.csproj"),
            Path.Combine("desktop-agent", "interaction-engine", "config", "interaction.example.json"));
        StartWorker(
            "Orchestrator",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "orchestrator", "AriadGSM.Orchestrator.Worker.exe"),
            Path.Combine("desktop-agent", "orchestrator-engine", "src", "AriadGSM.Orchestrator.Worker", "AriadGSM.Orchestrator.Worker.csproj"),
            Path.Combine("desktop-agent", "orchestrator-engine", "config", "orchestrator.example.json"));
        MarkBootPhase("python_core", "running", "Encendiendo ciclo mental local.");
        StartCoreLoop();
        MarkBootPhase("python_core", "ok", "Python Core Loop solicitado.");
        StartWorker(
            "Hands",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("desktop-agent", "hands-engine", "src", "AriadGSM.Hands.Worker", "AriadGSM.Hands.Worker.csproj"),
            Path.Combine("desktop-agent", "hands-engine", "config", "hands.example.json"));
        MarkBootPhase("workers", "ok", "Workers solicitados y registrados por Runtime Governor.");
        MarkBootPhase("supervisor", "running", "Encendiendo supervisor de confiabilidad.");
        StartSupervisorLoop();
        MarkBootPhase("supervisor", "ok", "Supervisor de confiabilidad activo.");
        MarkBootPhase("readiness", "ready", "Lectura, pensamiento y accion quedan separados en Control Plane.");
        WriteLifeState("running", "engines_running", "Ojos, memoria, razonamiento y manos encendidos.", "start");
        CompleteControlPlaneCommand(startCommand, "running", "Motores encendidos bajo runSessionId.");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task RunOnceAsync()
    {
        var command = BeginControlPlaneCommand("run_once", "ui.read_once", "operator_requested_single_cycle");
        WriteLifeState("running", "single_cycle", "Ejecutando una lectura completa bajo demanda.", "run_once");
        WriteLog("Running one full read cycle.");
        await RunWorkerOnceAsync(
            "Vision once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "vision", "AriadGSM.Vision.Worker.exe"),
            Path.Combine("desktop-agent", "vision-engine", "src", "AriadGSM.Vision.Worker", "AriadGSM.Vision.Worker.csproj"),
            Path.Combine("desktop-agent", "vision-engine", "config", "vision.example.json")).ConfigureAwait(false);
        await RunWorkerOnceAsync(
            "Perception once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "perception", "AriadGSM.Perception.Worker.exe"),
            Path.Combine("desktop-agent", "perception-engine", "src", "AriadGSM.Perception.Worker", "AriadGSM.Perception.Worker.csproj"),
            Path.Combine("desktop-agent", "perception-engine", "config", "perception.example.json")).ConfigureAwait(false);
        await RunWorkerOnceAsync(
            "Interaction once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "interaction", "AriadGSM.Interaction.Worker.exe"),
            Path.Combine("desktop-agent", "interaction-engine", "src", "AriadGSM.Interaction.Worker", "AriadGSM.Interaction.Worker.csproj"),
            Path.Combine("desktop-agent", "interaction-engine", "config", "interaction.example.json")).ConfigureAwait(false);
        await RunWorkerOnceAsync(
            "Orchestrator once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "orchestrator", "AriadGSM.Orchestrator.Worker.exe"),
            Path.Combine("desktop-agent", "orchestrator-engine", "src", "AriadGSM.Orchestrator.Worker", "AriadGSM.Orchestrator.Worker.csproj"),
            Path.Combine("desktop-agent", "orchestrator-engine", "config", "orchestrator.example.json")).ConfigureAwait(false);
        await RunCoreSequenceAsync(CancellationToken.None).ConfigureAwait(false);
        await RunWorkerOnceAsync(
            "Hands once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("desktop-agent", "hands-engine", "src", "AriadGSM.Hands.Worker", "AriadGSM.Hands.Worker.csproj"),
            Path.Combine("desktop-agent", "hands-engine", "config", "hands.example.json")).ConfigureAwait(false);
        CompleteControlPlaneCommand(command, "completed", "Lectura bajo demanda finalizada.");
    }

    public void Stop(string reason = "operator_or_app_shutdown", string source = "unknown")
    {
        var stopCommand = BeginControlPlaneCommand("stop", source, reason, endsSession: true);
        RegisterControlPlaneStopCause(reason, source, $"Apagado solicitado desde {source}.");
        if (_stopping && !IsRunning)
        {
            WriteLifeState("stopped", "already_stopped", $"IA local ya estaba detenida: {reason}.", reason);
            CompleteControlPlaneCommand(stopCommand, "already_stopped", "La IA ya estaba detenida; causa registrada.");
            EndRunSession(reason, source);
            return;
        }

        WriteLog("Stopping AriadGSM Agent.");
        MarkBootPhase("readiness", "stopping", $"Apagando por {source}: {reason}.");
        WriteLifeState("stopping", "shutdown_requested", "Apagando motores locales de forma ordenada.", reason);
        _stopping = true;
        _desiredRunning = false;
        StopWorkspaceGuardianLoop();
        _supervisorCts?.Cancel();
        _coreLoopCts?.Cancel();
        lock (_gate)
        {
            foreach (var item in _processes.ToArray())
            {
                TryStop(item);
            }

            _processes.Clear();
            _workerSpecs.Clear();
        }

        StopRuntimeGovernor(reason);
        WriteSupervisorState("stopped", $"Agent stopped: {reason}.");
        WriteLifeState("stopped", "engines_stopped", $"IA local detenida: {reason}.", reason);
        CompleteControlPlaneCommand(stopCommand, "stopped", $"IA local detenida por {source}: {reason}.");
        EndRunSession(reason, source);
    }

    public AgentSnapshot Snapshot()
    {
        return new AgentSnapshot(
            ReadJsonStatus("vision-health.json"),
            ReadJsonStatus("perception-health.json"),
            ReadJsonStatus("interaction-state.json"),
            ReadJsonStatus("orchestrator-state.json"),
            ReadJsonStatus("timeline-state.json"),
            ReadJsonStatus("cognitive-state.json"),
            ReadJsonStatus("operating-state.json"),
            ReadJsonStatus("memory-state.json"),
            ReadJsonStatus("business-brain-state.json"),
            ReadJsonStatus("hands-state.json"),
            ReadJsonStatus("supervisor-state.json"),
            ReadJsonStatus("autonomous-cycle-state.json"),
            ReadJsonStatus("domain-events-state.json"),
            ActiveProcessSummary());
    }

    public PreflightReport Preflight()
    {
        var items = new List<HealthItem>
        {
            CheckAdministrator(),
            CheckRuntimeWritable(),
            CheckUpdaterReady(),
            CheckWebPanelDependency(),
            CheckPythonCore(),
            CheckWorkerReady(
                "Vision worker",
                Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "vision", "AriadGSM.Vision.Worker.exe"),
                Path.Combine("desktop-agent", "vision-engine", "src", "AriadGSM.Vision.Worker", "AriadGSM.Vision.Worker.csproj")),
            CheckWorkerReady(
                "Perception worker",
                Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "perception", "AriadGSM.Perception.Worker.exe"),
                Path.Combine("desktop-agent", "perception-engine", "src", "AriadGSM.Perception.Worker", "AriadGSM.Perception.Worker.csproj")),
            CheckWorkerReady(
                "Interaction worker",
                Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "interaction", "AriadGSM.Interaction.Worker.exe"),
                Path.Combine("desktop-agent", "interaction-engine", "src", "AriadGSM.Interaction.Worker", "AriadGSM.Interaction.Worker.csproj")),
            CheckWorkerReady(
                "Orchestrator worker",
                Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "orchestrator", "AriadGSM.Orchestrator.Worker.exe"),
                Path.Combine("desktop-agent", "orchestrator-engine", "src", "AriadGSM.Orchestrator.Worker", "AriadGSM.Orchestrator.Worker.csproj")),
            CheckWorkerReady(
                "Hands worker",
                Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "hands", "AriadGSM.Hands.Worker.exe"),
                Path.Combine("desktop-agent", "hands-engine", "src", "AriadGSM.Hands.Worker", "AriadGSM.Hands.Worker.csproj"))
        };

        items.AddRange(CheckWhatsAppChannels());
        return new PreflightReport(items);
    }

    public IReadOnlyList<HealthItem> Health()
    {
        var items = new List<HealthItem>
        {
            RuntimeKernelHealth(),
            StateHealth("Runtime Governor", "runtime-governor-state.json", "LifeController"),
            StateHealth("Vision", "vision-health.json", "Vision"),
            StateHealth("Perception", "perception-health.json", "Perception"),
            StateHealth("Reader Core", "reader-core-state.json", "PythonCoreLoop"),
            StateHealth("Event Backbone", "event-backbone-state.json", "PythonCoreLoop"),
            StateHealth("Reality Resolver", "window-reality-state.json", "PythonCoreLoop"),
            StateHealth("Support & Telemetry", "support-telemetry-state.json", "PythonCoreLoop"),
            StateHealth("Interaction", "interaction-state.json", "Interaction"),
            StateHealth("Orchestrator", "orchestrator-state.json", "Orchestrator"),
            StateHealth("Timeline", "timeline-state.json", "PythonCoreLoop"),
            StateHealth("Cognitive", "cognitive-state.json", "PythonCoreLoop"),
            StateHealth("Operating", "operating-state.json", "PythonCoreLoop"),
            StateHealth("Case Manager", "case-manager-state.json", "PythonCoreLoop"),
            StateHealth("Channel Routing", "channel-routing-state.json", "PythonCoreLoop"),
            StateHealth("Accounting Core", "accounting-core-state.json", "PythonCoreLoop"),
            StateHealth("Memory", "memory-state.json", "PythonCoreLoop"),
            StateHealth("Business Brain", "business-brain-state.json", "PythonCoreLoop"),
            StateHealth("Tool Registry", "tool-registry-state.json", "PythonCoreLoop"),
            StateHealth("Cloud Sync", "cloud-sync-state.json", "PythonCoreLoop"),
            StateHealth("Evaluation + Release", "evaluation-release-state.json", "PythonCoreLoop"),
            StateHealth("Domain Events", "domain-events-state.json", "PythonCoreLoop"),
            StateHealth("Trust & Safety", "trust-safety-state.json", "PythonCoreLoop"),
            StateHealth("Hands", "hands-state.json", "Hands"),
            StateHealth("Action Queue", "action-queue-state.json", "Hands"),
            StateHealth("Input Arbiter", "input-arbiter-state.json", "Hands"),
            StateHealth("Supervisor", "supervisor-state.json", "PythonCoreLoop"),
            StateHealth("Ciclo autonomo", "autonomous-cycle-state.json", "PythonCoreLoop"),
            StateHealth("Control Plane", "control-plane-state.json", "LifeController"),
            StateHealth("Life Controller", "life-controller-state.json", "LifeController"),
            StateHealth("Agent Supervisor", "agent-supervisor-state.json", "ReliabilitySupervisor"),
            StateHealth("Status Bus", "status-bus-state.json", "StatusBus"),
            StateHealth("Cabin Manager", "cabin-manager-state.json", "CabinManager"),
            StateHealth("Alistamiento cabina", "workspace-setup-state.json", "WorkspaceSetup"),
            StateHealth("Autoridad de cabina", "cabin-authority-state.json", "WorkspaceGuardian"),
            CabinReadinessHealth(),
            UpdateHealth(),
            WebPanelHealth(),
            CoreLoopHealth()
        };

        return items;
    }

    public IReadOnlyList<string> ActiveProcessSummary()
    {
        lock (_gate)
        {
            return _processes
                .Where(item => !item.Process.HasExited)
                .Select(item => $"{item.Name} #{item.Process.Id}")
                .Concat(_coreLoopTask is { IsCompleted: false } ? ["PythonCoreLoop"] : [])
                .Concat(_supervisorTask is { IsCompleted: false } ? ["ReliabilitySupervisor"] : [])
                .Concat(_workspaceGuardianTask is { IsCompleted: false } ? ["WorkspaceGuardian"] : [])
                .ToArray();
        }
    }

    public IReadOnlyList<string> RecentProblemLines(int maxLines)
    {
        if (!File.Exists(_logFile))
        {
            return [];
        }

        try
        {
            return ReadTailLinesShared(_logFile, 512 * 1024)
                .Where(ContainsProblemSignal)
                .TakeLast(maxLines)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<string> OperationalActivityLines(
        PreflightReport preflight,
        IReadOnlyList<HealthItem> health,
        IReadOnlyList<string> recentLogLines)
    {
        using var vision = ReadJsonStatus("vision-health.json");
        using var runtimeKernel = ReadJsonStatus("runtime-kernel-state.json");
        using var runtimeGovernor = ReadJsonStatus("runtime-governor-state.json");
        using var perception = ReadJsonStatus("perception-health.json");
        using var readerCore = ReadJsonStatus("reader-core-state.json");
        using var eventBackbone = ReadJsonStatus("event-backbone-state.json");
        using var windowReality = ReadJsonStatus("window-reality-state.json");
        using var supportTelemetry = ReadJsonStatus("support-telemetry-state.json");
        using var interaction = ReadJsonStatus("interaction-state.json");
        using var orchestrator = ReadJsonStatus("orchestrator-state.json");
        using var timeline = ReadJsonStatus("timeline-state.json");
        using var cognitive = ReadJsonStatus("cognitive-state.json");
        using var operating = ReadJsonStatus("operating-state.json");
        using var caseManager = ReadJsonStatus("case-manager-state.json");
        using var channelRouting = ReadJsonStatus("channel-routing-state.json");
        using var accountingCore = ReadJsonStatus("accounting-core-state.json");
        using var memory = ReadJsonStatus("memory-state.json");
        using var businessBrain = ReadJsonStatus("business-brain-state.json");
        using var toolRegistry = ReadJsonStatus("tool-registry-state.json");
        using var cloudSync = ReadJsonStatus("cloud-sync-state.json");
        using var hands = ReadJsonStatus("hands-state.json");
        using var inputArbiter = ReadJsonStatus("input-arbiter-state.json");
        using var supervisor = ReadJsonStatus("supervisor-state.json");
        using var autonomousCycle = ReadJsonStatus("autonomous-cycle-state.json");
        using var life = ReadJsonStatus("life-controller-state.json");
        using var agentSupervisor = ReadJsonStatus("agent-supervisor-state.json");
        using var workspaceSetup = ReadJsonStatus("workspace-setup-state.json");
        using var statusBus = ReadJsonStatus("status-bus-state.json");
        using var cabinManager = ReadJsonStatus("cabin-manager-state.json");
        using var cabinAuthority = ReadJsonStatus("cabin-authority-state.json");

        var whatsappSummary = preflight.Items
            .Where(item => item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase))
            .Select(item => $"{item.Name.Replace("WhatsApp ", string.Empty, StringComparison.OrdinalIgnoreCase)}={item.Status.ToLowerInvariant()}")
            .ToArray();
        var active = ActiveProcessSummary();
        var blockers = health
            .Where(item => item.Severity is HealthSeverity.Error or HealthSeverity.Warning)
            .Take(4)
            .Select(item => $"{item.Name}: {item.Detail}")
            .ToArray();

        var lines = new List<string>
        {
            "Esta zona resume el trabajo real. Los JSON y trazas largas quedan en Logs tecnicos.",
            $"Runtime Kernel: {Text(runtimeKernel, "status")} | {RuntimeKernelAuthorityText(runtimeKernel)}",
            $"Runtime Governor: {Text(runtimeGovernor, "status")} | propios vivos={NestedNumber(runtimeGovernor, "summary", "runningOwned")} | apagado verificado={NestedBool(runtimeGovernor, "summary", "verifiedStopped")}",
            $"Modo: {(IsRunning ? "trabajando" : "detenido")} | Procesos: {(active.Count == 0 ? "ninguno" : string.Join(", ", active))}",
            $"WhatsApps: {(whatsappSummary.Length == 0 ? "sin revision" : string.Join(" | ", whatsappSummary))}",
            $"Vision: capturas={Number(vision, "framesCaptured", "eventsWritten")} | ventanas={Number(vision, "visibleWindowCount")} | intervalo={Number(vision, "captureIntervalMs")}ms",
            $"Reader Core: nuevos={NestedNumber(readerCore, "ingested", "newMessages")} | rechazados={NestedNumber(readerCore, "ingested", "rejected")} | bytes={NestedNumber(readerCore, "ingested", "sourceBytesRead")} | ciclo={NestedNumber(readerCore, "ingested", "cycleDurationMs")}ms",
            $"Event Backbone: bytes={DeepNumber(eventBackbone, "latestBatch", "summary", "bytesRead")} | backlog={DeepNumber(eventBackbone, "latestBatch", "summary", "backlogBytes")} | saltado={DeepNumber(eventBackbone, "latestBatch", "summary", "skippedBacklogBytes")} | modo={DeepText(eventBackbone, "latestBatch", "mode")}",
            $"Reality Resolver: {Text(windowReality, "status")} | operables={NestedNumber(windowReality, "summary", "operationalChannels")}/{NestedNumber(windowReality, "summary", "expectedChannels")} | conflictos={NestedNumber(windowReality, "summary", "conflictedChannels")} | viejo={NestedNumber(windowReality, "summary", "staleInputs")}",
            $"Support: {Text(supportTelemetry, "status")} | incidentes={NestedNumber(supportTelemetry, "summary", "incidentsOpen")} | caja negra={NestedNumber(supportTelemetry, "summary", "blackboxEventsRetained")} | bundle={NestedBool(supportTelemetry, "summary", "bundleReady")}",
            $"Perception OCR: mensajes={Number(perception, "messagesExtracted")} | conversaciones={Number(perception, "conversationEventsWritten")} | reader={Text(perception, "lastReaderStatus")}",
            $"Interaction: objetivos={Number(interaction, "targetsObserved")} | accionables={Number(interaction, "actionableTargets")} | rechazados={Number(interaction, "targetsRejected")} | mejor={Text(interaction, "lastAcceptedTargetTitle")}",
            $"Orchestrator: fase={Text(orchestrator, "phase")} | {Text(orchestrator, "summary")}",
            $"Life Controller: {Text(life, "phase")} | {Text(life, "summary")}",
            $"Status Bus: {Text(statusBus, "phase")} | {Text(statusBus, "summary")}",
            $"Cabin Manager: {Text(cabinManager, "phase")} | {Text(cabinManager, "summary")}",
            $"Alistamiento: fase={Text(workspaceSetup, "phase")} | {Text(workspaceSetup, "summary")}",
            $"Autoridad de cabina: {Text(cabinAuthority, "status")} | {Text(cabinAuthority, "summary")}",
            $"Timeline: mensajes unidos={NestedNumber(timeline, "ingested", "messages")} | historias={NestedNumber(timeline, "ingested", "timelines")} | durable={NestedNumber(timeline, "durable", "storedMessages")}",
            $"Cognitive/Memory: decisiones={NestedNumber(cognitive, "summary", "decisions")} | memoria={NestedNumber(memory, "summary", "memoryMessages")} | aprendizaje={NestedNumber(memory, "summary", "learningEvents")}",
            $"Operating/Contabilidad: casos={NestedNumber(operating, "summary", "cases")} | tareas={NestedNumber(operating, "summary", "openTasks")} | borradores contables={NestedNumber(operating, "summary", "accountingDrafts")}",
            $"Case Manager: abiertos={NestedNumber(caseManager, "summary", "openCases")} | humano={NestedNumber(caseManager, "summary", "needsHuman")} | eventos={NestedNumber(caseManager, "summary", "emittedCaseEvents")}",
            $"Channel Routing: propuestas={NestedNumber(channelRouting, "summary", "proposedRoutes")} | aprobadas={NestedNumber(channelRouting, "summary", "approvedRoutes")} | humano={NestedNumber(channelRouting, "summary", "needsHuman")}",
            $"Accounting Core: registros={NestedNumber(accountingCore, "summary", "accountingRecords")} | confirmados={NestedNumber(accountingCore, "summary", "confirmedRecords")} | falta evidencia={NestedNumber(accountingCore, "summary", "needsEvidence")}",
            $"Business Brain: propuestas={NestedNumber(businessBrain, "summary", "recommendations")} | humano={NestedNumber(businessBrain, "summary", "requiresHuman")} | memoria={NestedNumber(businessBrain, "summary", "memoryItemsRead")}",
            $"Tool Registry: herramientas={NestedNumber(toolRegistry, "summary", "toolsRegistered")} | capacidades={NestedNumber(toolRegistry, "summary", "capabilitiesRegistered")} | planes={NestedNumber(toolRegistry, "summary", "matchedRequests")} | humano={NestedNumber(toolRegistry, "summary", "plansNeedHuman")}",
            $"Cloud Sync: estado={Text(cloudSync, "status")} | eventos={NestedNumber(cloudSync, "summary", "eventsPrepared")} | mensajes={NestedNumber(cloudSync, "summary", "messagesPrepared")} | nube={Text(cloudSync, "endpoint")}",
            $"Input Arbiter: {Text(inputArbiter, "phase")} | idle={Number(inputArbiter, "operatorIdleMs")}ms | {Text(inputArbiter, "summary")}",
            $"Hands: ejecutadas={Number(hands, "actionsExecuted")} | verificadas={Number(hands, "actionsVerified")} | bloqueadas={Number(hands, "actionsBlocked")} | ultimo={Text(hands, "lastSummary")}",
            $"Supervisor: hallazgos={NestedNumber(supervisor, "summary", "findings")} | requiere humano={NestedNumber(supervisor, "summary", "requiresHumanConfirmation")} | bloqueadas={NestedNumber(supervisor, "summary", "blocked")}",
            $"Ciclo autonomo: fase={Text(autonomousCycle, "phase")} | {Text(autonomousCycle, "summary")}",
            $"Confiabilidad: {Text(agentSupervisor, "status")} | reinicios={Number(agentSupervisor, "restartCount")} | {Text(agentSupervisor, "lastSummary")}"
        };

        if (File.Exists(_cabinReadinessFile))
        {
            using var cabin = ReadJsonStatus("cabin-readiness.json");
            lines.Insert(4, $"Cabina: {Text(cabin, "status")} | {Text(cabin, "summary")}");
        }

        if (blockers.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Atencion operativa:");
            lines.AddRange(blockers.Select(item => $"- {item}"));
        }

        var usefulLogs = recentLogLines
            .Where(line => !LooksLikeRawJson(line))
            .TakeLast(8)
            .ToArray();
        if (usefulLogs.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Ultimos movimientos:");
            lines.AddRange(usefulLogs.Select(line => $"- {line}"));
        }

        return lines;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = ReadCurrentVersion();
        var manifestSource = ResolveUpdateManifestSource();
        MarkBootPhase("update_check", "running", "Revisando si hay una version nueva antes de encender IA.");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await ReadTextAsync(manifestSource, client, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var latestVersion = TryString(root, "version", "latestVersion") ?? currentVersion;
            var packageUrl = TryString(root, "packageUrl", "url") ?? string.Empty;
            var sha256 = TryString(root, "sha256", "hash") ?? string.Empty;
            var autoApply = TryBool(root, "autoApply") ?? false;
            var available = CompareVersions(latestVersion, currentVersion) > 0;
            var result = new UpdateCheckResult(
                available,
                autoApply,
                currentVersion,
                latestVersion,
                packageUrl,
                sha256,
                manifestSource,
                available
                    ? $"Version {latestVersion} disponible."
                    : $"Version {currentVersion} al dia.");
            WriteUpdateState(result, available ? "available" : "current");
            MarkBootPhase("update_check", available ? "attention" : "ok", result.Detail);
            return result;
        }
        catch (Exception exception)
        {
            var result = new UpdateCheckResult(
                false,
                false,
                currentVersion,
                currentVersion,
                string.Empty,
                string.Empty,
                manifestSource,
                $"No pude revisar actualizaciones: {exception.Message}");
            WriteUpdateState(result, "unavailable");
            WriteLog(result.Detail);
            MarkBootPhase("update_check", "attention", result.Detail);
            return result;
        }
    }

    public bool TryLaunchUpdater(UpdateCheckResult update)
    {
        var command = BeginControlPlaneCommand("update", "runtime.updater", $"auto_apply={update.AutoApply}; latest={update.LatestVersion}");
        if (!update.Available)
        {
            CompleteControlPlaneCommand(command, "skipped", "No hay actualizacion disponible.");
            return false;
        }

        if (!update.AutoApply)
        {
            WriteLog($"Update available but not auto-applied: {update.LatestVersion}.");
            CompleteControlPlaneCommand(command, "skipped", "Hay actualizacion, pero autoApply esta desactivado.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(update.PackageUrl))
        {
            WriteLog("Update available but packageUrl is empty.");
            CompleteControlPlaneCommand(command, "blocked", "Hay actualizacion, pero packageUrl esta vacio.", accepted: false);
            return false;
        }

        var updaterExe = ResolveUpdaterExe();
        if (updaterExe is null)
        {
            WriteLog("Updater executable not found.");
            CompleteControlPlaneCommand(command, "blocked", "No encontre AriadGSM Updater.", accepted: false);
            return false;
        }

        var runnerDir = Path.Combine(_runtimeDir, "updater-runner");
        if (Directory.Exists(runnerDir))
        {
            Directory.Delete(runnerDir, recursive: true);
        }

        CopyDirectory(Path.GetDirectoryName(updaterExe)!, runnerDir, overwrite: true);
        var runnerExe = Path.Combine(runnerDir, Path.GetFileName(updaterExe));
        var currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AriadGSM Agent.exe");
        var restartExe = ResolveLauncherExe() ?? currentExe;

        var startInfo = new ProcessStartInfo
        {
            FileName = runnerExe,
            WorkingDirectory = runnerDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--apply");
        startInfo.ArgumentList.Add("--current-dir");
        startInfo.ArgumentList.Add(AppContext.BaseDirectory);
        startInfo.ArgumentList.Add("--install-root");
        startInfo.ArgumentList.Add(_desktopRoot);
        startInfo.ArgumentList.Add("--version");
        startInfo.ArgumentList.Add(update.LatestVersion);
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(update.PackageUrl);
        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            startInfo.ArgumentList.Add("--sha256");
            startInfo.ArgumentList.Add(update.Sha256);
        }

        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(restartExe);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--state");
        startInfo.ArgumentList.Add(_updateStateFile);

        Process.Start(startInfo);
        RegisterControlPlaneStopCause("update", "runtime.updater", $"Updater iniciado para version {update.LatestVersion}.");
        MarkBootPhase("update_check", "ok", $"Updater iniciado para version {update.LatestVersion}.");
        WriteLifeState("updating", "updater_launched", $"Actualizador iniciado para version {update.LatestVersion}.", "update");
        CompleteControlPlaneCommand(command, "launched", $"Updater iniciado para version {update.LatestVersion}.");
        WriteLog($"Updater launched for version {update.LatestVersion}.");
        return true;
    }

    public string WriteDiagnosticReport()
    {
        var path = Path.Combine(_runtimeDir, "diagnostic-latest.txt");
        var lines = new List<string>
        {
            "AriadGSM Agent Desktop - Diagnostico",
            $"Fecha local: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Repo: {_repoRoot}",
            $"Runtime: {_runtimeDir}",
            $"Log: {_logFile}",
            $"Estado general: {(IsRunning ? "TRABAJANDO" : "DETENIDO")}",
            string.Empty,
            "== Diagnostico previo =="
        };

        foreach (var item in Preflight().Items)
        {
            lines.Add(FormatHealthLine(item));
        }

        lines.Add(string.Empty);
        lines.Add("== Salud de motores ==");
        foreach (var item in Health())
        {
            lines.Add(FormatHealthLine(item));
        }

        lines.Add(string.Empty);
        lines.Add("== Actualizaciones ==");
        lines.Add(FormatHealthLine(UpdateHealth()));

        lines.Add(string.Empty);
        lines.Add("== Procesos activos ==");
        var processes = ActiveProcessSummary();
        lines.AddRange(processes.Count == 0 ? ["ninguno"] : processes);

        lines.Add(string.Empty);
        lines.Add("== Ventanas visibles ==");
        foreach (var window in VisibleWindows())
        {
            lines.Add($"{window.ProcessName} #{window.ProcessId} | {window.Title}");
        }

        lines.Add(string.Empty);
        lines.Add("== Ultimas pistas de log ==");
        var problemLines = RecentProblemLines(40);
        lines.AddRange(problemLines.Count == 0 ? ["sin errores recientes"] : problemLines);

        File.WriteAllLines(path, lines, Encoding.UTF8);
        WriteLog($"Diagnostic report written: {path}");
        return path;
    }

    public void OpenMissingWhatsApps()
    {
        var windows = VisibleWindows();
        var readiness = ReadChannelMappings()
            .Select(mapping => EvaluateChannelReadiness(mapping, windows))
            .ToArray();
        WriteCabinManagerState("preparing", "manual_open_missing", readiness, CabinSummary(readiness));
        var opened = 0;
        foreach (var item in readiness)
        {
            var mapping = item.Mapping;
            if (item.IsReady)
            {
                WriteLog($"{mapping.ChannelId}: WhatsApp already visible in {mapping.BrowserProcess}.");
                continue;
            }

            if (item.RequiresHuman && !item.CanLaunch)
            {
                WriteLog($"{mapping.ChannelId}: cabin needs human action before opening more windows: {item.Detail}");
                continue;
            }

            if (!item.CanLaunch)
            {
                WriteLog($"{mapping.ChannelId}: not launching because channel is {item.Status}: {item.Detail}");
                continue;
            }

            if (LaunchWhatsAppWindow(mapping, item.Detail))
            {
                opened++;
            }
        }

        if (opened == 0)
        {
            WriteLog("No missing WhatsApp windows were opened.");
        }
    }

    public async Task<PreflightReport> PrepareWhatsAppWorkspaceAsync(TimeSpan timeout, IProgress<CabinSetupProgress>? progress = null)
    {
        var started = DateTimeOffset.Now;
        var deadline = DateTimeOffset.Now + timeout;
        var launchAttempts = ReadChannelMappings()
            .ToDictionary(item => item.ChannelId, _ => 0, StringComparer.OrdinalIgnoreCase);
        var arrangedOnce = false;
        WriteLog("Cabin setup orchestrator: preparing WhatsApp 1/2/3.");
        WriteStatusBusState("preparing", "observe_channels", "Revisando Edge, Chrome y Firefox antes de mover algo.", []);
        progress?.Report(new CabinSetupProgress(
            8,
            "observe_channels",
            "Revisando Edge, Chrome y Firefox antes de mover algo.",
            ReadChannelMappings()
                .Select(mapping => new CabinSetupChannelProgress(mapping.ChannelId, mapping.BrowserProcess, "SEARCHING_EXISTING_SESSION", "Pendiente de revision."))
                .ToArray(),
            false,
            false));

        while (DateTimeOffset.Now < deadline)
        {
            var windows = VisibleWindows();
            var readiness = ReadChannelMappings()
                .Select(mapping => EvaluateChannelReadiness(mapping, windows))
                .ToArray();
            PublishCabinSetup(progress, "preparing", "classify_channels", 20, readiness, CabinSummary(readiness));

            if (readiness.All(item => item.IsReady))
            {
                if (!arrangedOnce)
                {
                    arrangedOnce = true;
                    PublishCabinSetup(progress, "preparing", "arrange_windows", 82, readiness, "Los 3 WhatsApps estan vivos. Estoy acomodando la cabina una sola vez.");
                    ArrangeWhatsAppWorkspace();
                }

                var arrangedReadiness = ReadChannelMappings()
                    .Select(mapping => EvaluateChannelReadiness(mapping, VisibleWindows()))
                    .ToArray();
                PublishCabinSetup(progress, "ready", "ready", 100, arrangedReadiness, "Cabina lista: puedes encender la IA.");
                WriteLog("Cabin setup orchestrator: all WhatsApp channels are visible and arranged.");
                return Preflight();
            }

            if (!arrangedOnce && readiness.Any(item => item.Status.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase)))
            {
                arrangedOnce = true;
                PublishCabinSetup(progress, "preparing", "arrange_windows", 48, readiness, "Detecte ventanas superpuestas. Acomodo la cabina una vez y vuelvo a validar.");
                ArrangeWhatsAppWorkspace();
                await Task.Delay(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                continue;
            }

            var locatedExisting = false;
            foreach (var item in readiness.Where(item => !item.IsReady && !item.RequiresHuman))
            {
                if (TryLocateExistingWhatsAppSession(item.Mapping, windows))
                {
                    locatedExisting = true;
                }
            }

            if (locatedExisting)
            {
                PublishCabinSetup(progress, "preparing", "locate_existing_session", 44, readiness, "Encontre una sesion de WhatsApp abierta; la estoy trayendo al frente.");
                await Task.Delay(TimeSpan.FromMilliseconds(900)).ConfigureAwait(false);
                continue;
            }

            var blocked = readiness.Where(item => item.RequiresHuman).ToArray();
            if (blocked.Length > 0 && !CanStartWithDegradedCabin(readiness))
            {
                PublishCabinSetup(progress, "attention", "human_required", 100, readiness, "Cabina bloqueada: necesito tu ayuda antes de encender IA.");
                foreach (var item in blocked)
                {
                    WriteLog($"{item.ChannelId}: cabin blocked by {item.Status}. {item.Detail}");
                }

                return Preflight();
            }

            if (blocked.Length > 0 && CanStartWithDegradedCabin(readiness))
            {
                PublishCabinSetup(progress, "degraded", "human_required_degraded", 100, readiness, "Cabina parcial: hay canales listos, pero uno necesita tu ayuda.");
                foreach (var item in blocked)
                {
                    WriteLog($"{item.ChannelId}: cabin degraded by {item.Status}. {item.Detail}");
                }

                return Preflight();
            }

            if (DateTimeOffset.Now - started < TimeSpan.FromSeconds(8))
            {
                PublishCabinSetup(progress, "preparing", "locate_existing_session", 34, readiness, "Sigo buscando WhatsApp ya abierto antes de crear sesiones nuevas.");
                await Task.Delay(TimeSpan.FromMilliseconds(900)).ConfigureAwait(false);
                continue;
            }

            var launchable = readiness.Where(item => item.CanLaunch).ToArray();
            if (launchable.Length > 0)
            {
                PublishCabinSetup(progress, "preparing", "open_missing_channels", 58, readiness, "No encontre todos los canales abiertos. Abrire solo los WhatsApp que faltan.");
            }

            foreach (var item in launchable)
            {
                if (!launchAttempts.TryGetValue(item.ChannelId, out var attempts))
                {
                    attempts = 0;
                }

                if (attempts >= 1)
                {
                    WriteLog($"{item.ChannelId}: WhatsApp Web already launched once; waiting instead of opening more windows. {item.Detail}");
                    continue;
                }

                if (LaunchWhatsAppWindow(item.Mapping, item.Detail))
                {
                    launchAttempts[item.ChannelId] = attempts + 1;
                }
            }

            if (CanStartWithDegradedCabin(readiness)
                && readiness.Any(item => !item.IsReady)
                && DateTimeOffset.Now - started > TimeSpan.FromSeconds(26))
            {
                PublishCabinSetup(progress, "degraded", "partial_ready", 100, readiness, "Cabina parcial: puedes encender IA con canales listos o revisar el faltante.");
                WriteLog("Cabin setup orchestrator: partial cabin ready after waiting for missing channels.");
                return Preflight();
            }

            PublishCabinSetup(progress, "preparing", "validate_channels", 72, readiness, "Validando que los canales abiertos ya muestren WhatsApp utilizable.");
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }

        progress?.Report(new CabinSetupProgress(
            88,
            "arrange_windows",
            "Tiempo de busqueda terminado. Estoy acomodando lo que encontre.",
            [],
            false,
            false));
        if (!arrangedOnce)
        {
            ArrangeWhatsAppWorkspace();
        }

        var finalReport = Preflight();
        var stillMissing = finalReport.Items
            .Where(item => item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase)
                && item.Severity == HealthSeverity.Warning)
            .Select(item => item.Name)
            .ToArray();
        var finalReadiness = ReadChannelMappings()
            .Select(mapping => EvaluateChannelReadiness(mapping, VisibleWindows()))
            .ToArray();
        var finalStatus = finalReadiness.All(item => item.IsReady)
            ? "ready"
            : CanStartWithDegradedCabin(finalReadiness)
                ? "degraded"
                : "attention";
        PublishCabinSetup(
            progress,
            finalStatus,
            finalStatus,
            100,
            finalReadiness,
            finalStatus.Equals("ready", StringComparison.OrdinalIgnoreCase)
                ? "Cabina lista: puedes encender la IA."
                : finalStatus.Equals("degraded", StringComparison.OrdinalIgnoreCase)
                    ? "Cabina parcial: puedes encender IA con canales listos o revisar el faltante."
                    : "Cabina bloqueada: necesito tu ayuda antes de encender IA.");
        WriteLog(stillMissing.Length == 0
            ? "Cabin setup orchestrator: WhatsApp workspace ready after wait."
            : $"Cabin setup orchestrator: partial/attention after wait: {string.Join(", ", stillMissing)}.");
        return finalReport;
    }

    public async Task<PreflightReport> BootstrapAutonomousWorkspaceAsync(TimeSpan timeout)
    {
        MarkBootPhase("workspace_bootstrap", "running", "Preparando WhatsApp 1/2/3 antes de encender IA.");
        WriteLifeState("starting", "workspace_bootstrap", "Preparando WhatsApp 1/2/3 antes de encender IA.", "bootstrap");
        WriteWorkspaceSetupState(
            "preparing",
            "Ordenando la cabina antes de encender IA.",
            10,
            []);
        WriteLog("Workspace setup: first arrange any visible WhatsApp windows before reading.");
        ArrangeWhatsAppWorkspace();
        await Task.Delay(TimeSpan.FromMilliseconds(450)).ConfigureAwait(false);

        WriteWorkspaceSetupState(
            "preparing",
            "Abriendo o recuperando WhatsApp 1/2/3.",
            35,
            []);
        var report = await PrepareWhatsAppWorkspaceAsync(timeout).ConfigureAwait(false);

        WriteWorkspaceSetupState(
            "validating",
            "Validando que los 3 canales quedaron visibles y alineados.",
            70,
            []);
        ArrangeWhatsAppWorkspace();
        WriteWorkspaceGuardianState(EnsureWorkspaceOwned("bootstrap"));
        await Task.Delay(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false);

        var finalReadiness = ReadChannelMappings()
            .Select(mapping => EvaluateChannelReadiness(mapping, VisibleWindows()))
            .ToArray();
        var readyChannels = finalReadiness.Count(item => item.IsReady);
        var cabinStatus = finalReadiness.All(item => item.IsReady)
            ? "ready"
            : CanStartWithDegradedCabin(finalReadiness)
                ? "degraded"
                : "attention";
        WriteCabinReadinessState(cabinStatus, finalReadiness);
        WriteCabinManagerState(cabinStatus, cabinStatus, finalReadiness, CabinSummary(finalReadiness));

        var blockers = finalReadiness
            .Where(item => !item.IsReady)
            .Select(item => $"{item.ChannelId}: {item.Status} - {item.Detail}")
            .ToArray();

        if (readyChannels == 0)
        {
            MarkBootPhase("workspace_bootstrap", "blocked", "La cabina necesita revision humana antes de encender IA.");
            WriteLifeState("attention", "workspace_attention", "La IA no arranco porque la cabina necesita revision humana.", "bootstrap");
            WriteWorkspaceSetupState(
                "attention",
                "No encendi ojos ni manos porque la cabina no quedo lista.",
                100,
                blockers);
            WriteLog($"Workspace setup: blocked before IA start: {string.Join(" | ", blockers)}");
            return Preflight();
        }

        if (blockers.Length > 0)
        {
            MarkBootPhase("workspace_bootstrap", "attention", $"Cabina parcial: {readyChannels}/3 WhatsApps listos.");
            WriteWorkspaceSetupState(
                "degraded",
                $"Arranco en modo degradado: {readyChannels}/3 WhatsApps listos. Sigo trabajando con canales disponibles.",
                100,
                blockers);
            WriteLifeState("ready", "workspace_ready_degraded", $"Cabina parcial lista: {readyChannels}/3 canales. Motores pueden iniciar en modo degradado.", "bootstrap");
            WriteStatusBusState("degraded", "workspace_ready_degraded", $"IA iniciara con {readyChannels}/3 WhatsApps; canales faltantes quedan pausados.", blockers);
            WriteLog($"Workspace setup: degraded start allowed: {string.Join(" | ", blockers)}");
            return report;
        }

        WriteWorkspaceSetupState(
            "ready",
            "Cabina lista: Edge=WhatsApp 1, Chrome=WhatsApp 2, Firefox=WhatsApp 3.",
            100,
            []);
        MarkBootPhase("workspace_bootstrap", "ok", "Cabina lista: Edge=WhatsApp 1, Chrome=WhatsApp 2, Firefox=WhatsApp 3.");
        WriteLifeState("ready", "workspace_ready", "Cabina lista para encender motores autonomos.", "bootstrap");
        WriteStatusBusState("ready", "workspace_ready", "Cabina lista: los 3 WhatsApp pueden leer y aprender.", []);
        WriteLog("Workspace setup: cabin ready; engines may start now.");
        return report;
    }

    private bool LaunchWhatsAppWindow(ChannelMapping mapping, string reason)
    {
        var browser = ResolveBrowser(mapping.BrowserProcess);
        if (browser is null)
        {
            WriteLog($"{mapping.ChannelId}: could not open WhatsApp. Browser not found: {mapping.BrowserProcess}.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = browser,
                WorkingDirectory = _repoRoot,
                UseShellExecute = false
            };
            foreach (var argument in BuildBrowserLaunchArguments(mapping))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            WriteLog($"{mapping.ChannelId}: opening WhatsApp Web in {mapping.BrowserProcess} with {browser}. Reason: {reason}");
            return true;
        }
        catch (Exception exception)
        {
            WriteLog($"{mapping.ChannelId}: could not open WhatsApp: {exception.Message}");
            return false;
        }
    }

    private ChannelReadiness EvaluateChannelReadiness(ChannelMapping mapping, IReadOnlyList<WindowInfo> windows)
    {
        var matches = FindMatchingWindows(windows, mapping).ToArray();
        if (matches.Length > 1)
        {
            return new ChannelReadiness(
                mapping,
                "DUPLICATE_WHATSAPP_WINDOWS",
                false,
                true,
                false,
                $"{mapping.BrowserProcess} tiene {matches.Length} ventanas de WhatsApp. Cierra duplicadas o elige 'Usar aqui' en una sola ventana.",
                matches.Select(item => $"{item.ProcessName} #{item.ProcessId}: {item.Title}").ToArray());
        }

        if (matches.Length == 1)
        {
            var window = matches[0];
            if (FindCoveringWindow(window, windows) is { } coveringWindow)
            {
                return new ChannelReadiness(
                    mapping,
                    "COVERED_BY_WINDOW",
                    false,
                    true,
                    false,
                    $"{window.ProcessName} #{window.ProcessId} es {mapping.ChannelId}, pero '{coveringWindow.Title}' esta encima. No abrire otra ventana; primero libero o reporto esta zona.",
                    [$"{window.ProcessName} #{window.ProcessId}: {window.Title}", $"Cubre: {coveringWindow.ProcessName} #{coveringWindow.ProcessId}: {coveringWindow.Title}"],
                    window);
            }

            var lines = ReadWindowAccessibilityLines(window.Handle, 260);
            if (DiagnoseWhatsAppBlocker(lines, window.Title) is { } blocker)
            {
                return new ChannelReadiness(
                    mapping,
                    blocker.Status,
                    false,
                    blocker.RequiresHuman,
                    false,
                    blocker.Detail,
                    SampleLines(lines, window.Title),
                    window);
            }

            if (LooksLikeWhatsAppReady(lines, window.Title))
            {
                return new ChannelReadiness(
                    mapping,
                    "READY",
                    true,
                    false,
                    false,
                    $"{window.ProcessName} #{window.ProcessId}: WhatsApp Web visible y utilizable.",
                    SampleLines(lines, window.Title),
                    window);
            }

            return new ChannelReadiness(
                mapping,
                "LOADING_OR_UNKNOWN",
                false,
                false,
                false,
                $"{window.ProcessName} #{window.ProcessId}: ventana WhatsApp visible, esperando que termine de cargar o muestre login.",
                SampleLines(lines, window.Title),
                window);
        }

        var browserWindows = FindBrowserWindows(windows, mapping).ToArray();
        if (browserWindows.Length > 0)
        {
            var window = browserWindows[0];
            var lines = ReadWindowAccessibilityLines(window.Handle, 220);
            if (DiagnoseWhatsAppBlocker(lines, window.Title) is { } blocker)
            {
                return new ChannelReadiness(
                    mapping,
                    blocker.Status,
                    false,
                    false,
                    true,
                    blocker.Detail,
                    SampleLines(lines, window.Title),
                    window);
            }

            return new ChannelReadiness(
                mapping,
                "BROWSER_BUSY_OPEN_WEB",
                false,
                false,
                true,
                $"{mapping.ChannelId} pertenece a {mapping.BrowserProcess}, pero la ventana visible esta ocupada con otra pagina. Primero buscare pestana WhatsApp; si no existe, abrire web.whatsapp.com en ese navegador.",
                [$"{window.ProcessName} #{window.ProcessId}: {window.Title}"],
                window);
        }

        if (IsBrowserProcessRunning(mapping.BrowserProcess))
        {
            return new ChannelReadiness(
                mapping,
                "BROWSER_RUNNING_NO_VISIBLE_WHATSAPP",
                false,
                false,
                true,
                $"{mapping.BrowserProcess} esta abierto, pero no veo una ventana WhatsApp Web visible. Buscare pestanas y, si no hay, abrire web.whatsapp.com en ese navegador.",
                []);
        }

        var browser = ResolveBrowser(mapping.BrowserProcess);
        if (browser is null)
        {
            return new ChannelReadiness(
                mapping,
                "BROWSER_NOT_FOUND",
                false,
                true,
                false,
                $"No encuentro {mapping.BrowserProcess}. Instala el navegador o configura ARIADGSM_BROWSER_{NormalizeProcessName(mapping.BrowserProcess).ToUpperInvariant()}.",
                []);
        }

        return new ChannelReadiness(
            mapping,
            "NOT_OPEN",
            false,
            false,
            true,
            $"No veo {mapping.BrowserProcess} con WhatsApp Web. Abrire web.whatsapp.com en ese navegador, no la app instalada.",
            []);
    }

    private static CabinBlocker? DiagnoseWhatsAppBlocker(IReadOnlyList<string> lines, string title)
    {
        var text = NormalizeForSearch(string.Join(" ", lines.Prepend(title)));
        if (text.Contains("se produjo un error en el perfil", StringComparison.Ordinal)
            || text.Contains("no se pueden leer tus preferencias", StringComparison.Ordinal))
        {
            return new CabinBlocker(
                "PROFILE_ERROR",
                true,
                "El navegador muestra 'Se produjo un error en el perfil'. Pulsa Aceptar y revisa ese perfil antes de arrancar la IA.");
        }

        if (text.Contains("whatsapp esta abierto en otra ventana", StringComparison.Ordinal)
            || text.Contains("usar aqui", StringComparison.Ordinal))
        {
            return new CabinBlocker(
                "NEEDS_USE_HERE",
                true,
                "WhatsApp dice que ya esta abierto en otra ventana. Elige manualmente 'Usar aqui' en una sola ventana o cierra duplicadas.");
        }

        if (text.Contains("usa whatsapp en tu computadora", StringComparison.Ordinal)
            || text.Contains("vincular dispositivo", StringComparison.Ordinal)
            || text.Contains("codigo qr", StringComparison.Ordinal)
            || text.Contains("iniciar sesion", StringComparison.Ordinal))
        {
            return new CabinBlocker(
                "LOGIN_REQUIRED",
                true,
                "WhatsApp Web pide login/QR. Debes vincular ese canal antes de iniciar modo vivo/aprendizaje.");
        }

        return null;
    }

    private static WindowInfo? FindCoveringWindow(WindowInfo target, IReadOnlyList<WindowInfo> windows)
    {
        return windows
            .Where(window => window.ZOrder < target.ZOrder)
            .Where(window => !window.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase)
                || window.ProcessId != target.ProcessId)
            .Where(window => !IsIgnoredCoverageWindow(window))
            .Where(window => OverlapRatio(window.Bounds, target.Bounds) >= 0.20)
            .OrderBy(window => window.ZOrder)
            .FirstOrDefault();
    }

    private static bool IsIgnoredCoverageWindow(WindowInfo window)
    {
        var process = NormalizeProcessName(window.ProcessName);
        var title = NormalizeForSearch(window.Title);
        return process.Equals("textinputhost", StringComparison.OrdinalIgnoreCase)
            || process.Equals("shellexperiencehost", StringComparison.OrdinalIgnoreCase)
            || process.Equals("ariadgsm agent", StringComparison.OrdinalIgnoreCase)
            || process.Equals("ariadgsm.agent.desktop", StringComparison.OrdinalIgnoreCase)
            || title.Contains("ariadgsm ia local", StringComparison.Ordinal)
            || title.Contains("ariadgsm agent desktop", StringComparison.Ordinal)
            || window.Title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase);
    }

    private static double OverlapRatio(WindowBounds a, WindowBounds b)
    {
        if (a.Width <= 0 || a.Height <= 0 || b.Width <= 0 || b.Height <= 0)
        {
            return 0;
        }

        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Left + a.Width, b.Left + b.Width);
        var bottom = Math.Min(a.Top + a.Height, b.Top + b.Height);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        var intersection = width * height;
        return intersection / (double)Math.Max(1, b.Width * b.Height);
    }

    private bool TryAutoRecoverChannel(ChannelMapping mapping, IReadOnlyList<WindowInfo> windows, string status)
    {
        var window = FindMatchingWindows(windows, mapping).FirstOrDefault();
        if (window is null)
        {
            return false;
        }

        try
        {
            ShowWindow(window.Handle, ShowWindowRestore);
        }
        catch
        {
        }

        if (status.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _ = SetWindowPos(window.Handle, HwndTop, window.Bounds.Left, window.Bounds.Top, window.Bounds.Width, window.Bounds.Height, SetWindowPosShowWindow);
                _ = BringWindowToTop(window.Handle);
                _ = SetForegroundWindow(window.Handle);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsAutoRecoverableCabinStatus(string status)
    {
        return status.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWhatsAppReady(IReadOnlyList<string> lines, string title)
    {
        var text = NormalizeForSearch(string.Join(" ", lines.Prepend(title)));
        return text.Contains("buscar un chat", StringComparison.Ordinal)
            || text.Contains("buscar o iniciar un chat", StringComparison.Ordinal)
            || text.Contains("whatsapp business en la web", StringComparison.Ordinal)
            || text.Contains("tus mensajes personales estan cifrados", StringComparison.Ordinal)
            || text.Contains("todos no leidos favoritos grupos", StringComparison.Ordinal);
    }

    private IReadOnlyList<string> ReadWindowAccessibilityLines(IntPtr handle, int maxNodes)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            if (root is null)
            {
                return [];
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nodes = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            var count = Math.Min(nodes.Count, Math.Max(20, maxNodes));
            for (var index = 0; index < count && result.Count < 80; index++)
            {
                var element = nodes[index];
                var text = ReadAutomationElementText(element);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = NormalizeText(text);
                if (text.Length < 3 || !seen.Add(text))
                {
                    continue;
                }

                result.Add(text);
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private void WriteCabinReadinessState(string status, IReadOnlyList<ChannelReadiness> readiness)
    {
        try
        {
            var summary = string.Join(" | ", readiness.Select(item => $"{item.ChannelId}:{item.Status}"));
            var state = new Dictionary<string, object?>
            {
                ["identityVersion"] = "cabin-window-identity-v1",
                ["status"] = status,
                ["summary"] = summary,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["ready"] = readiness.Count > 0 && readiness.All(item => item.IsReady),
                ["requiresHuman"] = readiness.Any(item => item.RequiresHuman),
                ["canStartDegraded"] = CanStartWithDegradedCabin(readiness),
                ["readyChannels"] = readiness.Count(item => item.IsReady),
                ["expectedChannels"] = readiness.Count,
                ["channels"] = readiness.Select(item => new Dictionary<string, object?>
                {
                    ["channelId"] = item.ChannelId,
                    ["browser"] = item.Mapping.BrowserProcess,
                    ["status"] = item.Status,
                    ["structuralReady"] = item.IsReady,
                    ["semanticFresh"] = false,
                    ["actionReady"] = false,
                    ["isReady"] = item.IsReady,
                    ["requiresHuman"] = item.RequiresHuman,
                    ["canLaunch"] = item.CanLaunch,
                    ["detail"] = item.Detail,
                    ["evidence"] = item.Evidence,
                    ["window"] = SerializeCabinWindow(item.Window)
                }).ToArray()
            };
            WriteAllTextAtomicShared(_cabinReadinessFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            WriteWindowRealityProjection(status, readiness);
        }
        catch
        {
        }
    }

    private void WriteWindowRealityProjection(string status, IReadOnlyList<ChannelReadiness> readiness)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var channels = readiness.Select(item =>
            {
                var rawStatus = item.Status;
                var channelStatus = item.IsReady
                    ? "READY_PENDING_READER"
                    : rawStatus.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase)
                        ? "COVERED_CONFIRMED"
                        : item.RequiresHuman
                            ? "HUMAN_REQUIRED"
                            : "MISSING_OR_WRONG_SESSION";
                var isOperational = item.IsReady;
                return new Dictionary<string, object?>
                {
                    ["channelId"] = item.ChannelId,
                    ["status"] = channelStatus,
                    ["confidence"] = item.IsReady ? 0.72 : 0.42,
                    ["isOperational"] = isOperational,
                    ["structuralReady"] = isOperational,
                    ["semanticFresh"] = false,
                    ["actionReady"] = false,
                    ["requiresHuman"] = item.RequiresHuman,
                    ["handsMayAct"] = false,
                    ["decision"] = new Dictionary<string, object?>
                    {
                        ["reason"] = item.IsReady
                            ? "Cabina confirma ventana WhatsApp; Reader Core debe aportar lectura fresca antes de manos."
                            : item.Detail,
                        ["accepted"] = isOperational,
                        ["actionPolicy"] = isOperational ? "read_only_pending_resolver" : "hold"
                    },
                    ["signals"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "structural",
                            ["status"] = item.IsReady ? "ok" : item.RequiresHuman ? "needs_human" : "unknown",
                            ["confidence"] = item.IsReady ? 0.9 : 0.35,
                            ["detail"] = item.Detail,
                            ["evidence"] = item.Evidence.Take(6).ToArray()
                        },
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "visual",
                            ["status"] = item.IsReady ? "ok" : rawStatus.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase) ? "conflict" : "unknown",
                            ["confidence"] = item.IsReady ? 0.72 : 0.35,
                            ["detail"] = item.Window is null ? "Sin ventana asociada." : "Ventana asociada por Win32/DWM.",
                            ["evidence"] = item.Window is null ? Array.Empty<string>() : new[] { item.Window.Title }
                        },
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "semantic",
                            ["status"] = "unknown",
                            ["confidence"] = 0.25,
                            ["detail"] = "Reader Core aun no ha confirmado mensajes frescos para este canal.",
                            ["evidence"] = Array.Empty<string>()
                        },
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "actionability",
                            ["status"] = "unknown",
                            ["confidence"] = 0.35,
                            ["detail"] = "Input Arbiter y Hands validaran permisos antes de actuar.",
                            ["evidence"] = Array.Empty<string>()
                        },
                        new Dictionary<string, object?>
                        {
                            ["kind"] = "freshness",
                            ["status"] = "partial",
                            ["confidence"] = 0.55,
                            ["detail"] = "Proyeccion inmediata de Cabin Authority; falta resolver Python.",
                            ["evidence"] = Array.Empty<string>()
                        }
                    },
                    ["evidence"] = new Dictionary<string, object?>
                    {
                        ["detail"] = item.Detail,
                        ["rawStatus"] = rawStatus,
                        ["sourceEvidence"] = item.Evidence.Take(6).ToArray(),
                        ["window"] = SerializeCabinWindow(item.Window)
                    }
                };
            }).ToArray();

            var operational = readiness.Count(item => item.IsReady);
            var conflicted = readiness.Count(item => item.Status.Equals("COVERED_BY_WINDOW", StringComparison.OrdinalIgnoreCase));
            var requiresHuman = readiness.Count(item => item.RequiresHuman);
            var stateStatus = operational == readiness.Count && requiresHuman == 0 && status.Equals("ready", StringComparison.OrdinalIgnoreCase)
                ? "attention"
                : operational > 0
                    ? "attention"
                    : "blocked";
            var state = new Dictionary<string, object?>
            {
                ["status"] = stateStatus,
                ["engine"] = "ariadgsm_window_reality_resolver",
                ["version"] = CurrentVersion,
                ["updatedAt"] = now,
                ["contract"] = "window_reality_state",
                ["policy"] = new Dictionary<string, object?>
                {
                    ["evidenceFusion"] = new[]
                    {
                        "structural_windows",
                        "visual_geometry",
                        "semantic_reader_core",
                        "freshness_ttl",
                        "actionability_input_hands"
                    },
                    ["freshness"] = new Dictionary<string, object?>
                    {
                        ["cabinReadinessMaxAgeMs"] = 45000,
                        ["readerCoreMaxAgeMs"] = 8000,
                        ["inputArbiterMaxAgeMs"] = 30000,
                        ["handsMaxAgeMs"] = 60000
                    },
                    ["actionability"] = new Dictionary<string, object?>
                    {
                        ["operatorHasPriority"] = true,
                        ["doNotActOnCoveredWindow"] = true,
                        ["allowReadWhenSemanticFreshButVisualConflicted"] = true,
                        ["handsRequireFreshReaderMessage"] = true
                    }
                },
                ["inputs"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["file"] = "cabin-readiness.json",
                        ["freshness"] = new Dictionary<string, object?> { ["status"] = "fresh", ["ageMs"] = 0, ["maxAgeMs"] = 45000, ["fresh"] = true }
                    },
                    new Dictionary<string, object?>
                    {
                        ["file"] = "reader-core-state.json",
                        ["freshness"] = new Dictionary<string, object?> { ["status"] = "unknown", ["ageMs"] = null, ["maxAgeMs"] = 8000, ["fresh"] = false }
                    },
                    new Dictionary<string, object?>
                    {
                        ["file"] = "input-arbiter-state.json",
                        ["freshness"] = new Dictionary<string, object?> { ["status"] = "unknown", ["ageMs"] = null, ["maxAgeMs"] = 30000, ["fresh"] = false }
                    },
                    new Dictionary<string, object?>
                    {
                        ["file"] = "hands-state.json",
                        ["freshness"] = new Dictionary<string, object?> { ["status"] = "unknown", ["ageMs"] = null, ["maxAgeMs"] = 60000, ["fresh"] = false }
                    }
                },
                ["summary"] = new Dictionary<string, object?>
                {
                    ["expectedChannels"] = readiness.Count,
                    ["operationalChannels"] = operational,
                    ["readyChannels"] = operational,
                    ["structuralReadyChannels"] = operational,
                    ["actionReadyChannels"] = 0,
                    ["conflictedChannels"] = conflicted,
                    ["requiresHumanChannels"] = requiresHuman,
                    ["staleInputs"] = 0,
                    ["handsMayActChannels"] = 0
                },
                ["channels"] = channels,
                ["humanReport"] = new Dictionary<string, object?>
                {
                    ["headline"] = "Cabina proyectada, falta resolver lectura",
                    ["queEstaPasando"] = new[]
                    {
                        $"Cabin Authority vio {operational}/{readiness.Count} canales estructuralmente operables.",
                        "Window Reality Resolver Python reemplazara esta proyeccion al iniciar motores."
                    },
                    ["queAcepte"] = readiness.Where(item => item.IsReady).Select(item => $"{item.ChannelId}: READY_PENDING_READER").ToArray(),
                    ["queDude"] = readiness.Where(item => !item.IsReady).Select(item => $"{item.ChannelId}: {item.Status} - {item.Detail}").ToArray(),
                    ["queNecesitoDeBryams"] = readiness.Where(item => item.RequiresHuman).Select(item => $"{item.ChannelId}: {item.Detail}").DefaultIfEmpty("No necesito ayuda inmediata.").ToArray(),
                    ["riesgos"] = new[]
                    {
                        "Esta es una proyeccion estructural; no habilita manos por si sola.",
                        "Reader Core, Input Arbiter y Hands deben confirmar antes de actuar."
                    }
                }
            };
            WriteAllTextAtomicShared(_windowRealityStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private void WriteWorkspaceSetupState(string status, string summary, int progress, IReadOnlyList<string> blockers)
    {
        try
        {
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["phase"] = status,
                ["summary"] = summary,
                ["progress"] = Math.Clamp(progress, 0, 100),
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["blockers"] = blockers,
                ["contract"] = "workspace_setup_v1",
                ["steps"] = new[]
                {
                    "map_fixed_browsers",
                    "open_or_recover_whatsapp",
                    "arrange_three_columns",
                    "validate_readiness",
                    "allow_engines"
                }
            };
            WriteAllTextAtomicShared(_workspaceSetupStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private HealthItem CabinReadinessHealth()
    {
        if (!File.Exists(_cabinReadinessFile))
        {
            return new HealthItem("Cabina WhatsApp", "SIN PREPARAR", HealthSeverity.Info, null, "Se revisara antes del arranque autonomo.");
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(_cabinReadinessFile));
            var root = document.RootElement;
            var status = TryString(root, "status") ?? "unknown";
            var summary = TryString(root, "summary") ?? "Estado de cabina recibido.";
            var updatedAt = TryDate(root, "updatedAt");
            var requiresHuman = TryBool(root, "requiresHuman") ?? false;
            var ready = TryBool(root, "ready") ?? false;
            var severity = ready
                ? HealthSeverity.Ok
                : requiresHuman || status.Contains("attention", StringComparison.OrdinalIgnoreCase)
                    ? HealthSeverity.Warning
                    : HealthSeverity.Info;
            return new HealthItem("Cabina WhatsApp", status.ToUpperInvariant(), severity, updatedAt, summary);
        }
        catch (Exception exception)
        {
            return new HealthItem("Cabina WhatsApp", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, $"No pude leer cabina: {exception.Message}");
        }
    }

    private static IReadOnlyList<string> SampleLines(IReadOnlyList<string> lines, string title)
    {
        return lines.Prepend(title)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static string ReadAutomationElementText(AutomationElement element)
    {
        var name = SafeRead(() => element.Current.Name) ?? string.Empty;
        var value = string.Empty;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
        {
            value = SafeRead(() => valuePattern.Current.Value) ?? string.Empty;
        }

        return string.IsNullOrWhiteSpace(value) || name.Contains(value, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} {value}";
    }

    private static string NormalizeText(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string NormalizeForSearch(string value)
    {
        var normalized = NormalizeText(value).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static T? SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default;
        }
    }

    public void ArrangeWhatsAppWorkspace()
    {
        var mappings = ReadChannelMappings().ToArray();
        var windows = VisibleWindows();
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var columnWidth = Math.Max(500, area.Width / Math.Max(1, mappings.Length));
        var arranged = 0;

        for (var index = 0; index < mappings.Length; index++)
        {
            var mapping = mappings[index];
            var window = FindMatchingWindows(windows, mapping).FirstOrDefault();
            if (window is null)
            {
                continue;
            }

            var left = area.Left + (index * columnWidth);
            var width = index == mappings.Length - 1
                ? area.Right - left
                : columnWidth;
            try
            {
                ShowWindow(window.Handle, ShowWindowRestore);
                _ = SetWindowPos(window.Handle, HwndTop, left, area.Top, width, area.Height, SetWindowPosShowWindow);
                _ = BringWindowToTop(window.Handle);
                _ = SetForegroundWindow(window.Handle);
                arranged++;
                WriteLog($"{mapping.ChannelId}: arranged {window.ProcessName} #{window.ProcessId} at column {index + 1}.");
            }
            catch (Exception exception)
            {
                WriteLog($"{mapping.ChannelId}: could not arrange window: {exception.Message}");
            }
        }

        if (arranged == 0)
        {
            WriteLog("Autonomous bootstrap: no WhatsApp windows available to arrange.");
        }

        WriteWorkspaceGuardianState(EnsureWorkspaceOwned("arrange_windows"));
    }

    public void OpenPanel()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "http://127.0.0.1:3000/operativa-v2.html",
            UseShellExecute = true
        });
    }

    public void OpenLogs()
    {
        if (!File.Exists(_logFile))
        {
            AppendAllTextShared(_logFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} Log file created.{Environment.NewLine}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _logFile,
            UseShellExecute = true
        });
    }

    public void Dispose()
    {
        if (IsRunning || _desiredRunning)
        {
            Stop("dispose", "runtime.dispose");
        }
        else
        {
            WriteControlPlaneCheckpoint("dispose", "already_stopped", "Dispose libero recursos sin cambiar la causa real del ultimo stop.");
        }

        _coreLoopCts?.Dispose();
        _supervisorCts?.Dispose();
        _workspaceGuardianCts?.Dispose();
    }

    private HealthItem CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return isAdmin
                ? new HealthItem("Permisos Windows", "OK", HealthSeverity.Ok, DateTimeOffset.Now, "Ejecutando como administrador.")
                : new HealthItem("Permisos Windows", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, "No esta como administrador; algunas acciones de mouse/ventanas pueden fallar.");
        }
        catch (Exception exception)
        {
            return new HealthItem("Permisos Windows", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, $"No pude confirmar permisos: {exception.Message}");
        }
    }

    private HealthItem CheckRuntimeWritable()
    {
        try
        {
            Directory.CreateDirectory(_runtimeDir);
            var probe = Path.Combine(_runtimeDir, "write-probe.tmp");
            WriteAllTextAtomicShared(probe, DateTimeOffset.Now.ToString("O"));
            File.Delete(probe);
            return new HealthItem("Runtime local", "OK", HealthSeverity.Ok, DateTimeOffset.Now, "Carpeta de trabajo lista y escribible.");
        }
        catch (Exception exception)
        {
            return new HealthItem("Runtime local", "ERROR", HealthSeverity.Error, DateTimeOffset.Now, $"No puedo escribir en runtime: {exception.Message}", Blocking: true);
        }
    }

    private HealthItem CheckUpdaterReady()
    {
        var updater = ResolveUpdaterExe();
        return updater is null
            ? new HealthItem("AriadGSM Updater", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, "Updater no encontrado; el agente trabaja, pero no podra autoactualizarse.")
            : new HealthItem("AriadGSM Updater", "OK", HealthSeverity.Ok, DateTimeOffset.Now, $"Updater listo: {updater}");
    }

    private HealthItem CheckWebPanelDependency()
    {
        if (IsTcpPortOpen("127.0.0.1", 3000))
        {
            return new HealthItem("Panel local", "OK", HealthSeverity.Ok, DateTimeOffset.Now, "http://127.0.0.1:3000 ya esta escuchando.");
        }

        var node = ResolveNode();
        return node is null
            ? new HealthItem("Panel local", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, "Node.js no encontrado; el panel local no arrancara automaticamente.")
            : new HealthItem("Panel local", "LISTO", HealthSeverity.Info, DateTimeOffset.Now, $"Node listo: {node}");
    }

    private HealthItem CheckPythonCore()
    {
        var python = ResolvePython();
        return python is null
            ? new HealthItem("Nucleo IA Python", "ERROR", HealthSeverity.Error, DateTimeOffset.Now, "Python no encontrado; Timeline/Cognitive/Memory/Supervisor no pueden pensar.", Blocking: true)
            : new HealthItem("Nucleo IA Python", "OK", HealthSeverity.Ok, DateTimeOffset.Now, $"Python listo: {python}");
    }

    private HealthItem CheckWorkerReady(string name, string packagedExe, string projectPath)
    {
        var exePath = ResolvePackagedPath(packagedExe);
        if (File.Exists(exePath))
        {
            return new HealthItem(name, "OK", HealthSeverity.Ok, DateTimeOffset.Now, $"Ejecutable listo: {exePath}");
        }

        var project = Path.Combine(_repoRoot, projectPath);
        if (File.Exists(project) && ResolveDotnet() is not null)
        {
            return new HealthItem(name, "DEV", HealthSeverity.Warning, DateTimeOffset.Now, "Ejecutable empaquetado no existe; puedo correrlo con dotnet en modo desarrollo.");
        }

        return new HealthItem(name, "ERROR", HealthSeverity.Error, DateTimeOffset.Now, "No encuentro ejecutable empaquetado ni .NET SDK/runtime para correr el proyecto.", Blocking: true);
    }

    private IReadOnlyList<HealthItem> CheckWhatsAppChannels()
    {
        var windows = VisibleWindows();
        var items = new List<HealthItem>();
        foreach (var mapping in ReadChannelMappings())
        {
            var readiness = EvaluateChannelReadiness(mapping, windows);
            if (!readiness.IsReady)
            {
                items.Add(new HealthItem(
                    $"WhatsApp {mapping.ChannelId}",
                    readiness.Status,
                    HealthSeverity.Warning,
                    DateTimeOffset.Now,
                    readiness.Detail));
                continue;
            }

            items.Add(new HealthItem(
                $"WhatsApp {mapping.ChannelId}",
                "VISIBLE",
                HealthSeverity.Ok,
                DateTimeOffset.Now,
                readiness.Detail));
        }

        return items;
    }

    private HealthItem StateHealth(string name, string fileName, string processName)
    {
        using var document = ReadJsonStatus(fileName);
        if (document is null)
        {
            return IsManagedProcessActive(processName)
                ? new HealthItem(name, "ARRANCANDO", HealthSeverity.Warning, DateTimeOffset.Now, "Proceso activo, esperando primer archivo de estado.")
                : new HealthItem(name, "SIN ESTADO", HealthSeverity.Info, null, "Aun no hay lectura de este motor.");
        }

        var root = document.RootElement;
        var status = TryString(root, "status", "Status") ?? "ok";
        var updatedAt = TryDate(root, "updatedAt", "UpdatedAt", "observedAt", "ObservedAt");
        var lastError = TryString(root, "lastError", "LastError", "error", "Error") ?? string.Empty;
        var detail = BuildStateDetail(root, lastError);
        var severity = SeverityFromState(status, lastError);

        if (IsManagedProcessActive(processName)
            && updatedAt is not null
            && DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime() > RunningStateStaleAfter)
        {
            severity = HealthSeverity.Warning;
            status = "stale";
            detail = $"El motor esta activo, pero no actualiza estado hace mas de {RunningStateStaleAfter.TotalSeconds:0} segundos. {detail}";
        }

        return new HealthItem(name, status.ToUpperInvariant(), severity, updatedAt, detail);
    }

    private HealthItem WebPanelHealth()
    {
        return IsTcpPortOpen("127.0.0.1", 3000)
            ? new HealthItem("WebPanel", "OK", HealthSeverity.Ok, DateTimeOffset.Now, "Panel local disponible en http://127.0.0.1:3000/operativa-v2.html")
            : new HealthItem("WebPanel", "DETENIDO", IsRunning ? HealthSeverity.Warning : HealthSeverity.Info, null, "Panel local no esta escuchando.");
    }

    private HealthItem CoreLoopHealth()
    {
        if (_coreLoopTask is null)
        {
            return new HealthItem("PythonCoreLoop", "DETENIDO", HealthSeverity.Info, null, "Nucleo cognitivo aun no iniciado.");
        }

        if (_coreLoopTask.IsFaulted)
        {
            return new HealthItem("PythonCoreLoop", "ERROR", HealthSeverity.Error, DateTimeOffset.Now, _coreLoopTask.Exception?.GetBaseException().Message ?? "Fallo desconocido.");
        }

        return _coreLoopTask.IsCompleted
            ? new HealthItem("PythonCoreLoop", "DETENIDO", HealthSeverity.Warning, DateTimeOffset.Now, "El ciclo cognitivo termino o fue detenido.")
            : new HealthItem("PythonCoreLoop", "ACTIVO", HealthSeverity.Ok, DateTimeOffset.Now, "Timeline, Cognitive, Operating, Memory, Business Brain, Tool Registry, Cloud Sync, Supervisor y Ciclo autonomo estan ciclando.");
    }

    private HealthItem UpdateHealth()
    {
        if (!File.Exists(_updateStateFile))
        {
            return new HealthItem("Actualizaciones", "SIN REVISION", HealthSeverity.Info, null, "Se revisaran al arrancar en modo autonomo.");
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(_updateStateFile));
            var root = document.RootElement;
            var status = TryString(root, "status") ?? "unknown";
            var detail = TryString(root, "detail") ?? "Estado de actualizacion recibido.";
            var updatedAt = TryDate(root, "updatedAt");
            if (status.Equals("applying", StringComparison.OrdinalIgnoreCase)
                && updatedAt is { } applyingAt
                && DateTimeOffset.UtcNow - applyingAt.ToUniversalTime() > TimeSpan.FromMinutes(5))
            {
                return new HealthItem(
                    "Actualizaciones",
                    "BLOQUEADO",
                    HealthSeverity.Error,
                    updatedAt,
                    $"Update lleva mas de 5 minutos en proceso. Ultimo paso: {detail}");
            }

            var severity = status switch
            {
                "available" => HealthSeverity.Warning,
                "applying" => HealthSeverity.Warning,
                "failed" => HealthSeverity.Warning,
                "applied" => HealthSeverity.Ok,
                "current" => HealthSeverity.Ok,
                _ => HealthSeverity.Info
            };
            return new HealthItem("Actualizaciones", status.ToUpperInvariant(), severity, updatedAt, detail);
        }
        catch (Exception exception)
        {
            return new HealthItem("Actualizaciones", "AVISO", HealthSeverity.Warning, DateTimeOffset.Now, $"No pude leer estado de update: {exception.Message}");
        }
    }

    private void StartWebPanel()
    {
        if (IsTcpPortOpen("127.0.0.1", 3000))
        {
            WriteLog("WebPanel already listening on http://127.0.0.1:3000.");
            return;
        }

        var node = ResolveNode();
        if (node is null)
        {
            WriteLog("Node.js was not found. Local panel cannot start.");
            return;
        }

        var server = Path.Combine(_repoRoot, "server-wrapper.js");
        StartProcess("WebPanel", node, [server], _repoRoot, new Dictionary<string, string?>
        {
            ["PORT"] = "3000"
        });
    }

    private static bool IsTcpPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            return task.Wait(350) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void StartWorker(string name, string packagedExe, string projectPath, string configPath)
    {
        var spec = ResolveWorker(packagedExe, projectPath, configPath, once: false);
        if (spec is null)
        {
            WriteLog($"{name} worker could not start. Build the worker or install .NET.");
            return;
        }

        var workerSpec = new WorkerSpec(name, spec.FileName, spec.Arguments, spec.WorkingDirectory);
        lock (_gate)
        {
            _workerSpecs.RemoveAll(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            _workerSpecs.Add(workerSpec);
        }

        StartManagedWorker(workerSpec, "startup");
    }

    private async Task RunWorkerOnceAsync(string name, string packagedExe, string projectPath, string configPath)
    {
        var spec = ResolveWorker(packagedExe, projectPath, configPath, once: true);
        if (spec is null)
        {
            WriteLog($"{name} could not run. Build the worker or install .NET.");
            return;
        }

        await RunProcessToExitAsync(name, spec.FileName, spec.Arguments, spec.WorkingDirectory, CancellationToken.None).ConfigureAwait(false);
    }

    private ProcessSpec? ResolveWorker(string packagedExe, string projectPath, string configPath, bool once)
    {
        var exePath = ResolvePackagedPath(packagedExe);
        var config = ResolveConfigPath(configPath);
        if (File.Exists(exePath))
        {
            var args = once ? new[] { config, "--once" } : new[] { config };
            return new ProcessSpec(exePath, args, _repoRoot);
        }

        var dotnet = ResolveDotnet();
        if (dotnet is null)
        {
            return null;
        }

        var project = Path.Combine(_repoRoot, projectPath);
        var arguments = new List<string> { "run", "--project", project, "--", config };
        if (once)
        {
            arguments.Add("--once");
        }

        return new ProcessSpec(dotnet, arguments, _repoRoot);
    }

    private string ResolveConfigPath(string configPath)
    {
        var fileName = Path.GetFileName(configPath).Replace(".example", string.Empty, StringComparison.OrdinalIgnoreCase);
        var packagedConfig = Path.Combine(AppContext.BaseDirectory, "config", fileName);
        return File.Exists(packagedConfig)
            ? packagedConfig
            : Path.Combine(_repoRoot, configPath);
    }

    private string ResolvePackagedPath(string packagedPath)
    {
        var normalized = packagedPath.Replace('/', Path.DirectorySeparatorChar);
        var marker = Path.Combine("desktop-agent", "dist", "AriadGSMAgent") + Path.DirectorySeparatorChar;
        var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            var relativeToPackage = normalized[(markerIndex + marker.Length)..];
            var nextToCurrentExe = Path.Combine(AppContext.BaseDirectory, relativeToPackage);
            if (File.Exists(nextToCurrentExe))
            {
                return nextToCurrentExe;
            }
        }

        return Path.Combine(_repoRoot, normalized);
    }

    private string? ResolveUpdaterExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "updater", "AriadGSM Updater.exe"),
            Path.Combine(AppContext.BaseDirectory, "AriadGSM Updater.exe"),
            Path.Combine(_repoRoot, "desktop-agent", "dist", "AriadGSMAgent-next", "updater", "AriadGSM Updater.exe"),
            Path.Combine(_repoRoot, "desktop-agent", "dist", "AriadGSMAgent", "updater", "AriadGSM Updater.exe"),
            Path.Combine(_repoRoot, "desktop-agent", "windows-app", "src", "AriadGSM.Agent.Updater", "bin", "Debug", "net10.0-windows", "AriadGSM Updater.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private string? ResolveLauncherExe()
    {
        var candidates = new[]
        {
            Path.Combine(_desktopRoot, "launcher", "AriadGSM Launcher.exe"),
            Path.Combine(AppContext.BaseDirectory, "launcher", "AriadGSM Launcher.exe"),
            Path.Combine(_repoRoot, "desktop-agent", "dist", "AriadGSMLauncher", "AriadGSM Launcher.exe"),
            Path.Combine(_repoRoot, "desktop-agent", "windows-app", "src", "AriadGSM.Agent.Launcher", "bin", "Debug", "net10.0-windows", "AriadGSM Launcher.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task PrimeTrustSafetyAsync(CancellationToken cancellationToken)
    {
        var python = ResolvePython();
        if (python is null)
        {
            WriteLog("TrustSafety prime skipped: Python was not found.");
            return;
        }

        WriteLog("TrustSafety prime: refreshing permission gate before Hands starts.");
        await RunProcessToExitAsync(
            "TrustSafetyPrime",
            python,
            ["-m", "ariadgsm_agent.trust_safety", "--autonomy-level", "3", "--json"],
            _desktopRoot,
            cancellationToken).ConfigureAwait(false);
    }

    private void WriteInputArbiterHeartbeatState(string phase)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var requiredIdleMs = 1200;
            var operatorIdleMs = GetOperatorIdleMilliseconds();
            var operatorActive = operatorIdleMs < requiredIdleMs;
            var state = new Dictionary<string, object?>
            {
                ["contractVersion"] = "0.8.14",
                ["status"] = operatorActive ? "attention" : "ok",
                ["engine"] = "ariadgsm_input_arbiter",
                ["version"] = "0.8.14",
                ["phase"] = operatorActive ? "operator_control" : "operator_idle",
                ["decision"] = operatorActive ? "PAUSE_FOR_OPERATOR" : "ALLOW",
                ["activeOwner"] = operatorActive ? "operator" : "none",
                ["updatedAt"] = now,
                ["leaseId"] = operatorActive ? "operator_active" : "operator_idle",
                ["blockedActionId"] = "",
                ["actionType"] = "heartbeat",
                ["channelId"] = "",
                ["conversationTitle"] = "",
                ["operatorIdleMs"] = operatorIdleMs,
                ["requiredIdleMs"] = requiredIdleMs,
                ["operatorHasPriority"] = operatorActive,
                ["handsPausedOnly"] = operatorActive,
                ["eyesContinue"] = true,
                ["memoryContinue"] = true,
                ["cognitiveContinue"] = true,
                ["businessBrainContinue"] = true,
                ["lease"] = new Dictionary<string, object?>
                {
                    ["leaseId"] = operatorActive ? "operator_active" : "operator_idle",
                    ["granted"] = !operatorActive,
                    ["requiresInput"] = false,
                    ["issuedAt"] = now,
                    ["expiresAt"] = now.AddMilliseconds(1000),
                    ["ttlMs"] = 1000,
                    ["actionId"] = "",
                    ["actionType"] = "heartbeat",
                    ["reason"] = $"Control Plane {phase}: refresco Input Arbiter antes de manos."
                },
                ["operator"] = new Dictionary<string, object?>
                {
                    ["hasPriority"] = operatorActive,
                    ["idleMs"] = operatorIdleMs,
                    ["requiredIdleMs"] = requiredIdleMs,
                    ["cooldownUntil"] = operatorActive ? now.AddMilliseconds(1600) : now,
                    ["cooldownMs"] = operatorActive ? 1600 : 0
                },
                ["continuation"] = new Dictionary<string, object?>
                {
                    ["hands"] = !operatorActive,
                    ["eyes"] = true,
                    ["memory"] = true,
                    ["cognitive"] = true,
                    ["businessBrain"] = true
                },
                ["summary"] = operatorActive
                    ? "Tu estas usando mouse o teclado; manos esperan pero ojos y memoria siguen."
                    : "Operador inactivo; manos disponibles para acciones verificadas."
            };
            WriteAllTextAtomicShared(
                Path.Combine(_runtimeDir, "input-arbiter-state.json"),
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            WriteLog($"Input Arbiter heartbeat failed: {exception.Message}");
        }
    }

    private void StartCoreLoop()
    {
        if (_coreLoopTask is { IsCompleted: false })
        {
            return;
        }

        var python = ResolvePython();
        if (python is null)
        {
            WriteLog("Python was not found. Cognitive/Timeline/Memory loop is disabled.");
            return;
        }

        _coreLoopCts = new CancellationTokenSource();
        _coreLoopTask = Task.Run(async () =>
        {
            WriteLog("Python core loop started.");
            while (!_coreLoopCts.IsCancellationRequested)
            {
                try
                {
                    await RunCoreSequenceAsync(_coreLoopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    WriteLog($"Python core loop error: {exception.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1.5), _coreLoopCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            WriteLog("Python core loop stopped.");
        });
    }

    private void StartSupervisorLoop()
    {
        if (_supervisorTask is { IsCompleted: false })
        {
            return;
        }

        _supervisorCts = new CancellationTokenSource();
        _supervisorTask = Task.Run(async () =>
        {
            WriteLog("Reliability supervisor started.");
            WriteSupervisorState("ok", "Supervisor watching local engines.");
            WriteRuntimeKernelState("runtime_orchestrator", "supervisor_started");
            while (!_supervisorCts.IsCancellationRequested)
            {
                try
                {
                    RecoverExpectedWorkers();
                    WriteSupervisorState("ok", "Supervisor watching local engines.");
                    WriteRuntimeKernelState("runtime_orchestrator", "supervisor_loop");
                    await Task.Delay(SupervisorInterval, _supervisorCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    WriteLog($"Reliability supervisor error: {exception.Message}");
                    WriteSupervisorState("warning", exception.Message);
                    WriteRuntimeKernelState("runtime_orchestrator", "supervisor_error");
                    await Task.Delay(SupervisorInterval).ConfigureAwait(false);
                }
            }

            WriteLog("Reliability supervisor stopped.");
            WriteRuntimeKernelState("runtime_orchestrator", "supervisor_stopped");
        });
    }

    private async Task RunCoreSequenceAsync(CancellationToken cancellationToken)
    {
        var python = ResolvePython();
        if (python is null)
        {
            return;
        }

        var modules = new[]
        {
            ("StageZero", "ariadgsm_agent.stage_zero", new[] { "--json" }),
            ("DomainContracts", "ariadgsm_agent.domain_contracts", new[] { "--json" }),
            ("AutonomousCycleStart", "ariadgsm_agent.autonomous_cycle", new[] { "--trigger", "start", "--json" }),
            ("ReaderCore", "ariadgsm_agent.reader_core", new[] { "--json" }),
            ("WindowReality", "ariadgsm_agent.window_reality", new[] { "--json" }),
            ("Timeline", "ariadgsm_agent.timeline", new[] { "--json" }),
            ("Cognitive", "ariadgsm_agent.cognitive", new[] { "--autonomy-level", "3", "--json" }),
            ("Operating", "ariadgsm_agent.operating", new[] { "--autonomy-level", "3", "--json" }),
            ("TrustSafetyFast", "ariadgsm_agent.trust_safety", new[] { "--autonomy-level", "3", "--json" }),
            ("DomainEventsBeforeCaseManager", "ariadgsm_agent.domain_events", new[] { "--json" }),
            ("CaseManager", "ariadgsm_agent.case_manager", new[] { "--json" }),
            ("ChannelRouting", "ariadgsm_agent.channel_routing", new[] { "--json" }),
            ("DomainEventsBeforeAccounting", "ariadgsm_agent.domain_events", new[] { "--json" }),
            ("AccountingCore", "ariadgsm_agent.accounting_evidence", new[] { "--json" }),
            ("DomainEventsBeforeMemory", "ariadgsm_agent.domain_events", new[] { "--json" }),
            ("Memory", "ariadgsm_agent.memory", new[] { "--json" }),
            ("BusinessBrain", "ariadgsm_agent.business_brain", new[] { "--autonomy-level", "3", "--json" }),
            ("ToolRegistry", "ariadgsm_agent.tool_registry", new[] { "--json" }),
            ("DomainEventsAfterBusinessBrain", "ariadgsm_agent.domain_events", new[] { "--json" }),
            ("TrustSafety", "ariadgsm_agent.trust_safety", new[] { "--autonomy-level", "3", "--json" }),
            ("Supervisor", "ariadgsm_agent.supervisor", new[] { "--autonomy-level", "3", "--json" }),
            ("AutonomousCycle", "ariadgsm_agent.autonomous_cycle", new[] { "--json" }),
            ("DomainEventsAfterCycle", "ariadgsm_agent.domain_events", new[] { "--json" }),
            ("RuntimeGovernor", "ariadgsm_agent.runtime_governor", new[] { "--desired-running", "--json" }),
            ("RuntimeKernel", "ariadgsm_agent.runtime_kernel", new[] { "--json" }),
            ("SupportTelemetry", "ariadgsm_agent.support_telemetry", new[] { "--json" }),
            ("CloudSync", "ariadgsm_agent.cloud_sync", new[] { "--json" }),
            ("EvaluationRelease", "ariadgsm_agent.release_evaluation", new[] { "--version", CurrentVersion, "--json" })
        };

        foreach (var (name, module, args) in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var arguments = new List<string> { "-m", module };
            arguments.AddRange(args);
            await RunProcessToExitAsync(name, python, arguments, _desktopRoot, cancellationToken).ConfigureAwait(false);
        }
    }

    private void StartProcess(
        string name,
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var process = CreateProcess(name, fileName, arguments, workingDirectory, environment);
        process.Start();
        RegisterOwnedProcess(name, process, "support");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
        {
            _processes.Add(new ManagedProcess(name, process, null));
        }

        WriteLog($"{name} started pid={process.Id}");
    }

    private void StartManagedWorker(WorkerSpec spec, string reason)
    {
        var process = CreateProcess(spec.Name, spec.FileName, spec.Arguments, spec.WorkingDirectory, null);
        process.Start();
        RegisterOwnedProcess(spec.Name, process, "worker");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
        {
            _processes.RemoveAll(item => item.Name.Equals(spec.Name, StringComparison.OrdinalIgnoreCase) && item.Process.HasExited);
            _processes.Add(new ManagedProcess(spec.Name, process, spec));
        }

        WriteLog($"{spec.Name} started pid={process.Id} reason={reason}");
    }

    private async Task RunProcessToExitAsync(
        string name,
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var process = CreateProcess(name, fileName, arguments, workingDirectory, null);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        WriteLog($"{name} exited code={process.ExitCode}");
    }

    private Process CreateProcess(
        string name,
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PYTHONUTF8"] = "1";
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is not null)
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data) && !ShouldSuppressProcessOutput(eventArgs.Data))
            {
                WriteLogNoThrow($"{name}: {CompactLogLine(eventArgs.Data)}");
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                WriteLogNoThrow($"{name} error: {eventArgs.Data}");
            }
        };
        process.Exited += (_, _) =>
        {
            MarkOwnedProcessStopped(process.Id);
            var exitCode = SafeExitCode(process);
            WriteLogNoThrow(_stopping
                ? $"{name} stopped by shutdown."
                : $"{name} exited code={exitCode}.");
        };
        return process;
    }

    private void StopExternalWorkerProcesses()
    {
        var currentProcessId = Environment.ProcessId;
        var workerNames = new[]
        {
            "AriadGSM.Vision.Worker",
            "AriadGSM.Perception.Worker",
            "AriadGSM.Interaction.Worker",
            "AriadGSM.Orchestrator.Worker",
            "AriadGSM.Hands.Worker"
        };

        foreach (var workerName in workerNames)
        {
            foreach (var process in Process.GetProcessesByName(workerName))
            {
                try
                {
                    if (process.Id == currentProcessId)
                    {
                        continue;
                    }

                    WriteLogNoThrow($"Stopping orphaned worker {workerName} pid={process.Id} before startup.");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
                catch (Exception exception)
                {
                    WriteLogNoThrow($"Could not stop orphaned worker {workerName} pid={process.Id}: {exception.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private void RecoverExpectedWorkers()
    {
        WorkerSpec[] specs;
        lock (_gate)
        {
            if (!_desiredRunning || _stopping)
            {
                return;
            }

            _processes.RemoveAll(item =>
            {
                if (!item.Process.HasExited)
                {
                    return false;
                }

                item.Process.Dispose();
                return true;
            });

            specs = _workerSpecs
                .Where(spec => !_processes.Any(item =>
                    item.Name.Equals(spec.Name, StringComparison.OrdinalIgnoreCase)
                    && !item.Process.HasExited))
                .ToArray();
        }

        foreach (var spec in specs)
        {
            if (!CanRestart(spec.Name, out var reason))
            {
                WriteSupervisorState("warning", reason);
                continue;
            }

            WriteLog($"{spec.Name} was not running. Restarting from reliability supervisor.");
            StartManagedWorker(spec, "supervisor_restart");
        }

        if (_desiredRunning && !_stopping && _coreLoopTask is null or { IsCompleted: true })
        {
            if (CanRestart("PythonCoreLoop", out var reason))
            {
                WriteLog("PythonCoreLoop was not running. Restarting from reliability supervisor.");
                StartCoreLoop();
            }
            else
            {
                WriteSupervisorState("warning", reason);
            }
        }
    }

    private bool CanRestart(string name, out string reason)
    {
        var tracker = _restartTrackers.TryGetValue(name, out var existing)
            ? existing
            : _restartTrackers[name] = new RestartTracker();
        var now = DateTimeOffset.UtcNow;
        tracker.Restarts.RemoveAll(item => now - item > RestartWindow);
        if (tracker.Restarts.Count >= MaxRestartsPerWindow)
        {
            reason = $"{name} reached restart limit ({MaxRestartsPerWindow} in {RestartWindow.TotalMinutes:0} min).";
            return false;
        }

        tracker.Restarts.Add(now);
        tracker.LastRestartAt = now;
        reason = $"{name} restart allowed.";
        return true;
    }

    private void WriteSupervisorState(string status, string summary)
    {
        try
        {
            WorkerSpec[] specs;
            ManagedProcess[] processes;
            lock (_gate)
            {
                specs = _workerSpecs.ToArray();
                processes = _processes.ToArray();
            }

            var restartCount = _restartTrackers.Values.Sum(item => item.Restarts.Count);
            var state = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["engine"] = "ariadgsm_agent_reliability_supervisor",
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["desiredRunning"] = _desiredRunning,
                ["lastSummary"] = summary,
                ["restartCount"] = restartCount,
                ["workers"] = specs.Select(spec =>
                {
                    var running = processes.FirstOrDefault(item =>
                        item.Name.Equals(spec.Name, StringComparison.OrdinalIgnoreCase)
                        && !item.Process.HasExited);
                    _restartTrackers.TryGetValue(spec.Name, out var tracker);
                    return new Dictionary<string, object?>
                    {
                        ["name"] = spec.Name,
                        ["running"] = running is not null,
                        ["pid"] = running?.Process.Id,
                        ["restartCount"] = tracker?.Restarts.Count ?? 0,
                        ["lastRestartAt"] = tracker?.LastRestartAt
                    };
                }).ToArray(),
                ["coreLoopRunning"] = _coreLoopTask is { IsCompleted: false },
                ["supervisorRunning"] = _supervisorTask is { IsCompleted: false }
            };
            WriteAllTextAtomicShared(_agentSupervisorStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            WriteDiagnosticTimelineEvent(
                "runtime_orchestrator",
                status,
                summary,
                $"workers={specs.Length}; restartCount={restartCount}",
                status.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "info" : "warning");
        }
        catch
        {
        }
    }

    private void TryStop(ManagedProcess item)
    {
        try
        {
            if (!item.Process.HasExited)
            {
                if (item.Process.CloseMainWindow())
                {
                    item.Process.WaitForExit(1200);
                }

                if (!item.Process.HasExited)
                {
                    item.Process.Kill(entireProcessTree: true);
                    item.Process.WaitForExit(3000);
                    WriteLog($"{item.Name} force-stopped by Runtime Governor.");
                }
                else
                {
                    WriteLog($"{item.Name} stopped gracefully.");
                }
            }
        }
        catch (Exception exception)
        {
            WriteLog($"Could not stop {item.Name}: {exception.Message}");
        }
        finally
        {
            MarkOwnedProcessStopped(item.Process.Id);
            item.Process.Dispose();
        }
    }

    private bool IsManagedProcessActive(string name)
    {
        if (name.Equals("PythonCoreLoop", StringComparison.OrdinalIgnoreCase))
        {
            return _coreLoopTask is { IsCompleted: false };
        }

        if (name.Equals("ReliabilitySupervisor", StringComparison.OrdinalIgnoreCase))
        {
            return _supervisorTask is { IsCompleted: false };
        }

        if (name.Equals("WorkspaceGuardian", StringComparison.OrdinalIgnoreCase))
        {
            return _workspaceGuardianTask is { IsCompleted: false };
        }

        lock (_gate)
        {
            return _processes.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !item.Process.HasExited);
        }
    }

    private JsonDocument? ReadJsonStatus(string fileName)
    {
        var path = Path.Combine(_runtimeDir, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(ReadAllTextShared(path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteLog(string message)
    {
        WriteLogNoThrow(message);
    }

    private void WriteLogNoThrow(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";
        AriadGSMTelemetryEventSource.Log.SafeRuntimeLog(message);
        try
        {
            Directory.CreateDirectory(_runtimeDir);
            RotateLogIfNeeded();
            AppendAllTextShared(_logFile, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never be able to crash the control center.
        }

        try
        {
            LogReceived?.Invoke(line);
        }
        catch
        {
            // UI callbacks are best-effort.
        }
    }

    private void RotateLogIfNeeded()
    {
        try
        {
            if (!File.Exists(_logFile) || new FileInfo(_logFile).Length <= MaxLogBytes)
            {
                return;
            }

            for (var index = MaxLogArchives - 1; index >= 1; index--)
            {
                var source = Path.Combine(_runtimeDir, $"windows-app.{index}.log");
                var target = Path.Combine(_runtimeDir, $"windows-app.{index + 1}.log");
                if (File.Exists(source))
                {
                    File.Move(source, target, overwrite: true);
                }
            }

            File.Move(_logFile, Path.Combine(_runtimeDir, "windows-app.1.log"), overwrite: true);
        }
        catch
        {
        }
    }

    private static void AppendAllTextShared(string path, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<string> ReadTailLinesShared(string path, int maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var offset = Math.Max(0, stream.Length - Math.Max(4096, maxBytes));
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return content
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
    }

    private static async Task<string> ReadAllTextSharedAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteAllTextAtomicShared(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                File.WriteAllText(tempPath, text, Encoding.UTF8);
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Thread.Sleep(25 * (attempt + 1));
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private string? ResolvePython()
    {
        if (DateTimeOffset.Now - _pythonResolvedAt < ToolResolveTtl)
        {
            return _cachedPython;
        }

        _pythonResolvedAt = DateTimeOffset.Now;
        var configured = ResolveExecutable("ARIADGSM_PYTHON", "python.exe");
        if (configured is not null && CanRun(configured, "--version"))
        {
            _cachedPython = configured;
            return _cachedPython;
        }

        var bundled = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "codex-runtimes",
            "codex-primary-runtime",
            "dependencies",
            "python",
            "python.exe");
        _cachedPython = File.Exists(bundled) && CanRun(bundled, "--version") ? bundled : null;
        return _cachedPython;
    }

    private string? ResolveNode()
    {
        if (DateTimeOffset.Now - _nodeResolvedAt < ToolResolveTtl)
        {
            return _cachedNode;
        }

        _nodeResolvedAt = DateTimeOffset.Now;
        _cachedNode = ResolveExecutable("ARIADGSM_NODE", "node.exe");
        return _cachedNode;
    }

    private string? ResolveDotnet()
    {
        if (DateTimeOffset.Now - _dotnetResolvedAt < ToolResolveTtl)
        {
            return _cachedDotnet;
        }

        _dotnetResolvedAt = DateTimeOffset.Now;
        _cachedDotnet = ResolveExecutable("ARIADGSM_DOTNET", "dotnet.exe");
        return _cachedDotnet;
    }

    private string? ResolveBrowser(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        var envName = $"ARIADGSM_BROWSER_{normalized.ToUpperInvariant()}";
        var exe = normalized switch
        {
            "msedge" => "msedge.exe",
            "chrome" => "chrome.exe",
            "firefox" => "firefox.exe",
            var value when value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => value,
            var value => $"{value}.exe"
        };

        return ResolveExecutable(envName, exe, KnownBrowserPaths(normalized));
    }

    private IReadOnlyList<string> BuildBrowserLaunchArguments(ChannelMapping mapping)
    {
        var normalized = NormalizeProcessName(mapping.BrowserProcess);
        var arguments = new List<string>();

        if (normalized.Equals("msedge", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("chrome", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("--new-window");
            arguments.Add("--no-first-run");
            arguments.Add("--disable-session-crashed-bubble");
            arguments.Add("--disable-features=DesktopPWAsLinkCapturing");
            if (BrowserProfilePinningEnabled()
                && !string.IsNullOrWhiteSpace(mapping.ProfileDirectory))
            {
                arguments.Add($"--profile-directory={mapping.ProfileDirectory}");
            }

            arguments.Add(WhatsAppWebUrl);
            return arguments;
        }

        if (normalized.Equals("firefox", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-new-window");
            arguments.Add(WhatsAppWebUrl);
            return arguments;
        }

        arguments.Add(WhatsAppWebUrl);
        return arguments;
    }

    private static bool BrowserProfilePinningEnabled()
    {
        var value = Environment.GetEnvironmentVariable("ARIADGSM_ENABLE_BROWSER_PROFILE_PINNING");
        return value is not null
            && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> KnownBrowserPaths(string normalizedProcessName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return normalizedProcessName switch
        {
            "msedge" => [
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")
            ],
            "chrome" => [
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
            ],
            "firefox" => [
                Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
                Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe")
            ],
            _ => []
        };
    }

    private static string? ResolveExecutable(string? envName, string executableName, IReadOnlyList<string>? extraCandidates = null)
    {
        if (!string.IsNullOrWhiteSpace(envName))
        {
            var configured = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }
        }

        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var candidate = Path.Combine(path.Trim(), executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in extraCandidates ?? [])
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool CanRun(string fileName, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = argument,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -999;
        }
    }

    private static bool ShouldSuppressProcessOutput(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (trimmed is "{" or "}" or "[" or "]")
        {
            return true;
        }

        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
            || (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\",", StringComparison.Ordinal)))
        {
            return true;
        }

        if (trimmed.StartsWith("\"", StringComparison.Ordinal)
            && trimmed.Count(character => character == '"') <= 4)
        {
            return true;
        }

        if (trimmed.StartsWith("\"", StringComparison.Ordinal)
            && (trimmed.Contains("\":", StringComparison.Ordinal) || trimmed.EndsWith(",", StringComparison.Ordinal)))
        {
            return true;
        }

        return trimmed.Contains("\"eventType\"", StringComparison.Ordinal)
            || trimmed.Contains("\"messages\"", StringComparison.Ordinal)
            || trimmed.Contains("\"latestAssessments\"", StringComparison.Ordinal)
            || trimmed.Contains("\"latestTimelines\"", StringComparison.Ordinal);
    }

    private static string CompactLogLine(string line)
    {
        var compact = string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 320 ? compact : compact[..320] + "...";
    }

    private string ReadCurrentVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ariadgsm-version.json");
        if (!File.Exists(path))
        {
            return "0.0.0-dev";
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(path));
            return TryString(document.RootElement, "version") ?? "0.0.0-dev";
        }
        catch
        {
            return "0.0.0-dev";
        }
    }

    private string ReadVersionSummary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ariadgsm-version.json");
        if (!File.Exists(path))
        {
            return "v0.0.0-dev";
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(path));
            var root = document.RootElement;
            var version = TryString(root, "version") ?? "0.0.0-dev";
            var channel = TryString(root, "channel") ?? "local";
            var commit = TryString(root, "buildCommit");
            var builtAt = TryString(root, "builtAt");
            var detail = new List<string> { channel };
            if (!string.IsNullOrWhiteSpace(commit))
            {
                detail.Add(commit);
            }

            if (!string.IsNullOrWhiteSpace(builtAt))
            {
                detail.Add(builtAt);
            }

            return $"v{version} ({string.Join(", ", detail)})";
        }
        catch
        {
            return $"v{ReadCurrentVersion()}";
        }
    }

    private string ResolveUpdateManifestSource()
    {
        var configured = Environment.GetEnvironmentVariable("ARIADGSM_UPDATE_MANIFEST");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var localPackageManifest = Path.Combine(AppContext.BaseDirectory, "ariadgsm-update.json");
        if (File.Exists(localPackageManifest))
        {
            return localPackageManifest;
        }

        return DefaultUpdateManifestUrl;
    }

    private static async Task<string> ReadTextAsync(string source, HttpClient client, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            return await client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        }

        return await ReadAllTextSharedAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private void WriteUpdateState(UpdateCheckResult update, string status)
    {
        var state = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["currentVersion"] = update.CurrentVersion,
            ["latestVersion"] = update.LatestVersion,
            ["packageUrl"] = update.PackageUrl,
            ["sha256"] = update.Sha256,
            ["autoApply"] = update.AutoApply,
            ["manifest"] = update.ManifestSource,
            ["detail"] = update.Detail,
            ["updatedAt"] = DateTimeOffset.UtcNow
        };
        WriteAllTextAtomicShared(_updateStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int CompareVersions(string left, string right)
    {
        return NormalizeVersion(left).CompareTo(NormalizeVersion(right));
    }

    private static Version NormalizeVersion(string value)
    {
        return Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private IReadOnlyList<ChannelMapping> ReadChannelMappings()
    {
        var registryMappings = TryReadChannelMappingsFromRegistry();
        if (registryMappings.Count > 0)
        {
            return registryMappings;
        }

        var path = ResolveConfigPath(Path.Combine("desktop-agent", "perception-engine", "config", "perception.example.json"));
        if (!File.Exists(path))
        {
            return DefaultChannelMappings();
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(path));
            if (!document.RootElement.TryGetProperty("channelMappings", out var mappings)
                || mappings.ValueKind != JsonValueKind.Array)
            {
                return DefaultChannelMappings();
            }

            var result = new List<ChannelMapping>();
            foreach (var item in mappings.EnumerateArray())
            {
                var channelId = TryString(item, "channelId") ?? string.Empty;
                var browserProcess = TryString(item, "browserProcess") ?? string.Empty;
                var titleContains = TryString(item, "titleContains") ?? "WhatsApp";
                var profileDirectory = TryString(item, "profileDirectory") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(browserProcess))
                {
                    result.Add(new ChannelMapping(channelId, browserProcess, titleContains, profileDirectory));
                }
            }

            return result.Count == 0 ? DefaultChannelMappings() : result;
        }
        catch
        {
            return DefaultChannelMappings();
        }
    }

    private IReadOnlyList<ChannelMapping> TryReadChannelMappingsFromRegistry()
    {
        if (string.IsNullOrWhiteSpace(_cabinChannelRegistryFile) || !File.Exists(_cabinChannelRegistryFile))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(_cabinChannelRegistryFile));
            if (!document.RootElement.TryGetProperty("channels", out var mappings)
                || mappings.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<ChannelMapping>();
            foreach (var item in mappings.EnumerateArray())
            {
                var channelId = TryString(item, "channelId") ?? string.Empty;
                var browserProcess = TryString(item, "browserProcess") ?? string.Empty;
                var titleContains = TryString(item, "titleContains") ?? "WhatsApp";
                var profileDirectory = TryString(item, "launchProfileDirectory", "profileDirectory") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(browserProcess))
                {
                    result.Add(new ChannelMapping(channelId, browserProcess, titleContains, profileDirectory));
                }
            }

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ChannelMapping> DefaultChannelMappings()
    {
        return
        [
            new("wa-1", "msedge", "WhatsApp", ""),
            new("wa-2", "chrome", "WhatsApp", ""),
            new("wa-3", "firefox", "WhatsApp", "")
        ];
    }

    private static IEnumerable<WindowInfo> FindMatchingWindows(IReadOnlyList<WindowInfo> windows, ChannelMapping mapping)
    {
        var process = NormalizeProcessName(mapping.BrowserProcess);
        return windows.Where(window =>
            NormalizeProcessName(window.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase)
            && (window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase)
                || LooksLikeWhatsAppWindowTitle(window.Title)))
            .OrderBy(window => window.ZOrder);
    }

    private static IEnumerable<WindowInfo> FindBrowserWindows(IReadOnlyList<WindowInfo> windows, ChannelMapping mapping)
    {
        var process = NormalizeProcessName(mapping.BrowserProcess);
        return windows.Where(window =>
            NormalizeProcessName(window.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase)
            && !window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase)
            && !LooksLikeWhatsAppWindowTitle(window.Title))
            .OrderBy(window => window.ZOrder);
    }

    private static bool LooksLikeWhatsAppWindowTitle(string title)
    {
        return NormalizeForSearch(title).Contains("whatsapp", StringComparison.Ordinal);
    }

    private static bool IsBrowserProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(NormalizeProcessName(processName)).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<WindowInfo> VisibleWindows()
    {
        var windows = new List<WindowInfo>();
        var zOrder = 0;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            if (IsWindowCloaked(handle))
            {
                return true;
            }

            var titleBuilder = new StringBuilder(512);
            _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
            var title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            var threadId = GetWindowThreadProcessId(handle, out var processId);
            var bounds = GetWindowRect(handle, out var rect)
                ? new WindowBounds(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
                : new WindowBounds(0, 0, 0, 0);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new WindowInfo(handle, (int)processId, process.ProcessName, title, bounds, zOrder++));
            }
            catch
            {
                windows.Add(new WindowInfo(handle, (int)processId, "unknown", title, bounds, zOrder++));
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static Dictionary<string, object?>? SerializeCabinWindow(WindowInfo? window)
    {
        if (window is null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["processId"] = window.ProcessId,
            ["processName"] = window.ProcessName,
            ["title"] = window.Title,
            ["zOrder"] = window.ZOrder,
            ["bounds"] = new Dictionary<string, object?>
            {
                ["left"] = window.Bounds.Left,
                ["top"] = window.Bounds.Top,
                ["width"] = window.Bounds.Width,
                ["height"] = window.Bounds.Height
            }
        };
    }

    private static bool IsWindowCloaked(IntPtr handle)
    {
        try
        {
            return DwmGetWindowAttribute(handle, DwmwaCloaked, out var cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeProcessName(string value)
    {
        var name = value.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private static HealthSeverity SeverityFromState(string status, string lastError)
    {
        if (!string.IsNullOrWhiteSpace(lastError)
            || status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return HealthSeverity.Error;
        }

        return status.Contains("attention", StringComparison.OrdinalIgnoreCase)
            || status.Contains("warning", StringComparison.OrdinalIgnoreCase)
            || status.Contains("warn", StringComparison.OrdinalIgnoreCase)
            || status.Contains("degraded", StringComparison.OrdinalIgnoreCase)
            ? HealthSeverity.Warning
            : HealthSeverity.Ok;
    }

    private static string BuildStateDetail(JsonElement root, string lastError)
    {
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            return lastError;
        }

        var parts = new List<string>();
        AddStringPart(parts, root, "lastSummary", "summaryText", "lastReaderStatus", "lastExtractionSummary");
        AddNumberPart(parts, root, "messagesExtracted", "eventsWritten", "conversationEventsWritten", "actionsWritten", "actionsBlocked", "whatsAppWindowsDetected", "readerLinesObserved");
        AddNumberPart(parts, root, "targetsObserved", "targetsAccepted", "targetsRejected", "actionableTargets", "interactionEventsWritten");
        AddNestedNumberPart(parts, root, "summary", "findings", "blocked", "requiresHumanConfirmation", "critical", "memoryMessages");
        AddNestedNumberPart(parts, root, "ingested", "newMessages", "sourceBytesRead", "sourceBacklogBytes", "cycleDurationMs");
        AddNestedNumberPart(parts, root, "durable", "storedMessages", "storedTimelines");
        AddBackbonePart(parts, root);
        return parts.Count == 0 ? "Estado recibido correctamente." : string.Join(" | ", parts.Take(4));
    }

    private static void AddStringPart(List<string> parts, JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = TryString(root, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
                return;
            }
        }
    }

    private static void AddNumberPart(List<string> parts, JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryNumber(root, name) is { } number)
            {
                parts.Add($"{name}={number}");
            }
        }
    }

    private static void AddNestedNumberPart(List<string> parts, JsonElement root, string objectName, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(objectName, out var child)
            || child.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var name in names)
        {
            if (TryNumber(child, name) is { } number)
            {
                parts.Add($"{objectName}.{name}={number}");
            }
        }
    }

    private static void AddBackbonePart(List<string> parts, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("latestBatch", out var latestBatch)
            || latestBatch.ValueKind != JsonValueKind.Object
            || !latestBatch.TryGetProperty("summary", out var summary)
            || summary.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var bytes = TryNumber(summary, "bytesRead") ?? 0;
        var backlog = TryNumber(summary, "backlogBytes") ?? 0;
        var skipped = TryNumber(summary, "skippedBacklogBytes") ?? 0;
        parts.Add($"backbone bytes={bytes}");
        parts.Add($"backlog={backlog}");
        parts.Add($"saltado={skipped}");
    }

    private static string Text(JsonDocument? document, params string[] names)
    {
        if (document is null)
        {
            return "-";
        }

        var value = TryString(document.RootElement, names);
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string Number(JsonDocument? document, params string[] names)
    {
        if (document is null)
        {
            return "0";
        }

        return TryNumber(document.RootElement, names)?.ToString() ?? "0";
    }

    private static string NestedNumber(JsonDocument? document, string objectName, params string[] names)
    {
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(objectName, out var child)
            || child.ValueKind != JsonValueKind.Object)
        {
            return "0";
        }

        return TryNumber(child, names)?.ToString() ?? "0";
    }

    private static string NestedBool(JsonDocument? document, string objectName, params string[] names)
    {
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(objectName, out var child)
            || child.ValueKind != JsonValueKind.Object)
        {
            return "no";
        }

        foreach (var name in names)
        {
            if (child.TryGetProperty(name, out var value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean() ? "si" : "no";
            }
        }

        return "no";
    }

    private static string DeepText(JsonDocument? document, params string[] path)
    {
        if (document is null || path.Length == 0)
        {
            return "-";
        }

        var current = document.RootElement;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return "-";
            }
        }

        return current.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(current.GetString())
            ? current.GetString()!
            : "-";
    }

    private static string DeepNumber(JsonDocument? document, params string[] path)
    {
        if (document is null || path.Length == 0)
        {
            return "0";
        }

        var current = document.RootElement;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return "0";
            }
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt64(out var number)
            ? number.ToString()
            : "0";
    }

    private static bool LooksLikeRawJson(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("}", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal)
            || trimmed.Contains("\"eventType\"", StringComparison.Ordinal)
            || trimmed.Contains("\"messages\"", StringComparison.Ordinal)
            || trimmed.Contains("\"conversationEventId\"", StringComparison.Ordinal);
    }

    private static string? TryString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static long? TryNumber(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool? TryBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }
        }

        return null;
    }

    private static DateTimeOffset? TryDate(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool ContainsProblemSignal(string line)
    {
        return line.Contains(" error:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || line.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || line.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("disabled", StringComparison.OrdinalIgnoreCase)
            || line.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || line.Contains("bloque", StringComparison.OrdinalIgnoreCase)
            || line.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || line.Contains("exited code=1", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no encontre", StringComparison.OrdinalIgnoreCase)
            || line.Contains("no pude", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatHealthLine(HealthItem item)
    {
        var updated = item.UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
        return $"{item.Name} | {item.Status} | {item.Severity} | {updated} | {item.Detail}";
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static string LocateRepoRoot()
    {
        var candidates = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory
        };

        foreach (var start in candidates)
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "server-wrapper.js"))
                    && Directory.Exists(Path.Combine(current.FullName, "desktop-agent")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return Environment.CurrentDirectory;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    private static long GetOperatorIdleMilliseconds()
    {
        try
        {
            var lastInput = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref lastInput))
            {
                return int.MaxValue - 1L;
            }

            var now = GetTickCount64();
            var idle = now >= lastInput.DwTime ? now - lastInput.DwTime : 0;
            return idle >= int.MaxValue ? int.MaxValue - 1L : (long)idle;
        }
        catch
        {
            return int.MaxValue - 1L;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

    private sealed record ManagedProcess(string Name, Process Process, WorkerSpec? Spec);

    private sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

    private sealed record WorkerSpec(string Name, string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

    private sealed record ChannelReadiness(
        ChannelMapping Mapping,
        string Status,
        bool IsReady,
        bool RequiresHuman,
        bool CanLaunch,
        string Detail,
        IReadOnlyList<string> Evidence,
        WindowInfo? Window = null)
    {
        public string ChannelId => Mapping.ChannelId;
    }

    private sealed record CabinBlocker(string Status, bool RequiresHuman, string Detail);

    private sealed class RestartTracker
    {
        public List<DateTimeOffset> Restarts { get; } = [];

        public DateTimeOffset? LastRestartAt { get; set; }
    }

    private sealed record ChannelMapping(string ChannelId, string BrowserProcess, string TitleContains, string ProfileDirectory);

    private sealed record WindowInfo(IntPtr Handle, int ProcessId, string ProcessName, string Title, WindowBounds Bounds, int ZOrder);

    private sealed record WindowBounds(int Left, int Top, int Width, int Height);
}

internal enum HealthSeverity
{
    Info,
    Ok,
    Warning,
    Error
}

internal sealed record HealthItem(
    string Name,
    string Status,
    HealthSeverity Severity,
    DateTimeOffset? UpdatedAt,
    string Detail,
    bool Blocking = false);

internal sealed record PreflightReport(IReadOnlyList<HealthItem> Items)
{
    public bool HasBlockingErrors => Items.Any(item => item.Blocking && item.Severity == HealthSeverity.Error);
}

internal sealed record UpdateCheckResult(
    bool Available,
    bool AutoApply,
    string CurrentVersion,
    string LatestVersion,
    string PackageUrl,
    string Sha256,
    string ManifestSource,
    string Detail);

internal sealed record AgentSnapshot(
    JsonDocument? Vision,
    JsonDocument? Perception,
    JsonDocument? Interaction,
    JsonDocument? Orchestrator,
    JsonDocument? Timeline,
    JsonDocument? Cognitive,
    JsonDocument? Operating,
    JsonDocument? Memory,
    JsonDocument? BusinessBrain,
    JsonDocument? Hands,
    JsonDocument? Supervisor,
    JsonDocument? AutonomousCycle,
    JsonDocument? DomainEvents,
    IReadOnlyList<string> Processes);
