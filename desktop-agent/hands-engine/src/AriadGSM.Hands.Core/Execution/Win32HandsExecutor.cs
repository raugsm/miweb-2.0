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
    private const int ShowWindowRestore = 9;
    private const int ShowWindowShow = 5;
    private readonly HandsOptions _options;

    public Win32HandsExecutor(HandsOptions options)
    {
        _options = options;
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

        var channelId = GetTargetString(plan, "channelId");
        var window = FindWhatsAppWindow(channelId);
        if (window is null)
        {
            return ValueTask.FromResult(new ExecutionResult(
                "failed",
                $"No visible WhatsApp browser window was found for channel '{channelId ?? "unknown"}'.",
                0));
        }

        var clickX = 0;
        var clickY = 0;
        var hasClickCoordinates = plan.ActionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            && TryGetTargetInt(plan, "clickX", out clickX)
            && TryGetTargetInt(plan, "clickY", out clickY);
        var focused = TryFocusWindow(window.Handle);
        if (!focused && !hasClickCoordinates)
        {
            return ValueTask.FromResult(new ExecutionResult("failed", $"Could not focus window '{window.Title}'.", 0.2));
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
            "focus_window" => $"Focused WhatsApp window '{window.Title}'.",
            "open_chat" when hasClickCoordinates => focused
                ? $"Focused '{window.Title}' and clicked chat row at {clickX},{clickY}."
                : $"Clicked verified chat row at {clickX},{clickY}; Windows did not grant foreground focus first.",
            "open_chat" => $"Focused WhatsApp window '{window.Title}', but no chat-row coordinates were available.",
            "capture_conversation" => $"Focused WhatsApp window '{window.Title}' so Perception can refresh the conversation.",
            "scroll_history" => $"Focused WhatsApp window '{window.Title}' and scrolled upward.",
            "record_accounting" => "Accounting record action is routed to Operating/Accounting Core, not direct UI typing.",
            _ => $"Executed {plan.ActionType} on '{window.Title}'."
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
            ShowWindow(handle, ShowWindowRestore);
        }
        else
        {
            ShowWindow(handle, ShowWindowShow);
        }

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
            return SetForegroundWindow(handle) || GetForegroundWindow() == handle;
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

    private BrowserWindow? FindWhatsAppWindow(string? channelId)
    {
        var mapping = _options.ChannelMappings.FirstOrDefault(item => string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase));
        return EnumerateWindows()
            .Where(window => window.Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
            .Where(window => mapping is null
                || (window.ProcessName.Equals(mapping.BrowserProcess, StringComparison.OrdinalIgnoreCase)
                    && window.Title.Contains(mapping.TitleContains, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(window => window.Title.Contains("Business", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
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
            if (!IsWindowVisible(handle))
            {
                return true;
            }

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

            windows.Add(new BrowserWindow(handle, title, processName));
            return true;
        }, IntPtr.Zero);
        return windows;
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

    private sealed record BrowserWindow(IntPtr Handle, string Title, string ProcessName);

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
    private static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extraInfo);
}
