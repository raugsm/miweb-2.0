using System.Text.Json;
using AriadGSM.Orchestrator.Config;
using AriadGSM.Orchestrator.Runtime;

namespace AriadGSM.Orchestrator.Pipeline;

public sealed class OrchestratorPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly OrchestratorOptions _options;
    private string _lastPhase = "starting";
    private string _lastSummary = string.Empty;
    private string _lastError = string.Empty;

    public OrchestratorPipeline(OrchestratorOptions options)
    {
        _options = options;
    }

    public async ValueTask<OrchestratorState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JsonDocument> actionDocuments = [];
        try
        {
            using var cabinDocument = RuntimeJson.ReadDocument(RuntimeFile("cabin-readiness.json"));
            using var visionDocument = RuntimeJson.ReadDocument(RuntimeFile("vision-health.json"));
            using var perceptionDocument = RuntimeJson.ReadDocument(RuntimeFile("perception-health.json"));
            using var interactionDocument = RuntimeJson.ReadDocument(RuntimeFile("interaction-state.json"));
            using var handsDocument = RuntimeJson.ReadDocument(RuntimeFile("hands-state.json"));
            using var windowRealityDocument = RuntimeJson.ReadDocument(RuntimeFile("window-reality-state.json"));
            actionDocuments = RuntimeJson.ReadJsonlTail(RuntimeFile("action-events.jsonl"), _options.ActionTailLines);

            var cabin = ReadCabin(cabinDocument);
            var vision = ReadVision(visionDocument);
            var perception = ReadPerception(perceptionDocument);
            var interaction = interactionDocument is null ? null : ReadInteraction(interactionDocument);
            var hands = handsDocument is null ? null : ReadHands(handsDocument);
            var windowReality = ReadWindowReality(windowRealityDocument);
            var actionFailures = ReadActionFailures(actionDocuments);
            var staleStates = StaleStates(visionDocument, perceptionDocument, interactionDocument, handsDocument, windowRealityDocument);
            var codexWindows = vision.Windows
                .Where(item =>
                    item.ProcessName.Equals("Codex", StringComparison.OrdinalIgnoreCase)
                    || item.Title.Contains("Codex", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var channels = new List<OrchestratorChannelState>();
            var blockers = new List<OrchestratorBlocker>();
            var recommendations = new List<OrchestratorRecommendation>();

            foreach (var mapping in _options.ChannelMappings)
            {
                var cabinChannel = cabin.Channels.TryGetValue(mapping.ChannelId, out var foundCabin)
                    ? foundCabin
                    : CabinChannelSnapshot.Missing(mapping.ChannelId, mapping.BrowserProcess);
                var visionWindow = FindWhatsAppWindow(mapping, vision.Windows);
                var cabinWindow = cabinChannel.Window;
                var window = visionWindow ?? cabinWindow;
                var browserWindows = vision.Windows
                    .Where(item => item.ProcessName.Equals(mapping.BrowserProcess, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var perceptionSeen = perception.ChannelIds.Contains(mapping.ChannelId);
                var realityChannel = windowReality.Channels.TryGetValue(mapping.ChannelId, out var foundReality)
                    ? foundReality
                    : WindowRealityChannelSnapshot.Missing(mapping.ChannelId);
                var failedActions = actionFailures.TryGetValue(mapping.ChannelId, out var failures) ? failures : 0;
                var codexOverlap = visionWindow is not null && codexWindows.Any(item => OverlapRatio(item.Bounds, visionWindow.Bounds) > 0.25);

                var actionsAllowed = cabinChannel.IsReady
                    && realityChannel.HandsMayAct
                    && !cabinChannel.RequiresHuman
                    && visionWindow is not null
                    && perceptionSeen;
                var status = actionsAllowed ? "OK" : "ATTENTION";
                var details = new List<string>();

                if (!cabinChannel.IsReady)
                {
                    details.Add("Cabin readiness no confirma este canal.");
                    blockers.Add(new OrchestratorBlocker(
                        "channel_not_ready",
                        cabinChannel.RequiresHuman ? "error" : "warning",
                        mapping.ChannelId,
                        cabinChannel.Detail));
                    recommendations.Add(new OrchestratorRecommendation(
                        "prepare_whatsapp",
                        mapping.ChannelId,
                        "Ejecutar Preparar WhatsApps o dejar ese navegador en web.whatsapp.com."));
                }

                if (cabinChannel.RequiresHuman)
                {
                    status = "BLOCKED";
                    details.Add("Requiere accion humana.");
                }

                if (!windowReality.Available)
                {
                    details.Add("Window Reality aun no publico consenso; no permito manos.");
                    blockers.Add(new OrchestratorBlocker(
                        "window_reality_missing",
                        "warning",
                        mapping.ChannelId,
                        "Reality Resolver debe fusionar cabina, lectura fresca e input antes de actuar."));
                }
                else if (!realityChannel.HandsMayAct)
                {
                    details.Add($"Reality Resolver no autoriza manos: {realityChannel.Status}.");
                    blockers.Add(new OrchestratorBlocker(
                        "window_reality_not_actionable",
                        realityChannel.RequiresHuman ? "error" : "warning",
                        mapping.ChannelId,
                        realityChannel.Reason));
                    recommendations.Add(new OrchestratorRecommendation(
                        "wait_for_actionable_reality",
                        mapping.ChannelId,
                        "Esperar lectura fresca y permiso de input antes de tocar la ventana."));
                }

                if (visionWindow is null)
                {
                    if (cabinWindow is not null)
                    {
                        details.Add("Cabina recuerda la ventana de WhatsApp, pero Vision no la ve ahora mismo.");
                        blockers.Add(new OrchestratorBlocker(
                            "channel_hidden_or_covered",
                            "warning",
                            mapping.ChannelId,
                            $"{mapping.BrowserProcess} estaba identificado como {cabinWindow.Title}, pero no esta visible para lectura; puede estar cubierto, minimizado o con la pestana cambiada."));
                        recommendations.Add(new OrchestratorRecommendation(
                            "restore_cabin_window",
                            mapping.ChannelId,
                            "Restaurar la ventana registrada por cabina antes de permitir manos."));
                    }
                    else
                    {
                        details.Add("Vision no ve una ventana usable de WhatsApp para este navegador.");
                        blockers.Add(new OrchestratorBlocker(
                            "channel_missing_from_vision",
                            "warning",
                            mapping.ChannelId,
                            $"{mapping.BrowserProcess} no aparece como WhatsApp visible ahora mismo."));
                        recommendations.Add(new OrchestratorRecommendation(
                            "restore_browser_whatsapp",
                            mapping.ChannelId,
                            $"Abrir o restaurar web.whatsapp.com en {mapping.BrowserProcess}."));
                    }

                    var wrongWindow = browserWindows.FirstOrDefault(item => !LooksLikeWhatsApp(item.Title));
                    if (wrongWindow is not null)
                    {
                        blockers.Add(new OrchestratorBlocker(
                            "browser_not_on_whatsapp",
                            "warning",
                            mapping.ChannelId,
                            $"{mapping.BrowserProcess} muestra '{wrongWindow.Title}', no WhatsApp."));
                    }
                }

                if (!perceptionSeen)
                {
                    details.Add("Perception no tiene lectura actual de este canal.");
                    blockers.Add(new OrchestratorBlocker(
                        "channel_missing_from_perception",
                        "warning",
                        mapping.ChannelId,
                        $"Perception reporto canales: {JoinOrNone(perception.ChannelIds)}."));
                    recommendations.Add(new OrchestratorRecommendation(
                        "wait_for_fresh_perception",
                        mapping.ChannelId,
                        "Pauso acciones de este canal hasta que Perception lo vea de nuevo."));
                }

                if (failedActions > 0)
                {
                    details.Add($"{failedActions} acciones recientes fallaron en este canal.");
                    blockers.Add(new OrchestratorBlocker(
                        "recent_action_failure",
                        "warning",
                        mapping.ChannelId,
                        $"Hands fallo {failedActions} veces intentando actuar en este canal."));
                }

                if (codexOverlap)
                {
                    details.Add("Codex u otra ventana de trabajo esta encima de la zona de lectura.");
                    blockers.Add(new OrchestratorBlocker(
                        "operator_window_overlap",
                        "warning",
                        mapping.ChannelId,
                        "Una ventana de trabajo cubre parte importante de WhatsApp; puede ensuciar la lectura."));
                    recommendations.Add(new OrchestratorRecommendation(
                        "clear_whatsapp_zone",
                        mapping.ChannelId,
                        "Dejar esta zona libre antes de modo autonomo."));
                }

                if (details.Count == 0)
                {
                    details.Add("Canal listo: cabina, vision y perception coinciden.");
                }

                channels.Add(new OrchestratorChannelState(
                    mapping.ChannelId,
                    mapping.BrowserProcess,
                    status,
                    cabinChannel.IsReady,
                    visionWindow is not null,
                    perceptionSeen,
                    actionsAllowed,
                    string.Join(" ", details),
                    window));
            }

            foreach (var stale in staleStates)
            {
                blockers.Add(new OrchestratorBlocker(
                    "stale_engine_state",
                    "warning",
                    "",
                    stale));
            }

            if (hands is not null && hands.ActionsSkipped >= _options.HighSkippedActionsThreshold)
            {
                blockers.Add(new OrchestratorBlocker(
                    "hands_queue_replay",
                    "warning",
                    "",
                    $"Hands acumulo {hands.ActionsSkipped} acciones saltadas; conviene compactar/rotar eventos viejos cuando Orchestrator lo permita."));
                recommendations.Add(new OrchestratorRecommendation(
                    "compact_runtime_events",
                    "",
                    "Mover eventos antiguos a archivo de auditoria para que las manos no repasen cola vieja."));
            }

            var pausedChannels = channels
                .Where(item => !item.ActionsAllowed)
                .Select(item => item.ChannelId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var allowedCount = channels.Count(item => item.ActionsAllowed);
            var phase = DeterminePhase(channels, blockers, hands, interaction);
            var statusText = phase switch
            {
                "blocked" => "blocked",
                "degraded" => "attention",
                "acting" => "ok",
                "observing" => "ok",
                _ => "ok"
            };
            var summary = $"{allowedCount}/{channels.Count} canales listos; pausados: {JoinOrNone(pausedChannels)}.";

            var metrics = new OrchestratorMetrics(
                channels.Count,
                channels.Count(item => item.CabinReady),
                channels.Count(item => item.VisionVisible),
                perception.ChannelIds.Count,
                actionFailures.Values.Sum(),
                hands?.ActionsExecuted ?? 0,
                hands?.ActionsVerified ?? 0,
                hands?.ActionsSkipped ?? 0,
                interaction?.ActionableTargets ?? 0);

            var state = new OrchestratorState(
                "ariadgsm_runtime_orchestrator",
                statusText,
                DateTimeOffset.UtcNow,
                phase,
                summary,
                channels,
                blockers,
                recommendations,
                metrics,
                string.Empty);

            var commands = new OrchestratorCommands(
                DateTimeOffset.UtcNow,
                allowedCount > 0,
                pausedChannels,
                summary);

            await WriteStateAsync(state, commands, cancellationToken).ConfigureAwait(false);
            _lastPhase = phase;
            _lastSummary = summary;
            _lastError = string.Empty;
            return state;
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            var state = new OrchestratorState(
                "ariadgsm_runtime_orchestrator",
                "error",
                DateTimeOffset.UtcNow,
                "blocked",
                "Orchestrator fallo al leer el runtime.",
                [],
                [new OrchestratorBlocker("orchestrator_error", "error", "", exception.Message)],
                [],
                new OrchestratorMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0),
                exception.Message);
            await WriteStateOnlyAsync(state, CancellationToken.None).ConfigureAwait(false);
            return state;
        }
        finally
        {
            RuntimeJson.DisposeAll(actionDocuments);
        }
    }

    public async ValueTask<OrchestratorRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        OrchestratorState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            if (maxCycles > 0 && cycles >= maxCycles)
            {
                break;
            }

            if (duration is not null && DateTimeOffset.UtcNow - started >= duration.Value)
            {
                break;
            }

            try
            {
                await Task.Delay(Math.Max(100, _options.PollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new OrchestratorRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            cycles,
            lastState?.Phase ?? _lastPhase,
            lastState?.Summary ?? _lastSummary,
            _lastError);
    }

    private async ValueTask WriteStateAsync(
        OrchestratorState state,
        OrchestratorCommands commands,
        CancellationToken cancellationToken)
    {
        await WriteStateOnlyAsync(state, cancellationToken).ConfigureAwait(false);
        await RuntimeJson.WriteTextAtomicAsync(
            _options.CommandsFile,
            JsonSerializer.Serialize(commands, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask WriteStateOnlyAsync(OrchestratorState state, CancellationToken cancellationToken)
    {
        return RuntimeJson.WriteTextAtomicAsync(
            _options.StateFile,
            JsonSerializer.Serialize(state, JsonOptions),
            cancellationToken);
    }

    private string RuntimeFile(string fileName) => Path.Combine(_options.RuntimeDir, fileName);

    private static CabinSnapshot ReadCabin(JsonDocument? document)
    {
        if (document is null)
        {
            return new CabinSnapshot(false, false, new Dictionary<string, CabinChannelSnapshot>(StringComparer.OrdinalIgnoreCase));
        }

        var root = document.RootElement;
        var ready = RuntimeJson.Bool(root, false, "ready");
        var requiresHuman = RuntimeJson.Bool(root, false, "requiresHuman");
        var channels = new Dictionary<string, CabinChannelSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("channels", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var channelId = RuntimeJson.String(item, "channelId");
                if (string.IsNullOrWhiteSpace(channelId))
                {
                    continue;
                }

                channels[channelId] = new CabinChannelSnapshot(
                    channelId,
                    RuntimeJson.String(item, "browser"),
                    RuntimeJson.String(item, "status"),
                    RuntimeJson.Bool(item, false, "isReady"),
                    RuntimeJson.Bool(item, false, "requiresHuman"),
                    RuntimeJson.String(item, "detail"),
                    ReadCabinWindow(item));
            }
        }

        return new CabinSnapshot(ready, requiresHuman, channels);
    }

    private static OrchestratorWindowSnapshot? ReadCabinWindow(JsonElement channel)
    {
        if (!channel.TryGetProperty("window", out var window)
            || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        OrchestratorBounds bounds = new(0, 0, 0, 0);
        if (window.TryGetProperty("bounds", out var boundsElement) && boundsElement.ValueKind == JsonValueKind.Object)
        {
            bounds = new OrchestratorBounds(
                RuntimeJson.Int(boundsElement, 0, "left"),
                RuntimeJson.Int(boundsElement, 0, "top"),
                RuntimeJson.Int(boundsElement, 0, "width"),
                RuntimeJson.Int(boundsElement, 0, "height"));
        }

        var processId = RuntimeJson.Int(window, 0, "processId");
        var processName = RuntimeJson.String(window, "processName");
        var title = RuntimeJson.String(window, "title");
        if (processId <= 0 || string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new OrchestratorWindowSnapshot(processId, processName, title, bounds);
    }

    private static WindowRealitySnapshot ReadWindowReality(JsonDocument? document)
    {
        if (document is null)
        {
            return new WindowRealitySnapshot(false, new Dictionary<string, WindowRealityChannelSnapshot>(StringComparer.OrdinalIgnoreCase));
        }

        var channels = new Dictionary<string, WindowRealityChannelSnapshot>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("channels", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var channelId = RuntimeJson.String(item, "channelId");
                if (string.IsNullOrWhiteSpace(channelId))
                {
                    continue;
                }

                var decision = item.TryGetProperty("decision", out var decisionElement)
                    ? decisionElement
                    : default;
                channels[channelId] = new WindowRealityChannelSnapshot(
                    channelId,
                    RuntimeJson.String(item, "status"),
                    RuntimeJson.Bool(item, false, "isOperational", "structuralReady"),
                    RuntimeJson.Bool(item, false, "requiresHuman"),
                    RuntimeJson.Bool(item, false, "handsMayAct", "actionReady"),
                    RuntimeJson.String(decision, "reason"));
            }
        }

        return new WindowRealitySnapshot(true, channels);
    }

    private static VisionSnapshot ReadVision(JsonDocument? document)
    {
        if (document is null)
        {
            return new VisionSnapshot([]);
        }

        var windows = new List<OrchestratorWindowSnapshot>();
        if (document.RootElement.TryGetProperty("visibleWindows", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                OrchestratorBounds bounds = new(0, 0, 0, 0);
                if (item.TryGetProperty("bounds", out var boundsElement) && boundsElement.ValueKind == JsonValueKind.Object)
                {
                    bounds = new OrchestratorBounds(
                        RuntimeJson.Int(boundsElement, 0, "left"),
                        RuntimeJson.Int(boundsElement, 0, "top"),
                        RuntimeJson.Int(boundsElement, 0, "width"),
                        RuntimeJson.Int(boundsElement, 0, "height"));
                }

                windows.Add(new OrchestratorWindowSnapshot(
                    RuntimeJson.Int(item, 0, "processId"),
                    RuntimeJson.String(item, "processName"),
                    RuntimeJson.String(item, "title"),
                    bounds));
            }
        }

        return new VisionSnapshot(windows);
    }

    private static PerceptionSnapshot ReadPerception(JsonDocument? document)
    {
        if (document is null)
        {
            return new PerceptionSnapshot(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        var channels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.TryGetProperty("channelIds", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    channels.Add(item.GetString()!);
                }
            }
        }

        return new PerceptionSnapshot(channels);
    }

    private static InteractionSnapshot ReadInteraction(JsonDocument document)
    {
        var root = document.RootElement;
        return new InteractionSnapshot(RuntimeJson.Int(root, 0, "actionableTargets"));
    }

    private static HandsSnapshot ReadHands(JsonDocument document)
    {
        var root = document.RootElement;
        return new HandsSnapshot(
            RuntimeJson.Int(root, 0, "actionsExecuted"),
            RuntimeJson.Int(root, 0, "actionsVerified"),
            RuntimeJson.Int(root, 0, "actionsSkipped"));
    }

    private static Dictionary<string, int> ReadActionFailures(IReadOnlyList<JsonDocument> actionDocuments)
    {
        var failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in actionDocuments)
        {
            var root = document.RootElement;
            if (!RuntimeJson.String(root, "eventType").Equals("action_event", StringComparison.OrdinalIgnoreCase)
                || !RuntimeJson.String(root, "actionType").Equals("open_chat", StringComparison.OrdinalIgnoreCase)
                || !RuntimeJson.String(root, "status").Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!root.TryGetProperty("target", out var target))
            {
                continue;
            }

            var channelId = RuntimeJson.String(target, "channelId");
            var summary = string.Join(" ",
                RuntimeJson.String(target, "executionSummary"),
                root.TryGetProperty("verification", out var verification) ? RuntimeJson.String(verification, "summary") : string.Empty);
            if (string.IsNullOrWhiteSpace(channelId)
                || !summary.Contains("No visible WhatsApp browser window", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            failures[channelId] = failures.TryGetValue(channelId, out var count) ? count + 1 : 1;
        }

        return failures;
    }

    private IReadOnlyList<string> StaleStates(params JsonDocument?[] documents)
    {
        if (_options.StaleStateSeconds <= 0)
        {
            return [];
        }

        var stale = new List<string>();
        foreach (var document in documents)
        {
            if (document is null)
            {
                continue;
            }

            var root = document.RootElement;
            var status = RuntimeJson.String(root, "status");
            var updatedAt = RuntimeJson.Date(root, "updatedAt", "observedAt");
            if (updatedAt is null)
            {
                continue;
            }

            if (DateTimeOffset.UtcNow - updatedAt.Value.ToUniversalTime() > TimeSpan.FromSeconds(_options.StaleStateSeconds))
            {
                stale.Add($"{status}: estado sin actualizar desde {updatedAt.Value:yyyy-MM-dd HH:mm:ss}.");
            }
        }

        return stale;
    }

    private static OrchestratorWindowSnapshot? FindWhatsAppWindow(
        OrchestratorChannelMapping mapping,
        IReadOnlyList<OrchestratorWindowSnapshot> windows)
    {
        return windows
            .Where(item =>
                item.ProcessName.Equals(mapping.BrowserProcess, StringComparison.OrdinalIgnoreCase)
                && LooksLikeWhatsApp(item.Title)
                && item.Bounds.Width >= 500
                && item.Bounds.Height >= 500)
            .OrderByDescending(item => item.Bounds.Width * item.Bounds.Height)
            .FirstOrDefault();
    }

    private static bool LooksLikeWhatsApp(string title)
    {
        return title.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase);
    }

    private static string DeterminePhase(
        IReadOnlyList<OrchestratorChannelState> channels,
        IReadOnlyList<OrchestratorBlocker> blockers,
        HandsSnapshot? hands,
        InteractionSnapshot? interaction)
    {
        if (blockers.Any(item => item.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)))
        {
            return "blocked";
        }

        if (channels.Any(item => !item.ActionsAllowed)
            || blockers.Any(item => item.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "degraded";
        }

        if ((hands?.ActionsExecuted ?? 0) > 0)
        {
            return "acting";
        }

        if ((interaction?.ActionableTargets ?? 0) > 0)
        {
            return "observing";
        }

        return "ready";
    }

    private static double OverlapRatio(OrchestratorBounds a, OrchestratorBounds b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Left + a.Width, b.Left + b.Width);
        var bottom = Math.Min(a.Top + a.Height, b.Top + b.Height);
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        var intersection = width * height;
        var area = Math.Max(1, b.Width * b.Height);
        return intersection / (double)area;
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var clean = values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return clean.Length == 0 ? "ninguno" : string.Join(",", clean);
    }

    private sealed record CabinSnapshot(
        bool Ready,
        bool RequiresHuman,
        IReadOnlyDictionary<string, CabinChannelSnapshot> Channels);

    private sealed record CabinChannelSnapshot(
        string ChannelId,
        string Browser,
        string Status,
        bool IsReady,
        bool RequiresHuman,
        string Detail,
        OrchestratorWindowSnapshot? Window)
    {
        public static CabinChannelSnapshot Missing(string channelId, string browser)
        {
            return new CabinChannelSnapshot(channelId, browser, "MISSING", false, false, "No hay diagnostico de cabina para este canal.", null);
        }
    }

    private sealed record VisionSnapshot(IReadOnlyList<OrchestratorWindowSnapshot> Windows);

    private sealed record WindowRealitySnapshot(
        bool Available,
        IReadOnlyDictionary<string, WindowRealityChannelSnapshot> Channels);

    private sealed record WindowRealityChannelSnapshot(
        string ChannelId,
        string Status,
        bool IsOperational,
        bool RequiresHuman,
        bool HandsMayAct,
        string Reason)
    {
        public static WindowRealityChannelSnapshot Missing(string channelId)
        {
            return new WindowRealityChannelSnapshot(
                channelId,
                "MISSING",
                false,
                false,
                false,
                "Reality Resolver no tiene este canal en su consenso.");
        }
    }

    private sealed record PerceptionSnapshot(IReadOnlySet<string> ChannelIds);

    private sealed record InteractionSnapshot(int ActionableTargets);

    private sealed record HandsSnapshot(int ActionsExecuted, int ActionsVerified, int ActionsSkipped);
}
