using System.IO;
using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Pipeline;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var configPath = args.Length > 1 ? args[1] : Path.Combine("config", "hands.example.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

switch (command)
{
    case "status":
        Console.WriteLine("AriadGSM Hands CLI ready. Commands: status, sample, diagnose, watch.");
        break;
    case "sample":
        var sampleOptions = HandsOptions.Load(configPath);
        var samplePipeline = new HandsPipeline(sampleOptions);
        var sampleState = await samplePipeline.RunOnceAsync();
        Console.WriteLine($"sample {sampleState.Status}: decisions={sampleState.DecisionsRead}, planned={sampleState.ActionsPlanned}, written={sampleState.ActionsWritten}, blocked={sampleState.ActionsBlocked}, executed={sampleState.ActionsExecuted}, verified={sampleState.ActionsVerified}, skipped={sampleState.ActionsSkipped}, last={sampleState.LastSummary}");
        if (!string.IsNullOrWhiteSpace(sampleState.LastError))
        {
            Console.WriteLine($"error={sampleState.LastError}");
        }
        break;
    case "diagnose":
        var diagnoseOptions = HandsOptions.Load(configPath);
        var diagnosePipeline = new HandsPipeline(diagnoseOptions);
        var health = await diagnosePipeline.RunOnceAsync();
        Console.WriteLine(JsonSerializer.Serialize(health, jsonOptions));
        break;
    case "watch":
        var watchOptions = HandsOptions.Load(configPath);
        var seconds = args.Length > 2 && double.TryParse(args[2], out var parsedSeconds)
            ? parsedSeconds
            : 0;
        var maxCycles = args.Length > 3 && int.TryParse(args[3], out var parsedCycles)
            ? parsedCycles
            : watchOptions.MaxCycles;
        using (var cts = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };
            var watchPipeline = new HandsPipeline(watchOptions);
            var summary = await watchPipeline.RunContinuousAsync(
                maxCycles,
                seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
                cts.Token);
            Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
        }
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        return 2;
}

return 0;
