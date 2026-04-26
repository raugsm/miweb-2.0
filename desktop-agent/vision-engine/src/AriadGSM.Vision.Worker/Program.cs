using AriadGSM.Vision.Config;
using AriadGSM.Vision.Pipeline;

var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("config", "vision.example.json");
var once = args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase));
var options = VisionOptions.Load(configPath);
var worker = new VisionPipeline(options);
if (once)
{
    var state = await worker.RunOnceAsync();
    Console.WriteLine($"AriadGSM Vision Worker: {state.Status}, frames={state.FramesCaptured}, events={state.EventsWritten}, skipped={state.FramesSkipped}, storage={state.StorageRoot}");
    Console.WriteLine($"capture={state.CaptureMode}, screen={state.ScreenWidth}x{state.ScreenHeight}, windows={state.VisibleWindowCount}, frame={state.LastFramePath}");
}
else
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };
    var summary = await worker.RunContinuousAsync(
        options.MaxCycles,
        options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
        cts.Token);
    Console.WriteLine($"AriadGSM Vision Worker: {summary.Status}, frames={summary.FramesCaptured}, events={summary.EventsWritten}, skipped={summary.FramesSkipped}, windows={summary.VisibleWindowCount}");
}
