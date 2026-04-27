using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private static readonly TimeSpan WorkspaceGuardianInterval = TimeSpan.FromMilliseconds(900);

    private string WorkspaceGuardianStateFile => Path.Combine(_runtimeDir, "workspace-guardian-state.json");
    private string CabinAuthorityStateFile => Path.Combine(_runtimeDir, "cabin-authority-state.json");

    private void StartWorkspaceGuardianLoop()
    {
        if (_workspaceGuardianTask is { IsCompleted: false })
        {
            return;
        }

        _workspaceGuardianCts?.Dispose();
        _workspaceGuardianCts = new CancellationTokenSource();
        var token = _workspaceGuardianCts.Token;
        _workspaceGuardianTask = Task.Run(async () =>
        {
            WriteLog("Workspace Guardian started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var report = EnsureWorkspaceOwned("loop");
                    WriteWorkspaceGuardianState(report);
                    await Task.Delay(WorkspaceGuardianInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    WriteWorkspaceGuardianState(WorkspaceGuardianReport.Attention($"Guardian cabina fallo: {exception.Message}"));
                    WriteLog($"Workspace Guardian error: {exception.Message}");
                    await Task.Delay(WorkspaceGuardianInterval).ConfigureAwait(false);
                }
            }

            WriteWorkspaceGuardianState(WorkspaceGuardianReport.Stopped());
            WriteLog("Workspace Guardian stopped.");
        });
    }

    private void StopWorkspaceGuardianLoop()
    {
        _workspaceGuardianCts?.Cancel();
    }

    private WorkspaceGuardianReport EnsureWorkspaceOwned(string reason)
    {
        var mappings = ReadChannelMappings().ToArray();
        if (mappings.Length == 0)
        {
            return WorkspaceGuardianReport.Attention("No hay mapa de canales para custodiar.");
        }

        var canModifyWindows = CabinAuthorityCanModifyWindows(reason);
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var windows = VisibleWindows();
        var channels = new List<WorkspaceGuardianChannel>();
        var blockers = new List<WorkspaceGuardianBlocker>();
        var actions = new List<WorkspaceGuardianAction>();

        for (var index = 0; index < mappings.Length; index++)
        {
            var mapping = mappings[index];
            var expected = ExpectedColumn(area, index, mappings.Length);
            var target = FindWorkspaceTarget(windows, mapping);
            if (target is null)
            {
                blockers.Add(new WorkspaceGuardianBlocker(
                    mapping.ChannelId,
                    "whatsapp_window_missing",
                    $"{mapping.BrowserProcess} no tiene una ventana WhatsApp recuperable para {mapping.ChannelId}."));
                channels.Add(new WorkspaceGuardianChannel(mapping.ChannelId, mapping.BrowserProcess, "missing", expected, null, 0));
                continue;
            }

            var needsPlacement = WindowNeedsPlacement(target.Bounds, expected);
            var restored = false;
            if (canModifyWindows)
            {
                restored = RestoreAndPlaceWindow(target, expected, ForceForeground(reason));
            }

            if (restored)
            {
                actions.Add(new WorkspaceGuardianAction(
                    mapping.ChannelId,
                    "authority_place_whatsapp",
                    $"{mapping.ChannelId}: Cabin Authority coloco {target.ProcessName} #{target.ProcessId} en columna {index + 1}."));
            }
            else if (needsPlacement && !canModifyWindows)
            {
                actions.Add(new WorkspaceGuardianAction(
                    mapping.ChannelId,
                    "observe_needs_place",
                    $"{mapping.ChannelId}: necesita alistamiento; el monitor no movera ventanas en segundo plano."));
            }

            windows = VisibleWindows();
            target = FindWindowByHandle(windows, target.Handle) ?? FindWorkspaceTarget(windows, mapping) ?? target;
            var covering = FindWorkspaceBlockers(windows, target, expected, mappings).ToArray();
            foreach (var blocker in covering)
            {
                blockers.Add(new WorkspaceGuardianBlocker(
                    mapping.ChannelId,
                    "zone_covered",
                    $"{blocker.ProcessName} #{blocker.ProcessId} '{blocker.Title}' cubre {mapping.ChannelId}. No lo minimizo; solo reporto el bloqueo."));
            }

            if (covering.Length > 0 && canModifyWindows)
            {
                Thread.Sleep(80);
                windows = VisibleWindows();
                target = FindWindowByHandle(windows, target.Handle) ?? FindWorkspaceTarget(windows, mapping) ?? target;
                if (RestoreAndPlaceWindow(target, expected, forceForeground: false))
                {
                    actions.Add(new WorkspaceGuardianAction(
                        mapping.ChannelId,
                        "authority_reassert_whatsapp",
                        $"{mapping.ChannelId}: Cabin Authority recupero la columna sin cerrar ni minimizar otras ventanas."));
                }
            }

            var remaining = FindWorkspaceBlockers(VisibleWindows(), target, expected, mappings).Count();
            channels.Add(new WorkspaceGuardianChannel(
                mapping.ChannelId,
                mapping.BrowserProcess,
                remaining == 0 ? "ready" : "covered",
                expected,
                target,
                remaining));
        }

        var ready = channels.Count == mappings.Length && channels.All(item => item.Status.Equals("ready", StringComparison.OrdinalIgnoreCase));
        var summary = ready
            ? "Cabin Authority: las 3 columnas WhatsApp estan visibles y libres."
            : $"Cabina con atencion: {string.Join(", ", channels.Where(item => item.Status != "ready").Select(item => $"{item.ChannelId}={item.Status}"))}.";
        if (actions.Count > 0)
        {
            WriteLog($"Cabin Authority: {summary} Acciones={actions.Count}; motivo={reason}; modify={canModifyWindows}.");
        }

        return new WorkspaceGuardianReport(
            ready ? "ok" : "attention",
            ready ? "ready" : canModifyWindows ? "arranging" : "observing",
            summary,
            reason,
            channels,
            blockers.TakeLast(12).ToArray(),
            actions.TakeLast(12).ToArray());
    }

    private static WindowBounds ExpectedColumn(Rectangle area, int index, int count)
    {
        var columnWidth = Math.Max(500, area.Width / Math.Max(1, count));
        var left = area.Left + (index * columnWidth);
        var width = index == count - 1
            ? area.Right - left
            : columnWidth;
        return new WindowBounds(left, area.Top, width, area.Height);
    }

    private WindowInfo? FindWorkspaceTarget(IReadOnlyList<WindowInfo> windows, ChannelMapping mapping)
    {
        var visible = FindMatchingWindows(windows, mapping).FirstOrDefault();
        if (visible is not null)
        {
            return visible;
        }

        var processName = NormalizeProcessName(mapping.BrowserProcess);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero
                    || string.IsNullOrWhiteSpace(process.MainWindowTitle)
                    || !process.MainWindowTitle.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bounds = GetWindowRect(process.MainWindowHandle, out var rect)
                    ? new WindowBounds(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
                    : new WindowBounds(0, 0, 0, 0);
                return new WindowInfo(process.MainWindowHandle, process.Id, process.ProcessName, process.MainWindowTitle, bounds, int.MaxValue);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static WindowInfo? FindWindowByHandle(IReadOnlyList<WindowInfo> windows, IntPtr handle)
    {
        return windows.FirstOrDefault(item => item.Handle == handle);
    }

    private static bool ForceForeground(string reason)
    {
        return reason.Equals("bootstrap", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("prepare", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("arrange_windows", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CabinAuthorityCanModifyWindows(string reason)
    {
        return reason.Equals("bootstrap", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("prepare", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("manual_setup", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("arrange_windows", StringComparison.OrdinalIgnoreCase);
    }

    private bool RestoreAndPlaceWindow(WindowInfo window, WindowBounds expected, bool forceForeground)
    {
        try
        {
            var needsPlacement = WindowNeedsPlacement(window.Bounds, expected);
            if (!needsPlacement && !forceForeground)
            {
                return false;
            }

            ShowWindow(window.Handle, ShowWindowRestore);
            if (needsPlacement)
            {
                _ = SetWindowPos(window.Handle, HwndTop, expected.Left, expected.Top, expected.Width, expected.Height, SetWindowPosShowWindow);
            }

            if (forceForeground)
            {
                _ = BringWindowToTop(window.Handle);
                _ = SetForegroundWindow(window.Handle);
            }
            return true;
        }
        catch (Exception exception)
        {
            WriteLog($"{window.ProcessName} #{window.ProcessId}: guardian could not place window: {exception.Message}");
            return false;
        }
    }

    private static bool WindowNeedsPlacement(WindowBounds actual, WindowBounds expected)
    {
        const int positionTolerance = 10;
        const int sizeTolerance = 28;
        return actual.Width <= 0
            || actual.Height <= 0
            || Math.Abs(actual.Left - expected.Left) > positionTolerance
            || Math.Abs(actual.Top - expected.Top) > positionTolerance
            || Math.Abs(actual.Width - expected.Width) > sizeTolerance
            || Math.Abs(actual.Height - expected.Height) > sizeTolerance;
    }

    private IEnumerable<WindowInfo> FindWorkspaceBlockers(
        IReadOnlyList<WindowInfo> windows,
        WindowInfo target,
        WindowBounds expected,
        IReadOnlyList<ChannelMapping> mappings)
    {
        return windows
            .Where(window => window.Handle != target.Handle)
            .Where(window => window.ZOrder < target.ZOrder || target.ZOrder == int.MaxValue)
            .Where(window => !IsWorkspaceGuardianProtectedWindow(window, mappings))
            .Where(window => OverlapRatio(window.Bounds, expected) >= 0.12)
            .OrderBy(window => window.ZOrder)
            .Take(6);
    }

    private static bool IsWorkspaceGuardianProtectedWindow(WindowInfo window, IReadOnlyList<ChannelMapping> mappings)
    {
        if (IsIgnoredCoverageWindow(window))
        {
            return true;
        }

        return mappings.Any(mapping =>
            NormalizeProcessName(window.ProcessName).Equals(NormalizeProcessName(mapping.BrowserProcess), StringComparison.OrdinalIgnoreCase)
            && window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase));
    }

    private void WriteWorkspaceGuardianState(WorkspaceGuardianReport report)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var state = new Dictionary<string, object?>
            {
                ["status"] = report.Status,
                ["engine"] = "ariadgsm_workspace_guardian",
                ["phase"] = report.Phase,
                ["summary"] = report.Summary,
                ["reason"] = report.Reason,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["channels"] = report.Channels.Select(channel => new Dictionary<string, object?>
                {
                    ["channelId"] = channel.ChannelId,
                    ["browserProcess"] = channel.BrowserProcess,
                    ["status"] = channel.Status,
                    ["expectedBounds"] = SerializeBounds(channel.ExpectedBounds),
                    ["window"] = SerializeCabinWindow(channel.Window),
                    ["remainingBlockers"] = channel.RemainingBlockers
                }).ToArray(),
                ["blockers"] = report.Blockers.Select(blocker => new Dictionary<string, object?>
                {
                    ["channelId"] = blocker.ChannelId,
                    ["code"] = blocker.Code,
                    ["detail"] = blocker.Detail
                }).ToArray(),
                ["actions"] = report.Actions.Select(action => new Dictionary<string, object?>
                {
                    ["channelId"] = action.ChannelId,
                    ["type"] = action.Type,
                    ["detail"] = action.Detail
                }).ToArray()
            };

            WriteAllTextAtomicShared(WorkspaceGuardianStateFile, JsonSerializer.Serialize(state, options));

            var authority = new Dictionary<string, object?>
            {
                ["status"] = report.Status,
                ["engine"] = "ariadgsm_cabin_authority",
                ["phase"] = report.Phase,
                ["summary"] = report.Summary,
                ["reason"] = report.Reason,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["exclusiveWindowControl"] = true,
                ["handsMayFocus"] = report.Channels.Any(channel => channel.Status.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                ["handsMayRecoverWindows"] = false,
                ["handsMayArrangeWindows"] = false,
                ["policy"] = new[]
                {
                    "Solo Cabin Authority puede acomodar o restaurar ventanas de navegador.",
                    "El monitor en bucle solo observa; no minimiza ventanas del operador.",
                    "Hands puede clicar solo en canales ready, visibles y sin bloqueadores.",
                    "Si una ventana cubre WhatsApp, se reporta al operador en vez de cerrarla."
                },
                ["channels"] = report.Channels.Select(channel => new Dictionary<string, object?>
                {
                    ["channelId"] = channel.ChannelId,
                    ["browserProcess"] = channel.BrowserProcess,
                    ["status"] = channel.Status,
                    ["handsMayAct"] = channel.Status.Equals("ready", StringComparison.OrdinalIgnoreCase),
                    ["expectedBounds"] = SerializeBounds(channel.ExpectedBounds),
                    ["window"] = SerializeCabinWindow(channel.Window),
                    ["remainingBlockers"] = channel.RemainingBlockers
                }).ToArray(),
                ["blockers"] = report.Blockers.Select(blocker => new Dictionary<string, object?>
                {
                    ["channelId"] = blocker.ChannelId,
                    ["code"] = blocker.Code,
                    ["detail"] = blocker.Detail
                }).ToArray(),
                ["actions"] = report.Actions.Select(action => new Dictionary<string, object?>
                {
                    ["channelId"] = action.ChannelId,
                    ["type"] = action.Type,
                    ["detail"] = action.Detail
                }).ToArray()
            };

            WriteAllTextAtomicShared(CabinAuthorityStateFile, JsonSerializer.Serialize(authority, options));
            if (report.Blockers.Count > 0 || report.Actions.Count > 0 || !report.Status.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                WriteDiagnosticTimelineEvent(
                    "cabin_authority",
                    report.Status,
                    report.Summary,
                    string.Join(" | ", report.Blockers.Select(item => $"{item.ChannelId}:{item.Code}")),
                    report.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) ? "info" : "warning");
            }
        }
        catch
        {
        }
    }

    private static Dictionary<string, object?> SerializeBounds(WindowBounds bounds)
    {
        return new Dictionary<string, object?>
        {
            ["left"] = bounds.Left,
            ["top"] = bounds.Top,
            ["width"] = bounds.Width,
            ["height"] = bounds.Height
        };
    }

    private sealed record WorkspaceGuardianReport(
        string Status,
        string Phase,
        string Summary,
        string Reason,
        IReadOnlyList<WorkspaceGuardianChannel> Channels,
        IReadOnlyList<WorkspaceGuardianBlocker> Blockers,
        IReadOnlyList<WorkspaceGuardianAction> Actions)
    {
        public static WorkspaceGuardianReport Attention(string summary)
        {
            return new WorkspaceGuardianReport("attention", "error", summary, "exception", [], [], []);
        }

        public static WorkspaceGuardianReport Stopped()
        {
            return new WorkspaceGuardianReport("stopped", "stopped", "Guardian de cabina detenido.", "stop", [], [], []);
        }
    }

    private sealed record WorkspaceGuardianChannel(
        string ChannelId,
        string BrowserProcess,
        string Status,
        WindowBounds ExpectedBounds,
        WindowInfo? Window,
        int RemainingBlockers);

    private sealed record WorkspaceGuardianBlocker(string ChannelId, string Code, string Detail);

    private sealed record WorkspaceGuardianAction(string ChannelId, string Type, string Detail);
}
