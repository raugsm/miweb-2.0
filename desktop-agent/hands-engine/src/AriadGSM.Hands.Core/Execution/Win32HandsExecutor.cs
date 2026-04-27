using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Execution;

public sealed class Win32HandsExecutor : IHandsExecutor
{
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const int WheelDelta = 120;
    private const int ShowWindowShow = 5;
    private readonly HandsOptions _options;
    private readonly CabinWindowRegistry _cabinRegistry;

    public Win32HandsExecutor(HandsOptions options)
    {
        _options = options;
        _cabinRegistry = new CabinWindowRegistry(options.CabinReadinessFile);
    }

    public ValueTask<ExecutionResult> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (plan.ActionType.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new ExecutionResult("verified", "No action needed.", 1));
        }

        if (plan.ActionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
            || plan.ActionType.Equals("send_message", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new ExecutionResult("failed", "Text and send execution are intentionally not implemented in Hands V1.", 0));
        }

        if (plan.ActionType.Equals("record_accounting", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new ExecutionResult("verified", "Accounting record action is routed to Operating/Accounting Core, not direct UI control.", 1));
        }

        var channelId = GetTargetString(plan, "channelId");
        var resolution = FindWhatsAppWindow(channelId);
        if (resolution.Window is null)
        {
            return ValueTask.FromResult(new ExecutionResult(
                "failed",
                resolution.FailureReason,
                0));
        }

        var window = resolution.Window;
        var clickX = 0;
        var clickY = 0;
        var hasClickCoordinates = plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            && TryGetTargetInt(plan, "clickX", out clickX)
            && TryGetTargetInt(plan, "clickY", out clickY);
        var focused = TryFocusWindow(window.Handle);
        if (!focused)
        {
            return ValueTask.FromResult(new ExecutionResult(
                "failed",
                $"Could not focus {window.Describe()} for channel '{channelId ?? "unknown"}'.",
                0.2));
        }

        if (hasClickCoordinates)
        {
            Thread.Sleep(80);
            SetCursorPos(clickX, clickY);
            Thread.Sleep(40);
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(35);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }

        if (plan.ActionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 0; index < 6; index++)
            {
                mouse_event(MouseEventWheel, 0, 0, WheelDelta, UIntPtr.Zero);
            }
        }

        var summary = plan.ActionType switch
        {
            "focus_window" => $"Focused {window.Describe()}.",
            "open_chat" when hasClickCoordinates => $"Focused {window.Describe()} and clicked chat row at {clickX},{clickY}.",
            "open_chat" => $"Focused {window.Describe()}, but no chat-row coordinates were available.",
            "capture_conversation" => $"Focused {window.Describe()} so Perception can refresh the conversation.",
            "scroll_history" => $"Focused {window.Describe()} and scrolled upward.",
            "record_accounting" => "Accounting record action is routed to Operating/Accounting Core, not direct UI typing.",
            _ => $"Executed {plan.ActionType} on {window.Describe()}."
        };

        return ValueTask.FromResult(new ExecutionResult("executed", summary, 0.72));
    }

    private static bool TryFocusWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(handle))
        {
            return false;
        }

        ShowWindow(handle, ShowWindowShow);
        BringWindowToTop(handle);
        if (SetForegroundWindow(handle))
        {
            return true;
        }

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(handle, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var attachedTarget = false;
        var attachedForeground = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            BringWindowToTop(handle);
            SetActiveWindow(handle);
            SetFocus(handle);
            return SetForegroundWindow(handle)
                || GetForegroundWindow() == handle
                || (IsWindowVisible(handle) && !IsIconic(handle));
        }
        finally
        {
            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }

            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
        }
    }

    private WindowResolution FindWhatsAppWindow(string? channelId)
    {
        var mapping = _options.ChannelMappings.FirstOrDefault(item => string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase));
        var windows = EnumerateWindows();
        var cabin = _cabinRegistry.Find(channelId);
        if (cabin is not null)
        {
            var candidates = windows
                .Where(window => MatchesCabin(window, cabin, mapping))
                .ToArray();
            var best = candidates
                .Where(window => window.Visible && !window.Minimized && IsWhatsAppTitle(window.Title))
                .OrderByDescending(window => WindowScore(window, cabin))
                .FirstOrDefault();
            if (best is not null)
            {
                return new WindowResolution(best, string.Empty);
            }

            var recoverable = candidates
                .Where(window => IsWhatsAppTitle(window.Title))
                .OrderByDescending(window => WindowScore(window, cabin))
                .FirstOrDefault();
            if (recoverable is not null)
            {
                return new WindowResolution(
                    null,
                    $"Cabin Authority must recover '{channelId ?? "unknown"}'; Hands will not restore or move browser windows directly.");
            }

            var processHint = cabin.ProcessId > 0
                ? $"{cabin.ProcessName} #{cabin.ProcessId}"
                : cabin.ProcessName;
            var candidateSummary = candidates.Length == 0
                ? "no hay handles candidatos para ese PID/navegador"
                : $"{candidates.Length} handle(s) candidatos, pero ninguno esta visible y utilizable";
            return new WindowResolution(
                null,
                $"Cabin registry has '{channelId ?? "unknown"}' mapped to {processHint}, but Hands cannot use it: {candidateSummary}.");
        }

        var fallback = windows
            .Where(window => window.Visible && !window.Minimized && IsWhatsAppTitle(window.Title))
            .Where(window => mapping is null || MatchesMapping(window, mapping))
            .OrderByDescending(window => window.Title.Contains("Business", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        return fallback is null
            ? new WindowResolution(null, $"No visible WhatsApp browser window was found for channel '{channelId ?? "unknown"}'.")
            : new WindowResolution(fallback, string.Empty);
    }

    private static string? GetTargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool TryGetTargetInt(ActionPlan plan, string key, out int result)
    {
        result = 0;
        if (!plan.Target.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        if (value is int integer)
        {
            result = integer;
            return true;
        }

        return int.TryParse(value.ToString(), out result);
    }

    private static IReadOnlyList<BrowserWindow> EnumerateWindows()
    {
        var windows = new List<BrowserWindow>();
        EnumWindows((handle, _) =>
        {
            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            var processName = string.Empty;
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            var bounds = GetBounds(handle);
            windows.Add(new BrowserWindow(
                handle,
                (int)processId,
                title,
                processName,
                IsWindowVisible(handle),
                IsIconic(handle),
                bounds));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool MatchesCabin(BrowserWindow window, CabinWindowIdentity cabin, HandsChannelMapping? mapping)
    {
        if (mapping is not null && !MatchesMapping(window, mapping))
        {
            return false;
        }

        if (cabin.ProcessId > 0 && window.ProcessId == cabin.ProcessId)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(cabin.ProcessName)
            && window.ProcessName.Equals(cabin.ProcessName, StringComparison.OrdinalIgnoreCase)
            && IsWhatsAppTitle(window.Title);
    }

    private static bool MatchesMapping(BrowserWindow window, HandsChannelMapping mapping)
    {
        return window.ProcessName.Equals(mapping.BrowserProcess, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(mapping.TitleContains)
                || window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWhatsAppTitle(string title)
    {
        return title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
    }

    private static int WindowScore(BrowserWindow window, CabinWindowIdentity cabin)
    {
        var score = 0;
        if (window.ProcessId == cabin.ProcessId)
        {
            score += 100;
        }

        if (window.Title.Contains("Business", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        if (cabin.Bounds is not null)
        {
            score -= Math.Min(50, BoundsDistance(window.Bounds, cabin.Bounds));
        }

        return score;
    }

    private static int BoundsDistance(WindowBounds current, CabinWindowBounds expected)
    {
        return Math.Abs(current.Left - expected.Left)
            + Math.Abs(current.Top - expected.Top)
            + Math.Abs(current.Width - expected.Width)
            + Math.Abs(current.Height - expected.Height);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static WindowBounds GetBounds(IntPtr handle)
    {
        return GetWindowRect(handle, out var rect)
            ? new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : new WindowBounds(0, 0, 0, 0);
    }

    private sealed record BrowserWindow(
        IntPtr Handle,
        int ProcessId,
        string Title,
        string ProcessName,
        bool Visible,
        bool Minimized,
        WindowBounds Bounds)
    {
        public string Describe()
        {
            return $"WhatsApp window '{Title}' ({ProcessName} #{ProcessId}, handle {Handle.ToInt64()})";
        }
    }

    private sealed record WindowResolution(BrowserWindow? Window, string FailureReason);

    private sealed record WindowBounds(int Left, int Top, int Width, int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint currentThreadId, uint targetThreadId, bool attach);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);
}
