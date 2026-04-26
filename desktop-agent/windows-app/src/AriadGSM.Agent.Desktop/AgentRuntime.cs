using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace AriadGSM.Agent.Desktop;

internal sealed class AgentRuntime : IDisposable
{
    private readonly List<ManagedProcess> _processes = [];
    private readonly object _gate = new();
    private readonly string _repoRoot;
    private readonly string _desktopRoot;
    private readonly string _runtimeDir;
    private readonly string _logFile;
    private CancellationTokenSource? _coreLoopCts;
    private Task? _coreLoopTask;

    public AgentRuntime()
    {
        _repoRoot = LocateRepoRoot();
        _desktopRoot = Path.Combine(_repoRoot, "desktop-agent");
        _runtimeDir = Path.Combine(_desktopRoot, "runtime");
        Directory.CreateDirectory(_runtimeDir);
        _logFile = Path.Combine(_runtimeDir, "windows-app.log");
    }

    public string RepoRoot => _repoRoot;

    public string RuntimeDir => _runtimeDir;

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

        Directory.CreateDirectory(_runtimeDir);
        WriteLog("Starting AriadGSM Agent without PowerShell.");
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
            ReadJsonStatus("timeline-state.json"),
            ReadJsonStatus("cognitive-state.json"),
            ReadJsonStatus("operating-state.json"),
            ReadJsonStatus("memory-state.json"),
            ReadJsonStatus("hands-state.json"),
            ReadJsonStatus("supervisor-state.json"),
            ActiveProcesses());
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
            File.WriteAllText(_logFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} Log file created.{Environment.NewLine}");
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

    private void StartWebPanel()
    {
        if (IsTcpPortOpen("127.0.0.1", 3000))
        {
            WriteLog("WebPanel already listening on http://127.0.0.1:3000.");
            return;
        }

        var node = ResolveExecutable("ARIADGSM_NODE", "node.exe");
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
        var exePath = Path.Combine(_repoRoot, packagedExe);
        var config = Path.Combine(_repoRoot, configPath);
        if (File.Exists(exePath))
        {
            var args = once ? new[] { config, "--once" } : new[] { config };
            return new ProcessSpec(exePath, args, _repoRoot);
        }

        var dotnet = ResolveExecutable("ARIADGSM_DOTNET", "dotnet.exe");
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
                WriteLog($"{name}: {eventArgs.Data}");
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                WriteLog($"{name} error: {eventArgs.Data}");
            }
        };
        process.Exited += (_, _) => WriteLog($"{name} stopped.");
        return process;
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

    private IReadOnlyList<string> ActiveProcesses()
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

    private JsonDocument? ReadJsonStatus(string fileName)
    {
        var path = Path.Combine(_runtimeDir, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private void WriteLog(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";
        Directory.CreateDirectory(_runtimeDir);
        File.AppendAllText(_logFile, line + Environment.NewLine);
        LogReceived?.Invoke(line);
    }

    private string? ResolvePython()
    {
        var configured = ResolveExecutable("ARIADGSM_PYTHON", "python.exe");
        if (configured is not null && CanRun(configured, "--version"))
        {
            return configured;
        }

        var bundled = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "codex-runtimes",
            "codex-primary-runtime",
            "dependencies",
            "python",
            "python.exe");
        return File.Exists(bundled) && CanRun(bundled, "--version") ? bundled : null;
    }

    private static string? ResolveExecutable(string envName, string executableName)
    {
        var configured = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
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

    private sealed record ManagedProcess(string Name, Process Process);

    private sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);
}

internal sealed record AgentSnapshot(
    JsonDocument? Vision,
    JsonDocument? Perception,
    JsonDocument? Timeline,
    JsonDocument? Cognitive,
    JsonDocument? Operating,
    JsonDocument? Memory,
    JsonDocument? Hands,
    JsonDocument? Supervisor,
    IReadOnlyList<string> Processes);
