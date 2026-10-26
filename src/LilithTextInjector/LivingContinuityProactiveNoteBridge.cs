using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum LivingProactiveNoteBridgeMode
{
    Off = 0,
    Prepared = 1
}

internal enum LivingProactiveNoteBridgeAdmission
{
    Disabled = 0,
    SourceRejected = 1,
    RuntimeMismatch = 2,
    SituationMismatch = 3,
    Expired = 4,
    Prepared = 5,
    Duplicate = 6,
    EnvelopeRejected = 7,
    DispatchAccepted = 8,
    Faulted = 9,
    Cancelled = 10,
    CompletionAccepted = 11,
    CompletionRejected = 12
}

internal enum LivingProactiveNoteForbiddenEffect
{
    ConversationMemoryWrite = 0,
    AiNoteEventConsideration = 1,
    Speech = 2,
    ReplyBubble = 3,
    AiNoteReschedule = 4,
    CandidateCreation = 5,
    AutomaticLearning = 6
}

internal sealed class LivingProactiveNoteBridgePolicy
{
    internal static LivingProactiveNoteBridgePolicy Off { get; } =
        new(LivingProactiveNoteBridgeMode.Off);

    internal LivingProactiveNoteBridgeMode Mode { get; }
    internal bool Enabled => Mode == LivingProactiveNoteBridgeMode.Prepared;

    internal LivingProactiveNoteBridgePolicy(
        LivingProactiveNoteBridgeMode mode)
    {
        if (!Enum.IsDefined(typeof(LivingProactiveNoteBridgeMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
    }
}

internal sealed class LivingProactiveNoteSession
{
    internal static readonly TimeSpan MaximumProviderCompletionLifetime =
        TimeSpan.FromSeconds(30);

    internal string SessionToken { get; }
    internal ProactiveRespondAuthorization Authorization { get; }
    internal LivingPromptSourceAuthorization PromptAuthorization { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string EpochId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal DateTimeOffset PreparedAt { get; }
    internal DateTimeOffset DispatchExpiresAt { get; }
    internal DateTimeOffset CompletionExpiresAt { get; }

    internal LivingProactiveNoteSession(
        ProactiveRespondAuthorization authorization,
        LivingPromptSourceAuthorization promptAuthorization,
        ProactiveRespondOpportunity opportunity,
        DateTimeOffset preparedAt)
    {
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        PromptAuthorization = promptAuthorization
            ?? throw new ArgumentNullException(nameof(promptAuthorization));
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (promptAuthorization.SourceKind
            != LivingPromptSourceKind.BrainAuthorizedProactive)
        {
            throw new ArgumentException(
                "Proactive note session requires a Brain-authorized source.",
                nameof(promptAuthorization));
        }
        var expected = LivingPromptSourceAuthorization.FromProactive(
            authorization);
        if (!string.Equals(
                expected.Fingerprint(),
                promptAuthorization.Fingerprint(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Prompt source must preserve the exact Slice E authorization.",
                nameof(promptAuthorization));
        }

        PreparedAt = LivingContinuityContractGuard.Timestamp(
            preparedAt,
            nameof(preparedAt));
        if (PreparedAt < authorization.IssuedAt
            || PreparedAt >= authorization.ExpiresAt)
        {
            throw new ArgumentOutOfRangeException(nameof(preparedAt));
        }
        if (opportunity.OccurrenceId != authorization.OccurrenceId
            || !opportunity.RuntimeIdentity.Matches(
                authorization.RuntimeIdentity)
            || opportunity.SituationId != authorization.SnapshotId
            || opportunity.SituationRevision
                != authorization.SituationRevision
            || !string.Equals(
                opportunity.EpochId,
                authorization.EpochId,
                StringComparison.Ordinal)
            || opportunity.Purpose != authorization.Purpose
            || !string.Equals(
                opportunity.SourceContentHash,
                authorization.SourceContentHash,
                StringComparison.Ordinal)
            || !string.Equals(
                opportunity.SessionId,
                authorization.RuntimeIdentity.OrchestratorSessionId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Opportunity must preserve the exact authorization lineage.",
                nameof(opportunity));
        }

        RuntimeIdentity = CopyRuntime(authorization.RuntimeIdentity);
        EpochId = authorization.EpochId;
        SituationId = authorization.SnapshotId;
        SituationRevision = authorization.SituationRevision;
        DispatchExpiresAt = authorization.ExpiresAt;
        var boundedProviderDeadline =
            PreparedAt.Add(MaximumProviderCompletionLifetime);
        CompletionExpiresAt = opportunity.ExpiresAt
            < boundedProviderDeadline
            ? opportunity.ExpiresAt
            : boundedProviderDeadline;
        if (CompletionExpiresAt <= PreparedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opportunity),
                "Source policy expires before provider completion can begin.");
        }
        SessionToken = "d025-proactive-note-"
            + LivingPromptHash.Of(
                authorization.CorrelationFingerprint()
                + "|"
                + promptAuthorization.Fingerprint()
                + "|"
                + EpochId
                + "|"
                + opportunity.ExpiresAt.ToUnixTimeMilliseconds()
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture))[..32];
    }

    private static CharacterRuntimeIdentity CopyRuntime(
        CharacterRuntimeIdentity source)
        => new(
            source.RuntimeProfileId,
            source.RuntimeInstanceId,
            source.Generation,
            source.OrchestratorSessionId,
            source.OwnerIdentity);
}

internal sealed class LivingProactiveNoteDeliveryProjection
{
    internal string SessionToken { get; }
    internal Guid OccurrenceId { get; }
    internal string CandidateId { get; }
    internal Guid DecisionId { get; }
    internal string CompletionId { get; }
    internal string ProviderRequestId { get; }
    internal string ProviderRouteId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string EpochId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal ProactiveRespondPurpose Purpose { get; }
    internal string ResponseText { get; }
    internal DateTimeOffset CompletedAt { get; }
    internal bool MayWriteNativeInbox => true;
    internal bool MayApply(LivingProactiveNoteForbiddenEffect effect)
    {
        if (!Enum.IsDefined(typeof(LivingProactiveNoteForbiddenEffect), effect))
            throw new ArgumentOutOfRangeException(nameof(effect));
        return false;
    }

    internal bool ForbiddenEffectsAreZero
    {
        get
        {
            foreach (LivingProactiveNoteForbiddenEffect effect
                in Enum.GetValues(
                    typeof(LivingProactiveNoteForbiddenEffect)))
            {
                if (MayApply(effect))
                    return false;
            }
            return true;
        }
    }

    internal LivingProactiveNoteDeliveryProjection(
        LivingProactiveNoteSession session,
        LivingProactiveNoteCompletionProjection projection)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (projection == null)
            throw new ArgumentNullException(nameof(projection));
        if (projection.Destination
            != LivingPreparedCompletionDestination.ProactiveNote
            || projection.CreatesCandidate
            || projection.Learns
            || projection.MayAppendConversationHistory
            || projection.MayScheduleAnotherNote
            || projection.MayEmitSpeech
            || projection.MayEmitVisualBubble)
        {
            throw new ArgumentException(
                "Proactive completion projection contains a forbidden effect.",
                nameof(projection));
        }
        if (!projection.RuntimeIdentity.Matches(session.RuntimeIdentity)
            || projection.SituationId != session.SituationId
            || projection.SituationRevision != session.SituationRevision
            || !string.Equals(
                projection.AuthorizationFingerprint,
                session.PromptAuthorization.Fingerprint(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Proactive completion does not match the exact session.",
                nameof(projection));
        }

        SessionToken = session.SessionToken;
        OccurrenceId = session.Authorization.OccurrenceId;
        CandidateId = session.Authorization.CandidateId;
        DecisionId = session.Authorization.DecisionId;
        CompletionId = projection.CompletionId;
        ProviderRequestId = projection.ProviderRequestId;
        ProviderRouteId = projection.ProviderRouteId;
        RuntimeIdentity = new CharacterRuntimeIdentity(
            projection.RuntimeIdentity.RuntimeProfileId,
            projection.RuntimeIdentity.RuntimeInstanceId,
            projection.RuntimeIdentity.Generation,
            projection.RuntimeIdentity.OrchestratorSessionId,
            projection.RuntimeIdentity.OwnerIdentity);
        EpochId = session.EpochId;
        SituationId = projection.SituationId;
        SituationRevision = projection.SituationRevision;
        Purpose = session.Authorization.Purpose;
        ResponseText = projection.ResponseText;
        CompletedAt = projection.CompletedAt;
    }
}

internal static class LivingProactiveNoteCompletionProjector
{
    internal static LivingProactiveNoteBridgeAdmission TryProject(
        LivingProactiveNoteSession session,
        LivingPreparedProviderExchange exchange,
        LivingPromptCompletionRecord completion,
        out LivingProactiveNoteDeliveryProjection? projection)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (exchange == null)
            throw new ArgumentNullException(nameof(exchange));
        if (completion == null)
            throw new ArgumentNullException(nameof(completion));
        projection = null;

        var admission = LivingPreparedCompletionProjector.TryProject(
            exchange,
            completion,
            out var prepared);
        if (admission
                != LivingPreparedProviderAdmission.CompletionProjected
            || prepared is not LivingProactiveNoteCompletionProjection note)
        {
            return LivingProactiveNoteBridgeAdmission.CompletionRejected;
        }

        try
        {
            projection = new LivingProactiveNoteDeliveryProjection(
                session,
                note);
            return LivingProactiveNoteBridgeAdmission.CompletionAccepted;
        }
        catch (ArgumentException)
        {
            return LivingProactiveNoteBridgeAdmission.CompletionRejected;
        }
    }
}

internal sealed class LivingProactiveNoteBridgeCounters
{
    internal int Prepared { get; private set; }
    internal int ProviderAttempts { get; private set; }
    internal int Completed { get; private set; }
    internal int Faulted { get; private set; }
    internal int Cancelled { get; private set; }
    internal int ProviderRetries => 0;
    internal int LegacyFallbacks => 0;
    internal int ConversationMemoryWrites => 0;
    internal int AiNoteEventConsiderations => 0;
    internal int SpeechRequests => 0;
    internal int ReplyBubbles => 0;
    internal int AiNoteReschedules => 0;
    internal int CandidatesCreated => 0;
    internal int AutomaticLearningWrites => 0;

    internal void RecordPrepared() => Prepared++;
    internal void RecordProviderAttempt() => ProviderAttempts++;
    internal void RecordCompleted() => Completed++;
    internal void RecordFaulted() => Faulted++;
    internal void RecordCancelled() => Cancelled++;

    internal bool ForbiddenOutputsAreZero =>
        ProviderRetries == 0
        && LegacyFallbacks == 0
        && ConversationMemoryWrites == 0
        && AiNoteEventConsiderations == 0
        && SpeechRequests == 0
        && ReplyBubbles == 0
        && AiNoteReschedules == 0
        && CandidatesCreated == 0
        && AutomaticLearningWrites == 0;
}

internal sealed class LivingProactiveNoteBridge
{
    private sealed class ActiveSession
    {
        internal LivingProactiveNoteSession Session { get; }
        internal bool DispatchAttempted { get; set; }
        internal bool Terminal { get; set; }
        internal LivingPreparedProviderAdapter? Provider { get; set; }
        internal LivingPromptEnvelope? Envelope { get; set; }
        internal LivingPromptProviderReceipt? Receipt { get; set; }

        internal ActiveSession(LivingProactiveNoteSession session)
        {
            Session = session;
        }
    }

    private readonly object _sync = new();
    private readonly LivingProactiveNoteBridgePolicy _policy;
    private readonly CharacterRuntimeIdentity _runtimeIdentity;
    private readonly HashSet<string> _terminalSessionTokens =
        new(StringComparer.Ordinal);
    private ActiveSession? _active;

    internal LivingProactiveNoteBridgeCounters Counters { get; } = new();
    internal bool HasActive
    {
        get
        {
            lock (_sync)
                return _active != null && !_active.Terminal;
        }
    }

    internal LivingProactiveNoteBridge(
        LivingProactiveNoteBridgePolicy policy,
        CharacterRuntimeIdentity runtimeIdentity)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _runtimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
    }

    internal LivingProactiveNoteBridgeAdmission TryPrepare(
        ProactiveRespondResult source,
        ProactiveRespondOpportunity opportunity,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset now,
        out LivingProactiveNoteSession? session)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (activeSituationId == Guid.Empty)
            throw new ArgumentException(
                "Situation identity cannot be empty.",
                nameof(activeSituationId));
        if (activeSituationRevision <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(activeSituationRevision));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        session = null;

        lock (_sync)
        {
            if (!_policy.Enabled)
                return LivingProactiveNoteBridgeAdmission.Disabled;
            if (_active != null && !_active.Terminal)
                return LivingProactiveNoteBridgeAdmission.Duplicate;
            if (!activeRuntime.Matches(_runtimeIdentity))
                return LivingProactiveNoteBridgeAdmission.RuntimeMismatch;
            if (!IsAcceptedSliceESource(source))
                return LivingProactiveNoteBridgeAdmission.SourceRejected;

            var authorization = source.Authorization!;
            if (!authorization.RuntimeIdentity.Matches(activeRuntime))
                return LivingProactiveNoteBridgeAdmission.RuntimeMismatch;
            if (authorization.SnapshotId != activeSituationId
                || authorization.SituationRevision
                    != activeSituationRevision)
            {
                return LivingProactiveNoteBridgeAdmission.SituationMismatch;
            }
            if (now < authorization.IssuedAt
                || now >= authorization.ExpiresAt)
            {
                return LivingProactiveNoteBridgeAdmission.Expired;
            }

            var promptAuthorization =
                LivingPromptSourceAuthorization.FromProactive(
                    authorization);
            session = new LivingProactiveNoteSession(
                authorization,
                promptAuthorization,
                opportunity,
                now);
            if (_terminalSessionTokens.Contains(session.SessionToken))
            {
                session = null;
                return LivingProactiveNoteBridgeAdmission.Duplicate;
            }
            _active = new ActiveSession(session);
            Counters.RecordPrepared();
            return LivingProactiveNoteBridgeAdmission.Prepared;
        }
    }

    internal LivingProactiveNoteBridgeAdmission TryDispatch(
        LivingProactiveNoteSession session,
        LivingPromptCompositionHost compositionHost,
        LivingPromptEnvelope envelope,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        string activeProviderRouteId,
        DateTimeOffset now,
        ILivingPreparedProviderTransport transport,
        out LivingPromptProviderReceipt? receipt)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (compositionHost == null)
            throw new ArgumentNullException(nameof(compositionHost));
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        if (transport == null)
            throw new ArgumentNullException(nameof(transport));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        receipt = null;

        lock (_sync)
        {
            if (!TryGetActive(session, out var active))
            {
                compositionHost.Cancel(
                    envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    now);
                return LivingProactiveNoteBridgeAdmission.Duplicate;
            }
            if (active.DispatchAttempted)
                return LivingProactiveNoteBridgeAdmission.Duplicate;
            active.DispatchAttempted = true;

            if (!MatchesEnvelope(
                    session,
                    envelope,
                    activeRuntime,
                    activeSituationId,
                    activeSituationRevision,
                    activeProviderRouteId,
                    now))
            {
                compositionHost.Cancel(
                    envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.EnvelopeRejected;
            }

            var provider = new LivingPreparedProviderAdapter(transport);
            active.Provider = provider;
            active.Envelope = envelope;
            LivingPromptCompositionAdmission admission;
            try
            {
                admission = compositionHost.TryDispatch(
                    envelope,
                    activeRuntime,
                    activeRuntime.OrchestratorSessionId,
                    activeSituationId,
                    activeSituationRevision,
                    activeProviderRouteId,
                    now,
                    provider,
                    out receipt);
            }
            catch
            {
                compositionHost.Cancel(
                    envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.Faulted;
            }
            if (provider.Counters.ProviderCalls > 0)
                Counters.RecordProviderAttempt();

            if (admission
                != LivingPromptCompositionAdmission.DispatchAccepted
                || receipt == null)
            {
                compositionHost.Cancel(
                    envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.Faulted;
            }

            active.Receipt = receipt;
            return LivingProactiveNoteBridgeAdmission.DispatchAccepted;
        }
    }

    internal LivingProactiveNoteBridgeAdmission TryComplete(
        LivingProactiveNoteSession session,
        LivingPromptCompositionHost compositionHost,
        LivingPreparedProviderResponse response,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        DateTimeOffset now,
        out LivingProactiveNoteDeliveryProjection? projection)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (compositionHost == null)
            throw new ArgumentNullException(nameof(compositionHost));
        if (response == null)
            throw new ArgumentNullException(nameof(response));
        if (activeRuntime == null)
            throw new ArgumentNullException(nameof(activeRuntime));
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        projection = null;

        lock (_sync)
        {
            if (!TryGetActive(session, out var active)
                || !active.DispatchAttempted
                || active.Provider == null
                || active.Envelope == null
                || active.Receipt == null)
            {
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }
            if (!activeRuntime.Matches(session.RuntimeIdentity)
                || activeSituationId != session.SituationId
                || activeSituationRevision != session.SituationRevision)
            {
                compositionHost.Cancel(
                    active.Envelope.CompositionId,
                    ContinuityRetirementReason.SituationRevisionChanged,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }
            if (now >= session.CompletionExpiresAt)
            {
                compositionHost.Cancel(
                    active.Envelope.CompositionId,
                    ContinuityRetirementReason.Expired,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }

            var responseAdmission = active.Provider.TryAcceptResponse(
                response,
                now,
                out var exchange);
            if (responseAdmission
                    != LivingPreparedProviderAdmission.ResponseAccepted
                || exchange == null)
            {
                compositionHost.Cancel(
                    active.Envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    now);
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }

            var completionAdmission = compositionHost.RecordCompletion(
                active.Envelope,
                active.Receipt,
                "d025-note-completion-"
                    + LivingPromptHash.Of(
                        exchange.Response.ProviderRequestId)[..32],
                OutcomeStatus.Completed,
                activeRuntime,
                activeRuntime.OrchestratorSessionId,
                activeSituationId,
                activeSituationRevision,
                now,
                out var completion);
            if (completionAdmission
                    != LivingPromptCompositionAdmission.CompletionRecorded
                || completion == null)
            {
                MarkTerminal(active);
                Counters.RecordFaulted();
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }

            var projectionAdmission =
                LivingProactiveNoteCompletionProjector.TryProject(
                    session,
                    exchange,
                    completion,
                    out projection);
            MarkTerminal(active);
            if (projectionAdmission
                != LivingProactiveNoteBridgeAdmission.CompletionAccepted)
            {
                Counters.RecordFaulted();
                projection = null;
                return LivingProactiveNoteBridgeAdmission.CompletionRejected;
            }

            Counters.RecordCompleted();
            return LivingProactiveNoteBridgeAdmission.CompletionAccepted;
        }
    }

    internal LivingProactiveNoteBridgeAdmission Cancel(
        LivingProactiveNoteSession session,
        LivingPromptCompositionHost compositionHost,
        ContinuityRetirementReason reason,
        DateTimeOffset now)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (compositionHost == null)
            throw new ArgumentNullException(nameof(compositionHost));
        if (!Enum.IsDefined(typeof(ContinuityRetirementReason), reason)
            || reason == ContinuityRetirementReason.Consumed)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        now = LivingContinuityContractGuard.Timestamp(now, nameof(now));
        lock (_sync)
        {
            if (!TryGetActive(session, out var active))
                return LivingProactiveNoteBridgeAdmission.Duplicate;
            if (active.Envelope != null)
            {
                compositionHost.Cancel(
                    active.Envelope.CompositionId,
                    reason,
                    now);
            }
            MarkTerminal(active);
            Counters.RecordCancelled();
            return LivingProactiveNoteBridgeAdmission.Cancelled;
        }
    }

    private void MarkTerminal(ActiveSession active)
    {
        active.Terminal = true;
        _terminalSessionTokens.Add(active.Session.SessionToken);
    }

    private bool TryGetActive(
        LivingProactiveNoteSession session,
        out ActiveSession active)
    {
        active = _active!;
        return active != null
            && !active.Terminal
            && string.Equals(
                active.Session.SessionToken,
                session.SessionToken,
                StringComparison.Ordinal);
    }

    private static bool IsAcceptedSliceESource(
        ProactiveRespondResult source)
    {
        var authorization = source.Authorization;
        var ownership = source.Ownership;
        return source.Admission
                == ProactiveRespondAdmission.AuthorizationPrepared
            && source.SuppressLegacy
            && !source.LegacyMayExecute
            && authorization != null
            && source.Candidate != null
            && source.Evaluation != null
            && ownership != null
            && ownership.Request.Domain
                == ContinuityOwnershipDomain.ProactiveOccurrence
            && ownership.Request.RequestedOwner
                == ContinuityOwner.LivingContinuityOwned
            && ownership.IsTerminal
            && ownership.WasConsumed
            && ownership.RetirementReason
                == ContinuityRetirementReason.Consumed
            && string.Equals(
                ownership.Request.OccurrenceId,
                authorization.OccurrenceId.ToString("N"),
                StringComparison.Ordinal)
            && string.Equals(
                source.Candidate.Id,
                authorization.CandidateId,
                StringComparison.Ordinal)
            && source.Evaluation.Trace.DecisionId
                == authorization.DecisionId
            && source.Evaluation.Trace.SnapshotId
                == authorization.SnapshotId
            && source.Evaluation.Trace.SnapshotSequence
                == authorization.SituationRevision;
    }

    private static bool MatchesEnvelope(
        LivingProactiveNoteSession session,
        LivingPromptEnvelope envelope,
        CharacterRuntimeIdentity activeRuntime,
        Guid activeSituationId,
        long activeSituationRevision,
        string activeProviderRouteId,
        DateTimeOffset now)
        => envelope.SourceKind
                == LivingPromptSourceKind.BrainAuthorizedProactive
            && string.Equals(
                envelope.AuthorizationFingerprint,
                session.PromptAuthorization.Fingerprint(),
                StringComparison.Ordinal)
            && envelope.RuntimeIdentity.Matches(session.RuntimeIdentity)
            && envelope.RuntimeIdentity.Matches(activeRuntime)
            && envelope.SituationId == session.SituationId
            && envelope.SituationId == activeSituationId
            && envelope.SituationRevision == session.SituationRevision
            && envelope.SituationRevision == activeSituationRevision
            && string.Equals(
                envelope.ProviderRouteId,
                activeProviderRouteId,
                StringComparison.Ordinal)
            && now >= envelope.CreatedAt
            && now < envelope.ExpiresAt
            && now < session.DispatchExpiresAt;
}
