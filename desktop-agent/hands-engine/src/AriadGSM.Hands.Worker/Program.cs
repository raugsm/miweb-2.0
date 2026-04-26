using System.IO;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Pipeline;

var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("config", "hands.example.json");
var once = args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase));
var options = HandsOptions.Load(configPath);
var pipeline = new HandsPipeline(options);

if (once)
{
    var state = await pipeline.RunOnceAsync();
    Console.WriteLine($"AriadGSM Hands Worker: {state.Status}, decisions={state.DecisionsRead}, planned={state.ActionsPlanned}, written={state.ActionsWritten}, blocked={state.ActionsBlocked}, executed={state.ActionsExecuted}, verified={state.ActionsVerified}, skipped={state.ActionsSkipped}");
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
    var summary = await pipeline.RunContinuousAsync(options.MaxCycles, null, cts.Token);
    Console.WriteLine($"AriadGSM Hands Worker: {summary.Status}, cycles={summary.Cycles}, planned={summary.ActionsPlanned}, written={summary.ActionsWritten}, blocked={summary.ActionsBlocked}, executed={summary.ActionsExecuted}, verified={summary.ActionsVerified}, skipped={summary.ActionsSkipped}");
}
