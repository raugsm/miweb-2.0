using AriadGSM.Interaction.Config;
using AriadGSM.Interaction.Pipeline;

var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("config", "interaction.example.json");
var once = args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase));
var options = InteractionOptions.Load(configPath);
var pipeline = new InteractionPipeline(options);
if (once)
{
    var state = await pipeline.RunOnceAsync();
    Console.WriteLine($"AriadGSM Interaction Worker: {state.Status}, perception={state.PerceptionEventsRead}, targets={state.TargetsObserved}, actionable={state.ActionableTargets}, accepted={state.TargetsAccepted}, rejected={state.TargetsRejected}, best={state.LastAcceptedTargetTitle}");
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
    Console.WriteLine($"AriadGSM Interaction Worker: {summary.Status}, cycles={summary.Cycles}, events={summary.InteractionEventsWritten}, actionable={summary.ActionableTargets}, idle={summary.IdleCycles}");
}
