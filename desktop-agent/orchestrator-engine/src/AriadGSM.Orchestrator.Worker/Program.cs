using AriadGSM.Orchestrator.Config;
using AriadGSM.Orchestrator.Pipeline;

var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("config", "orchestrator.example.json");
var once = args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase));
var options = OrchestratorOptions.Load(configPath);
var pipeline = new OrchestratorPipeline(options);

if (once)
{
    var state = await pipeline.RunOnceAsync();
    Console.WriteLine($"AriadGSM Orchestrator Worker: {state.Status}, phase={state.Phase}, {state.Summary}");
    if (!string.IsNullOrWhiteSpace(state.LastError))
    {
        Console.WriteLine($"error={state.LastError}");
    }
}
else
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };
    var summary = await pipeline.RunContinuousAsync(
        options.MaxCycles,
        options.DurationSeconds > 0 ? TimeSpan.FromSeconds(options.DurationSeconds) : null,
        cts.Token);
    Console.WriteLine($"AriadGSM Orchestrator Worker: {summary.Status}, cycles={summary.Cycles}, phase={summary.Phase}, {summary.Summary}");
}
