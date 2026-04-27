using System.Threading;
using System.Windows.Automation;

namespace AriadGSM.Agent.Desktop;

internal sealed partial class AgentRuntime
{
    private bool TryLocateExistingWhatsAppSession(ChannelMapping mapping, IReadOnlyList<WindowInfo> windows)
    {
        var activeWhatsApp = FindMatchingWindows(windows, mapping)
            .OrderBy(window => window.ZOrder)
            .FirstOrDefault();
        if (activeWhatsApp is not null)
        {
            BringBrowserWindowForward(activeWhatsApp);
            WriteLog($"{mapping.ChannelId}: Session Locator encontro WhatsApp activo en {activeWhatsApp.ProcessName} #{activeWhatsApp.ProcessId}.");
            return true;
        }

        foreach (var browserWindow in FindBrowserSessionWindows(windows, mapping))
        {
            if (TrySelectWhatsAppTab(browserWindow))
            {
                WriteLog($"{mapping.ChannelId}: Session Locator activo una pestana existente de WhatsApp en {browserWindow.ProcessName} #{browserWindow.ProcessId}.");
                return true;
            }
        }

        return false;
    }

    private bool TrySelectWhatsAppTab(WindowInfo browserWindow)
    {
        try
        {
            BringBrowserWindowForward(browserWindow);
            Thread.Sleep(160);

            var root = AutomationElement.FromHandle(browserWindow.Handle);
            if (root is null)
            {
                return false;
            }

            var nodes = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
            var count = Math.Min(nodes.Count, 700);
            for (var index = 0; index < count; index++)
            {
                var element = nodes[index];
                if (!IsWhatsAppTabCandidate(element))
                {
                    continue;
                }

                if (TrySelectAutomationElement(element) || TryInvokeAutomationElement(element))
                {
                    Thread.Sleep(300);
                    BringBrowserWindowForward(browserWindow);
                    return true;
                }
            }
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception exception)
        {
            WriteLog($"Session Locator: no pude activar pestana en {browserWindow.ProcessName} #{browserWindow.ProcessId}: {exception.Message}");
        }

        return false;
    }

    private static IEnumerable<WindowInfo> FindBrowserSessionWindows(IReadOnlyList<WindowInfo> windows, ChannelMapping mapping)
    {
        var process = NormalizeProcessName(mapping.BrowserProcess);
        return windows
            .Where(window => NormalizeProcessName(window.ProcessName).Equals(process, StringComparison.OrdinalIgnoreCase))
            .Where(window => window.Bounds.Width > 200 && window.Bounds.Height > 200)
            .OrderBy(window => window.ZOrder);
    }

    private static bool IsWhatsAppTabCandidate(AutomationElement element)
    {
        try
        {
            var name = NormalizeForSearch(element.Current.Name ?? string.Empty);
            if (!name.Contains("whatsapp", StringComparison.Ordinal)
                && !name.Contains("web.whatsapp.com", StringComparison.Ordinal))
            {
                return false;
            }

            var controlType = element.Current.ControlType;
            return controlType == ControlType.TabItem
                || controlType == ControlType.Button
                || controlType == ControlType.ListItem
                || controlType == ControlType.Custom;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySelectAutomationElement(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
                && pattern is SelectionItemPattern selectionItem)
            {
                selectionItem.Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryInvokeAutomationElement(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)
                && pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static void BringBrowserWindowForward(WindowInfo window)
    {
        try
        {
            ShowWindow(window.Handle, ShowWindowRestore);
            _ = SetWindowPos(window.Handle, HwndTop, window.Bounds.Left, window.Bounds.Top, window.Bounds.Width, window.Bounds.Height, SetWindowPosShowWindow);
            _ = BringWindowToTop(window.Handle);
            _ = SetForegroundWindow(window.Handle);
        }
        catch
        {
        }
    }
}
