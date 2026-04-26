using AriadGSM.Vision.Config;
using AriadGSM.Vision.Pipeline;

var configPath = args.Length > 0 ? args[0] : Path.Combine("config", "vision.example.json");
var options = VisionOptions.Load(configPath);
var worker = new VisionPipeline(options);
var state = await worker.RunOnceAsync();
Console.WriteLine($"AriadGSM Vision Worker: {state.Status}, frames={state.FramesCaptured}, events={state.EventsWritten}, storage={state.StorageRoot}");
