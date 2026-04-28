using System.Diagnostics.Tracing;

namespace AriadGSM.Agent.Desktop;

[EventSource(Name = "AriadGSM-Agent-Desktop")]
internal sealed class AriadGSMTelemetryEventSource : EventSource
{
    public static readonly AriadGSMTelemetryEventSource Log = new();

    private AriadGSMTelemetryEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational, Message = "{0}")]
    public void RuntimeLog(string message)
    {
        WriteEvent(1, message);
    }

    [NonEvent]
    public void SafeRuntimeLog(string message)
    {
        try
        {
            var bounded = message.Length > 1200 ? message[..1200] : message;
            RuntimeLog(bounded);
        }
        catch
        {
            // EventSource must never be another reason for the desktop app to fail.
        }
    }
}
