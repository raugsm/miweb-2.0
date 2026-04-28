using System.Text.Json;
using AriadGSM.Orchestrator.Config;
using AriadGSM.Orchestrator.Pipeline;

await TestOrchestratorPausesMissingChannel();
Console.WriteLine("AriadGSM Orchestrator tests OK");
return 0;

static async Task TestOrchestratorPausesMissingChannel()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-orchestrator-test-" + Guid.NewGuid().ToString("N"));
    var runtime = Path.Combine(root, "runtime");
    Directory.CreateDirectory(runtime);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(runtime, "cabin-readiness.json"), JsonSerializer.Serialize(new
        {
            status = "ready",
            ready = true,
            requiresHuman = false,
            updatedAt = DateTimeOffset.UtcNow,
            channels = new object[]
            {
                new
                {
                    channelId = "wa-1",
                    browser = "msedge",
                    status = "READY",
                    isReady = true,
                    requiresHuman = false,
                    detail = "edge ready",
                    window = new
                    {
                        processId = 10,
                        processName = "msedge",
                        title = "(34) WhatsApp Business - Perfil 1: Microsoft Edge",
                        bounds = new { left = 0, top = 0, width = 1146, height = 1392 }
                    }
                },
                new { channelId = "wa-2", browser = "chrome", status = "READY", isReady = true, requiresHuman = false, detail = "chrome ready" },
                new { channelId = "wa-3", browser = "firefox", status = "READY", isReady = true, requiresHuman = false, detail = "firefox ready" }
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "vision-health.json"), JsonSerializer.Serialize(new
        {
            status = "ok",
            updatedAt = DateTimeOffset.UtcNow,
            visibleWindows = new object[]
            {
                new { processId = 10, processName = "msedge", title = "Restaurar paginas", bounds = new { left = 0, top = 0, width = 340, height = 180 } },
                new { processId = 11, processName = "chrome", title = "(29) WhatsApp Business - Google Chrome", bounds = new { left = 1146, top = 0, width = 1146, height = 1392 } },
                new { processId = 12, processName = "firefox", title = "(29) WhatsApp Business - Mozilla Firefox", bounds = new { left = 2292, top = 0, width = 1148, height = 1392 } }
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "perception-health.json"), JsonSerializer.Serialize(new
        {
            status = "ok",
            updatedAt = DateTimeOffset.UtcNow,
            channelIds = new[] { "wa-2", "wa-3" }
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "interaction-state.json"), JsonSerializer.Serialize(new
        {
            status = "ok",
            updatedAt = DateTimeOffset.UtcNow,
            actionableTargets = 3
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "hands-state.json"), JsonSerializer.Serialize(new
        {
            status = "idle",
            updatedAt = DateTimeOffset.UtcNow,
            actionsExecuted = 12,
            actionsVerified = 6,
            actionsSkipped = 4781
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "window-reality-state.json"), JsonSerializer.Serialize(new
        {
            status = "attention",
            updatedAt = DateTimeOffset.UtcNow,
            channels = new object[]
            {
                new
                {
                    channelId = "wa-1",
                    status = "MISSING_OR_WRONG_SESSION",
                    isOperational = false,
                    requiresHuman = false,
                    handsMayAct = false,
                    decision = new { reason = "Vision no confirma wa-1 en pantalla." }
                },
                new
                {
                    channelId = "wa-2",
                    status = "READY",
                    isOperational = true,
                    requiresHuman = false,
                    handsMayAct = true,
                    decision = new { reason = "wa-2 esta visible, fresco y accionable." }
                },
                new
                {
                    channelId = "wa-3",
                    status = "READY",
                    isOperational = true,
                    requiresHuman = false,
                    handsMayAct = true,
                    decision = new { reason = "wa-3 esta visible, fresco y accionable." }
                }
            }
        }));
        await File.WriteAllTextAsync(Path.Combine(runtime, "action-events.jsonl"), JsonSerializer.Serialize(new
        {
            eventType = "action_event",
            actionType = "open_chat",
            status = "failed",
            target = new
            {
                channelId = "wa-1",
                executionSummary = "No visible WhatsApp browser window was found for channel 'wa-1'."
            },
            verification = new
            {
                summary = "No visible WhatsApp browser window was found for channel 'wa-1'."
            }
        }) + Environment.NewLine);

        var stateFile = Path.Combine(runtime, "orchestrator-state.json");
        var commandsFile = Path.Combine(runtime, "orchestrator-commands.json");
        var options = new OrchestratorOptions
        {
            RuntimeDir = runtime,
            StateFile = stateFile,
            CommandsFile = commandsFile,
            ActionTailLines = 20,
            HighSkippedActionsThreshold = 1000
        };

        var state = await new OrchestratorPipeline(options).RunOnceAsync();
        Assert(state.Status == "attention", "orchestrator should degrade when one channel is missing");
        Assert(state.Channels.Single(item => item.ChannelId == "wa-1").ActionsAllowed == false, "wa-1 should be paused");
        Assert(state.Channels.Single(item => item.ChannelId == "wa-1").Window is not null, "wa-1 should keep cabin identity for diagnostics");
        Assert(state.Channels.Single(item => item.ChannelId == "wa-2").ActionsAllowed, "wa-2 should remain allowed");
        Assert(state.Blockers.Any(item => item.Code == "channel_hidden_or_covered" && item.ChannelId == "wa-1"), "cabin identity should turn missing vision into hidden/covered diagnosis");
        Assert(state.Blockers.Any(item => item.Code == "channel_missing_from_perception" && item.ChannelId == "wa-1"), "missing perception blocker expected");
        Assert(File.Exists(commandsFile), "commands file should be written");
        using var commands = JsonDocument.Parse(await File.ReadAllTextAsync(commandsFile));
        Assert(commands.RootElement.TryGetProperty("pausedChannels", out var paused), "pausedChannels should exist");
        Assert(paused.EnumerateArray().Any(item => item.GetString() == "wa-1"), "commands should pause wa-1");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
