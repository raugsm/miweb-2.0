using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AriadGSM.Agent.Updater;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        var parsed = Args.Parse(args);
        var stateFile = parsed.Value("--state")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AriadGSM", "update-state.json");
        var journalFile = Path.Combine(Path.GetDirectoryName(stateFile) ?? Environment.CurrentDirectory, "update-journal.jsonl");

        try
        {
            if (parsed.Has("--check"))
            {
                Check(parsed, stateFile, journalFile);
                return 0;
            }

            if (parsed.Has("--apply"))
            {
                ApplyVersioned(parsed, stateFile, journalFile);
                return 0;
            }

            if (parsed.Has("--rollback"))
            {
                Rollback(parsed, stateFile, journalFile);
                return 0;
            }

            WriteState(stateFile, journalFile, UpdateState.Failed("No command supplied. Use --check, --apply or --rollback."));
            return 1;
        }
        catch (Exception exception)
        {
            WriteState(stateFile, journalFile, UpdateState.Failed(exception.Message));
            return 2;
        }
    }

    private static void Check(Args args, string stateFile, string journalFile)
    {
        var currentDir = RequiredDirectory(args.Value("--current-dir"), "--current-dir");
        var manifestSource = RequiredValue(args.Value("--manifest"), "--manifest");
        var manifest = ReadManifest(manifestSource);
        var current = ReadCurrentVersion(currentDir);
        var available = CompareVersions(manifest.Version, current) > 0 && !string.IsNullOrWhiteSpace(manifest.PackageUrl);
        var status = available ? "available" : "current";
        var detail = available
            ? $"Version {manifest.Version} disponible."
            : $"Version actual {current}. No hay actualizacion aplicable.";

        WriteState(stateFile, journalFile, new UpdateState(
            status,
            current,
            manifest.Version,
            manifest.PackageUrl,
            manifest.Sha256,
            manifest.AutoApply,
            detail,
            DateTimeOffset.UtcNow,
            null,
            null,
            null));
    }

    private static void ApplyVersioned(Args args, string stateFile, string journalFile)
    {
        var currentDir = RequiredDirectory(args.Value("--current-dir"), "--current-dir");
        var installRoot = ResolveInstallRoot(args.Value("--install-root"), currentDir);
        var runtimeDir = Path.Combine(installRoot, "runtime");
        var versionsDir = Path.Combine(installRoot, "versions");
        var launcherDir = Path.Combine(installRoot, "launcher");
        var activeVersionFile = Path.Combine(runtimeDir, "active-version.json");
        var packageSource = RequiredValue(args.Value("--package"), "--package");
        var requestedVersion = args.Value("--version");
        var sha256 = args.Value("--sha256") ?? string.Empty;
        var noRestart = args.Has("--no-restart");

        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(versionsDir);

        var workDir = Path.Combine(runtimeDir, "updates", DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        var packagePath = Path.Combine(workDir, "AriadGSMAgent-update.zip");
        var stagingDir = Path.Combine(workDir, "staging");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(stagingDir);

        WriteState(stateFile, journalFile, UpdateState.Applying("Esperando que AriadGSM Agent cierre.", installRoot));
        if (int.TryParse(args.Value("--wait-pid"), out var pid) && pid > 0)
        {
            WaitForProcessExit(pid, TimeSpan.FromSeconds(45));
        }

        WriteState(stateFile, journalFile, UpdateState.Applying("Descargando o copiando paquete.", installRoot));
        CopyOrDownload(packageSource, packagePath);

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var actual = ComputeSha256(packagePath);
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hash SHA256 invalido. Esperado {sha256}, recibido {actual}.");
            }
        }

        WriteState(stateFile, journalFile, UpdateState.Applying("Extrayendo paquete en staging.", installRoot));
        ZipFile.ExtractToDirectory(packagePath, stagingDir, overwriteFiles: true);
        ValidatePackage(stagingDir);

        var version = !string.IsNullOrWhiteSpace(requestedVersion)
            ? requestedVersion
            : ReadCurrentVersion(stagingDir);
        if (string.IsNullOrWhiteSpace(version) || version.Equals("0.0.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El paquete no declara una version valida.");
        }

        WriteState(stateFile, journalFile, UpdateState.Applying($"Probando version {version} antes de activarla.", installRoot, version));
        RunSelfTest(stagingDir);

        var previous = ReadActiveVersion(activeVersionFile, currentDir);
        var targetDir = Path.Combine(versionsDir, version);
        WriteState(stateFile, journalFile, UpdateState.Applying($"Instalando version {version} en carpeta aislada.", installRoot, version, targetDir));
        ReplaceDirectory(stagingDir, targetDir);

        var stagedLauncher = Path.Combine(targetDir, "launcher");
        if (Directory.Exists(stagedLauncher))
        {
            WriteState(stateFile, journalFile, UpdateState.Applying("Actualizando launcher estable.", installRoot, version, targetDir));
            ReplaceDirectory(stagedLauncher, launcherDir);
        }

        var active = new ActiveVersionState(
            version,
            targetDir,
            previous.ActiveVersion,
            previous.ActiveDirectory,
            PendingConfirmation: !noRestart,
            RollbackReason: null,
            UpdatedAt: DateTimeOffset.UtcNow);
        WriteActiveVersion(activeVersionFile, active);

        var launcherExe = Path.Combine(launcherDir, "AriadGSM Launcher.exe");
        var explicitRestart = args.Value("--restart");
        var restartPath = File.Exists(launcherExe)
            ? launcherExe
            : !string.IsNullOrWhiteSpace(explicitRestart)
                ? explicitRestart
                : Path.Combine(targetDir, "AriadGSM Agent.exe");

        WriteState(stateFile, journalFile, UpdateState.Applied($"Version {version} activada. Anterior: {previous.ActiveVersion}.", installRoot, version, targetDir));

        if (noRestart)
        {
            WriteState(stateFile, journalFile, UpdateState.Applied($"Version {version} activada. Reinicio omitido por --no-restart.", installRoot, version, targetDir));
            return;
        }

        if (!File.Exists(restartPath))
        {
            WriteState(stateFile, journalFile, UpdateState.Applied($"Version {version} activada, pero no encontre reinicio: {restartPath}.", installRoot, version, targetDir));
            return;
        }

        if (!LooksLikeWindowsExecutable(restartPath))
        {
            WriteState(stateFile, journalFile, UpdateState.Applied($"Version {version} activada, pero el reinicio no parece ejecutable Windows: {restartPath}.", installRoot, version, targetDir));
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = restartPath,
            WorkingDirectory = Path.GetDirectoryName(restartPath) ?? installRoot,
            UseShellExecute = true
        });
    }

    private static void Rollback(Args args, string stateFile, string journalFile)
    {
        var currentDir = RequiredDirectory(args.Value("--current-dir"), "--current-dir");
        var installRoot = ResolveInstallRoot(args.Value("--install-root"), currentDir);
        var activeVersionFile = Path.Combine(installRoot, "runtime", "active-version.json");
        var current = ReadActiveVersion(activeVersionFile, currentDir);
        if (string.IsNullOrWhiteSpace(current.PreviousVersion) || string.IsNullOrWhiteSpace(current.PreviousDirectory))
        {
            throw new InvalidOperationException("No hay version anterior registrada para rollback.");
        }

        var rollback = current with
        {
            ActiveVersion = current.PreviousVersion,
            ActiveDirectory = current.PreviousDirectory,
            PendingConfirmation = false,
            RollbackReason = args.Value("--reason") ?? "Rollback solicitado.",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        WriteActiveVersion(activeVersionFile, rollback);
        WriteState(stateFile, journalFile, UpdateState.Applied($"Rollback aplicado a {rollback.ActiveVersion}.", installRoot, rollback.ActiveVersion, rollback.ActiveDirectory));
    }

    private static void ValidatePackage(string packageDir)
    {
        var required = new[]
        {
            "AriadGSM Agent.exe",
            "ariadgsm-version.json",
            Path.Combine("engines", "vision", "AriadGSM.Vision.Worker.exe"),
            Path.Combine("engines", "perception", "AriadGSM.Perception.Worker.exe"),
            Path.Combine("engines", "interaction", "AriadGSM.Interaction.Worker.exe"),
            Path.Combine("engines", "hands", "AriadGSM.Hands.Worker.exe"),
            Path.Combine("config", "vision.json"),
            Path.Combine("config", "perception.json"),
            Path.Combine("config", "interaction.json"),
            Path.Combine("config", "hands.json")
        };

        foreach (var relative in required)
        {
            var path = Path.Combine(packageDir, relative);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Paquete incompleto. Falta: {relative}");
            }
        }
    }

    private static void RunSelfTest(string packageDir)
    {
        var agentExe = Path.Combine(packageDir, "AriadGSM Agent.exe");
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = agentExe,
                WorkingDirectory = packageDir,
                UseShellExecute = false,
                CreateNoWindow = true
            }.WithArguments(["--self-test"]));
        }
        catch (Exception exception) when (exception.Message.Contains("elevaci", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("elevation", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (process is null)
        {
            throw new InvalidOperationException("No pude ejecutar self-test de AriadGSM Agent.");
        }

        if (!process.WaitForExit(30_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new InvalidOperationException("Self-test de AriadGSM Agent excedio 30 segundos.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Self-test de AriadGSM Agent fallo con codigo {process.ExitCode}.");
        }
    }

    private static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    private static string ResolveInstallRoot(string? configured, string currentDir)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return RequiredDirectory(configured, "--install-root");
        }

        var current = new DirectoryInfo(currentDir);
        while (current is not null)
        {
            if (current.Name.Equals("desktop-agent", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            if (Directory.Exists(Path.Combine(current.FullName, "desktop-agent")))
            {
                return Path.Combine(current.FullName, "desktop-agent");
            }

            current = current.Parent;
        }

        var parent = Directory.GetParent(currentDir)?.FullName;
        if (parent is null)
        {
            throw new InvalidOperationException("No pude resolver install-root del agente.");
        }

        return parent;
    }

    private static ActiveVersionState ReadActiveVersion(string activeFile, string currentDir)
    {
        if (File.Exists(activeFile))
        {
            try
            {
                using var document = JsonDocument.Parse(ReadAllTextShared(activeFile));
                var root = document.RootElement;
                var version = TryString(root, "activeVersion") ?? ReadCurrentVersion(currentDir);
                return new ActiveVersionState(
                    version,
                    TryString(root, "activeDirectory") ?? currentDir,
                    TryString(root, "previousVersion"),
                    TryString(root, "previousDirectory"),
                    TryBool(root, "pendingConfirmation") ?? false,
                    TryString(root, "rollbackReason"),
                    TryDate(root, "updatedAt") ?? DateTimeOffset.UtcNow);
            }
            catch
            {
            }
        }

        return new ActiveVersionState(ReadCurrentVersion(currentDir), currentDir, null, null, false, null, DateTimeOffset.UtcNow);
    }

    private static void WriteActiveVersion(string activeFile, ActiveVersionState state)
    {
        WriteAllTextAtomicShared(activeFile, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static UpdateManifest ReadManifest(string source)
    {
        var json = ReadText(source);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
        {
            throw new InvalidOperationException("Manifest de actualizacion invalido.");
        }

        return manifest;
    }

    private static string ReadText(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            return client.GetStringAsync(uri).GetAwaiter().GetResult();
        }

        return ReadAllTextShared(source);
    }

    private static void CopyOrDownload(string source, string destination)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using var stream = client.GetStreamAsync(uri).GetAwaiter().GetResult();
            using var output = File.Create(destination);
            stream.CopyTo(output);
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static string ReadCurrentVersion(string currentDir)
    {
        var versionFile = Path.Combine(currentDir, "ariadgsm-version.json");
        if (!File.Exists(versionFile))
        {
            return "0.0.0";
        }

        using var document = JsonDocument.Parse(ReadAllTextShared(versionFile));
        return document.RootElement.TryGetProperty("version", out var version)
            && version.ValueKind == JsonValueKind.String
            ? version.GetString() ?? "0.0.0"
            : "0.0.0";
    }

    private static int CompareVersions(string left, string right)
    {
        return NormalizeVersion(left).CompareTo(NormalizeVersion(right));
    }

    private static Version NormalizeVersion(string value)
    {
        return Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private static void WaitForProcessExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ReplaceDirectory(string sourceDir, string destinationDir)
    {
        var parent = Directory.GetParent(destinationDir)?.FullName
            ?? throw new InvalidOperationException($"Ruta destino invalida: {destinationDir}");
        Directory.CreateDirectory(parent);

        if (Directory.Exists(destinationDir))
        {
            var oldDir = $"{destinationDir}.old-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            Directory.Move(destinationDir, oldDir);
            TryDeleteDirectory(oldDir);
        }

        CopyDirectory(sourceDir, destinationDir, overwrite: true);
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch when (attempt < 4)
            {
                Thread.Sleep(150 * (attempt + 1));
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, directory));
            Directory.CreateDirectory(target);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destinationDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static bool LooksLikeWindowsExecutable(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[2];
            using var stream = File.OpenRead(path);
            return stream.Read(header) == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
        }
        catch
        {
            return false;
        }
    }

    private static string RequiredValue(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} es requerido.")
            : value;
    }

    private static string RequiredDirectory(string? value, string name)
    {
        var path = RequiredValue(value, name);
        return Directory.Exists(path)
            ? Path.GetFullPath(path)
            : throw new DirectoryNotFoundException($"{name} no existe: {path}");
    }

    private static void WriteState(string stateFile, string journalFile, UpdateState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        WriteAllTextAtomicShared(stateFile, JsonSerializer.Serialize(state, JsonOptions));
        AppendJournal(journalFile, state);
    }

    private static void AppendJournal(string journalFile, UpdateState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journalFile)!);
            File.AppendAllText(journalFile, JsonSerializer.Serialize(state, JsonOptions).ReplaceLineEndings(" ") + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void WriteAllTextAtomicShared(string path, string text)
    {
        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                }
            }
        }
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

internal sealed record UpdateManifest(
    string Version,
    string PackageUrl,
    string Sha256,
    bool AutoApply,
    string? Notes = null,
    string? PublishedAt = null);

internal sealed record UpdateState(
    string Status,
    string CurrentVersion,
    string LatestVersion,
    string? PackageUrl,
    string? Sha256,
    bool AutoApply,
    string Detail,
    DateTimeOffset UpdatedAt,
    string? InstallRoot,
    string? TargetVersion,
    string? TargetDirectory)
{
    public static UpdateState Applying(string detail, string? installRoot = null, string? targetVersion = null, string? targetDirectory = null)
    {
        return new("applying", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow, installRoot, targetVersion, targetDirectory);
    }

    public static UpdateState Applied(string detail, string? installRoot = null, string? targetVersion = null, string? targetDirectory = null)
    {
        return new("applied", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow, installRoot, targetVersion, targetDirectory);
    }

    public static UpdateState Failed(string detail)
    {
        return new("failed", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow, null, null, null);
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

internal sealed class Args
{
    private readonly Dictionary<string, string?> _values;

    private Args(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public static Args Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Count; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : null;
            values[key] = value;
        }

        return new Args(values);
    }

    public bool Has(string key)
    {
        return _values.ContainsKey(key);
    }

    public string? Value(string key)
    {
        return _values.TryGetValue(key, out var value) ? value : null;
    }
}
