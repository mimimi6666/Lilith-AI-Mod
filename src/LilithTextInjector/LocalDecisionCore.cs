using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal static class LocalDecisionCore
{
    internal const string Version = "local-decision-core.shadow.v3";

    internal static DecisionEvaluation Evaluate(
        SituationSnapshot snapshot,
        DateTimeOffset evaluatedAt)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        var normalizedEvaluatedAt = DecisionCoreContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (normalizedEvaluatedAt < snapshot.AsOf)
            throw new ArgumentOutOfRangeException(
                nameof(evaluatedAt),
                "Evaluation cannot occur before the snapshot.");

        var eligibleGoals = EligibleGoals(snapshot, normalizedEvaluatedAt);
        var consideredGoalIds = new string[eligibleGoals.Count];
        for (var index = 0; index < eligibleGoals.Count; index++)
            consideredGoalIds[index] = eligibleGoals[index].Id;

        DecisionOutcome outcome;
        if (normalizedEvaluatedAt >= snapshot.ValidUntil)
        {
            outcome = Outcome(
                DecisionDisposition.Defer,
                DecisionReason.SnapshotExpired);
        }
        else if (snapshot.ConflictLevel == SituationConflictLevel.Critical)
        {
            outcome = Outcome(
                DecisionDisposition.Defer,
                DecisionReason.CriticalConflict);
        }
        else if (snapshot.IsAvatarUnderDirectPlayerControl)
        {
            outcome = Outcome(
                DecisionDisposition.Observe,
                DecisionReason.DirectPlayerControl);
        }
        else if (snapshot.HasPendingExecution)
        {
            outcome = Outcome(
                DecisionDisposition.Observe,
                DecisionReason.PendingExecution);
        }
        else if (snapshot.IsConversationBusy)
        {
            outcome = Outcome(
                DecisionDisposition.Defer,
                DecisionReason.ConversationBusy);
        }
        else if (eligibleGoals.Count == 0)
        {
            outcome = Outcome(
                DecisionDisposition.Silence,
                DecisionReason.NoEligibleGoal);
        }
        else
        {
            var selectedGoal = SelectGoal(snapshot, eligibleGoals);
            outcome = EvaluateSelectedGoal(snapshot, selectedGoal);
        }

        var decisionId = CreateDeterministicDecisionId(
            snapshot.Id,
            snapshot.Sequence,
            outcome);
        var trace = new DecisionTrace(
            decisionId,
            snapshot,
            outcome,
            consideredGoalIds,
            Version);
        return new DecisionEvaluation(outcome, trace);
    }

    private static List<GoalState> EligibleGoals(
        SituationSnapshot snapshot,
        DateTimeOffset evaluatedAt)
    {
        var eligible = new List<GoalState>(snapshot.Goals.Count);
        foreach (var goal in snapshot.Goals)
        {
            if (goal.Status is not GoalStatus.Active and not GoalStatus.Candidate)
                continue;
            if (goal.ExpiresAt.HasValue && evaluatedAt >= goal.ExpiresAt.Value)
                continue;
            eligible.Add(goal);
        }
        eligible.Sort(CompareGoals);
        return eligible;
    }

    private static GoalState SelectGoal(
        SituationSnapshot snapshot,
        IReadOnlyList<GoalState> eligibleGoals)
    {
        var highestPriority = eligibleGoals[0];
        var focusedGoalId = snapshot.Attention.FocusedGoalId;
        if (focusedGoalId == null)
            return highestPriority;

        GoalState? focused = null;
        foreach (var goal in eligibleGoals)
        {
            if (!string.Equals(goal.Id, focusedGoalId, StringComparison.Ordinal))
                continue;
            focused = goal;
            break;
        }
        if (focused == null)
            return highestPriority;

        return highestPriority.PriorityClass < focused.PriorityClass
            ? highestPriority
            : focused;
    }

    private static DecisionOutcome EvaluateSelectedGoal(
        SituationSnapshot snapshot,
        GoalState goal)
    {
        if (goal.PriorityClass == GoalPriorityClass.Initiative)
        {
            if (snapshot.IsQuietPeriod)
            {
                return Outcome(
                    DecisionDisposition.Defer,
                    DecisionReason.QuietPeriod,
                    goal.Id);
            }
            if (snapshot.IsInitiativeCooldownActive)
            {
                return Outcome(
                    DecisionDisposition.Defer,
                    DecisionReason.InitiativeCooldown,
                    goal.Id);
            }
            if (goal.RequiresAttentionBudget)
            {
                if (!snapshot.Budgets.MaximumAttentionCost.HasValue)
                {
                    return Outcome(
                        DecisionDisposition.Defer,
                        DecisionReason.AttentionBudgetUnavailable,
                        goal.Id);
                }
                if (goal.AttentionCost
                    > snapshot.Budgets.MaximumAttentionCost.Value)
                {
                    return Outcome(
                        DecisionDisposition.Defer,
                        DecisionReason.AttentionBudgetExhausted,
                        goal.Id);
                }
            }
        }

        var missingEvidence = goal.RequiredEvidence & ~snapshot.AvailableEvidence;
        if (missingEvidence != DecisionEvidence.None)
        {
            var terminal = snapshot.FindEvidenceTerminalForGoal(goal);
            if (terminal != null)
            {
                return Outcome(
                    DecisionDisposition.Defer,
                    terminal.Reason == EvidenceTerminalReason.EvidenceUnavailable
                        ? DecisionReason.EvidenceUnavailable
                        : DecisionReason.EvidenceInsufficient,
                    goal.Id);
            }
            var invocation = CreateInvocationPlan(missingEvidence, snapshot.Budgets);
            return Outcome(
                DecisionDisposition.Defer,
                invocation == null
                    ? DecisionReason.EvidenceUnavailable
                    : DecisionReason.EvidenceInvocationPlanned,
                goal.Id,
                invocation);
        }

        if (goal.RequiresConsent
            || goal.DesiredDisposition == DecisionDisposition.AskConsent)
        {
            return Outcome(
                DecisionDisposition.AskConsent,
                DecisionReason.ConsentRequired,
                goal.Id);
        }

        return Outcome(
            goal.DesiredDisposition,
            DecisionReason.GoalSelected,
            goal.Id);
    }

    private static InvocationPlan? CreateInvocationPlan(
        DecisionEvidence missingEvidence,
        DecisionBudgets budgets)
    {
        var invokableEvidence = DecisionEvidence.None;
        var memoryItems = 0;
        var contextCharacters = 0;
        var reasoningCalls = 0;

        if ((missingEvidence & DecisionEvidence.Memory) != 0
            && budgets.MaximumMemoryItems > 0
            && budgets.MaximumContextCharacters > 0)
        {
            invokableEvidence |= DecisionEvidence.Memory;
            memoryItems = budgets.MaximumMemoryItems;
            contextCharacters = budgets.MaximumContextCharacters;
        }
        if ((missingEvidence & DecisionEvidence.Reasoning) != 0
            && budgets.MaximumReasoningCalls > 0
            && budgets.MaximumContextCharacters > 0)
        {
            invokableEvidence |= DecisionEvidence.Reasoning;
            reasoningCalls = budgets.MaximumReasoningCalls;
            contextCharacters = budgets.MaximumContextCharacters;
        }

        return invokableEvidence == DecisionEvidence.None
            ? null
            : new InvocationPlan(
                invokableEvidence,
                memoryItems,
                contextCharacters,
                reasoningCalls);
    }

    private static DecisionOutcome Outcome(
        DecisionDisposition disposition,
        DecisionReason reason,
        string? selectedGoalId = null,
        InvocationPlan? invocation = null)
        => new(disposition, reason, selectedGoalId, invocation);

    private static int CompareGoals(GoalState? left, GoalState? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var comparison = left.PriorityClass.CompareTo(right.PriorityClass);
        if (comparison != 0)
            return comparison;
        comparison = right.Urgency.CompareTo(left.Urgency);
        if (comparison != 0)
            return comparison;
        comparison = CompareOptionalTimestamp(left.Deadline, right.Deadline);
        if (comparison != 0)
            return comparison;
        comparison = left.InterruptionCost.CompareTo(right.InterruptionCost);
        if (comparison != 0)
            return comparison;
        comparison = left.CreatedAt.CompareTo(right.CreatedAt);
        if (comparison != 0)
            return comparison;
        return string.CompareOrdinal(left.Id, right.Id);
    }

    private static int CompareOptionalTimestamp(
        DateTimeOffset? left,
        DateTimeOffset? right)
    {
        if (left.HasValue && right.HasValue)
            return left.Value.CompareTo(right.Value);
        if (left.HasValue)
            return -1;
        return right.HasValue ? 1 : 0;
    }

    private static Guid CreateDeterministicDecisionId(
        Guid snapshotId,
        long snapshotSequence,
        DecisionOutcome outcome)
    {
        var bytes = snapshotId.ToByteArray();
        var sequenceBytes = BitConverter.GetBytes(snapshotSequence);
        for (var index = 0; index < sequenceBytes.Length; index++)
            bytes[index] ^= sequenceBytes[index];
        bytes[8] ^= (byte)outcome.Disposition;
        bytes[9] ^= (byte)outcome.Reason;
        return new Guid(bytes);
    }
}

internal static class OutcomeAppraiser
{
    internal static OutcomeAppraisal? Appraise(
        OutcomeCorrelationResult correlation)
    {
        if (correlation == null)
            throw new ArgumentNullException(nameof(correlation));
        if (correlation.Disposition
            != OutcomeCorrelationDisposition.AcceptedTerminal)
        {
            return null;
        }
        return CreateAppraisal(
            correlation.Observation
                ?? throw new InvalidOperationException(
                    "Accepted terminal correlation has no observation."),
            correlation.CausalConfidence);
    }

    internal static OutcomeAppraisal Appraise(OutcomeObservation observation)
    {
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));

        var confidence = CausalConfidence.None;
        if (observation.HasDirectExecutionCorrelation)
        {
            confidence = observation.HasConflictingIntervention
                ? CausalConfidence.Low
                : CausalConfidence.High;
        }

        return CreateAppraisal(observation, confidence);
    }

    private static OutcomeAppraisal CreateAppraisal(
        OutcomeObservation observation,
        CausalConfidence confidence)
    {
        AdaptationProposal? proposal = null;
        if (confidence >= CausalConfidence.Medium)
        {
            var kind = observation.Status switch
            {
                OutcomeStatus.Failed or OutcomeStatus.TimedOut =>
                    AdaptationProposalKind.ReviewCapabilityChoice,
                OutcomeStatus.Interrupted =>
                    AdaptationProposalKind.ReviewInterventionTiming,
                _ => AdaptationProposalKind.None
            };
            if (kind != AdaptationProposalKind.None)
                proposal = new AdaptationProposal(kind, confidence);
        }

        return new OutcomeAppraisal(
            observation.DecisionId,
            observation.Status,
            confidence,
            proposal);
    }
}
