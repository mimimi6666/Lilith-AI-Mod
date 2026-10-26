using System;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum DesktopArtifactPreviewStatus
{
    Invalid = 0,
    Off = 1,
    Ready = 2,
    TransitionPreview = 3,
    Fault = 4
}

internal enum DesktopArtifactInputKind
{
    Invalid = 0,
    PrimarySingleClick = 1,
    PrimaryDoubleClick = 2
}

internal enum DesktopArtifactPreviewAdmission
{
    Invalid = 0,
    Ready = 1,
    DisabledAndCleared = 2,
    SingleClickObserved = 3,
    TransitionPreviewAccepted = 4,
    TransitionAlreadyActive = 5,
    RuntimeIdentityMismatch = 6,
    RuntimeGenerationMismatch = 7,
    SurfaceIdentityMismatch = 8,
    InputSequenceRejected = 9,
    RequestNotYetValid = 10,
    RequestExpired = 11,
    SessionExpired = 12
}

internal sealed class DesktopArtifactRuntimeIdentity
{
    internal string RuntimeInstanceId { get; }
    internal long RuntimeGeneration { get; }
    internal string SessionId { get; }
    internal string Fingerprint { get; }

    internal DesktopArtifactRuntimeIdentity(
        string runtimeInstanceId,
        long runtimeGeneration,
        string sessionId)
    {
        RuntimeInstanceId = Required(runtimeInstanceId, nameof(runtimeInstanceId));
        RuntimeGeneration = Positive(runtimeGeneration, nameof(runtimeGeneration));
        SessionId = Required(sessionId, nameof(sessionId));
        Fingerprint = Hash(
            "D030/DESKTOP-ARTIFACT-RUNTIME/1",
            RuntimeInstanceId,
            RuntimeGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SessionId);
    }

    internal bool Matches(DesktopArtifactRuntimeIdentity? other)
        => other != null
            && string.Equals(
                RuntimeInstanceId,
                other.RuntimeInstanceId,
                StringComparison.Ordinal)
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            && RuntimeGeneration == other.RuntimeGeneration;

    internal bool MatchesProcess(DesktopArtifactRuntimeIdentity? other)
        => other != null
            && string.Equals(
                RuntimeInstanceId,
                other.RuntimeInstanceId,
                StringComparison.Ordinal)
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal);

    internal static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }

    internal static long Positive(long value, string name)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }

    internal static DateTimeOffset Timestamp(DateTimeOffset value, string name)
    {
        if (value == default)
            throw new ArgumentOutOfRangeException(name);
        return value.ToUniversalTime();
    }

    internal static string Hash(params string?[] values)
    {
        using var sha = SHA256.Create();
        var payload = Encoding.UTF8.GetBytes(string.Join("\u001f", values));
        return Convert.ToHexString(sha.ComputeHash(payload));
    }
}

internal sealed class DesktopArtifactPreviewSession
{
    internal string SurfaceInstanceId { get; }
    internal DesktopArtifactRuntimeIdentity RuntimeIdentity { get; }
    internal DateTimeOffset LeaseIssuedAt { get; }
    internal DateTimeOffset LeaseExpiresAt { get; }
    internal string AssetVersion { get; }
    internal string Fingerprint { get; }

    internal DesktopArtifactPreviewSession(
        string surfaceInstanceId,
        DesktopArtifactRuntimeIdentity runtimeIdentity,
        DateTimeOffset leaseIssuedAt,
        DateTimeOffset leaseExpiresAt,
        string assetVersion)
    {
        SurfaceInstanceId = DesktopArtifactRuntimeIdentity.Required(
            surfaceInstanceId,
            nameof(surfaceInstanceId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        LeaseIssuedAt = DesktopArtifactRuntimeIdentity.Timestamp(
            leaseIssuedAt,
            nameof(leaseIssuedAt));
        LeaseExpiresAt = DesktopArtifactRuntimeIdentity.Timestamp(
            leaseExpiresAt,
            nameof(leaseExpiresAt));
        if (LeaseExpiresAt <= LeaseIssuedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt));
        if (LeaseExpiresAt - LeaseIssuedAt > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt));
        AssetVersion = DesktopArtifactRuntimeIdentity.Required(
            assetVersion,
            nameof(assetVersion));
        Fingerprint = DesktopArtifactRuntimeIdentity.Hash(
            "D030/DESKTOP-ARTIFACT-SESSION/1",
            SurfaceInstanceId,
            RuntimeIdentity.Fingerprint,
            LeaseIssuedAt.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            LeaseExpiresAt.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            AssetVersion);
    }
}

internal sealed class DesktopArtifactPreviewRequest
{
    internal string SurfaceInstanceId { get; }
    internal DesktopArtifactRuntimeIdentity RuntimeIdentity { get; }
    internal DesktopArtifactInputKind InputKind { get; }
    internal long InputSequence { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal DesktopArtifactPreviewRequest(
        string surfaceInstanceId,
        DesktopArtifactRuntimeIdentity runtimeIdentity,
        DesktopArtifactInputKind inputKind,
        long inputSequence,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        SurfaceInstanceId = DesktopArtifactRuntimeIdentity.Required(
            surfaceInstanceId,
            nameof(surfaceInstanceId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (!Enum.IsDefined(typeof(DesktopArtifactInputKind), inputKind)
            || inputKind == DesktopArtifactInputKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(inputKind));
        }
        InputKind = inputKind;
        InputSequence = DesktopArtifactRuntimeIdentity.Positive(
            inputSequence,
            nameof(inputSequence));
        RequestedAt = DesktopArtifactRuntimeIdentity.Timestamp(
            requestedAt,
            nameof(requestedAt));
        ExpiresAt = DesktopArtifactRuntimeIdentity.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= RequestedAt
            || ExpiresAt - RequestedAt > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }
        Fingerprint = DesktopArtifactRuntimeIdentity.Hash(
            "D030/DESKTOP-ARTIFACT-REQUEST/1",
            SurfaceInstanceId,
            RuntimeIdentity.Fingerprint,
            ((int)InputKind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            InputSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RequestedAt.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ExpiresAt.ToUnixTimeMilliseconds().ToString(
                System.Globalization.CultureInfo.InvariantCulture));
    }
}

internal sealed class DesktopArtifactPreviewReceipt
{
    internal DesktopArtifactPreviewAdmission Admission { get; }
    internal DesktopArtifactPreviewStatus Status { get; }
    internal bool AllowsUnityPresentation =>
        Admission == DesktopArtifactPreviewAdmission.Ready
        || Admission == DesktopArtifactPreviewAdmission.SingleClickObserved
        || Admission
            == DesktopArtifactPreviewAdmission.TransitionPreviewAccepted;
    internal bool AllowsTransitionPreview =>
        Admission
        == DesktopArtifactPreviewAdmission.TransitionPreviewAccepted;
    internal bool AllowsApplicationLaunch => false;
    internal bool AllowsFileOrShortcutCreation => false;
    internal bool AllowsCapabilityOutput => false;
    internal string Fingerprint { get; }

    internal DesktopArtifactPreviewReceipt(
        DesktopArtifactPreviewAdmission admission,
        DesktopArtifactPreviewStatus status,
        string subjectFingerprint)
    {
        if (!Enum.IsDefined(typeof(DesktopArtifactPreviewAdmission), admission)
            || admission == DesktopArtifactPreviewAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (!Enum.IsDefined(typeof(DesktopArtifactPreviewStatus), status)
            || status == DesktopArtifactPreviewStatus.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }
        Admission = admission;
        Status = status;
        Fingerprint = DesktopArtifactRuntimeIdentity.Hash(
            "D030/DESKTOP-ARTIFACT-RECEIPT/1",
            ((int)Admission).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ((int)Status).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            subjectFingerprint);
    }
}

internal sealed class DesktopArtifactPreviewHost
{
    internal static readonly TimeSpan TransitionDuration =
        TimeSpan.FromMilliseconds(720);

    internal DesktopArtifactPreviewSession? Session { get; private set; }
    internal DesktopArtifactPreviewStatus Status { get; private set; } =
        DesktopArtifactPreviewStatus.Off;
    internal long LastInputSequence { get; private set; }
    internal DateTimeOffset? TransitionEndsAt { get; private set; }
    internal int VisualSurfaceCount => Session == null ? 0 : 1;
    internal DesktopArtifactPreviewAuthorityCounters AuthorityCounters { get; } =
        new();

    internal DesktopArtifactPreviewReceipt Reconcile(
        bool enabled,
        DesktopArtifactPreviewSession? session,
        DateTimeOffset observedAt)
    {
        observedAt = DesktopArtifactRuntimeIdentity.Timestamp(
            observedAt,
            nameof(observedAt));
        if (!enabled)
        {
            var subject = Session?.Fingerprint ?? "off";
            Session = null;
            LastInputSequence = 0;
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Off;
            return new DesktopArtifactPreviewReceipt(
                DesktopArtifactPreviewAdmission.DisabledAndCleared,
                Status,
                subject);
        }
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (observedAt < session.LeaseIssuedAt)
        {
            return new DesktopArtifactPreviewReceipt(
                DesktopArtifactPreviewAdmission.RequestNotYetValid,
                Status,
                session.Fingerprint);
        }
        if (observedAt >= session.LeaseExpiresAt)
        {
            Session = null;
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Off;
            return new DesktopArtifactPreviewReceipt(
                DesktopArtifactPreviewAdmission.SessionExpired,
                Status,
                session.Fingerprint);
        }
        if (Session != null
            && !Session.RuntimeIdentity.Matches(session.RuntimeIdentity))
        {
            Session = null;
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Off;
        }
        var preservesActiveTransition = Session != null
            && string.Equals(
                Session.Fingerprint,
                session.Fingerprint,
                StringComparison.Ordinal)
            && Status == DesktopArtifactPreviewStatus.TransitionPreview;
        Session = session;
        if (!preservesActiveTransition)
            Status = DesktopArtifactPreviewStatus.Ready;
        return new DesktopArtifactPreviewReceipt(
            DesktopArtifactPreviewAdmission.Ready,
            Status,
            session.Fingerprint);
    }

    internal DesktopArtifactPreviewReceipt ObserveInput(
        DesktopArtifactPreviewRequest request,
        DateTimeOffset observedAt)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        observedAt = DesktopArtifactRuntimeIdentity.Timestamp(
            observedAt,
            nameof(observedAt));
        var session = Session;
        if (session == null || Status == DesktopArtifactPreviewStatus.Off)
            return Reject(DesktopArtifactPreviewAdmission.SessionExpired);
        if (observedAt >= session.LeaseExpiresAt)
        {
            Session = null;
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Off;
            return Reject(DesktopArtifactPreviewAdmission.SessionExpired);
        }
        if (observedAt < request.RequestedAt)
            return Reject(DesktopArtifactPreviewAdmission.RequestNotYetValid);
        if (observedAt >= request.ExpiresAt)
            return Reject(DesktopArtifactPreviewAdmission.RequestExpired);
        if (!string.Equals(
                session.SurfaceInstanceId,
                request.SurfaceInstanceId,
                StringComparison.Ordinal))
        {
            return Reject(
                DesktopArtifactPreviewAdmission.SurfaceIdentityMismatch);
        }
        if (!session.RuntimeIdentity.MatchesProcess(request.RuntimeIdentity))
        {
            return Reject(
                DesktopArtifactPreviewAdmission.RuntimeIdentityMismatch);
        }
        if (session.RuntimeIdentity.RuntimeGeneration
            != request.RuntimeIdentity.RuntimeGeneration)
        {
            return Reject(
                DesktopArtifactPreviewAdmission.RuntimeGenerationMismatch);
        }
        if (request.InputSequence <= LastInputSequence)
        {
            return Reject(
                DesktopArtifactPreviewAdmission.InputSequenceRejected);
        }
        LastInputSequence = request.InputSequence;
        if (request.InputKind == DesktopArtifactInputKind.PrimarySingleClick)
        {
            return new DesktopArtifactPreviewReceipt(
                DesktopArtifactPreviewAdmission.SingleClickObserved,
                Status,
                request.Fingerprint);
        }
        if (Status == DesktopArtifactPreviewStatus.TransitionPreview)
        {
            return Reject(
                DesktopArtifactPreviewAdmission.TransitionAlreadyActive);
        }
        Status = DesktopArtifactPreviewStatus.TransitionPreview;
        TransitionEndsAt = observedAt.Add(TransitionDuration);
        return new DesktopArtifactPreviewReceipt(
            DesktopArtifactPreviewAdmission.TransitionPreviewAccepted,
            Status,
            request.Fingerprint);

        DesktopArtifactPreviewReceipt Reject(
            DesktopArtifactPreviewAdmission admission)
            => new(admission, Status, request.Fingerprint);
    }

    internal void Tick(DateTimeOffset observedAt)
    {
        observedAt = DesktopArtifactRuntimeIdentity.Timestamp(
            observedAt,
            nameof(observedAt));
        if (Session != null && observedAt >= Session.LeaseExpiresAt)
        {
            Session = null;
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Off;
            return;
        }
        if (Status == DesktopArtifactPreviewStatus.TransitionPreview
            && TransitionEndsAt.HasValue
            && observedAt >= TransitionEndsAt.Value)
        {
            TransitionEndsAt = null;
            Status = DesktopArtifactPreviewStatus.Ready;
        }
    }
}

internal sealed class DesktopArtifactPreviewAuthorityCounters
{
    internal int FileCreates => 0;
    internal int FolderCreates => 0;
    internal int ShortcutCreates => 0;
    internal int ShellExtensions => 0;
    internal int ControlPlaneWrites => 0;
    internal int PromptWrites => 0;
    internal int ProviderCalls => 0;
    internal int MemoryReads => 0;
    internal int MemoryWrites => 0;
    internal int DecisionOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int CapabilityOutputs => 0;
    internal int PlayerDataWrites => 0;
    internal int Total => 0;
}
