using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Execution;

public sealed class DryRunHandsExecutor : IHandsExecutor
{
    public ValueTask<ExecutionResult> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.ActionType.Equals("noop", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new ExecutionResult("verified", "No action needed.", 1));
        }

        return ValueTask.FromResult(new ExecutionResult(
            "planned",
            "Dry-run mode: action was planned and audited, but mouse/keyboard were not moved.",
            0.75));
    }
}
