using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LilithPermissionControl;

internal enum D045ExpectedTargetKind
{
    None = 0,
    File = 1,
    Directory = 2,
    Application = 3
}

internal enum D045HarnessOutcomeReason
{
    Unspecified = 0,
    EffectVerified = 1,
    PermissionDefaultOff = 2,
    PermissionChangedBeforeAttempt = 3,
    PermissionChangedAfterAttempt = 4,
    TaskAlreadyConsumed = 5,
    InvalidRequest = 6,
    TargetNotFound = 7,
    TargetAmbiguous = 8,
    TargetWrongKind = 9,
    TargetLeafMismatch = 10,
    Timeout = 11,
    ToolFailure = 12,
    TargetChanged = 13
}

internal enum D045HarnessVerificationStrength
{
    None = 0,
    PreconditionsValidated = 1,
    EffectPossiblyApplied = 2,
    DeterministicPostcondition = 3
}

internal enum D045HarnessPostconditionCode
{
    None = 0,
    ReadResultBounded = 1,
    HttpSuccessStatus = 2,
    ProcessExitCodeZero = 3,
    TargetContentStable = 4,
    TargetEnumerationStable = 5,
    TargetExists = 6,
    TargetAbsent = 7,
    ProcessRunning = 8,
    ProcessExited = 9,
    DispatchAccepted = 10,
    VisibleApplicationWindowObserved = 11
}

internal static class D045ExactWindowTarget
{
    private const string Prefix = "d045win:";

    internal static string Format(int processId, IntPtr handle)
    {
        if (processId <= 0 || handle == IntPtr.Zero)
            throw new ArgumentOutOfRangeException(nameof(processId));
        return Prefix
            + processId.ToString(CultureInfo.InvariantCulture)
            + ":"
            + handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
    }

    internal static bool TryParse(
        string? value,
        out int processId,
        out IntPtr handle)
    {
        processId = 0;
        handle = IntPtr.Zero;
        var text = (value ?? string.Empty).Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        var parts = text[Prefix.Length..].Split(':');
        if (parts.Length != 2
            || !int.TryParse(
                parts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out processId)
            || processId <= 0
            || !long.TryParse(
                parts[1],
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var handleValue)
            || handleValue == 0)
        {
            processId = 0;
            return false;
        }
        handle = new IntPtr(handleValue);
        return true;
    }
}

internal sealed class D045HarnessRequest
{
    private readonly object _dispatchGate = new();
    private Func<bool>? _dispatchAdmission;
    private readonly Action? _beforeHostIdentityRecapture;
    private int _dispatchState;
    private int _identityRecaptureState;
    internal Guid TaskId { get; }
    internal D045PermissionCapability Capability { get; }
    internal string Target { get; }
    internal string SecondaryTarget { get; }
    internal string Payload { get; }
    internal int TimeoutMilliseconds { get; }
    internal D045ExpectedTargetKind ExpectedTargetKind { get; }
    internal string ExpectedLeafName { get; }
    internal long ExpectedPermissionRevision { get; }
    /// <summary>
    /// Host-only identity captured while an opaque semantic target reference
    /// was redeemed.  This value is never projected to a provider, receipt
    /// output or log; only the request fingerprint binds it indirectly.
    /// </summary>
    internal string ExpectedHostIdentityFingerprint { get; }
    internal bool RequireVisibleApplicationWindowTarget { get; }

    internal D045HarnessRequest(
        Guid taskId,
        D045PermissionCapability capability,
        string? target = null,
        string? secondaryTarget = null,
        string? payload = null,
        int timeoutMilliseconds = 30000,
        D045ExpectedTargetKind expectedTargetKind = D045ExpectedTargetKind.None,
        string expectedLeafName = "",
        long expectedPermissionRevision = 0,
        string expectedHostIdentityFingerprint = "",
        Action? beforeHostIdentityRecapture = null,
        bool requireVisibleApplicationWindowTarget = false)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("A task identity is required.", nameof(taskId));
        D045PermissionControlSnapshot.DemandCapability(capability);
        if (timeoutMilliseconds is < 1000 or > 120000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (!Enum.IsDefined(typeof(D045ExpectedTargetKind), expectedTargetKind))
            throw new ArgumentOutOfRangeException(nameof(expectedTargetKind));
        if (expectedPermissionRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPermissionRevision));
        var normalizedHostIdentity = (expectedHostIdentityFingerprint
                ?? string.Empty)
            .Trim()
            .ToUpperInvariant();
        if (normalizedHostIdentity.Length != 0
            && (normalizedHostIdentity.Length != 64
                || normalizedHostIdentity.Any(character =>
                    character is not (>= '0' and <= '9')
                        and not (>= 'A' and <= 'F'))))
        {
            throw new ArgumentException(
                "Expected host identity must be empty or one SHA-256 fingerprint.",
                nameof(expectedHostIdentityFingerprint));
        }
        if (normalizedHostIdentity.Length != 0
            && !CapabilitySupportsHostIdentity(capability, expectedTargetKind))
        {
            throw new ArgumentException(
                "A redeemed host identity requires the exact semantic capability and target kind.",
                nameof(expectedHostIdentityFingerprint));
        }
        if (requireVisibleApplicationWindowTarget
            && capability != D045PermissionCapability.ApplicationLaunch)
        {
            throw new ArgumentException(
                "A visible application-window target can be required only for Application Launch.",
                nameof(requireVisibleApplicationWindowTarget));
        }
        var normalizedExpectedLeaf = NormalizeLeafName(expectedLeafName);
        if (expectedLeafName is null
            || normalizedExpectedLeaf.Length > 255
            || normalizedExpectedLeaf.IndexOf('\0') >= 0
            || normalizedExpectedLeaf.IndexOfAny(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            }) >= 0
            || normalizedExpectedLeaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || normalizedExpectedLeaf is "." or "..")
        {
            throw new ArgumentException(
                "Expected leaf name must be empty or one valid filesystem leaf of at most 255 characters.",
                nameof(expectedLeafName));
        }
        TaskId = taskId;
        Capability = capability;
        Target = (target ?? string.Empty).Trim();
        SecondaryTarget = (secondaryTarget ?? string.Empty).Trim();
        Payload = payload ?? string.Empty;
        TimeoutMilliseconds = timeoutMilliseconds;
        ExpectedTargetKind = expectedTargetKind;
        ExpectedLeafName = normalizedExpectedLeaf;
        ExpectedPermissionRevision = expectedPermissionRevision;
        ExpectedHostIdentityFingerprint = normalizedHostIdentity;
        RequireVisibleApplicationWindowTarget =
            requireVisibleApplicationWindowTarget;
        _beforeHostIdentityRecapture = beforeHostIdentityRecapture;
    }

    internal static string NormalizeLeafName(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormC);

    internal void BindDispatchAdmission(Func<bool>? admission)
    {
        if (admission is null)
            return;
        lock (_dispatchGate)
        {
            if (_dispatchAdmission is not null || _dispatchState != 0)
            {
                throw new InvalidOperationException(
                    "A Harness request can bind one dispatch admission only.");
            }
            _dispatchAdmission = admission;
        }
    }

    internal bool TryAdmitEffectDispatch()
    {
        lock (_dispatchGate)
        {
            if (_dispatchState != 0)
                return _dispatchState > 0;
            _dispatchState = _dispatchAdmission is null
                || _dispatchAdmission()
                ? 1
                : -1;
            return _dispatchState > 0;
        }
    }

    internal bool EffectDispatchAdmitted
    {
        get
        {
            lock (_dispatchGate)
                return _dispatchState > 0;
        }
    }

    internal void InvokeBeforeHostIdentityRecapture()
    {
        Action? callback;
        lock (_dispatchGate)
        {
            if (_identityRecaptureState != 0)
                return;
            _identityRecaptureState = 1;
            callback = _beforeHostIdentityRecapture;
        }
        callback?.Invoke();
    }

    private static bool CapabilitySupportsHostIdentity(
        D045PermissionCapability capability,
        D045ExpectedTargetKind kind)
        => capability switch
        {
            D045PermissionCapability.LocalFileRead =>
                kind == D045ExpectedTargetKind.File,
            D045PermissionCapability.LocalFolderEnumerate =>
                kind == D045ExpectedTargetKind.Directory,
            D045PermissionCapability.ApplicationLaunch =>
                kind == D045ExpectedTargetKind.Application,
            D045PermissionCapability.LocalContentOpen =>
                kind is D045ExpectedTargetKind.File
                    or D045ExpectedTargetKind.Directory,
            _ => false
        };
}

/// <summary>
/// Captures an in-process, host-only identity for the object currently at one
/// canonical path.  Path identity is bound separately by the Harness request;
/// this fingerprint detects replacement of the object at that same path.
/// </summary>
internal static class D045HarnessHostTargetIdentity
{
    private const int FileIdInfoClass = 18;

    internal static string Capture(
        string canonicalPath,
        D045ExpectedTargetKind kind)
    {
        if (kind is not (D045ExpectedTargetKind.File
                or D045ExpectedTargetKind.Directory
                or D045ExpectedTargetKind.Application))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var isDirectory = kind == D045ExpectedTargetKind.Directory;
        if ((isDirectory && !Directory.Exists(canonicalPath))
            || (!isDirectory && !File.Exists(canonicalPath)))
        {
            throw new FileNotFoundException(
                "The host identity target must exist while it is captured.",
                canonicalPath);
        }

        var attributes = File.GetAttributes(canonicalPath);
        var actualDirectory = (attributes & FileAttributes.Directory) != 0;
        if (actualDirectory != isDirectory
            || (attributes & (FileAttributes.Device
                    | FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException(
                "The host identity target kind changed or is not a regular local item.");
        }

        long length;
        DateTime creationUtc;
        DateTime lastWriteUtc;
        if (isDirectory)
        {
            var directory = new DirectoryInfo(canonicalPath);
            directory.Refresh();
            length = 0;
            creationUtc = directory.CreationTimeUtc;
            lastWriteUtc = directory.LastWriteTimeUtc;
        }
        else
        {
            var file = new FileInfo(canonicalPath);
            file.Refresh();
            length = file.Length;
            creationUtc = file.CreationTimeUtc;
            lastWriteUtc = file.LastWriteTimeUtc;
        }

        var leaf = Path.GetFileName(canonicalPath)
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant();
        var nativeIdentity = isDirectory
            ? string.Empty
            : TryReadWindowsFileIdentity(canonicalPath);
        var builder = new StringBuilder(
            "D045.SEMANTIC.HOST-TARGET-IDENTITY.V1\n",
            384);
        AppendIdentityField(
            builder,
            ((int)kind).ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(builder, leaf);
        AppendIdentityField(builder, length.ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(
            builder,
            creationUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(
            builder,
            lastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(
            builder,
            ((long)attributes).ToString(CultureInfo.InvariantCulture));
        AppendIdentityField(builder, nativeIdentity);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString())));
    }

    internal static bool TryCaptureNativeFileIdentity(
        string canonicalPath,
        out string nativeIdentity)
    {
        nativeIdentity = TryReadWindowsFileIdentity(canonicalPath);
        return nativeIdentity.Length != 0;
    }

    private static void AppendIdentityField(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string TryReadWindowsFileIdentity(string canonicalPath)
    {
        if (!OperatingSystem.IsWindows())
            return string.Empty;
        try
        {
            using var stream = new FileStream(
                canonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandleEx(
                    stream.SafeFileHandle,
                    FileIdInfoClass,
                    out var identity,
                    Marshal.SizeOf<D045FileIdInfo>()))
            {
                return string.Empty;
            }
            var fileId = BitConverter.GetBytes(identity.FileIdLow)
                .Concat(BitConverter.GetBytes(identity.FileIdHigh))
                .ToArray();
            return identity.VolumeSerialNumber.ToString(
                    "X16",
                    CultureInfo.InvariantCulture)
                + ":"
                + Convert.ToHexString(fileId);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D045FileIdInfo
    {
        internal ulong VolumeSerialNumber;
        internal ulong FileIdLow;
        internal ulong FileIdHigh;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int informationClass,
        out D045FileIdInfo information,
        int size);
}

internal sealed class D045HarnessReceipt
{
    internal Guid TaskId { get; }
    internal D045PermissionCapability Capability { get; }
    internal bool Attempted { get; }
    internal bool Verified { get; }
    internal int ExitCode { get; }
    internal string Message { get; }
    internal string Output { get; }
    internal long PermissionRevision { get; }
    internal string RequestFingerprint { get; }
    internal string OutcomeFingerprint { get; }
    internal string ResolvedTargetFingerprint { get; }
    internal D045ExpectedTargetKind ResolvedTargetKind { get; }
    internal string ResolvedExtension { get; }
    internal string ResolvedLeafNameFingerprint { get; }
    internal string ContentSha256 { get; }
    internal D045HarnessOutcomeReason OutcomeReason { get; }
    internal D045HarnessVerificationStrength VerificationStrength { get; }
    internal D045HarnessPostconditionCode PostconditionCode { get; }
    internal bool RetryAllowed { get; }
    internal bool RetryRequiresChangedTarget { get; }
    internal bool EffectPossiblyApplied =>
        VerificationStrength == D045HarnessVerificationStrength.EffectPossiblyApplied;
    // Compatibility-only target/secondary identity. It does not bind the
    // capability, payload or timeout and is not complete request evidence.
    internal string TargetFingerprint { get; }

    internal D045HarnessReceipt(
        D045HarnessRequest request,
        bool attempted,
        bool verified,
        int exitCode,
        string message,
        string output,
        long permissionRevision = 0,
        string? resolvedTarget = null,
        string? contentSha256 = null,
        D045ExpectedTargetKind resolvedTargetKind = D045ExpectedTargetKind.None,
        string? resolvedExtension = null,
        D045HarnessOutcomeReason outcomeReason = D045HarnessOutcomeReason.Unspecified,
        D045HarnessVerificationStrength verificationStrength = D045HarnessVerificationStrength.None,
        D045HarnessPostconditionCode postconditionCode = D045HarnessPostconditionCode.None,
        bool? retryAllowed = null,
        bool retryRequiresChangedTarget = false)
    {
        if (!Enum.IsDefined(typeof(D045HarnessOutcomeReason), outcomeReason))
            throw new ArgumentOutOfRangeException(nameof(outcomeReason));
        if (!Enum.IsDefined(typeof(D045HarnessVerificationStrength), verificationStrength))
            throw new ArgumentOutOfRangeException(nameof(verificationStrength));
        if (!Enum.IsDefined(typeof(D045HarnessPostconditionCode), postconditionCode))
            throw new ArgumentOutOfRangeException(nameof(postconditionCode));
        TaskId = request.TaskId;
        Capability = request.Capability;
        Attempted = attempted;
        Verified = verified;
        ExitCode = exitCode;
        Message = message ?? string.Empty;
        Output = output ?? string.Empty;
        PermissionRevision = permissionRevision;
        OutcomeReason = NormalizeOutcomeReason(
            attempted,
            verified,
            outcomeReason);
        VerificationStrength = NormalizeVerificationStrength(
            attempted,
            verified,
            verificationStrength);
        PostconditionCode = verified
            ? postconditionCode == D045HarnessPostconditionCode.None
                ? DefaultPostconditionFor(request.Capability)
                : postconditionCode
            : postconditionCode;
        RetryAllowed = NormalizeRetryAllowed(OutcomeReason, retryAllowed);
        RetryRequiresChangedTarget = RetryAllowed
            && (retryRequiresChangedTarget
                || OutcomeReason is D045HarnessOutcomeReason.TargetNotFound
                    or D045HarnessOutcomeReason.TargetAmbiguous);
        RequestFingerprint = ComputeRequestFingerprint(request);
        OutcomeFingerprint = ComputeOutcomeFingerprint(
            Attempted,
            Verified,
            ExitCode,
            Message,
            Output,
            OutcomeReason,
            VerificationStrength,
            PostconditionCode,
            RetryAllowed,
            RetryRequiresChangedTarget);
        ResolvedTargetFingerprint = string.IsNullOrWhiteSpace(resolvedTarget)
            ? string.Empty
            : Sha256Hex(Path.GetFullPath(resolvedTarget));
        ResolvedTargetKind = resolvedTargetKind;
        ResolvedExtension = NormalizeExtension(resolvedExtension);
        ResolvedLeafNameFingerprint = string.IsNullOrWhiteSpace(resolvedTarget)
            ? string.Empty
            : Sha256Hex(NormalizeLeafForIdentity(Path.GetFileName(
                Path.GetFullPath(resolvedTarget))));
        ContentSha256 = contentSha256 ?? string.Empty;
        TargetFingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(request.Target + "\n" + request.SecondaryTarget)));
    }

    private D045HarnessReceipt(
        D045HarnessReceipt source,
        long permissionRevision,
        bool? verified = null,
        string? message = null,
        D045HarnessOutcomeReason? outcomeReason = null,
        D045HarnessVerificationStrength? verificationStrength = null,
        D045HarnessPostconditionCode? postconditionCode = null,
        bool? retryAllowed = null,
        bool? retryRequiresChangedTarget = null)
    {
        TaskId = source.TaskId;
        Capability = source.Capability;
        Attempted = source.Attempted;
        Verified = verified ?? source.Verified;
        ExitCode = source.ExitCode;
        Message = message ?? source.Message;
        Output = source.Output;
        PermissionRevision = permissionRevision;
        OutcomeReason = outcomeReason ?? source.OutcomeReason;
        VerificationStrength = verificationStrength ?? source.VerificationStrength;
        PostconditionCode = postconditionCode ?? source.PostconditionCode;
        RetryAllowed = NormalizeRetryAllowed(
            OutcomeReason,
            retryAllowed ?? source.RetryAllowed);
        RetryRequiresChangedTarget = RetryAllowed
            && ((retryRequiresChangedTarget ?? source.RetryRequiresChangedTarget)
                || OutcomeReason is D045HarnessOutcomeReason.TargetNotFound
                    or D045HarnessOutcomeReason.TargetAmbiguous);
        RequestFingerprint = source.RequestFingerprint;
        OutcomeFingerprint = ComputeOutcomeFingerprint(
            Attempted,
            Verified,
            ExitCode,
            Message,
            Output,
            OutcomeReason,
            VerificationStrength,
            PostconditionCode,
            RetryAllowed,
            RetryRequiresChangedTarget);
        ResolvedTargetFingerprint = source.ResolvedTargetFingerprint;
        ResolvedTargetKind = source.ResolvedTargetKind;
        ResolvedExtension = source.ResolvedExtension;
        ResolvedLeafNameFingerprint = source.ResolvedLeafNameFingerprint;
        ContentSha256 = source.ContentSha256;
        TargetFingerprint = source.TargetFingerprint;
    }

    internal D045HarnessReceipt WithPermissionRevision(long permissionRevision)
        => new(this, permissionRevision);

    internal D045HarnessReceipt RejectAfterPermissionChange(
        long permissionRevision)
        => new(
            this,
            permissionRevision,
            verified: false,
            message: Attempted
                ? "Permission changed after the operation may have been attempted; automatic retry is forbidden."
                : "Permission changed before the operation was attempted.",
            outcomeReason: Attempted
                ? D045HarnessOutcomeReason.PermissionChangedAfterAttempt
                : D045HarnessOutcomeReason.PermissionChangedBeforeAttempt,
            verificationStrength: Attempted
                ? D045HarnessVerificationStrength.EffectPossiblyApplied
                : D045HarnessVerificationStrength.None,
            retryAllowed: false,
            retryRequiresChangedTarget: false);

    private static string ComputeRequestFingerprint(D045HarnessRequest request)
    {
        var builder = new StringBuilder("D045.HARNESS.REQUEST.V4\n", 512);
        AppendFingerprintField(
            builder,
            ((int)request.Capability).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(builder, request.Target);
        AppendFingerprintField(builder, request.SecondaryTarget);
        AppendFingerprintField(builder, Sha256Hex(request.Payload));
        AppendFingerprintField(
            builder,
            request.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(
            builder,
            ((int)request.ExpectedTargetKind).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(
            builder,
            NormalizeLeafForIdentity(request.ExpectedLeafName));
        AppendFingerprintField(
            builder,
            request.ExpectedPermissionRevision.ToString(
                CultureInfo.InvariantCulture));
        AppendFingerprintField(
            builder,
            request.ExpectedHostIdentityFingerprint);
        AppendFingerprintField(
            builder,
            request.RequireVisibleApplicationWindowTarget ? "1" : "0");
        return Sha256Hex(builder.ToString());
    }

    private static string ComputeOutcomeFingerprint(
        bool attempted,
        bool verified,
        int exitCode,
        string message,
        string output,
        D045HarnessOutcomeReason outcomeReason,
        D045HarnessVerificationStrength verificationStrength,
        D045HarnessPostconditionCode postconditionCode,
        bool retryAllowed,
        bool retryRequiresChangedTarget)
    {
        var builder = new StringBuilder("D045.HARNESS.OUTCOME.V2\n", 384);
        AppendFingerprintField(builder, attempted ? "1" : "0");
        AppendFingerprintField(builder, verified ? "1" : "0");
        AppendFingerprintField(
            builder,
            exitCode.ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(builder, Sha256Hex(message));
        AppendFingerprintField(builder, Sha256Hex(output));
        AppendFingerprintField(
            builder,
            ((int)outcomeReason).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(
            builder,
            ((int)verificationStrength).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(
            builder,
            ((int)postconditionCode).ToString(CultureInfo.InvariantCulture));
        AppendFingerprintField(builder, retryAllowed ? "1" : "0");
        AppendFingerprintField(builder, retryRequiresChangedTarget ? "1" : "0");
        return Sha256Hex(builder.ToString());
    }

    private static D045HarnessOutcomeReason NormalizeOutcomeReason(
        bool attempted,
        bool verified,
        D045HarnessOutcomeReason requested)
    {
        if (verified)
            return D045HarnessOutcomeReason.EffectVerified;
        if (requested != D045HarnessOutcomeReason.Unspecified)
            return requested;
        return attempted
            ? D045HarnessOutcomeReason.ToolFailure
            : D045HarnessOutcomeReason.InvalidRequest;
    }

    private static D045HarnessVerificationStrength NormalizeVerificationStrength(
        bool attempted,
        bool verified,
        D045HarnessVerificationStrength requested)
    {
        if (verified)
            return D045HarnessVerificationStrength.DeterministicPostcondition;
        if (requested != D045HarnessVerificationStrength.None)
            return requested;
        return attempted
            ? D045HarnessVerificationStrength.EffectPossiblyApplied
            : D045HarnessVerificationStrength.None;
    }

    private static bool DefaultRetryAllowed(D045HarnessOutcomeReason reason)
        => reason is D045HarnessOutcomeReason.TargetNotFound
            or D045HarnessOutcomeReason.TargetAmbiguous;

    private static bool NormalizeRetryAllowed(
        D045HarnessOutcomeReason reason,
        bool? requested)
        => (requested ?? DefaultRetryAllowed(reason))
            && reason is D045HarnessOutcomeReason.TargetNotFound
                or D045HarnessOutcomeReason.TargetAmbiguous;

    private static D045HarnessPostconditionCode DefaultPostconditionFor(
        D045PermissionCapability capability)
        => capability switch
        {
            D045PermissionCapability.SanitizedLilithStatusRead
                or D045PermissionCapability.ClipboardRead =>
                D045HarnessPostconditionCode.ReadResultBounded,
            D045PermissionCapability.PublicHttpsUrlFetch
                or D045PermissionCapability.ScreenshotUpload
                or D045PermissionCapability.LocalContentExternalShare =>
                D045HarnessPostconditionCode.HttpSuccessStatus,
            D045PermissionCapability.LocalFileRead =>
                D045HarnessPostconditionCode.TargetContentStable,
            D045PermissionCapability.LocalFolderEnumerate =>
                D045HarnessPostconditionCode.TargetEnumerationStable,
            D045PermissionCapability.ScreenshotCapture
                or D045PermissionCapability.LocalDataCreate =>
                D045HarnessPostconditionCode.TargetExists,
            D045PermissionCapability.GracefulProcessClose
                or D045PermissionCapability.ForceTerminateProcess =>
                D045HarnessPostconditionCode.ProcessExited,
            D045PermissionCapability.RecycleBinDelete
                or D045PermissionCapability.PermanentDelete =>
                D045HarnessPostconditionCode.TargetAbsent,
            D045PermissionCapability.Shutdown
                or D045PermissionCapability.Restart
                or D045PermissionCapability.RawShellArbitraryCommand =>
                D045HarnessPostconditionCode.ProcessExitCodeZero,
            D045PermissionCapability.GeneralElevationAdminAccess
                or D045PermissionCapability.ApplicationLaunch =>
                D045HarnessPostconditionCode.ProcessRunning,
            D045PermissionCapability.ArbitraryExternalAppInputKeystrokes
                or D045PermissionCapability.LocalContentOpen =>
                D045HarnessPostconditionCode.DispatchAccepted,
            _ => D045HarnessPostconditionCode.None
        };

    private static void AppendFingerprintField(
        StringBuilder builder,
        string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string NormalizeLeafForIdentity(string? value)
        => D045HarnessRequest.NormalizeLeafName(value).ToUpperInvariant();

    private static string NormalizeExtension(string? value)
    {
        var extension = (value ?? string.Empty).Trim();
        if (extension.Length == 0)
            return string.Empty;
        if (extension[0] != '.')
            extension = "." + extension;
        return extension.ToLowerInvariant();
    }
}

/// <summary>
/// The sole execution owner for D-045 local capabilities. Every request is
/// single-use, bound to one permission-store revision and followed by an
/// operation-specific local postcondition check before Verified can be true.
/// </summary>
internal sealed partial class D045LocalTaskHarness
{
    internal const int MaximumTextOutputCharacters = 64 * 1024;
    internal const int MaximumFileReadBytes = 1024 * 1024;
    internal const int MaximumUploadBytes = 8 * 1024 * 1024;
    internal const int MaximumFolderEntries = 256;
    internal const int MaximumCreatedDataBytes = 1024 * 1024;
    internal const int MaximumKnownFolderResolutionEntries = 256;
    internal const int MaximumKnownFolderRecursiveDepth = 4;
    internal const int MaximumKnownFolderRecursiveEntries = 1024;

    private static readonly IReadOnlyDictionary<string, Guid> KnownFolderIds =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["desktop"] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
            ["documents"] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
            ["downloads"] = new("374DE290-123F-4565-9164-39C4925E467B"),
            ["pictures"] = new("33E28130-4E1E-4676-835A-98395C3BC3BB")
        };

    private static readonly HashSet<string> SafeLocalContentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".rtf", ".pdf",
            ".doc", ".docx", ".odt", ".xls", ".xlsx", ".ods",
            ".csv", ".tsv", ".ppt", ".pptx", ".odp",
            ".json", ".xml", ".yaml", ".yml", ".log",
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
            ".tif", ".tiff",
            ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac",
            ".wma", ".opus",
            ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv",
            ".m4v", ".mpeg", ".mpg",
            ".stl", ".obj", ".ply", ".3mf", ".glb", ".gltf",
            ".fbx", ".dae", ".step", ".stp", ".iges", ".igs",
            ".dxf", ".dwg",
            ".psd", ".psb", ".xcf", ".kra", ".sai", ".sai2",
            ".clip", ".afdesign", ".afphoto", ".afpub"
        };

    private static readonly HashSet<string> BlockedApplicationHostNames =
        new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "powershell_ise", "pwsh", "cmd", "command",
        "wscript", "cscript", "mshta", "rundll32", "regsvr32", "msiexec",
        "python", "python3", "pythonw", "py", "node", "nodejs", "npm", "npx",
        "deno", "bun", "bash", "sh", "wsl", "wt", "windowsterminal",
        "dotnet", "csi", "fsi", "java", "javaw", "jshell", "perl", "ruby",
        "php", "lua", "msbuild", "installutil", "regasm", "regsvcs", "cmstp",
        "wmic", "netsh", "nslookup", "diskpart", "diskshadow", "regedit",
        "regedt32", "schtasks", "reg", "forfiles", "sqlcmd", "osql", "mysql",
        "psql", "sqlite3", "ftp", "sftp", "telnet", "debug", "cdb", "windbg"
    };

    private static readonly HashSet<string> BlockedApplicationHostIdentities =
        new(
            BlockedApplicationHostNames
                .Select(NormalizeApplicationHostIdentity)
                .Concat(new[]
                {
                    "windowscommandprocessor",
                    "windowscommandprompt",
                    "commandprompt",
                    "windowspowershell",
                    "microsoftpowershell",
                    "powershellcore",
                    "pythonlauncher",
                    "pythoninterpreter",
                    "nodejsruntime",
                    "microsoftwindowsbasedscripthost",
                    "microsoftconsolebasedscripthost",
                    "microsofthtmlapplicationhost",
                    "windowshostprocessrundll32",
                    "microsoftregisterserver"
                }),
            StringComparer.Ordinal);

    private readonly D045PermissionControlStore _permissions;
    private readonly Func<string> _sanitizedStatusProvider;
    private readonly Func<string, string?> _knownFolderPathResolver;
    private readonly Func<string, int> _localContentOpenDispatcher;
    private readonly Func<string, IPAddress[]> _publicHostResolver;
    private readonly Func<IPAddress, int, CancellationToken, ValueTask<Stream>>
        _pinnedConnectionFactory;
    private readonly Func<string, IReadOnlyList<D045ApplicationCandidate>>?
        _applicationCandidateProvider;
    private readonly Func<string, string, int, D045ApplicationLaunchAttempt>
        _applicationLauncher;
    private readonly Func<string, IReadOnlyList<D045VisibleApplicationWindow>>
        _visibleApplicationWindowResolver;
    private readonly Action<int> _applicationLaunchDelay;
    private readonly Func<IntPtr, int, bool> _exactVisibleWindowValidator;
    private readonly Func<IntPtr, string, bool> _exactWindowTextDispatcher;
    private readonly Func<IntPtr, bool> _foregroundWindowValidator;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _consumedTasks = new();

    internal D045LocalTaskHarness(
        D045PermissionControlStore permissions,
        Func<string>? sanitizedStatusProvider = null,
        Func<string, string?>? knownFolderPathResolver = null,
        Func<string, int>? localContentOpenDispatcher = null,
        Func<string, IPAddress[]>? publicHostResolver = null,
        Func<IPAddress, int, CancellationToken, ValueTask<Stream>>?
            pinnedConnectionFactory = null,
        Func<string, IReadOnlyList<D045ApplicationCandidate>>?
            applicationCandidateProvider = null,
        Func<string, string, int, D045ApplicationLaunchAttempt>?
            applicationLauncher = null,
        Func<string, IReadOnlyList<D045VisibleApplicationWindow>>?
            visibleApplicationWindowResolver = null,
        Action<int>? applicationLaunchDelay = null,
        Func<IntPtr, int, bool>? exactVisibleWindowValidator = null,
        Func<IntPtr, string, bool>? exactWindowTextDispatcher = null,
        Func<IntPtr, bool>? foregroundWindowValidator = null)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _sanitizedStatusProvider = sanitizedStatusProvider
            ?? (() => "Lilith local runtime is available.");
        _knownFolderPathResolver = knownFolderPathResolver
            ?? ResolveWindowsKnownFolderPath;
        _localContentOpenDispatcher = localContentOpenDispatcher
            ?? OpenLocalContentWithWindowsAssociation;
        _publicHostResolver = publicHostResolver
            ?? (host => Dns.GetHostAddresses(host));
        _pinnedConnectionFactory = pinnedConnectionFactory
            ?? ConnectPinnedSocketAsync;
        _applicationCandidateProvider = applicationCandidateProvider;
        _applicationLauncher = applicationLauncher
            ?? StartAndVerifyPlainApplication;
        _visibleApplicationWindowResolver = visibleApplicationWindowResolver
            ?? EnumerateVisibleExactImageWindows;
        _applicationLaunchDelay = applicationLaunchDelay ?? Thread.Sleep;
        _exactVisibleWindowValidator = exactVisibleWindowValidator
            ?? HasExactVisibleWindowIdentity;
        _exactWindowTextDispatcher = exactWindowTextDispatcher
            ?? DispatchUnicodeTextToExactWindow;
        _foregroundWindowValidator = foregroundWindowValidator
            ?? (window => GetForegroundWindow() == window);
    }

    internal D045HarnessReceipt Execute(D045HarnessRequest request)
        => Execute(request, dispatchAdmission: null);

    internal D045HarnessReceipt Execute(
        D045HarnessRequest request,
        Func<bool>? dispatchAdmission)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        request.BindDispatchAdmission(dispatchAdmission);

        D045PermissionControlSnapshot admitted;
        lock (_gate)
        {
            if (!_consumedTasks.Add(request.TaskId))
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Task identity was already consumed.",
                    outcomeReason: D045HarnessOutcomeReason.TaskAlreadyConsumed,
                    retryAllowed: false);
            }
            admitted = _permissions.Load().Snapshot;
        }

        if (!admitted.IsEnabled(request.Capability))
        {
            return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Permission is Default Off.",
                    outcomeReason: D045HarnessOutcomeReason.PermissionDefaultOff,
                    retryAllowed: false)
                .WithPermissionRevision(admitted.Revision);
        }
        if (request.ExpectedPermissionRevision > 0
            && admitted.Revision != request.ExpectedPermissionRevision)
        {
            return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Permission revision changed before effect dispatch.",
                    outcomeReason:
                        D045HarnessOutcomeReason.PermissionChangedBeforeAttempt,
                    retryAllowed: false)
                .WithPermissionRevision(admitted.Revision);
        }
        D045HarnessReceipt result;
        try
        {
            result = request.Capability switch
            {
                D045PermissionCapability.SanitizedLilithStatusRead =>
                    ReadSanitizedStatus(request),
                D045PermissionCapability.PublicHttpsUrlFetch =>
                    FetchPublicHttps(request),
                D045PermissionCapability.ClipboardRead => ReadClipboard(request),
                D045PermissionCapability.LocalFileRead => ReadLocalFile(request),
                D045PermissionCapability.LocalFolderEnumerate => EnumerateFolder(request),
                D045PermissionCapability.ScreenshotCapture => CaptureScreenshot(request),
                D045PermissionCapability.ScreenshotUpload => UploadFile(request, requirePng: true),
                D045PermissionCapability.LocalContentExternalShare => UploadFile(request, requirePng: false),
                D045PermissionCapability.GracefulProcessClose => CloseProcess(request, force: false),
                D045PermissionCapability.ForceTerminateProcess => CloseProcess(request, force: true),
                D045PermissionCapability.RecycleBinDelete => DeletePath(request, recycle: true),
                D045PermissionCapability.PermanentDelete => DeletePath(request, recycle: false),
                D045PermissionCapability.Shutdown => SchedulePower(request, restart: false),
                D045PermissionCapability.Restart => SchedulePower(request, restart: true),
                D045PermissionCapability.RawShellArbitraryCommand => RunShell(request),
                D045PermissionCapability.ArbitraryExternalAppInputKeystrokes => SendExternalInput(request),
                D045PermissionCapability.GeneralElevationAdminAccess => StartElevated(request),
                D045PermissionCapability.LocalDataCreate => CreateLocalData(request),
                D045PermissionCapability.ApplicationLaunch => LaunchApplication(request),
                D045PermissionCapability.LocalContentOpen => OpenLocalContent(request),
                _ => Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Unknown capability.",
                    outcomeReason: D045HarnessOutcomeReason.InvalidRequest,
                    retryAllowed: false)
            };
        }
        catch (D045HarnessRejectionException exception)
        {
            var attempted = request.EffectDispatchAdmitted;
            result = Receipt(
                request,
                attempted,
                false,
                -1,
                attempted
                    ? "Local task was rejected after effect dispatch; the effect may have been applied: "
                        + Limit(exception.Message)
                    : Limit(exception.Message),
                outcomeReason: exception.OutcomeReason,
                verificationStrength: attempted
                    ? D045HarnessVerificationStrength.EffectPossiblyApplied
                    : D045HarnessVerificationStrength.None,
                retryAllowed: !attempted && exception.RetryAllowed,
                retryRequiresChangedTarget:
                    !attempted && exception.RetryRequiresChangedTarget);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException)
        {
            var attempted = request.EffectDispatchAdmitted;
            result = Receipt(
                request,
                attempted,
                false,
                -1,
                attempted
                    ? "Local task target disappeared after effect dispatch; the effect may have been applied: "
                        + Limit(exception.Message)
                    : "Local task target was not found before effect dispatch: "
                        + Limit(exception.Message),
                outcomeReason: D045HarnessOutcomeReason.TargetNotFound,
                verificationStrength: attempted
                    ? D045HarnessVerificationStrength.EffectPossiblyApplied
                    : D045HarnessVerificationStrength.None,
                retryAllowed: !attempted,
                retryRequiresChangedTarget: !attempted);
        }
        catch (Exception exception) when (
            exception is TimeoutException
                or OperationCanceledException)
        {
            var attempted = request.EffectDispatchAdmitted;
            result = Receipt(
                request,
                attempted,
                false,
                -1,
                attempted
                    ? "Local task timed out after effect dispatch; the effect may have been applied: "
                        + Limit(exception.Message)
                    : "Local task timed out before effect dispatch; no effect was attempted: "
                        + Limit(exception.Message),
                outcomeReason: D045HarnessOutcomeReason.Timeout,
                verificationStrength:
                    attempted
                        ? D045HarnessVerificationStrength.EffectPossiblyApplied
                        : D045HarnessVerificationStrength.None,
                retryAllowed: false);
        }
        catch (Exception exception)
        {
            var attempted = request.EffectDispatchAdmitted;
            result = Receipt(
                request,
                attempted,
                false,
                -1,
                attempted
                    ? "Local task failed after effect dispatch; the effect may have been applied: "
                        + Limit(exception.Message)
                    : "Local task failed before effect dispatch; no effect was attempted: "
                        + Limit(exception.Message),
                outcomeReason: D045HarnessOutcomeReason.ToolFailure,
                verificationStrength:
                    attempted
                        ? D045HarnessVerificationStrength.EffectPossiblyApplied
                        : D045HarnessVerificationStrength.None,
                retryAllowed: false);
        }
        result = result.WithPermissionRevision(admitted.Revision);

        var current = _permissions.Load().Snapshot;
        if (current.Revision != admitted.Revision
            || !current.IsEnabled(request.Capability))
        {
            return result.RejectAfterPermissionChange(current.Revision);
        }
        return result;
    }

    private D045HarnessReceipt ReadSanitizedStatus(D045HarnessRequest request)
    {
        DemandEffectDispatch(request);
        var value = Limit(_sanitizedStatusProvider());
        return Receipt(request, true, value.Length > 0, 0,
            value.Length > 0 ? "Sanitized Lilith status read." : "No status was available.",
            value);
    }

    private D045HarnessReceipt FetchPublicHttps(D045HarnessRequest request)
    {
        var target = ResolvePinnedPublicHttps(request.Target);
        DemandEffectDispatch(request);
        using var cancellation = new CancellationTokenSource(request.TimeoutMilliseconds);
        using var client = CreatePinnedHttpClient(target);
        using var response = client.GetAsync(
                target.Uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellation.Token)
            .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return Receipt(request, true, false, (int)response.StatusCode,
                "HTTPS request returned " + (int)response.StatusCode + ".");
        using var stream = response.Content.ReadAsStreamAsync(cancellation.Token).GetAwaiter().GetResult();
        var bytes = ReadBounded(stream, MaximumFileReadBytes);
        var text = DecodeText(bytes);
        return Receipt(request, true, true, (int)response.StatusCode,
            "Public HTTPS response verified.", text);
    }

    private static D045HarnessReceipt ReadClipboard(D045HarnessRequest request)
    {
        DemandEffectDispatch(request);
        var result = RunPowerShell("Get-Clipboard -Raw", request.TimeoutMilliseconds);
        return Receipt(request, true, result.ExitCode == 0, result.ExitCode,
            result.ExitCode == 0 ? "Clipboard text read." : "Clipboard read failed.",
            result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError);
    }

    private static D045HarnessReceipt ReadLocalFile(D045HarnessRequest request)
    {
        var path = DemandExistingFile(request.Target);
        if (!ResolvedLeafMatchesExpected(
                path,
                isDirectory: false,
                request.ExpectedLeafName))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "The resolved file leaf does not match the trusted expected leaf name.",
                resolvedTarget: path,
                resolvedTargetKind: D045ExpectedTargetKind.File,
                resolvedExtension: Path.GetExtension(path),
                outcomeReason: D045HarnessOutcomeReason.TargetLeafMismatch,
                retryAllowed: false);
        }
        var file = new FileInfo(path);
        if (file.Length > MaximumFileReadBytes)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "File exceeds the 1 MiB Harness read limit.",
                resolvedTarget: path);
        }

        DemandEffectDispatch(
            request,
            path,
            D045ExpectedTargetKind.File);

        var beforeLength = file.Length;
        var beforeBytes = File.ReadAllBytes(path);
        var beforeHash = SHA256.HashData(beforeBytes);
        var contentSha256 = Convert.ToHexString(beforeHash);
        var afterBytes = File.ReadAllBytes(path);
        var afterLength = File.Exists(path) ? new FileInfo(path).Length : -1;
        var afterHash = SHA256.HashData(afterBytes);
        var unchanged = beforeLength == beforeBytes.LongLength
            && afterLength == afterBytes.LongLength
            && beforeBytes.LongLength == afterBytes.LongLength
            && CryptographicOperations.FixedTimeEquals(beforeHash, afterHash);
        if (!unchanged)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "File length or SHA-256 changed during read.",
                resolvedTarget: path,
                contentSha256: contentSha256);
        }

        var decoded = DecodeTextWithoutLimit(beforeBytes);
        if (decoded.Length > MaximumTextOutputCharacters)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "Decoded file output exceeds the 64 Ki-character receipt ceiling.",
                resolvedTarget: path,
                contentSha256: contentSha256);
        }
        return Receipt(
            request,
            true,
            true,
            0,
            "Exact file length and SHA-256 read verified.",
            decoded,
            resolvedTarget: path,
            contentSha256: contentSha256);
    }

    private D045HarnessReceipt EnumerateFolder(D045HarnessRequest request)
    {
        var recursiveSelector = IsExplicitRecursiveKnownFolderSelector(
            request.Target);
        var resolution = ResolveKnownOrExactPath(
            request.Target,
            allowFile: recursiveSelector,
            allowDirectory: true);
        if (!resolution.Resolved)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                resolution.Message,
                resolution.CandidateSummary,
                outcomeReason: resolution.OutcomeReason,
                retryAllowed: resolution.RetryAllowed,
                retryRequiresChangedTarget:
                    resolution.RetryRequiresChangedTarget);
        }
        var path = resolution.CanonicalPath;
        if (!TryValidateNoReparsePath(path, out var reparseMessage))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                reparseMessage,
                resolvedTarget: path);
        }
        DemandEffectDispatch(
            request,
            path,
            Directory.Exists(path)
                ? D045ExpectedTargetKind.Directory
                : D045ExpectedTargetKind.File);
        if (recursiveSelector && !Directory.Exists(path))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                    or DirectoryNotFoundException
                    or IOException
                    or UnauthorizedAccessException
                    or PathTooLongException)
            {
                return Receipt(
                    request,
                    true,
                    false,
                    -1,
                    "The recursively resolved file candidate changed during inspection.",
                    resolvedTarget: path,
                    resolvedTargetKind: D045ExpectedTargetKind.File,
                    resolvedExtension: Path.GetExtension(path));
            }
            if ((attributes & (FileAttributes.Directory
                    | FileAttributes.Device
                    | FileAttributes.ReparsePoint)) != 0)
            {
                return Receipt(
                    request,
                    true,
                    false,
                    -1,
                    "The recursively resolved candidate is not a regular local file.",
                    resolvedTarget: path,
                    resolvedTargetKind: D045ExpectedTargetKind.File,
                    resolvedExtension: Path.GetExtension(path));
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var exactTarget = BuildEnumeratedTargetToken(request.Target, path);
            var candidateOutput = string.Join(
                "\n",
                path,
                "kind=file",
                "extension=" + (extension.Length == 0 ? "none" : extension),
                "candidate=" + JsonSerializer.Serialize(new
                {
                    kind = "file",
                    extension = extension.Length == 0 ? "none" : extension,
                    name = Path.GetFileName(path),
                    target = exactTarget
                }));
            if (candidateOutput.Length > MaximumTextOutputCharacters)
            {
                return Receipt(
                    request,
                    true,
                    false,
                    -1,
                    "Typed recursive candidate exceeds the 64 Ki-character receipt ceiling.",
                    resolvedTarget: path,
                    resolvedTargetKind: D045ExpectedTargetKind.File,
                    resolvedExtension: extension);
            }
            var candidateStillExists = File.Exists(path);
            return Receipt(
                request,
                true,
                candidateStillExists,
                candidateStillExists ? 0 : -1,
                candidateStillExists
                    ? "Recursive known-folder file candidate verified without opening or reading it."
                    : "The recursive known-folder file candidate disappeared during inspection.",
                candidateOutput,
                resolvedTarget: path,
                resolvedTargetKind: D045ExpectedTargetKind.File,
                resolvedExtension: extension);
        }
        var entries = Directory.EnumerateFileSystemEntries(path)
            .Take(MaximumFolderEntries + 1)
            .Select(Path.GetFullPath)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length > MaximumFolderEntries)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "Folder exceeds the 256-entry Harness limit.",
                resolvedTarget: path,
                resolvedTargetKind: D045ExpectedTargetKind.Directory);
        }
        var output = new List<string>
        {
            path,
            "kind=directory",
            "extension=none"
        };
        foreach (var entry in entries)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException
                    or DirectoryNotFoundException
                    or IOException
                    or UnauthorizedAccessException
                    or PathTooLongException)
            {
                return Receipt(
                    request,
                    true,
                    false,
                    -1,
                    "A folder entry could not be typed completely.",
                    resolvedTarget: path,
                    resolvedTargetKind: D045ExpectedTargetKind.Directory);
            }

            var reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var kind = reparse
                ? "blocked-reparse"
                : isDirectory ? "directory" : "file";
            var extension = isDirectory
                ? string.Empty
                : Path.GetExtension(entry).ToLowerInvariant();
            var exactTarget = BuildEnumeratedTargetToken(request.Target, entry);
            output.Add("entry=" + JsonSerializer.Serialize(new
            {
                kind,
                extension = extension.Length == 0 ? "none" : extension,
                name = Path.GetFileName(entry),
                target = exactTarget
            }));
        }
        var rendered = string.Join("\n", output);
        if (rendered.Length > MaximumTextOutputCharacters)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "Typed folder enumeration exceeds the 64 Ki-character receipt ceiling.",
                resolvedTarget: path,
                resolvedTargetKind: D045ExpectedTargetKind.Directory);
        }
        var stillExists = Directory.Exists(path);
        return Receipt(request, true, stillExists, 0,
            stillExists ? $"Folder enumeration verified ({entries.Length} typed entries)." : "Folder disappeared during enumeration.",
            rendered,
            resolvedTarget: path,
            resolvedTargetKind: D045ExpectedTargetKind.Directory);
    }

    private string BuildEnumeratedTargetToken(
        string requestedFolder,
        string canonicalEntry)
    {
        var requested = requestedFolder.Trim();
        if (!requested.StartsWith("known:", StringComparison.OrdinalIgnoreCase))
            return canonicalEntry;

        var body = requested["known:".Length..];
        var separatorIndex = body.IndexOfAny(new[] { '/', '\\' });
        var key = separatorIndex < 0 ? body : body[..separatorIndex];
        if (!KnownFolderIds.ContainsKey(key))
            return canonicalEntry;
        var resolvedRoot = _knownFolderPathResolver(key);
        if (string.IsNullOrWhiteSpace(resolvedRoot))
            return canonicalEntry;
        try
        {
            var root = Path.GetFullPath(resolvedRoot);
            var entry = Path.GetFullPath(canonicalEntry);
            if (!IsExactDescendant(root, entry)
                || IsNetworkOrSharePath(root))
            {
                return canonicalEntry;
            }
            var relative = Path.GetRelativePath(root, entry)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            return "known:" + key.ToLowerInvariant() + "/" + relative;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return canonicalEntry;
        }
    }

    private static D045HarnessReceipt CaptureScreenshot(D045HarnessRequest request)
    {
        DemandEffectDispatch(request);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        var directory = Path.Combine(pictures, "Lilith Screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Lilith_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; " +
            "$b=[System.Windows.Forms.SystemInformation]::VirtualScreen; " +
            "$m=New-Object System.Drawing.Bitmap($b.Width,$b.Height); " +
            "$g=[System.Drawing.Graphics]::FromImage($m); " +
            "$g.CopyFromScreen($b.Left,$b.Top,0,0,$m.Size); " +
            "$m.Save('" + EscapePowerShell(path) + "',[System.Drawing.Imaging.ImageFormat]::Png); " +
            "$g.Dispose(); $m.Dispose();";
        var result = RunPowerShell(script, request.TimeoutMilliseconds);
        var verified = result.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 8;
        return Receipt(request, true, verified, result.ExitCode,
            verified ? "Screenshot captured and verified." : "Screenshot capture failed.",
            verified ? path : result.StandardError,
            resolvedTarget: path);
    }

    private D045HarnessReceipt UploadFile(D045HarnessRequest request, bool requirePng)
    {
        var path = DemandExistingFile(request.Target);
        if (requirePng && !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Screenshot upload requires an exact PNG path.",
                resolvedTarget: path);
        }
        var file = new FileInfo(path);
        if (file.Length <= 0 || file.Length > MaximumUploadBytes)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Upload exceeds the 8 MiB Harness limit.",
                resolvedTarget: path);
        }
        var target = ResolvePinnedPublicHttps(request.SecondaryTarget);
        DemandEffectDispatch(request);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            requirePng ? "image/png" : "application/octet-stream");
        using var cancellation = new CancellationTokenSource(request.TimeoutMilliseconds);
        using var client = CreatePinnedHttpClient(target);
        using var response = client.PostAsync(target.Uri, content, cancellation.Token)
            .GetAwaiter().GetResult();
        var verified = response.IsSuccessStatusCode && File.Exists(path) && new FileInfo(path).Length == file.Length;
        return Receipt(request, true, verified, (int)response.StatusCode,
            verified ? "HTTPS upload completed and local source remained exact." : "HTTPS upload was not verified.",
            resolvedTarget: path);
    }

    private static D045HarnessReceipt CloseProcess(D045HarnessRequest request, bool force)
    {
        var pid = DemandPositiveInt32(request.Target, "PID");
        if (pid == Environment.ProcessId)
            return Receipt(
                request,
                false,
                false,
                -1,
                "Harness cannot terminate its own host process.",
                outcomeReason: D045HarnessOutcomeReason.InvalidRequest,
                retryAllowed: false);
        using var process = Process.GetProcessById(pid);
        DemandEffectDispatch(request);
        var attempted = force ? Kill(process) : process.CloseMainWindow();
        if (!attempted)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "The target did not accept the requested close action.",
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                outcomeReason: D045HarnessOutcomeReason.ToolFailure,
                verificationStrength:
                    D045HarnessVerificationStrength.PreconditionsValidated,
                retryAllowed: false);
        }
        var exited = process.WaitForExit(request.TimeoutMilliseconds);
        return Receipt(
            request,
            true,
            exited,
            exited ? 0 : -1,
            exited
                ? "Exact process exit verified."
                : "Process remained active after the timeout.",
            resolvedTargetKind: D045ExpectedTargetKind.Application,
            outcomeReason: exited
                ? D045HarnessOutcomeReason.EffectVerified
                : D045HarnessOutcomeReason.Timeout,
            verificationStrength: exited
                ? D045HarnessVerificationStrength.DeterministicPostcondition
                : D045HarnessVerificationStrength.EffectPossiblyApplied,
            postconditionCode: exited
                ? D045HarnessPostconditionCode.ProcessExited
                : D045HarnessPostconditionCode.None,
            retryAllowed: false);
    }

    private static bool Kill(Process process)
    {
        process.Kill(entireProcessTree: true);
        return true;
    }

    private static D045HarnessReceipt DeletePath(D045HarnessRequest request, bool recycle)
    {
        var path = DemandSafeDestructivePath(request.Target);
        var existed = File.Exists(path) || Directory.Exists(path);
        if (!existed)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Exact delete target does not exist.",
                resolvedTarget: path);
        }

        DemandNoReparseDestructivePath(path);
        DemandEffectDispatch(request);
        // Dispatch admission can run concurrently with filesystem changes. Revalidate
        // the entire target chain at the last boundary before the destructive call.
        DemandNoReparseDestructivePath(path);

        if (recycle)
        {
            var isDirectory = Directory.Exists(path);
            var script =
                "Add-Type -AssemblyName Microsoft.VisualBasic; " +
                (isDirectory
                    ? "[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory('" + EscapePowerShell(path) + "','OnlyErrorDialogs','SendToRecycleBin')"
                    : "[Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile('" + EscapePowerShell(path) + "','OnlyErrorDialogs','SendToRecycleBin')");
            var result = RunPowerShell(script, request.TimeoutMilliseconds);
            var absent = result.ExitCode == 0 && !File.Exists(path) && !Directory.Exists(path);
            return Receipt(request, true, absent, result.ExitCode,
                absent ? "Source path moved to Recycle Bin and absence verified." : "Recycle operation was not verified.",
                result.StandardError,
                resolvedTarget: path);
        }

        if (File.Exists(path))
            File.Delete(path);
        else
            Directory.Delete(path, recursive: true);
        var verified = !File.Exists(path) && !Directory.Exists(path);
        return Receipt(request, true, verified, verified ? 0 : -1,
            verified ? "Permanent deletion and source absence verified." : "Permanent deletion was not verified.",
            resolvedTarget: path);
    }

    private static D045HarnessReceipt SchedulePower(D045HarnessRequest request, bool restart)
    {
        DemandEffectDispatch(request);
        var result = RunProcess(
            Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
            restart ? "/r /t 30" : "/s /t 30",
            request.TimeoutMilliseconds,
            useShellExecute: false);
        return Receipt(request, true, result.ExitCode == 0, result.ExitCode,
            result.ExitCode == 0
                ? (restart ? "Restart scheduled for 30 seconds." : "Shutdown scheduled for 30 seconds.")
                : "Windows rejected the power request.",
            result.StandardError,
            outcomeReason: result.ExitCode == 0
                ? D045HarnessOutcomeReason.EffectVerified
                : D045HarnessOutcomeReason.ToolFailure,
            verificationStrength: result.ExitCode == 0
                ? D045HarnessVerificationStrength.DeterministicPostcondition
                : D045HarnessVerificationStrength.PreconditionsValidated,
            postconditionCode: result.ExitCode == 0
                ? D045HarnessPostconditionCode.DispatchAccepted
                : D045HarnessPostconditionCode.None,
            retryAllowed: false);
    }

    private static D045HarnessReceipt RunShell(D045HarnessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Payload))
            return Receipt(request, false, false, -1, "Shell command is required.");
        DemandEffectDispatch(request);
        var result = RunPowerShell(request.Payload, request.TimeoutMilliseconds);
        return Receipt(request, true, result.ExitCode == 0, result.ExitCode,
            result.ExitCode == 0 ? "Shell command exit code verified as zero." : "Shell command returned a non-zero exit code.",
            result.StandardOutput.Length > 0 ? result.StandardOutput : result.StandardError,
            outcomeReason: result.ExitCode == 0
                ? D045HarnessOutcomeReason.EffectVerified
                : D045HarnessOutcomeReason.ToolFailure,
            verificationStrength: D045HarnessVerificationStrength.EffectPossiblyApplied,
            postconditionCode: result.ExitCode == 0
                ? D045HarnessPostconditionCode.ProcessExitCodeZero
                : D045HarnessPostconditionCode.None,
            retryAllowed: false);
    }

    private D045HarnessReceipt SendExternalInput(D045HarnessRequest request)
    {
        var hasExactWindow = D045ExactWindowTarget.TryParse(
            request.Target,
            out var pid,
            out var window);
        if (!hasExactWindow)
            pid = DemandPositiveInt32(request.Target, "PID");
        using var process = Process.GetProcessById(pid);
        if (process.HasExited
            || string.IsNullOrEmpty(request.Payload)
            || (hasExactWindow
                && !_exactVisibleWindowValidator(window, pid)))
        {
            return Receipt(request, false, false, -1, "A live target PID and input payload are required.");
        }
        DemandEffectDispatch(request);
        if (hasExactWindow)
        {
            if (!_exactVisibleWindowValidator(window, pid))
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "The exact visible application window changed before input dispatch.");
            }
            var accepted = _exactWindowTextDispatcher(
                window,
                request.Payload);
            process.Refresh();
            var exactWindowVerified = accepted
                && !process.HasExited
                && _exactVisibleWindowValidator(window, pid)
                && _foregroundWindowValidator(window);
            return Receipt(
                request,
                true,
                exactWindowVerified,
                exactWindowVerified ? 0 : -1,
                exactWindowVerified
                    ? "Input was sent to the exact host-bound application window."
                    : "External input dispatch to the exact window was not verified.");
        }
        var encodedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Payload));
        var script =
            "$w=New-Object -ComObject WScript.Shell; " +
            "if(-not $w.AppActivate(" + pid.ToString(CultureInfo.InvariantCulture) + ")){exit 3}; " +
            "Start-Sleep -Milliseconds 150; " +
            "$t=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedPayload + "')); " +
            "$w.SendKeys($t);";
        var result = RunPowerShell(script, request.TimeoutMilliseconds);
        process.Refresh();
        var verified = result.ExitCode == 0 && !process.HasExited;
        return Receipt(request, true, verified, result.ExitCode,
            verified ? "Input was sent to the exact live PID." : "External input dispatch was not verified.",
            result.StandardError);
    }

    private static bool HasExactVisibleWindowIdentity(
        IntPtr window,
        int processId)
    {
        if (window == IntPtr.Zero
            || !IsWindow(window)
            || !IsWindowVisible(window))
        {
            return false;
        }
        _ = GetWindowThreadProcessId(window, out var observedProcessId);
        return observedProcessId == (uint)processId;
    }

    private static bool DispatchUnicodeTextToExactWindow(
        IntPtr window,
        string text)
    {
        if (!TryActivateExactWindowForInput(window))
            return false;
        var inputs = new D045NativeInput[text.Length * 2];
        for (var index = 0; index < text.Length; index++)
        {
            inputs[index * 2] = D045NativeInput.Unicode(text[index], keyUp: false);
            inputs[index * 2 + 1] = D045NativeInput.Unicode(text[index], keyUp: true);
        }
        return inputs.Length == 0
            || SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf<D045NativeInput>()) == (uint)inputs.Length;
    }

    private static bool TryActivateExactWindowForInput(IntPtr window)
    {
        if (GetForegroundWindow() == window)
            return true;

        _ = ShowWindow(window, ShowWindowRestore);
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(window, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0u
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var attachedToForeground = false;
        var attachedToTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedToForeground = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: true);
            }
            if (targetThread != 0
                && targetThread != currentThread
                && targetThread != foregroundThread)
            {
                attachedToTarget = AttachThreadInput(
                    currentThread,
                    targetThread,
                    attach: true);
            }
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = AttachThreadInput(
                    currentThread,
                    targetThread,
                    attach: false);
            }
            if (attachedToForeground)
            {
                _ = AttachThreadInput(
                    currentThread,
                    foregroundThread,
                    attach: false);
            }
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (GetForegroundWindow() == window)
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    private static D045HarnessReceipt StartElevated(D045HarnessRequest request)
    {
        var path = DemandExistingFile(request.Target);
        if (IsBlockedApplicationHost(path))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Command, script and control hosts cannot be launched through elevation; elevated shell execution requires a separately authorized composite capability.",
                resolvedTarget: path,
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                outcomeReason: D045HarnessOutcomeReason.InvalidRequest,
                retryAllowed: false);
        }
        DemandEffectDispatch(request);
        using var process = Process.Start(new ProcessStartInfo(path)
        {
            Arguments = request.Payload,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        });
        if (process is null)
        {
            return Receipt(
                request,
                true,
                false,
                -1,
                "Windows did not return an elevated process.",
                resolvedTarget: path);
        }
        Thread.Sleep(200);
        process.Refresh();
        return Receipt(request, true, !process.HasExited, 0,
            !process.HasExited ? "Windows UAC accepted and elevated process is live." : "Elevated process exited before verification.",
            resolvedTarget: path);
    }

    private static D045HarnessReceipt CreateLocalData(
        D045HarnessRequest request)
    {
        var path = DemandNewDataTarget(request.Target);
        var kind = request.SecondaryTarget.Trim().ToLowerInvariant();
        if (kind is "directory" or "folder")
        {
            if (request.Payload.Length != 0)
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Directory creation does not accept a content payload.");
            }
            DemandEffectDispatch(request);
            Directory.CreateDirectory(path);
            var verified = Directory.Exists(path) && !File.Exists(path);
            return Receipt(
                request,
                true,
                verified,
                verified ? 0 : -1,
                verified
                    ? "Exact new directory creation verified."
                    : "Directory creation was not verified.",
                verified ? path : string.Empty,
                resolvedTarget: path);
        }
        if (kind is not ("file" or "text" or "text_file"))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Data creation kind must be file or directory.");
        }

        var bytes = new UTF8Encoding(false, true).GetBytes(request.Payload);
        if (bytes.Length > MaximumCreatedDataBytes)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "New file content exceeds the 1 MiB Harness limit.");
        }
        DemandEffectDispatch(request);
        using (var stream = new FileStream(
                   path,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None,
                   bufferSize: 4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        var verifiedBytes = File.ReadAllBytes(path);
        var verifiedFile = verifiedBytes.Length == bytes.Length
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(verifiedBytes),
                SHA256.HashData(bytes));
        return Receipt(
            request,
            true,
            verifiedFile,
            verifiedFile ? 0 : -1,
            verifiedFile
                ? "Exact new UTF-8 file creation verified."
                : "File creation was not verified.",
            verifiedFile ? path : string.Empty,
            resolvedTarget: path);
    }

    private D045HarnessReceipt LaunchApplication(
        D045HarnessRequest request)
    {
        if (request.SecondaryTarget.Length != 0 || request.Payload.Length != 0)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Application launch accepts only one app target and no secondary target or payload.");
        }
        if (request.ExpectedLeafName.Length != 0)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Application launch does not accept an expected local-content leaf name.");
        }
        if (request.ExpectedTargetKind is not (
                D045ExpectedTargetKind.None or D045ExpectedTargetKind.Application))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Application launch requires an Application expected target kind.");
        }
        var resolution = ResolveApplicationTarget(request.Target);
        if (!resolution.Resolved)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                resolution.Message,
                resolution.CandidateSummary,
                outcomeReason: resolution.OutcomeReason,
                retryAllowed: resolution.RetryAllowed,
                retryRequiresChangedTarget:
                    resolution.RetryRequiresChangedTarget);
        }
        if (resolution.FixedArguments.Length != 0)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Application shortcuts with fixed arguments are rejected.",
                resolvedTarget: resolution.ExecutablePath,
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                resolvedExtension: ".exe");
        }
        if (IsBlockedApplicationHost(resolution.ExecutablePath))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Command interpreters, scripting hosts and control utilities require Raw Shell permission.",
                resolvedTarget: resolution.ExecutablePath,
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                resolvedExtension: ".exe");
        }
        var windowsBeforeLaunch = request.RequireVisibleApplicationWindowTarget
            ? _visibleApplicationWindowResolver(resolution.ExecutablePath)
            : Array.Empty<D045VisibleApplicationWindow>();
        DemandEffectDispatch(
            request,
            resolution.ExecutablePath,
            D045ExpectedTargetKind.Application);
        var launch = _applicationLauncher(
            resolution.ExecutablePath,
            Path.GetDirectoryName(resolution.ExecutablePath) ?? string.Empty,
            request.TimeoutMilliseconds);
        if (!launch.Verified)
        {
            return Receipt(
                request,
                true,
                false,
                launch.ExitCode,
                launch.Message,
                resolvedTarget: resolution.ExecutablePath,
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                resolvedExtension: ".exe");
        }
        if (request.RequireVisibleApplicationWindowTarget)
        {
            var window = AwaitCorrelatedVisibleApplicationWindow(
                resolution.ExecutablePath,
                windowsBeforeLaunch,
                launch.ProcessId,
                request.TimeoutMilliseconds,
                out var windowFailure);
            if (window is null)
            {
                return Receipt(
                    request,
                    true,
                    false,
                    launch.ExitCode,
                    windowFailure == D045HarnessOutcomeReason.TargetAmbiguous
                        ? "The application was launched, but more than one new or reusable exact-image window matched; no UI target was selected."
                        : "The application was launched, but no exact visible window could be bound for UI control.",
                    resolvedTarget: resolution.ExecutablePath,
                    resolvedTargetKind: D045ExpectedTargetKind.Application,
                    resolvedExtension: ".exe",
                    outcomeReason: windowFailure,
                    verificationStrength:
                        D045HarnessVerificationStrength.EffectPossiblyApplied,
                    retryAllowed: false);
            }
            return Receipt(
                request,
                true,
                true,
                launch.ExitCode,
                "The exact application launch and one correlated visible window were verified.",
                D045ExactWindowTarget.Format(window.ProcessId, window.Handle),
                resolvedTarget: resolution.ExecutablePath,
                resolvedTargetKind: D045ExpectedTargetKind.Application,
                resolvedExtension: ".exe",
                postconditionCode:
                    D045HarnessPostconditionCode.VisibleApplicationWindowObserved);
        }
        return Receipt(
            request,
            true,
            true,
            launch.ExitCode,
            launch.Message,
            launch.ProcessId.ToString(CultureInfo.InvariantCulture),
            resolvedTarget: resolution.ExecutablePath,
            resolvedTargetKind: D045ExpectedTargetKind.Application,
            resolvedExtension: ".exe");
    }

    private static D045ApplicationLaunchAttempt StartAndVerifyPlainApplication(
        string executablePath,
        string workingDirectory,
        int timeoutMilliseconds)
    {
        using var process = Process.Start(new ProcessStartInfo(executablePath)
        {
            Arguments = string.Empty,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        });
        if (process is null)
        {
            return D045ApplicationLaunchAttempt.Failure(
                "Windows did not return a launched process.");
        }
        var verification = VerifyPlainApplicationLaunch(
            process,
            executablePath,
            timeoutMilliseconds);
        return new D045ApplicationLaunchAttempt(
            process.Id,
            verification.Verified,
            verification.ExitCode,
            verification.Message);
    }

    private D045VisibleApplicationWindow? AwaitCorrelatedVisibleApplicationWindow(
        string executablePath,
        IReadOnlyList<D045VisibleApplicationWindow> before,
        int launchedProcessId,
        int timeoutMilliseconds,
        out D045HarnessOutcomeReason failure)
    {
        var beforeHandles = before
            .Select(value => value.Handle)
            .ToHashSet();
        var attempts = Math.Clamp(
            Math.Max(1, timeoutMilliseconds - 250) / 100,
            1,
            30);
        D045VisibleApplicationWindow? stable = null;
        var stableCount = 0;
        D045HarnessOutcomeReason lastFailure =
            D045HarnessOutcomeReason.TargetNotFound;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
                _applicationLaunchDelay(100);
            var selection = SelectCorrelatedVisibleApplicationWindow(
                executablePath,
                beforeHandles,
                launchedProcessId,
                _visibleApplicationWindowResolver(executablePath));
            lastFailure = selection.Failure;
            if (selection.Window is null)
            {
                stable = null;
                stableCount = 0;
                continue;
            }
            if (stable is not null
                && stable.Handle == selection.Window.Handle
                && stable.ProcessId == selection.Window.ProcessId)
            {
                stableCount++;
            }
            else
            {
                stable = selection.Window;
                stableCount = 1;
            }
            if (stableCount >= 2)
            {
                failure = D045HarnessOutcomeReason.Unspecified;
                return stable;
            }
        }
        failure = lastFailure;
        return null;
    }

    private static D045VisibleApplicationWindowSelection
        SelectCorrelatedVisibleApplicationWindow(
            string executablePath,
            IReadOnlySet<IntPtr> beforeHandles,
            int launchedProcessId,
            IReadOnlyList<D045VisibleApplicationWindow> after)
    {
        var canonicalPath = Path.GetFullPath(executablePath);
        var matches = after
            .Where(value => value.ProcessId > 0
                && value.Handle != IntPtr.Zero
                && string.Equals(
                    Path.GetFullPath(value.ExecutablePath),
                    canonicalPath,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => value.Handle)
            .Select(group => group.First())
            .ToArray();
        var newWindows = matches
            .Where(value => !beforeHandles.Contains(value.Handle))
            .ToArray();
        if (newWindows.Length == 1)
            return D045VisibleApplicationWindowSelection.Success(newWindows[0]);
        if (newWindows.Length > 1)
            return D045VisibleApplicationWindowSelection.Ambiguous();

        var launchedProcessWindows = matches
            .Where(value => value.ProcessId == launchedProcessId)
            .ToArray();
        if (launchedProcessWindows.Length == 1)
        {
            return D045VisibleApplicationWindowSelection.Success(
                launchedProcessWindows[0]);
        }
        if (launchedProcessWindows.Length > 1 || matches.Length > 1)
            return D045VisibleApplicationWindowSelection.Ambiguous();
        return matches.Length == 1
            ? D045VisibleApplicationWindowSelection.Success(matches[0])
            : D045VisibleApplicationWindowSelection.NotFound();
    }

    private static bool IsBlockedApplicationHost(string executablePath)
    {
        if (IsBlockedApplicationHostLeaf(Path.GetFileName(executablePath)))
            return true;

        try
        {
            var identity = FileVersionInfo.GetVersionInfo(executablePath);
            return new[]
                {
                    identity.OriginalFilename,
                    identity.InternalName,
                    identity.ProductName,
                    identity.FileDescription
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeApplicationHostIdentity)
                .Any(BlockedApplicationHostIdentities.Contains);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FileNotFoundException
                or System.ComponentModel.Win32Exception)
        {
            // Missing version metadata is normal for many GUI applications.
            // Their exact leaf identity remains the fail-closed host boundary.
            return false;
        }
    }

    private static bool IsBlockedApplicationHostLeaf(string? value)
    {
        var normalized = NormalizeApplicationHostIdentity(value);
        return BlockedApplicationHostIdentities.Contains(normalized)
            || Regex.IsMatch(
                normalized,
                @"^(?:pythonw?|pythonlauncher|pypy|ipython|node|nodejs)(?:v?\d+)*$",
                RegexOptions.CultureInvariant);
    }

    private static string NormalizeApplicationHostIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var leaf = Path.GetFileName(value.Trim().Trim('"'));
        while (leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
               || leaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
               || leaf.EndsWith(".mui", StringComparison.OrdinalIgnoreCase))
        {
            leaf = Path.GetFileNameWithoutExtension(leaf);
        }
        return Regex.Replace(
            leaf.ToLowerInvariant(),
            @"[^\p{L}\p{N}]+",
            string.Empty,
            RegexOptions.CultureInvariant);
    }

    private static D045ApplicationLaunchVerification VerifyPlainApplicationLaunch(
        Process process,
        string expectedExecutablePath,
        int timeoutMilliseconds)
    {
        if (process.Id <= 0)
        {
            return D045ApplicationLaunchVerification.Failure(
                "Windows did not provide a valid process identity.");
        }

        var shortCheckMilliseconds = Math.Min(250, timeoutMilliseconds);
        if (process.WaitForExit(shortCheckMilliseconds))
        {
            return process.ExitCode == 0
                ? D045ApplicationLaunchVerification.Success(
                    "The locally resolved application completed a verified short-lived launch.")
                : D045ApplicationLaunchVerification.Failure(
                    "The locally resolved application exited with a failure code.",
                    process.ExitCode);
        }

        try
        {
            process.Refresh();
            var launchedImage = process.MainModule?.FileName;
            var exactImage = !string.IsNullOrWhiteSpace(launchedImage)
                && string.Equals(
                    Path.GetFullPath(launchedImage),
                    Path.GetFullPath(expectedExecutablePath),
                    StringComparison.OrdinalIgnoreCase);
            return exactImage
                ? D045ApplicationLaunchVerification.Success(
                    "The exact locally resolved application image is running.")
                : D045ApplicationLaunchVerification.Failure(
                    "The running process image did not match the resolved application.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return D045ApplicationLaunchVerification.Failure(
                "The launched process image could not be verified.");
        }
    }

    private D045HarnessReceipt OpenLocalContent(D045HarnessRequest request)
    {
        if (request.SecondaryTarget.Length != 0 || request.Payload.Length != 0)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Local content open accepts only one exact target and no arguments or payload.");
        }
        if (request.ExpectedTargetKind == D045ExpectedTargetKind.Application)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Local Content Open accepts only File or Directory expected target kinds.");
        }

        var resolution = ResolveKnownOrExactPath(
            request.Target,
            allowFile: request.ExpectedTargetKind != D045ExpectedTargetKind.Directory,
            allowDirectory: request.ExpectedTargetKind != D045ExpectedTargetKind.File);
        if (!resolution.Resolved)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                resolution.Message,
                resolution.CandidateSummary,
                outcomeReason: resolution.OutcomeReason,
                retryAllowed: resolution.RetryAllowed,
                retryRequiresChangedTarget:
                    resolution.RetryRequiresChangedTarget);
        }

        var path = resolution.CanonicalPath;
        if (!TryValidateNoReparsePath(path, out var reparseMessage))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                reparseMessage,
                resolvedTarget: path);
        }
        var isDirectory = Directory.Exists(path);
        var resolvedKind = isDirectory
            ? D045ExpectedTargetKind.Directory
            : D045ExpectedTargetKind.File;
        if (request.ExpectedTargetKind != D045ExpectedTargetKind.None
            && request.ExpectedTargetKind != resolvedKind)
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "The resolved local content item kind does not match the trusted expected target kind.",
                resolvedTarget: path,
                resolvedTargetKind: resolvedKind,
                resolvedExtension: isDirectory ? string.Empty : Path.GetExtension(path),
                outcomeReason: D045HarnessOutcomeReason.TargetWrongKind,
                retryAllowed: false);
        }
        if (!ResolvedLeafMatchesExpected(
                path,
                isDirectory,
                request.ExpectedLeafName))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "The resolved local content leaf does not match the trusted expected leaf name.",
                resolvedTarget: path,
                resolvedTargetKind: resolvedKind,
                resolvedExtension: isDirectory ? string.Empty : Path.GetExtension(path),
                outcomeReason: D045HarnessOutcomeReason.TargetLeafMismatch,
                retryAllowed: false);
        }
        if (IsFileSystemRoot(path))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Drive and share roots cannot be opened by Local Content Open.",
                resolvedTarget: path,
                resolvedTargetKind: resolvedKind,
                resolvedExtension: isDirectory ? string.Empty : Path.GetExtension(path));
        }
        if (IsNetworkOrSharePath(path))
        {
            return Receipt(
                request,
                false,
                false,
                -1,
                "Network and share paths are not local content targets.",
                resolvedTarget: path,
                resolvedTargetKind: resolvedKind,
                resolvedExtension: isDirectory ? string.Empty : Path.GetExtension(path));
        }

        var extension = string.Empty;
        if (isDirectory)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "Reparse-point directories are not accepted as exact local content.",
                    resolvedTarget: path,
                    resolvedTargetKind: resolvedKind);
            }
        }
        else
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory
                    | FileAttributes.Device
                    | FileAttributes.ReparsePoint)) != 0)
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "The target is not a regular local data file.",
                    resolvedTarget: path,
                    resolvedTargetKind: resolvedKind,
                    resolvedExtension: Path.GetExtension(path));
            }
            extension = Path.GetExtension(path);
            if (!SafeLocalContentExtensions.Contains(extension))
            {
                return Receipt(
                    request,
                    false,
                    false,
                    -1,
                    "The file type is not an allowlisted document, image, audio, video or data format.",
                    resolvedTarget: path,
                    resolvedTargetKind: resolvedKind,
                    resolvedExtension: extension);
            }
        }

        DemandEffectDispatch(request, path, resolvedKind);
        var processId = _localContentOpenDispatcher(path);
        var remainedExact = isDirectory ? Directory.Exists(path) : File.Exists(path);
        var verified = processId >= 0 && remainedExact;
        var output = new StringBuilder(path)
            .Append('\n')
            .Append("kind=")
            .Append(isDirectory ? "directory" : "file")
            .Append('\n')
            .Append("extension=")
            .Append(extension.Length == 0 ? "none" : extension.ToLowerInvariant());
        if (processId > 0)
        {
            output.Append('\n')
                .Append("process_id=")
                .Append(processId.ToString(CultureInfo.InvariantCulture));
        }
        return Receipt(
            request,
            true,
            verified,
            verified ? 0 : -1,
            verified
                ? "Windows accepted the exact local content association and the target remained present."
                : "Windows did not verify the local content association.",
            output.ToString(),
            resolvedTarget: path,
            resolvedTargetKind: resolvedKind,
            resolvedExtension: extension);
    }

    private static void DemandEffectDispatch(
        D045HarnessRequest request,
        string resolvedTarget = "",
        D045ExpectedTargetKind resolvedTargetKind = D045ExpectedTargetKind.None)
    {
        if (request.ExpectedHostIdentityFingerprint.Length != 0)
        {
            if (resolvedTarget.Length == 0
                || resolvedTargetKind != request.ExpectedTargetKind)
            {
                throw new D045HarnessRejectionException(
                    D045HarnessOutcomeReason.TargetChanged,
                    "The redeemed local target identity no longer matched before effect dispatch.");
            }

            request.InvokeBeforeHostIdentityRecapture();
            string currentIdentity;
            try
            {
                currentIdentity = D045HarnessHostTargetIdentity.Capture(
                    resolvedTarget,
                    resolvedTargetKind);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or FileNotFoundException
                    or DirectoryNotFoundException
                    or IOException
                    or NotSupportedException
                    or PathTooLongException
                    or UnauthorizedAccessException)
            {
                throw new D045HarnessRejectionException(
                    D045HarnessOutcomeReason.TargetChanged,
                    "The redeemed local target changed before effect dispatch.");
            }
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(currentIdentity),
                    Convert.FromHexString(
                        request.ExpectedHostIdentityFingerprint)))
            {
                throw new D045HarnessRejectionException(
                    D045HarnessOutcomeReason.TargetChanged,
                    "The redeemed local target changed before effect dispatch.");
            }
        }
        if (request.TryAdmitEffectDispatch())
            return;
        throw new D045HarnessRejectionException(
            D045HarnessOutcomeReason.InvalidRequest,
            "The player operation was no longer live at the exact effect dispatch boundary.");
    }

    private static bool ResolvedLeafMatchesExpected(
        string resolvedPath,
        bool isDirectory,
        string expectedLeafName)
    {
        if (expectedLeafName.Length == 0)
            return true;
        var actualLeaf = D045HarnessRequest.NormalizeLeafName(
            Path.GetFileName(Path.GetFullPath(resolvedPath)));
        if (string.Equals(
                actualLeaf,
                expectedLeafName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return !isDirectory
            && string.Equals(
                Path.GetFileNameWithoutExtension(actualLeaf),
                expectedLeafName,
                StringComparison.OrdinalIgnoreCase);
    }

    private D045LocalPathResolution ResolveKnownOrExactPath(
        string requestedTarget,
        bool allowFile,
        bool allowDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget)
            || requestedTarget.IndexOf('\0') >= 0)
        {
            return D045LocalPathResolution.NotFound(
                "An exact existing local path or known-folder target is required.");
        }

        var requested = requestedTarget.Trim();
        if (requested.StartsWith("known:", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveKnownFolderTarget(requested, allowFile, allowDirectory);
        }
        if (!Path.IsPathFullyQualified(requested)
            || LooksLikeUri(requested))
        {
            return D045LocalPathResolution.NotFound(
                "Only an exact absolute local path or known:desktop/documents/downloads/pictures target is accepted.");
        }

        try
        {
            var canonical = Path.GetFullPath(requested);
            if (IsNetworkOrSharePath(canonical))
            {
                return D045LocalPathResolution.NotFound(
                    "Network and share paths are not local content targets.");
            }
            return ClassifyExistingPath(canonical, allowFile, allowDirectory);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return D045LocalPathResolution.NotFound(
                "The local content target is not a valid filesystem path.");
        }
    }

    private D045LocalPathResolution ResolveKnownFolderTarget(
        string requested,
        bool allowFile,
        bool allowDirectory)
    {
        var body = requested["known:".Length..];
        var separatorIndex = body.IndexOfAny(new[] { '/', '\\' });
        var key = separatorIndex < 0 ? body : body[..separatorIndex];
        var relative = separatorIndex < 0
            ? string.Empty
            : body[(separatorIndex + 1)..];
        if (!KnownFolderIds.ContainsKey(key)
            || relative.IndexOf('\0') >= 0)
        {
            return D045LocalPathResolution.NotFound(
                "Known folder must be desktop, documents, downloads or pictures.");
        }

        var resolvedRoot = _knownFolderPathResolver(key);
        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            return D045LocalPathResolution.NotFound(
                "The requested Windows known folder is unavailable.");
        }

        string root;
        try
        {
            root = Path.GetFullPath(resolvedRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return D045LocalPathResolution.NotFound(
                "Windows returned an invalid known-folder path.");
        }
        if (IsNetworkOrSharePath(root))
        {
            return D045LocalPathResolution.NotFound(
                "Network-redirected known folders are not local content targets.");
        }
        if (!Directory.Exists(root))
        {
            return D045LocalPathResolution.NotFound(
                "The requested Windows known folder does not exist.");
        }
        if (!TryValidateNoReparsePath(root, out var rootReparseMessage))
            return D045LocalPathResolution.NotFound(rootReparseMessage);
        if (relative.Length == 0)
            return ClassifyExistingPath(root, allowFile, allowDirectory);
        if (relative.StartsWith("**/", StringComparison.Ordinal)
            || relative.StartsWith("**\\", StringComparison.Ordinal))
        {
            var leaf = relative[3..];
            if (!IsValidRecursiveKnownFolderLeaf(leaf))
            {
                return D045LocalPathResolution.NotFound(
                    "Recursive known-folder selection requires exactly known:<folder>/**/<leaf> with one literal leaf name.");
            }
            return ResolveKnownFolderRecursive(
                root,
                leaf,
                allowFile,
                allowDirectory);
        }
        if (Path.IsPathFullyQualified(relative)
            || LooksLikeUri(relative)
            || relative.IndexOf('*') >= 0
            || ContainsTraversalSegment(relative))
        {
            return D045LocalPathResolution.NotFound(
                "A known-folder descendant must be a bounded relative path.");
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsExactDescendant(root, candidate))
            {
                return D045LocalPathResolution.NotFound(
                    "Known-folder traversal outside the selected folder is rejected.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return D045LocalPathResolution.NotFound(
                "The known-folder descendant is not a valid relative path.");
        }

        var exact = ClassifyExistingPath(candidate, allowFile, allowDirectory);
        if (exact.Resolved)
            return exact;

        var parent = Path.GetDirectoryName(candidate);
        var requestedName = Path.GetFileName(candidate);
        if (string.IsNullOrWhiteSpace(parent)
            || string.IsNullOrWhiteSpace(requestedName)
            || !Directory.Exists(parent)
            || !IsExactDescendantOrSame(root, parent))
        {
            return exact;
        }
        if (!TryValidateNoReparsePath(parent, out var parentReparseMessage))
            return D045LocalPathResolution.NotFound(parentReparseMessage);

        string[] entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(parent)
                .Take(MaximumKnownFolderResolutionEntries + 1)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return D045LocalPathResolution.NotFound(
                "The known-folder parent could not be inspected.");
        }
        if (entries.Length > MaximumKnownFolderResolutionEntries)
        {
            return D045LocalPathResolution.NotFound(
                "The known-folder parent exceeds the bounded 256-entry resolution limit.");
        }

        var matches = entries
            .Where(entry =>
                string.Equals(
                    Path.GetFileName(entry),
                    requestedName,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    Path.GetFileNameWithoutExtension(entry),
                    requestedName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Where(path => ClassifyExistingPath(path, allowFile, allowDirectory).Resolved)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches.Length switch
        {
            1 => ClassifyExistingPath(matches[0], allowFile, allowDirectory),
            > 1 => D045LocalPathResolution.Ambiguous(matches),
            _ => exact
        };
    }

    private static bool IsValidRecursiveKnownFolderLeaf(string leaf)
        => !string.IsNullOrWhiteSpace(leaf)
            && leaf.Length <= 255
            && leaf is not ("." or "..")
            && leaf.IndexOfAny(new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar,
                '*',
                '?',
                '\0'
            }) < 0
            && leaf.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsExplicitRecursiveKnownFolderSelector(
        string requestedTarget)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget))
            return false;
        var requested = requestedTarget.Trim();
        if (!requested.StartsWith("known:", StringComparison.OrdinalIgnoreCase))
            return false;
        var body = requested["known:".Length..];
        var separatorIndex = body.IndexOfAny(new[] { '/', '\\' });
        if (separatorIndex < 0
            || !KnownFolderIds.ContainsKey(body[..separatorIndex]))
        {
            return false;
        }
        var relative = body[(separatorIndex + 1)..];
        if (!relative.StartsWith("**/", StringComparison.Ordinal)
            && !relative.StartsWith("**\\", StringComparison.Ordinal))
        {
            return false;
        }
        return IsValidRecursiveKnownFolderLeaf(relative[3..]);
    }

    private static D045LocalPathResolution ResolveKnownFolderRecursive(
        string root,
        string requestedLeaf,
        bool allowFile,
        bool allowDirectory)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var inspectedEntries = 0;
        var matches = new List<string>();
        var requestedHasExtension = Path.GetExtension(requestedLeaf).Length != 0;

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current.Path)
                    .Take(MaximumKnownFolderResolutionEntries + 1)
                    .Select(Path.GetFullPath)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(value => value, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or PathTooLongException)
            {
                return D045LocalPathResolution.NotFound(
                    "A recursive known-folder directory could not be inspected completely.");
            }

            if (entries.Length > MaximumKnownFolderResolutionEntries)
            {
                return D045LocalPathResolution.NotFound(
                    "A recursive known-folder directory exceeds the bounded 256-entry limit.");
            }
            if (inspectedEntries + entries.Length
                > MaximumKnownFolderRecursiveEntries)
            {
                return D045LocalPathResolution.NotFound(
                    "Recursive known-folder resolution exceeds the bounded 1024-entry limit.");
            }
            inspectedEntries += entries.Length;

            foreach (var entry in entries)
            {
                if (!IsExactDescendant(root, entry)
                    || IsNetworkOrSharePath(entry))
                {
                    return D045LocalPathResolution.NotFound(
                        "Recursive known-folder resolution encountered a non-local descendant.");
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (
                    exception is FileNotFoundException
                        or DirectoryNotFoundException
                        or IOException
                        or UnauthorizedAccessException
                        or PathTooLongException)
                {
                    return D045LocalPathResolution.NotFound(
                        "A recursive known-folder item could not be inspected completely.");
                }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return D045LocalPathResolution.NotFound(
                        "Reparse-point items stop recursive known-folder resolution.");
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if ((isDirectory && !Directory.Exists(entry))
                    || (!isDirectory && !File.Exists(entry)))
                {
                    return D045LocalPathResolution.NotFound(
                        "A recursive known-folder item changed during inspection.");
                }

                var name = Path.GetFileName(entry);
                var nameMatches = string.Equals(
                    name,
                    requestedLeaf,
                    StringComparison.OrdinalIgnoreCase);
                if (!requestedHasExtension && !isDirectory)
                {
                    nameMatches |= string.Equals(
                        Path.GetFileNameWithoutExtension(name),
                        requestedLeaf,
                        StringComparison.OrdinalIgnoreCase);
                }
                if (nameMatches
                    && ((isDirectory && allowDirectory)
                        || (!isDirectory && allowFile)))
                {
                    matches.Add(entry);
                }

                var entryDepth = current.Depth + 1;
                if (isDirectory
                    && entryDepth < MaximumKnownFolderRecursiveDepth)
                {
                    queue.Enqueue((entry, entryDepth));
                }
            }
        }

        var unique = matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return unique.Length switch
        {
            1 => ClassifyExistingPath(unique[0], allowFile, allowDirectory),
            > 1 => D045LocalPathResolution.Ambiguous(unique),
            _ => D045LocalPathResolution.NotFound(
                "No unique item matched inside the bounded recursive known-folder search.")
        };
    }

    private static D045LocalPathResolution ClassifyExistingPath(
        string path,
        bool allowFile,
        bool allowDirectory)
    {
        var canonical = Path.GetFullPath(path);
        if (!TryValidateNoReparsePath(canonical, out var reparseMessage))
            return D045LocalPathResolution.NotFound(reparseMessage);
        if (allowFile && File.Exists(canonical))
            return D045LocalPathResolution.Success(canonical);
        if (allowDirectory && Directory.Exists(canonical))
            return D045LocalPathResolution.Success(canonical);
        if ((!allowFile && File.Exists(canonical))
            || (!allowDirectory && Directory.Exists(canonical)))
        {
            return D045LocalPathResolution.NotFound(
                "The exact local content target has the wrong item kind.",
                D045HarnessOutcomeReason.TargetWrongKind);
        }
        return D045LocalPathResolution.NotFound(
            "The exact local content target was not found.");
    }

    private static bool TryValidateNoReparsePath(
        string path,
        out string message)
    {
        message = string.Empty;
        try
        {
            var canonical = Path.GetFullPath(path);
            var root = Path.GetPathRoot(canonical);
            if (string.IsNullOrWhiteSpace(root))
            {
                message = "A rooted local filesystem path is required.";
                return false;
            }

            var segments = new List<string> { root };
            var relative = Path.GetRelativePath(root, canonical);
            var current = root;
            foreach (var segment in relative.Split(
                         new[]
                         {
                             Path.DirectorySeparatorChar,
                             Path.AltDirectorySeparatorChar
                         },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                    continue;
                current = Path.Combine(current, segment);
                segments.Add(current);
            }

            foreach (var segmentPath in segments)
            {
                var attributes = File.GetAttributes(segmentPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    message = "Reparse-point path segments are rejected before local content resolution.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FileNotFoundException
                or DirectoryNotFoundException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            message = "Every local content path segment must be an accessible non-reparse filesystem item.";
            return false;
        }
    }

    private static bool IsExactDescendant(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Length > 0
            && !string.Equals(relative, ".", StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative)
            && !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            && !relative.StartsWith(
                ".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    internal static bool IsExactDescendantOrSame(string root, string path)
    {
        var canonicalRoot = CanonicalPath(root);
        var canonicalPath = CanonicalPath(path);
        return string.Equals(
                canonicalRoot,
                canonicalPath,
                StringComparison.OrdinalIgnoreCase)
            || IsExactDescendant(canonicalRoot, canonicalPath);
    }

    private static bool LooksLikeUri(string value)
    {
        if (Path.IsPathFullyQualified(value))
            return false;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    private static bool ContainsTraversalSegment(string relative)
        => relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private static bool IsFileSystemRoot(string path)
    {
        var canonical = Path.GetFullPath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(canonical)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(root)
            && string.Equals(canonical, root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNetworkOrSharePath(string path)
    {
        if (path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrWhiteSpace(root)
                && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string? ResolveWindowsKnownFolderPath(string key)
    {
        if (!OperatingSystem.IsWindows()
            || !KnownFolderIds.TryGetValue(key, out var folderId))
        {
            return null;
        }

        var result = SHGetKnownFolderPath(
            ref folderId,
            0,
            IntPtr.Zero,
            out var pathPointer);
        if (result != 0 || pathPointer == IntPtr.Zero)
            return null;
        try
        {
            var value = Marshal.PtrToStringUni(pathPointer);
            return string.IsNullOrWhiteSpace(value)
                ? null
                : Path.GetFullPath(value);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    private static int OpenLocalContentWithWindowsAssociation(string path)
    {
        if (!OperatingSystem.IsWindows())
            return -1;
        var info = new D045ShellExecuteInfo
        {
            Size = Marshal.SizeOf<D045ShellExecuteInfo>(),
            Mask = ShellExecuteNoCloseProcess | ShellExecuteNoUi,
            Verb = "open",
            File = path,
            Directory = Path.GetDirectoryName(path),
            Show = 1
        };
        if (!ShellExecuteEx(ref info))
            return -1;
        try
        {
            if (info.Process == IntPtr.Zero)
                return 0;
            var processId = GetProcessId(info.Process);
            return processId > int.MaxValue ? 0 : (int)processId;
        }
        finally
        {
            if (info.Process != IntPtr.Zero)
                CloseHandle(info.Process);
        }
    }

    private D045ApplicationResolution ResolveApplicationTarget(
        string requestedTarget)
    {
        if (string.IsNullOrWhiteSpace(requestedTarget))
        {
            return D045ApplicationResolution.NotFound(
                "An executable path or installed application name is required.");
        }

        var requested = requestedTarget.Trim();
        try
        {
            if (Path.IsPathFullyQualified(requested))
            {
                var exact = Path.GetFullPath(requested);
                if (!File.Exists(exact)
                    || !string.Equals(
                        Path.GetExtension(exact),
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return D045ApplicationResolution.NotFound(
                        "The exact executable target was not found.");
                }
                return D045ApplicationResolution.Success(
                    Path.GetFileNameWithoutExtension(exact),
                    exact,
                    string.Empty,
                    Path.GetDirectoryName(exact) ?? string.Empty);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return D045ApplicationResolution.NotFound(
                "The application target is not a valid path or name.");
        }

        if (requested.Length > 120
            || Regex.IsMatch(requested, "[\\\\/:*?\"<>|`$]"))
        {
            return D045ApplicationResolution.NotFound(
                "The application name contains unsupported path or command characters.");
        }

        var candidates = _applicationCandidateProvider is null
            ? DiscoverApplicationCandidates(requested)
            : (_applicationCandidateProvider(requested)
                ?? Array.Empty<D045ApplicationCandidate>()).ToList();

        var ordered = candidates
            .Where(IsSafeApplicationResolutionCandidate)
            .GroupBy(ApplicationLogicalIdentity, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
                .ThenBy(
                    candidate => candidate.ExecutablePath,
                    StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.DisplayName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0 || ordered[0].Score < 350)
        {
            return D045ApplicationResolution.NotFound(
                "No installed application matched that name. Try its Start-menu name or exact executable path.");
        }

        var best = ordered[0];
        if (ordered.Length > 1
            && !AllCandidatesAreTrustedPlatformAliases(ordered))
        {
            return D045ApplicationResolution.Ambiguous(
                ordered.Take(6).Select(candidate => candidate.DisplayName));
        }

        return D045ApplicationResolution.Success(
            best.DisplayName,
            best.ExecutablePath,
            best.FixedArguments,
            best.WorkingDirectory);
    }

    private static List<D045ApplicationCandidate> DiscoverApplicationCandidates(
        string requested)
    {
        var candidates = new List<D045ApplicationCandidate>();
        AddRunningProcessCandidates(candidates, requested);
        foreach (var executableName in ExecutableNamesFor(requested))
            AddExecutableCandidates(candidates, requested, executableName);
        AddStartMenuShortcutCandidates(candidates, requested);
        return candidates;
    }

    private static bool IsSafeApplicationResolutionCandidate(
        D045ApplicationCandidate candidate)
    {
        try
        {
            var canonical = Path.GetFullPath(candidate.ExecutablePath);
            return File.Exists(canonical)
                && string.Equals(
                    Path.GetExtension(canonical),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase)
                && TryValidateNoReparsePath(canonical, out _);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ApplicationLogicalIdentity(
        D045ApplicationCandidate candidate)
    {
        var canonical = Path.GetFullPath(candidate.ExecutablePath);
        if (D045HarnessHostTargetIdentity.TryCaptureNativeFileIdentity(
                canonical,
                out var nativeIdentity))
        {
            return "NATIVE:"
                + nativeIdentity
                + "\n"
                + candidate.FixedArguments;
        }
        // Without a native object identity, never merge two paths merely
        // because mutable metadata happens to match.
        return "PATH:"
            + canonical.Normalize(NormalizationForm.FormC).ToUpperInvariant()
            + "\n"
            + candidate.FixedArguments;
    }

    private static bool AllCandidatesAreTrustedPlatformAliases(
        IReadOnlyList<D045ApplicationCandidate> candidates)
    {
        if (candidates.Count < 2 || !OperatingSystem.IsWindows())
            return false;
        var identities = candidates
            .Select(TryReadTrustedPlatformApplicationIdentity)
            .ToArray();
        return identities.All(identity => identity.Length != 0)
            && identities.Distinct(StringComparer.Ordinal).Count() == 1;
    }

    private static string TryReadTrustedPlatformApplicationIdentity(
        D045ApplicationCandidate candidate)
    {
        try
        {
            if (candidate.FixedArguments.Length != 0)
                return string.Empty;
            var canonical = Path.GetFullPath(candidate.ExecutablePath);
            var windows = Path.GetFullPath(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");
            var trustedPath = canonical.StartsWith(
                    windows + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || canonical.StartsWith(
                    windowsApps + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
            if (!trustedPath)
                return string.Empty;
            var version = FileVersionInfo.GetVersionInfo(canonical);
            if (!string.Equals(
                    version.CompanyName?.Trim(),
                    "Microsoft Corporation",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            var internalName = NormalizeApplicationHostIdentity(
                version.InternalName);
            return internalName.Length == 0
                ? string.Empty
                : "MICROSOFT:" + internalName;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FileNotFoundException
                or IOException
                or NotSupportedException
                or PathTooLongException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> ExecutableNamesFor(string requested)
    {
        var normalized = NormalizeApplicationName(requested);
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["記事本"] = new[] { "notepad.exe" },
            ["记事本"] = new[] { "notepad.exe" },
            ["メモ帳"] = new[] { "notepad.exe" },
            ["notepad"] = new[] { "notepad.exe" },
            ["計算機"] = new[] { "calc.exe" },
            ["计算器"] = new[] { "calc.exe" },
            ["電卓"] = new[] { "calc.exe" },
            ["calculator"] = new[] { "calc.exe" },
            ["calc"] = new[] { "calc.exe" },
            ["檔案總管"] = new[] { "explorer.exe" },
            ["文件资源管理器"] = new[] { "explorer.exe" },
            ["資源管理器"] = new[] { "explorer.exe" },
            ["fileexplorer"] = new[] { "explorer.exe" },
            ["explorer"] = new[] { "explorer.exe" },
            ["chrome"] = new[] { "chrome.exe" },
            ["googlechrome"] = new[] { "chrome.exe" },
            ["edge"] = new[] { "msedge.exe" },
            ["microsoftedge"] = new[] { "msedge.exe" },
            ["firefox"] = new[] { "firefox.exe" },
            ["steam"] = new[] { "steam.exe" },
            ["discord"] = new[] { "Discord.exe" },
            ["spotify"] = new[] { "Spotify.exe" },
            ["vscode"] = new[] { "Code.exe" },
            ["visualstudiocode"] = new[] { "Code.exe" }
        };
        if (aliases.TryGetValue(normalized, out var mapped))
        {
            foreach (var value in mapped)
                yield return value;
        }

        var leaf = requested.Trim();
        if (Regex.IsMatch(leaf, "^[A-Za-z0-9_.+ -]{1,80}$"))
        {
            if (!leaf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                leaf += ".exe";
            yield return leaf;
        }
    }

    private static void AddRunningProcessCandidates(
        ICollection<D045ApplicationCandidate> candidates,
        string requested)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var score = Math.Max(
                    MatchApplicationName(requested, process.ProcessName),
                    MatchApplicationName(requested, process.MainWindowTitle));
                var path = process.MainModule?.FileName;
                if (score > 0 && !string.IsNullOrWhiteSpace(path))
                {
                    AddApplicationCandidate(
                        candidates,
                        process.MainWindowTitle.Length == 0
                            ? process.ProcessName
                            : process.MainWindowTitle,
                        path,
                        string.Empty,
                        score + 25);
                }
            }
            catch
            {
                // Protected and cross-architecture processes are intentionally skipped.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void AddExecutableCandidates(
        ICollection<D045ApplicationCandidate> candidates,
        string requested,
        string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName)
            || Path.GetFileName(executableName) != executableName)
        {
            return;
        }

        var locations = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        AddIfPresent(
            locations,
            Path.Combine(Environment.SystemDirectory, executableName),
            900);
        AddIfPresent(locations, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            executableName), 880);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH")
            ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                AddIfPresent(
                    locations,
                    Path.Combine(folder.Trim().Trim('"'), executableName),
                    800);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(hive, view);
                        using var key = root.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"
                                + executableName);
                        if (key?.GetValue(null) is string registered)
                        {
                            AddIfPresent(
                                locations,
                                registered.Trim().Trim('"'),
                                1200);
                        }
                    }
                    catch
                    {
                        // A missing registry view is equivalent to no candidate.
                    }
                }
            }
        }

        foreach (var location in locations)
        {
            var path = location.Key;
            AddApplicationCandidate(
                candidates,
                Path.GetFileNameWithoutExtension(path),
                path,
                string.Empty,
                location.Value + (MatchApplicationName(
                    requested,
                    Path.GetFileNameWithoutExtension(path)) / 20));
        }
    }

    private static void AddStartMenuShortcutCandidates(
        ICollection<D045ApplicationCandidate> candidates,
        string requested)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        foreach (var root in roots
            .Where(value => !string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(
                    root,
                    "*.lnk",
                    SearchOption.AllDirectories).Take(4096).ToArray();
            }
            catch
            {
                continue;
            }
            foreach (var shortcutPath in shortcuts)
            {
                var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                var score = MatchApplicationName(requested, displayName);
                if (score < 350
                    || !TryResolveShortcut(
                        shortcutPath,
                        out var executablePath,
                        out var arguments,
                        out var workingDirectory))
                {
                    continue;
                }
                AddApplicationCandidate(
                    candidates,
                    displayName,
                    executablePath,
                    arguments,
                    score,
                    workingDirectory);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryResolveShortcut(
        string shortcutPath,
        out string executablePath,
        out string arguments,
        out string workingDirectory)
    {
        executablePath = string.Empty;
        arguments = string.Empty;
        workingDirectory = string.Empty;
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath },
                CultureInfo.InvariantCulture);
            if (shortcut is null)
                return false;
            var shortcutType = shortcut.GetType();
            executablePath = Convert.ToString(shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null,
                CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) ?? string.Empty;
            arguments = Convert.ToString(shortcutType.InvokeMember(
                "Arguments",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null,
                CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) ?? string.Empty;
            workingDirectory = Convert.ToString(shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null,
                CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) ?? string.Empty;
            executablePath = Environment.ExpandEnvironmentVariables(
                executablePath.Trim().Trim('"'));
            return File.Exists(executablePath)
                && string.Equals(
                    Path.GetExtension(executablePath),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void AddApplicationCandidate(
        ICollection<D045ApplicationCandidate> candidates,
        string displayName,
        string executablePath,
        string fixedArguments,
        int score,
        string? workingDirectory = null)
    {
        var path = Environment.ExpandEnvironmentVariables(
            executablePath.Trim().Trim('"'));
        if (!File.Exists(path)
            || !string.Equals(
                Path.GetExtension(path),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var directory = !string.IsNullOrWhiteSpace(workingDirectory)
            && Directory.Exists(workingDirectory)
                ? Path.GetFullPath(workingDirectory)
                : Path.GetDirectoryName(path) ?? string.Empty;
        candidates.Add(new D045ApplicationCandidate(
            displayName.Trim(),
            Path.GetFullPath(path),
            fixedArguments ?? string.Empty,
            directory,
            score));
    }

    private static void AddIfPresent(
        IDictionary<string, int> paths,
        string candidate,
        int priority)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(
                candidate.Trim().Trim('"'));
            if (File.Exists(expanded))
            {
                var canonical = Path.GetFullPath(expanded);
                if (!paths.TryGetValue(canonical, out var existing)
                    || priority > existing)
                {
                    paths[canonical] = priority;
                }
            }
        }
        catch
        {
            // Invalid and inaccessible candidates are ignored.
        }
    }

    private static int MatchApplicationName(string requested, string candidate)
    {
        var left = NormalizeApplicationName(requested);
        var right = NormalizeApplicationName(candidate);
        if (left.Length < 2 || right.Length < 2)
            return 0;
        if (string.Equals(left, right, StringComparison.Ordinal))
            return 1000;
        if (right.StartsWith(left, StringComparison.Ordinal))
            return 800 - Math.Min(100, Math.Abs(right.Length - left.Length));
        if (right.Contains(left, StringComparison.Ordinal))
            return 600 - Math.Min(100, Math.Abs(right.Length - left.Length));
        if (left.Contains(right, StringComparison.Ordinal) && right.Length >= 4)
            return 400 - Math.Min(100, Math.Abs(right.Length - left.Length));
        return 0;
    }

    private static string NormalizeApplicationName(string value)
        => Regex.Replace(
            value.ToLowerInvariant(),
            @"[^\p{L}\p{N}]+",
            string.Empty,
            RegexOptions.CultureInvariant);

    private static D045ProcessResult RunPowerShell(string script, int timeoutMilliseconds)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return RunProcess(
            "powershell.exe",
            "-NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
            timeoutMilliseconds,
            useShellExecute: false);
    }

    private static D045ProcessResult RunProcess(
        string fileName,
        string arguments,
        int timeoutMilliseconds,
        bool useShellExecute)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName)
        {
            Arguments = arguments,
            UseShellExecute = useShellExecute,
            CreateNoWindow = !useShellExecute,
            RedirectStandardOutput = !useShellExecute,
            RedirectStandardError = !useShellExecute
        }) ?? throw new InvalidOperationException("Process could not be started.");
        var stdoutTask = useShellExecute ? null : process.StandardOutput.ReadToEndAsync();
        var stderrTask = useShellExecute ? null : process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Harness process timed out.");
        }
        return new D045ProcessResult(
            process.ExitCode,
            Limit(stdoutTask?.GetAwaiter().GetResult()),
            Limit(stderrTask?.GetAwaiter().GetResult()));
    }

    private D045PinnedHttpsTarget ResolvePinnedPublicHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length > 0)
        {
            throw new InvalidDataException("An absolute public HTTPS URL is required.");
        }

        var addresses = _publicHostResolver(uri.DnsSafeHost)
            .Select(NormalizeIpAddress)
            .Distinct()
            .OrderBy(address => address.AddressFamily)
            .ThenBy(address => address.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (addresses.Length == 0)
            throw new InvalidDataException("The HTTPS target did not resolve to an address.");
        if (addresses.Any(address => !IsPublicInternetAddress(address)))
        {
            throw new InvalidDataException(
                "Private, loopback, link-local and non-public HTTPS targets are rejected.");
        }
        return new D045PinnedHttpsTarget(
            uri,
            uri.DnsSafeHost,
            uri.IsDefaultPort ? 443 : uri.Port,
            addresses[0]);
    }

    private HttpClient CreatePinnedHttpClient(D045PinnedHttpsTarget target)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = false,
            ConnectCallback = (context, cancellationToken) =>
            {
                if (!string.Equals(
                        context.DnsEndPoint.Host,
                        target.Host,
                        StringComparison.OrdinalIgnoreCase)
                    || context.DnsEndPoint.Port != target.Port)
                {
                    throw new HttpRequestException(
                        "The HTTP connection endpoint changed after admission.");
                }
                return _pinnedConnectionFactory(
                    target.Address,
                    target.Port,
                    cancellationToken);
            }
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static async ValueTask<Stream> ConnectPinnedSocketAsync(
        IPAddress admittedAddress,
        int port,
        CancellationToken cancellationToken)
    {
        var normalizedAddress = NormalizeIpAddress(admittedAddress);
        var socket = new Socket(
            normalizedAddress.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(
                new IPEndPoint(normalizedAddress, port),
                cancellationToken).ConfigureAwait(false);
            if (socket.RemoteEndPoint is not IPEndPoint remote
                || remote.Port != port
                || !NormalizeIpAddress(remote.Address).Equals(normalizedAddress))
            {
                throw new IOException(
                    "The connected HTTPS endpoint did not match the admitted address.");
            }
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static IPAddress NormalizeIpAddress(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool IsPublicInternetAddress(IPAddress candidate)
    {
        var address = NormalizeIpAddress(candidate);
        if (IPAddress.IsLoopback(address))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return !(bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 198 && bytes[1] is 18 or 19)
                || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                || bytes[0] >= 224);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var documentation = bytes[0] == 0x20
                && bytes[1] == 0x01
                && bytes[2] == 0x0D
                && bytes[3] == 0xB8;
            return !address.Equals(IPAddress.IPv6Any)
                && !address.Equals(IPAddress.IPv6None)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6SiteLocal
                && (bytes[0] & 0xFE) != 0xFC
                && bytes[0] != 0xFF
                && !documentation;
        }
        return false;
    }

    private static string DemandExistingFile(string value)
    {
        var path = Path.GetFullPath(value);
        if (Directory.Exists(path))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.TargetWrongKind,
                "The exact target is a directory, not a file.");
        }
        if (!File.Exists(path))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.TargetNotFound,
                "Exact file target was not found.",
                retryAllowed: true,
                retryRequiresChangedTarget: true);
        }
        return path;
    }

    private static string DemandExistingDirectory(string value)
    {
        var path = Path.GetFullPath(value);
        if (File.Exists(path))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.TargetWrongKind,
                "The exact target is a file, not a directory.");
        }
        if (!Directory.Exists(path))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.TargetNotFound,
                "Exact folder target was not found.",
                retryAllowed: true,
                retryRequiresChangedTarget: true);
        }
        return path;
    }

    private static string DemandNewDataTarget(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("An exact new data path is required.");
        var path = Path.GetFullPath(value).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(path)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(path)
            || string.IsNullOrWhiteSpace(root)
            || string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A filesystem root cannot be a data-creation target.");
        }
        if (File.Exists(path) || Directory.Exists(path))
            throw new InvalidDataException("The exact target already exists.");
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                "The exact parent directory does not exist.");
        }
        return path;
    }

    private static string DemandSafeDestructivePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.InvalidRequest,
                "An exact destructive filesystem target is required.");
        }

        var path = CanonicalPath(value);
        var root = Path.GetPathRoot(path);
        var exactProtectedRoots = new[]
        {
            root,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        }.Where(item => !string.IsNullOrWhiteSpace(item));
        if (IsProtectedDestructivePath(
                path,
                exactProtectedRoots,
                Array.Empty<string>()))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.InvalidRequest,
                "Filesystem roots and the user-profile root cannot be destructive targets.");
        }

        var protectedTrees = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        }.Where(item => !string.IsNullOrWhiteSpace(item));
        if (IsProtectedDestructivePath(
                path,
                Array.Empty<string>(),
                protectedTrees))
        {
            throw new D045HarnessRejectionException(
                D045HarnessOutcomeReason.InvalidRequest,
                "Windows, System, and Program Files trees cannot be destructive targets.");
        }
        return path;
    }

    internal static bool IsProtectedDestructivePath(
        string path,
        IEnumerable<string?> exactProtectedRoots,
        IEnumerable<string?> protectedTrees)
    {
        var canonicalPath = CanonicalPath(path);
        return exactProtectedRoots
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Any(item => string.Equals(
                    canonicalPath,
                    CanonicalPath(item!),
                    StringComparison.OrdinalIgnoreCase))
            || protectedTrees
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Any(item => IsExactDescendantOrSame(item!, canonicalPath));
    }

    private static void DemandNoReparseDestructivePath(string path)
    {
        if (TryValidateNoReparsePath(path, out var message))
            return;
        throw new D045HarnessRejectionException(
            D045HarnessOutcomeReason.InvalidRequest,
            "Destructive target rejected: " + message);
    }

    private static string CanonicalPath(string value)
    {
        var canonical = Path.GetFullPath(value);
        var root = Path.GetPathRoot(canonical);
        if (string.IsNullOrWhiteSpace(root))
            return canonical;
        var trimmed = canonical.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var trimmedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return string.Equals(
                trimmed,
                trimmedRoot,
                StringComparison.OrdinalIgnoreCase)
            ? root
            : trimmed;
    }

    private static int DemandPositiveInt32(string value, string label)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
            throw new InvalidDataException(label + " must be a positive integer.");
        return result;
    }

    private static byte[] ReadBounded(Stream stream, int maximumBytes)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return memory.ToArray();
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("Response exceeds the Harness limit.");
            memory.Write(buffer, 0, read);
        }
    }

    private static string DecodeText(byte[] bytes)
        => Limit(DecodeTextWithoutLimit(bytes));

    private static string DecodeTextWithoutLimit(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToBase64String(bytes);
        }
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");

    private static string Limit(string? value)
    {
        value ??= string.Empty;
        return value.Length <= MaximumTextOutputCharacters
            ? value
            : value[..MaximumTextOutputCharacters];
    }

    private static D045HarnessReceipt Receipt(
        D045HarnessRequest request,
        bool attempted,
        bool verified,
        int exitCode,
        string message,
        string output = "",
        string? resolvedTarget = null,
        string? contentSha256 = null,
        D045ExpectedTargetKind resolvedTargetKind = D045ExpectedTargetKind.None,
        string? resolvedExtension = null,
        D045HarnessOutcomeReason outcomeReason = D045HarnessOutcomeReason.Unspecified,
        D045HarnessVerificationStrength verificationStrength = D045HarnessVerificationStrength.None,
        D045HarnessPostconditionCode postconditionCode = D045HarnessPostconditionCode.None,
        bool? retryAllowed = null,
        bool retryRequiresChangedTarget = false)
        => new(
            request,
            attempted,
            verified,
            exitCode,
            message,
            Limit(output),
            resolvedTarget: resolvedTarget,
            contentSha256: contentSha256,
            resolvedTargetKind: resolvedTargetKind,
            resolvedExtension: resolvedExtension,
            outcomeReason: outcomeReason,
            verificationStrength: verificationStrength,
            postconditionCode: postconditionCode,
            retryAllowed: retryAllowed,
            retryRequiresChangedTarget: retryRequiresChangedTarget);

    private static IReadOnlyList<D045VisibleApplicationWindow>
        EnumerateVisibleExactImageWindows(string executablePath)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<D045VisibleApplicationWindow>();
        var expectedPath = Path.GetFullPath(executablePath);
        var result = new List<D045VisibleApplicationWindow>();
        _ = EnumWindows((window, ignored) =>
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window))
                return true;
            _ = GetWindowThreadProcessId(window, out var processIdValue);
            if (processIdValue == 0 || processIdValue > int.MaxValue)
                return true;
            try
            {
                using var process = Process.GetProcessById((int)processIdValue);
                if (process.HasExited)
                    return true;
                var image = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(image)
                    || !string.Equals(
                        Path.GetFullPath(image),
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                result.Add(new D045VisibleApplicationWindow(
                    process.Id,
                    window,
                    expectedPath));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or NotSupportedException
                    or System.ComponentModel.Win32Exception)
            {
                // A window may disappear while it is being enumerated. It is
                // not usable as stable host evidence in that case.
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const uint ShellExecuteNoCloseProcess = 0x00000040;
    private const uint ShellExecuteNoUi = 0x00000400;
    private const int ShowWindowRestore = 9;

    private delegate bool D045EnumWindowsCallback(
        IntPtr window,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        D045EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint sourceThread,
        uint targetThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        D045NativeInput[] inputs,
        int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct D045NativeInput
    {
        internal uint Type;
        internal D045NativeInputUnion Data;

        internal static D045NativeInput Unicode(char character, bool keyUp)
            => new()
            {
                Type = 1,
                Data = new D045NativeInputUnion
                {
                    Keyboard = new D045NativeKeyboardInput
                    {
                        Scan = character,
                        Flags = 0x0004u | (keyUp ? 0x0002u : 0u)
                    }
                }
            };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct D045NativeInputUnion
    {
        [FieldOffset(0)]
        internal D045NativeMouseInput Mouse;
        [FieldOffset(0)]
        internal D045NativeKeyboardInput Keyboard;
        [FieldOffset(0)]
        internal D045NativeHardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D045NativeMouseInput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D045NativeKeyboardInput
    {
        internal ushort VirtualKey;
        internal ushort Scan;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D045NativeHardwareInput
    {
        internal uint Message;
        internal ushort ParameterLow;
        internal ushort ParameterHigh;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid folderId,
        uint flags,
        IntPtr token,
        out IntPtr path);

    [DllImport(
        "shell32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(
        ref D045ShellExecuteInfo executeInfo);

    [DllImport("kernel32.dll")]
    private static extern uint GetProcessId(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct D045ShellExecuteInfo
    {
        internal int Size;
        internal uint Mask;
        internal IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? File;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? Directory;
        internal int Show;
        internal IntPtr Instance;
        internal IntPtr ItemIdList;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? Class;
        internal IntPtr ClassKey;
        internal uint HotKey;
        internal IntPtr IconOrMonitor;
        internal IntPtr Process;
    }

    private sealed class D045LocalPathResolution
    {
        internal bool Resolved { get; }
        internal string CanonicalPath { get; }
        internal string Message { get; }
        internal string CandidateSummary { get; }
        internal D045HarnessOutcomeReason OutcomeReason { get; }
        internal bool RetryAllowed { get; }
        internal bool RetryRequiresChangedTarget { get; }

        private D045LocalPathResolution(
            bool resolved,
            string canonicalPath,
            string message,
            string candidateSummary,
            D045HarnessOutcomeReason outcomeReason,
            bool retryAllowed,
            bool retryRequiresChangedTarget)
        {
            Resolved = resolved;
            CanonicalPath = canonicalPath;
            Message = message;
            CandidateSummary = candidateSummary;
            OutcomeReason = outcomeReason;
            RetryAllowed = retryAllowed;
            RetryRequiresChangedTarget = retryAllowed
                && retryRequiresChangedTarget;
        }

        internal static D045LocalPathResolution Success(string path)
            => new(
                true,
                path,
                "Exact local path resolved.",
                string.Empty,
                D045HarnessOutcomeReason.Unspecified,
                false,
                false);

        internal static D045LocalPathResolution NotFound(
            string message,
            D045HarnessOutcomeReason outcomeReason = D045HarnessOutcomeReason.TargetNotFound)
            => new(
                false,
                string.Empty,
                message,
                string.Empty,
                outcomeReason,
                outcomeReason == D045HarnessOutcomeReason.TargetNotFound,
                outcomeReason == D045HarnessOutcomeReason.TargetNotFound);

        internal static D045LocalPathResolution Ambiguous(
            IEnumerable<string> candidates)
        {
            var bounded = candidates
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
            return new(
                false,
                string.Empty,
                "More than one exact known-folder item matched. Retry with one canonical path or exact relative filename.",
                string.Join(Environment.NewLine, bounded),
                D045HarnessOutcomeReason.TargetAmbiguous,
                true,
                true);
        }
    }

    internal sealed record D045ApplicationCandidate(
        string DisplayName,
        string ExecutablePath,
        string FixedArguments,
        string WorkingDirectory,
        int Score);

    internal sealed record D045ApplicationLaunchAttempt(
        int ProcessId,
        bool Verified,
        int ExitCode,
        string Message)
    {
        internal static D045ApplicationLaunchAttempt Failure(string message)
            => new(0, false, -1, message);
    }

    internal sealed record D045VisibleApplicationWindow(
        int ProcessId,
        IntPtr Handle,
        string ExecutablePath);

    private sealed record D045VisibleApplicationWindowSelection(
        D045VisibleApplicationWindow? Window,
        D045HarnessOutcomeReason Failure)
    {
        internal static D045VisibleApplicationWindowSelection Success(
            D045VisibleApplicationWindow window)
            => new(window, D045HarnessOutcomeReason.Unspecified);

        internal static D045VisibleApplicationWindowSelection NotFound()
            => new(null, D045HarnessOutcomeReason.TargetNotFound);

        internal static D045VisibleApplicationWindowSelection Ambiguous()
            => new(null, D045HarnessOutcomeReason.TargetAmbiguous);
    }

    private sealed record D045PinnedHttpsTarget(
        Uri Uri,
        string Host,
        int Port,
        IPAddress Address);

    private sealed record D045ApplicationLaunchVerification(
        bool Verified,
        int ExitCode,
        string Message)
    {
        internal static D045ApplicationLaunchVerification Success(string message)
            => new(true, 0, message);

        internal static D045ApplicationLaunchVerification Failure(
            string message,
            int exitCode = -1)
            => new(false, exitCode, message);
    }

    private sealed class D045ApplicationResolution
    {
        internal bool Resolved { get; }
        internal string ExecutablePath { get; }
        internal string FixedArguments { get; }
        internal string WorkingDirectory { get; }
        internal string Message { get; }
        internal string CandidateSummary { get; }
        internal D045HarnessOutcomeReason OutcomeReason { get; }
        internal bool RetryAllowed { get; }
        internal bool RetryRequiresChangedTarget { get; }

        private D045ApplicationResolution(
            bool resolved,
            string executablePath,
            string fixedArguments,
            string workingDirectory,
            string message,
            string candidateSummary,
            D045HarnessOutcomeReason outcomeReason,
            bool retryAllowed,
            bool retryRequiresChangedTarget)
        {
            Resolved = resolved;
            ExecutablePath = executablePath;
            FixedArguments = fixedArguments;
            WorkingDirectory = workingDirectory;
            Message = message;
            CandidateSummary = candidateSummary;
            OutcomeReason = outcomeReason;
            RetryAllowed = retryAllowed;
            RetryRequiresChangedTarget = retryAllowed
                && retryRequiresChangedTarget;
        }

        internal static D045ApplicationResolution Success(
            string displayName,
            string executablePath,
            string fixedArguments,
            string workingDirectory)
            => new(
                true,
                executablePath,
                fixedArguments,
                workingDirectory,
                "Resolved installed application '" + displayName
                    + "' to one exact local executable.",
                string.Empty,
                D045HarnessOutcomeReason.Unspecified,
                false,
                false);

        internal static D045ApplicationResolution NotFound(
            string message,
            D045HarnessOutcomeReason outcomeReason = D045HarnessOutcomeReason.TargetNotFound)
            => new(
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                message,
                string.Empty,
                outcomeReason,
                outcomeReason == D045HarnessOutcomeReason.TargetNotFound,
                outcomeReason == D045HarnessOutcomeReason.TargetNotFound);

        internal static D045ApplicationResolution Ambiguous(
            IEnumerable<string> candidates)
        {
            var values = candidates
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
            return new(
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                "More than one installed application matched. Retry with one exact candidate name.",
                string.Join(Environment.NewLine, values),
                D045HarnessOutcomeReason.TargetAmbiguous,
                true,
                true);
        }
    }

    private sealed record D045ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class D045HarnessRejectionException : Exception
    {
        internal D045HarnessOutcomeReason OutcomeReason { get; }
        internal bool RetryAllowed { get; }
        internal bool RetryRequiresChangedTarget { get; }

        internal D045HarnessRejectionException(
            D045HarnessOutcomeReason outcomeReason,
            string message,
            bool retryAllowed = false,
            bool retryRequiresChangedTarget = false)
            : base(message)
        {
            OutcomeReason = outcomeReason;
            RetryAllowed = retryAllowed;
            RetryRequiresChangedTarget = retryAllowed
                && retryRequiresChangedTarget;
        }
    }
}
