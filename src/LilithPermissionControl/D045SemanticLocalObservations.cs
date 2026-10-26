using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithPermissionControl;

/// <summary>
/// Provider-neutral, read-only questions which the local host may answer.  The
/// model can select a query kind, but it cannot select or widen its permission
/// purpose; the host supplies that independently.
/// </summary>
internal enum D045SemanticLocalQueryKind
{
    ResolveLocalTarget = 1,
    InspectLocalItem = 2,
    QueryApplication = 3
}

internal enum D045SemanticLocalPurpose
{
    ReadLocalFile = 1,
    EnumerateLocalFolder = 2,
    LaunchApplication = 3,
    OpenLocalContent = 4
}

internal enum D045SemanticLocalTargetKind
{
    File = 1,
    Directory = 2,
    Application = 3
}

internal enum D045SemanticObservationAdmissionFailure
{
    None = 0,
    UnknownQueryKind = 1,
    UnknownPurpose = 2,
    QueryPurposeMismatch = 3,
    QueryMissing = 4,
    QueryInvalid = 5,
    PermissionStateUnavailable = 6,
    PermissionDefaultOff = 7,
    PermissionRevisionChanged = 8
}

internal enum D045SemanticObservationCreationFailure
{
    None = 0,
    InvalidSession = 1,
    InvalidTask = 2,
    InvalidTimestamp = 3,
    CandidateLimitExceeded = 4,
    CandidateInvalid = 5,
    CandidateKindMismatch = 6,
    DuplicateCandidate = 7,
    RegistryCapacityReached = 8,
    OpaqueReferenceCollision = 9
}

internal enum D045SemanticTargetReferenceFailure
{
    None = 0,
    ReferenceMissing = 1,
    ReferenceNotFound = 2,
    ReferenceExpired = 3,
    ReferenceAlreadyConsumed = 4,
    WrongSession = 5,
    WrongTask = 6,
    WrongQueryKind = 7,
    WrongPurpose = 8,
    WrongCapability = 9,
    WrongPermissionRevision = 10,
    PermissionStateUnavailable = 11,
    PermissionDefaultOff = 12,
    WrongTargetKind = 13,
    WrongTargetFingerprint = 14,
    TargetChanged = 15,
    UnsafeTargetRejected = 16,
    ApplicationBlocked = 17
}

internal enum D045SemanticObservationExecutionDisposition
{
    ObservedUnique = 1,
    TargetNotFound = 2,
    TargetAmbiguous = 3,
    UnsafeTargetRejected = 4,
    ApplicationBlocked = 5,
    ApplicationFixedArgumentsRejected = 6,
    AdmissionRejected = 7,
    ObservationCreationRejected = 8
}

internal enum D045SemanticReceiptKind
{
    Observation = 1
}

internal sealed class D045SemanticObservationAdmission
{
    internal D045SemanticLocalQueryKind QueryKind { get; }
    internal D045SemanticLocalPurpose Purpose { get; }
    internal D045PermissionCapability Capability { get; }
    internal long PermissionRevision { get; }
    internal string QueryFingerprint { get; }

    internal D045SemanticObservationAdmission(
        D045SemanticLocalQueryKind queryKind,
        D045SemanticLocalPurpose purpose,
        D045PermissionCapability capability,
        long permissionRevision,
        string queryFingerprint)
    {
        if (!Enum.IsDefined(typeof(D045SemanticLocalQueryKind), queryKind))
            throw new ArgumentOutOfRangeException(nameof(queryKind));
        if (!Enum.IsDefined(typeof(D045SemanticLocalPurpose), purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (!D045SemanticLocalObservationPolicy.PurposeSupportsQuery(
                purpose,
                queryKind))
        {
            throw new ArgumentException(
                "The semantic query is not valid for the host-owned purpose.",
                nameof(queryKind));
        }
        if (D045SemanticLocalObservationPolicy.CapabilityFor(purpose)
            != capability)
        {
            throw new ArgumentException(
                "The capability must be the purpose's single backing permission row.",
                nameof(capability));
        }
        if (permissionRevision <= 0)
            throw new ArgumentOutOfRangeException(nameof(permissionRevision));
        if (!D045SemanticObservationFingerprint.IsSha256(queryFingerprint))
            throw new ArgumentException(
                "A canonical query fingerprint is required.",
                nameof(queryFingerprint));
        QueryKind = queryKind;
        Purpose = purpose;
        Capability = capability;
        PermissionRevision = permissionRevision;
        QueryFingerprint = queryFingerprint;
    }
}

/// <summary>
/// The only authority mapper for semantic local observations.  Every purpose
/// maps to exactly one existing D-045 permission row; no new permission or
/// aggregate/wildcard capability is introduced here.
/// </summary>
internal static class D045SemanticLocalObservationPolicy
{
    internal const int MaximumQueryCharacters = 512;

    internal static D045PermissionCapability CapabilityFor(
        D045SemanticLocalPurpose purpose)
        => purpose switch
        {
            D045SemanticLocalPurpose.ReadLocalFile =>
                D045PermissionCapability.LocalFileRead,
            D045SemanticLocalPurpose.EnumerateLocalFolder =>
                D045PermissionCapability.LocalFolderEnumerate,
            D045SemanticLocalPurpose.LaunchApplication =>
                D045PermissionCapability.ApplicationLaunch,
            D045SemanticLocalPurpose.OpenLocalContent =>
                D045PermissionCapability.LocalContentOpen,
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };

    internal static bool TryAdmit(
        D045SemanticLocalQueryKind queryKind,
        D045SemanticLocalPurpose hostOwnedPurpose,
        string? query,
        D045PermissionLoadResult? permissionState,
        out D045SemanticObservationAdmission? admission,
        out D045SemanticObservationAdmissionFailure failure)
    {
        admission = null;
        failure = D045SemanticObservationAdmissionFailure.None;

        if (!Enum.IsDefined(typeof(D045SemanticLocalQueryKind), queryKind))
        {
            failure = D045SemanticObservationAdmissionFailure.UnknownQueryKind;
            return false;
        }

        if (!Enum.IsDefined(typeof(D045SemanticLocalPurpose), hostOwnedPurpose))
        {
            failure = D045SemanticObservationAdmissionFailure.UnknownPurpose;
            return false;
        }

        if (!PurposeSupportsQuery(hostOwnedPurpose, queryKind))
        {
            failure = D045SemanticObservationAdmissionFailure.QueryPurposeMismatch;
            return false;
        }

        var normalizedQuery = NormalizeQuery(query);
        if (queryKind == D045SemanticLocalQueryKind.QueryApplication
            && normalizedQuery.Length == 0)
        {
            failure = D045SemanticObservationAdmissionFailure.QueryMissing;
            return false;
        }

        if (normalizedQuery.Length > MaximumQueryCharacters
            || normalizedQuery.Any(char.IsControl))
        {
            failure = D045SemanticObservationAdmissionFailure.QueryInvalid;
            return false;
        }

        if (!TryGetCurrentSnapshot(permissionState, out var snapshot))
        {
            failure =
                D045SemanticObservationAdmissionFailure.PermissionStateUnavailable;
            return false;
        }

        var capability = CapabilityFor(hostOwnedPurpose);
        if (!snapshot.IsEnabled(capability))
        {
            failure = D045SemanticObservationAdmissionFailure.PermissionDefaultOff;
            return false;
        }

        admission = new D045SemanticObservationAdmission(
            queryKind,
            hostOwnedPurpose,
            capability,
            snapshot.Revision,
            D045SemanticObservationFingerprint.Of(
                "D045.SEMANTIC.QUERY.V1",
                ((int)queryKind).ToString(CultureInfo.InvariantCulture),
                ((int)hostOwnedPurpose).ToString(CultureInfo.InvariantCulture),
                normalizedQuery));
        return true;
    }

    internal static bool PurposeSupportsQuery(
        D045SemanticLocalPurpose purpose,
        D045SemanticLocalQueryKind queryKind)
        => queryKind switch
        {
            D045SemanticLocalQueryKind.ResolveLocalTarget =>
                purpose is D045SemanticLocalPurpose.ReadLocalFile
                    or D045SemanticLocalPurpose.EnumerateLocalFolder
                    or D045SemanticLocalPurpose.LaunchApplication
                    or D045SemanticLocalPurpose.OpenLocalContent,
            D045SemanticLocalQueryKind.InspectLocalItem =>
                purpose is D045SemanticLocalPurpose.ReadLocalFile
                    or D045SemanticLocalPurpose.EnumerateLocalFolder,
            D045SemanticLocalQueryKind.QueryApplication =>
                purpose == D045SemanticLocalPurpose.LaunchApplication,
            _ => false
        };

    internal static bool PurposeSupportsTargetKind(
        D045SemanticLocalPurpose purpose,
        D045SemanticLocalTargetKind targetKind)
        => purpose switch
        {
            D045SemanticLocalPurpose.ReadLocalFile =>
                targetKind == D045SemanticLocalTargetKind.File,
            D045SemanticLocalPurpose.EnumerateLocalFolder =>
                targetKind == D045SemanticLocalTargetKind.Directory,
            D045SemanticLocalPurpose.LaunchApplication =>
                targetKind == D045SemanticLocalTargetKind.Application,
            D045SemanticLocalPurpose.OpenLocalContent =>
                targetKind is D045SemanticLocalTargetKind.File
                    or D045SemanticLocalTargetKind.Directory,
            _ => false
        };

    internal static bool TryGetCurrentSnapshot(
        D045PermissionLoadResult? permissionState,
        out D045PermissionControlSnapshot snapshot)
    {
        snapshot = permissionState?.Snapshot
            ?? new D045PermissionControlSnapshot();
        return permissionState is not null
            && Enum.IsDefined(
                typeof(D045PermissionStoreDisposition),
                permissionState.Disposition)
            && permissionState.Disposition
                != D045PermissionStoreDisposition.InvalidDefaultOff
            && snapshot.SchemaVersion
                == D045PermissionControlSnapshot.CurrentSchemaVersion
            && snapshot.Revision > 0
            && snapshot.Enabled is not null
            && snapshot.Enabled.Length
                == D045PermissionControlSnapshot.ExactCapabilityCount
            && (permissionState.Disposition
                    != D045PermissionStoreDisposition.MissingDefaultOff
                || !snapshot.AnyEnabled);
    }

    private static string NormalizeQuery(string? query)
        => (query ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
}

/// <summary>
/// Host-only input.  A canonical path exists only while the in-memory target
/// registry is alive; it is never included in a provider observation receipt.
/// </summary>
internal sealed class D045SemanticLocalCandidateInput
{
    internal string CanonicalPath { get; }
    internal D045SemanticLocalTargetKind Kind { get; }
    internal string TargetFingerprint { get; }
    internal string HostIdentityFingerprint { get; }
    internal string HarnessTargetFingerprint { get; }
    internal string SafeDisplayLeaf { get; }
    internal string SafeExtension { get; }
    internal string DisplayLeafFingerprint { get; }

    internal D045SemanticLocalCandidateInput(
        string canonicalPath,
        D045SemanticLocalTargetKind kind)
    {
        if (string.IsNullOrWhiteSpace(canonicalPath))
            throw new ArgumentException("A local canonical path is required.", nameof(canonicalPath));
        if (!Enum.IsDefined(typeof(D045SemanticLocalTargetKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));

        var fullPath = Path.GetFullPath(canonicalPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Normalize(NormalizationForm.FormC);
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("A fully-qualified local path is required.", nameof(canonicalPath));

        var rawLeaf = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(rawLeaf))
            throw new ArgumentException("The local target must have a leaf name.", nameof(canonicalPath));

        CanonicalPath = fullPath;
        Kind = kind;
        HostIdentityFingerprint = D045HarnessHostTargetIdentity.Capture(
            fullPath,
            (D045ExpectedTargetKind)(int)kind);
        TargetFingerprint = D045SemanticObservationFingerprint.Of(
            "D045.SEMANTIC.TARGET.V1",
            fullPath.ToUpperInvariant());
        HarnessTargetFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fullPath + "\n")));
        SafeDisplayLeaf = SafeLeaf(rawLeaf);
        SafeExtension = SafeExtensionOf(rawLeaf, kind);
        DisplayLeafFingerprint = D045SemanticObservationFingerprint.Of(
            "D045.SEMANTIC.DISPLAY.V1",
            SafeDisplayLeaf.ToUpperInvariant());
    }

    private static string SafeLeaf(string rawLeaf)
    {
        var builder = new StringBuilder(Math.Min(rawLeaf.Length, 96));
        foreach (var character in rawLeaf.Normalize(NormalizationForm.FormC))
        {
            if (builder.Length == 96)
                break;
            builder.Append(char.IsControl(character)
                || character == Path.DirectorySeparatorChar
                || character == Path.AltDirectorySeparatorChar
                    ? '_'
                    : character);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? "local-item" : result;
    }

    private static string SafeExtensionOf(
        string rawLeaf,
        D045SemanticLocalTargetKind kind)
    {
        if (kind == D045SemanticLocalTargetKind.Directory)
            return string.Empty;

        var extension = Path.GetExtension(rawLeaf).ToLowerInvariant();
        if (extension.Length is < 2 or > 17)
            return string.Empty;
        return extension.Skip(1).All(character =>
                D045SemanticObservationFingerprint.IsAsciiLetterOrDigit(character)
                || character is '_' or '-')
            ? extension
            : string.Empty;
    }
}

/// <summary>
/// The provider-visible target projection.  It deliberately has no local path
/// property and cannot itself authorize or prove an operating-system effect.
/// </summary>
internal sealed class D045SemanticObservationCandidate
{
    internal string OpaqueTargetReference { get; }
    internal string SafeDisplayLeaf { get; }
    internal D045SemanticLocalTargetKind Kind { get; }
    internal string SafeExtension { get; }
    internal string TargetFingerprint { get; }
    internal string DisplayLeafFingerprint { get; }
    // Host-only compatibility identity for the existing Harness changed-target
    // retry gate.  Provider projection/ToString/observation hashes omit it.
    internal string HarnessTargetFingerprint { get; }
    internal bool IsRedeemable => OpaqueTargetReference.Length != 0;

    internal D045SemanticObservationCandidate(
        string opaqueTargetReference,
        D045SemanticLocalCandidateInput source)
    {
        OpaqueTargetReference = opaqueTargetReference;
        SafeDisplayLeaf = source.SafeDisplayLeaf;
        Kind = source.Kind;
        SafeExtension = source.SafeExtension;
        TargetFingerprint = source.TargetFingerprint;
        DisplayLeafFingerprint = source.DisplayLeafFingerprint;
        HarnessTargetFingerprint = source.HarnessTargetFingerprint;
    }

    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{SafeDisplayLeaf}|{Kind}|{SafeExtension}|{TargetFingerprint}");
}

/// <summary>
/// Read-only semantic observation.  HasHarnessReceipt is permanently false;
/// this type is not interchangeable with D045HarnessReceipt and cannot close
/// an effect goal or assert that an effect was attempted or verified.
/// </summary>
internal sealed class D045SemanticObservationReceipt
{
    internal const int MaximumCandidates = 5;

    internal D045SemanticReceiptKind ReceiptKind =>
        D045SemanticReceiptKind.Observation;
    internal bool HasHarnessReceipt => false;
    internal bool AttemptedEffect => false;
    internal bool VerifiedEffect => false;
    internal bool MaySatisfyEffectGoal => false;

    internal D045SemanticLocalQueryKind QueryKind { get; }
    internal D045SemanticLocalPurpose Purpose { get; }
    internal D045PermissionCapability Capability { get; }
    internal long PermissionRevision { get; }
    internal string QueryFingerprint { get; }
    internal string ObservationFingerprint { get; }
    internal bool Complete { get; }
    internal bool Ambiguous { get; }
    internal IReadOnlyList<D045SemanticObservationCandidate> Candidates { get; }

    internal D045SemanticObservationReceipt(
        D045SemanticObservationAdmission admission,
        IReadOnlyList<D045SemanticObservationCandidate> candidates,
        bool complete,
        bool ambiguous,
        DateTimeOffset observedAtUtc)
    {
        QueryKind = admission.QueryKind;
        Purpose = admission.Purpose;
        Capability = admission.Capability;
        PermissionRevision = admission.PermissionRevision;
        QueryFingerprint = admission.QueryFingerprint;
        Complete = complete;
        Ambiguous = ambiguous || candidates.Count > 1;
        Candidates = new ReadOnlyCollection<D045SemanticObservationCandidate>(
            candidates.ToArray());
        ObservationFingerprint = D045SemanticObservationFingerprint.Of(
            "D045.SEMANTIC.OBSERVATION.V1",
            ((int)QueryKind).ToString(CultureInfo.InvariantCulture),
            ((int)Purpose).ToString(CultureInfo.InvariantCulture),
            ((int)Capability).ToString(CultureInfo.InvariantCulture),
            PermissionRevision.ToString(CultureInfo.InvariantCulture),
            QueryFingerprint,
            Complete ? "1" : "0",
            Ambiguous ? "1" : "0",
            observedAtUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            string.Join("\n", Candidates.Select(candidate =>
                candidate.TargetFingerprint)));
    }
}

internal sealed class D045SemanticTargetReferenceUse
{
    internal string OpaqueTargetReference { get; }
    internal string SessionId { get; }
    internal Guid TaskId { get; }
    internal D045SemanticLocalQueryKind QueryKind { get; }
    internal D045SemanticLocalPurpose Purpose { get; }
    internal D045PermissionCapability Capability { get; }
    internal long PermissionRevision { get; }
    internal D045SemanticLocalTargetKind ExpectedKind { get; }
    internal string ExpectedTargetFingerprint { get; }

    internal D045SemanticTargetReferenceUse(
        string opaqueTargetReference,
        string sessionId,
        Guid taskId,
        D045SemanticLocalQueryKind queryKind,
        D045SemanticLocalPurpose purpose,
        D045PermissionCapability capability,
        long permissionRevision,
        D045SemanticLocalTargetKind expectedKind,
        string expectedTargetFingerprint)
    {
        OpaqueTargetReference = (opaqueTargetReference ?? string.Empty).Trim();
        SessionId = sessionId ?? string.Empty;
        TaskId = taskId;
        QueryKind = queryKind;
        Purpose = purpose;
        Capability = capability;
        PermissionRevision = permissionRevision;
        ExpectedKind = expectedKind;
        ExpectedTargetFingerprint =
            (expectedTargetFingerprint ?? string.Empty).Trim();
    }
}

/// <summary>
/// Host-only redemption result.  It must be translated into a fresh Harness
/// request and pass that Harness' own permission/policy checks before any
/// effect.  It is intentionally not part of an observation receipt.
/// </summary>
internal sealed class D045SemanticHostResolvedTarget
{
    internal string CanonicalPath { get; }
    internal D045SemanticLocalTargetKind Kind { get; }
    internal string TargetFingerprint { get; }
    internal string HostIdentityFingerprint { get; }
    internal bool HasHarnessReceipt => false;
    internal bool EffectAuthorized => false;

    internal D045SemanticHostResolvedTarget(
        string canonicalPath,
        D045SemanticLocalTargetKind kind,
        string targetFingerprint,
        string hostIdentityFingerprint)
    {
        CanonicalPath = canonicalPath;
        Kind = kind;
        TargetFingerprint = targetFingerprint;
        HostIdentityFingerprint = hostIdentityFingerprint;
    }
}

/// <summary>
/// Bounded, process-memory-only target reference registry.  References are
/// exact single-use, live for at most two minutes, and bind session, task,
/// query, host-owned purpose, permission row/revision, kind and target hash.
/// There is no persistence or serialization API.
/// </summary>
internal sealed class D045SemanticTargetReferenceRegistry
{
    internal const int MaximumActiveReferences = 32;
    internal static readonly TimeSpan ReferenceLifetime = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _active =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RetiredEntry> _retired =
        new(StringComparer.Ordinal);
    private readonly Func<string> _referenceFactory;

    internal int ActiveCount
    {
        get
        {
            lock (_gate)
                return _active.Count;
        }
    }

    internal int RetiredCount
    {
        get
        {
            lock (_gate)
                return _retired.Count;
        }
    }

    internal D045SemanticTargetReferenceRegistry(Func<string>? referenceFactory = null)
    {
        _referenceFactory = referenceFactory ?? CreateOpaqueReference;
    }

    internal bool TryCreateObservation(
        D045SemanticObservationAdmission admission,
        string sessionId,
        Guid taskId,
        IReadOnlyList<D045SemanticLocalCandidateInput>? candidates,
        bool complete,
        DateTimeOffset observedAtUtc,
        out D045SemanticObservationReceipt? receipt,
        out D045SemanticObservationCreationFailure failure)
        => TryCreateObservation(
            admission,
            sessionId,
            taskId,
            candidates,
            complete,
            ambiguous: candidates is not null && candidates.Count > 1,
            observedAtUtc,
            out receipt,
            out failure);

    internal bool TryCreateObservation(
        D045SemanticObservationAdmission admission,
        string sessionId,
        Guid taskId,
        IReadOnlyList<D045SemanticLocalCandidateInput>? candidates,
        bool complete,
        bool ambiguous,
        DateTimeOffset observedAtUtc,
        out D045SemanticObservationReceipt? receipt,
        out D045SemanticObservationCreationFailure failure)
    {
        receipt = null;
        failure = D045SemanticObservationCreationFailure.None;
        if (!IsValidSession(sessionId))
        {
            failure = D045SemanticObservationCreationFailure.InvalidSession;
            return false;
        }
        if (taskId == Guid.Empty)
        {
            failure = D045SemanticObservationCreationFailure.InvalidTask;
            return false;
        }
        if (observedAtUtc == DateTimeOffset.MinValue
            || observedAtUtc > DateTimeOffset.MaxValue - ReferenceLifetime)
        {
            failure = D045SemanticObservationCreationFailure.InvalidTimestamp;
            return false;
        }

        var sourceCandidates = candidates ??
            Array.Empty<D045SemanticLocalCandidateInput>();
        if (sourceCandidates.Count > D045SemanticObservationReceipt.MaximumCandidates)
        {
            failure = D045SemanticObservationCreationFailure.CandidateLimitExceeded;
            return false;
        }
        if (sourceCandidates.Any(candidate => candidate is null))
        {
            failure = D045SemanticObservationCreationFailure.CandidateInvalid;
            return false;
        }
        if (sourceCandidates.Any(candidate =>
                !D045SemanticLocalObservationPolicy.PurposeSupportsTargetKind(
                    admission.Purpose,
                    candidate.Kind)))
        {
            failure = D045SemanticObservationCreationFailure.CandidateKindMismatch;
            return false;
        }
        if (sourceCandidates.Select(candidate => candidate.TargetFingerprint)
            .Distinct(StringComparer.Ordinal).Count() != sourceCandidates.Count)
        {
            failure = D045SemanticObservationCreationFailure.DuplicateCandidate;
            return false;
        }

        lock (_gate)
        {
            Purge(observedAtUtc);
            var observationIsAmbiguous = ambiguous
                || sourceCandidates.Count > 1;
            var issuesRedeemableReference = !observationIsAmbiguous
                && sourceCandidates.Count == 1;
            var referenceCount = issuesRedeemableReference ? 1 : 0;
            if (_active.Count + referenceCount > MaximumActiveReferences)
            {
                failure =
                    D045SemanticObservationCreationFailure.RegistryCapacityReached;
                return false;
            }

            var issued = new List<(string Reference, D045SemanticLocalCandidateInput Source)>(
                referenceCount);
            if (issuesRedeemableReference)
            {
                var candidate = sourceCandidates[0];
                var opaqueReference = TryMakeUniqueReference(issued);
                if (opaqueReference is null)
                {
                    failure =
                        D045SemanticObservationCreationFailure.OpaqueReferenceCollision;
                    return false;
                }
                issued.Add((opaqueReference, candidate));
            }

            var expiry = observedAtUtc.ToUniversalTime() + ReferenceLifetime;
            foreach (var item in issued)
            {
                _active.Add(item.Reference, new Entry(
                    item.Reference,
                    sessionId,
                    taskId,
                    admission.QueryKind,
                    admission.Purpose,
                    admission.Capability,
                    admission.PermissionRevision,
                    item.Source.Kind,
                    item.Source.TargetFingerprint,
                    item.Source.HostIdentityFingerprint,
                    item.Source.CanonicalPath,
                    expiry));
            }

            var issuedReference = issued.Count == 1
                ? issued[0].Reference
                : string.Empty;

            receipt = new D045SemanticObservationReceipt(
                admission,
                sourceCandidates.Select(source =>
                    new D045SemanticObservationCandidate(
                        issuedReference,
                        source)).ToArray(),
                complete,
                observationIsAmbiguous,
                observedAtUtc);
            return true;
        }
    }

    internal bool TryConsume(
        D045SemanticTargetReferenceUse use,
        D045PermissionLoadResult? currentPermissionState,
        DateTimeOffset nowUtc,
        out D045SemanticHostResolvedTarget? target,
        out D045SemanticTargetReferenceFailure failure)
    {
        target = null;
        failure = D045SemanticTargetReferenceFailure.None;
        if (use.OpaqueTargetReference.Length == 0)
        {
            failure = D045SemanticTargetReferenceFailure.ReferenceMissing;
            return false;
        }

        lock (_gate)
        {
            Purge(nowUtc, use.OpaqueTargetReference);
            if (!_active.TryGetValue(use.OpaqueTargetReference, out var entry))
            {
                if (_retired.TryGetValue(use.OpaqueTargetReference, out var retired))
                {
                    failure = retired.Consumed
                        ? D045SemanticTargetReferenceFailure.ReferenceAlreadyConsumed
                        : D045SemanticTargetReferenceFailure.ReferenceExpired;
                    return false;
                }

                failure = D045SemanticTargetReferenceFailure.ReferenceNotFound;
                return false;
            }

            if (!string.Equals(entry.SessionId, use.SessionId, StringComparison.Ordinal))
                return Reject(D045SemanticTargetReferenceFailure.WrongSession, out failure);
            if (entry.TaskId != use.TaskId)
                return Reject(D045SemanticTargetReferenceFailure.WrongTask, out failure);
            if (entry.QueryKind != use.QueryKind)
                return Reject(D045SemanticTargetReferenceFailure.WrongQueryKind, out failure);
            if (entry.Purpose != use.Purpose)
                return Reject(D045SemanticTargetReferenceFailure.WrongPurpose, out failure);
            if (entry.Capability != use.Capability
                || D045SemanticLocalObservationPolicy.CapabilityFor(use.Purpose)
                    != use.Capability)
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.WrongCapability,
                    out failure);
            }
            if (entry.PermissionRevision != use.PermissionRevision)
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.WrongPermissionRevision,
                    out failure);
            }
            if (!D045SemanticLocalObservationPolicy.TryGetCurrentSnapshot(
                    currentPermissionState,
                    out var snapshot))
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.PermissionStateUnavailable,
                    out failure);
            }
            if (snapshot.Revision != entry.PermissionRevision)
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.WrongPermissionRevision,
                    out failure);
            }
            if (!snapshot.IsEnabled(entry.Capability))
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.PermissionDefaultOff,
                    out failure);
            }
            if (entry.Kind != use.ExpectedKind)
                return Reject(D045SemanticTargetReferenceFailure.WrongTargetKind, out failure);
            if (!string.Equals(
                    entry.TargetFingerprint,
                    use.ExpectedTargetFingerprint,
                    StringComparison.Ordinal))
            {
                return Reject(
                    D045SemanticTargetReferenceFailure.WrongTargetFingerprint,
                    out failure);
            }

            _active.Remove(entry.Reference);
            AddRetired(entry.Reference, consumed: true, nowUtc + ReferenceLifetime);
            target = new D045SemanticHostResolvedTarget(
                entry.CanonicalPath,
                entry.Kind,
                entry.TargetFingerprint,
                entry.HostIdentityFingerprint);
            return true;
        }
    }

    internal void Retire(
        string? opaqueReference,
        string? sessionId,
        Guid taskId,
        DateTimeOffset nowUtc)
    {
        var reference = opaqueReference ?? string.Empty;
        var session = sessionId ?? string.Empty;
        if (!IsOpaqueReference(reference)
            || !IsValidSession(session)
            || taskId == Guid.Empty
            || nowUtc == DateTimeOffset.MinValue
            || nowUtc > DateTimeOffset.MaxValue - ReferenceLifetime)
        {
            return;
        }

        lock (_gate)
        {
            Purge(nowUtc, reference);
            if (!_active.TryGetValue(reference, out var entry)
                || !string.Equals(
                    entry.SessionId,
                    session,
                    StringComparison.Ordinal)
                || entry.TaskId != taskId)
            {
                return;
            }

            _active.Remove(reference);
            AddRetired(
                reference,
                consumed: false,
                nowUtc.ToUniversalTime() + ReferenceLifetime);
        }
    }

    private string? TryMakeUniqueReference(
        IReadOnlyList<(string Reference, D045SemanticLocalCandidateInput Source)> issued)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var candidate = (_referenceFactory() ?? string.Empty).Trim();
            if (!IsOpaqueReference(candidate)
                || _active.ContainsKey(candidate)
                || _retired.ContainsKey(candidate)
                || issued.Any(item => string.Equals(
                    item.Reference,
                    candidate,
                    StringComparison.Ordinal)))
            {
                continue;
            }
            return candidate;
        }
        return null;
    }

    private void Purge(DateTimeOffset nowUtc, string? preserve = null)
    {
        var now = nowUtc.ToUniversalTime();
        foreach (var pair in _active
            .Where(pair => now >= pair.Value.ExpiresUtc)
            .ToArray())
        {
            _active.Remove(pair.Key);
            AddRetired(pair.Key, consumed: false, now + ReferenceLifetime);
        }

        foreach (var pair in _retired
            .Where(pair => now >= pair.Value.RetainUntilUtc
                && !string.Equals(pair.Key, preserve, StringComparison.Ordinal))
            .ToArray())
        {
            _retired.Remove(pair.Key);
        }
    }

    private void AddRetired(
        string reference,
        bool consumed,
        DateTimeOffset retainUntilUtc)
    {
        if (_retired.Count >= MaximumActiveReferences)
        {
            var oldest = _retired.OrderBy(pair => pair.Value.RetainUntilUtc)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First();
            _retired.Remove(oldest.Key);
        }
        _retired[reference] = new RetiredEntry(consumed, retainUntilUtc);
    }

    private static bool Reject(
        D045SemanticTargetReferenceFailure reason,
        out D045SemanticTargetReferenceFailure failure)
    {
        failure = reason;
        return false;
    }

    private static bool IsValidSession(string? sessionId)
        => !string.IsNullOrWhiteSpace(sessionId)
            && sessionId.Length <= 128
            && !sessionId.Any(char.IsControl);

    private static bool IsOpaqueReference(string value)
        => value.Length is >= 16 and <= 96
            && value.StartsWith("d045ref_", StringComparison.Ordinal)
            && value.All(character =>
                D045SemanticObservationFingerprint.IsAsciiLetterOrDigit(character)
                || character is '_' or '-');

    private static string CreateOpaqueReference()
        => "d045ref_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            .ToLowerInvariant();

    private sealed class Entry
    {
        internal string Reference { get; }
        internal string SessionId { get; }
        internal Guid TaskId { get; }
        internal D045SemanticLocalQueryKind QueryKind { get; }
        internal D045SemanticLocalPurpose Purpose { get; }
        internal D045PermissionCapability Capability { get; }
        internal long PermissionRevision { get; }
        internal D045SemanticLocalTargetKind Kind { get; }
        internal string TargetFingerprint { get; }
        internal string HostIdentityFingerprint { get; }
        internal string CanonicalPath { get; }
        internal DateTimeOffset ExpiresUtc { get; }

        internal Entry(
            string reference,
            string sessionId,
            Guid taskId,
            D045SemanticLocalQueryKind queryKind,
            D045SemanticLocalPurpose purpose,
            D045PermissionCapability capability,
            long permissionRevision,
            D045SemanticLocalTargetKind kind,
            string targetFingerprint,
            string hostIdentityFingerprint,
            string canonicalPath,
            DateTimeOffset expiresUtc)
        {
            Reference = reference;
            SessionId = sessionId;
            TaskId = taskId;
            QueryKind = queryKind;
            Purpose = purpose;
            Capability = capability;
            PermissionRevision = permissionRevision;
            Kind = kind;
            TargetFingerprint = targetFingerprint;
            HostIdentityFingerprint = hostIdentityFingerprint;
            CanonicalPath = canonicalPath;
            ExpiresUtc = expiresUtc;
        }
    }

    private sealed class RetiredEntry
    {
        internal bool Consumed { get; }
        internal DateTimeOffset RetainUntilUtc { get; }

        internal RetiredEntry(bool consumed, DateTimeOffset retainUntilUtc)
        {
            Consumed = consumed;
            RetainUntilUtc = retainUntilUtc;
        }
    }
}

/// <summary>
/// Host-owned request for one semantic observation.  Query is provider text;
/// RedeemedExactTarget, when present, is an in-process host value returned by
/// TryRedeemTarget and is never projected back to the provider.
/// </summary>
internal sealed class D045SemanticLocalObservationHostRequest
{
    internal string SessionId { get; }
    internal Guid TaskId { get; }
    internal D045SemanticLocalQueryKind QueryKind { get; }
    internal D045SemanticLocalPurpose HostOwnedPurpose { get; }
    internal string Query { get; }
    internal D045SemanticHostResolvedTarget? RedeemedExactTarget { get; }

    internal D045SemanticLocalObservationHostRequest(
        string sessionId,
        Guid taskId,
        D045SemanticLocalQueryKind queryKind,
        D045SemanticLocalPurpose hostOwnedPurpose,
        string? query,
        D045SemanticHostResolvedTarget? redeemedExactTarget = null)
    {
        SessionId = sessionId ?? string.Empty;
        TaskId = taskId;
        QueryKind = queryKind;
        HostOwnedPurpose = hostOwnedPurpose;
        Query = query ?? string.Empty;
        RedeemedExactTarget = redeemedExactTarget;
    }
}

internal sealed class D045SemanticLocalObservationExecutionResult
{
    internal D045SemanticObservationExecutionDisposition Disposition { get; }
    internal D045SemanticObservationReceipt? Receipt { get; }
    internal D045SemanticObservationAdmissionFailure AdmissionFailure { get; }
    internal D045SemanticObservationCreationFailure CreationFailure { get; }
    internal string ReasonCode { get; }
    internal bool Observed => Receipt is not null;
    internal bool HasHarnessReceipt => false;
    internal bool EffectDispatched => false;

    internal D045SemanticLocalObservationExecutionResult(
        D045SemanticObservationExecutionDisposition disposition,
        D045SemanticObservationReceipt? receipt,
        D045SemanticObservationAdmissionFailure admissionFailure,
        D045SemanticObservationCreationFailure creationFailure,
        string reasonCode)
    {
        if (!Enum.IsDefined(
                typeof(D045SemanticObservationExecutionDisposition),
                disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        Disposition = disposition;
        Receipt = receipt;
        AdmissionFailure = admissionFailure;
        CreationFailure = creationFailure;
        ReasonCode = reasonCode;
    }
}

/// <summary>
/// Real read-only semantic observation executor.  It reuses the Harness'
/// existing local target resolvers and permission store, but it never calls
/// Execute, Process.Start, ShellExecute or the local-content dispatcher.
/// </summary>
internal sealed partial class D045LocalTaskHarness
{
    private readonly D045SemanticTargetReferenceRegistry
        _semanticTargetReferences = new();

    internal D045SemanticLocalObservationExecutionResult
        ExecuteSemanticObservation(
            D045SemanticLocalObservationHostRequest request)
        => ExecuteSemanticObservation(request, DateTimeOffset.UtcNow);

    internal D045SemanticLocalObservationExecutionResult
        ExecuteSemanticObservation(
            D045SemanticLocalObservationHostRequest request,
            DateTimeOffset observedAtUtc)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.RedeemedExactTarget is not null
            && request.QueryKind != D045SemanticLocalQueryKind.InspectLocalItem)
        {
            return AdmissionRejected(
                D045SemanticObservationAdmissionFailure.QueryInvalid,
                "redeemed_target_requires_inspect");
        }

        if (!D045SemanticLocalObservationPolicy.TryAdmit(
                request.QueryKind,
                request.HostOwnedPurpose,
                request.Query,
                _permissions.Load(),
                out var admission,
                out var admissionFailure))
        {
            return AdmissionRejected(
                admissionFailure,
                "semantic_admission_rejected");
        }

        return request.HostOwnedPurpose
            == D045SemanticLocalPurpose.LaunchApplication
            ? ExecuteSemanticApplicationObservation(
                request,
                admission!,
                observedAtUtc)
            : ExecuteSemanticPathObservation(
                request,
                admission!,
                observedAtUtc);
    }

    internal bool TryRedeemTarget(
        D045SemanticTargetReferenceUse use,
        out D045SemanticHostResolvedTarget? target,
        out D045SemanticTargetReferenceFailure failure)
        => TryRedeemTarget(
            use,
            DateTimeOffset.UtcNow,
            out target,
            out failure);

    internal void RetireSemanticTargetReference(
        string? opaqueReference,
        string? sessionId,
        Guid taskId,
        DateTimeOffset nowUtc)
        => _semanticTargetReferences.Retire(
            opaqueReference,
            sessionId,
            taskId,
            nowUtc);

    internal bool TryRedeemTarget(
        D045SemanticTargetReferenceUse use,
        DateTimeOffset nowUtc,
        out D045SemanticHostResolvedTarget? target,
        out D045SemanticTargetReferenceFailure failure)
    {
        if (use is null)
            throw new ArgumentNullException(nameof(use));

        target = null;
        if (!_semanticTargetReferences.TryConsume(
                use,
                _permissions.Load(),
                nowUtc,
                out var consumed,
                out failure))
        {
            return false;
        }

        if (consumed is null)
        {
            failure = D045SemanticTargetReferenceFailure.TargetChanged;
            return false;
        }

        if (!TryValidateNoReparsePath(
                consumed.CanonicalPath,
                out _))
        {
            failure = D045SemanticTargetReferenceFailure.UnsafeTargetRejected;
            return false;
        }

        if (!TryRevalidateRedeemedTarget(consumed, out failure))
            return false;

        target = consumed;
        return true;
    }

    private D045SemanticLocalObservationExecutionResult
        ExecuteSemanticPathObservation(
            D045SemanticLocalObservationHostRequest request,
            D045SemanticObservationAdmission admission,
            DateTimeOffset observedAtUtc)
    {
        var allowFile = request.HostOwnedPurpose is
            D045SemanticLocalPurpose.ReadLocalFile
                or D045SemanticLocalPurpose.OpenLocalContent;
        var allowDirectory = request.HostOwnedPurpose is
            D045SemanticLocalPurpose.EnumerateLocalFolder
                or D045SemanticLocalPurpose.OpenLocalContent;

        if (request.RedeemedExactTarget is not null
            && !D045SemanticLocalObservationPolicy.PurposeSupportsTargetKind(
                request.HostOwnedPurpose,
                request.RedeemedExactTarget.Kind))
        {
            return AdmissionRejected(
                D045SemanticObservationAdmissionFailure.QueryPurposeMismatch,
                "redeemed_target_kind_mismatch");
        }

        var targetQuery = request.RedeemedExactTarget?.CanonicalPath
            ?? request.Query;
        var resolution = ResolveKnownOrExactPath(
            targetQuery,
            allowFile,
            allowDirectory);
        if (!resolution.Resolved)
        {
            if (IsUnsafeResolutionMessage(resolution.Message))
            {
                return CreateSemanticObservation(
                    request,
                    admission,
                    Array.Empty<D045SemanticLocalCandidateInput>(),
                    D045SemanticObservationExecutionDisposition
                        .UnsafeTargetRejected,
                    "unsafe_local_target_rejected",
                    observedAtUtc);
            }

            if (resolution.OutcomeReason
                == D045HarnessOutcomeReason.TargetAmbiguous)
            {
                var candidates = BuildPathCandidates(
                    resolution.CandidateSummary,
                    allowFile,
                    allowDirectory);
                return CreateSemanticObservation(
                    request,
                    admission,
                    candidates,
                    D045SemanticObservationExecutionDisposition
                        .TargetAmbiguous,
                    "semantic_target_ambiguous",
                    observedAtUtc);
            }

            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                D045SemanticObservationExecutionDisposition.TargetNotFound,
                "semantic_target_not_found",
                observedAtUtc);
        }

        D045SemanticLocalCandidateInput candidate;
        try
        {
            var kind = Directory.Exists(resolution.CanonicalPath)
                ? D045SemanticLocalTargetKind.Directory
                : D045SemanticLocalTargetKind.File;
            candidate = new D045SemanticLocalCandidateInput(
                resolution.CanonicalPath,
                kind);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                D045SemanticObservationExecutionDisposition.TargetNotFound,
                "semantic_target_changed",
                observedAtUtc);
        }

        if (request.RedeemedExactTarget is not null
            && (!string.Equals(
                    candidate.TargetFingerprint,
                    request.RedeemedExactTarget.TargetFingerprint,
                    StringComparison.Ordinal)
                || candidate.Kind != request.RedeemedExactTarget.Kind))
        {
            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                D045SemanticObservationExecutionDisposition.TargetNotFound,
                "redeemed_target_changed",
                observedAtUtc);
        }

        return CreateSemanticObservation(
            request,
            admission,
            new[] { candidate },
            D045SemanticObservationExecutionDisposition.ObservedUnique,
            "semantic_target_resolved",
            observedAtUtc);
    }

    private D045SemanticLocalObservationExecutionResult
        ExecuteSemanticApplicationObservation(
            D045SemanticLocalObservationHostRequest request,
            D045SemanticObservationAdmission admission,
            DateTimeOffset observedAtUtc)
    {
        var resolution = ResolveApplicationTarget(request.Query);
        if (!resolution.Resolved)
        {
            var disposition = resolution.OutcomeReason
                    == D045HarnessOutcomeReason.TargetAmbiguous
                ? D045SemanticObservationExecutionDisposition.TargetAmbiguous
                : D045SemanticObservationExecutionDisposition.TargetNotFound;
            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                disposition,
                disposition
                    == D045SemanticObservationExecutionDisposition.TargetAmbiguous
                    ? "semantic_application_ambiguous"
                    : "semantic_application_not_found",
                observedAtUtc);
        }

        if (resolution.FixedArguments.Length != 0)
        {
            return new D045SemanticLocalObservationExecutionResult(
                D045SemanticObservationExecutionDisposition
                    .ApplicationFixedArgumentsRejected,
                null,
                D045SemanticObservationAdmissionFailure.None,
                D045SemanticObservationCreationFailure.None,
                "application_fixed_arguments_rejected");
        }
        if (IsBlockedApplicationHost(resolution.ExecutablePath))
        {
            return new D045SemanticLocalObservationExecutionResult(
                D045SemanticObservationExecutionDisposition.ApplicationBlocked,
                null,
                D045SemanticObservationAdmissionFailure.None,
                D045SemanticObservationCreationFailure.None,
                "application_host_blocked");
        }
        if (!TryValidateNoReparsePath(resolution.ExecutablePath, out _))
        {
            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                D045SemanticObservationExecutionDisposition
                    .UnsafeTargetRejected,
                "unsafe_application_target_rejected",
                observedAtUtc);
        }

        D045SemanticLocalCandidateInput candidate;
        try
        {
            candidate = new D045SemanticLocalCandidateInput(
                resolution.ExecutablePath,
                D045SemanticLocalTargetKind.Application);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return CreateSemanticObservation(
                request,
                admission,
                Array.Empty<D045SemanticLocalCandidateInput>(),
                D045SemanticObservationExecutionDisposition.TargetNotFound,
                "semantic_application_changed",
                observedAtUtc);
        }

        return CreateSemanticObservation(
            request,
            admission,
            new[] { candidate },
            D045SemanticObservationExecutionDisposition.ObservedUnique,
            "semantic_application_resolved",
            observedAtUtc);
    }

    private D045SemanticLocalObservationExecutionResult
        CreateSemanticObservation(
            D045SemanticLocalObservationHostRequest request,
            D045SemanticObservationAdmission admitted,
            IReadOnlyList<D045SemanticLocalCandidateInput> candidates,
            D045SemanticObservationExecutionDisposition disposition,
            string reasonCode,
            DateTimeOffset observedAtUtc)
    {
        if (!D045SemanticLocalObservationPolicy.TryAdmit(
                request.QueryKind,
                request.HostOwnedPurpose,
                request.Query,
                _permissions.Load(),
                out var current,
                out var currentFailure))
        {
            return AdmissionRejected(
                currentFailure,
                "semantic_permission_changed_during_resolution");
        }
        if (current!.PermissionRevision != admitted.PermissionRevision)
        {
            return AdmissionRejected(
                D045SemanticObservationAdmissionFailure
                    .PermissionRevisionChanged,
                "semantic_permission_revision_changed");
        }

        if (!_semanticTargetReferences.TryCreateObservation(
                admitted,
                request.SessionId,
                request.TaskId,
                candidates,
                complete: true,
                ambiguous: disposition
                    == D045SemanticObservationExecutionDisposition
                        .TargetAmbiguous,
                observedAtUtc,
                out var receipt,
                out var creationFailure))
        {
            return new D045SemanticLocalObservationExecutionResult(
                D045SemanticObservationExecutionDisposition
                    .ObservationCreationRejected,
                null,
                D045SemanticObservationAdmissionFailure.None,
                creationFailure,
                "semantic_observation_creation_rejected");
        }

        return new D045SemanticLocalObservationExecutionResult(
            disposition,
            receipt,
            D045SemanticObservationAdmissionFailure.None,
            D045SemanticObservationCreationFailure.None,
            reasonCode);
    }

    private static D045SemanticLocalObservationExecutionResult
        AdmissionRejected(
            D045SemanticObservationAdmissionFailure failure,
            string reasonCode)
        => new(
            D045SemanticObservationExecutionDisposition.AdmissionRejected,
            null,
            failure,
            D045SemanticObservationCreationFailure.None,
            reasonCode);

    private static D045SemanticLocalCandidateInput[] BuildPathCandidates(
        string candidateSummary,
        bool allowFile,
        bool allowDirectory)
    {
        var candidates = new List<D045SemanticLocalCandidateInput>();
        foreach (var rawPath in candidateSummary.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (candidates.Count
                == D045SemanticObservationReceipt.MaximumCandidates)
            {
                break;
            }

            try
            {
                var canonical = Path.GetFullPath(rawPath);
                if (!TryValidateNoReparsePath(canonical, out _))
                    continue;
                if (allowDirectory && Directory.Exists(canonical))
                {
                    candidates.Add(new D045SemanticLocalCandidateInput(
                        canonical,
                        D045SemanticLocalTargetKind.Directory));
                }
                else if (allowFile && File.Exists(canonical))
                {
                    candidates.Add(new D045SemanticLocalCandidateInput(
                        canonical,
                        D045SemanticLocalTargetKind.File));
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or IOException
                    or NotSupportedException
                    or PathTooLongException
                    or UnauthorizedAccessException)
            {
                // A changing candidate is omitted rather than exposing its path.
            }
        }
        return candidates.ToArray();
    }

    private bool TryRevalidateRedeemedTarget(
        D045SemanticHostResolvedTarget consumed,
        out D045SemanticTargetReferenceFailure failure)
    {
        failure = D045SemanticTargetReferenceFailure.None;
        string resolvedPath;
        switch (consumed.Kind)
        {
            case D045SemanticLocalTargetKind.File:
            {
                var resolution = ResolveKnownOrExactPath(
                    consumed.CanonicalPath,
                    allowFile: true,
                    allowDirectory: false);
                if (!resolution.Resolved)
                {
                    failure = IsUnsafeResolutionMessage(resolution.Message)
                        ? D045SemanticTargetReferenceFailure
                            .UnsafeTargetRejected
                        : D045SemanticTargetReferenceFailure.TargetChanged;
                    return false;
                }
                resolvedPath = resolution.CanonicalPath;
                break;
            }
            case D045SemanticLocalTargetKind.Directory:
            {
                var resolution = ResolveKnownOrExactPath(
                    consumed.CanonicalPath,
                    allowFile: false,
                    allowDirectory: true);
                if (!resolution.Resolved)
                {
                    failure = IsUnsafeResolutionMessage(resolution.Message)
                        ? D045SemanticTargetReferenceFailure
                            .UnsafeTargetRejected
                        : D045SemanticTargetReferenceFailure.TargetChanged;
                    return false;
                }
                resolvedPath = resolution.CanonicalPath;
                break;
            }
            case D045SemanticLocalTargetKind.Application:
            {
                var resolution = ResolveApplicationTarget(consumed.CanonicalPath);
                if (!resolution.Resolved || resolution.FixedArguments.Length != 0)
                {
                    failure =
                        D045SemanticTargetReferenceFailure.TargetChanged;
                    return false;
                }
                if (IsBlockedApplicationHost(resolution.ExecutablePath))
                {
                    failure =
                        D045SemanticTargetReferenceFailure.ApplicationBlocked;
                    return false;
                }
                resolvedPath = resolution.ExecutablePath;
                break;
            }
            default:
                failure = D045SemanticTargetReferenceFailure.WrongTargetKind;
                return false;
        }

        var current = new D045SemanticLocalCandidateInput(
            resolvedPath,
            consumed.Kind);
        if (!string.Equals(
                current.TargetFingerprint,
                consumed.TargetFingerprint,
                StringComparison.Ordinal)
            || !string.Equals(
                current.HostIdentityFingerprint,
                consumed.HostIdentityFingerprint,
                StringComparison.Ordinal))
        {
            failure = D045SemanticTargetReferenceFailure.TargetChanged;
            return false;
        }
        return true;
    }

    private static bool IsUnsafeResolutionMessage(string message)
        => message.StartsWith("Reparse-point", StringComparison.OrdinalIgnoreCase)
            || message.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("share", StringComparison.OrdinalIgnoreCase) >= 0
            || message.IndexOf("traversal", StringComparison.OrdinalIgnoreCase) >= 0;
}

internal static class D045SemanticObservationFingerprint
{
    internal static bool IsAsciiLetterOrDigit(char value)
        => value is >= '0' and <= '9'
            or >= 'A' and <= 'Z'
            or >= 'a' and <= 'z';

    internal static bool IsSha256(string? value)
        => value is not null
            && value.Length == 64
            && value.All(character => character is >= '0' and <= '9'
                or >= 'A' and <= 'F');

    internal static string Of(string domain, params string[] fields)
    {
        var builder = new StringBuilder(domain).Append('\n');
        foreach (var field in fields)
        {
            var value = field ?? string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
