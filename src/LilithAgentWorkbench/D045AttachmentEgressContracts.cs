using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace LilithAgentWorkbench;

internal enum D045AttachmentKind
{
    LocalFile = 1,
    LocalFolder = 2,
    ClipboardSnapshot = 3,
    Screenshot = 4,
    PastedImage = 5,
    PublicHttpsUrlReference = 6,
    GeneratedArtifact = 7
}

internal enum D045AttachmentContentForm
{
    Binary = 1,
    Utf8Text = 2
}

internal enum D045AttachmentProvenanceKind
{
    SyntheticFixture = 1
}

internal enum D045AttachmentLifetime
{
    CurrentMessage = 1,
    CurrentTurn = 2,
    CurrentTask = 3,
    CurrentSession = 4
}

internal enum D045AttachmentSharingScope
{
    LocalPreviewOnly = 1,
    ProviderTextRequestCandidate = 2,
    ExactExternalRecipientCandidate = 3
}

internal enum D045AttachmentEgressDestinationKind
{
    ProviderTextRequest = 1,
    ExactExternalRecipient = 2
}

internal enum D045AttachmentRetirementMarker
{
    NoBoundaryEvent = 1,
    MessageSent = 2,
    MessageCancelled = 3,
    TurnClosed = 4,
    TaskClosed = 5,
    SessionClosed = 6
}

internal enum D045AttachmentContractFailure
{
    None = 0,
    AttachmentIdentityMissing = 1,
    DigestMissing = 2,
    ClosedValueInvalid = 3,
    DeclaredLengthInvalid = 4,
    TextElementCountInvalid = 5,
    MonotonicValueInvalid = 6,
    LifetimeInvalid = 7,
    MessageShapeInvalid = 8,
    DescriptorCapacityExceeded = 9,
    DuplicateAttachmentIdentity = 10,
    AggregateLengthExceeded = 11,
    EgressBindingInvalid = 12,
    DescriptorSubstitution = 13,
    OneActionRequestInvalid = 14,
    EgressDestinationInvalid = 15,
    EgressCapabilityUnsupported = 16,
    ConstitutionAmendmentRequired = 17
}

internal enum D045AttachmentEgressReservationFailure
{
    None = 0,
    ReplayResultInvalid = 1,
    ReplayProvenanceInvalid = 2,
    OneActionBindingNotFound = 3,
    OneActionBindingInvalid = 4,
    AcceptedPreviewMissing = 5,
    PresentedPreviewMissing = 6,
    PreviewSubstitution = 7,
    AttachmentRetired = 8,
    DuplicateReservation = 9,
    ReservationCapacityExceeded = 10,
    ReservationTransitionInvalid = 11
}

internal sealed class D045AttachmentContractException : Exception
{
    internal D045AttachmentContractFailure Failure { get; }

    internal D045AttachmentContractException(
        D045AttachmentContractFailure failure,
        string field)
        : base(field)
    {
        Failure = failure;
    }
}

internal readonly struct D045AttachmentNonAuthorityClaims
{
    internal bool PlayerSelectionIsContentRead => false;
    internal bool FolderListingIsContentRead => false;
    internal bool LocalReadIsExternalShare => false;
    internal bool ScreenshotCaptureIsExternalShare => false;
    internal bool ScreenshotExternalShareIsCapture => false;
    internal bool PublicReferenceIsNetworkRetrieval => false;
    internal bool DescriptorIsExternalRequestInclusion => false;
    internal bool WorkbenchTextIsExternalApplicationInput => false;
    internal bool BytesExistenceProven => false;
    internal bool BytesAdmissionProven => false;
}

internal readonly struct D045AttachmentEgressAuthorityClaims
{
    internal bool BytesAdmitted => false;
    internal bool NetworkSubmission => false;
    internal bool ExternalEffect => false;
    internal bool RuntimeCallerPresent => false;
    internal bool ConsentEffective => false;
    internal bool ActionAuthority => false;
}

internal readonly struct D045AttachmentEgressReservationAuthorityClaims
{
    internal bool GlobalUniqueness => false;
    internal bool ExecutionConsumption => false;
    internal bool LinearizationAuthority => false;
    internal bool RuntimeCallerPresent => false;
    internal bool ExternalEffect => false;
}

internal sealed class D045AttachmentDescriptor
{
    internal const long MaximumDeclaredBytes = 8_388_608;
    internal const int MaximumUtf8TextBytes = 16_384;
    internal const int MaximumTextElements = 4_096;
    internal const long MaximumTurnLifetimeMicroseconds = 30_000_000;
    internal const long MaximumTaskLifetimeMicroseconds = 1_800_000_000;
    internal const long MaximumSessionLifetimeMicroseconds = 28_800_000_000;

    internal Guid AttachmentId { get; }
    internal D045Sha256Fingerprint ContentIdentity { get; }
    internal D045AttachmentKind Kind { get; }
    internal D045AttachmentContentForm ContentForm { get; }
    internal long DeclaredByteLength { get; }
    internal int TextElementCount { get; }
    internal D045AttachmentProvenanceKind ProvenanceKind { get; }
    internal D045Sha256Fingerprint ProvenanceFingerprint { get; }
    internal D045AttachmentLifetime Lifetime { get; }
    internal long CreatedAtMonotonicMicroseconds { get; }
    internal long RequestedLifetimeMicroseconds { get; }
    internal D045AttachmentSharingScope SharingScope { get; }
    internal D045AttachmentNonAuthorityClaims NonAuthorityClaims => default;

    internal D045AttachmentDescriptor(
        Guid attachmentId,
        D045Sha256Fingerprint contentIdentity,
        D045AttachmentKind kind,
        D045AttachmentContentForm contentForm,
        long declaredByteLength,
        int textElementCount,
        D045AttachmentProvenanceKind provenanceKind,
        D045Sha256Fingerprint provenanceFingerprint,
        D045AttachmentLifetime lifetime,
        long createdAtMonotonicMicroseconds,
        long requestedLifetimeMicroseconds,
        D045AttachmentSharingScope sharingScope)
    {
        if (attachmentId == Guid.Empty)
            Reject(D045AttachmentContractFailure.AttachmentIdentityMissing, nameof(attachmentId));
        if (contentIdentity is null || provenanceFingerprint is null)
            Reject(D045AttachmentContractFailure.DigestMissing, nameof(contentIdentity));
        if (!Enum.IsDefined(typeof(D045AttachmentKind), kind)
            || !Enum.IsDefined(typeof(D045AttachmentContentForm), contentForm)
            || provenanceKind != D045AttachmentProvenanceKind.SyntheticFixture
            || !Enum.IsDefined(typeof(D045AttachmentLifetime), lifetime)
            || !Enum.IsDefined(typeof(D045AttachmentSharingScope), sharingScope))
        {
            Reject(D045AttachmentContractFailure.ClosedValueInvalid, nameof(kind));
        }
        if (declaredByteLength <= 0 || declaredByteLength > MaximumDeclaredBytes)
            Reject(D045AttachmentContractFailure.DeclaredLengthInvalid, nameof(declaredByteLength));
        if (contentForm == D045AttachmentContentForm.Binary)
        {
            if (textElementCount != 0)
                Reject(D045AttachmentContractFailure.TextElementCountInvalid, nameof(textElementCount));
        }
        else if (textElementCount <= 0
                 || textElementCount > MaximumTextElements
                 || declaredByteLength > MaximumUtf8TextBytes
                 || declaredByteLength < textElementCount)
        {
            Reject(D045AttachmentContractFailure.TextElementCountInvalid, nameof(textElementCount));
        }
        if (createdAtMonotonicMicroseconds < 0)
            Reject(D045AttachmentContractFailure.MonotonicValueInvalid, nameof(createdAtMonotonicMicroseconds));

        RequireLifetime(lifetime, requestedLifetimeMicroseconds);
        _ = ExpiryFor(
            createdAtMonotonicMicroseconds,
            requestedLifetimeMicroseconds,
            lifetime);

        AttachmentId = attachmentId;
        ContentIdentity = contentIdentity;
        Kind = kind;
        ContentForm = contentForm;
        DeclaredByteLength = declaredByteLength;
        TextElementCount = textElementCount;
        ProvenanceKind = provenanceKind;
        ProvenanceFingerprint = provenanceFingerprint;
        Lifetime = lifetime;
        CreatedAtMonotonicMicroseconds = createdAtMonotonicMicroseconds;
        RequestedLifetimeMicroseconds = requestedLifetimeMicroseconds;
        SharingScope = sharingScope;
    }

    internal bool IsRetiredAt(
        long monotonicMicroseconds,
        D045AttachmentRetirementMarker marker)
    {
        if (monotonicMicroseconds < CreatedAtMonotonicMicroseconds
            || !Enum.IsDefined(typeof(D045AttachmentRetirementMarker), marker))
        {
            Reject(
                D045AttachmentContractFailure.MonotonicValueInvalid,
                nameof(monotonicMicroseconds));
        }

        if (MarkerRetires(Lifetime, marker))
            return true;
        if (Lifetime == D045AttachmentLifetime.CurrentMessage)
            return false;
        return monotonicMicroseconds >= ExpiryFor(
            CreatedAtMonotonicMicroseconds,
            RequestedLifetimeMicroseconds,
            Lifetime);
    }

    internal bool EqualsExact(D045AttachmentDescriptor? other)
        => other is not null
           && AttachmentId == other.AttachmentId
           && ContentIdentity.Equals(other.ContentIdentity)
           && Kind == other.Kind
           && ContentForm == other.ContentForm
           && DeclaredByteLength == other.DeclaredByteLength
           && TextElementCount == other.TextElementCount
           && ProvenanceKind == other.ProvenanceKind
           && ProvenanceFingerprint.Equals(other.ProvenanceFingerprint)
           && Lifetime == other.Lifetime
           && CreatedAtMonotonicMicroseconds
              == other.CreatedAtMonotonicMicroseconds
           && RequestedLifetimeMicroseconds
              == other.RequestedLifetimeMicroseconds
           && SharingScope == other.SharingScope;

    public override string ToString()
        => "D045AttachmentDescriptor(content-free)";

    private static void RequireLifetime(
        D045AttachmentLifetime lifetime,
        long requestedLifetimeMicroseconds)
    {
        var valid = lifetime switch
        {
            D045AttachmentLifetime.CurrentMessage
                => requestedLifetimeMicroseconds == 0,
            D045AttachmentLifetime.CurrentTurn
                => requestedLifetimeMicroseconds is > 0
                    and <= MaximumTurnLifetimeMicroseconds,
            D045AttachmentLifetime.CurrentTask
                => requestedLifetimeMicroseconds is > 0
                    and <= MaximumTaskLifetimeMicroseconds,
            D045AttachmentLifetime.CurrentSession
                => requestedLifetimeMicroseconds is > 0
                    and <= MaximumSessionLifetimeMicroseconds,
            _ => false
        };
        if (!valid)
            Reject(D045AttachmentContractFailure.LifetimeInvalid, nameof(lifetime));
    }

    private static long ExpiryFor(
        long createdAt,
        long duration,
        D045AttachmentLifetime lifetime)
    {
        if (lifetime == D045AttachmentLifetime.CurrentMessage)
            return long.MaxValue;
        try
        {
            return checked(createdAt + duration);
        }
        catch (OverflowException)
        {
            Reject(D045AttachmentContractFailure.MonotonicValueInvalid, nameof(duration));
            return 0;
        }
    }

    private static bool MarkerRetires(
        D045AttachmentLifetime lifetime,
        D045AttachmentRetirementMarker marker)
        => lifetime switch
        {
            D045AttachmentLifetime.CurrentMessage => marker is
                D045AttachmentRetirementMarker.MessageSent
                or D045AttachmentRetirementMarker.MessageCancelled
                or D045AttachmentRetirementMarker.TurnClosed
                or D045AttachmentRetirementMarker.TaskClosed
                or D045AttachmentRetirementMarker.SessionClosed,
            D045AttachmentLifetime.CurrentTurn => marker is
                D045AttachmentRetirementMarker.TurnClosed
                or D045AttachmentRetirementMarker.TaskClosed
                or D045AttachmentRetirementMarker.SessionClosed,
            D045AttachmentLifetime.CurrentTask => marker is
                D045AttachmentRetirementMarker.TaskClosed
                or D045AttachmentRetirementMarker.SessionClosed,
            D045AttachmentLifetime.CurrentSession => marker is
                D045AttachmentRetirementMarker.SessionClosed,
            _ => true
        };

    [DoesNotReturn]
    private static void Reject(
        D045AttachmentContractFailure failure,
        string field)
        => throw new D045AttachmentContractException(failure, field);
}

internal sealed class D045WorkbenchMessageDescriptor
{
    internal const int MaximumMessageTextElements = 4_096;
    internal const int MaximumMessageUtf8Bytes = 16_384;
    internal const int MaximumAttachmentDescriptors = 8;
    internal const long MaximumAggregateAttachmentBytes = 16_777_216;

    private readonly ReadOnlyCollection<D045AttachmentDescriptor> _attachments;

    internal int TextElementCount { get; }
    internal int Utf8ByteLength { get; }
    internal IReadOnlyList<D045AttachmentDescriptor> Attachments => _attachments;
    internal long AggregateDeclaredAttachmentBytes { get; }
    internal D045AttachmentNonAuthorityClaims NonAuthorityClaims => default;

    internal D045WorkbenchMessageDescriptor(
        int textElementCount,
        int utf8ByteLength,
        IReadOnlyList<D045AttachmentDescriptor>? attachments)
    {
        if (textElementCount < 0
            || textElementCount > MaximumMessageTextElements
            || utf8ByteLength < 0
            || utf8ByteLength > MaximumMessageUtf8Bytes
            || (textElementCount == 0) != (utf8ByteLength == 0)
            || utf8ByteLength < textElementCount)
        {
            Reject(D045AttachmentContractFailure.MessageShapeInvalid, nameof(textElementCount));
        }
        if (attachments is null || attachments.Count > MaximumAttachmentDescriptors)
            Reject(D045AttachmentContractFailure.DescriptorCapacityExceeded, nameof(attachments));

        var copy = new D045AttachmentDescriptor[attachments.Count];
        var ids = new HashSet<Guid>();
        long aggregate = 0;
        for (var index = 0; index < attachments.Count; index++)
        {
            var descriptor = attachments[index];
            if (descriptor is null)
                Reject(D045AttachmentContractFailure.ClosedValueInvalid, nameof(attachments));
            if (!ids.Add(descriptor.AttachmentId))
                Reject(D045AttachmentContractFailure.DuplicateAttachmentIdentity, nameof(attachments));
            try
            {
                aggregate = checked(aggregate + descriptor.DeclaredByteLength);
            }
            catch (OverflowException)
            {
                Reject(D045AttachmentContractFailure.AggregateLengthExceeded, nameof(attachments));
            }
            if (aggregate > MaximumAggregateAttachmentBytes)
                Reject(D045AttachmentContractFailure.AggregateLengthExceeded, nameof(attachments));
            copy[index] = descriptor;
        }

        TextElementCount = textElementCount;
        Utf8ByteLength = utf8ByteLength;
        _attachments = Array.AsReadOnly(copy);
        AggregateDeclaredAttachmentBytes = aggregate;
    }

    public override string ToString()
        => "D045WorkbenchMessageDescriptor(content-free)";

    [DoesNotReturn]
    private static void Reject(
        D045AttachmentContractFailure failure,
        string field)
        => throw new D045AttachmentContractException(failure, field);
}

internal sealed class D045AttachmentEgressPreview
{
    internal D045AttachmentDescriptor Attachment { get; }
    internal D045AttachmentEgressDestinationKind DestinationKind { get; }
    internal D045Sha256Fingerprint DestinationFingerprint { get; }
    internal D045Sha256Fingerprint PurposeFingerprint { get; }
    internal D045AuthorityCapability Capability { get; }
    internal D045Sha256Fingerprint TargetScopeFingerprint { get; }
    internal Guid ConsentGenerationId { get; }
    internal D045Sha256Fingerprint CanonicalPreviewFingerprint { get; }

    internal D045AttachmentEgressPreview(
        D045AttachmentDescriptor attachment,
        D045AttachmentEgressDestinationKind destinationKind,
        D045Sha256Fingerprint destinationFingerprint,
        D045Sha256Fingerprint purposeFingerprint,
        D045AuthorityCapability capability,
        D045Sha256Fingerprint targetScopeFingerprint,
        Guid consentGenerationId)
    {
        if (attachment is null
            || destinationFingerprint is null
            || purposeFingerprint is null
            || targetScopeFingerprint is null
            || consentGenerationId == Guid.Empty)
        {
            Reject(
                D045AttachmentContractFailure.EgressBindingInvalid,
                nameof(attachment));
        }
        if (!Enum.IsDefined(
                typeof(D045AttachmentEgressDestinationKind),
                destinationKind))
        {
            Reject(
                D045AttachmentContractFailure.EgressDestinationInvalid,
                nameof(destinationKind));
        }

        RequireExactEgressMapping(attachment, destinationKind, capability);

        Attachment = attachment;
        DestinationKind = destinationKind;
        DestinationFingerprint = destinationFingerprint;
        PurposeFingerprint = purposeFingerprint;
        Capability = capability;
        TargetScopeFingerprint = targetScopeFingerprint;
        ConsentGenerationId = consentGenerationId;
        CanonicalPreviewFingerprint =
            D045AttachmentEgressCanonicalIdentity.ComputePreview(this);
    }

    internal bool EqualsExact(D045AttachmentEgressPreview? other)
        => other is not null
           && Attachment.EqualsExact(other.Attachment)
           && DestinationKind == other.DestinationKind
           && DestinationFingerprint == other.DestinationFingerprint
           && PurposeFingerprint == other.PurposeFingerprint
           && Capability == other.Capability
           && TargetScopeFingerprint == other.TargetScopeFingerprint
           && ConsentGenerationId == other.ConsentGenerationId
           && CanonicalPreviewFingerprint
               == other.CanonicalPreviewFingerprint;

    public override string ToString()
        => "D045AttachmentEgressPreview(content-free)";

    private static void RequireExactEgressMapping(
        D045AttachmentDescriptor attachment,
        D045AttachmentEgressDestinationKind destinationKind,
        D045AuthorityCapability capability)
    {
        if (!D045AuthorityTaxonomy.TryGetRow(capability, out var row)
            || row is null)
        {
            Reject(
                D045AttachmentContractFailure.EgressCapabilityUnsupported,
                nameof(capability));
        }
        if (row.ConstitutionAmendmentRequired)
        {
            Reject(
                D045AttachmentContractFailure
                    .ConstitutionAmendmentRequired,
                nameof(capability));
        }

        var capabilityIsExact = attachment.Kind switch
        {
            D045AttachmentKind.Screenshot =>
                capability == D045AuthorityCapability.ScreenshotUpload,
            D045AttachmentKind.LocalFile
                or D045AttachmentKind.LocalFolder
                or D045AttachmentKind.ClipboardSnapshot
                or D045AttachmentKind.PastedImage
                or D045AttachmentKind.PublicHttpsUrlReference
                or D045AttachmentKind.GeneratedArtifact =>
                    capability
                        == D045AuthorityCapability.LocalContentExternalShare,
            _ => false
        };
        if (!capabilityIsExact)
        {
            Reject(
                D045AttachmentContractFailure.EgressCapabilityUnsupported,
                nameof(capability));
        }

        var contentFormIsExact = attachment.Kind switch
        {
            D045AttachmentKind.LocalFile => attachment.ContentForm is
                D045AttachmentContentForm.Binary
                or D045AttachmentContentForm.Utf8Text,
            D045AttachmentKind.LocalFolder => attachment.ContentForm
                == D045AttachmentContentForm.Utf8Text,
            D045AttachmentKind.ClipboardSnapshot => attachment.ContentForm is
                D045AttachmentContentForm.Binary
                or D045AttachmentContentForm.Utf8Text,
            D045AttachmentKind.Screenshot => attachment.ContentForm
                == D045AttachmentContentForm.Binary,
            D045AttachmentKind.PastedImage => attachment.ContentForm
                == D045AttachmentContentForm.Binary,
            D045AttachmentKind.PublicHttpsUrlReference => attachment.ContentForm
                == D045AttachmentContentForm.Utf8Text,
            D045AttachmentKind.GeneratedArtifact => attachment.ContentForm is
                D045AttachmentContentForm.Binary
                or D045AttachmentContentForm.Utf8Text,
            _ => false
        };
        if (!contentFormIsExact)
        {
            Reject(
                D045AttachmentContractFailure.EgressDestinationInvalid,
                nameof(attachment));
        }

        var destinationIsExact = attachment.SharingScope switch
        {
            D045AttachmentSharingScope.ProviderTextRequestCandidate =>
                destinationKind
                    == D045AttachmentEgressDestinationKind
                        .ProviderTextRequest
                && attachment.ContentForm
                    == D045AttachmentContentForm.Utf8Text
                && attachment.Kind is D045AttachmentKind.LocalFile
                    or D045AttachmentKind.LocalFolder
                    or D045AttachmentKind.ClipboardSnapshot
                    or D045AttachmentKind.PublicHttpsUrlReference
                    or D045AttachmentKind.GeneratedArtifact,
            D045AttachmentSharingScope.ExactExternalRecipientCandidate =>
                destinationKind
                    == D045AttachmentEgressDestinationKind
                        .ExactExternalRecipient
                && attachment.Kind is D045AttachmentKind.LocalFile
                    or D045AttachmentKind.LocalFolder
                    or D045AttachmentKind.ClipboardSnapshot
                    or D045AttachmentKind.Screenshot
                    or D045AttachmentKind.PastedImage
                    or D045AttachmentKind.PublicHttpsUrlReference
                    or D045AttachmentKind.GeneratedArtifact,
            _ => false
        };
        if (!destinationIsExact)
        {
            Reject(
                D045AttachmentContractFailure.EgressDestinationInvalid,
                nameof(destinationKind));
        }
    }

    [DoesNotReturn]
    private static void Reject(
        D045AttachmentContractFailure failure,
        string field)
        => throw new D045AttachmentContractException(failure, field);
}

internal sealed class D045AttachmentEgressReservationResult
{
    internal D045ConsentReplayResult? PriorReplay { get; }
    internal D045ConsentReplayResult? UpdatedReplay { get; }
    internal D045AttachmentEgressCandidate? Candidate { get; }
    internal D045AttachmentEgressReservationFailure Failure { get; }
    internal bool Reserved =>
        Failure == D045AttachmentEgressReservationFailure.None
        && Candidate is not null
        && UpdatedReplay is not null;
    internal bool PartialMutation => false;
    internal D045AttachmentEgressReservationAuthorityClaims AuthorityClaims =>
        default;

    private D045AttachmentEgressReservationResult(
        object transitionSeal,
        D045ConsentReplayResult? priorReplay,
        D045ConsentReplayResult? updatedReplay,
        D045AttachmentEgressCandidate? candidate,
        D045AttachmentEgressReservationFailure failure)
    {
        if (!D045AttachmentEgressCandidate.HasReservationTransitionSeal(
                transitionSeal))
        {
            throw new ArgumentException(
                "A validated reservation transition is required.",
                nameof(transitionSeal));
        }

        PriorReplay = priorReplay;
        UpdatedReplay = updatedReplay;
        Candidate = candidate;
        Failure = failure;
    }

    internal static D045AttachmentEgressReservationResult Accept(
        object transitionSeal,
        D045ConsentReplayResult priorReplay,
        D045ConsentReplayResult updatedReplay,
        D045AttachmentEgressCandidate candidate)
        => new(
            transitionSeal,
            priorReplay,
            updatedReplay,
            candidate,
            D045AttachmentEgressReservationFailure.None);

    internal static D045AttachmentEgressReservationResult Reject(
        object transitionSeal,
        D045ConsentReplayResult? replay,
        D045AttachmentEgressReservationFailure failure)
        => new(
            transitionSeal,
            replay,
            replay,
            null,
            failure);

    public override string ToString()
        => $"D045AttachmentEgressReservationResult({Failure}; no effect)";
}

internal sealed class D045AttachmentEgressCandidate
{
    private sealed class ReservationTransitionSeal
    {
    }

    internal const long MaximumOneActionLifetimeMicroseconds = 30_000_000;

    internal D045AttachmentEgressPreview Preview { get; }
    internal D045AttachmentDescriptor Attachment => Preview.Attachment;
    internal Guid OwnerId { get; }
    internal Guid ProfileId { get; }
    internal Guid SessionId { get; }
    internal Guid TaskId { get; }
    internal Guid ConsentGenerationId { get; }
    internal Guid OneActionRequestId { get; }
    internal Guid OneActionCandidateId { get; }
    internal D045AuthorityCapability Capability => Preview.Capability;
    internal D045AttachmentEgressDestinationKind DestinationKind =>
        Preview.DestinationKind;
    internal D045Sha256Fingerprint TargetScopeFingerprint =>
        Preview.TargetScopeFingerprint;
    internal D045Sha256Fingerprint DestinationFingerprint =>
        Preview.DestinationFingerprint;
    internal D045Sha256Fingerprint PurposeFingerprint =>
        Preview.PurposeFingerprint;
    internal D045Sha256Fingerprint PreviewFingerprint =>
        Preview.CanonicalPreviewFingerprint;
    internal D045Sha256Fingerprint ReservationIdentity { get; }
    internal D045Sha256Fingerprint CandidateIdentity { get; }
    internal int AcceptedReplaySequenceNumber { get; }
    internal long AcceptedReplayMonotonicMicroseconds { get; }
    internal D045AttachmentEgressAuthorityClaims AuthorityClaims => default;
    internal D045AttachmentEgressReservationAuthorityClaims ReservationClaims =>
        default;

    private D045AttachmentEgressCandidate(
        object transitionSeal,
        D045AttachmentEgressPreview preview,
        D045OneActionConsentProjection binding,
        D045Sha256Fingerprint reservationIdentity,
        D045Sha256Fingerprint candidateIdentity,
        int acceptedReplaySequenceNumber,
        long acceptedReplayMonotonicMicroseconds)
    {
        if (!HasReservationTransitionSeal(transitionSeal))
        {
            throw new ArgumentException(
                "A validated reservation transition is required.",
                nameof(transitionSeal));
        }

        Preview = preview;
        OwnerId = binding.OwnerId;
        ProfileId = binding.ProfileId;
        SessionId = binding.SessionId;
        TaskId = binding.TaskId;
        ConsentGenerationId = binding.ConsentGenerationId;
        OneActionRequestId = binding.RequestId;
        OneActionCandidateId = binding.CandidateId;
        ReservationIdentity = reservationIdentity;
        CandidateIdentity = candidateIdentity;
        AcceptedReplaySequenceNumber = acceptedReplaySequenceNumber;
        AcceptedReplayMonotonicMicroseconds =
            acceptedReplayMonotonicMicroseconds;
    }

    internal static bool HasReservationTransitionSeal(object? transitionSeal)
        => transitionSeal is ReservationTransitionSeal;

    internal static D045AttachmentEgressReservationResult TryReserve(
        D045ConsentReplayResult? replay,
        Guid oneActionRequestId,
        D045AttachmentEgressPreview? acceptedPreview,
        D045AttachmentEgressPreview? presentedPreview,
        long evaluatedAtMonotonicMicroseconds,
        D045AttachmentRetirementMarker retirementMarker)
    {
        var transitionSeal = new ReservationTransitionSeal();
        if (replay is null || !replay.Completed)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure.ReplayResultInvalid);
        }
        if (!replay.HasReplayProvenance)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure
                    .ReplayProvenanceInvalid);
        }
        if (acceptedPreview is null)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure
                    .AcceptedPreviewMissing);
        }
        if (presentedPreview is null)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure
                    .PresentedPreviewMissing);
        }
        if (!acceptedPreview.EqualsExact(presentedPreview))
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure.PreviewSubstitution);
        }
        if (oneActionRequestId == Guid.Empty
            || !replay.TryGetOneActionBinding(
                oneActionRequestId,
                out var binding)
            || binding is null)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure
                    .OneActionBindingNotFound);
        }

        if (!binding.IsReplayProof
            || binding.OwnerId != replay.OwnerId
            || binding.ProfileId != replay.ProfileId
            || binding.ConsentGenerationId
                != replay.CurrentConsentGenerationId
            || binding.RequestId != oneActionRequestId
            || binding.Consumed
            || binding.Revoked
            || !binding.IsEligibleForExactBindingAt(
                evaluatedAtMonotonicMicroseconds)
            || binding.Request.RequestedLifetimeMicroseconds <= 0
            || binding.Request.RequestedLifetimeMicroseconds
                > MaximumOneActionLifetimeMicroseconds
            || acceptedPreview.ConsentGenerationId
                != binding.ConsentGenerationId
            || acceptedPreview.Capability != binding.Capability
            || acceptedPreview.TargetScopeFingerprint
                != binding.TargetScopeFingerprint
            || acceptedPreview.PurposeFingerprint
                != binding.PurposeFingerprint
            || acceptedPreview.CanonicalPreviewFingerprint
                != binding.PreviewFingerprint
            || evaluatedAtMonotonicMicroseconds
                < replay.LastAcceptedMonotonicMicroseconds)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure
                    .OneActionBindingInvalid);
        }

        if (!Enum.IsDefined(
                typeof(D045AttachmentRetirementMarker),
                retirementMarker)
            || evaluatedAtMonotonicMicroseconds
                < acceptedPreview.Attachment
                    .CreatedAtMonotonicMicroseconds
            || acceptedPreview.Attachment.IsRetiredAt(
                evaluatedAtMonotonicMicroseconds,
                retirementMarker))
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                D045AttachmentEgressReservationFailure.AttachmentRetired);
        }

        var reservationIdentity =
            D045AttachmentEgressCanonicalIdentity.ComputeReservation(
                replay,
                binding,
                acceptedPreview);
        var candidateIdentity =
            D045AttachmentEgressCanonicalIdentity.ComputeCandidate(
                reservationIdentity,
                binding.CandidateId);
        var reservation = new D045AttachmentEgressReservationRecord(
            transitionSeal,
            binding.OwnerId,
            binding.ProfileId,
            binding.ConsentGenerationId,
            binding.RequestId,
            binding.CandidateId,
            binding.TaskId,
            binding.SessionId,
            replay.LastAcceptedSequenceNumber,
            replay.LastAcceptedMonotonicMicroseconds,
            reservationIdentity,
            candidateIdentity);
        var transitionFailure = replay.TryApplyAttachmentEgressReservation(
            transitionSeal,
            reservation,
            out var updatedReplay);
        if (transitionFailure != D045AttachmentEgressReservationFailure.None)
        {
            return D045AttachmentEgressReservationResult.Reject(
                transitionSeal,
                replay,
                transitionFailure);
        }

        var candidate = new D045AttachmentEgressCandidate(
            transitionSeal,
            acceptedPreview,
            binding,
            reservationIdentity,
            candidateIdentity,
            replay.LastAcceptedSequenceNumber,
            replay.LastAcceptedMonotonicMicroseconds);
        return D045AttachmentEgressReservationResult.Accept(
            transitionSeal,
            replay,
            updatedReplay,
            candidate);
    }

    internal bool MatchesPreview(D045AttachmentEgressPreview? preview)
        => Preview.EqualsExact(preview);

    public override string ToString()
        => "D045AttachmentEgressCandidate(content-free; no-submission)";
}

internal static class D045AttachmentEgressCanonicalIdentity
{
    internal static D045Sha256Fingerprint ComputePreview(
        D045AttachmentEgressPreview preview)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDomain(hash, kind: 1);
        AppendGuid(hash, preview.Attachment.AttachmentId);
        AppendFingerprint(hash, preview.Attachment.ContentIdentity);
        AppendInt32(hash, (int)preview.Attachment.Kind);
        AppendInt32(hash, (int)preview.Attachment.ContentForm);
        AppendInt64(hash, preview.Attachment.DeclaredByteLength);
        AppendInt32(hash, preview.Attachment.TextElementCount);
        AppendInt32(hash, (int)preview.Attachment.ProvenanceKind);
        AppendFingerprint(hash, preview.Attachment.ProvenanceFingerprint);
        AppendInt32(hash, (int)preview.Attachment.Lifetime);
        AppendInt64(
            hash,
            preview.Attachment.CreatedAtMonotonicMicroseconds);
        AppendInt64(
            hash,
            preview.Attachment.RequestedLifetimeMicroseconds);
        AppendInt32(hash, (int)preview.Attachment.SharingScope);
        AppendInt32(hash, (int)preview.DestinationKind);
        AppendFingerprint(hash, preview.DestinationFingerprint);
        AppendFingerprint(hash, preview.PurposeFingerprint);
        AppendInt32(hash, (int)preview.Capability);
        AppendFingerprint(hash, preview.TargetScopeFingerprint);
        AppendGuid(hash, preview.ConsentGenerationId);
        return D045Sha256Fingerprint.Create(hash.GetHashAndReset());
    }

    internal static D045Sha256Fingerprint ComputeReservation(
        D045ConsentReplayResult replay,
        D045OneActionConsentProjection binding,
        D045AttachmentEgressPreview preview)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDomain(hash, kind: 2);
        AppendGuid(hash, binding.OwnerId);
        AppendGuid(hash, binding.ProfileId);
        AppendGuid(hash, binding.ConsentGenerationId);
        AppendGuid(hash, binding.RequestId);
        AppendGuid(hash, binding.CandidateId);
        AppendGuid(hash, binding.TaskId);
        AppendGuid(hash, binding.SessionId);
        AppendInt32(hash, replay.LastAcceptedSequenceNumber);
        AppendInt64(hash, replay.LastAcceptedMonotonicMicroseconds);
        AppendFingerprint(hash, preview.CanonicalPreviewFingerprint);
        return D045Sha256Fingerprint.Create(hash.GetHashAndReset());
    }

    internal static D045Sha256Fingerprint ComputeCandidate(
        D045Sha256Fingerprint reservationIdentity,
        Guid oneActionCandidateId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendDomain(hash, kind: 3);
        AppendFingerprint(hash, reservationIdentity);
        AppendGuid(hash, oneActionCandidateId);
        return D045Sha256Fingerprint.Create(hash.GetHashAndReset());
    }

    private static void AppendDomain(IncrementalHash hash, int kind)
    {
        AppendInt32(hash, 0x44303435);
        AppendInt32(hash, 1);
        AppendInt32(hash, kind);
    }

    private static void AppendGuid(IncrementalHash hash, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes))
            throw new InvalidOperationException("Guid encoding failed.");
        hash.AppendData(bytes);
    }

    private static void AppendFingerprint(
        IncrementalHash hash,
        D045Sha256Fingerprint fingerprint)
    {
        var bytes = fingerprint.CopyBytes();
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
