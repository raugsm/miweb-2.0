using System.Text.Json;
using AriadGSM.Hands.Config;
using AriadGSM.Hands.Decisions;
using AriadGSM.Hands.Events;
using AriadGSM.Hands.Execution;
using AriadGSM.Hands.Perception;
using AriadGSM.Hands.Planning;
using AriadGSM.Hands.Safety;
using AriadGSM.Hands.Verification;

namespace AriadGSM.Hands.Pipeline;

public sealed class HandsPipeline
{
    private readonly HandsOptions _options;
    private readonly DecisionEventReader _decisionReader;
    private readonly PerceptionContextReader _perceptionReader;
    private readonly ActionPlanner _planner = new();
    private readonly HandsSafetyPolicy _safety;
    private readonly IHandsExecutor _executor;
    private readonly ActionVerifier _verifier = new();
    private readonly ActionEventWriter _writer;
    private int _idleCycles;
    private int _decisionsRead;
    private int _actionsPlanned;
    private int _actionsWritten;
    private int _actionsBlocked;
    private int _actionsExecuted;
    private int _actionsVerified;
    private int _actionsSkipped;
    private string _lastActionId = string.Empty;
    private string _lastSummary = string.Empty;
    private string _lastError = string.Empty;

    public HandsPipeline(HandsOptions options, IHandsExecutor? executor = null)
    {
        _options = options;
        _decisionReader = new DecisionEventReader(
            [options.CognitiveDecisionEventsFile, options.OperatingDecisionEventsFile],
            options.DecisionLimit);
        _perceptionReader = new PerceptionContextReader(options.PerceptionEventsFile, options.PerceptionLimit);
        _safety = new HandsSafetyPolicy(options);
        _executor = executor ?? (options.ExecuteActions ? new Win32HandsExecutor(options) : new DryRunHandsExecutor());
        _writer = new ActionEventWriter(options.ActionEventsFile);
    }

    public async ValueTask<HandsHealthState> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var decisions = await _decisionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            _decisionsRead = decisions.Count;

            if (decisions.Count == 0)
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", "No decision events available.", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            var perception = await _perceptionReader.ReadLatestAsync(cancellationToken).ConfigureAwait(false);
            var existingIds = await _writer.ReadExistingActionIdsAsync(cancellationToken).ConfigureAwait(false);
            var writtenThisCycle = 0;

            foreach (var decision in decisions)
            {
                foreach (var plan in _planner.Plan(decision))
                {
                    _actionsPlanned++;
                    if (existingIds.Contains(plan.ActionId))
                    {
                        _actionsSkipped++;
                        continue;
                    }

                    var safety = _safety.Evaluate(plan);
                    ActionEvent actionEvent;
                    if (safety.Blocked)
                    {
                        _actionsBlocked++;
                        actionEvent = CreateActionEvent(
                            plan,
                            "blocked",
                            new ActionVerification(false, safety.Reason, 0),
                            safety.Reason,
                            executionSummary: string.Empty);
                    }
                    else
                    {
                        var execution = await _executor.ExecuteAsync(plan, cancellationToken).ConfigureAwait(false);
                        if (execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase))
                        {
                            _actionsExecuted++;
                        }

                        var verification = _verifier.Verify(plan, execution, perception);
                        var finalStatus = execution.Status.Equals("executed", StringComparison.OrdinalIgnoreCase) && verification.Verified
                            ? "verified"
                            : execution.Status;
                        if (finalStatus.Equals("verified", StringComparison.OrdinalIgnoreCase))
                        {
                            _actionsVerified++;
                        }

                        actionEvent = CreateActionEvent(
                            plan,
                            finalStatus,
                            verification,
                            safety.Reason,
                            execution.Summary);
                    }

                    var errors = ActionContractValidator.Validate(actionEvent);
                    if (errors.Count > 0)
                    {
                        throw new InvalidOperationException(string.Join("; ", errors));
                    }

                    await _writer.AppendAsync(actionEvent, cancellationToken).ConfigureAwait(false);
                    existingIds.Add(actionEvent.ActionId);
                    writtenThisCycle++;
                    _actionsWritten++;
                    _lastActionId = actionEvent.ActionId;
                    _lastSummary = $"{actionEvent.ActionType}: {actionEvent.Status}. {actionEvent.Verification.Summary}";
                }
            }

            if (writtenThisCycle == 0)
            {
                _idleCycles++;
                return await WriteStateAsync(CreateState("idle", "No new actions; all known action ids were already emitted.", string.Empty), cancellationToken).ConfigureAwait(false);
            }

            return await WriteStateAsync(CreateState("ok", _lastSummary, string.Empty), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _lastError = exception.Message;
            return await WriteStateAsync(CreateState("error", _lastSummary, exception.Message), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask<HandsRunSummary> RunContinuousAsync(
        int maxCycles = 0,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cycles = 0;
        HandsHealthState? lastState = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            cycles++;
            lastState = await RunOnceAsync(cancellationToken).ConfigureAwait(false);
            if (maxCycles > 0 && cycles >= maxCycles)
            {
                break;
            }

            if (duration is not null && DateTimeOffset.UtcNow - started >= duration.Value)
            {
                break;
            }

            try
            {
                await Task.Delay(Math.Max(50, _options.PollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return new HandsRunSummary(
            lastState?.Status ?? "completed",
            started,
            DateTimeOffset.UtcNow,
            cycles,
            _idleCycles,
            _decisionsRead,
            _actionsPlanned,
            _actionsWritten,
            _actionsBlocked,
            _actionsExecuted,
            _actionsVerified,
            _actionsSkipped,
            _lastActionId,
            _lastSummary,
            _lastError);
    }

    private ActionEvent CreateActionEvent(
        ActionPlan plan,
        string status,
        ActionVerification verification,
        string safetyReason,
        string executionSummary)
    {
        var target = new Dictionary<string, object?>(plan.Target, StringComparer.OrdinalIgnoreCase)
        {
            ["requiredAutonomyLevel"] = plan.RequiredAutonomyLevel,
            ["handsAutonomyLevel"] = _options.AutonomyLevel,
            ["executeActions"] = _options.ExecuteActions,
            ["safetyReason"] = safetyReason,
            ["executionSummary"] = executionSummary
        };

        return new ActionEvent(
            "action_event",
            plan.ActionId,
            DateTimeOffset.UtcNow,
            plan.ActionType,
            target,
            status,
            verification);
    }

    private HandsHealthState CreateState(string status, string summary, string error)
    {
        return new HandsHealthState(
            status,
            DateTimeOffset.UtcNow,
            _decisionsRead,
            _actionsPlanned,
            _actionsWritten,
            _actionsBlocked,
            _actionsExecuted,
            _actionsVerified,
            _actionsSkipped,
            _options.CognitiveDecisionEventsFile,
            _options.OperatingDecisionEventsFile,
            _options.PerceptionEventsFile,
            _options.ActionEventsFile,
            _lastActionId,
            summary,
            error);
    }

    private async ValueTask<HandsHealthState> WriteStateAsync(HandsHealthState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_options.StateFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_options.StateFile, json, cancellationToken).ConfigureAwait(false);
        return state;
    }
}
