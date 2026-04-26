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

        try
        {
            if (parsed.Has("--check"))
            {
                Check(parsed, stateFile);
                return 0;
            }

            if (parsed.Has("--apply"))
            {
                Apply(parsed, stateFile);
                return 0;
            }

            WriteState(stateFile, UpdateState.Failed("No command supplied. Use --check or --apply."));
            return 1;
        }
        catch (Exception exception)
        {
            WriteState(stateFile, UpdateState.Failed(exception.Message));
            return 2;
        }
    }

    private static void Check(Args args, string stateFile)
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

        WriteState(stateFile, new UpdateState(
            status,
            current,
            manifest.Version,
            manifest.PackageUrl,
            manifest.Sha256,
            manifest.AutoApply,
            detail,
            DateTimeOffset.UtcNow));
    }

    private static void Apply(Args args, string stateFile)
    {
        var currentDir = RequiredDirectory(args.Value("--current-dir"), "--current-dir");
        var packageSource = RequiredValue(args.Value("--package"), "--package");
        var restart = args.Value("--restart");
        var noRestart = args.Has("--no-restart");
        var sha256 = args.Value("--sha256") ?? string.Empty;

        WriteState(stateFile, UpdateState.Applying("Esperando que AriadGSM Agent cierre."));
        if (int.TryParse(args.Value("--wait-pid"), out var pid) && pid > 0)
        {
            WaitForProcessExit(pid, TimeSpan.FromSeconds(45));
        }

        var workDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AriadGSM",
            "updates",
            DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"));
        var packagePath = Path.Combine(workDir, "AriadGSMAgent-update.zip");
        var stagingDir = Path.Combine(workDir, "staging");
        var parentDir = Directory.GetParent(currentDir)?.FullName
            ?? throw new InvalidOperationException("No pude ubicar la carpeta padre del agente.");
        var backupDir = Path.Combine(parentDir, $"AriadGSMAgent-backup-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");

        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(stagingDir);

        WriteState(stateFile, UpdateState.Applying("Descargando o copiando paquete."));
        CopyOrDownload(packageSource, packagePath);

        if (!string.IsNullOrWhiteSpace(sha256))
        {
            var actual = ComputeSha256(packagePath);
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Hash SHA256 invalido. Esperado {sha256}, recibido {actual}.");
            }
        }

        WriteState(stateFile, UpdateState.Applying("Extrayendo paquete en staging."));
        ZipFile.ExtractToDirectory(packagePath, stagingDir, overwriteFiles: true);
        var stagedAgent = Path.Combine(stagingDir, "AriadGSM Agent.exe");
        if (!File.Exists(stagedAgent))
        {
            throw new InvalidOperationException("El paquete no contiene AriadGSM Agent.exe en la raiz.");
        }

        WriteState(stateFile, UpdateState.Applying("Creando respaldo de la version anterior."));
        CopyDirectory(currentDir, backupDir, overwrite: false);

        try
        {
            WriteState(stateFile, UpdateState.Applying("Aplicando nueva version."));
            CopyDirectory(stagingDir, currentDir, overwrite: true);
        }
        catch
        {
            CopyDirectory(backupDir, currentDir, overwrite: true);
            throw;
        }

        var restartPath = !string.IsNullOrWhiteSpace(restart)
            ? restart
            : Path.Combine(currentDir, "AriadGSM Agent.exe");
        WriteState(stateFile, UpdateState.Applied($"Actualizacion aplicada. Respaldo: {backupDir}"));

        if (noRestart)
        {
            WriteState(stateFile, UpdateState.Applied($"Actualizacion aplicada. Reinicio omitido por --no-restart. Respaldo: {backupDir}"));
            return;
        }

        if (File.Exists(restartPath))
        {
            if (!LooksLikeWindowsExecutable(restartPath))
            {
                WriteState(stateFile, UpdateState.Applied($"Actualizacion aplicada, pero no reinicie porque el archivo no parece un ejecutable Windows valido: {restartPath}. Respaldo: {backupDir}"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = restartPath,
                    WorkingDirectory = currentDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception exception)
            {
                WriteState(stateFile, UpdateState.Applied($"Actualizacion aplicada, pero no pude reiniciar: {exception.Message}. Respaldo: {backupDir}"));
            }
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
            // The process already exited or cannot be observed; continue applying.
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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

    private static void WriteState(string stateFile, UpdateState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        WriteAllTextAtomicShared(stateFile, JsonSerializer.Serialize(state, JsonOptions));
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
    DateTimeOffset UpdatedAt)
{
    public static UpdateState Applying(string detail)
    {
        return new("applying", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow);
    }

    public static UpdateState Applied(string detail)
    {
        return new("applied", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow);
    }

    public static UpdateState Failed(string detail)
    {
        return new("failed", "unknown", "unknown", null, null, false, detail, DateTimeOffset.UtcNow);
    }
}

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
