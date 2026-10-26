using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class ProactiveRespondRuntime
{
    internal const int MaximumSeededSuccessfulOutcomes = 32;

    private readonly ProactiveRespondPolicy _policy;
    private readonly ContinuityOwnershipRegistry _ownership;
    private readonly CharacterRuntimeIdentity _runtimeIdentity;
    private readonly List<DateTimeOffset> _successfulAt = new();
    private readonly Dictionary<Guid, ProactiveRespondAuthorization> _active =
        new();
    private readonly HashSet<Guid> _terminal = new();
    private DateTimeOffset? _lastEvaluatedAt;
    private int _authorizationCount;

    internal ProactiveRespondRuntime(
        ProactiveRespondPolicy policy,
        ContinuityOwnershipRegistry ownership,
        CharacterRuntimeIdentity runtimeIdentity,
        IEnumerable<DateTimeOffset>? successfulOutcomeHistory = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _ownership = ownership
            ?? throw new ArgumentNullException(nameof(ownership));
        _runtimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (successfulOutcomeHistory != null)
        {
            MergeSuccessfulOutcomeHistory(successfulOutcomeHistory);
        }
    }

    internal int ActiveCount => _active.Count;
    internal int AuthorizationCount => _authorizationCount;
    internal int SuccessfulCount => _successfulAt.Count;
    internal LivingContinuityAuthorityCounters AuthorityCounters =>
        LivingContinuityAuthorityCounters.Zero;

    internal void RefreshSuccessfulOutcomeHistory(
        IEnumerable<DateTimeOffset> successfulOutcomeHistory,
        DateTimeOffset now)
    {
        if (successfulOutcomeHistory == null)
        {
            throw new ArgumentNullException(
                nameof(successfulOutcomeHistory));
        }
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        MergeSuccessfulOutcomeHistory(
            successfulOutcomeHistory.Where(value => value <= now));
        RemoveOldSuccesses(now);
    }

    internal ProactiveRespondResult Process(
        ProactiveRespondOpportunity opportunity,
        ProactiveRespondSituation situation,
        DateTimeOffset now)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (situation == null)
            throw new ArgumentNullException(nameof(situation));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        if (_policy.Mode == ProactiveRespondMode.Off)
            return Result(ProactiveRespondAdmission.Disabled);
        if (_policy.RequiresExplicitArm
            && !_policy.ExplicitlyArmed)
        {
            return Result(ProactiveRespondAdmission.ArmRequired);
        }
        if (!situation.RuntimeIdentity.Matches(_runtimeIdentity)
            || !opportunity.RuntimeIdentity.Matches(_runtimeIdentity))
        {
            return Result(ProactiveRespondAdmission.RuntimeMismatch);
        }
        if (opportunity.SituationId != situation.SituationId
            || opportunity.SituationRevision != situation.SituationRevision)
        {
            return Result(ProactiveRespondAdmission.SituationMismatch);
        }
        if (!string.Equals(
                opportunity.EpochId,
                situation.EpochId,
                StringComparison.Ordinal))
        {
            return Result(ProactiveRespondAdmission.EpochMismatch);
        }
        if (now >= opportunity.ExpiresAt)
            return Result(ProactiveRespondAdmission.Expired);

        var existing = _ownership.Find(
            ContinuityOwnershipDomain.ProactiveOccurrence,
            opportunity.OccurrenceToken);
        if (existing != null)
        {
            if (existing.Request.RequestedOwner
                == ContinuityOwner.LivingContinuityOwned)
            {
                return Result(
                    _terminal.Contains(opportunity.OccurrenceId)
                        ? ProactiveRespondAdmission.DuplicateTerminal
                        : ProactiveRespondAdmission.BrainOwnedNoFallback,
                    ownership: existing,
                    suppressLegacy: true);
            }
            return Result(
                existing.IsTerminal
                    ? ProactiveRespondAdmission.LegacyAlreadyCompleted
                    : ProactiveRespondAdmission.OwnershipConflict,
                ownership: existing,
                suppressLegacy: existing.IsTerminal);
        }

        var candidate = opportunity.ToBaselineCandidate();
        var control = new InitiativeControlState(
            situation.MasterEnabled
                ? InitiativeMasterState.On
                : InitiativeMasterState.Off,
            _policy.Mode == ProactiveRespondMode.Observe
                ? InitiativePromotionMode.Observe
                : InitiativePromotionMode.Assist,
            InitiativeSensitivity.LocalGameMetadata,
            situation.IsQuietPeriod,
            IsCooldownActive(now),
            InitiativeAuthorityBudget.I1Zero,
            situation.SourceEnabled
                ? new[] { InitiativeProducerSourceIds.AiNoteScheduler }
                : Array.Empty<string>());

        if (_policy.Mode == ProactiveRespondMode.Observe)
        {
            var observation = Evaluate(
                candidate,
                opportunity,
                situation,
                control,
                now);
            _lastEvaluatedAt = now;
            return Result(
                ProactiveRespondAdmission.ObserveOnly,
                candidate,
                observation);
        }

        var ownershipRequest = OwnershipRequest(opportunity, now);
        var acquired = _ownership.Acquire(
            ownershipRequest,
            _runtimeIdentity,
            now);
        if (acquired.Disposition
            != ContinuityOwnershipDisposition.Acquired)
        {
            return Result(
                ProactiveRespondAdmission.OwnershipConflict,
                candidate,
                ownership: acquired,
                suppressLegacy: acquired.Request.RequestedOwner
                    == ContinuityOwner.LivingContinuityOwned);
        }

        var consumed = _ownership.Consume(
            ownershipRequest,
            _runtimeIdentity,
            opportunity.SituationId,
            opportunity.SituationRevision,
            now);
        if (!consumed.WasConsumed)
        {
            _ownership.Retire(
                ownershipRequest,
                ContinuityRetirementReason.Faulted,
                now);
            _terminal.Add(opportunity.OccurrenceId);
            return Result(
                ProactiveRespondAdmission.BrainOwnedNoFallback,
                candidate,
                ownership: consumed,
                suppressLegacy: true);
        }

        _terminal.Add(opportunity.OccurrenceId);

        if (!situation.MasterEnabled)
        {
            return Result(
                ProactiveRespondAdmission.MasterDisabled,
                candidate,
                ownership: consumed,
                suppressLegacy: true);
        }
        if (!situation.SourceEnabled)
        {
            return Result(
                ProactiveRespondAdmission.SourceDisabled,
                candidate,
                ownership: consumed,
                suppressLegacy: true);
        }
        if (_lastEvaluatedAt.HasValue
            && now - _lastEvaluatedAt.Value
                < ProactiveRespondPolicy.MinimumEvaluationInterval)
        {
            return Result(
                ProactiveRespondAdmission.EvaluationRateLimited,
                candidate,
                ownership: consumed,
                suppressLegacy: true);
        }

        _lastEvaluatedAt = now;
        var evaluation = Evaluate(
            candidate,
            opportunity,
            situation,
            control,
            now);
        if (evaluation.Outcome.Disposition != DecisionDisposition.Respond)
        {
            return Result(
                ProactiveRespondAdmission.DecisionDidNotRespond,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }
        if (_active.Count >= ProactiveRespondPolicy.MaximumActive)
        {
            return Result(
                ProactiveRespondAdmission.ActiveCapacityReached,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }
        if (_authorizationCount
            >= ProactiveRespondPolicy.MaximumAuthorizationsPerProcess)
        {
            return Result(
                ProactiveRespondAdmission.ProcessCapacityReached,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }
        RemoveOldSuccesses(now);
        if (_successfulAt.Count
            >= ProactiveRespondPolicy.MaximumSuccessfulPerRollingWindow)
        {
            return Result(
                ProactiveRespondAdmission.RollingCapacityReached,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }
        if (IsCooldownActive(now))
        {
            return Result(
                ProactiveRespondAdmission.CooldownActive,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }

        var expiry = now.AddSeconds(1);
        if (expiry > opportunity.ExpiresAt)
            expiry = opportunity.ExpiresAt;
        if (expiry <= now)
        {
            return Result(
                ProactiveRespondAdmission.Expired,
                candidate,
                evaluation,
                ownership: consumed,
                suppressLegacy: true);
        }
        var authorization = new ProactiveRespondAuthorization(
            "d025-auth-" + opportunity.OccurrenceToken,
            opportunity,
            candidate,
            evaluation,
            now,
            expiry);
        _active.Add(opportunity.OccurrenceId, authorization);
        _authorizationCount++;
        return Result(
            ProactiveRespondAdmission.AuthorizationPrepared,
            candidate,
            evaluation,
            authorization,
            consumed,
            suppressLegacy: true);
    }

    internal ProactiveRespondResult RecordOutcome(
        ProactiveRespondAuthorization authorization,
        OutcomeStatus status,
        CharacterRuntimeIdentity runtimeIdentity,
        Guid situationId,
        long situationRevision,
        DateTimeOffset now,
        string? epochId = null)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (!Enum.IsDefined(typeof(OutcomeStatus), status)
            || status is OutcomeStatus.Unknown
                or OutcomeStatus.Started
                or OutcomeStatus.ConsentPending)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));

        if (!_active.TryGetValue(
                authorization.OccurrenceId,
                out var active)
            || !string.Equals(
                active.CorrelationFingerprint(),
                authorization.CorrelationFingerprint(),
                StringComparison.Ordinal)
            || !runtimeIdentity.Matches(_runtimeIdentity)
            || !authorization.RuntimeIdentity.Matches(_runtimeIdentity)
            || !string.Equals(
                authorization.EpochId,
                epochId ?? authorization.EpochId,
                StringComparison.Ordinal)
            || authorization.SnapshotId != situationId
            || authorization.SituationRevision != situationRevision)
        {
            return Result(
                ProactiveRespondAdmission.OutcomeRejected,
                suppressLegacy: true);
        }
        if (now > authorization.ExpiresAt.AddMinutes(30))
        {
            _active.Remove(authorization.OccurrenceId);
            return Result(
                ProactiveRespondAdmission.OutcomeRejected,
                suppressLegacy: true);
        }

        _active.Remove(authorization.OccurrenceId);
        if (status == OutcomeStatus.Completed)
        {
            _successfulAt.Add(now);
            RemoveOldSuccesses(now);
        }
        return Result(
            ProactiveRespondAdmission.OutcomeRecorded,
            suppressLegacy: true);
    }

    private static DecisionEvaluation Evaluate(
        InitiativeCandidate candidate,
        ProactiveRespondOpportunity opportunity,
        ProactiveRespondSituation situation,
        InitiativeControlState control,
        DateTimeOffset now)
    {
        var validity = opportunity.ExpiresAt - now;
        if (validity > SituationSnapshotAssembler.MaximumValidity)
            validity = SituationSnapshotAssembler.MaximumValidity;
        return InitiativeCandidateProjector.Evaluate(
            opportunity.SessionId,
            control,
            new[] { candidate },
            new InitiativeEvaluationRequest(
                opportunity.SituationId,
                opportunity.SituationRevision,
                "d025-note-trigger-" + opportunity.OccurrenceToken,
                SituationTriggerKind.Timer,
                now,
                now,
                validity,
                situation.IsAvatarUnderDirectPlayerControl,
                situation.IsConversationBusy,
                situation.HasPendingExecution,
                DecisionEvidence.None,
                DecisionBudgets.ShadowDefault,
                candidate.Id));
    }

    private static ContinuityOwnershipRequest OwnershipRequest(
        ProactiveRespondOpportunity opportunity,
        DateTimeOffset now)
    {
        var expiry = now.AddMilliseconds(500);
        if (expiry > opportunity.ExpiresAt)
            expiry = opportunity.ExpiresAt;
        return new ContinuityOwnershipRequest(
            "d025-proactive-" + opportunity.OccurrenceToken,
            ContinuityOwnershipDomain.ProactiveOccurrence,
            ContinuityOwner.LivingContinuityOwned,
            opportunity.OccurrenceToken,
            opportunity.SessionId,
            opportunity.SituationId,
            opportunity.SituationRevision,
            opportunity.RuntimeIdentity,
            now,
            expiry);
    }

    private bool IsCooldownActive(DateTimeOffset now)
    {
        RemoveOldSuccesses(now);
        return _successfulAt.Count > 0
            && now - _successfulAt[^1]
                < ProactiveRespondPolicy.SuccessfulCooldown;
    }

    private void RemoveOldSuccesses(DateTimeOffset now)
        => _successfulAt.RemoveAll(
            item => now - item >= ProactiveRespondPolicy.RollingWindow);

    private void MergeSuccessfulOutcomeHistory(
        IEnumerable<DateTimeOffset> successfulOutcomeHistory)
    {
        var normalized =
            LivingProactiveNoteDeliveryPolicy.CoalesceSuccessfulHistory(
                _successfulAt.Concat(successfulOutcomeHistory),
                MaximumSeededSuccessfulOutcomes);
        _successfulAt.Clear();
        _successfulAt.AddRange(normalized);
    }

    private static ProactiveRespondResult Result(
        ProactiveRespondAdmission admission,
        InitiativeCandidate? candidate = null,
        DecisionEvaluation? evaluation = null,
        ProactiveRespondAuthorization? authorization = null,
        ContinuityOwnershipRecord? ownership = null,
        bool suppressLegacy = false)
        => new(
            admission,
            candidate,
            evaluation,
            authorization,
            ownership,
            suppressLegacy);
}
