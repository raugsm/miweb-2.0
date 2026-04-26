using System.IO;
using AriadGSM.Perception.Config;
using AriadGSM.Perception.Pipeline;

var configPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal)) ?? Path.Combine("config", "perception.example.json");
var once = args.Any(arg => arg.Equals("--once", StringComparison.OrdinalIgnoreCase));
var options = PerceptionOptions.Load(configPath);
var pipeline = new PerceptionPipeline(options);
if (once)
{
    var state = await pipeline.RunOnceAsync();
    Console.WriteLine($"AriadGSM Perception Worker: {state.Status}, whatsapp_windows={state.WhatsAppWindowsDetected}, events={state.PerceptionEventsWritten}, conversations={state.ConversationEventsWritten}, messages={state.MessagesExtracted}, channels={string.Join(",", state.ChannelIds)}, extraction={state.LastExtractionSummary}");
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
    Console.WriteLine($"AriadGSM Perception Worker: {summary.Status}, cycles={summary.Cycles}, events={summary.PerceptionEventsWritten}, conversations={summary.ConversationEventsWritten}, messages={summary.LastMessagesExtracted}, idle={summary.IdleCycles}, channels={string.Join(",", summary.LastChannelIds)}");
}
