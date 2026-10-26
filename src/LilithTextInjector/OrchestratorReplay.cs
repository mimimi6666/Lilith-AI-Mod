using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal sealed class OrchestratorReplayStepResult
{
    internal string EventId { get; }
    internal OrchestratorProcessResult ProcessResult { get; }
    internal OrchestratorControlPlaneState State { get; }

    internal OrchestratorReplayStepResult(
        string eventId,
        OrchestratorProcessResult processResult,
        OrchestratorControlPlaneState state)
    {
        EventId = DecisionCoreContractGuard.Identifier(
            eventId,
            128,
            nameof(eventId));
        ProcessResult = processResult
            ?? throw new ArgumentNullException(nameof(processResult));
        State = state ?? throw new ArgumentNullException(nameof(state));
    }
}

internal sealed class OrchestratorReplayResult
{
    internal OrchestratorControlPlaneState FinalState { get; }
    internal IReadOnlyList<OrchestratorReplayStepResult> Steps { get; }

    internal OrchestratorReplayResult(
        OrchestratorControlPlaneState finalState,
        IReadOnlyList<OrchestratorReplayStepResult> steps)
    {
        FinalState = finalState
            ?? throw new ArgumentNullException(nameof(finalState));
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));

        var copy = new OrchestratorReplayStepResult[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            copy[index] = steps[index]
                ?? throw new ArgumentException(
                    "Replay result cannot contain a null step.",
                    nameof(steps));
        }
        Steps = Array.AsReadOnly(copy);
    }
}

internal static class OrchestratorReplay
{
    internal const int MaximumSteps = 1_024;

    internal static OrchestratorReplayResult Run(
        IReadOnlyList<OrchestratorShadowStep> steps)
    {
        if (steps == null)
            throw new ArgumentNullException(nameof(steps));
        if (steps.Count == 0 || steps.Count > MaximumSteps)
            throw new ArgumentOutOfRangeException(nameof(steps));

        var state = OrchestratorControlPlaneState.Empty;
        var results = new OrchestratorReplayStepResult[steps.Count];
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index]
                ?? throw new ArgumentException(
                    "Replay cannot contain a null step.",
                    nameof(steps));
            var transition = UnifiedOrchestratorShadow.Apply(state, step);
            state = transition.State;
            results[index] = new OrchestratorReplayStepResult(
                step.Envelope.Id,
                transition.Result,
                state);
        }
        return new OrchestratorReplayResult(
            state,
            Array.AsReadOnly(results));
    }
}
