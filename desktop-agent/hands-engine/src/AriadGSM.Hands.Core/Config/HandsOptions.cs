using System.Text.Json;

namespace AriadGSM.Hands.Config;

public sealed class HandsOptions
{
    public string CognitiveDecisionEventsFile { get; init; } = @"desktop-agent\runtime\cognitive-decision-events.jsonl";

    public string OperatingDecisionEventsFile { get; init; } = @"desktop-agent\runtime\decision-events.jsonl";

    public string PerceptionEventsFile { get; init; } = @"desktop-agent\runtime\perception-events.jsonl";

    public string InteractionEventsFile { get; init; } = @"desktop-agent\runtime\interaction-events.jsonl";

    public string ActionEventsFile { get; init; } = @"desktop-agent\runtime\action-events.jsonl";

    public string ActionQueueStateFile { get; init; } = @"desktop-agent\runtime\action-queue-state.json";

    public string StateFile { get; init; } = @"desktop-agent\runtime\hands-state.json";

    public string OrchestratorCommandsFile { get; init; } = @"desktop-agent\runtime\orchestrator-commands.json";

    public string CabinReadinessFile { get; init; } = @"desktop-agent\runtime\cabin-readiness.json";

    public string CabinAuthorityStateFile { get; init; } = @"desktop-agent\runtime\cabin-authority-state.json";

    public string CursorFile { get; init; } = @"desktop-agent\runtime\hands-cursor.json";

    public string InputArbiterStateFile { get; init; } = @"desktop-agent\runtime\input-arbiter-state.json";

    public int AutonomyLevel { get; init; } = 3;

    public bool ExecuteActions { get; init; } = false;

    public bool InputArbiterEnabled { get; init; } = true;

    public bool RequireCabinAuthorityForWindowActions { get; init; } = true;

    public int CabinAuthorityMaxAgeMs { get; init; } = 2500;

    public bool EnableInteractionNavigator { get; init; } = true;

    public bool RespectOrchestratorCommands { get; init; } = true;

    public bool AllowTextInput { get; init; } = false;

    public bool AllowSendMessage { get; init; } = false;

    public int PollIntervalMs { get; init; } = 250;

    public int MaxCycles { get; init; } = 0;

    public int DecisionLimit { get; init; } = 200;

    public int MaxDecisionAgeMinutes { get; init; } = 20;

    public int PerceptionLimit { get; init; } = 50;

    public int InteractionLimit { get; init; } = 20;

    public int NavigatorMaxChatsPerCycle { get; init; } = 1;

    public int NavigatorRevisitMinutes { get; init; } = 30;

    public int NavigatorMinimumSecondsBetweenClicks { get; init; } = 2;

    public int OperatorIdleRequiredMs { get; init; } = 1200;

    public int OperatorCooldownMs { get; init; } = 1600;

    public int AiControlLeaseMs { get; init; } = 900;

    public int ProcessedDecisionCursorLimit { get; init; } = 2000;

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
