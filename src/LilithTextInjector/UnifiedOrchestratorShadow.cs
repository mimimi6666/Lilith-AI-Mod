using System;

namespace LilithTextInjector;

internal static class UnifiedOrchestratorShadow
{
    internal static OrchestratorShadowTransition Apply(
        OrchestratorControlPlaneState state,
        OrchestratorShadowStep step)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (step == null)
            throw new ArgumentNullException(nameof(step));

        var envelope = step.Envelope;
        if (!string.Equals(
                envelope.ProtocolVersion,
                OrchestratorProtocol.CurrentVersion,
                StringComparison.Ordinal))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnsupportedProtocol);
        }
        if (state.ContainsEventId(envelope.Id))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.DuplicateEventIdentity);
        }

        if (envelope is OrchestratorSessionOpenedEnvelope opened)
            return OpenSession(state, step, opened);

        var session = state.FindSession(envelope.SessionId);
        if (session == null)
        {
            return Reject(
                state,
                envelope,
                state.FindRetiredSession(envelope.SessionId) == null
                    ? OrchestratorProcessReason.SessionMissing
                    : OrchestratorProcessReason.SessionAlreadyRetired);
        }
        if (!session.Owner.Matches(envelope.Owner))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OwnerMismatch);
        }
        if (envelope.Sequence != session.LastEnvelopeSequence + 1)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SequenceMismatch);
        }
        if (envelope.OccurredAt < session.UpdatedAt)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.TimestampReversal);
        }
        if (envelope is OrchestratorSessionRetirementEnvelope retirement)
        {
            if (session.ProcessedEventIds.Count
                >= OrchestratorSessionState.MaximumEvents)
            {
                return Reject(
                    state,
                    envelope,
                    OrchestratorProcessReason.EventLimitReached);
            }
            return RetireSession(state, session, step, retirement);
        }
        if (session.Status != OrchestratorSessionStatus.Active)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionNotActive);
        }

        var isTerminalLifecycle = envelope
            is OrchestratorCancellationEnvelope
            or OrchestratorSessionCloseEnvelope;
        var eventLimit = isTerminalLifecycle
            ? OrchestratorSessionState.MaximumEvents - 1
            : OrchestratorSessionState.MaximumOperationalEvents;
        if (session.ProcessedEventIds.Count >= eventLimit)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.EventLimitReached);
        }

        return envelope switch
        {
            OrchestratorSituationEnvelope situation =>
                ApplySituation(state, session, step, situation),
            OrchestratorCancellationEnvelope cancellation =>
                CancelSession(state, session, step, cancellation),
            OrchestratorSessionCloseEnvelope close =>
                CloseSession(state, session, step, close),
            OrchestratorInvocationResultEnvelope invocationResult =>
                ApplyInvocationResult(
                    state,
                    session,
                    step,
                    invocationResult),
            OrchestratorOutcomeExpectationEnvelope expectation =>
                RegisterOutcomeExpectation(
                    state,
                    session,
                    step,
                    expectation),
            OrchestratorOutcomeSignalEnvelope outcomeSignal =>
                ApplyOutcomeSignal(
                    state,
                    session,
                    step,
                    outcomeSignal),
            _ => throw new ArgumentOutOfRangeException(
                nameof(step),
                "Unknown Orchestrator envelope type.")
        };
    }

    private static OrchestratorShadowTransition OpenSession(
        OrchestratorControlPlaneState state,
        OrchestratorShadowStep step,
        OrchestratorSessionOpenedEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }
        if (state.FindSession(envelope.SessionId) != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionAlreadyExists);
        }
        if (state.FindRetiredSession(envelope.SessionId) != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionAlreadyRetired);
        }
        if (state.Sessions.Count >= OrchestratorControlPlaneState.MaximumSessions)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionLimitReached);
        }

        var next = state.Add(OrchestratorSessionState.Open(envelope));
        return Accept(
            next,
            envelope,
            OrchestratorProcessReason.SessionOpened);
    }

    private static OrchestratorShadowTransition ApplySituation(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorSituationEnvelope envelope)
    {
        var stamp = step.EvaluationStamp;
        if (stamp == null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.MissingEvaluationStamp);
        }
        if (!string.Equals(
                envelope.Event.SessionId,
                session.Id,
                StringComparison.Ordinal)
            || envelope.Event.OccurredAt != envelope.OccurredAt
            || envelope.Event.Sequence
                != session.Situation.LastEventSequence + 1)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SituationEnvelopeMismatch);
        }
        if (session.ContainsSnapshotId(stamp.SnapshotId))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SnapshotIdentityDuplicate);
        }
        if (stamp.EvaluatedAt < envelope.OccurredAt)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.TimestampReversal);
        }

        try
        {
            var projection = SituationEventReducer.Apply(
                session.Situation,
                envelope.Event);
            var snapshot = SituationSnapshotAssembler.Assemble(
                projection,
                stamp.SnapshotId,
                session.LastSnapshotSequence + 1,
                stamp.SnapshotValidity,
                stamp.Budgets);
            var evaluation = LocalDecisionCore.Evaluate(
                snapshot,
                stamp.EvaluatedAt);
            var invocation = ResolveInvocation(
                session,
                envelope,
                evaluation,
                stamp.EvaluatedAt,
                out var newInvocation,
                out var invocationFailure);
            if (invocationFailure.HasValue)
            {
                return Reject(
                    state,
                    envelope,
                    invocationFailure.Value);
            }
            var acceptedSession = session.AcceptSituation(
                envelope,
                projection,
                stamp.SnapshotId,
                stamp.EvaluatedAt,
                evaluation,
                newInvocation);
            var next = state.Replace(acceptedSession);
            return Accept(
                next,
                envelope,
                OrchestratorProcessReason.SituationEvaluated,
                snapshot,
                evaluation,
                invocation);
        }
        catch (ArgumentException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SituationRejected);
        }
        catch (InvalidOperationException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SituationRejected);
        }
    }

    private static OrchestratorInvocationRecord? ResolveInvocation(
        OrchestratorSessionState session,
        OrchestratorSituationEnvelope envelope,
        DecisionEvaluation evaluation,
        DateTimeOffset registeredAt,
        out OrchestratorInvocationRecord? newInvocation,
        out OrchestratorProcessReason? failure)
    {
        newInvocation = null;
        failure = null;
        var plan = evaluation.Outcome.Invocation;
        if (plan == null)
            return null;

        var goalId = evaluation.Outcome.SelectedGoalId!;
        var existing = session.FindEquivalentPendingInvocation(
            goalId,
            plan.RequiredEvidence);
        if (existing != null)
            return existing;
        if (session.Invocations.Count
                >= OrchestratorSessionState.MaximumInvocations
            || session.PendingInvocationCount
                >= OrchestratorSessionState.MaximumPendingInvocations)
        {
            failure = OrchestratorProcessReason.InvocationLimitReached;
            return null;
        }

        newInvocation = OrchestratorInvocationRecord.Create(
            session.Id,
            envelope.CorrelationId,
            evaluation,
            registeredAt);
        return newInvocation;
    }

    private static OrchestratorShadowTransition ApplyInvocationResult(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorInvocationResultEnvelope envelope)
    {
        var invocation = session.FindInvocation(envelope.InvocationId);
        if (invocation == null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.InvocationMissing);
        }
        if (invocation.Status != OrchestratorInvocationStatus.Pending)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.InvocationNotPending);
        }
        if (!string.Equals(
                invocation.CorrelationId,
                envelope.CorrelationId,
                StringComparison.Ordinal)
            || !IsResultCompatible(invocation, envelope))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.InvocationResultMismatch);
        }
        if ((envelope.Status == OrchestratorInvocationStatus.TimedOut
                && envelope.OccurredAt < invocation.ExpiresAt)
            || (envelope.Status != OrchestratorInvocationStatus.TimedOut
                && envelope.OccurredAt > invocation.ExpiresAt))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.InvocationResultLate);
        }

        var completed = envelope.Status
            == OrchestratorInvocationStatus.Completed;
        if (completed && step.EvaluationStamp == null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.MissingEvaluationStamp);
        }
        if (!completed && step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }

        var updatedInvocation = invocation.Complete(envelope);
        if (!completed)
        {
            var terminalSession = session.AcceptInvocationResult(
                envelope,
                updatedInvocation,
                session.Situation,
                snapshotId: null,
                evaluatedAt: null,
                evaluation: null);
            return Accept(
                state.Replace(terminalSession),
                envelope,
                OrchestratorProcessReason.InvocationResultAccepted,
                invocation: updatedInvocation);
        }

        var stamp = step.EvaluationStamp!;
        if (session.ContainsSnapshotId(stamp.SnapshotId))
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SnapshotIdentityDuplicate);
        }
        if (stamp.EvaluatedAt < envelope.OccurredAt)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.TimestampReversal);
        }

        try
        {
            var evidenceEvent = new EvidenceChangedSituationEvent(
                envelope.Id,
                session.Situation.LastEventSequence + 1,
                session.Id,
                envelope.OccurredAt,
                SituationTriggerKind.InternalStateChanged,
                session.Situation.AvailableEvidence
                    | envelope.ProvidedEvidence);
            var projection = SituationEventReducer.Apply(
                session.Situation,
                evidenceEvent);
            var snapshot = SituationSnapshotAssembler.Assemble(
                projection,
                stamp.SnapshotId,
                session.LastSnapshotSequence + 1,
                stamp.SnapshotValidity,
                stamp.Budgets);
            var evaluation = LocalDecisionCore.Evaluate(
                snapshot,
                stamp.EvaluatedAt);
            var acceptedSession = session.AcceptInvocationResult(
                envelope,
                updatedInvocation,
                projection,
                stamp.SnapshotId,
                stamp.EvaluatedAt,
                evaluation);
            return Accept(
                state.Replace(acceptedSession),
                envelope,
                OrchestratorProcessReason.InvocationResultAccepted,
                snapshot,
                evaluation,
                updatedInvocation);
        }
        catch (ArgumentException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SituationRejected);
        }
        catch (InvalidOperationException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SituationRejected);
        }
    }

    private static bool IsResultCompatible(
        OrchestratorInvocationRecord invocation,
        OrchestratorInvocationResultEnvelope result)
    {
        var sourceEvidence = result.InvocationSource switch
        {
            OrchestratorInvocationSourceKind.MemoryService =>
                DecisionEvidence.Memory,
            OrchestratorInvocationSourceKind.ReasoningAdapter =>
                DecisionEvidence.Reasoning,
            _ => DecisionEvidence.None
        };
        if ((invocation.RemainingEvidence & sourceEvidence) == 0)
            return false;
        if (result.Status == OrchestratorInvocationStatus.Completed)
        {
            if (result.ProvidedEvidence != sourceEvidence)
                return false;
            if ((result.ProvidedEvidence & invocation.RemainingEvidence)
                != result.ProvidedEvidence)
            {
                return false;
            }
        }
        if (invocation.MemoryItemCount + result.MemoryItemCount
                > invocation.Plan.MaximumMemoryItems
            || invocation.EvidenceCharacterCount
                    + result.EvidenceCharacterCount
                > invocation.Plan.MaximumContextCharacters
            || invocation.ReasoningCallCount + result.ReasoningCallCount
                > invocation.Plan.MaximumReasoningCalls)
        {
            return false;
        }
        return true;
    }

    private static OrchestratorShadowTransition RegisterOutcomeExpectation(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorOutcomeExpectationEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }

        var decision = session.FindDecision(envelope.DecisionId);
        if (decision == null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeDecisionMissing);
        }
        if (!decision.SupportsOutcomeRegistration)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeRegistrationRejected);
        }
        if (envelope.OccurredAt < decision.EvaluatedAt)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.TimestampReversal);
        }
        if (session.Outcomes.Entries.Count
            >= OutcomeCorrelationLedger.MaximumExpectations)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeExpectationLimitReached);
        }

        try
        {
            var expectation = OutcomeExpectation.Create(
                decision.Evaluation,
                envelope.CorrelationId,
                envelope.ExpectationKind,
                envelope.OccurredAt,
                envelope.ObserveUntil);
            var outcomes = session.Outcomes.Register(expectation);
            var acceptedSession = session.AcceptOutcomeRegistration(
                envelope,
                outcomes);
            return Accept(
                state.Replace(acceptedSession),
                envelope,
                OrchestratorProcessReason.OutcomeExpectationRegistered,
                outcomeExpectation: expectation);
        }
        catch (ArgumentException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeRegistrationRejected);
        }
        catch (InvalidOperationException)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeRegistrationRejected);
        }
    }

    private static OrchestratorShadowTransition ApplyOutcomeSignal(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorOutcomeSignalEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }

        var update = session.Outcomes.Correlate(envelope.ToSignal());
        if (update.Result.Disposition
            == OutcomeCorrelationDisposition.Rejected)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.OutcomeSignalRejected,
                update.Result);
        }

        var acceptedSession = session.AcceptOutcomeSignal(
            envelope,
            update.Ledger);
        if (update.Result.Disposition
            == OutcomeCorrelationDisposition.AcceptedProgress)
        {
            return Accept(
                state.Replace(acceptedSession),
                envelope,
                OrchestratorProcessReason.OutcomeProgressAccepted,
                outcomeCorrelation: update.Result);
        }

        var appraisal = OutcomeAppraiser.Appraise(update.Result)
            ?? throw new InvalidOperationException(
                "Accepted terminal outcome must produce appraisal metadata.");
        return Accept(
            state.Replace(acceptedSession),
            envelope,
            OrchestratorProcessReason.OutcomeTerminalAccepted,
            outcomeCorrelation: update.Result,
            outcomeAppraisal: appraisal);
    }

    private static OrchestratorShadowTransition CancelSession(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorCancellationEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }

        var next = state.Replace(session.Cancel(envelope));
        return Accept(
            next,
            envelope,
            OrchestratorProcessReason.SessionCancelled);
    }

    private static OrchestratorShadowTransition CloseSession(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorSessionCloseEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }
        if (session.PendingInvocationCount > 0)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionClosePendingInvocation);
        }
        if (session.PendingOutcomeExpectationCount > 0)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionClosePendingOutcome);
        }

        return Accept(
            state.Replace(session.Close(envelope)),
            envelope,
            OrchestratorProcessReason.SessionClosed);
    }

    private static OrchestratorShadowTransition RetireSession(
        OrchestratorControlPlaneState state,
        OrchestratorSessionState session,
        OrchestratorShadowStep step,
        OrchestratorSessionRetirementEnvelope envelope)
    {
        if (step.EvaluationStamp != null)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.UnexpectedEvaluationStamp);
        }
        if (session.Status == OrchestratorSessionStatus.Active)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionRetirementRequiresTerminal);
        }
        if (!session.IsRetirable)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.SessionRetirementPendingWork);
        }
        if (state.RetiredSessions.Count
            >= OrchestratorControlPlaneState.MaximumRetiredSessions)
        {
            return Reject(
                state,
                envelope,
                OrchestratorProcessReason.RetiredSessionLimitReached);
        }

        return Accept(
            state.Retire(session, envelope),
            envelope,
            OrchestratorProcessReason.SessionRetired);
    }

    private static OrchestratorShadowTransition Accept(
        OrchestratorControlPlaneState state,
        OrchestratorEventEnvelope envelope,
        OrchestratorProcessReason reason,
        SituationSnapshot? snapshot = null,
        DecisionEvaluation? evaluation = null,
        OrchestratorInvocationRecord? invocation = null,
        OutcomeExpectation? outcomeExpectation = null,
        OutcomeCorrelationResult? outcomeCorrelation = null,
        OutcomeAppraisal? outcomeAppraisal = null)
        => new(
            state,
            new OrchestratorProcessResult(
                envelope.Id,
                OrchestratorProcessDisposition.Accepted,
                reason,
                snapshot,
                evaluation,
                invocation,
                outcomeExpectation,
                outcomeCorrelation,
                outcomeAppraisal));

    private static OrchestratorShadowTransition Reject(
        OrchestratorControlPlaneState state,
        OrchestratorEventEnvelope envelope,
        OrchestratorProcessReason reason,
        OutcomeCorrelationResult? outcomeCorrelation = null)
        => new(
            state,
            new OrchestratorProcessResult(
                envelope.Id,
                OrchestratorProcessDisposition.Rejected,
                reason,
                snapshot: null,
                evaluation: null,
                outcomeCorrelation: outcomeCorrelation));
}
