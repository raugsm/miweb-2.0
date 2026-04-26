using System.Text.Json;

namespace AriadGSM.Interaction.Config;

public sealed class InteractionOptions
{
    public string PerceptionEventsFile { get; init; } = @"desktop-agent\runtime\perception-events.jsonl";

    public string InteractionEventsFile { get; init; } = @"desktop-agent\runtime\interaction-events.jsonl";

    public string StateFile { get; init; } = @"desktop-agent\runtime\interaction-state.json";

    public int PollIntervalMs { get; init; } = 200;

    public int MaxCycles { get; init; } = 0;

    public int DurationSeconds { get; init; } = 0;

    public int PerceptionLimit { get; init; } = 120;

    public double MinimumActionableConfidence { get; init; } = 0.66;

    public bool RejectBusinessAdminGroups { get; init; } = true;

    public IReadOnlyList<string> LowValueChatTitlePatterns { get; init; } =
    [
        "pagos mexico",
        "pagos chile",
        "pago colombia",
        "pagos colombia"
    ];

    public IReadOnlyList<string> BrowserUiTitlePatterns { get; init; } =
    [
        "anadir esta pagina a marcadores",
        "editar marcador",
        "marcadores",
        "leer en voz alta",
        "informacion del sitio",
        "copiar ruta",
        "nueva pestana",
        "google chrome",
        "microsoft edge",
        "mozilla firefox"
    ];

    public static InteractionOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            return new InteractionOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<InteractionOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new InteractionOptions();
    }
}
