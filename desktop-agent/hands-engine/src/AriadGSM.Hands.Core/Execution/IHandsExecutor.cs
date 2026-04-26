using AriadGSM.Hands.Planning;

namespace AriadGSM.Hands.Execution;

public interface IHandsExecutor
{
    ValueTask<ExecutionResult> ExecuteAsync(ActionPlan plan, CancellationToken cancellationToken = default);
}
