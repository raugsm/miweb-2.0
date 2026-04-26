using System.Runtime.InteropServices;
using System.Text;
using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.Reader;

public sealed class NativeWindowHandleResolver
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public IntPtr Resolve(VisionWindow target)
    {
        var best = IntPtr.Zero;
        var bestScore = 0;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if ((int)processId != target.ProcessId)
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

            var score = Score(target, title, rect);
            if (score > bestScore)
            {
                bestScore = score;
                best = handle;
            }

            return true;
        }, IntPtr.Zero);

        return bestScore >= 3 ? best : IntPtr.Zero;
    }

    private static int Score(VisionWindow target, string title, Rect rect)
    {
        var score = 0;
        if (title.Equals(target.Title, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }
        else if (title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (Math.Abs(rect.Left - target.Bounds.Left) <= 16)
        {
            score++;
        }
        if (Math.Abs(rect.Top - target.Bounds.Top) <= 16)
        {
            score++;
        }
        if (Math.Abs(width - target.Bounds.Width) <= 24)
        {
            score++;
        }
        if (Math.Abs(height - target.Bounds.Height) <= 24)
        {
            score++;
        }

        return score;
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

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

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
}
