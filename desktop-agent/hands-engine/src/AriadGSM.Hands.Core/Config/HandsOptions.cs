using System.Text.Json;

namespace AriadGSM.Hands.Config;

public sealed class HandsOptions
{
    public string CognitiveDecisionEventsFile { get; init; } = @"desktop-agent\runtime\cognitive-decision-events.jsonl";

    public string OperatingDecisionEventsFile { get; init; } = @"desktop-agent\runtime\decision-events.jsonl";

    public string PerceptionEventsFile { get; init; } = @"desktop-agent\runtime\perception-events.jsonl";

    public string InteractionEventsFile { get; init; } = @"desktop-agent\runtime\interaction-events.jsonl";

    public string ActionEventsFile { get; init; } = @"desktop-agent\runtime\action-events.jsonl";

    public string StateFile { get; init; } = @"desktop-agent\runtime\hands-state.json";

    public int AutonomyLevel { get; init; } = 3;

    public bool ExecuteActions { get; init; } = false;

    public bool AllowTextInput { get; init; } = false;

    public bool AllowSendMessage { get; init; } = false;

    public int PollIntervalMs { get; init; } = 250;

    public int MaxCycles { get; init; } = 0;

    public int DecisionLimit { get; init; } = 200;

    public int MaxDecisionAgeMinutes { get; init; } = 20;

    public int PerceptionLimit { get; init; } = 50;

    public int InteractionLimit { get; init; } = 20;

    public IReadOnlyList<HandsChannelMapping> ChannelMappings { get; init; } =
    [
        new("wa-1", "msedge", "WhatsApp"),
        new("wa-2", "chrome", "WhatsApp"),
        new("wa-3", "firefox", "WhatsApp")
    ];

    public static HandsOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            return new HandsOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HandsOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new HandsOptions();
    }
}

public sealed record HandsChannelMapping(
    string ChannelId,
    string BrowserProcess,
    string TitleContains);
