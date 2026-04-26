using System.Text.Json;
using AriadGSM.Vision.Config;
using AriadGSM.Vision.Pipeline;
using AriadGSM.Vision.Windows;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
var configPath = args.Length > 1 ? args[1] : Path.Combine("config", "vision.example.json");

switch (command)
{
    case "status":
        Console.WriteLine("AriadGSM Vision CLI ready. Commands: status, sample, diagnose, windows.");
        break;
    case "sample":
        var options = VisionOptions.Load(configPath);
        var worker = new VisionPipeline(options);
        var state = await worker.RunOnceAsync();
        Console.WriteLine($"sample ok: capture={state.CaptureMode}, screen={state.ScreenWidth}x{state.ScreenHeight}, windows={state.VisibleWindowCount}, frame={state.LastFramePath}, events={state.EventsWritten}");
        break;
    case "diagnose":
        var diagnoseOptions = VisionOptions.Load(configPath);
        var pipeline = new VisionPipeline(diagnoseOptions);
        var health = await pipeline.RunOnceAsync();
        Console.WriteLine(JsonSerializer.Serialize(health, new JsonSerializerOptions { WriteIndented = true }));
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
