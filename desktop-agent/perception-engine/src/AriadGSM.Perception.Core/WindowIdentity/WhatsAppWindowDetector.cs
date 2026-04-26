using AriadGSM.Perception.VisionInput;

namespace AriadGSM.Perception.WindowIdentity;

public sealed class WhatsAppWindowDetector
{
    private static readonly HashSet<string> SupportedBrowsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox"
    };

    public IReadOnlyList<WhatsAppWindowCandidate> Detect(IReadOnlyList<VisionWindow> windows, double minimumConfidence)
    {
        var candidates = new List<WhatsAppWindowCandidate>();
        foreach (var window in windows)
        {
            var candidate = Score(window);
            if (candidate.Confidence >= minimumConfidence)
            {
                candidates.Add(candidate);
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Window.Bounds.Left)
            .ThenBy(candidate => candidate.Window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WhatsAppWindowCandidate Score(VisionWindow window)
    {
        var processOk = SupportedBrowsers.Contains(window.ProcessName);
        var titleHasWhatsApp = window.Title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
        var titleHasBusiness = window.Title.Contains("WhatsApp Business", StringComparison.OrdinalIgnoreCase);
        if (!processOk || !titleHasWhatsApp)
        {
            return new WhatsAppWindowCandidate(window, 0, "not_supported_whatsapp_browser");
        }

        var confidence = titleHasBusiness ? 0.95 : 0.85;
        return new WhatsAppWindowCandidate(window, confidence, "supported_browser_title_match");
    }
}
