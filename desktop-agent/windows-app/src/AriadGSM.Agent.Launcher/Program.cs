using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace AriadGSM.Agent.Launcher;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [STAThread]
    private static int Main(string[] args)
    {
        SetCurrentProcessExplicitAppUserModelID("AriadGSM.Agent");

        try
        {
            if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }

            var repoRoot = LocateRepoRoot();
            var desktopRoot = Path.Combine(repoRoot, "desktop-agent");
            var runtimeDir = Path.Combine(desktopRoot, "runtime");
            Directory.CreateDirectory(runtimeDir);

            var activeFile = Path.Combine(runtimeDir, "active-version.json");
            var launchStateFile = Path.Combine(runtimeDir, "launch-state.json");
            var state = ReadActiveVersion(activeFile, desktopRoot);
            var appDir = ResolveAgentDirectory(state, desktopRoot);
            var appExe = Path.Combine(appDir, "AriadGSM Agent.exe");

            if (!File.Exists(appExe))
            {
                throw new FileNotFoundException("No encontre AriadGSM Agent.exe para iniciar.", appExe);
            }

            WriteLaunchState(launchStateFile, "starting", state.ActiveVersion, appExe, "Abriendo AriadGSM Agent.");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = appExe,
                WorkingDirectory = appDir,
                UseShellExecute = true
            }.WithArguments(args));

            if (process is null)
            {
                throw new InvalidOperationException("Windows no devolvio proceso al abrir AriadGSM Agent.");
            }

            var quickExit = process.WaitForExit(45_000);
            if (!quickExit)
            {
                ConfirmLaunch(activeFile, state);
                WriteLaunchState(launchStateFile, "ok", state.ActiveVersion, appExe, $"AriadGSM Agent quedo vivo. pid={process.Id}");
                return 0;
            }

            var exitCode = SafeExitCode(process);
            WriteLaunchState(launchStateFile, "failed", state.ActiveVersion, appExe, $"AriadGSM Agent salio rapido. exitCode={exitCode}");
            if (!string.IsNullOrWhiteSpace(state.PreviousVersion))
            {
                var previousDir = Path.Combine(desktopRoot, "versions", state.PreviousVersion);
                var previousExe = Path.Combine(previousDir, "AriadGSM Agent.exe");
                if (File.Exists(previousExe))
                {
                    var rollback = state with
                    {
                        ActiveVersion = state.PreviousVersion,
                        ActiveDirectory = previousDir,
                        PendingConfirmation = false,
                        RollbackReason = $"La version {state.ActiveVersion} salio rapido con codigo {exitCode}.",
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    WriteActiveVersion(activeFile, rollback);
                    WriteLaunchState(launchStateFile, "rollback", rollback.ActiveVersion, previousExe, rollback.RollbackReason ?? "Rollback automatico.");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = previousExe,
                        WorkingDirectory = previousDir,
                        UseShellExecute = true
                    }.WithArguments(args));
                    return 0;
                }
            }

            ShowError($"AriadGSM Agent se cerro al iniciar. Codigo: {exitCode}.");
            return exitCode == 0 ? 1 : exitCode;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            return 2;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static ActiveVersionState ReadActiveVersion(string activeFile, string desktopRoot)
    {
        if (File.Exists(activeFile))
        {
            try
            {
                using var document = JsonDocument.Parse(ReadAllTextShared(activeFile));
                var root = document.RootElement;
                var version = TryString(root, "activeVersion") ?? string.Empty;
                var activeDirectory = TryString(root, "activeDirectory") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return new ActiveVersionState(
                        version,
                        activeDirectory,
                        TryString(root, "previousVersion"),
                        TryString(root, "previousDirectory"),
                        TryBool(root, "pendingConfirmation") ?? false,
                        TryString(root, "rollbackReason"),
                        TryDate(root, "updatedAt") ?? DateTimeOffset.UtcNow);
                }
            }
            catch
            {
            }
        }

        var legacyDir = Path.Combine(desktopRoot, "dist", "AriadGSMAgent");
        var legacyVersion = ReadVersion(legacyDir);
        return new ActiveVersionState(legacyVersion, legacyDir, null, null, false, null, DateTimeOffset.UtcNow);
    }

    private static string ResolveAgentDirectory(ActiveVersionState state, string desktopRoot)
    {
        if (!string.IsNullOrWhiteSpace(state.ActiveDirectory)
            && Directory.Exists(state.ActiveDirectory))
        {
            return state.ActiveDirectory;
        }

        var versionDir = Path.Combine(desktopRoot, "versions", state.ActiveVersion);
        if (Directory.Exists(versionDir))
        {
            return versionDir;
        }

        return Path.Combine(desktopRoot, "dist", "AriadGSMAgent");
    }

    private static void ConfirmLaunch(string activeFile, ActiveVersionState state)
    {
        if (!state.PendingConfirmation)
        {
            return;
        }

        WriteActiveVersion(activeFile, state with
        {
            PendingConfirmation = false,
            RollbackReason = null,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void WriteActiveVersion(string activeFile, ActiveVersionState state)
    {
        WriteAllTextAtomicShared(activeFile, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static void WriteLaunchState(string path, string status, string version, string executable, string detail)
    {
        var state = new
        {
            status,
            version,
            executable,
            detail,
            updatedAt = DateTimeOffset.UtcNow
        };
        WriteAllTextAtomicShared(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static string ReadVersion(string directory)
    {
        var versionFile = Path.Combine(directory, "ariadgsm-version.json");
        if (!File.Exists(versionFile))
        {
            return "0.0.0-dev";
        }

        try
        {
            using var document = JsonDocument.Parse(ReadAllTextShared(versionFile));
            return TryString(document.RootElement, "version") ?? "0.0.0-dev";
        }
        catch
        {
            return "0.0.0-dev";
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
            return -1;
        }
    }

    private static string LocateRepoRoot()
    {
        var candidates = new[]
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

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "AriadGSM Launcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteAllTextAtomicShared(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, text, Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
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
}

internal sealed record ActiveVersionState(
    string ActiveVersion,
    string ActiveDirectory,
    string? PreviousVersion,
    string? PreviousDirectory,
    bool PendingConfirmation,
    string? RollbackReason,
    DateTimeOffset UpdatedAt);
