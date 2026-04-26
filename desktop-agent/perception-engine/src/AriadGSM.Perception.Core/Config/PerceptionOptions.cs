using System.Text.Json;

namespace AriadGSM.Perception.Config;

public sealed class PerceptionOptions
{
    public string VisionEventsFile { get; init; } = @"desktop-agent\runtime\vision-events.jsonl";

    public string PerceptionEventsFile { get; init; } = @"desktop-agent\runtime\perception-events.jsonl";

    public string ConversationEventsFile { get; init; } = @"desktop-agent\runtime\conversation-events.jsonl";

    public string StateFile { get; init; } = @"desktop-agent\runtime\perception-health.json";

    public int HistoryLimitDays { get; init; } = 30;

    public int PollIntervalMs { get; init; } = 250;

    public int MaxCycles { get; init; } = 0;

    public double DurationSeconds { get; init; } = 0;

    public double MinimumWhatsAppConfidence { get; init; } = 0.75;

    public int MaxAccessibilityNodes { get; init; } = 900;

    public int MaxReaderLines { get; init; } = 250;

    public int MinimumUsefulTextLength { get; init; } = 3;

    public double MinimumMessageConfidence { get; init; } = 0.55;

    public IReadOnlyList<ChannelMapping> ChannelMappings { get; init; } =
    [
        new("wa-1", "msedge", "WhatsApp"),
        new("wa-2", "chrome", "WhatsApp"),
        new("wa-3", "firefox", "WhatsApp")
    ];

    public static PerceptionOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            return new PerceptionOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PerceptionOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new PerceptionOptions();
    }
}

public sealed record ChannelMapping(
    string ChannelId,
    string BrowserProcess,
    string TitleContains);
