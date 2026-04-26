using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace AriadGSM.Agent.Desktop;

internal sealed class AgentRuntime : IDisposable
{
    private const string DefaultUpdateManifestUrl = "https://raw.githubusercontent.com/raugsm/miweb-2.0/main/desktop-agent/update/ariadgsm-update.json";
    private const int ShowWindowRestore = 9;
    private static readonly TimeSpan ToolResolveTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunningStateStaleAfter = TimeSpan.FromSeconds(45);
    private readonly List<ManagedProcess> _processes = [];
    private readonly object _gate = new();
    private readonly string _repoRoot;
    private readonly string _desktopRoot;
    private readonly string _runtimeDir;
    private readonly string _logFile;
    private readonly string _updateStateFile;
    private CancellationTokenSource? _coreLoopCts;
    private Task? _coreLoopTask;
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
    }

    public string RepoRoot => _repoRoot;

    public string RuntimeDir => _runtimeDir;

    public string ExecutableDirectory => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public string VersionSummary => ReadVersionSummary();

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
        if (IsRunning)
        {
            WriteLog("Agent already running.");
            return;
        }

        var report = Preflight();
        foreach (var item in report.Items.Where(item => item.Severity != HealthSeverity.Ok && item.Severity != HealthSeverity.Info))
        {
            WriteLog($"Preflight {item.Status}: {item.Name} - {item.Detail}");
        }

        if (report.HasBlockingErrors)
        {
            throw new InvalidOperationException("No puedo iniciar: hay errores base en el diagnostico previo.");
        }

        Directory.CreateDirectory(_runtimeDir);
        WriteLog("Starting AriadGSM Agent without PowerShell.");
        StopExternalWorkerProcesses();
        StartWebPanel();
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
            "Hands",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("desktop-agent", "hands-engine", "src", "AriadGSM.Hands.Worker", "AriadGSM.Hands.Worker.csproj"),
            Path.Combine("desktop-agent", "hands-engine", "config", "hands.example.json"));
        StartCoreLoop();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task RunOnceAsync()
    {
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
        await RunCoreSequenceAsync(CancellationToken.None).ConfigureAwait(false);
        await RunWorkerOnceAsync(
            "Hands once",
            Path.Combine("desktop-agent", "dist", "AriadGSMAgent", "engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("desktop-agent", "hands-engine", "src", "AriadGSM.Hands.Worker", "AriadGSM.Hands.Worker.csproj"),
            Path.Combine("desktop-agent", "hands-engine", "config", "hands.example.json")).ConfigureAwait(false);
    }

    public void Stop()
    {
        WriteLog("Stopping AriadGSM Agent.");
        _coreLoopCts?.Cancel();
        lock (_gate)
        {
            foreach (var item in _processes.ToArray())
            {
                TryStop(item);
            }

            _processes.Clear();
        }
    }

    public AgentSnapshot Snapshot()
    {
        return new AgentSnapshot(
            ReadJsonStatus("vision-health.json"),
            ReadJsonStatus("perception-health.json"),
            ReadJsonStatus("interaction-state.json"),
            ReadJsonStatus("timeline-state.json"),
            ReadJsonStatus("cognitive-state.json"),
            ReadJsonStatus("operating-state.json"),
            ReadJsonStatus("memory-state.json"),
            ReadJsonStatus("hands-state.json"),
            ReadJsonStatus("supervisor-state.json"),
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
            StateHealth("Vision", "vision-health.json", "Vision"),
            StateHealth("Perception", "perception-health.json", "Perception"),
            StateHealth("Interaction", "interaction-state.json", "Interaction"),
            StateHealth("Timeline", "timeline-state.json", "PythonCoreLoop"),
            StateHealth("Cognitive", "cognitive-state.json", "PythonCoreLoop"),
            StateHealth("Operating", "operating-state.json", "PythonCoreLoop"),
            StateHealth("Memory", "memory-state.json", "PythonCoreLoop"),
            StateHealth("Hands", "hands-state.json", "Hands"),
            StateHealth("Supervisor", "supervisor-state.json", "PythonCoreLoop"),
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
            return File.ReadLines(_logFile)
                .TakeLast(300)
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
        using var perception = ReadJsonStatus("perception-health.json");
        using var interaction = ReadJsonStatus("interaction-state.json");
        using var timeline = ReadJsonStatus("timeline-state.json");
        using var cognitive = ReadJsonStatus("cognitive-state.json");
        using var operating = ReadJsonStatus("operating-state.json");
        using var memory = ReadJsonStatus("memory-state.json");
        using var hands = ReadJsonStatus("hands-state.json");
        using var supervisor = ReadJsonStatus("supervisor-state.json");

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
            $"Modo: {(IsRunning ? "trabajando" : "detenido")} | Procesos: {(active.Count == 0 ? "ninguno" : string.Join(", ", active))}",
            $"WhatsApps: {(whatsappSummary.Length == 0 ? "sin revision" : string.Join(" | ", whatsappSummary))}",
            $"Vision: capturas={Number(vision, "framesCaptured", "eventsWritten")} | ventanas={Number(vision, "visibleWindowCount")} | intervalo={Number(vision, "captureIntervalMs")}ms",
            $"Lectura: mensajes={Number(perception, "messagesExtracted")} | conversaciones={Number(perception, "conversationEventsWritten")} | reader={Text(perception, "lastReaderStatus")}",
            $"Interaction: objetivos={Number(interaction, "targetsObserved")} | accionables={Number(interaction, "actionableTargets")} | rechazados={Number(interaction, "targetsRejected")} | mejor={Text(interaction, "lastAcceptedTargetTitle")}",
            $"Timeline: mensajes unidos={NestedNumber(timeline, "ingested", "messages")} | historias={NestedNumber(timeline, "ingested", "timelines")}",
            $"Cognitive/Memory: decisiones={NestedNumber(cognitive, "summary", "decisions")} | memoria={NestedNumber(memory, "summary", "memoryMessages")} | aprendizaje={NestedNumber(memory, "summary", "learningEvents")}",
            $"Operating/Contabilidad: casos={NestedNumber(operating, "summary", "cases")} | tareas={NestedNumber(operating, "summary", "openTasks")} | borradores contables={NestedNumber(operating, "summary", "accountingDrafts")}",
            $"Hands: ejecutadas={Number(hands, "actionsExecuted")} | verificadas={Number(hands, "actionsVerified")} | bloqueadas={Number(hands, "actionsBlocked")} | ultimo={Text(hands, "lastSummary")}",
            $"Supervisor: hallazgos={NestedNumber(supervisor, "summary", "findings")} | requiere humano={NestedNumber(supervisor, "summary", "requiresHumanConfirmation")} | bloqueadas={NestedNumber(supervisor, "summary", "blocked")}"
        };

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
            return result;
        }
    }

    public bool TryLaunchUpdater(UpdateCheckResult update)
    {
        if (!update.Available)
        {
            return false;
        }

        if (!update.AutoApply)
        {
            WriteLog($"Update available but not auto-applied: {update.LatestVersion}.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(update.PackageUrl))
        {
            WriteLog("Update available but packageUrl is empty.");
            return false;
        }

        var updaterExe = ResolveUpdaterExe();
        if (updaterExe is null)
        {
            WriteLog("Updater executable not found.");
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
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(update.PackageUrl);
        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            startInfo.ArgumentList.Add("--sha256");
            startInfo.ArgumentList.Add(update.Sha256);
        }

        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(currentExe);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--state");
        startInfo.ArgumentList.Add(_updateStateFile);

        Process.Start(startInfo);
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
        var opened = 0;
        foreach (var mapping in ReadChannelMappings())
        {
            if (FindMatchingWindows(windows, mapping).Any())
            {
                WriteLog($"{mapping.ChannelId}: WhatsApp already visible in {mapping.BrowserProcess}.");
                continue;
            }

            var browser = ResolveBrowser(mapping.BrowserProcess);
            if (browser is null)
            {
                WriteLog($"{mapping.ChannelId}: could not open WhatsApp. Browser not found: {mapping.BrowserProcess}.");
                continue;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = browser,
                    WorkingDirectory = _repoRoot,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("https://web.whatsapp.com/");
                Process.Start(startInfo);
                opened++;
                WriteLog($"{mapping.ChannelId}: opening WhatsApp in {mapping.BrowserProcess}.");
            }
            catch (Exception exception)
            {
                WriteLog($"{mapping.ChannelId}: could not open WhatsApp: {exception.Message}");
            }
        }

        if (opened == 0)
        {
            WriteLog("No missing WhatsApp windows were opened.");
        }
    }

    public async Task<PreflightReport> PrepareWhatsAppWorkspaceAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        WriteLog("Autonomous bootstrap: preparing WhatsApp 1/2/3.");

        while (DateTimeOffset.Now < deadline)
        {
            var windows = VisibleWindows();
            var missing = ReadChannelMappings()
                .Where(mapping => !FindMatchingWindows(windows, mapping).Any())
                .Select(mapping => mapping.ChannelId)
                .ToArray();

            if (missing.Length == 0)
            {
                ArrangeWhatsAppWorkspace();
                WriteLog("Autonomous bootstrap: all WhatsApp channels are visible and arranged.");
                return Preflight();
            }

            WriteLog($"Autonomous bootstrap: missing {string.Join(", ", missing)}. Opening required browsers.");
            OpenMissingWhatsApps();
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(false);
        }

        ArrangeWhatsAppWorkspace();
        var finalReport = Preflight();
        var stillMissing = finalReport.Items
            .Where(item => item.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase)
                && item.Severity == HealthSeverity.Warning)
            .Select(item => item.Name)
            .ToArray();
        WriteLog(stillMissing.Length == 0
            ? "Autonomous bootstrap: WhatsApp workspace ready after wait."
            : $"Autonomous bootstrap: still needs attention: {string.Join(", ", stillMissing)}.");
        return finalReport;
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
                MoveWindow(window.Handle, left, area.Top, width, area.Height, repaint: true);
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
        Stop();
        _coreLoopCts?.Dispose();
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
            var matches = FindMatchingWindows(windows, mapping).ToArray();
            if (matches.Length == 0)
            {
                items.Add(new HealthItem(
                    $"WhatsApp {mapping.ChannelId}",
                    "FALTA",
                    HealthSeverity.Warning,
                    DateTimeOffset.Now,
                    $"No veo WhatsApp visible en {mapping.BrowserProcess}. Pulsa Preparar WhatsApps o abre web.whatsapp.com en ese navegador."));
                continue;
            }

            var best = matches[0];
            items.Add(new HealthItem(
                $"WhatsApp {mapping.ChannelId}",
                "VISIBLE",
                HealthSeverity.Ok,
                DateTimeOffset.Now,
                $"{best.ProcessName} #{best.ProcessId}: {best.Title}"));
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
            : new HealthItem("PythonCoreLoop", "ACTIVO", HealthSeverity.Ok, DateTimeOffset.Now, "Timeline, Cognitive, Operating, Memory y Supervisor estan ciclando.");
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

        StartProcess(name, spec.FileName, spec.Arguments, spec.WorkingDirectory);
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

    private void StartCoreLoop()
    {
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

                await Task.Delay(TimeSpan.FromSeconds(1.5), _coreLoopCts.Token).ConfigureAwait(false);
            }

            WriteLog("Python core loop stopped.");
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
            ("Timeline", "ariadgsm_agent.timeline", new[] { "--json" }),
            ("Cognitive", "ariadgsm_agent.cognitive", new[] { "--autonomy-level", "3", "--json" }),
            ("Operating", "ariadgsm_agent.operating", new[] { "--autonomy-level", "3", "--json" }),
            ("Memory", "ariadgsm_agent.memory", new[] { "--json" }),
            ("Supervisor", "ariadgsm_agent.supervisor", new[] { "--autonomy-level", "3", "--json" })
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
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_gate)
        {
            _processes.Add(new ManagedProcess(name, process));
        }

        WriteLog($"{name} started pid={process.Id}");
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
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                WriteLogNoThrow($"{name}: {eventArgs.Data}");
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                WriteLogNoThrow($"{name} error: {eventArgs.Data}");
            }
        };
        process.Exited += (_, _) => WriteLogNoThrow($"{name} stopped.");
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

    private void TryStop(ManagedProcess item)
    {
        try
        {
            if (!item.Process.HasExited)
            {
                item.Process.Kill(entireProcessTree: true);
                item.Process.WaitForExit(3000);
                WriteLog($"{item.Name} killed.");
            }
        }
        catch (Exception exception)
        {
            WriteLog($"Could not stop {item.Name}: {exception.Message}");
        }
        finally
        {
            item.Process.Dispose();
        }
    }

    private bool IsManagedProcessActive(string name)
    {
        if (name.Equals("PythonCoreLoop", StringComparison.OrdinalIgnoreCase))
        {
            return _coreLoopTask is { IsCompleted: false };
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
        try
        {
            Directory.CreateDirectory(_runtimeDir);
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
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
        var exe = NormalizeProcessName(processName) switch
        {
            "msedge" => "msedge.exe",
            "chrome" => "chrome.exe",
            "firefox" => "firefox.exe",
            var value when value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => value,
            var value => $"{value}.exe"
        };

        return ResolveExecutable(null, exe);
    }

    private static string? ResolveExecutable(string? envName, string executableName)
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
                if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(browserProcess))
                {
                    result.Add(new ChannelMapping(channelId, browserProcess, titleContains));
                }
            }

            return result.Count == 0 ? DefaultChannelMappings() : result;
        }
        catch
        {
            return DefaultChannelMappings();
        }
    }

    private static IReadOnlyList<ChannelMapping> DefaultChannelMappings()
    {
        return
        [
            new("wa-1", "msedge", "WhatsApp"),
            new("wa-2", "chrome", "WhatsApp"),
            new("wa-3", "firefox", "WhatsApp")
        ];
    }

    private static IEnumerable<WindowInfo> FindMatchingWindows(IReadOnlyList<WindowInfo> windows, ChannelMapping mapping)
    {
        var process = NormalizeProcessName(mapping.BrowserProcess);
        return windows.Where(window =>
            NormalizeProcessName(window.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase)
            && window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<WindowInfo> VisibleWindows()
    {
        var windows = new List<WindowInfo>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
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
            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new WindowInfo(handle, (int)processId, process.ProcessName, title));
            }
            catch
            {
                windows.Add(new WindowInfo(handle, (int)processId, "unknown", title));
            }

            return true;
        }, IntPtr.Zero);
        return windows;
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

    private sealed record ManagedProcess(string Name, Process Process);

    private sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

    private sealed record ChannelMapping(string ChannelId, string BrowserProcess, string TitleContains);

    private sealed record WindowInfo(IntPtr Handle, int ProcessId, string ProcessName, string Title);
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
    JsonDocument? Timeline,
    JsonDocument? Cognitive,
    JsonDocument? Operating,
    JsonDocument? Memory,
    JsonDocument? Hands,
    JsonDocument? Supervisor,
    IReadOnlyList<string> Processes);
