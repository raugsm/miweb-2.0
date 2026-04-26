using System.Text.Json;
using System.IO;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Pipeline;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var configPath = args.Length > 1 ? args[1] : Path.Combine("config", "perception.example.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

switch (command)
{
    case "status":
        Console.WriteLine("AriadGSM Perception CLI ready. Commands: status, sample, diagnose, watch.");
        break;
    case "sample":
        var sampleOptions = PerceptionOptions.Load(configPath);
        var samplePipeline = new PerceptionPipeline(sampleOptions);
        var sampleState = await samplePipeline.RunOnceAsync();
        Console.WriteLine($"sample {sampleState.Status}: whatsapp_windows={sampleState.WhatsAppWindowsDetected}, channels={string.Join(",", sampleState.ChannelIds)}, reader={sampleState.LastReaderStatus}, lines={sampleState.ReaderLinesObserved}, messages={sampleState.MessagesExtracted}, conversations={sampleState.ConversationEventsWritten}, source={sampleState.LastSourceVisionEventId}");
        if (!string.IsNullOrWhiteSpace(sampleState.LastError))
        {
            Console.WriteLine($"error={sampleState.LastError}");
        }
        break;
    case "diagnose":
        var diagnoseOptions = PerceptionOptions.Load(configPath);
        var diagnosePipeline = new PerceptionPipeline(diagnoseOptions);
        var health = await diagnosePipeline.RunOnceAsync();
        Console.WriteLine(JsonSerializer.Serialize(health, jsonOptions));
        break;
    case "watch":
        var watchOptions = PerceptionOptions.Load(configPath);
        var seconds = args.Length > 2 && double.TryParse(args[2], out var parsedSeconds)
            ? parsedSeconds
            : watchOptions.DurationSeconds;
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
            var watchPipeline = new PerceptionPipeline(watchOptions);
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
