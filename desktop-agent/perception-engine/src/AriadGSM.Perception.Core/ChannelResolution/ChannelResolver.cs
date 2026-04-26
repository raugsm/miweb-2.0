using AriadGSM.Perception.Config;
using AriadGSM.Perception.WindowIdentity;

namespace AriadGSM.Perception.ChannelResolution;

public sealed class ChannelResolver
{
    private readonly IReadOnlyList<ChannelMapping> _mappings;

    public ChannelResolver(IReadOnlyList<ChannelMapping> mappings)
    {
        _mappings = mappings;
    }

    public IReadOnlyList<ResolvedChannel> Resolve(IReadOnlyList<WhatsAppWindowCandidate> candidates)
    {
        var resolved = new List<ResolvedChannel>();
        var fallbackIndex = 1;
        foreach (var candidate in candidates)
        {
            var mapping = _mappings.FirstOrDefault(item =>
                candidate.Window.ProcessName.Equals(item.BrowserProcess, StringComparison.OrdinalIgnoreCase)
                && candidate.Window.Title.Contains(item.TitleContains, StringComparison.OrdinalIgnoreCase));
            if (mapping is not null)
            {
                resolved.Add(new ResolvedChannel(mapping.ChannelId, candidate, candidate.Confidence, "configured_browser_title"));
                continue;
            }

            resolved.Add(new ResolvedChannel($"wa-auto-{fallbackIndex}", candidate, candidate.Confidence * 0.75, "fallback_visible_order"));
            fallbackIndex++;
        }

        return resolved;
    }
}
