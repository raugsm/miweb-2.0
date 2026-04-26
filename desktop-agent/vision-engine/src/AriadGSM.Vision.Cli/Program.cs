using System.Text.Json;
using AriadGSM.Vision.Config;
using AriadGSM.Vision.Pipeline;
using AriadGSM.Vision.Windows;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var configPath = args.Length > 1 ? args[1] : Path.Combine("config", "vision.example.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

switch (command)
{
    case "status":
        Console.WriteLine("AriadGSM Vision CLI ready. Commands: status, sample, diagnose, watch, windows.");
        break;
    case "sample":
        var options = VisionOptions.Load(configPath);
        var worker = new VisionPipeline(options);
        var state = await worker.RunOnceAsync();
        Console.WriteLine($"sample ok: capture={state.CaptureMode}, screen={state.ScreenWidth}x{state.ScreenHeight}, changed={state.LastFrameChanged}, change={state.LastChangeScore:0.000000}, windows={state.VisibleWindowCount}, frame={state.LastFramePath}, events={state.EventsWritten}");
        break;
    case "diagnose":
        var diagnoseOptions = VisionOptions.Load(configPath);
        var pipeline = new VisionPipeline(diagnoseOptions);
        var health = await pipeline.RunOnceAsync();
        Console.WriteLine(JsonSerializer.Serialize(health, jsonOptions));
        break;
    case "watch":
        var watchOptions = VisionOptions.Load(configPath);
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
            var watchPipeline = new VisionPipeline(watchOptions);
            var summary = await watchPipeline.RunContinuousAsync(
                maxCycles,
                seconds > 0 ? TimeSpan.FromSeconds(seconds) : null,
                cts.Token);
            Console.WriteLine(JsonSerializer.Serialize(summary, jsonOptions));
        }
        break;
    case "windows":
        var enumerator = new Win32WindowEnumerator();
        foreach (var window in enumerator.GetVisibleWindows().Take(30))
        {
            Console.WriteLine($"{window.ProcessName} [{window.ProcessId}] {window.Bounds.Width}x{window.Bounds.Height} {window.Title}");
        }
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        return 2;
}

return 0;
