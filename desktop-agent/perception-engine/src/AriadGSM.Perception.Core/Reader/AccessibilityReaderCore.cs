using System.Windows.Automation;
using AriadGSM.Perception.ChannelResolution;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.VisionInput;
using WpfRect = System.Windows.Rect;

namespace AriadGSM.Perception.Reader;

public sealed class AccessibilityReaderCore : IReaderCore
{
    private readonly PerceptionOptions _options;
    private readonly NativeWindowHandleResolver _handleResolver = new();

    public AccessibilityReaderCore(PerceptionOptions options)
    {
        _options = options;
    }

    public ValueTask<ReaderCoreResult> ReadAsync(ResolvedChannel channel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handle = _handleResolver.Resolve(channel.Candidate.Window);
            if (handle == IntPtr.Zero)
            {
                return ValueTask.FromResult(ReaderCoreResult.Empty(channel.ChannelId, "accessibility", "no_native_handle", "Could not resolve live window handle."));
            }

            var root = AutomationElement.FromHandle(handle);
            if (root is null)
            {
                return ValueTask.FromResult(ReaderCoreResult.Empty(channel.ChannelId, "accessibility", "no_accessibility_root", "Automation root was not available."));
            }

            var lines = ReadLines(root, channel);
            var status = lines.Count > 0 ? "ok" : "empty";
            var confidence = lines.Count > 0 ? Math.Min(0.9, 0.55 + (lines.Count * 0.01)) : 0;
            return ValueTask.FromResult(new ReaderCoreResult(channel.ChannelId, "accessibility", status, lines, confidence, string.Empty));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(ReaderCoreResult.Empty(channel.ChannelId, "accessibility", "error", exception.Message));
        }
    }

    private IReadOnlyList<ReaderTextLine> ReadLines(AutomationElement root, ResolvedChannel channel)
    {
        var result = new List<ReaderTextLine>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
        var maxNodes = Math.Min(nodes.Count, Math.Max(50, _options.MaxAccessibilityNodes));
        for (var index = 0; index < maxNodes && result.Count < _options.MaxReaderLines; index++)
        {
            var element = nodes[index];
            var text = ReadElementText(element);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            text = NormalizeText(text);
            if (text.Length < _options.MinimumUsefulTextLength)
            {
                continue;
            }

            var key = $"{text}|{ReadAutomationId(element)}";
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(new ReaderTextLine(
                text,
                ReadControlType(element),
                ReadBounds(element, channel.Candidate.Window.Bounds),
                0.72,
                "windows_accessibility"));
        }

        return result;
    }

    private static string ReadElementText(AutomationElement element)
    {
        var name = SafeRead(() => element.Current.Name);
        var value = string.Empty;
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern valuePattern)
        {
            value = SafeRead(() => valuePattern.Current.Value);
        }

        return string.IsNullOrWhiteSpace(value) || name.Contains(value, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} {value}";
    }

    private static string ReadControlType(AutomationElement element)
    {
        return SafeRead(() => element.Current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.Ordinal));
    }

    private static string ReadAutomationId(AutomationElement element)
    {
        return SafeRead(() => element.Current.AutomationId);
    }

    private static VisionBounds? ReadBounds(AutomationElement element, VisionBounds fallback)
    {
        var rect = SafeRead(() => element.Current.BoundingRectangle);
        if (rect == WpfRect.Empty || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var left = (int)Math.Round(rect.Left);
        var top = (int)Math.Round(rect.Top);
        var width = (int)Math.Round(rect.Width);
        var height = (int)Math.Round(rect.Height);
        if (!Intersects(left, top, width, height, fallback))
        {
            return null;
        }

        return new VisionBounds(left, top, width, height);
    }

    private static bool Intersects(int left, int top, int width, int height, VisionBounds bounds)
    {
        var right = left + width;
        var bottom = top + height;
        var targetRight = bounds.Left + bounds.Width;
        var targetBottom = bounds.Top + bounds.Height;
        return left < targetRight && right > bounds.Left && top < targetBottom && bottom > bounds.Top;
    }

    private static string NormalizeText(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static T SafeRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default!;
        }
    }
}
