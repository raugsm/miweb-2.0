using System.Text.Json;

namespace AriadGSM.Orchestrator.Config;

public sealed class OrchestratorOptions
{
    public string RuntimeDir { get; init; } = @"desktop-agent\runtime";

    public string StateFile { get; init; } = @"desktop-agent\runtime\orchestrator-state.json";

    public string CommandsFile { get; init; } = @"desktop-agent\runtime\orchestrator-commands.json";

    public int PollIntervalMs { get; init; } = 500;

    public int MaxCycles { get; init; } = 0;

    public int DurationSeconds { get; init; } = 0;

    public int StaleStateSeconds { get; init; } = 45;

    public int ActionTailLines { get; init; } = 200;

    public int HighSkippedActionsThreshold { get; init; } = 1000;

    public IReadOnlyList<string> ExpectedChannels { get; init; } = ["wa-1", "wa-2", "wa-3"];

    public IReadOnlyList<OrchestratorChannelMapping> ChannelMappings { get; init; } =
    [
        new("wa-1", "msedge", "WhatsApp"),
        new("wa-2", "chrome", "WhatsApp"),
        new("wa-3", "firefox", "WhatsApp")
    ];

    public static OrchestratorOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            return new OrchestratorOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<OrchestratorOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new OrchestratorOptions();
    }
}

public sealed record OrchestratorChannelMapping(
    string ChannelId,
    string BrowserProcess,
    string TitleContains);
