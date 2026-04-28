using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Input;

public sealed class InputArbiter
{
    private readonly HandsOptions _options;
    private DateTimeOffset _lastAiControlAt = DateTimeOffset.MinValue;
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;

    public InputArbiter(HandsOptions options)
    {
        _options = options;
    }

    public InputLease Acquire(ActionPlan plan)
    {
        var requiresInput = RequiresHumanInputSurface(plan.ActionType);
        if (!_options.InputArbiterEnabled || !_options.ExecuteActions || !requiresInput)
        {
            return InputLease.Allow(
                "bypass",
                "Esta accion no necesita tomar el mouse.",
                GetOperatorIdleMilliseconds(),
                _options.OperatorIdleRequiredMs,
                requiresInput);
        }

        var now = DateTimeOffset.UtcNow;
        var operatorIdleMs = GetOperatorIdleMilliseconds();
        var recentAiControl = _lastAiControlAt != DateTimeOffset.MinValue
            && now - _lastAiControlAt <= TimeSpan.FromMilliseconds(Math.Max(100, _options.AiControlLeaseMs));

        if (now < _cooldownUntil && !recentAiControl)
        {
            var lease = InputLease.Blocked(
                "operator_cooldown",
                "Detecte control humano reciente; dejo quietas las manos y sigo observando.",
                operatorIdleMs,
                _options.OperatorIdleRequiredMs,
                requiresInput);
            WriteState(lease, plan, "operator_control");
            return lease;
        }

        if (operatorIdleMs < Math.Max(100, _options.OperatorIdleRequiredMs) && !recentAiControl)
        {
            _cooldownUntil = now.AddMilliseconds(Math.Max(250, _options.OperatorCooldownMs));
            var lease = InputLease.Blocked(
                "operator_active",
                "Tu estas usando mouse o teclado; la IA no pelea el control.",
                operatorIdleMs,
                _options.OperatorIdleRequiredMs,
                requiresInput);
            WriteState(lease, plan, "operator_control");
            return lease;
        }

        var granted = InputLease.Allow(
            $"lease-{now.ToUnixTimeMilliseconds()}",
            recentAiControl
                ? "Continuo una accion corta ya iniciada por la IA."
                : "Operador inactivo; la IA puede mover mouse de forma controlada.",
            operatorIdleMs,
            _options.OperatorIdleRequiredMs,
            requiresInput);
        WriteState(granted, plan, "ai_control_granted");
        return granted;
    }

    public void Complete(InputLease lease, ActionPlan plan, ExecutionResult execution)
    {
        if (!lease.RequiresInput || !_options.InputArbiterEnabled || !_options.ExecuteActions)
        {
            return;
        }

        _lastAiControlAt = DateTimeOffset.UtcNow;
        var completed = lease with
        {
            Granted = execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase),
            Reason = execution.Summary,
            OperatorIdleMs = GetOperatorIdleMilliseconds()
        };
        WriteState(completed, plan, completed.Granted ? "ai_control_released" : "ai_control_failed");
    }

    private static bool RequiresHumanInputSurface(string actionType)
    {
        return actionType.Equals("focus_window", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("open_chat", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("scroll_history", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("write_text", StringComparison.OrdinalIgnoreCase)
            || actionType.Equals("send_message", StringComparison.OrdinalIgnoreCase);
    }

    private void WriteState(InputLease lease, ActionPlan plan, string phase)
    {
        try
        {
            var directory = Path.GetDirectoryName(_options.InputArbiterStateFile);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var state = new Dictionary<string, object?>
            {
                ["status"] = lease.Granted ? "ok" : "attention",
                ["engine"] = "ariadgsm_input_arbiter",
                ["phase"] = phase,
                ["updatedAt"] = DateTimeOffset.UtcNow,
                ["leaseId"] = lease.LeaseId,
                ["blockedActionId"] = plan.ActionId,
                ["actionType"] = plan.ActionType,
                ["channelId"] = TargetString(plan, "channelId"),
                ["conversationTitle"] = TargetString(plan, "conversationTitle") ?? TargetString(plan, "chatRowTitle"),
                ["operatorIdleMs"] = lease.OperatorIdleMs,
                ["requiredIdleMs"] = lease.RequiredIdleMs,
                ["operatorHasPriority"] = !lease.Granted,
                ["handsPausedOnly"] = !lease.Granted,
                ["eyesContinue"] = true,
                ["memoryContinue"] = true,
                ["cognitiveContinue"] = true,
                ["summary"] = lease.Reason
            };
            WriteTextAtomic(
                _options.InputArbiterStateFile,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static string? TargetString(ActionPlan plan, string key)
    {
        return plan.Target.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static long GetOperatorIdleMilliseconds()
    {
        try
        {
            var lastInput = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref lastInput))
            {
                return long.MaxValue;
            }

            var now = GetTickCount64();
            var idle = now >= lastInput.dwTime ? now - lastInput.dwTime : 0;
            return idle > long.MaxValue ? long.MaxValue : (long)idle;
        }
        catch
        {
            return long.MaxValue;
        }
    }

    private static void WriteTextAtomic(string path, string text)
    {
        var tempPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tempPath, text, Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}

public sealed record InputLease(
    bool Granted,
    string LeaseId,
    string Reason,
    long OperatorIdleMs,
    int RequiredIdleMs,
    bool RequiresInput)
{
    public static InputLease Allow(string leaseId, string reason, long operatorIdleMs, int requiredIdleMs, bool requiresInput)
    {
        return new InputLease(true, leaseId, reason, operatorIdleMs, requiredIdleMs, requiresInput);
    }

    public static InputLease Blocked(string leaseId, string reason, long operatorIdleMs, int requiredIdleMs, bool requiresInput)
    {
        return new InputLease(false, leaseId, reason, operatorIdleMs, requiredIdleMs, requiresInput);
    }
}
