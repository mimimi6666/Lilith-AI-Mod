using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithAgentWorkbench;

internal enum D045ConsentReplayFailure
{
    None = 0,
    InvalidReplayIdentity = 1,
    InvalidInitialMonotonicMicroseconds = 2,
    EventCapacityExceeded = 3,
    NullEvent = 4,
    SequenceDiscontinuity = 5,
    MonotonicDiscontinuity = 6,
    MonotonicRetrograde = 7
}

internal enum D045ConsentReplayEventKind
{
    ConsentRequested = 1,
    OneActionBindingConsumed = 2,
    ConsentCandidateRevoked = 3,
    TaskEnded = 4,
    SessionEnded = 5,
    ConsentGenerationChanged = 6,
    EmergencyStop = 7,
    MonotonicCheckpoint = 8
}

internal abstract class D045ConsentReplayEvent
{
    internal int SequenceNumber { get; }
    internal Guid EventId { get; }
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid ConsentGenerationId { get; }
    internal long PreviousMonotonicMicroseconds { get; }
    internal long MonotonicMicroseconds { get; }
    internal abstract D045ConsentReplayEventKind Kind { get; }
    internal abstract Guid AuditRequestId { get; }
    internal abstract Guid AuditCandidateId { get; }
    internal abstract D045AuthorityCapability? AuditCapability { get; }
    internal abstract D045Sha256Fingerprint? AuditTargetScopeFingerprint
    {
        get;
    }

    protected D045ConsentReplayEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
    {
        SequenceNumber = sequenceNumber;
        EventId = eventId;
        OwnerId = ownerId;
        ProfileId = profileId;
        ConsentGenerationId = consentGenerationId;
        PreviousMonotonicMicroseconds = previousMonotonicMicroseconds;
        MonotonicMicroseconds = monotonicMicroseconds;
    }
}

internal sealed class D045ConsentRequestedEvent : D045ConsentReplayEvent
{
    internal D045ConsentRequest? Request { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.ConsentRequested;
    internal override Guid AuditRequestId => Request?.RequestId ?? Guid.Empty;
    internal override Guid AuditCandidateId =>
        Request?.CandidateId ?? Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability =>
        Request?.Capability;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        Request?.TargetScopeFingerprint;

    internal D045ConsentRequestedEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds,
        D045ConsentRequest? request)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            consentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        Request = request;
    }
}

internal sealed class D045ConsentBindingEvidence
{
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid ConsentGenerationId { get; }
    internal Guid RequestId { get; }
    internal Guid CandidateId { get; }
    internal Guid TaskId { get; }
    internal Guid SessionId { get; }
    internal D045AuthorityCapability Capability { get; }
    internal D045Sha256Fingerprint? TargetScopeFingerprint { get; }
    internal D045Sha256Fingerprint? PurposeFingerprint { get; }
    internal D045Sha256Fingerprint? PreviewFingerprint { get; }

    internal bool IsComplete =>
        OwnerId != Guid.Empty
        && ProfileId != Guid.Empty
        && ConsentGenerationId != Guid.Empty
        && RequestId != Guid.Empty
        && CandidateId != Guid.Empty
        && TaskId != Guid.Empty
        && SessionId != Guid.Empty
        && D045AuthorityTaxonomy.IsDefined(Capability)
        && TargetScopeFingerprint is not null
        && PurposeFingerprint is not null
        && PreviewFingerprint is not null;

    internal D045ConsentBindingEvidence(
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid requestId,
        Guid candidateId,
        Guid taskId,
        Guid sessionId,
        D045AuthorityCapability capability,
        D045Sha256Fingerprint? targetScopeFingerprint,
        D045Sha256Fingerprint? purposeFingerprint,
        D045Sha256Fingerprint? previewFingerprint)
    {
        OwnerId = ownerId;
        ProfileId = profileId;
        ConsentGenerationId = consentGenerationId;
        RequestId = requestId;
        CandidateId = candidateId;
        TaskId = taskId;
        SessionId = sessionId;
        Capability = capability;
        TargetScopeFingerprint = targetScopeFingerprint;
        PurposeFingerprint = purposeFingerprint;
        PreviewFingerprint = previewFingerprint;
    }

    internal static D045ConsentBindingEvidence From(
        D045OneActionConsentProjection projection)
    {
        if (projection is null)
            throw new ArgumentNullException(nameof(projection));
        return new D045ConsentBindingEvidence(
            projection.OwnerId,
            projection.ProfileId,
            projection.ConsentGenerationId,
            projection.RequestId,
            projection.CandidateId,
            projection.TaskId,
            projection.SessionId,
            projection.Capability,
            projection.TargetScopeFingerprint,
            projection.PurposeFingerprint,
            projection.PreviewFingerprint);
    }
}

internal sealed class D045OneActionBindingConsumedEvent :
    D045ConsentReplayEvent
{
    internal D045ConsentBindingEvidence? Binding { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.OneActionBindingConsumed;
    internal override Guid AuditRequestId => Binding?.RequestId ?? Guid.Empty;
    internal override Guid AuditCandidateId =>
        Binding?.CandidateId ?? Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability =>
        Binding?.Capability;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        Binding?.TargetScopeFingerprint;

    internal D045OneActionBindingConsumedEvent(
        int sequenceNumber,
        Guid eventId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds,
        D045ConsentBindingEvidence? binding)
        : base(
            sequenceNumber,
            eventId,
            binding?.OwnerId ?? Guid.Empty,
            binding?.ProfileId ?? Guid.Empty,
            binding?.ConsentGenerationId ?? Guid.Empty,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        Binding = binding;
    }
}

internal sealed class D045ConsentCandidateRevokedEvent :
    D045ConsentReplayEvent
{
    internal D045ConsentBindingEvidence? Binding { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.ConsentCandidateRevoked;
    internal override Guid AuditRequestId => Binding?.RequestId ?? Guid.Empty;
    internal override Guid AuditCandidateId =>
        Binding?.CandidateId ?? Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability =>
        Binding?.Capability;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        Binding?.TargetScopeFingerprint;

    internal D045ConsentCandidateRevokedEvent(
        int sequenceNumber,
        Guid eventId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds,
        D045ConsentBindingEvidence? binding)
        : base(
            sequenceNumber,
            eventId,
            binding?.OwnerId ?? Guid.Empty,
            binding?.ProfileId ?? Guid.Empty,
            binding?.ConsentGenerationId ?? Guid.Empty,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        Binding = binding;
    }
}

internal sealed class D045TaskEndedEvent : D045ConsentReplayEvent
{
    internal Guid TaskId { get; }
    internal Guid SessionId { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.TaskEnded;
    internal override Guid AuditRequestId => Guid.Empty;
    internal override Guid AuditCandidateId => Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability => null;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        null;

    internal D045TaskEndedEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid taskId,
        Guid sessionId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            consentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        TaskId = taskId;
        SessionId = sessionId;
    }
}

internal sealed class D045SessionEndedEvent : D045ConsentReplayEvent
{
    internal Guid SessionId { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.SessionEnded;
    internal override Guid AuditRequestId => Guid.Empty;
    internal override Guid AuditCandidateId => Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability => null;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        null;

    internal D045SessionEndedEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid sessionId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            consentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        SessionId = sessionId;
    }
}

internal sealed class D045ConsentGenerationChangedEvent :
    D045ConsentReplayEvent
{
    internal Guid NextConsentGenerationId { get; }
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.ConsentGenerationChanged;
    internal override Guid AuditRequestId => Guid.Empty;
    internal override Guid AuditCandidateId => Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability => null;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        null;

    internal D045ConsentGenerationChangedEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid currentConsentGenerationId,
        Guid nextConsentGenerationId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            currentConsentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
        NextConsentGenerationId = nextConsentGenerationId;
    }
}

internal sealed class D045EmergencyStopEvent : D045ConsentReplayEvent
{
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.EmergencyStop;
    internal override Guid AuditRequestId => Guid.Empty;
    internal override Guid AuditCandidateId => Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability => null;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        null;

    internal D045EmergencyStopEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            consentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
    }
}

internal sealed class D045MonotonicCheckpointEvent : D045ConsentReplayEvent
{
    internal override D045ConsentReplayEventKind Kind =>
        D045ConsentReplayEventKind.MonotonicCheckpoint;
    internal override Guid AuditRequestId => Guid.Empty;
    internal override Guid AuditCandidateId => Guid.Empty;
    internal override D045AuthorityCapability? AuditCapability => null;
    internal override D045Sha256Fingerprint? AuditTargetScopeFingerprint =>
        null;

    internal D045MonotonicCheckpointEvent(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        long previousMonotonicMicroseconds,
        long monotonicMicroseconds)
        : base(
            sequenceNumber,
            eventId,
            ownerId,
            profileId,
            consentGenerationId,
            previousMonotonicMicroseconds,
            monotonicMicroseconds)
    {
    }
}

internal sealed class D045AttachmentEgressReservationRecord
{
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid ConsentGenerationId { get; }
    internal Guid RequestId { get; }
    internal Guid CandidateId { get; }
    internal Guid TaskId { get; }
    internal Guid SessionId { get; }
    internal int AcceptedReplaySequenceNumber { get; }
    internal long AcceptedReplayMonotonicMicroseconds { get; }
    internal D045Sha256Fingerprint ReservationIdentity { get; }
    internal D045Sha256Fingerprint CandidateIdentity { get; }

    internal D045AttachmentEgressReservationRecord(
        object transitionSeal,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid requestId,
        Guid candidateId,
        Guid taskId,
        Guid sessionId,
        int acceptedReplaySequenceNumber,
        long acceptedReplayMonotonicMicroseconds,
        D045Sha256Fingerprint reservationIdentity,
        D045Sha256Fingerprint candidateIdentity)
    {
        if (!D045AttachmentEgressCandidate.HasReservationTransitionSeal(
                transitionSeal)
            || ownerId == Guid.Empty
            || profileId == Guid.Empty
            || consentGenerationId == Guid.Empty
            || requestId == Guid.Empty
            || candidateId == Guid.Empty
            || taskId == Guid.Empty
            || sessionId == Guid.Empty
            || acceptedReplaySequenceNumber < 0
            || acceptedReplayMonotonicMicroseconds < 0
            || reservationIdentity is null
            || candidateIdentity is null)
        {
            throw new ArgumentException(
                "A validated attachment-egress reservation transition is required.",
                nameof(transitionSeal));
        }

        OwnerId = ownerId;
        ProfileId = profileId;
        ConsentGenerationId = consentGenerationId;
        RequestId = requestId;
        CandidateId = candidateId;
        TaskId = taskId;
        SessionId = sessionId;
        AcceptedReplaySequenceNumber = acceptedReplaySequenceNumber;
        AcceptedReplayMonotonicMicroseconds =
            acceptedReplayMonotonicMicroseconds;
        ReservationIdentity = reservationIdentity;
        CandidateIdentity = candidateIdentity;
    }

    public override string ToString()
        => "D045AttachmentEgressReservationRecord(content-free)";
}

internal sealed class D045ConsentReplayResult
{
    private readonly object? _replayProvenance;

    internal D045ConsentReplayFailure Failure { get; }
    internal bool Completed => Failure == D045ConsentReplayFailure.None;
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid CurrentConsentGenerationId { get; }
    internal long LastMonotonicMicroseconds { get; }
    internal long LastAcceptedMonotonicMicroseconds =>
        LastMonotonicMicroseconds;
    internal int LastAcceptedSequenceNumber { get; }
    internal ReadOnlyCollection<D045ConsentStateProjection> Candidates
    {
        get;
    }
    internal ReadOnlyCollection<D045OneActionConsentProjection>
        OneActionBindings { get; }
    internal ReadOnlyCollection<D045ConsentAuditReceipt> AuditRecords { get; }
    internal ReadOnlyCollection<D045AttachmentEgressReservationRecord>
        AttachmentEgressReservations { get; }
    internal bool AllEffectiveOff => true;
    internal bool RestartHydrationPresent => false;
    internal bool HasReplayProvenance =>
        Completed
        && D045ConsentGrantReplay.HasReplayProvenance(_replayProvenance);
    internal D045SourceOnlyAuthorityClaims AuthorityClaims =>
        D045SourceOnlyAuthorityClaims.None;

    private D045ConsentReplayResult(
        object? replayProvenance,
        D045ConsentReplayFailure failure,
        Guid ownerId,
        Guid profileId,
        Guid currentConsentGenerationId,
        long lastMonotonicMicroseconds,
        int lastAcceptedSequenceNumber,
        IReadOnlyList<D045ConsentStateProjection> candidates,
        IReadOnlyList<D045OneActionConsentProjection> oneActionBindings,
        IReadOnlyList<D045ConsentAuditReceipt> auditRecords,
        IReadOnlyList<D045AttachmentEgressReservationRecord>
            attachmentEgressReservations)
    {
        _replayProvenance = replayProvenance;
        Failure = failure;
        OwnerId = ownerId;
        ProfileId = profileId;
        CurrentConsentGenerationId = currentConsentGenerationId;
        LastMonotonicMicroseconds = lastMonotonicMicroseconds;
        LastAcceptedSequenceNumber = lastAcceptedSequenceNumber;
        Candidates = Freeze(candidates);
        OneActionBindings = Freeze(oneActionBindings);
        AuditRecords = Freeze(auditRecords);
        AttachmentEgressReservations = Freeze(attachmentEgressReservations);
    }

    internal static D045ConsentReplayResult FromValidatedReplay(
        object replayProvenance,
        Guid ownerId,
        Guid profileId,
        Guid currentConsentGenerationId,
        long lastMonotonicMicroseconds,
        int lastAcceptedSequenceNumber,
        IReadOnlyList<D045ConsentStateProjection> candidates,
        IReadOnlyList<D045OneActionConsentProjection> oneActionBindings,
        IReadOnlyList<D045ConsentAuditReceipt> auditRecords)
    {
        RequireReplayProvenance(replayProvenance);
        return new D045ConsentReplayResult(
            replayProvenance,
            D045ConsentReplayFailure.None,
            ownerId,
            profileId,
            currentConsentGenerationId,
            lastMonotonicMicroseconds,
            lastAcceptedSequenceNumber,
            candidates,
            oneActionBindings,
            auditRecords,
            Array.Empty<D045AttachmentEgressReservationRecord>());
    }

    internal static D045ConsentReplayResult FromValidatedReplayFailure(
        object replayProvenance,
        D045ConsentReplayFailure failure,
        Guid ownerId,
        Guid profileId,
        Guid currentConsentGenerationId,
        long lastMonotonicMicroseconds)
    {
        RequireReplayProvenance(replayProvenance);
        return new D045ConsentReplayResult(
            replayProvenance,
            failure,
            ownerId,
            profileId,
            currentConsentGenerationId,
            lastMonotonicMicroseconds,
            0,
            Array.Empty<D045ConsentStateProjection>(),
            Array.Empty<D045OneActionConsentProjection>(),
            Array.Empty<D045ConsentAuditReceipt>(),
            Array.Empty<D045AttachmentEgressReservationRecord>());
    }

    internal bool TryGetCandidate(
        D045AuthorityCapability capability,
        out D045ConsentStateProjection? candidate)
    {
        for (var index = 0; index < Candidates.Count; index++)
        {
            if (Candidates[index].Request.Capability == capability)
            {
                candidate = Candidates[index];
                return true;
            }
        }

        candidate = null;
        return false;
    }

    internal bool TryGetOneActionBinding(
        Guid requestId,
        out D045OneActionConsentProjection? binding)
    {
        for (var index = 0; index < OneActionBindings.Count; index++)
        {
            if (OneActionBindings[index].RequestId == requestId)
            {
                binding = OneActionBindings[index];
                return true;
            }
        }

        binding = null;
        return false;
    }

    internal D045AttachmentEgressReservationFailure
        TryApplyAttachmentEgressReservation(
            object transitionSeal,
            D045AttachmentEgressReservationRecord reservation,
            out D045ConsentReplayResult updatedReplay)
    {
        updatedReplay = this;
        if (!HasReplayProvenance)
        {
            return D045AttachmentEgressReservationFailure
                .ReplayProvenanceInvalid;
        }
        if (!D045AttachmentEgressCandidate.HasReservationTransitionSeal(
                transitionSeal)
            || reservation is null
            || reservation.OwnerId != OwnerId
            || reservation.ProfileId != ProfileId
            || reservation.ConsentGenerationId
                != CurrentConsentGenerationId
            || reservation.AcceptedReplaySequenceNumber
                != LastAcceptedSequenceNumber
            || reservation.AcceptedReplayMonotonicMicroseconds
                != LastAcceptedMonotonicMicroseconds)
        {
            return D045AttachmentEgressReservationFailure
                .ReservationTransitionInvalid;
        }

        for (var index = 0;
             index < AttachmentEgressReservations.Count;
             index++)
        {
            if (AttachmentEgressReservations[index].RequestId
                == reservation.RequestId)
            {
                return D045AttachmentEgressReservationFailure
                    .DuplicateReservation;
            }
        }
        if (AttachmentEgressReservations.Count
            >= D045ConsentLimits.MaximumReplayEvents)
        {
            return D045AttachmentEgressReservationFailure
                .ReservationCapacityExceeded;
        }

        var reservations = new D045AttachmentEgressReservationRecord[
            AttachmentEgressReservations.Count + 1];
        for (var index = 0;
             index < AttachmentEgressReservations.Count;
             index++)
        {
            reservations[index] = AttachmentEgressReservations[index];
        }
        reservations[^1] = reservation;

        updatedReplay = new D045ConsentReplayResult(
            _replayProvenance,
            Failure,
            OwnerId,
            ProfileId,
            CurrentConsentGenerationId,
            LastMonotonicMicroseconds,
            LastAcceptedSequenceNumber,
            Candidates,
            OneActionBindings,
            AuditRecords,
            reservations);
        return D045AttachmentEgressReservationFailure.None;
    }

    private static void RequireReplayProvenance(object replayProvenance)
    {
        if (!D045ConsentGrantReplay.HasReplayProvenance(replayProvenance))
        {
            throw new ArgumentException(
                "Validated replay provenance is required.",
                nameof(replayProvenance));
        }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IReadOnlyList<T> source)
    {
        var copy = new T[source.Count];
        for (var index = 0; index < source.Count; index++)
            copy[index] = source[index];
        return Array.AsReadOnly(copy);
    }
}

internal static class D045ConsentGrantReplay
{
    private sealed class ReplayProvenance
    {
    }

    internal static bool HasReplayProvenance(object? replayProvenance)
        => replayProvenance is ReplayProvenance;

    internal static D045ConsentReplayResult RestartEmpty(
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        long initialMonotonicMicroseconds = 0)
        => Replay(
            ownerId,
            profileId,
            consentGenerationId,
            initialMonotonicMicroseconds,
            Array.Empty<D045ConsentReplayEvent?>());

    internal static D045ConsentReplayResult Replay(
        Guid ownerId,
        Guid profileId,
        Guid initialConsentGenerationId,
        IReadOnlyList<D045ConsentReplayEvent?>? events)
        => Replay(
            ownerId,
            profileId,
            initialConsentGenerationId,
            0,
            events);

    internal static D045ConsentReplayResult Replay(
        Guid ownerId,
        Guid profileId,
        Guid initialConsentGenerationId,
        long initialMonotonicMicroseconds,
        IReadOnlyList<D045ConsentReplayEvent?>? events)
    {
        var replayProvenance = new ReplayProvenance();
        if (ownerId == Guid.Empty
            || profileId == Guid.Empty
            || initialConsentGenerationId == Guid.Empty)
        {
            return D045ConsentReplayResult.FromValidatedReplayFailure(
                replayProvenance,
                D045ConsentReplayFailure.InvalidReplayIdentity,
                ownerId,
                profileId,
                initialConsentGenerationId,
                initialMonotonicMicroseconds);
        }

        if (initialMonotonicMicroseconds < 0)
        {
            return D045ConsentReplayResult.FromValidatedReplayFailure(
                replayProvenance,
                D045ConsentReplayFailure.InvalidInitialMonotonicMicroseconds,
                ownerId,
                profileId,
                initialConsentGenerationId,
                initialMonotonicMicroseconds);
        }

        var suppliedEvents = events ?? Array.Empty<D045ConsentReplayEvent?>();
        if (suppliedEvents.Count > D045ConsentLimits.MaximumReplayEvents)
        {
            return D045ConsentReplayResult.FromValidatedReplayFailure(
                replayProvenance,
                D045ConsentReplayFailure.EventCapacityExceeded,
                ownerId,
                profileId,
                initialConsentGenerationId,
                initialMonotonicMicroseconds);
        }

        var candidates =
            new Dictionary<D045AuthorityCapability, D045ConsentRequest>();
        var oneActions =
            new Dictionary<Guid, D045OneActionConsentProjection>();
        var oneActionOrder = new List<Guid>();
        var auditRecords = new List<D045ConsentAuditReceipt>();
        var attemptedEventIds = new HashSet<Guid>();
        var attemptedIssuanceRequestIds = new HashSet<Guid>();
        var attemptedIssuanceCandidateIds = new HashSet<Guid>();
        var currentGenerationId = initialConsentGenerationId;
        var previousMonotonicMicroseconds = initialMonotonicMicroseconds;
        var lastAcceptedSequenceNumber = 0;

        for (var index = 0; index < suppliedEvents.Count; index++)
        {
            var replayEvent = suppliedEvents[index];
            if (replayEvent is null)
            {
                return D045ConsentReplayResult.FromValidatedReplayFailure(
                    replayProvenance,
                    D045ConsentReplayFailure.NullEvent,
                    ownerId,
                    profileId,
                    currentGenerationId,
                    previousMonotonicMicroseconds);
            }

            if (replayEvent.SequenceNumber != index + 1)
            {
                return D045ConsentReplayResult.FromValidatedReplayFailure(
                    replayProvenance,
                    D045ConsentReplayFailure.SequenceDiscontinuity,
                    ownerId,
                    profileId,
                    currentGenerationId,
                    previousMonotonicMicroseconds);
            }

            if (replayEvent.PreviousMonotonicMicroseconds
                != previousMonotonicMicroseconds)
            {
                return D045ConsentReplayResult.FromValidatedReplayFailure(
                    replayProvenance,
                    D045ConsentReplayFailure.MonotonicDiscontinuity,
                    ownerId,
                    profileId,
                    currentGenerationId,
                    previousMonotonicMicroseconds);
            }

            if (replayEvent.MonotonicMicroseconds
                    < replayEvent.PreviousMonotonicMicroseconds
                || replayEvent.MonotonicMicroseconds < 0)
            {
                return D045ConsentReplayResult.FromValidatedReplayFailure(
                    replayProvenance,
                    D045ConsentReplayFailure.MonotonicRetrograde,
                    ownerId,
                    profileId,
                    currentGenerationId,
                    previousMonotonicMicroseconds);
            }

            var disposition = RecordAttemptIdentities(
                replayEvent,
                attemptedEventIds,
                attemptedIssuanceRequestIds,
                attemptedIssuanceCandidateIds);
            disposition ??= ValidateEnvelope(
                replayEvent,
                ownerId,
                profileId,
                currentGenerationId);

            var eventCandidates = new Dictionary<
                D045AuthorityCapability,
                D045ConsentRequest>(candidates);
            var eventOneActions = new Dictionary<
                Guid,
                D045OneActionConsentProjection>(oneActions);
            var eventOneActionOrder = new List<Guid>(oneActionOrder);
            var eventGenerationId = currentGenerationId;

            if (disposition is null)
            {
                ExpireAt(
                    eventCandidates,
                    eventOneActions,
                    replayEvent.MonotonicMicroseconds);
                disposition = ApplyEvent(
                    replayEvent,
                    replayProvenance,
                    eventCandidates,
                    eventOneActions,
                    eventOneActionOrder,
                    ref eventGenerationId);
            }

            if (IsAccepted(disposition.Value))
            {
                candidates = eventCandidates;
                oneActions = eventOneActions;
                oneActionOrder = eventOneActionOrder;
                currentGenerationId = eventGenerationId;
                previousMonotonicMicroseconds =
                    replayEvent.MonotonicMicroseconds;
                lastAcceptedSequenceNumber = replayEvent.SequenceNumber;
            }

            auditRecords.Add(CreateAudit(replayEvent, disposition.Value));
        }

        var candidateResults = new List<D045ConsentStateProjection>();
        for (var ordinal = 1;
             ordinal <= D045AuthorityTaxonomy.ExactRowCount;
             ordinal++)
        {
            var capability = (D045AuthorityCapability)ordinal;
            if (candidates.TryGetValue(capability, out var request))
                candidateResults.Add(request.Project(true));
        }

        var oneActionResults = new List<D045OneActionConsentProjection>();
        for (var index = 0; index < oneActionOrder.Count; index++)
        {
            if (oneActions.TryGetValue(
                    oneActionOrder[index],
                    out var projection))
            {
                oneActionResults.Add(projection);
            }
        }

        return D045ConsentReplayResult.FromValidatedReplay(
            replayProvenance,
            ownerId,
            profileId,
            currentGenerationId,
            previousMonotonicMicroseconds,
            lastAcceptedSequenceNumber,
            candidateResults,
            oneActionResults,
            auditRecords);
    }

    private static D045ConsentAuditDisposition? RecordAttemptIdentities(
        D045ConsentReplayEvent replayEvent,
        ISet<Guid> attemptedEventIds,
        ISet<Guid> attemptedIssuanceRequestIds,
        ISet<Guid> attemptedIssuanceCandidateIds)
    {
        var eventIdInvalid = replayEvent.EventId == Guid.Empty;
        var eventIdDuplicate = !eventIdInvalid
            && !attemptedEventIds.Add(replayEvent.EventId);

        var requestIdDuplicate = false;
        var candidateIdDuplicate = false;
        if (replayEvent is D045ConsentRequestedEvent requested
            && requested.Request is not null)
        {
            var requestId = requested.Request.RequestId;
            var candidateId = requested.Request.CandidateId;
            requestIdDuplicate = requestId != Guid.Empty
                && !attemptedIssuanceRequestIds.Add(requestId);
            candidateIdDuplicate = candidateId != Guid.Empty
                && !attemptedIssuanceCandidateIds.Add(candidateId);
        }

        if (eventIdInvalid)
            return D045ConsentAuditDisposition.InvalidEventIdentity;
        if (eventIdDuplicate)
            return D045ConsentAuditDisposition.DuplicateEventIdentity;
        if (requestIdDuplicate)
            return D045ConsentAuditDisposition.DuplicateRequestIdentity;
        if (candidateIdDuplicate)
            return D045ConsentAuditDisposition.DuplicateCandidateIdentity;
        return null;
    }

    private static D045ConsentAuditDisposition? ValidateEnvelope(
        D045ConsentReplayEvent replayEvent,
        Guid ownerId,
        Guid profileId,
        Guid currentGenerationId)
    {
        if (replayEvent.OwnerId != ownerId)
            return D045ConsentAuditDisposition.CrossOwnerRejected;
        if (replayEvent.ProfileId != profileId)
            return D045ConsentAuditDisposition.CrossProfileRejected;
        if (replayEvent.ConsentGenerationId != currentGenerationId)
            return D045ConsentAuditDisposition.CrossGenerationRejected;
        return null;
    }

    private static D045ConsentAuditDisposition ApplyEvent(
        D045ConsentReplayEvent replayEvent,
        object replayProvenance,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        IList<Guid> oneActionOrder,
        ref Guid currentGenerationId)
        => replayEvent switch
        {
            D045ConsentRequestedEvent requested => ApplyRequest(
                requested,
                replayProvenance,
                candidates,
                oneActions,
                oneActionOrder),
            D045OneActionBindingConsumedEvent consumed => ApplyConsume(
                consumed,
                candidates,
                oneActions),
            D045ConsentCandidateRevokedEvent revoked => ApplyRevocation(
                revoked,
                candidates,
                oneActions),
            D045TaskEndedEvent taskEnded => ApplyTaskEnd(
                taskEnded,
                candidates,
                oneActions),
            D045SessionEndedEvent sessionEnded => ApplySessionEnd(
                sessionEnded,
                candidates,
                oneActions),
            D045ConsentGenerationChangedEvent generationChanged =>
                ApplyGenerationChange(
                    generationChanged,
                    candidates,
                    oneActions,
                    ref currentGenerationId),
            D045EmergencyStopEvent => ApplyEmergencyStop(
                candidates,
                oneActions),
            D045MonotonicCheckpointEvent =>
                D045ConsentAuditDisposition.MonotonicCheckpointRecorded,
            _ => D045ConsentAuditDisposition.EventKindRejected
        };

    private static D045ConsentAuditDisposition ApplyRequest(
        D045ConsentRequestedEvent replayEvent,
        object replayProvenance,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        IList<Guid> oneActionOrder)
    {
        var request = replayEvent.Request;
        if (request is null)
            return D045ConsentAuditDisposition.InvalidEventBinding;
        if (request.OwnerId != replayEvent.OwnerId)
            return D045ConsentAuditDisposition.CrossOwnerRejected;
        if (request.ProfileId != replayEvent.ProfileId)
            return D045ConsentAuditDisposition.CrossProfileRejected;
        if (request.ConsentGenerationId != replayEvent.ConsentGenerationId)
            return D045ConsentAuditDisposition.CrossGenerationRejected;
        if (request.RequestedAtMonotonicMicroseconds
            != replayEvent.MonotonicMicroseconds)
        {
            return D045ConsentAuditDisposition.RequestedAtMismatch;
        }

        var row = D045AuthorityCatalogRow.CreateCanonical(request.Capability);
        if (row.ConstitutionAmendmentRequired)
        {
            return D045ConsentAuditDisposition
                .ConstitutionAmendmentRequired;
        }

        if (request.DesiredMode == D045ConsentDesiredMode.PersistentRequested)
        {
            return D045ConsentAuditDisposition
                .DurableGrantOwnerUnavailable;
        }

        if (request.DesiredMode == D045ConsentDesiredMode.Off)
        {
            if (candidates.TryGetValue(request.Capability, out var current)
                && current.TargetScopeFingerprint
                != request.TargetScopeFingerprint)
            {
                return D045ConsentAuditDisposition.CrossScopeRejected;
            }

            candidates.Remove(request.Capability);
            RetireMatchingOneActions(
                oneActions,
                request.Capability,
                request.TargetScopeFingerprint,
                D045ConsentCandidateRetirement.DesiredOff);
            return D045ConsentAuditDisposition.DesiredOffRecorded;
        }

        if (candidates.ContainsKey(request.Capability))
            return D045ConsentAuditDisposition.CandidateAlreadyCurrent;

        if (request.DesiredMode == D045ConsentDesiredMode.OneAction)
        {
            foreach (var projection in oneActions.Values)
            {
                if (projection.IsEligibleForExactBindingAt(
                        replayEvent.MonotonicMicroseconds))
                {
                    return D045ConsentAuditDisposition.OneActionAlreadyCurrent;
                }
            }
        }

        candidates.Add(request.Capability, request);
        if (request.DesiredMode == D045ConsentDesiredMode.OneAction)
        {
            oneActions.Add(
                request.RequestId,
                D045OneActionConsentProjection.FromValidatedReplay(
                    request,
                    D045ConsentCandidateRetirement.None,
                    replayProvenance));
            oneActionOrder.Add(request.RequestId);
        }

        return request.DesiredMode == D045ConsentDesiredMode.AskEachTime
            ? D045ConsentAuditDisposition.AskEachTimeRecorded
            : D045ConsentAuditDisposition.SourceOnlyCandidateRecorded;
    }

    private static D045ConsentAuditDisposition ApplyConsume(
        D045OneActionBindingConsumedEvent replayEvent,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions)
    {
        var binding = replayEvent.Binding;
        if (binding is null || !binding.IsComplete)
            return D045ConsentAuditDisposition.InvalidEventBinding;
        if (!oneActions.TryGetValue(binding.RequestId, out var projection))
        {
            return D045ConsentAuditDisposition.OneActionBindingNotFound;
        }

        var mismatch = CompareBinding(binding, projection);
        if (mismatch is not null)
            return mismatch.Value;
        if (projection.Consumed)
        {
            return D045ConsentAuditDisposition
                .OneActionBindingAlreadyConsumed;
        }

        if (projection.Retirement != D045ConsentCandidateRetirement.None)
        {
            return projection.Retirement
                == D045ConsentCandidateRetirement.Expired
                ? D045ConsentAuditDisposition.OneActionBindingExpired
                : D045ConsentAuditDisposition.OneActionBindingRevoked;
        }

        if (!projection.IsEligibleForExactBindingAt(
                replayEvent.MonotonicMicroseconds))
        {
            return D045ConsentAuditDisposition.OneActionBindingExpired;
        }

        oneActions[binding.RequestId] = projection.Retire(
            D045ConsentCandidateRetirement.Consumed);
        if (candidates.TryGetValue(binding.Capability, out var candidate)
            && candidate.RequestId == binding.RequestId)
        {
            candidates.Remove(binding.Capability);
        }

        return D045ConsentAuditDisposition
            .OneActionBindingConsumedWithoutEffect;
    }

    private static D045ConsentAuditDisposition ApplyRevocation(
        D045ConsentCandidateRevokedEvent replayEvent,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions)
    {
        var binding = replayEvent.Binding;
        if (binding is null || !binding.IsComplete)
            return D045ConsentAuditDisposition.InvalidEventBinding;
        if (!candidates.TryGetValue(binding.Capability, out var request))
            return D045ConsentAuditDisposition.CandidateNotFound;

        var mismatch = CompareBinding(binding, request);
        if (mismatch is not null)
            return mismatch.Value;

        candidates.Remove(binding.Capability);
        if (oneActions.TryGetValue(binding.RequestId, out var projection))
        {
            oneActions[binding.RequestId] = projection.Retire(
                D045ConsentCandidateRetirement.ExplicitRevocation);
        }

        return D045ConsentAuditDisposition.CandidateRevoked;
    }

    private static D045ConsentAuditDisposition ApplyTaskEnd(
        D045TaskEndedEvent replayEvent,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions)
    {
        if (replayEvent.TaskId == Guid.Empty
            || replayEvent.SessionId == Guid.Empty)
        {
            return D045ConsentAuditDisposition.InvalidEventIdentity;
        }

        var matched = false;
        var sawTask = false;
        for (var ordinal = 1;
             ordinal <= D045AuthorityTaxonomy.ExactRowCount;
             ordinal++)
        {
            var capability = (D045AuthorityCapability)ordinal;
            if (!candidates.TryGetValue(capability, out var request))
                continue;
            if (request.TaskId == replayEvent.TaskId)
                sawTask = true;
            if (request.TaskId != replayEvent.TaskId
                || request.SessionId != replayEvent.SessionId)
            {
                continue;
            }

            matched = true;
            candidates.Remove(capability);
            RetireOneAction(
                oneActions,
                request.RequestId,
                D045ConsentCandidateRetirement.TaskEnded);
        }

        if (matched)
            return D045ConsentAuditDisposition.TaskCandidatesRetired;
        return sawTask
            ? D045ConsentAuditDisposition.CrossSessionRejected
            : D045ConsentAuditDisposition.CrossTaskRejected;
    }

    private static D045ConsentAuditDisposition ApplySessionEnd(
        D045SessionEndedEvent replayEvent,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions)
    {
        if (replayEvent.SessionId == Guid.Empty)
            return D045ConsentAuditDisposition.InvalidEventIdentity;

        var matched = false;
        for (var ordinal = 1;
             ordinal <= D045AuthorityTaxonomy.ExactRowCount;
             ordinal++)
        {
            var capability = (D045AuthorityCapability)ordinal;
            if (!candidates.TryGetValue(capability, out var request)
                || request.SessionId != replayEvent.SessionId)
            {
                continue;
            }

            matched = true;
            candidates.Remove(capability);
            RetireOneAction(
                oneActions,
                request.RequestId,
                D045ConsentCandidateRetirement.SessionEnded);
        }

        return matched
            ? D045ConsentAuditDisposition.SessionCandidatesRetired
            : D045ConsentAuditDisposition.CrossSessionRejected;
    }

    private static D045ConsentAuditDisposition ApplyGenerationChange(
        D045ConsentGenerationChangedEvent replayEvent,
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        ref Guid currentGenerationId)
    {
        if (replayEvent.NextConsentGenerationId == Guid.Empty
            || replayEvent.NextConsentGenerationId == currentGenerationId)
        {
            return D045ConsentAuditDisposition.InvalidEventIdentity;
        }

        candidates.Clear();
        RetireAllOneActions(
            oneActions,
            D045ConsentCandidateRetirement.GenerationChanged);
        currentGenerationId = replayEvent.NextConsentGenerationId;
        return D045ConsentAuditDisposition.GenerationCandidatesRetired;
    }

    private static D045ConsentAuditDisposition ApplyEmergencyStop(
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions)
    {
        candidates.Clear();
        RetireAllOneActions(
            oneActions,
            D045ConsentCandidateRetirement.EmergencyStop);
        return D045ConsentAuditDisposition
            .EmergencyStopRevocationRecorded;
    }

    private static D045ConsentAuditDisposition? CompareBinding(
        D045ConsentBindingEvidence binding,
        D045OneActionConsentProjection projection)
        => CompareBinding(binding, projection.Request);

    private static D045ConsentAuditDisposition? CompareBinding(
        D045ConsentBindingEvidence binding,
        D045ConsentRequest request)
    {
        if (binding.OwnerId != request.OwnerId)
            return D045ConsentAuditDisposition.CrossOwnerRejected;
        if (binding.ProfileId != request.ProfileId)
            return D045ConsentAuditDisposition.CrossProfileRejected;
        if (binding.ConsentGenerationId != request.ConsentGenerationId)
            return D045ConsentAuditDisposition.CrossGenerationRejected;
        if (binding.RequestId != request.RequestId)
            return D045ConsentAuditDisposition.CrossRequestRejected;
        if (binding.CandidateId != request.CandidateId)
            return D045ConsentAuditDisposition.InvalidEventBinding;
        if (binding.TaskId != request.TaskId)
            return D045ConsentAuditDisposition.CrossTaskRejected;
        if (binding.SessionId != request.SessionId)
            return D045ConsentAuditDisposition.CrossSessionRejected;
        if (binding.Capability != request.Capability)
            return D045ConsentAuditDisposition.CrossCapabilityRejected;
        if (binding.TargetScopeFingerprint != request.TargetScopeFingerprint
            || binding.PurposeFingerprint != request.PurposeFingerprint
            || binding.PreviewFingerprint != request.PreviewFingerprint)
        {
            return D045ConsentAuditDisposition.CrossScopeRejected;
        }

        return null;
    }

    private static void ExpireAt(
        IDictionary<D045AuthorityCapability, D045ConsentRequest> candidates,
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        long monotonicMicroseconds)
    {
        for (var ordinal = 1;
             ordinal <= D045AuthorityTaxonomy.ExactRowCount;
             ordinal++)
        {
            var capability = (D045AuthorityCapability)ordinal;
            if (!candidates.TryGetValue(capability, out var request)
                || request.RequestedLifetimeMicroseconds == 0
                || monotonicMicroseconds
                    < request.EndsAtMonotonicMicroseconds)
            {
                continue;
            }

            candidates.Remove(capability);
            RetireOneAction(
                oneActions,
                request.RequestId,
                D045ConsentCandidateRetirement.Expired);
        }
    }

    private static void RetireMatchingOneActions(
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        D045AuthorityCapability capability,
        D045Sha256Fingerprint targetScopeFingerprint,
        D045ConsentCandidateRetirement retirement)
    {
        var requestIds = new List<Guid>(oneActions.Keys);
        for (var index = 0; index < requestIds.Count; index++)
        {
            var requestId = requestIds[index];
            var projection = oneActions[requestId];
            if (projection.Capability == capability
                && projection.TargetScopeFingerprint
                    == targetScopeFingerprint)
            {
                oneActions[requestId] = projection.Retire(retirement);
            }
        }
    }

    private static void RetireAllOneActions(
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        D045ConsentCandidateRetirement retirement)
    {
        var requestIds = new List<Guid>(oneActions.Keys);
        for (var index = 0; index < requestIds.Count; index++)
            RetireOneAction(oneActions, requestIds[index], retirement);
    }

    private static void RetireOneAction(
        IDictionary<Guid, D045OneActionConsentProjection> oneActions,
        Guid requestId,
        D045ConsentCandidateRetirement retirement)
    {
        if (oneActions.TryGetValue(requestId, out var projection))
            oneActions[requestId] = projection.Retire(retirement);
    }

    private static bool IsAccepted(D045ConsentAuditDisposition disposition)
        => disposition is
            D045ConsentAuditDisposition.SourceOnlyCandidateRecorded
            or D045ConsentAuditDisposition.DesiredOffRecorded
            or D045ConsentAuditDisposition.AskEachTimeRecorded
            or D045ConsentAuditDisposition
                .OneActionBindingConsumedWithoutEffect
            or D045ConsentAuditDisposition.CandidateRevoked
            or D045ConsentAuditDisposition.TaskCandidatesRetired
            or D045ConsentAuditDisposition.SessionCandidatesRetired
            or D045ConsentAuditDisposition.GenerationCandidatesRetired
            or D045ConsentAuditDisposition
                .EmergencyStopRevocationRecorded
            or D045ConsentAuditDisposition.MonotonicCheckpointRecorded;

    private static D045ConsentAuditReceipt CreateAudit(
        D045ConsentReplayEvent replayEvent,
        D045ConsentAuditDisposition disposition)
        => new(
            replayEvent.SequenceNumber,
            replayEvent.EventId,
            replayEvent.OwnerId,
            replayEvent.ProfileId,
            replayEvent.ConsentGenerationId,
            replayEvent.AuditRequestId,
            replayEvent.AuditCandidateId,
            replayEvent.AuditCapability,
            replayEvent.AuditTargetScopeFingerprint,
            replayEvent.MonotonicMicroseconds,
            IsAccepted(disposition),
            disposition);
}
