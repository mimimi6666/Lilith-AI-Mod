using System;
using System.Security.Cryptography;

namespace LilithAgentWorkbench;

internal enum D045ConsentDesiredMode
{
    Off = 0,
    AskEachTime = 1,
    OneAction = 2,
    CurrentTask = 3,
    CurrentSession = 4,
    PersistentRequested = 5
}

internal enum D045ConsentRequestCreationFailure
{
    None = 0,
    InvalidIdentity = 1,
    InvalidFingerprint = 2,
    UnknownCapability = 3,
    UnknownDesiredMode = 4,
    InvalidMonotonicMicroseconds = 5,
    InvalidLifetime = 6,
    LifetimeArithmeticOverflow = 7,
    ConstitutionAmendmentRequired = 8,
    DurableGrantOwnerUnavailable = 9
}

internal enum D045ConsentCandidateRetirement
{
    None = 0,
    DesiredOff = 1,
    Consumed = 2,
    ExplicitRevocation = 3,
    Expired = 4,
    TaskEnded = 5,
    SessionEnded = 6,
    GenerationChanged = 7,
    EmergencyStop = 8
}

internal enum D045ConsentAuditDisposition
{
    SourceOnlyCandidateRecorded = 1,
    DesiredOffRecorded = 2,
    AskEachTimeRecorded = 3,
    OneActionBindingConsumedWithoutEffect = 4,
    CandidateRevoked = 5,
    TaskCandidatesRetired = 6,
    SessionCandidatesRetired = 7,
    GenerationCandidatesRetired = 8,
    EmergencyStopRevocationRecorded = 9,
    MonotonicCheckpointRecorded = 10,
    ConstitutionAmendmentRequired = 11,
    DurableGrantOwnerUnavailable = 12,
    InvalidEventIdentity = 13,
    InvalidEventBinding = 14,
    RequestedAtMismatch = 15,
    DuplicateRequestIdentity = 16,
    DuplicateCandidateIdentity = 17,
    CandidateAlreadyCurrent = 18,
    OneActionAlreadyCurrent = 19,
    CandidateNotFound = 20,
    OneActionBindingNotFound = 21,
    OneActionBindingAlreadyConsumed = 22,
    OneActionBindingRevoked = 23,
    OneActionBindingExpired = 24,
    CrossOwnerRejected = 25,
    CrossProfileRejected = 26,
    CrossGenerationRejected = 27,
    CrossRequestRejected = 28,
    CrossTaskRejected = 29,
    CrossSessionRejected = 30,
    CrossCapabilityRejected = 31,
    CrossScopeRejected = 32,
    EventKindRejected = 33,
    DuplicateEventIdentity = 34
}

internal static class D045ConsentLimits
{
    internal const int ExactFingerprintBytes = 32;
    internal const int MaximumReplayEvents = 128;
    internal const int MaximumAuditRecords = 128;
    internal const long MaximumOneActionMicroseconds = 30_000_000;
    internal const long MaximumCurrentTaskMicroseconds = 1_800_000_000;
    internal const long MaximumCurrentSessionMicroseconds = 28_800_000_000;
}

internal sealed class D045Sha256Fingerprint :
    IEquatable<D045Sha256Fingerprint>
{
    private readonly byte[] _bytes;

    internal string CanonicalHex => Convert.ToHexString(_bytes);

    private D045Sha256Fingerprint(byte[] bytes)
    {
        _bytes = bytes;
    }

    internal static D045Sha256Fingerprint Create(byte[] bytes)
    {
        if (!TryCreate(bytes, out var fingerprint))
        {
            throw new ArgumentException(
                "An exact non-zero 32-byte SHA-256 fingerprint is required.",
                nameof(bytes));
        }

        return fingerprint!;
    }

    internal static bool TryCreate(
        byte[]? bytes,
        out D045Sha256Fingerprint? fingerprint)
    {
        if (bytes is null
            || bytes.Length != D045ConsentLimits.ExactFingerprintBytes)
        {
            fingerprint = null;
            return false;
        }

        var nonZero = false;
        var copy = new byte[D045ConsentLimits.ExactFingerprintBytes];
        for (var index = 0; index < copy.Length; index++)
        {
            copy[index] = bytes[index];
            nonZero |= bytes[index] != 0;
        }

        if (!nonZero)
        {
            fingerprint = null;
            return false;
        }

        fingerprint = new D045Sha256Fingerprint(copy);
        return true;
    }

    internal static bool TryParseCanonicalHex(
        string? canonicalHex,
        out D045Sha256Fingerprint? fingerprint)
    {
        fingerprint = null;
        if (canonicalHex is null
            || canonicalHex.Length
            != D045ConsentLimits.ExactFingerprintBytes * 2)
        {
            return false;
        }

        for (var index = 0; index < canonicalHex.Length; index++)
        {
            var character = canonicalHex[index];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }

        try
        {
            return TryCreate(Convert.FromHexString(canonicalHex), out fingerprint);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal byte[] CopyBytes()
        => (byte[])_bytes.Clone();

    public bool Equals(D045Sha256Fingerprint? other)
        => other is not null
           && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    public override bool Equals(object? obj)
        => obj is D045Sha256Fingerprint other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(
            _bytes[0],
            _bytes[7],
            _bytes[15],
            _bytes[23],
            _bytes[31]);

    public static bool operator ==(
        D045Sha256Fingerprint? left,
        D045Sha256Fingerprint? right)
        => ReferenceEquals(left, right)
           || (left is not null && left.Equals(right));

    public static bool operator !=(
        D045Sha256Fingerprint? left,
        D045Sha256Fingerprint? right)
        => !(left == right);

    public override string ToString()
        => "D045Sha256Fingerprint(content-free)";
}

internal sealed class D045ConsentIdentitySet
{
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid ConsentGenerationId { get; }
    internal Guid RequestId { get; }
    internal Guid TaskId { get; }
    internal Guid SessionId { get; }
    internal Guid CandidateId { get; }

    internal bool IsComplete =>
        OwnerId != Guid.Empty
        && ProfileId != Guid.Empty
        && ConsentGenerationId != Guid.Empty
        && RequestId != Guid.Empty
        && TaskId != Guid.Empty
        && SessionId != Guid.Empty
        && CandidateId != Guid.Empty;

    internal D045ConsentIdentitySet(
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid requestId,
        Guid taskId,
        Guid sessionId,
        Guid candidateId)
    {
        OwnerId = ownerId;
        ProfileId = profileId;
        ConsentGenerationId = consentGenerationId;
        RequestId = requestId;
        TaskId = taskId;
        SessionId = sessionId;
        CandidateId = candidateId;
    }
}

internal sealed class D045ConsentScopeDescriptor
{
    internal D045AuthorityCapability Capability { get; }
    internal D045Sha256Fingerprint TargetScopeFingerprint { get; }
    internal D045Sha256Fingerprint PurposeFingerprint { get; }
    internal D045Sha256Fingerprint PreviewFingerprint { get; }

    internal D045ConsentScopeDescriptor(
        D045AuthorityCapability capability,
        D045Sha256Fingerprint targetScopeFingerprint,
        D045Sha256Fingerprint purposeFingerprint,
        D045Sha256Fingerprint previewFingerprint)
    {
        Capability = capability;
        TargetScopeFingerprint = targetScopeFingerprint
            ?? throw new ArgumentNullException(nameof(targetScopeFingerprint));
        PurposeFingerprint = purposeFingerprint
            ?? throw new ArgumentNullException(nameof(purposeFingerprint));
        PreviewFingerprint = previewFingerprint
            ?? throw new ArgumentNullException(nameof(previewFingerprint));
    }
}

internal sealed class D045ConsentRequest
{
    internal D045ConsentIdentitySet Identities { get; }
    internal D045ConsentScopeDescriptor Scope { get; }
    internal D045ConsentDesiredMode DesiredMode { get; }
    internal long RequestedAtMonotonicMicroseconds { get; }
    internal long RequestedLifetimeMicroseconds { get; }
    internal long EndsAtMonotonicMicroseconds { get; }

    internal Guid OwnerId => Identities.OwnerId;
    internal Guid ProfileId => Identities.ProfileId;
    internal Guid ConsentGenerationId => Identities.ConsentGenerationId;
    internal Guid RequestId => Identities.RequestId;
    internal Guid TaskId => Identities.TaskId;
    internal Guid SessionId => Identities.SessionId;
    internal Guid CandidateId => Identities.CandidateId;
    internal D045AuthorityCapability Capability => Scope.Capability;
    internal D045Sha256Fingerprint TargetScopeFingerprint =>
        Scope.TargetScopeFingerprint;
    internal D045Sha256Fingerprint PurposeFingerprint =>
        Scope.PurposeFingerprint;
    internal D045Sha256Fingerprint PreviewFingerprint =>
        Scope.PreviewFingerprint;

    private D045ConsentRequest(
        D045ConsentIdentitySet identities,
        D045ConsentScopeDescriptor scope,
        D045ConsentDesiredMode desiredMode,
        long requestedAtMonotonicMicroseconds,
        long requestedLifetimeMicroseconds,
        long endsAtMonotonicMicroseconds)
    {
        Identities = identities;
        Scope = scope;
        DesiredMode = desiredMode;
        RequestedAtMonotonicMicroseconds = requestedAtMonotonicMicroseconds;
        RequestedLifetimeMicroseconds = requestedLifetimeMicroseconds;
        EndsAtMonotonicMicroseconds = endsAtMonotonicMicroseconds;
    }

    internal static D045ConsentRequestCreationResult TryCreate(
        D045ConsentIdentitySet? identities,
        D045ConsentScopeDescriptor? scope,
        D045ConsentDesiredMode desiredMode,
        long requestedAtMonotonicMicroseconds,
        long requestedLifetimeMicroseconds)
    {
        if (identities is null || !identities.IsComplete)
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure.InvalidIdentity);
        }

        if (scope is null
            || scope.TargetScopeFingerprint is null
            || scope.PurposeFingerprint is null
            || scope.PreviewFingerprint is null)
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure.InvalidFingerprint);
        }

        if (!D045AuthorityTaxonomy.IsDefined(scope.Capability))
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure.UnknownCapability);
        }

        if (!IsDefined(desiredMode))
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure.UnknownDesiredMode);
        }

        if (requestedAtMonotonicMicroseconds < 0)
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure
                    .InvalidMonotonicMicroseconds);
        }

        var maximumLifetime = MaximumLifetime(desiredMode);
        if ((maximumLifetime == 0 && requestedLifetimeMicroseconds != 0)
            || (maximumLifetime > 0
                && (requestedLifetimeMicroseconds <= 0
                    || requestedLifetimeMicroseconds > maximumLifetime)))
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure.InvalidLifetime);
        }

        long endsAt;
        try
        {
            endsAt = checked(
                requestedAtMonotonicMicroseconds
                + requestedLifetimeMicroseconds);
        }
        catch (OverflowException)
        {
            return D045ConsentRequestCreationResult.Reject(
                D045ConsentRequestCreationFailure
                    .LifetimeArithmeticOverflow);
        }

        var request = new D045ConsentRequest(
            identities,
            scope,
            desiredMode,
            requestedAtMonotonicMicroseconds,
            requestedLifetimeMicroseconds,
            endsAt);

        var row = D045AuthorityCatalogRow.CreateCanonical(scope.Capability);
        if (row.ConstitutionAmendmentRequired)
        {
            return D045ConsentRequestCreationResult.RepresentOnly(
                request,
                D045ConsentRequestCreationFailure
                    .ConstitutionAmendmentRequired);
        }

        if (desiredMode == D045ConsentDesiredMode.PersistentRequested)
        {
            return D045ConsentRequestCreationResult.RepresentOnly(
                request,
                D045ConsentRequestCreationFailure
                    .DurableGrantOwnerUnavailable);
        }

        return D045ConsentRequestCreationResult.Accept(request);
    }

    internal D045ConsentStateProjection Project(
        bool sourceCandidateCurrent)
        => new(this, sourceCandidateCurrent);

    private static bool IsDefined(D045ConsentDesiredMode desiredMode)
        => desiredMode is D045ConsentDesiredMode.Off
            or D045ConsentDesiredMode.AskEachTime
            or D045ConsentDesiredMode.OneAction
            or D045ConsentDesiredMode.CurrentTask
            or D045ConsentDesiredMode.CurrentSession
            or D045ConsentDesiredMode.PersistentRequested;

    private static long MaximumLifetime(D045ConsentDesiredMode desiredMode)
        => desiredMode switch
        {
            D045ConsentDesiredMode.OneAction =>
                D045ConsentLimits.MaximumOneActionMicroseconds,
            D045ConsentDesiredMode.CurrentTask =>
                D045ConsentLimits.MaximumCurrentTaskMicroseconds,
            D045ConsentDesiredMode.CurrentSession =>
                D045ConsentLimits.MaximumCurrentSessionMicroseconds,
            _ => 0
        };

    public override string ToString()
        => "D045ConsentRequest(content-free; EffectiveOff)";
}

internal sealed class D045ConsentRequestCreationResult
{
    internal D045ConsentRequest? Request { get; }
    internal D045ConsentRequestCreationFailure Failure { get; }
    internal bool Accepted => Failure == D045ConsentRequestCreationFailure.None;
    internal bool Represented => Request is not null;
    internal bool Effective => false;

    private D045ConsentRequestCreationResult(
        D045ConsentRequest? request,
        D045ConsentRequestCreationFailure failure)
    {
        Request = request;
        Failure = failure;
    }

    internal static D045ConsentRequestCreationResult Accept(
        D045ConsentRequest request)
        => new(request, D045ConsentRequestCreationFailure.None);

    internal static D045ConsentRequestCreationResult RepresentOnly(
        D045ConsentRequest request,
        D045ConsentRequestCreationFailure failure)
        => new(request, failure);

    internal static D045ConsentRequestCreationResult Reject(
        D045ConsentRequestCreationFailure failure)
        => new(null, failure);
}

internal sealed class D045ConsentStateProjection
{
    internal D045ConsentRequest Request { get; }
    internal D045ConsentDesiredMode Desired => Request.DesiredMode;
    internal bool SourceCandidateCurrent { get; }
    internal bool Available => false;
    internal bool Effective => false;
    internal bool Active => false;
    internal bool ExecutionAuthority => false;

    internal D045ConsentStateProjection(
        D045ConsentRequest request,
        bool sourceCandidateCurrent)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        SourceCandidateCurrent = sourceCandidateCurrent;
    }
}

internal readonly struct D045SourceOnlyAuthorityClaims
{
    internal static D045SourceOnlyAuthorityClaims None => default;

    internal bool RuntimeCallerPresent => false;
    internal bool OsEffectAuthority => false;
    internal bool NetworkAuthority => false;
    internal bool DurableGrantAuthority => false;
    internal bool SkillActivationAuthority => false;
    internal bool ExecutionAuthority => false;
    internal bool ExternalRequestAuthority => false;
    internal bool ActionReceiptPresent => false;
    internal bool AnyEffectiveCapability => false;
}

internal sealed class D045OneActionConsentProjection
{
    private readonly object? _replayProvenance;

    internal D045ConsentRequest Request { get; }
    internal D045ConsentCandidateRetirement Retirement { get; }
    internal long RequestedAtMonotonicMicroseconds =>
        Request.RequestedAtMonotonicMicroseconds;
    internal long ExpiresAtMonotonicMicroseconds =>
        Request.EndsAtMonotonicMicroseconds;

    internal Guid OwnerId => Request.OwnerId;
    internal Guid ProfileId => Request.ProfileId;
    internal Guid ConsentGenerationId => Request.ConsentGenerationId;
    internal Guid RequestId => Request.RequestId;
    internal Guid TaskId => Request.TaskId;
    internal Guid SessionId => Request.SessionId;
    internal Guid CandidateId => Request.CandidateId;
    internal D045AuthorityCapability Capability => Request.Capability;
    internal D045Sha256Fingerprint TargetScopeFingerprint =>
        Request.TargetScopeFingerprint;
    internal D045Sha256Fingerprint PurposeFingerprint =>
        Request.PurposeFingerprint;
    internal D045Sha256Fingerprint PreviewFingerprint =>
        Request.PreviewFingerprint;
    internal bool Consumed =>
        Retirement == D045ConsentCandidateRetirement.Consumed;
    internal bool Revoked =>
        Retirement is D045ConsentCandidateRetirement.ExplicitRevocation
            or D045ConsentCandidateRetirement.DesiredOff
            or D045ConsentCandidateRetirement.TaskEnded
            or D045ConsentCandidateRetirement.SessionEnded
            or D045ConsentCandidateRetirement.GenerationChanged
            or D045ConsentCandidateRetirement.EmergencyStop;
    internal bool Effective => false;
    internal bool ExecutionAuthority => false;
    internal bool IsReplayProof =>
        D045ConsentGrantReplay.HasReplayProvenance(_replayProvenance);

    internal D045OneActionConsentProjection(
        D045ConsentRequest request,
        D045ConsentCandidateRetirement retirement)
        : this(request, retirement, replayProvenance: null)
    {
    }

    private D045OneActionConsentProjection(
        D045ConsentRequest request,
        D045ConsentCandidateRetirement retirement,
        object? replayProvenance)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        if (request.DesiredMode != D045ConsentDesiredMode.OneAction)
        {
            throw new ArgumentException(
                "A one-action request is required.",
                nameof(request));
        }

        Retirement = retirement;
        _replayProvenance = replayProvenance;
    }

    internal static D045OneActionConsentProjection FromValidatedReplay(
        D045ConsentRequest request,
        D045ConsentCandidateRetirement retirement,
        object replayProvenance)
    {
        if (!D045ConsentGrantReplay.HasReplayProvenance(replayProvenance))
            throw new ArgumentException("Validated replay provenance is required.", nameof(replayProvenance));
        return new D045OneActionConsentProjection(
            request,
            retirement,
            replayProvenance);
    }

    internal bool IsEligibleForExactBindingAt(
        long monotonicMicroseconds)
        => monotonicMicroseconds >= Request.RequestedAtMonotonicMicroseconds
           && monotonicMicroseconds < ExpiresAtMonotonicMicroseconds
           && Retirement == D045ConsentCandidateRetirement.None;

    internal bool IsEligibleAt(long monotonicMicroseconds)
        => IsEligibleForExactBindingAt(monotonicMicroseconds);

    internal D045OneActionConsentProjection Retire(
        D045ConsentCandidateRetirement retirement)
    {
        if (retirement == D045ConsentCandidateRetirement.None)
            throw new ArgumentOutOfRangeException(nameof(retirement));
        return Retirement == D045ConsentCandidateRetirement.None
            ? new D045OneActionConsentProjection(
                Request,
                retirement,
                _replayProvenance)
            : this;
    }

    public override string ToString()
        => "D045OneActionConsentProjection(content-free; EffectiveOff)";
}

internal sealed class D045ConsentAuditReceipt
{
    internal int SequenceNumber { get; }
    internal Guid EventId { get; }
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid ConsentGenerationId { get; }
    internal Guid RequestId { get; }
    internal Guid CandidateId { get; }
    internal D045AuthorityCapability? Capability { get; }
    internal D045Sha256Fingerprint? TargetScopeFingerprint { get; }
    internal long MonotonicMicroseconds { get; }
    internal long AttemptedMonotonicMicroseconds => MonotonicMicroseconds;
    internal bool MonotonicProjectionAccepted { get; }
    internal D045ConsentAuditDisposition Disposition { get; }
    internal bool IsActionReceipt => false;
    internal bool Effective => false;
    internal D045SourceOnlyAuthorityClaims AuthorityClaims =>
        D045SourceOnlyAuthorityClaims.None;

    internal D045ConsentAuditReceipt(
        int sequenceNumber,
        Guid eventId,
        Guid ownerId,
        Guid profileId,
        Guid consentGenerationId,
        Guid requestId,
        Guid candidateId,
        D045AuthorityCapability? capability,
        D045Sha256Fingerprint? targetScopeFingerprint,
        long monotonicMicroseconds,
        bool monotonicProjectionAccepted,
        D045ConsentAuditDisposition disposition)
    {
        SequenceNumber = sequenceNumber;
        EventId = eventId;
        OwnerId = ownerId;
        ProfileId = profileId;
        ConsentGenerationId = consentGenerationId;
        RequestId = requestId;
        CandidateId = candidateId;
        Capability = capability;
        TargetScopeFingerprint = targetScopeFingerprint;
        MonotonicMicroseconds = monotonicMicroseconds;
        MonotonicProjectionAccepted = monotonicProjectionAccepted;
        Disposition = disposition;
    }

    public override string ToString()
        => $"D045ConsentAuditReceipt({SequenceNumber}:{Disposition}; no effect)";
}
