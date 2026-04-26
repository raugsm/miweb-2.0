using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AriadGSM.Vision.Windows;

public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public IReadOnlyList<WindowSnapshot> GetVisibleWindows()
    {
        var windows = new List<WindowSnapshot>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }
            if (IsIconic(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 120 || height <= 80)
            {
                return true;
            }
            if (!IntersectsVirtualScreen(rect))
            {
                return true;
            }
            if (title.Equals("Program Manager", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            var processName = GetProcessName(processId);
            windows.Add(new WindowSnapshot(
                handle,
                (int)processId,
                processName,
                title,
                new WindowBounds(rect.Left, rect.Top, width, height),
                true,
                DateTimeOffset.UtcNow));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool IntersectsVirtualScreen(Rect rect)
    {
        var left = GetSystemMetrics(SystemMetric.VirtualScreenLeft);
        var top = GetSystemMetrics(SystemMetric.VirtualScreenTop);
        var right = left + GetSystemMetrics(SystemMetric.VirtualScreenWidth);
        var bottom = top + GetSystemMetrics(SystemMetric.VirtualScreenHeight);
        return rect.Left < right && rect.Right > left && rect.Top < bottom && rect.Bottom > top;
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
        return builder.ToString().Trim();
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(SystemMetric nIndex);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum SystemMetric
    {
        VirtualScreenLeft = 76,
        VirtualScreenTop = 77,
        VirtualScreenWidth = 78,
        VirtualScreenHeight = 79
    }
}
