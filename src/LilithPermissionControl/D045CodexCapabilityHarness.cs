using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LilithPermissionControl;

internal enum D045CodexOperation
{
    Default = 0,
    FileOverwrite = 1,
    FileAppend = 2,
    ArchiveCreate = 3,
    ArchiveExtract = 4,
    WindowFocus = 5,
    WindowMinimize = 6,
    WindowMaximize = 7,
    WindowRestore = 8,
    UiInvoke = 9,
    UiSetValue = 10,
    PointerMove = 11,
    PointerClick = 12,
    PointerDoubleClick = 13,
    PointerDrag = 14,
    BackgroundStart = 15,
    BackgroundStatus = 16,
    BackgroundStop = 17,
    UiFocus = 18,
    UiToggle = 19,
    UiSelect = 20,
    UiScroll = 21
}

internal enum D045CodexOutcomeReason
{
    Unspecified = 0,
    EffectVerified = 1,
    PermissionDefaultOff = 2,
    PermissionCompositionMissing = 3,
    PermissionChangedBeforeAttempt = 4,
    PermissionChangedAfterAttempt = 5,
    RequestAlreadyConsumed = 6,
    InvalidRequest = 7,
    TargetNotFound = 8,
    TargetAlreadyExists = 9,
    TargetUnsafe = 10,
    TargetAmbiguous = 11,
    AccessDenied = 12,
    Unsupported = 13,
    Timeout = 14,
    OutputLimitExceeded = 15,
    IntegrityMismatch = 16,
    ToolFailure = 17,
    OperationCancelled = 18
}

internal enum D045CodexPostcondition
{
    None = 0,
    BoundedReadComplete = 1,
    ClipboardContentMatches = 2,
    TargetContentMatches = 3,
    TargetExists = 4,
    SourceAbsentAndTargetExists = 5,
    ArchiveValidated = 6,
    ProcessRunning = 7,
    ProcessExitCodeZero = 8,
    ProcessExited = 9,
    WindowStateMatches = 10,
    DispatchAccepted = 11,
    HttpsBodyDigestMatches = 12
}

/// <summary>
/// One closed request shape for the append-only D-045 rows 21-42. Capability-
/// specific validation rejects every irrelevant or missing field before an OS
/// effect. Arguments are an array and are never concatenated into a shell.
/// </summary>
internal sealed class D045CodexCapabilityRequest
{
    internal Guid TaskId { get; }
    internal D045PermissionCapability Capability { get; }
    internal D045CodexOperation Operation { get; }
    internal string Target { get; }
    internal string SecondaryTarget { get; }
    internal string Payload { get; }
    internal string Query { get; }
    internal string WorkingDirectory { get; }
    internal string ExpectedSha256 { get; }
    internal string OwnerId { get; }
    internal IReadOnlyList<string> Arguments { get; }
    internal bool Recursive { get; }
    internal int MaximumResults { get; }
    internal int MaximumDepth { get; }
    internal long MaximumBytes { get; }
    internal int TimeoutMilliseconds { get; }
    internal long ExpectedPermissionRevision { get; }

    internal D045CodexCapabilityRequest(
        Guid taskId,
        D045PermissionCapability capability,
        D045CodexOperation operation = D045CodexOperation.Default,
        string? target = null,
        string? secondaryTarget = null,
        string? payload = null,
        string? query = null,
        string? workingDirectory = null,
        string? expectedSha256 = null,
        string? ownerId = null,
        IEnumerable<string>? arguments = null,
        bool recursive = true,
        int maximumResults = 64,
        int maximumDepth = 8,
        long maximumBytes = 64L * 1024 * 1024,
        int timeoutMilliseconds = 30_000,
        long expectedPermissionRevision = 0)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("A request identity is required.", nameof(taskId));
        if ((int)capability is < 21 or > 42)
            throw new ArgumentOutOfRangeException(nameof(capability));
        if (!Enum.IsDefined(typeof(D045CodexOperation), operation))
            throw new ArgumentOutOfRangeException(nameof(operation));
        if (timeoutMilliseconds is < 1_000 or > 120_000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (maximumResults is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        if (maximumDepth is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        if (maximumBytes is < 1_024 or > 256L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (expectedPermissionRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPermissionRevision));

        var materializedArguments = (arguments ?? Array.Empty<string>()).ToArray();
        if (materializedArguments.Length > 128
            || materializedArguments.Any(value => value is null
                || value.Length > 8_192
                || value.IndexOf('\0') >= 0))
        {
            throw new ArgumentException(
                "Arguments must contain at most 128 bounded non-null values.",
                nameof(arguments));
        }

        TaskId = taskId;
        Capability = capability;
        Operation = operation;
        Target = NormalizeField(target, 32_768, nameof(target));
        SecondaryTarget = NormalizeField(
            secondaryTarget,
            32_768,
            nameof(secondaryTarget));
        Payload = NormalizePayload(payload, maximumBytes);
        Query = NormalizeField(query, 1_024, nameof(query));
        WorkingDirectory = NormalizeField(
            workingDirectory,
            32_768,
            nameof(workingDirectory));
        ExpectedSha256 = NormalizeSha256(expectedSha256);
        OwnerId = NormalizeField(ownerId, 128, nameof(ownerId));
        Arguments = Array.AsReadOnly(materializedArguments);
        Recursive = recursive;
        MaximumResults = maximumResults;
        MaximumDepth = maximumDepth;
        MaximumBytes = maximumBytes;
        TimeoutMilliseconds = timeoutMilliseconds;
        ExpectedPermissionRevision = expectedPermissionRevision;
    }

    private static string NormalizeField(string? value, int maximum, string field)
    {
        var normalized = (value ?? string.Empty).Trim().Normalize(
            NormalizationForm.FormC);
        if (normalized.Length > maximum || normalized.IndexOf('\0') >= 0)
            throw new ArgumentException("The request field is invalid.", field);
        return normalized;
    }

    private static string NormalizePayload(string? value, long maximumBytes)
    {
        var normalized = value ?? string.Empty;
        if (normalized.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(normalized) > maximumBytes)
        {
            throw new ArgumentException("The payload exceeds its byte ceiling.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeSha256(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length == 0)
            return string.Empty;
        if (normalized.Length != 64
            || normalized.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "Expected SHA-256 must be empty or 64 hexadecimal characters.",
                nameof(value));
        }
        return normalized;
    }
}

internal sealed class D045CodexCapabilityReceipt
{
    internal Guid TaskId { get; }
    internal D045PermissionCapability Capability { get; }
    internal bool Attempted { get; }
    internal bool Verified { get; }
    internal bool EffectPossiblyApplied { get; }
    internal int ExitCode { get; }
    internal string Message { get; }
    internal string Output { get; }
    internal string ContentSha256 { get; }
    internal string JobId { get; }
    internal long PermissionRevision { get; }
    internal D045CodexOutcomeReason OutcomeReason { get; }
    internal D045CodexPostcondition Postcondition { get; }
    internal string RequestFingerprint { get; }
    internal string OutcomeFingerprint { get; }

    internal D045CodexCapabilityReceipt(
        D045CodexCapabilityRequest request,
        bool attempted,
        bool verified,
        bool effectPossiblyApplied,
        int exitCode,
        string message,
        string output,
        string contentSha256,
        string jobId,
        long permissionRevision,
        D045CodexOutcomeReason outcomeReason,
        D045CodexPostcondition postcondition)
    {
        if (!Enum.IsDefined(typeof(D045CodexOutcomeReason), outcomeReason))
            throw new ArgumentOutOfRangeException(nameof(outcomeReason));
        if (!Enum.IsDefined(typeof(D045CodexPostcondition), postcondition))
            throw new ArgumentOutOfRangeException(nameof(postcondition));
        TaskId = request.TaskId;
        Capability = request.Capability;
        Attempted = attempted;
        Verified = verified;
        EffectPossiblyApplied = effectPossiblyApplied;
        ExitCode = exitCode;
        Message = Limit(message, 4_096);
        Output = Limit(output, D045CodexCapabilityHarness.MaximumOutputCharacters);
        ContentSha256 = contentSha256 ?? string.Empty;
        JobId = jobId ?? string.Empty;
        PermissionRevision = permissionRevision;
        OutcomeReason = verified
            ? D045CodexOutcomeReason.EffectVerified
            : outcomeReason == D045CodexOutcomeReason.Unspecified
                ? D045CodexOutcomeReason.ToolFailure
                : outcomeReason;
        Postcondition = verified ? postcondition : D045CodexPostcondition.None;
        RequestFingerprint = FingerprintRequest(request);
        OutcomeFingerprint = FingerprintOutcome(this);
    }

    internal D045CodexCapabilityReceipt RejectAfterPermissionChange(
        long currentRevision)
        => new(this, currentRevision);

    private D045CodexCapabilityReceipt(
        D045CodexCapabilityReceipt source,
        long currentRevision)
    {
        TaskId = source.TaskId;
        Capability = source.Capability;
        Attempted = source.Attempted;
        Verified = false;
        EffectPossiblyApplied = source.Attempted;
        ExitCode = source.ExitCode;
        Message = source.Attempted
            ? "Permission changed after the operation may have been applied."
            : "Permission changed before the operation was attempted.";
        Output = source.Output;
        ContentSha256 = source.ContentSha256;
        JobId = source.JobId;
        PermissionRevision = currentRevision;
        OutcomeReason = source.Attempted
            ? D045CodexOutcomeReason.PermissionChangedAfterAttempt
            : D045CodexOutcomeReason.PermissionChangedBeforeAttempt;
        Postcondition = D045CodexPostcondition.None;
        RequestFingerprint = source.RequestFingerprint;
        OutcomeFingerprint = FingerprintOutcome(this);
    }

    private static string FingerprintRequest(D045CodexCapabilityRequest request)
    {
        var builder = new StringBuilder("D045.CODEX.REQUEST.V1\n", 1_024);
        Append(builder, ((int)request.Capability).ToString(CultureInfo.InvariantCulture));
        Append(builder, ((int)request.Operation).ToString(CultureInfo.InvariantCulture));
        Append(builder, request.Target);
        Append(builder, request.SecondaryTarget);
        Append(builder, Hash(request.Payload));
        Append(builder, request.Query);
        Append(builder, request.WorkingDirectory);
        Append(builder, request.ExpectedSha256);
        Append(builder, request.OwnerId);
        foreach (var argument in request.Arguments)
            Append(builder, argument);
        Append(builder, request.Recursive ? "1" : "0");
        Append(builder, request.MaximumResults.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.MaximumDepth.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.MaximumBytes.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture));
        Append(builder, request.ExpectedPermissionRevision.ToString(CultureInfo.InvariantCulture));
        return Hash(builder.ToString());
    }

    private static string FingerprintOutcome(D045CodexCapabilityReceipt receipt)
    {
        var builder = new StringBuilder("D045.CODEX.OUTCOME.V1\n", 512);
        Append(builder, receipt.Attempted ? "1" : "0");
        Append(builder, receipt.Verified ? "1" : "0");
        Append(builder, receipt.EffectPossiblyApplied ? "1" : "0");
        Append(builder, receipt.ExitCode.ToString(CultureInfo.InvariantCulture));
        Append(builder, Hash(receipt.Message));
        Append(builder, Hash(receipt.Output));
        Append(builder, receipt.ContentSha256);
        Append(builder, receipt.JobId);
        Append(builder, ((int)receipt.OutcomeReason).ToString(CultureInfo.InvariantCulture));
        Append(builder, ((int)receipt.Postcondition).ToString(CultureInfo.InvariantCulture));
        return Hash(builder.ToString());
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append('\n');
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Limit(string? value, int maximum)
    {
        var result = value ?? string.Empty;
        return result.Length <= maximum ? result : result[..maximum];
    }
}

internal sealed class D045CodexPublicHttpsResponse : IDisposable
{
    internal HttpStatusCode StatusCode { get; }
    internal long? ContentLength { get; }
    internal Stream Body { get; }
    private readonly Action? _dispose;

    internal D045CodexPublicHttpsResponse(
        HttpStatusCode statusCode,
        Stream body,
        long? contentLength = null,
        Action? dispose = null)
    {
        StatusCode = statusCode;
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ContentLength = contentLength;
        _dispose = dispose;
    }

    public void Dispose()
    {
        Body.Dispose();
        _dispose?.Invoke();
    }
}

internal delegate D045CodexPublicHttpsResponse D045CodexPublicHttpsSender(
    Uri uri,
    IPAddress pinnedAddress,
    int timeoutMilliseconds,
    CancellationToken cancellationToken);

internal enum D045CodexPointerButton
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 3
}

/// <summary>
/// A host-resolved, visible top-level window with its client bounds expressed
/// in virtual-screen coordinates. The handle is never accepted from model
/// input; it comes only from the trusted host resolver.
/// </summary>
internal sealed class D045CodexWindowTarget
{
    internal IntPtr Handle { get; }
    internal int ProcessId { get; }
    internal string Title { get; }
    internal int ClientLeft { get; }
    internal int ClientTop { get; }
    internal int ClientWidth { get; }
    internal int ClientHeight { get; }

    internal D045CodexWindowTarget(
        IntPtr handle,
        int processId,
        string title,
        int clientLeft,
        int clientTop,
        int clientWidth,
        int clientHeight)
    {
        if (handle == IntPtr.Zero)
            throw new ArgumentException("A non-zero host window handle is required.", nameof(handle));
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));
        if (clientWidth <= 0 || clientHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(clientWidth));

        Handle = handle;
        ProcessId = processId;
        Title = (title ?? string.Empty).Trim();
        ClientLeft = clientLeft;
        ClientTop = clientTop;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
    }
}

/// <summary>
/// Fully resolved pointer input. Normalized coordinates are retained for an
/// auditable receipt while screen coordinates are host-derived and bounded to
/// the exact target window's client area.
/// </summary>
internal sealed class D045CodexPointerDispatch
{
    internal D045CodexWindowTarget Window { get; }
    internal D045CodexOperation Operation { get; }
    internal D045CodexPointerButton Button { get; }
    internal double StartNormalizedX { get; }
    internal double StartNormalizedY { get; }
    internal double EndNormalizedX { get; }
    internal double EndNormalizedY { get; }
    internal int StartScreenX { get; }
    internal int StartScreenY { get; }
    internal int EndScreenX { get; }
    internal int EndScreenY { get; }

    internal D045CodexPointerDispatch(
        D045CodexWindowTarget window,
        D045CodexOperation operation,
        D045CodexPointerButton button,
        double startNormalizedX,
        double startNormalizedY,
        double endNormalizedX,
        double endNormalizedY)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Operation = operation;
        Button = button;
        StartNormalizedX = startNormalizedX;
        StartNormalizedY = startNormalizedY;
        EndNormalizedX = endNormalizedX;
        EndNormalizedY = endNormalizedY;
        StartScreenX = ResolveScreenCoordinate(
            window.ClientLeft,
            window.ClientWidth,
            startNormalizedX);
        StartScreenY = ResolveScreenCoordinate(
            window.ClientTop,
            window.ClientHeight,
            startNormalizedY);
        EndScreenX = ResolveScreenCoordinate(
            window.ClientLeft,
            window.ClientWidth,
            endNormalizedX);
        EndScreenY = ResolveScreenCoordinate(
            window.ClientTop,
            window.ClientHeight,
            endNormalizedY);
    }

    private static int ResolveScreenCoordinate(int origin, int length, double normalized)
    {
        if (!double.IsFinite(normalized) || normalized is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(normalized));
        var offset = (long)Math.Floor(normalized * (length - 1L));
        var result = checked((long)origin + offset);
        if (result < int.MinValue || result > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(origin));
        return (int)result;
    }
}

/// <summary>
/// One bounded semantic element resolved by the trusted host adapter. Raw
/// handles never cross the provider boundary. Name is already redacted when
/// the native control can contain a password, value, or document body.
/// </summary>
internal sealed class D045CodexSemanticElement
{
    internal D045CodexWindowTarget Window { get; }
    internal IntPtr Handle { get; }
    internal int Depth { get; }
    internal string Role { get; }
    internal string Name { get; }
    internal bool Enabled { get; }
    internal bool Visible { get; }
    internal int Left { get; }
    internal int Top { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal bool SupportsInvoke { get; }
    internal bool SupportsFocus { get; }
    internal bool SupportsToggle { get; }
    internal bool SupportsSelect { get; }
    internal bool SupportsScroll { get; }

    internal D045CodexSemanticElement(
        D045CodexWindowTarget window,
        IntPtr handle,
        int depth,
        string role,
        string name,
        bool enabled,
        bool visible,
        int left,
        int top,
        int width,
        int height,
        bool supportsInvoke = false,
        bool supportsFocus = true,
        bool supportsToggle = false,
        bool supportsSelect = false,
        bool supportsScroll = false)
    {
        if (window is null)
            throw new ArgumentNullException(nameof(window));
        if (handle == IntPtr.Zero)
            throw new ArgumentException(
                "A non-zero trusted element handle is required.",
                nameof(handle));
        if (depth is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));

        Window = window;
        Handle = handle;
        Depth = depth;
        Role = NormalizeMetadata(role, 64, nameof(role));
        Name = NormalizeMetadata(name, 128, nameof(name));
        Enabled = enabled;
        Visible = visible;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        SupportsInvoke = supportsInvoke;
        SupportsFocus = supportsFocus;
        SupportsToggle = supportsToggle;
        SupportsSelect = supportsSelect;
        SupportsScroll = supportsScroll;
    }

    internal bool Supports(D045CodexOperation operation)
        => operation switch
        {
            D045CodexOperation.UiInvoke => SupportsInvoke,
            D045CodexOperation.UiFocus => SupportsFocus,
            D045CodexOperation.UiToggle => SupportsToggle,
            D045CodexOperation.UiSelect => SupportsSelect,
            D045CodexOperation.UiScroll => SupportsScroll,
            _ => false
        };

    private static string NormalizeMetadata(
        string? value,
        int maximum,
        string parameter)
    {
        var result = (value ?? string.Empty).Trim().Normalize(
            NormalizationForm.FormC);
        if (result.Length > maximum || result.IndexOf('\0') >= 0)
            throw new ArgumentException("Semantic metadata is invalid.", parameter);
        return result;
    }
}

internal sealed class D045CodexSemanticDispatch
{
    internal D045CodexWindowTarget Window { get; }
    internal D045CodexSemanticElement Element { get; }
    internal D045CodexOperation Operation { get; }
    internal string ScrollDirection { get; }
    internal int TimeoutMilliseconds { get; }

    internal D045CodexSemanticDispatch(
        D045CodexWindowTarget window,
        D045CodexSemanticElement element,
        D045CodexOperation operation,
        string scrollDirection,
        int timeoutMilliseconds)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Element = element ?? throw new ArgumentNullException(nameof(element));
        if (!element.Supports(operation))
            throw new ArgumentException(
                "The resolved element does not support the typed operation.",
                nameof(operation));
        if (timeoutMilliseconds is < 1_000 or > 120_000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        Operation = operation;
        ScrollDirection = scrollDirection ?? string.Empty;
        TimeoutMilliseconds = timeoutMilliseconds;
    }
}

internal sealed class D045CodexSemanticDispatchResult
{
    internal bool Accepted { get; }
    internal bool SemanticOutcomeObserved { get; }
    internal string Observation { get; }

    internal D045CodexSemanticDispatchResult(
        bool accepted,
        bool semanticOutcomeObserved = false,
        string? observation = null)
    {
        Accepted = accepted;
        SemanticOutcomeObserved = accepted && semanticOutcomeObserved;
        Observation = LimitObservation(observation);
    }

    private static string LimitObservation(string? value)
    {
        var result = value ?? string.Empty;
        return result.Length <= 128 ? result : result[..128];
    }
}

/// <summary>
/// Typed execution owner for the C7 workstation leaves. It neither parses
/// model prose nor changes permission state. Every invocation rechecks the
/// exact permission revision at the final dispatch boundary and afterwards.
/// </summary>
internal sealed partial class D045CodexCapabilityHarness
{
    internal const int MaximumOutputCharacters = 64 * 1024;
    internal const int MaximumSearchFiles = 4_096;
    internal const int MaximumArchiveEntries = 4_096;
    internal const long MaximumArchiveEntryBytes = 128L * 1024 * 1024;
    internal const long MaximumArchiveExpandedBytes = 256L * 1024 * 1024;
    internal const int MaximumArchiveCompressionRatio = 100;

    private readonly D045PermissionControlStore _permissions;
    private readonly Func<string, IPAddress[]> _hostResolver;
    private readonly D045CodexPublicHttpsSender _httpsSender;
    private readonly Func<string, bool> _clipboardWriter;
    private readonly Func<Uri, bool> _browserNavigator;
    private readonly Func<int, IReadOnlyList<D045CodexWindowTarget>> _visibleWindowResolver;
    private readonly Func<D045CodexPointerDispatch, bool> _pointerDispatcher;
    private readonly Func<IntPtr, bool> _foregroundVerifier;
    private readonly Func<D045CodexWindowTarget, int, int,
        IReadOnlyList<D045CodexSemanticElement>> _semanticUiInspector;
    private readonly Func<D045CodexSemanticDispatch,
        D045CodexSemanticDispatchResult> _semanticUiDispatcher;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _consumedRequests = new();
    private readonly ConcurrentDictionary<string, BackgroundJob> _backgroundJobs =
        new(StringComparer.Ordinal);

    internal D045CodexCapabilityHarness(
        D045PermissionControlStore permissions,
        Func<string, IPAddress[]>? hostResolver = null,
        D045CodexPublicHttpsSender? httpsSender = null,
        Func<string, bool>? clipboardWriter = null,
        Func<Uri, bool>? browserNavigator = null,
        Func<int, IReadOnlyList<D045CodexWindowTarget>>? visibleWindowResolver = null,
        Func<D045CodexPointerDispatch, bool>? pointerDispatcher = null,
        Func<IntPtr, bool>? foregroundVerifier = null,
        Func<D045CodexWindowTarget, int, int,
            IReadOnlyList<D045CodexSemanticElement>>? semanticUiInspector = null,
        Func<D045CodexSemanticDispatch,
            D045CodexSemanticDispatchResult>? semanticUiDispatcher = null)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _hostResolver = hostResolver ?? Dns.GetHostAddresses;
        _httpsSender = httpsSender ?? SendPinnedPublicHttps;
        _clipboardWriter = clipboardWriter ?? WriteClipboardText;
        _browserNavigator = browserNavigator ?? NavigateBrowser;
        _visibleWindowResolver = visibleWindowResolver ?? ResolveVisibleWindowTargets;
        _pointerDispatcher = pointerDispatcher ?? DispatchPointerWindows;
        _foregroundVerifier = foregroundVerifier ?? IsForegroundWindow;
        _semanticUiInspector = semanticUiInspector ?? InspectSemanticUiWindows;
        _semanticUiDispatcher = semanticUiDispatcher ?? DispatchSemanticUiWindows;
    }

    internal D045CodexCapabilityReceipt Execute(
        D045CodexCapabilityRequest request,
        Func<bool>? dispatchAdmission = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        lock (_gate)
        {
            if (!_consumedRequests.Add(request.TaskId))
            {
                return Receipt(
                    request,
                    false,
                    false,
                    false,
                    -1,
                    "Request identity was already consumed.",
                    reason: D045CodexOutcomeReason.RequestAlreadyConsumed);
            }
        }

        D045PermissionLoadResult loaded;
        try
        {
            loaded = _permissions.Load();
        }
        catch (Exception exception)
        {
            return Receipt(
                request,
                false,
                false,
                false,
                -1,
                "Permission state was unavailable: " + exception.Message,
                reason: D045CodexOutcomeReason.PermissionDefaultOff);
        }

        var admitted = loaded.Snapshot;
        if (!admitted.IsEnabled(request.Capability))
        {
            return Receipt(
                request,
                false,
                false,
                false,
                -1,
                "Permission is Default Off.",
                permissionRevision: admitted.Revision,
                reason: D045CodexOutcomeReason.PermissionDefaultOff);
        }
        if (request.ExpectedPermissionRevision > 0
            && request.ExpectedPermissionRevision != admitted.Revision)
        {
            return Receipt(
                request,
                false,
                false,
                false,
                -1,
                "Permission revision changed before execution.",
                permissionRevision: admitted.Revision,
                reason: D045CodexOutcomeReason.PermissionChangedBeforeAttempt);
        }

        IReadOnlyList<D045PermissionCapability> requiredPermissions;
        try
        {
            requiredPermissions = RequiredPermissions(request);
        }
        catch (D045CodexRejectionException exception)
        {
            return Receipt(
                request,
                false,
                false,
                false,
                -1,
                exception.Message,
                permissionRevision: admitted.Revision,
                reason: exception.Reason);
        }
        var missing = requiredPermissions.Where(value => !admitted.IsEnabled(value)).ToArray();
        if (missing.Length != 0)
        {
            return Receipt(
                request,
                false,
                false,
                false,
                -1,
                "The operation requires additional destination permission: "
                    + string.Join(", ", missing.Select(value =>
                        D045CapabilityDescriptorCatalog.Get(value).ToolToken))
                    + ".",
                permissionRevision: admitted.Revision,
                reason: D045CodexOutcomeReason.PermissionCompositionMissing);
        }

        var scope = new ExecutionScope(
            this,
            request,
            admitted.Revision,
            requiredPermissions,
            dispatchAdmission);
        D045CodexCapabilityReceipt result;
        try
        {
            result = request.Capability switch
            {
                D045PermissionCapability.ClipboardWrite => WriteClipboard(request, scope),
                D045PermissionCapability.SystemDiagnosticsRead => ReadDiagnostics(request, scope),
                D045PermissionCapability.ApplicationInventoryRead => ReadApplicationInventory(request, scope),
                D045PermissionCapability.ProcessInventoryRead => ReadProcessInventory(request, scope),
                D045PermissionCapability.WindowInventoryRead => ReadWindowInventory(request, scope),
                D045PermissionCapability.ExternalAppSemanticUiInspect => InspectSemanticUi(request, scope),
                D045PermissionCapability.LocalPathSearch => SearchPaths(request, scope),
                D045PermissionCapability.LocalContentSearch => SearchContent(request, scope),
                D045PermissionCapability.FileModify => ModifyFile(request, scope),
                D045PermissionCapability.LocalContentCopy => CopyLocalContent(request, scope),
                D045PermissionCapability.LocalContentMoveRename => MoveLocalContent(request, scope),
                D045PermissionCapability.FolderTreeCreate => CreateFolderTree(request, scope),
                D045PermissionCapability.ArchiveManage => ManageArchive(request, scope),
                D045PermissionCapability.WindowControl => ControlWindow(request, scope),
                D045PermissionCapability.ExternalAppSemanticUiAction => DispatchSemanticUi(request, scope),
                D045PermissionCapability.ExternalAppPointerInput => DispatchPointer(request, scope),
                D045PermissionCapability.ApplicationLaunchWithArguments => LaunchWithArguments(request, scope),
                D045PermissionCapability.ProcessExecuteNonInteractive => ExecuteNonInteractive(request, scope),
                D045PermissionCapability.ProcessBackgroundManage => ManageBackgroundProcess(request, scope),
                D045PermissionCapability.WebPublicSearch => SearchPublicWeb(request, scope),
                D045PermissionCapability.BrowserNavigate => NavigatePublicBrowser(request, scope),
                D045PermissionCapability.DownloadPublicHttps => DownloadPublicHttps(request, scope),
                _ => throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.Unsupported,
                    "The capability has no C7 executor.")
            };
        }
        catch (D045CodexRejectionException exception)
        {
            result = Receipt(
                request,
                scope.Dispatched,
                false,
                scope.Dispatched,
                -1,
                exception.Message,
                permissionRevision: admitted.Revision,
                reason: exception.Reason);
        }
        catch (D045CodexTimeoutException exception)
        {
            result = Receipt(
                request,
                scope.Dispatched,
                false,
                scope.Dispatched,
                -1,
                exception.Message,
                permissionRevision: admitted.Revision,
                reason: D045CodexOutcomeReason.Timeout);
        }
        catch (D045CodexOutputLimitException exception)
        {
            result = Receipt(
                request,
                scope.Dispatched,
                false,
                scope.Dispatched,
                -1,
                exception.Message,
                permissionRevision: admitted.Revision,
                reason: D045CodexOutcomeReason.OutputLimitExceeded);
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            result = Receipt(
                request,
                scope.Dispatched,
                false,
                scope.Dispatched,
                -1,
                "Typed C7 operation failed: " + exception.Message,
                permissionRevision: admitted.Revision,
                reason: exception is UnauthorizedAccessException
                    ? D045CodexOutcomeReason.AccessDenied
                    : D045CodexOutcomeReason.ToolFailure);
        }

        var current = _permissions.Load().Snapshot;
        if (current.Revision != admitted.Revision
            || requiredPermissions.Any(value => !current.IsEnabled(value)))
        {
            return result.RejectAfterPermissionChange(current.Revision);
        }
        return result;
    }

    private IReadOnlyList<D045PermissionCapability> RequiredPermissions(
        D045CodexCapabilityRequest request)
    {
        var result = new List<D045PermissionCapability> { request.Capability };
        if (request.Capability == D045PermissionCapability.DownloadPublicHttps)
        {
            var destination = DemandAbsoluteLocalPath(request.SecondaryTarget);
            if (Directory.Exists(destination))
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.InvalidRequest,
                    "Download destination must be a file path.");
            }
            result.Add(File.Exists(destination)
                ? D045PermissionCapability.FileModify
                : D045PermissionCapability.LocalDataCreate);
        }
        return result.Distinct().ToArray();
    }

    private static bool IsOperationalException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException
            or System.ComponentModel.Win32Exception
            or HttpRequestException
            or CryptographicException;

    private D045CodexCapabilityReceipt Receipt(
        D045CodexCapabilityRequest request,
        bool attempted,
        bool verified,
        bool possiblyApplied,
        int exitCode,
        string message,
        string output = "",
        string contentSha256 = "",
        string jobId = "",
        long permissionRevision = 0,
        D045CodexOutcomeReason reason = D045CodexOutcomeReason.Unspecified,
        D045CodexPostcondition postcondition = D045CodexPostcondition.None)
    {
        if (permissionRevision <= 0)
            permissionRevision = _permissions.Load().Snapshot.Revision;
        return new(
            request,
            attempted,
            verified,
            possiblyApplied,
            exitCode,
            message,
            output,
            contentSha256,
            jobId,
            permissionRevision,
            reason,
            postcondition);
    }

    private sealed class ExecutionScope
    {
        private readonly D045CodexCapabilityHarness _owner;
        private readonly D045CodexCapabilityRequest _request;
        private readonly long _permissionRevision;
        private readonly IReadOnlyList<D045PermissionCapability> _required;
        private readonly Func<bool>? _externalAdmission;
        private readonly object _gate = new();
        private int _state;

        internal bool Dispatched
        {
            get
            {
                lock (_gate)
                    return _state > 0;
            }
        }

        internal long PermissionRevision => _permissionRevision;

        internal ExecutionScope(
            D045CodexCapabilityHarness owner,
            D045CodexCapabilityRequest request,
            long permissionRevision,
            IReadOnlyList<D045PermissionCapability> required,
            Func<bool>? externalAdmission)
        {
            _owner = owner;
            _request = request;
            _permissionRevision = permissionRevision;
            _required = required;
            _externalAdmission = externalAdmission;
        }

        internal void DemandDispatch()
        {
            lock (_gate)
            {
                if (_state > 0)
                    return;
                if (_state < 0)
                {
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.OperationCancelled,
                        "Effect dispatch was cancelled.");
                }
                var current = _owner._permissions.Load().Snapshot;
                if (current.Revision != _permissionRevision
                    || _required.Any(value => !current.IsEnabled(value)))
                {
                    _state = -1;
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.PermissionChangedBeforeAttempt,
                        "Permission changed before the exact effect dispatch.");
                }
                if (_request.ExpectedPermissionRevision > 0
                    && current.Revision != _request.ExpectedPermissionRevision)
                {
                    _state = -1;
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.PermissionChangedBeforeAttempt,
                        "The request permission revision is stale.");
                }
                if (_externalAdmission is not null && !_externalAdmission())
                {
                    _state = -1;
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.OperationCancelled,
                        "The operation was cancelled before effect dispatch.");
                }
                current = _owner._permissions.Load().Snapshot;
                if (current.Revision != _permissionRevision
                    || _required.Any(value => !current.IsEnabled(value)))
                {
                    _state = -1;
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.PermissionChangedBeforeAttempt,
                        "Permission changed during final host admission.");
                }
                _state = 1;
            }
        }
    }

    private sealed class D045CodexRejectionException : Exception
    {
        internal D045CodexOutcomeReason Reason { get; }

        internal D045CodexRejectionException(
            D045CodexOutcomeReason reason,
            string message)
            : base(message)
        {
            Reason = reason;
        }
    }

    private sealed class D045CodexTimeoutException : Exception
    {
        internal D045CodexTimeoutException(string message) : base(message) { }
    }

    private sealed class D045CodexOutputLimitException : Exception
    {
        internal D045CodexOutputLimitException(string message) : base(message) { }
    }
}

internal sealed partial class D045CodexCapabilityHarness
{
    private static readonly HashSet<string> SearchableTextExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".cs", ".csx", ".vb", ".fs", ".fsx",
            ".c", ".h", ".cc", ".cpp", ".hpp", ".java", ".kt", ".kts",
            ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".py", ".pyi",
            ".rb", ".php", ".go", ".rs", ".swift", ".scala", ".sh", ".ps1",
            ".bat", ".cmd", ".json", ".jsonl", ".xml", ".yaml", ".yml",
            ".toml", ".ini", ".cfg", ".conf", ".config", ".props", ".targets",
            ".sln", ".csproj", ".fsproj", ".vbproj", ".html", ".htm", ".css",
            ".scss", ".sass", ".less", ".sql", ".graphql", ".gql", ".csv",
            ".tsv", ".log", ".gitignore", ".gitattributes", ".editorconfig"
        };

    private D045CodexCapabilityReceipt WriteClipboard(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Target, nameof(request.Target));
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        if (request.Payload.Length == 0)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Clipboard write requires non-empty text.");
        }
        if (Encoding.UTF8.GetByteCount(request.Payload) > 1024 * 1024)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Clipboard text exceeds the 1 MiB ceiling.");
        }

        scope.DemandDispatch();
        var verified = _clipboardWriter(request.Payload);
        return Receipt(
            request,
            true,
            verified,
            !verified,
            verified ? 0 : -1,
            verified
                ? "Clipboard content was written and verified."
                : "The clipboard writer did not verify the exact content.",
            permissionRevision: request.ExpectedPermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.ClipboardContentMatches);
    }

    private D045CodexCapabilityReceipt ReadDiagnostics(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandReadOnlyRequest(request);
        scope.DemandDispatch();
        var drives = DriveInfo.GetDrives()
            .Where(value => value.IsReady)
            .Take(32)
            .Select(value => new
            {
                name = value.Name,
                type = value.DriveType.ToString(),
                format = SafeDriveFormat(value),
                totalBytes = SafeDriveSize(value, free: false),
                freeBytes = SafeDriveSize(value, free: true)
            })
            .ToArray();
        var output = JsonSerializer.Serialize(new
        {
            os = RuntimeInformation.OSDescription,
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            machineNameSha256 = Sha256(Environment.MachineName),
            processorCount = Environment.ProcessorCount,
            systemPageSize = Environment.SystemPageSize,
            is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            is64BitProcess = Environment.Is64BitProcess,
            drives
        });
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Bounded system diagnostics were read.",
            output,
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt ReadApplicationInventory(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandReadOnlyRequest(request);
        if (!OperatingSystem.IsWindows())
            return Unsupported(request, "Application inventory is available only on Windows.");
        scope.DemandDispatch();
        var applications = new Dictionary<string, ApplicationInventoryItem>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                ReadUninstallInventory(hive, view, applications);
        }
        var output = JsonSerializer.Serialize(applications.Values
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray());
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Bounded installed-application inventory was read.",
            output,
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt ReadProcessInventory(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandReadOnlyRequest(request);
        scope.DemandDispatch();
        var items = new List<object>();
        foreach (var process in Process.GetProcesses()
                     .OrderBy(value => value.Id)
                     .Take(512))
        {
            using (process)
            {
                try
                {
                    items.Add(new
                    {
                        pid = process.Id,
                        name = LimitText(process.ProcessName, 128),
                        hasExited = process.HasExited,
                        mainWindow = LimitText(process.MainWindowTitle, 256),
                        startedUtc = SafeProcessStartUtc(process)
                    });
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                        or System.ComponentModel.Win32Exception
                        or NotSupportedException)
                {
                    // The process changed or is protected. It is omitted rather
                    // than turning inaccessible details into guessed metadata.
                }
            }
        }
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Bounded process inventory was read.",
            JsonSerializer.Serialize(items),
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt ReadWindowInventory(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandReadOnlyRequest(request);
        if (!OperatingSystem.IsWindows())
            return Unsupported(request, "Window inventory is available only on Windows.");
        scope.DemandDispatch();
        var windows = EnumerateVisibleWindows(256)
            .Select(value => new
            {
                pid = value.ProcessId,
                title = value.Title,
                handleFingerprint = Sha256(value.Handle.ToInt64().ToString(
                    CultureInfo.InvariantCulture)),
                minimized = IsIconic(value.Handle),
                maximized = IsZoomed(value.Handle)
            })
            .ToArray();
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Bounded visible-window inventory was read.",
            JsonSerializer.Serialize(windows),
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt InspectSemanticUi(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandEmpty(request.ExpectedSha256, nameof(request.ExpectedSha256));
        DemandNoArguments(request);
        var maximumDepth = Math.Min(request.MaximumDepth, 6);
        var maximumResults = Math.Min(request.MaximumResults, 256);

        var admittedWindow = DemandExactVisibleWindow(
            request.Target,
            "Semantic UI inspection");
        var processId = admittedWindow.ProcessId;
        scope.DemandDispatch();
        var inspectionWindow = DemandExactVisibleWindow(
            request.Target,
            "Semantic UI inspection");
        DemandSameWindowIdentity(admittedWindow, inspectionWindow);
        var elements = InspectSemanticElements(
            inspectionWindow,
            maximumDepth,
            maximumResults);
        var observedWindow = DemandExactVisibleWindow(
            request.Target,
            "Semantic UI inspection");
        DemandSameWindowIdentity(inspectionWindow, observedWindow);

        // Deliberately serialize only the bounded semantic fields. Native
        // handles, class names, automation values, document text and selector
        // fingerprints never leave this host adapter.
        var output = JsonSerializer.Serialize(new
        {
            processId,
            bounded = true,
            maximumDepth,
            maximumResults,
            count = elements.Count,
            elements = elements.Select(value => new
            {
                role = value.Role,
                name = value.Name,
                enabled = value.Enabled,
                visible = value.Visible,
                bounds = new
                {
                    left = value.Left,
                    top = value.Top,
                    width = value.Width,
                    height = value.Height
                }
            }).ToArray()
        });
        return Receipt(
            request,
            attempted: true,
            verified: true,
            possiblyApplied: false,
            exitCode: 0,
            message: "A bounded semantic UI snapshot was read from the exact visible process window; password, value, and document text were not exposed.",
            output,
            permissionRevision: scope.PermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt DispatchSemanticUi(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandEmpty(request.ExpectedSha256, nameof(request.ExpectedSha256));
        if (request.Operation == D045CodexOperation.UiSetValue)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Semantic UI value or text input is prohibited; use the separately permissioned keyboard capability.");
        }
        if (request.Operation is not (D045CodexOperation.UiInvoke
            or D045CodexOperation.UiFocus
            or D045CodexOperation.UiToggle
            or D045CodexOperation.UiSelect
            or D045CodexOperation.UiScroll))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Semantic UI action accepts only invoke, focus, toggle, select, or scroll.");
        }
        if (request.SecondaryTarget.Length == 0
            || request.SecondaryTarget.Length > 64
            || string.Equals(
                request.Query,
                "[redacted]",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Semantic UI action requires one exact inspected role and an optional non-redacted exact name.");
        }
        var scrollDirection = DemandSemanticArguments(request);
        var maximumDepth = Math.Min(request.MaximumDepth, 6);
        var maximumResults = Math.Min(request.MaximumResults, 256);

        var admittedWindow = DemandExactVisibleWindow(
            request.Target,
            "Semantic UI action");
        var processId = admittedWindow.ProcessId;
        var admittedElement = DemandExactSemanticElement(
            admittedWindow,
            request.SecondaryTarget,
            request.Query,
            request.Operation,
            maximumDepth,
            maximumResults);

        // The permission revision and external call budget are checked here,
        // after semantic admission and immediately before resolving the exact
        // dispatch identity a second time.
        scope.DemandDispatch();
        var dispatchWindow = DemandExactVisibleWindow(
            request.Target,
            "Semantic UI action");
        DemandSameWindowIdentity(admittedWindow, dispatchWindow);
        var dispatchElement = DemandExactSemanticElement(
            dispatchWindow,
            request.SecondaryTarget,
            request.Query,
            request.Operation,
            maximumDepth,
            maximumResults);
        DemandSameSemanticIdentity(admittedElement, dispatchElement);

        var dispatch = new D045CodexSemanticDispatch(
            dispatchWindow,
            dispatchElement,
            request.Operation,
            scrollDirection,
            request.TimeoutMilliseconds);
        var dispatchResult = _semanticUiDispatcher(dispatch)
            ?? new D045CodexSemanticDispatchResult(false);
        var output = JsonSerializer.Serialize(new
        {
            processId,
            operation = request.Operation.ToString(),
            role = dispatchElement.Role,
            name = dispatchElement.Name,
            dispatchAccepted = dispatchResult.Accepted,
            semanticOutcomeObserved = dispatchResult.SemanticOutcomeObserved,
            observation = dispatchResult.Observation
        });
        return Receipt(
            request,
            attempted: true,
            verified: dispatchResult.Accepted,
            possiblyApplied: !dispatchResult.Accepted,
            exitCode: dispatchResult.Accepted ? 0 : -1,
            message: dispatchResult.Accepted
                ? dispatchResult.SemanticOutcomeObserved
                    ? "The typed semantic UI dispatch was accepted for the revalidated exact element and its bounded outcome was observed."
                    : "The typed semantic UI dispatch was accepted for the revalidated exact element; no semantic outcome beyond dispatch acceptance was inferred."
                : "The typed semantic UI dispatch was attempted for the revalidated exact element, but dispatch acceptance was not observed.",
            output,
            permissionRevision: scope.PermissionRevision,
            reason: dispatchResult.Accepted
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.DispatchAccepted);
    }

    private IReadOnlyList<D045CodexSemanticElement> InspectSemanticElements(
        D045CodexWindowTarget window,
        int maximumDepth,
        int maximumResults)
    {
        var elements = _semanticUiInspector(window, maximumDepth, maximumResults)
            ?? Array.Empty<D045CodexSemanticElement>();
        if (elements.Count > maximumResults)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.OutputLimitExceeded,
                "The semantic UI adapter exceeded the bounded element ceiling.");
        }
        foreach (var element in elements)
        {
            if (element is null
                || element.Depth > maximumDepth
                || !HasSameWindowIdentity(window, element.Window))
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.TargetAmbiguous,
                    "The semantic UI adapter returned an element outside the exact bounded window identity.");
            }
        }
        return elements;
    }

    private D045CodexSemanticElement DemandExactSemanticElement(
        D045CodexWindowTarget window,
        string role,
        string name,
        D045CodexOperation operation,
        int maximumDepth,
        int maximumResults)
    {
        var matches = InspectSemanticElements(window, maximumDepth, maximumResults)
            .Where(value => string.Equals(
                    value.Role,
                    role,
                    StringComparison.OrdinalIgnoreCase)
                && (name.Length == 0 || string.Equals(
                    value.Name,
                    name,
                    StringComparison.Ordinal)))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetNotFound,
                "No enabled visible semantic element matched the exact role and name.");
        }
        if (matches.Length != 1)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetAmbiguous,
                "The semantic element selector matched more than one element.");
        }
        var element = matches[0];
        if (!element.Visible || !element.Enabled)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.AccessDenied,
                "The exact semantic element is not both visible and enabled.");
        }
        if (!element.Supports(operation))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "The exact semantic element does not support that typed action.");
        }
        return element;
    }

    private static void DemandSameSemanticIdentity(
        D045CodexSemanticElement admitted,
        D045CodexSemanticElement current)
    {
        if (admitted.Handle != current.Handle
            || admitted.Depth != current.Depth
            || !string.Equals(admitted.Role, current.Role, StringComparison.Ordinal)
            || !string.Equals(admitted.Name, current.Name, StringComparison.Ordinal)
            || !HasSameWindowIdentity(admitted.Window, current.Window))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetAmbiguous,
                "The exact semantic element identity changed before dispatch.");
        }
    }

    private static string DemandSemanticArguments(
        D045CodexCapabilityRequest request)
    {
        if (request.Operation != D045CodexOperation.UiScroll)
        {
            DemandNoArguments(request);
            return string.Empty;
        }
        if (request.Arguments.Count != 1
            || request.Arguments[0] is not ("up" or "down" or "page_up" or "page_down"))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Semantic scroll requires exactly one direction: up, down, page_up, or page_down.");
        }
        return request.Arguments[0];
    }

    private static int DemandPositiveProcessId(string value, string operation)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var processId)
            || processId <= 0)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                operation + " requires one exact positive visible-window process id.");
        }
        return processId;
    }

    private D045CodexCapabilityReceipt DispatchPointer(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandEmpty(request.ExpectedSha256, nameof(request.ExpectedSha256));
        var coordinates = ParsePointerCoordinates(request, out var button);
        var admittedWindow = DemandExactVisibleWindow(
            request.Target,
            "Pointer input");
        var processId = admittedWindow.ProcessId;

        // DemandDispatch performs both the exact permission-revision check and
        // the host callback. Resolve once more afterwards, so a callback that
        // changed foreground application state cannot redirect the input.
        scope.DemandDispatch();
        var dispatchWindow = DemandExactVisibleWindow(
            request.Target,
            "Pointer input");
        DemandSameWindowIdentity(admittedWindow, dispatchWindow);

        var dispatch = new D045CodexPointerDispatch(
            dispatchWindow,
            request.Operation,
            button,
            coordinates.StartX,
            coordinates.StartY,
            coordinates.EndX,
            coordinates.EndY);
        var accepted = _pointerDispatcher(dispatch);

        // This verifies only the bounded OS dispatch and exact foreground
        // identity. It intentionally does not claim that a semantic UI outcome
        // (button invoked, selection changed, and so on) occurred.
        var observedWindow = DemandExactVisibleWindow(
            request.Target,
            "Pointer input");
        var identityStable = HasSameWindowIdentity(dispatchWindow, observedWindow);
        var foregroundObserved = _foregroundVerifier(dispatchWindow.Handle);
        var verified = accepted && identityStable && foregroundObserved;
        var output = JsonSerializer.Serialize(new
        {
            processId,
            operation = request.Operation.ToString(),
            button = button.ToString(),
            start = new
            {
                normalizedX = dispatch.StartNormalizedX,
                normalizedY = dispatch.StartNormalizedY,
                screenX = dispatch.StartScreenX,
                screenY = dispatch.StartScreenY
            },
            end = request.Operation == D045CodexOperation.PointerDrag
                ? new
                {
                    normalizedX = dispatch.EndNormalizedX,
                    normalizedY = dispatch.EndNormalizedY,
                    screenX = dispatch.EndScreenX,
                    screenY = dispatch.EndScreenY
                }
                : null,
            exactWindowIdentityObserved = identityStable,
            foregroundObserved,
            semanticOutcomeVerified = false
        });
        return Receipt(
            request,
            attempted: true,
            verified,
            possiblyApplied: !verified,
            exitCode: verified ? 0 : -1,
            message: verified
                ? "The bounded pointer dispatch and exact foreground window identity were observed; no semantic UI outcome was inferred."
                : "Pointer input may have been dispatched, but its exact foreground window identity was not fully observed; no semantic UI outcome was inferred.",
            output,
            permissionRevision: scope.PermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.DispatchAccepted);
    }

    private static PointerCoordinates ParsePointerCoordinates(
        D045CodexCapabilityRequest request,
        out D045CodexPointerButton button)
    {
        var requiredCount = request.Operation switch
        {
            D045CodexOperation.PointerMove => 2,
            D045CodexOperation.PointerClick => 3,
            D045CodexOperation.PointerDoubleClick => 3,
            D045CodexOperation.PointerDrag => 5,
            _ => throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Pointer input accepts only move, click, double-click, or drag.")
        };
        if (request.Arguments.Count != requiredCount)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                request.Operation == D045CodexOperation.PointerMove
                    ? "Pointer move requires exactly [normalized_x, normalized_y]."
                    : request.Operation == D045CodexOperation.PointerDrag
                        ? "Pointer drag requires exactly [start_x, start_y, end_x, end_y, button]."
                        : "Pointer click requires exactly [normalized_x, normalized_y, button].");
        }

        var startX = ParseNormalizedCoordinate(request.Arguments[0]);
        var startY = ParseNormalizedCoordinate(request.Arguments[1]);
        var endX = startX;
        var endY = startY;
        button = D045CodexPointerButton.None;
        if (request.Operation == D045CodexOperation.PointerDrag)
        {
            endX = ParseNormalizedCoordinate(request.Arguments[2]);
            endY = ParseNormalizedCoordinate(request.Arguments[3]);
            button = ParsePointerButton(request.Arguments[4]);
        }
        else if (request.Operation is D045CodexOperation.PointerClick
            or D045CodexOperation.PointerDoubleClick)
        {
            button = ParsePointerButton(request.Arguments[2]);
        }
        return new PointerCoordinates(startX, startY, endX, endY);
    }

    private static double ParseNormalizedCoordinate(string value)
    {
        if (!double.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var result)
            || !double.IsFinite(result)
            || result is < 0 or > 1)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Pointer coordinates must be invariant decimal values from 0 through 1.");
        }
        return result;
    }

    private static D045CodexPointerButton ParsePointerButton(string value)
        => value switch
        {
            "left" => D045CodexPointerButton.Left,
            "right" => D045CodexPointerButton.Right,
            "middle" => D045CodexPointerButton.Middle,
            _ => throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Pointer button must be exactly left, right, or middle.")
        };

    private D045CodexWindowTarget DemandExactVisibleWindow(
        string target,
        string operation)
    {
        var hasExactHandle = D045ExactWindowTarget.TryParse(
            target,
            out var processId,
            out var exactHandle);
        if (!hasExactHandle)
            processId = DemandPositiveProcessId(target, operation);
        var matches = _visibleWindowResolver(processId)
            .Where(value => value is not null
                && value.ProcessId == processId
                && (!hasExactHandle || value.Handle == exactHandle))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetNotFound,
                "No exact visible window matched the host-bound target.");
        }
        if (matches.Length != 1)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetAmbiguous,
                "More than one visible window matched the host-bound target.");
        }
        return matches[0];
    }

    private static void DemandSameWindowIdentity(
        D045CodexWindowTarget expected,
        D045CodexWindowTarget actual)
    {
        if (!HasSameWindowIdentity(expected, actual))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetAmbiguous,
                "The exact visible window identity changed before pointer dispatch.");
        }
    }

    private static bool HasSameWindowIdentity(
        D045CodexWindowTarget expected,
        D045CodexWindowTarget actual)
        => expected.Handle == actual.Handle
            && expected.ProcessId == actual.ProcessId;

    private sealed record PointerCoordinates(
        double StartX,
        double StartY,
        double EndX,
        double EndY);

    private D045CodexCapabilityReceipt SearchPaths(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var root = DemandExistingSafeDirectory(request.Target);
        var query = DemandSearchQuery(request.Query);
        scope.DemandDispatch();

        var results = new List<object>();
        var visited = 0;
        WalkTree(
            root,
            request.Recursive,
            request.MaximumDepth,
            (path, isDirectory, depth) =>
            {
                visited++;
                if (visited > MaximumSearchFiles)
                {
                    throw new D045CodexOutputLimitException(
                        "Path search exceeded the 4,096-item inspection ceiling.");
                }
                var leaf = Path.GetFileName(path);
                if (leaf.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    return results.Count < request.MaximumResults;
                results.Add(new
                {
                    relative = NormalizeRelative(root, path),
                    kind = isDirectory ? "directory" : "file",
                    extension = isDirectory
                        ? string.Empty
                        : Path.GetExtension(path).ToLowerInvariant(),
                    size = isDirectory ? 0 : new FileInfo(path).Length
                });
                return results.Count < request.MaximumResults;
            });
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Scoped path search completed.",
            JsonSerializer.Serialize(new
            {
                complete = true,
                inspected = visited,
                matches = results
            }),
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt SearchContent(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var root = DemandExistingSafeDirectory(request.Target);
        var query = DemandSearchQuery(request.Query);
        scope.DemandDispatch();

        var results = new List<object>();
        var inspected = 0;
        long totalBytes = 0;
        WalkTree(
            root,
            request.Recursive,
            request.MaximumDepth,
            (path, isDirectory, depth) =>
            {
                if (isDirectory || !SearchableTextExtensions.Contains(Path.GetExtension(path)))
                    return results.Count < request.MaximumResults;
                inspected++;
                if (inspected > MaximumSearchFiles)
                {
                    throw new D045CodexOutputLimitException(
                        "Content search exceeded the 4,096-file inspection ceiling.");
                }
                var file = new FileInfo(path);
                if (file.Length > Math.Min(request.MaximumBytes, 4L * 1024 * 1024))
                    return true;
                totalBytes = checked(totalBytes + file.Length);
                if (totalBytes > request.MaximumBytes)
                {
                    throw new D045CodexOutputLimitException(
                        "Content search exceeded its aggregate byte ceiling.");
                }
                var text = ReadUtf8OrBomText(path, checked((int)file.Length));
                var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return true;
                var start = Math.Max(0, index - 100);
                var length = Math.Min(text.Length - start, query.Length + 200);
                results.Add(new
                {
                    relative = NormalizeRelative(root, path),
                    index,
                    snippet = SanitizeSnippet(text.Substring(start, length))
                });
                return results.Count < request.MaximumResults;
            });
        return Receipt(
            request,
            true,
            true,
            false,
            0,
            "Scoped content search completed.",
            JsonSerializer.Serialize(new
            {
                complete = true,
                inspected,
                inspectedBytes = totalBytes,
                matches = results
            }),
            permissionRevision: request.ExpectedPermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private static string DemandSearchQuery(string value)
    {
        var query = value.Trim();
        if (query.Length is < 1 or > 512 || query.Any(char.IsControl))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "A bounded non-control search query is required.");
        }
        return query;
    }

    private static void WalkTree(
        string root,
        bool recursive,
        int maximumDepth,
        Func<string, bool, int, bool> visitor)
    {
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current.Directory)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.AccessDenied,
                    "A scoped search directory could not be read: " + exception.Message);
            }
            foreach (var entry in entries)
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.TargetUnsafe,
                        "Scoped search encountered a reparse-point item.");
                }
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (!visitor(entry, isDirectory, current.Depth + 1))
                    return;
                if (recursive && isDirectory && current.Depth < maximumDepth)
                    pending.Push((entry, current.Depth + 1));
            }
        }
    }

    private static string ReadUtf8OrBomText(string path, int length)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != length || bytes.Any(value => value == 0))
            return string.Empty;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            using var reader = new StreamReader(
                new MemoryStream(bytes, writable: false),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: false);
            return reader.ReadToEnd();
        }
    }

    private static string SanitizeSnippet(string value)
        => LimitText(value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '), 512);

    private static string SafeDriveFormat(DriveInfo drive)
    {
        try { return drive.DriveFormat; }
        catch { return string.Empty; }
    }

    private static long SafeDriveSize(DriveInfo drive, bool free)
    {
        try { return free ? drive.AvailableFreeSpace : drive.TotalSize; }
        catch { return -1; }
    }

    private static DateTimeOffset? SafeProcessStartUtc(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); }
        catch { return null; }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadUninstallInventory(
        RegistryHive hive,
        RegistryView view,
        IDictionary<string, ApplicationInventoryItem> result)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                writable: false);
            if (uninstall is null)
                return;
            foreach (var subkeyName in uninstall.GetSubKeyNames().Take(1024))
            {
                using var item = uninstall.OpenSubKey(subkeyName, writable: false);
                var name = (item?.GetValue("DisplayName") as string ?? string.Empty).Trim();
                if (name.Length == 0 || name.Length > 256)
                    continue;
                var version = LimitText(
                    item?.GetValue("DisplayVersion") as string ?? string.Empty,
                    128);
                var publisher = LimitText(
                    item?.GetValue("Publisher") as string ?? string.Empty,
                    128);
                var key = name + "\n" + version + "\n" + publisher;
                result.TryAdd(key, new ApplicationInventoryItem(name, version, publisher));
                if (result.Count >= 512)
                    return;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
                or IOException
                or System.Security.SecurityException)
        {
            // One inaccessible registry view does not invalidate the bounded
            // inventory collected from other standard uninstall views.
        }
    }

    private sealed record ApplicationInventoryItem(
        string Name,
        string Version,
        string Publisher);
}

internal sealed partial class D045CodexCapabilityHarness
{
    private D045CodexCapabilityReceipt ModifyFile(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        if (request.Operation is not (D045CodexOperation.FileOverwrite
                or D045CodexOperation.FileAppend))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "File Modify requires FileOverwrite or FileAppend.");
        }
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var target = DemandExistingSafeFile(request.Target);
        var before = File.ReadAllBytes(target);
        var beforeSha256 = Sha256(before);
        if (request.ExpectedSha256.Length != 0
            && !string.Equals(
                request.ExpectedSha256,
                beforeSha256,
                StringComparison.Ordinal))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.IntegrityMismatch,
                "The existing file SHA-256 did not match the trusted precondition.");
        }
        var payload = new UTF8Encoding(false, true).GetBytes(request.Payload);
        byte[] next;
        if (request.Operation == D045CodexOperation.FileAppend)
        {
            if ((long)before.Length + payload.Length > request.MaximumBytes)
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.InvalidRequest,
                    "Append result exceeds the request byte ceiling.");
            }
            next = new byte[before.Length + payload.Length];
            Buffer.BlockCopy(before, 0, next, 0, before.Length);
            Buffer.BlockCopy(payload, 0, next, before.Length, payload.Length);
        }
        else
        {
            if (payload.LongLength > request.MaximumBytes)
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.InvalidRequest,
                    "Replacement content exceeds the request byte ceiling.");
            }
            next = payload;
        }

        var temporary = TemporarySibling(target);
        scope.DemandDispatch();
        try
        {
            WriteThroughNewFile(temporary, next);
            if (!BytesEqual(next, File.ReadAllBytes(temporary)))
                throw new InvalidDataException("Staged file verification failed.");
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            SafeRemoveStagingPath(temporary);
            CryptographicOperations.ZeroMemory(before);
            if (!ReferenceEquals(next, payload))
                CryptographicOperations.ZeroMemory(payload);
        }
        var actual = File.ReadAllBytes(target);
        var expectedHash = Sha256(next);
        var verified = BytesEqual(next, actual);
        CryptographicOperations.ZeroMemory(actual);
        CryptographicOperations.ZeroMemory(next);
        return Receipt(
            request,
            true,
            verified,
            !verified,
            verified ? 0 : -1,
            verified
                ? "Atomic file replacement and content digest were verified."
                : "The final file content did not match the staged content.",
            JsonSerializer.Serialize(new { byteLength = payload.Length, sha256 = expectedHash }),
            expectedHash,
            permissionRevision: request.ExpectedPermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.IntegrityMismatch,
            postcondition: D045CodexPostcondition.TargetContentMatches);
    }

    private D045CodexCapabilityReceipt CopyLocalContent(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var source = DemandExistingSafePath(request.Target, out var sourceDirectory);
        var destination = DemandNewSafeTarget(request.SecondaryTarget);
        if (sourceDirectory && IsDescendantOrSame(source, destination))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "A directory cannot be copied into its own subtree.");
        }

        var staging = TemporarySibling(destination);
        scope.DemandDispatch();
        string expectedDigest;
        try
        {
            if (sourceDirectory)
            {
                expectedDigest = CopyDirectoryTreeToStaging(
                    source,
                    staging,
                    request.MaximumDepth,
                    request.MaximumBytes);
                Directory.Move(staging, destination);
            }
            else
            {
                File.Copy(source, staging, overwrite: false);
                expectedDigest = Sha256File(staging, request.MaximumBytes);
                File.Move(staging, destination);
            }
        }
        finally
        {
            SafeRemoveStagingPath(staging);
        }
        var verified = sourceDirectory
            ? Directory.Exists(destination)
                && string.Equals(
                    expectedDigest,
                    ComputeTreeDigest(destination, request.MaximumDepth, request.MaximumBytes),
                    StringComparison.Ordinal)
            : File.Exists(destination)
                && string.Equals(
                    expectedDigest,
                    Sha256File(destination, request.MaximumBytes),
                    StringComparison.Ordinal);
        return Receipt(
            request,
            true,
            verified,
            !verified,
            verified ? 0 : -1,
            verified
                ? "Copy destination content digest was verified."
                : "Copy destination verification failed.",
            JsonSerializer.Serialize(new
            {
                kind = sourceDirectory ? "directory" : "file",
                sha256 = expectedDigest
            }),
            expectedDigest,
            permissionRevision: request.ExpectedPermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.IntegrityMismatch,
            postcondition: D045CodexPostcondition.TargetContentMatches);
    }

    private D045CodexCapabilityReceipt MoveLocalContent(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var source = DemandExistingSafePath(request.Target, out var isDirectory);
        var destination = DemandNewSafeTarget(request.SecondaryTarget);
        if (isDirectory && IsDescendantOrSame(source, destination))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "A directory cannot be moved into its own subtree.");
        }
        var sourceRoot = Path.GetPathRoot(source);
        var destinationRoot = Path.GetPathRoot(destination);
        if (!string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.Unsupported,
                "Cross-volume move is not performed because it is not one atomic move.");
        }

        scope.DemandDispatch();
        if (isDirectory)
            Directory.Move(source, destination);
        else
            File.Move(source, destination);
        var verified = !File.Exists(source)
            && !Directory.Exists(source)
            && (isDirectory ? Directory.Exists(destination) : File.Exists(destination));
        return Receipt(
            request,
            true,
            verified,
            !verified,
            verified ? 0 : -1,
            verified
                ? "Atomic move/rename source absence and destination presence were verified."
                : "Move/rename postcondition could not be verified.",
            permissionRevision: request.ExpectedPermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.SourceAbsentAndTargetExists);
    }

    private D045CodexCapabilityReceipt CreateFolderTree(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var target = DemandNewSafeTarget(request.Target, requireExistingParent: false);
        DemandSafeExistingAncestor(target);
        scope.DemandDispatch();
        Directory.CreateDirectory(target);
        var verified = Directory.Exists(target) && !File.Exists(target);
        return Receipt(
            request,
            true,
            verified,
            !verified,
            verified ? 0 : -1,
            verified
                ? "The exact folder tree exists."
                : "The folder tree could not be verified.",
            permissionRevision: request.ExpectedPermissionRevision,
            reason: verified
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.TargetExists);
    }

    private D045CodexCapabilityReceipt ManageArchive(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
        => request.Operation switch
        {
            D045CodexOperation.ArchiveCreate => CreateArchive(request, scope),
            D045CodexOperation.ArchiveExtract => ExtractArchive(request, scope),
            _ => throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Archive Manage requires ArchiveCreate or ArchiveExtract.")
        };

    private D045CodexCapabilityReceipt CreateArchive(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var source = DemandExistingSafePath(request.Target, out var isDirectory);
        var destination = DemandNewSafeTarget(request.SecondaryTarget);
        if (!string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Archive creation currently supports an exact new .zip target only.");
        }
        if (isDirectory && IsDescendantOrSame(source, destination))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "An archive destination cannot be inside the source tree.");
        }
        var staging = TemporarySibling(destination);
        scope.DemandDispatch();
        try
        {
            using (var stream = new FileStream(
                       staging,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                if (isDirectory)
                    AddDirectoryToArchive(source, archive, request.MaximumDepth, request.MaximumBytes);
                else
                    AddFileToArchive(source, Path.GetFileName(source), archive, request.MaximumBytes);
            }
            _ = ValidateArchive(staging, request.MaximumBytes);
            File.Move(staging, destination);
        }
        finally
        {
            SafeRemoveStagingPath(staging);
        }
        var validation = ValidateArchive(destination, request.MaximumBytes);
        var sha256 = Sha256File(destination, request.MaximumBytes);
        return Receipt(
            request,
            true,
            validation.EntryCount > 0,
            validation.EntryCount == 0,
            validation.EntryCount > 0 ? 0 : -1,
            validation.EntryCount > 0
                ? "ZIP archive structure and expansion bounds were verified."
                : "The ZIP archive contained no verified entries.",
            JsonSerializer.Serialize(validation),
            sha256,
            permissionRevision: request.ExpectedPermissionRevision,
            reason: validation.EntryCount > 0
                ? D045CodexOutcomeReason.EffectVerified
                : D045CodexOutcomeReason.IntegrityMismatch,
            postcondition: D045CodexPostcondition.ArchiveValidated);
    }

    private D045CodexCapabilityReceipt ExtractArchive(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var source = DemandExistingSafeFile(request.Target);
        if (!string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Archive extraction currently supports .zip only.");
        }
        var destination = DemandNewSafeTarget(request.SecondaryTarget);
        var validation = ValidateArchive(source, request.MaximumBytes);
        var staging = TemporarySibling(destination);
        scope.DemandDispatch();
        try
        {
            Directory.CreateDirectory(staging);
            ExtractZipToStaging(source, staging, request.MaximumBytes);
            var extractedDigest = ComputeTreeDigest(
                staging,
                request.MaximumDepth,
                request.MaximumBytes);
            Directory.Move(staging, destination);
            var verified = Directory.Exists(destination)
                && string.Equals(
                    extractedDigest,
                    ComputeTreeDigest(destination, request.MaximumDepth, request.MaximumBytes),
                    StringComparison.Ordinal);
            return Receipt(
                request,
                true,
                verified,
                !verified,
                verified ? 0 : -1,
                verified
                    ? "ZIP extraction remained inside the new target and its tree digest was verified."
                    : "Extracted tree digest verification failed.",
                JsonSerializer.Serialize(validation),
                extractedDigest,
                permissionRevision: request.ExpectedPermissionRevision,
                reason: verified
                    ? D045CodexOutcomeReason.EffectVerified
                    : D045CodexOutcomeReason.IntegrityMismatch,
                postcondition: D045CodexPostcondition.ArchiveValidated);
        }
        finally
        {
            SafeRemoveStagingPath(staging);
        }
    }

    private static void AddDirectoryToArchive(
        string root,
        ZipArchive archive,
        int maximumDepth,
        long maximumBytes)
    {
        var files = new List<string>();
        var emptyDirectories = new List<string>();
        WalkTree(
            root,
            recursive: true,
            maximumDepth,
            (path, isDirectory, depth) =>
            {
                if (isDirectory)
                {
                    if (!Directory.EnumerateFileSystemEntries(path).Any())
                        emptyDirectories.Add(NormalizeRelative(root, path) + "/");
                }
                else
                {
                    files.Add(path);
                }
                if (files.Count + emptyDirectories.Count > MaximumArchiveEntries)
                    throw new D045CodexOutputLimitException("Archive entry ceiling exceeded.");
                return true;
            });
        long total = 0;
        foreach (var directory in emptyDirectories)
            archive.CreateEntry(directory, CompressionLevel.NoCompression);
        foreach (var file in files.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var length = new FileInfo(file).Length;
            total = checked(total + length);
            if (length > MaximumArchiveEntryBytes || total > maximumBytes)
                throw new D045CodexOutputLimitException("Archive input byte ceiling exceeded.");
            AddFileToArchive(file, NormalizeRelative(root, file), archive, maximumBytes);
        }
    }

    private static void AddFileToArchive(
        string file,
        string entryName,
        ZipArchive archive,
        long maximumBytes)
    {
        var length = new FileInfo(file).Length;
        if (length > Math.Min(MaximumArchiveEntryBytes, maximumBytes))
            throw new D045CodexOutputLimitException("Archive entry byte ceiling exceeded.");
        var entry = archive.CreateEntry(
            entryName.Replace(Path.DirectorySeparatorChar, '/'),
            CompressionLevel.Optimal);
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = entry.Open();
        CopyStreamBounded(input, output, length);
    }

    private static ArchiveValidation ValidateArchive(string path, long requestMaximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaximumArchiveEntries)
            throw new D045CodexOutputLimitException("Archive entry ceiling exceeded.");
        long expanded = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var safeName = DemandSafeArchiveEntry(entry);
            if (!destinations.Add(safeName.TrimEnd('/')))
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.TargetAmbiguous,
                    "Archive contains colliding entry destinations.");
            }
            if (entry.Length > MaximumArchiveEntryBytes)
                throw new D045CodexOutputLimitException("Archive entry expansion ceiling exceeded.");
            expanded = checked(expanded + entry.Length);
            if (expanded > Math.Min(MaximumArchiveExpandedBytes, requestMaximum))
                throw new D045CodexOutputLimitException("Archive expansion ceiling exceeded.");
            if (entry.Length > 0)
            {
                if (entry.CompressedLength <= 0
                    || entry.Length / Math.Max(1, entry.CompressedLength)
                        > MaximumArchiveCompressionRatio)
                {
                    throw new D045CodexRejectionException(
                        D045CodexOutcomeReason.TargetUnsafe,
                        "Archive compression ratio exceeds the expansion-bomb limit.");
                }
            }
        }
        return new ArchiveValidation(archive.Entries.Count, expanded);
    }

    private static void ExtractZipToStaging(
        string archivePath,
        string staging,
        long requestMaximum)
    {
        using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var safeName = DemandSafeArchiveEntry(entry);
            var destination = Path.GetFullPath(Path.Combine(
                staging,
                safeName.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsDescendantOrSame(staging, destination))
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.TargetUnsafe,
                    "Archive entry escaped the extraction root.");
            }
            if (safeName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            total = checked(total + entry.Length);
            if (entry.Length > MaximumArchiveEntryBytes
                || total > Math.Min(MaximumArchiveExpandedBytes, requestMaximum))
            {
                throw new D045CodexOutputLimitException("Archive extraction byte ceiling exceeded.");
            }
            var parent = Path.GetDirectoryName(destination)
                ?? throw new InvalidDataException("Archive destination parent is missing.");
            Directory.CreateDirectory(parent);
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new D045CodexRejectionException(
                    D045CodexOutcomeReason.TargetAlreadyExists,
                    "Archive contains a duplicate or conflicting destination.");
            }
            using var input = entry.Open();
            using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough);
            CopyStreamBounded(input, output, entry.Length);
            output.Flush(flushToDisk: true);
            if (new FileInfo(destination).Length != entry.Length)
                throw new InvalidDataException("Extracted entry length did not match.");
        }
    }

    private static string DemandSafeArchiveEntry(ZipArchiveEntry entry)
    {
        if (entry.FullName.Length is < 1 or > 4_096
            || IsArchiveLink(entry))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "Archive contains an invalid or linked entry.");
        }
        var normalized = entry.FullName.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.IndexOf(':') >= 0)
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "Archive contains an absolute entry.");
        }
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(value => value is "." or ".."
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.TargetUnsafe,
                "Archive contains a traversal or invalid entry.");
        }
        return string.Join("/", segments)
            + (normalized.EndsWith("/", StringComparison.Ordinal) ? "/" : string.Empty);
    }

    private static bool IsArchiveLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        return unixMode == 0xA000
            || (windowsAttributes & FileAttributes.ReparsePoint) != 0;
    }

    private sealed record ArchiveValidation(int EntryCount, long ExpandedBytes);
}

internal sealed partial class D045CodexCapabilityHarness
{
    private static readonly HashSet<string> BlockedCommandHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cmd", "cmd.exe", "command.com", "powershell", "powershell.exe",
            "pwsh", "pwsh.exe", "wscript", "wscript.exe", "cscript",
            "cscript.exe", "mshta", "mshta.exe", "rundll32", "rundll32.exe"
        };

    private D045CodexCapabilityReceipt ControlWindow(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        if (!OperatingSystem.IsWindows())
            return Unsupported(request, "Window control is available only on Windows.");
        if (request.Operation is not (D045CodexOperation.WindowFocus
                or D045CodexOperation.WindowMinimize
                or D045CodexOperation.WindowMaximize
                or D045CodexOperation.WindowRestore))
        {
            throw new D045CodexRejectionException(
                D045CodexOutcomeReason.InvalidRequest,
                "Window Control requires an exact focus/minimize/maximize/restore operation.");
        }
        var admittedWindow = DemandExactVisibleWindow(
            request.Target,
            "Window Control");
        var handle = admittedWindow.Handle;
        scope.DemandDispatch();
        var dispatchWindow = DemandExactVisibleWindow(
            request.Target,
            "Window Control");
        DemandSameWindowIdentity(admittedWindow, dispatchWindow);
        handle = dispatchWindow.Handle;
        var accepted = request.Operation switch
        {
            D045CodexOperation.WindowFocus => SetForegroundWindow(handle),
            D045CodexOperation.WindowMinimize => ShowWindow(handle, ShowWindowMinimize),
            D045CodexOperation.WindowMaximize => ShowWindow(handle, ShowWindowMaximize),
            D045CodexOperation.WindowRestore => ShowWindow(handle, ShowWindowRestore),
            _ => false
        };
        Thread.Sleep(100);
        var verified = accepted && request.Operation switch
        {
            D045CodexOperation.WindowFocus => GetForegroundWindow() == handle,
            D045CodexOperation.WindowMinimize => IsIconic(handle),
            D045CodexOperation.WindowMaximize => IsZoomed(handle),
            D045CodexOperation.WindowRestore => !IsIconic(handle),
            _ => false
        };
        return Receipt(
            request, true, verified, !verified, verified ? 0 : -1,
            verified ? "The exact window state was verified." : "The requested window state was not verified.",
            permissionRevision: scope.PermissionRevision,
            reason: verified ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.WindowStateMatches);
    }

    private D045CodexCapabilityReceipt LaunchWithArguments(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        var executable = DemandExactExecutable(request.Target);
        var working = DemandWorkingDirectory(request.WorkingDirectory, executable);
        using var process = new Process { StartInfo = CreateStartInfo(executable, working, request.Arguments, redirect: false) };
        scope.DemandDispatch();
        var started = process.Start();
        if (!started)
            throw new InvalidOperationException("The exact application did not start.");
        Thread.Sleep(100);
        var verified = !process.HasExited || process.ExitCode == 0;
        return Receipt(
            request, true, verified, !verified, verified ? 0 : process.ExitCode,
            verified ? "The exact application launch was observed." : "The application exited with a failure before launch could be verified.",
            JsonSerializer.Serialize(new { processId = process.Id, running = !process.HasExited }),
            permissionRevision: scope.PermissionRevision,
            reason: verified ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.ProcessRunning);
    }

    private D045CodexCapabilityReceipt ExecuteNonInteractive(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        var executable = DemandExactExecutable(request.Target);
        var working = DemandWorkingDirectory(request.WorkingDirectory, executable);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, working, request.Arguments, redirect: true),
            EnableRaisingEvents = true
        };
        scope.DemandDispatch();
        if (!process.Start())
            throw new InvalidOperationException("The exact process did not start.");
        using var cancellation = new CancellationTokenSource(request.TimeoutMilliseconds);
        var stdout = ReadBoundedTextAsync(process.StandardOutput, MaximumOutputCharacters / 2, cancellation.Token);
        var stderr = ReadBoundedTextAsync(process.StandardError, MaximumOutputCharacters / 2, cancellation.Token);
        try
        {
            process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            throw new D045CodexTimeoutException("The exact process exceeded its execution timeout and was stopped.");
        }
        catch (D045CodexOutputLimitException)
        {
            TryKillProcessTree(process);
            throw;
        }
        var output = JoinProcessOutput(stdout.Result, stderr.Result);
        var verified = process.ExitCode == 0;
        return Receipt(
            request, true, verified, !verified, process.ExitCode,
            verified ? "The exact noninteractive process exited with code zero." : "The exact process returned a nonzero exit code.",
            output,
            permissionRevision: scope.PermissionRevision,
            reason: verified ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.ProcessExitCodeZero);
    }

    private D045CodexCapabilityReceipt ManageBackgroundProcess(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
        => request.Operation switch
        {
            D045CodexOperation.BackgroundStart => StartBackgroundProcess(request, scope),
            D045CodexOperation.BackgroundStatus => ReadBackgroundProcess(request, scope),
            D045CodexOperation.BackgroundStop => StopBackgroundProcess(request, scope),
            _ => throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "Background Manage requires start, status, or stop.")
        };

    private D045CodexCapabilityReceipt StartBackgroundProcess(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        if (request.OwnerId.Length == 0)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "Background start requires a bounded owner id.");
        if (_backgroundJobs.Count >= 32)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.OutputLimitExceeded, "Background job capacity is full.");
        var executable = DemandExactExecutable(request.Target);
        var working = DemandWorkingDirectory(request.WorkingDirectory, executable);
        var process = new Process
        {
            StartInfo = CreateStartInfo(executable, working, request.Arguments, redirect: true),
            EnableRaisingEvents = true
        };
        var jobId = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        scope.DemandDispatch();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("The background process did not start.");
            var job = new BackgroundJob(jobId, request.OwnerId, process);
            if (!_backgroundJobs.TryAdd(jobId, job))
                throw new InvalidOperationException("The background job id collided.");
            job.StartPumps();
            return Receipt(
                request, true, true, false, 0,
                "The owned background job started.",
                JsonSerializer.Serialize(new { jobId, processId = process.Id }),
                jobId: jobId,
                permissionRevision: scope.PermissionRevision,
                postcondition: D045CodexPostcondition.ProcessRunning);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private D045CodexCapabilityReceipt ReadBackgroundProcess(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandBackgroundLookupShape(request);
        var job = DemandOwnedJob(request.Target, request.OwnerId);
        scope.DemandDispatch();
        var status = job.Snapshot();
        return Receipt(
            request, true, true, false, status.ExitCode ?? 0,
            status.Running ? "The owned background job is running." : "The owned background job has exited.",
            JsonSerializer.Serialize(status),
            jobId: job.JobId,
            permissionRevision: scope.PermissionRevision,
            postcondition: status.Running ? D045CodexPostcondition.ProcessRunning : D045CodexPostcondition.ProcessExited);
    }

    private D045CodexCapabilityReceipt StopBackgroundProcess(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandBackgroundLookupShape(request);
        var job = DemandOwnedJob(request.Target, request.OwnerId);
        scope.DemandDispatch();
        job.Stop(request.TimeoutMilliseconds);
        var verified = !job.IsRunning;
        _backgroundJobs.TryRemove(job.JobId, out _);
        var status = job.Snapshot();
        job.Dispose();
        return Receipt(
            request, true, verified, !verified, status.ExitCode ?? 0,
            verified ? "The owned background process tree is stopped." : "The owned background process did not verify as stopped.",
            JsonSerializer.Serialize(status),
            jobId: job.JobId,
            permissionRevision: scope.PermissionRevision,
            reason: verified ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.ProcessExited);
    }

    private D045CodexCapabilityReceipt SearchPublicWeb(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Target, nameof(request.Target));
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var query = DemandSearchQuery(request.Query);
        var uri = new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(query), UriKind.Absolute);
        var pinned = DemandPublicHttpsTarget(uri);
        scope.DemandDispatch();
        using var cancellation = new CancellationTokenSource(request.TimeoutMilliseconds);
        using var response = _httpsSender(uri, pinned, request.TimeoutMilliseconds, cancellation.Token);
        DemandSuccessfulHttpsResponse(response, request.MaximumBytes);
        var bytes = ReadStreamBounded(response.Body, Math.Min(request.MaximumBytes, 1024 * 1024));
        var output = DecodePublicText(bytes);
        var digest = Sha256(bytes);
        CryptographicOperations.ZeroMemory(bytes);
        return Receipt(
            request, true, true, false, 0,
            "A bounded public HTTPS search response was read.",
            output,
            digest,
            permissionRevision: scope.PermissionRevision,
            postcondition: D045CodexPostcondition.BoundedReadComplete);
    }

    private D045CodexCapabilityReceipt NavigatePublicBrowser(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var uri = DemandHttpsUri(request.Target);
        _ = DemandPublicHttpsTarget(uri);
        scope.DemandDispatch();
        var accepted = _browserNavigator(uri);
        return Receipt(
            request, true, accepted, !accepted, accepted ? 0 : -1,
            accepted ? "The public HTTPS URL was accepted by the browser." : "The browser did not accept the navigation.",
            permissionRevision: scope.PermissionRevision,
            reason: accepted ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.ToolFailure,
            postcondition: D045CodexPostcondition.DispatchAccepted);
    }

    private D045CodexCapabilityReceipt DownloadPublicHttps(
        D045CodexCapabilityRequest request,
        ExecutionScope scope)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        var uri = DemandHttpsUri(request.Target);
        var pinned = DemandPublicHttpsTarget(uri);
        var destination = DemandAbsoluteLocalPath(request.SecondaryTarget);
        if (Directory.Exists(destination))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "Download destination must be a file path.");
        if (File.Exists(destination))
            _ = DemandExistingSafeFile(destination);
        else
            _ = DemandNewSafeTarget(destination);
        var staging = TemporarySibling(destination);
        scope.DemandDispatch();
        string digest;
        try
        {
            using var cancellation = new CancellationTokenSource(request.TimeoutMilliseconds);
            using var response = _httpsSender(uri, pinned, request.TimeoutMilliseconds, cancellation.Token);
            DemandSuccessfulHttpsResponse(response, request.MaximumBytes);
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                CopyStreamBounded(response.Body, output, request.MaximumBytes, requireExactLength: response.ContentLength);
                output.Flush(flushToDisk: true);
            }
            digest = Sha256File(staging, request.MaximumBytes);
            if (request.ExpectedSha256.Length != 0
                && !string.Equals(request.ExpectedSha256, digest, StringComparison.Ordinal))
            {
                throw new D045CodexRejectionException(D045CodexOutcomeReason.IntegrityMismatch, "Downloaded content SHA-256 did not match the request.");
            }
            File.Move(staging, destination, overwrite: File.Exists(destination));
        }
        finally
        {
            SafeRemoveStagingPath(staging);
        }
        var verified = File.Exists(destination)
            && string.Equals(digest, Sha256File(destination, request.MaximumBytes), StringComparison.Ordinal);
        return Receipt(
            request, true, verified, !verified, verified ? 0 : -1,
            verified ? "The bounded public HTTPS download and destination digest were verified." : "The downloaded destination digest did not verify.",
            JsonSerializer.Serialize(new { uri = uri.GetLeftPart(UriPartial.Path), sha256 = digest, bytes = new FileInfo(destination).Length }),
            digest,
            permissionRevision: scope.PermissionRevision,
            reason: verified ? D045CodexOutcomeReason.EffectVerified : D045CodexOutcomeReason.IntegrityMismatch,
            postcondition: D045CodexPostcondition.HttpsBodyDigestMatches);
    }
}

internal sealed partial class D045CodexCapabilityHarness
{
    private static void DemandOnlyDefaultOperation(D045CodexCapabilityRequest request)
    {
        if (request.Operation != D045CodexOperation.Default)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "This capability accepts only its default typed operation.");
    }

    private static void DemandReadOnlyRequest(D045CodexCapabilityRequest request)
    {
        DemandOnlyDefaultOperation(request);
        DemandEmpty(request.Target, nameof(request.Target));
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
    }

    private static void DemandEmpty(string value, string name)
    {
        if (value.Length != 0)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, name + " is not valid for this typed operation.");
    }

    private static void DemandNoArguments(D045CodexCapabilityRequest request)
    {
        if (request.Arguments.Count != 0)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "Argument arrays are not valid for this typed operation.");
    }

    private D045CodexCapabilityReceipt Unsupported(
        D045CodexCapabilityRequest request,
        string message)
        => Receipt(
            request, false, false, false, -1, message,
            permissionRevision: request.ExpectedPermissionRevision,
            reason: D045CodexOutcomeReason.Unsupported);

    private static string DemandAbsoluteLocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "An absolute local path is required.");
        var path = Path.GetFullPath(value);
        if (path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "Network and UNC paths are outside local-path capabilities.");
        }
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "A filesystem root cannot be the exact target.");
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var drive = new DriveInfo(root);
                if (drive.DriveType == DriveType.Network)
                    throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "Mapped network paths are outside local-path capabilities.");
            }
            catch (ArgumentException) { }
        }
        return path;
    }

    private static string DemandExistingSafeFile(string value)
    {
        var path = DemandAbsoluteLocalPath(value);
        if (!File.Exists(path))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The exact file target does not exist.");
        DemandNoReparse(path);
        DemandSafeExistingAncestor(path);
        return path;
    }

    private static string DemandExistingSafeDirectory(string value)
    {
        var path = DemandAbsoluteLocalPath(value);
        if (!Directory.Exists(path))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The exact directory target does not exist.");
        DemandNoReparse(path);
        DemandSafeExistingAncestor(path);
        return path;
    }

    private static string DemandExistingSafePath(string value, out bool isDirectory)
    {
        var path = DemandAbsoluteLocalPath(value);
        if (File.Exists(path))
        {
            isDirectory = false;
            DemandNoReparse(path);
            DemandSafeExistingAncestor(path);
            return path;
        }
        if (Directory.Exists(path))
        {
            isDirectory = true;
            DemandNoReparse(path);
            DemandSafeExistingAncestor(path);
            return path;
        }
        throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The exact source target does not exist.");
    }

    private static string DemandNewSafeTarget(string value, bool requireExistingParent = true)
    {
        var path = DemandAbsoluteLocalPath(value);
        if (File.Exists(path) || Directory.Exists(path))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetAlreadyExists, "The exact destination already exists.");
        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "The destination has no local parent.");
        if (requireExistingParent && !Directory.Exists(parent))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The exact destination parent does not exist.");
        DemandSafeExistingAncestor(path);
        return path;
    }

    private static void DemandSafeExistingAncestor(string path)
    {
        var current = File.Exists(path) || Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
                DemandNoReparse(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }

    private static void DemandNoReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "Reparse points and symbolic links are not followed.");
    }

    private static bool IsDescendantOrSame(string root, string candidate)
    {
        var left = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var right = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string TemporarySibling(string target)
    {
        var parent = Path.GetDirectoryName(target)
            ?? throw new InvalidDataException("The destination parent is missing.");
        return Path.Combine(parent, ".d045-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
    }

    private static void SafeRemoveStagingPath(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void WriteThroughNewFile(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough);
        stream.Write(content, 0, content.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool BytesEqual(byte[] left, byte[] right)
        => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    private static string Sha256(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value));

    private static string Sha256(string value)
        => Sha256(Encoding.UTF8.GetBytes(value));

    private static string Sha256File(string path, long maximumBytes)
    {
        var length = new FileInfo(path).Length;
        if (length > maximumBytes)
            throw new D045CodexOutputLimitException("File exceeds the operation byte ceiling.");
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream));
    }

    private static string CopyDirectoryTreeToStaging(string source, string staging, int maximumDepth, long maximumBytes)
    {
        Directory.CreateDirectory(staging);
        long total = 0;
        var count = 0;
        WalkTree(source, recursive: true, maximumDepth, (path, isDirectory, depth) =>
        {
            if (++count > MaximumSearchFiles)
                throw new D045CodexOutputLimitException("Directory copy item ceiling exceeded.");
            var relative = NormalizeRelative(source, path);
            var destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
            if (isDirectory)
                Directory.CreateDirectory(destination);
            else
            {
                var length = new FileInfo(path).Length;
                total = checked(total + length);
                if (total > maximumBytes)
                    throw new D045CodexOutputLimitException("Directory copy byte ceiling exceeded.");
                var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Copy parent is missing.");
                Directory.CreateDirectory(parent);
                File.Copy(path, destination, overwrite: false);
            }
            return true;
        });
        return ComputeTreeDigest(staging, maximumDepth, maximumBytes);
    }

    private static string ComputeTreeDigest(string root, int maximumDepth, long maximumBytes)
    {
        var rows = new List<string>();
        long total = 0;
        var count = 0;
        WalkTree(root, recursive: true, maximumDepth, (path, isDirectory, depth) =>
        {
            if (++count > MaximumSearchFiles)
                throw new D045CodexOutputLimitException("Directory digest item ceiling exceeded.");
            var relative = NormalizeRelative(root, path);
            if (isDirectory)
                rows.Add("D\0" + relative);
            else
            {
                var length = new FileInfo(path).Length;
                total = checked(total + length);
                if (total > maximumBytes)
                    throw new D045CodexOutputLimitException("Directory digest byte ceiling exceeded.");
                rows.Add("F\0" + relative + "\0" + length.ToString(CultureInfo.InvariantCulture) + "\0" + Sha256File(path, Math.Max(length, 1)));
            }
            return true;
        });
        rows.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows))));
    }

    private static void CopyStreamBounded(Stream input, Stream output, long maximumBytes, long? requireExactLength = null)
    {
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            while (true)
            {
                var read = input.Read(rented, 0, rented.Length);
                if (read == 0)
                    break;
                total = checked(total + read);
                if (total > maximumBytes)
                    throw new D045CodexOutputLimitException("Stream exceeded its byte ceiling.");
                output.Write(rented, 0, read);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
        }
        if (requireExactLength.HasValue && total != requireExactLength.Value)
            throw new InvalidDataException("Stream length did not match the trusted response length.");
    }

    private static byte[] ReadStreamBounded(Stream input, long maximumBytes)
    {
        using var output = new MemoryStream();
        CopyStreamBounded(input, output, maximumBytes);
        return output.ToArray();
    }

    private static string LimitText(string? value, int maximum)
    {
        var text = value ?? string.Empty;
        return text.Length <= maximum ? text : text[..maximum];
    }
}

internal sealed partial class D045CodexCapabilityHarness
{
    private static string DemandExactExecutable(string value)
    {
        var path = DemandExistingSafeFile(value);
        if (OperatingSystem.IsWindows()
            && !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "Windows process capabilities accept an exact executable file, never a script or document.");
        }
        if (IsBlockedCommandHost(path))
        {
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "Raw command and script hosts are not available through typed process execution.");
        }
        return path;
    }

    private static bool IsBlockedCommandHost(string path)
    {
        if (BlockedCommandHosts.Contains(Path.GetFileName(path)))
            return true;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new[] { info.OriginalFilename, info.InternalName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Any(value => BlockedCommandHosts.Contains(value!));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
    }

    private static string DemandWorkingDirectory(string value, string executable)
        => value.Length == 0
            ? DemandExistingSafeDirectory(Path.GetDirectoryName(executable)
                ?? throw new InvalidDataException("Executable directory is missing."))
            : DemandExistingSafeDirectory(value);

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        bool redirect)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            CreateNoWindow = redirect,
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            StandardOutputEncoding = redirect ? Encoding.UTF8 : null,
            StandardErrorEncoding = redirect ? Encoding.UTF8 : null
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task<string> ReadBoundedTextAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var buffer = new char[4_096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (builder.Length + read > maximumCharacters)
                throw new D045CodexOutputLimitException("Process output exceeded its character ceiling.");
            builder.Append(buffer, 0, read);
        }
        return builder.ToString();
    }

    private static string JoinProcessOutput(string stdout, string stderr)
    {
        if (stderr.Length == 0)
            return stdout;
        if (stdout.Length == 0)
            return "[stderr]\n" + stderr;
        return stdout + "\n[stderr]\n" + stderr;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private void DemandBackgroundLookupShape(D045CodexCapabilityRequest request)
    {
        DemandEmpty(request.SecondaryTarget, nameof(request.SecondaryTarget));
        DemandEmpty(request.Payload, nameof(request.Payload));
        DemandEmpty(request.Query, nameof(request.Query));
        DemandEmpty(request.WorkingDirectory, nameof(request.WorkingDirectory));
        DemandNoArguments(request);
        if (request.Target.Length != 48
            || request.Target.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F'))
            || request.OwnerId.Length == 0)
        {
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "Background lookup requires an exact opaque job id and owner id.");
        }
    }

    private BackgroundJob DemandOwnedJob(string jobId, string ownerId)
    {
        if (!_backgroundJobs.TryGetValue(jobId, out var job))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The owned background job does not exist.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(job.OwnerId), Encoding.UTF8.GetBytes(ownerId)))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.AccessDenied, "The background job owner did not match.");
        return job;
    }

    private sealed class BackgroundJob : IDisposable
    {
        private readonly Process _process;
        private readonly object _gate = new();
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();
        private bool _truncated;
        private Task? _stdoutPump;
        private Task? _stderrPump;

        internal string JobId { get; }
        internal string OwnerId { get; }
        internal bool IsRunning
        {
            get
            {
                try { return !_process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        internal BackgroundJob(string jobId, string ownerId, Process process)
        {
            JobId = jobId;
            OwnerId = ownerId;
            _process = process;
        }

        internal void StartPumps()
        {
            _stdoutPump = PumpAsync(_process.StandardOutput, _stdout);
            _stderrPump = PumpAsync(_process.StandardError, _stderr);
        }

        private async Task PumpAsync(StreamReader reader, StringBuilder target)
        {
            var buffer = new char[4_096];
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read == 0)
                    return;
                lock (_gate)
                {
                    var remaining = MaximumOutputCharacters / 2 - target.Length;
                    if (remaining > 0)
                        target.Append(buffer, 0, Math.Min(remaining, read));
                    if (read > remaining)
                        _truncated = true;
                }
            }
        }

        internal BackgroundSnapshot Snapshot()
        {
            lock (_gate)
            {
                var running = IsRunning;
                int? exitCode = null;
                if (!running)
                {
                    try { exitCode = _process.ExitCode; }
                    catch (InvalidOperationException) { }
                }
                return new BackgroundSnapshot(
                    JobId,
                    running,
                    exitCode,
                    LimitText(_stdout.ToString(), MaximumOutputCharacters / 2),
                    LimitText(_stderr.ToString(), MaximumOutputCharacters / 2),
                    _truncated);
            }
        }

        internal void Stop(int timeoutMilliseconds)
        {
            TryKillProcessTree(_process);
            try
            {
                if (!_process.WaitForExit(Math.Min(timeoutMilliseconds, 30_000)))
                    throw new D045CodexTimeoutException("The background process tree did not stop before the timeout.");
                Task.WaitAll(new[] { _stdoutPump, _stderrPump }.Where(value => value is not null).Cast<Task>().ToArray(), Math.Min(timeoutMilliseconds, 30_000));
            }
            catch (AggregateException exception)
            {
                throw new InvalidOperationException("Background output pump failed.", exception.Flatten());
            }
        }

        public void Dispose()
        {
            TryKillProcessTree(_process);
            _process.Dispose();
        }
    }

    private sealed record BackgroundSnapshot(
        string JobId,
        bool Running,
        int? ExitCode,
        string StandardOutput,
        string StandardError,
        bool OutputTruncated);
}

internal sealed partial class D045CodexCapabilityHarness
{
    private static Uri DemandHttpsUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.IdnHost)
            || (!uri.IsDefaultPort && uri.Port != 443))
        {
            throw new D045CodexRejectionException(D045CodexOutcomeReason.InvalidRequest, "An absolute credential-free public HTTPS URL on port 443 is required.");
        }
        return uri;
    }

    private IPAddress DemandPublicHttpsTarget(Uri uri)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.IdnHost, out var literal))
            addresses = new[] { literal };
        else
            addresses = _hostResolver(uri.IdnHost) ?? Array.Empty<IPAddress>();
        var distinct = addresses.Distinct().ToArray();
        if (distinct.Length == 0)
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetNotFound, "The public HTTPS host did not resolve.");
        if (distinct.Any(address => !IsPublicAddress(address)))
            throw new D045CodexRejectionException(D045CodexOutcomeReason.TargetUnsafe, "The HTTPS host resolved to a private, local, reserved, or mixed-address target.");
        return distinct
            .OrderBy(value => Convert.ToHexString(value.GetAddressBytes()), StringComparer.Ordinal)
            .First();
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            var first = bytes[0];
            var second = bytes[1];
            return first != 0
                && first != 10
                && first != 127
                && first < 224
                && !(first == 100 && second is >= 64 and <= 127)
                && !(first == 169 && second == 254)
                && !(first == 172 && second is >= 16 and <= 31)
                && !(first == 192 && second == 168)
                && !(first == 192 && second == 0)
                && !(first == 192 && second == 2)
                && !(first == 198 && second is 18 or 19)
                && !(first == 198 && second == 51 && bytes[2] == 100)
                && !(first == 203 && second == 0 && bytes[2] == 113);
        }
        if (bytes.Length != 16)
            return false;
        if (address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
            return false;
        if ((bytes[0] & 0xFE) == 0xFC)
            return false;
        if (bytes.Take(12).All(value => value == 0)
            && bytes[12] == 0 && bytes[13] == 0 && bytes[14] == 0 && bytes[15] == 1)
            return false;
        return !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }

    private static void DemandSuccessfulHttpsResponse(
        D045CodexPublicHttpsResponse response,
        long maximumBytes)
    {
        var code = (int)response.StatusCode;
        if (code is < 200 or >= 300)
            throw new HttpRequestException("Public HTTPS returned non-success status " + code.ToString(CultureInfo.InvariantCulture) + "; redirects are not followed.");
        if (response.ContentLength is < 0)
            throw new InvalidDataException("HTTPS Content-Length was invalid.");
        if (response.ContentLength > maximumBytes)
            throw new D045CodexOutputLimitException("HTTPS Content-Length exceeds the byte ceiling.");
    }

    private static string DecodePublicText(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes).Replace('\0', '\uFFFD');
        return LimitText(text, MaximumOutputCharacters);
    }

    private static D045CodexPublicHttpsResponse SendPinnedPublicHttps(
        Uri uri,
        IPAddress pinnedAddress,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMilliseconds),
            UseCookies = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(pinnedAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(pinnedAddress, 443), token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Lilith-D045-C7/1.0");
        try
        {
            var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStreamAsync(cancellationToken).GetAwaiter().GetResult();
            return new D045CodexPublicHttpsResponse(
                response.StatusCode,
                body,
                response.Content.Headers.ContentLength,
                () =>
                {
                    response.Dispose();
                    request.Dispose();
                    client.Dispose();
                });
        }
        catch
        {
            request.Dispose();
            client.Dispose();
            throw;
        }
    }

    private static bool NavigateBrowser(Uri uri)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return process is not null;
        }
        catch (Exception exception) when (IsOperationalException(exception))
        {
            return false;
        }
    }
}

internal sealed partial class D045CodexCapabilityHarness
{
    private const int ShowWindowMinimize = 6;
    private const int ShowWindowMaximize = 3;
    private const int ShowWindowRestore = 9;
    private const uint ClipboardUnicodeText = 13;
    private const uint GlobalMoveable = 0x0002;
    private const uint InputMouse = 0;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const int SystemMetricVirtualLeft = 76;
    private const int SystemMetricVirtualTop = 77;
    private const int SystemMetricVirtualWidth = 78;
    private const int SystemMetricVirtualHeight = 79;
    private const int WindowStyleIndex = -16;
    private const long EditPasswordStyle = 0x0020;
    private const long WindowVerticalScrollStyle = 0x00200000;
    private const long WindowHorizontalScrollStyle = 0x00100000;
    private const uint MessageSetFocus = 0x0007;
    private const uint MessageVerticalScroll = 0x0115;
    private const uint ButtonGetCheck = 0x00F0;
    private const uint ButtonClick = 0x00F5;
    private const uint SendMessageAbortIfHung = 0x0002;
    private const int ScrollLineUp = 0;
    private const int ScrollLineDown = 1;
    private const int ScrollPageUp = 2;
    private const int ScrollPageDown = 3;

    private static IReadOnlyList<WindowInfo> EnumerateVisibleWindows(int maximum)
    {
        var result = new List<WindowInfo>();
        if (!OperatingSystem.IsWindows())
            return result;
        EnumWindows((handle, ignored) =>
        {
            if (result.Count >= maximum)
                return false;
            if (!IsWindowVisible(handle))
                return true;
            var length = GetWindowTextLength(handle);
            if (length <= 0 || length > 4_096)
                return true;
            var title = new StringBuilder(length + 1);
            GetWindowText(handle, title, title.Capacity);
            GetWindowThreadProcessId(handle, out var pid);
            if (pid != 0 && title.Length != 0)
                result.Add(new WindowInfo(handle, checked((int)pid), LimitText(title.ToString(), 512)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IReadOnlyList<D045CodexWindowTarget> ResolveVisibleWindowTargets(
        int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
            return Array.Empty<D045CodexWindowTarget>();
        var result = new List<D045CodexWindowTarget>(2);
        foreach (var window in EnumerateVisibleWindows(256))
        {
            if (window.ProcessId != processId
                || !TryGetClientBounds(window.Handle, out var bounds))
            {
                continue;
            }
            result.Add(new D045CodexWindowTarget(
                window.Handle,
                window.ProcessId,
                window.Title,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height));
            if (result.Count == 2)
                break;
        }
        return result;
    }

    private static IReadOnlyList<D045CodexSemanticElement> InspectSemanticUiWindows(
        D045CodexWindowTarget window,
        int maximumDepth,
        int maximumResults)
    {
        if (!OperatingSystem.IsWindows()
            || maximumDepth <= 0
            || maximumResults <= 0
            || !HasExactNativeWindowIdentity(window))
        {
            return Array.Empty<D045CodexSemanticElement>();
        }

        var result = new List<D045CodexSemanticElement>(
            Math.Min(maximumResults, 64));
        _ = EnumChildWindows(window.Handle, (handle, ignored) =>
        {
            if (result.Count >= maximumResults)
                return false;
            GetWindowThreadProcessId(handle, out var nativeProcessId);
            if (nativeProcessId != (uint)window.ProcessId)
                return true;
            var depth = GetSemanticDepth(window.Handle, handle, maximumDepth);
            if (depth <= 0
                || !GetWindowRect(handle, out var bounds)
                || bounds.Right <= bounds.Left
                || bounds.Bottom <= bounds.Top)
            {
                return true;
            }

            var className = GetBoundedClassName(handle);
            var style = GetNativeWindowStyle(handle);
            var role = ResolveSemanticRole(className, style);
            var sensitive = IsSensitiveSemanticRole(role)
                || (role == "text_input" && (style & EditPasswordStyle) != 0);
            var name = sensitive
                ? "[redacted]"
                : CanReadSemanticName(role)
                    ? GetBoundedWindowName(handle)
                    : string.Empty;
            var isButton = role is "button" or "checkbox" or "radio";
            var supportsScroll = role is "scroll_bar" or "list" or "tree" or "document"
                || (style & (WindowVerticalScrollStyle
                    | WindowHorizontalScrollStyle)) != 0;
            result.Add(new D045CodexSemanticElement(
                window,
                handle,
                depth,
                role,
                name,
                IsWindowEnabled(handle),
                IsWindowVisible(handle),
                bounds.Left,
                bounds.Top,
                checked(bounds.Right - bounds.Left),
                checked(bounds.Bottom - bounds.Top),
                supportsInvoke: role == "button",
                supportsFocus: role != "label",
                supportsToggle: role == "checkbox",
                supportsSelect: role == "radio",
                supportsScroll: supportsScroll));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static D045CodexSemanticDispatchResult DispatchSemanticUiWindows(
        D045CodexSemanticDispatch dispatch)
    {
        if (!OperatingSystem.IsWindows()
            || !HasExactNativeWindowIdentity(dispatch.Window)
            || !IsWindow(dispatch.Element.Handle)
            || !IsChild(dispatch.Window.Handle, dispatch.Element.Handle)
            || !IsWindowVisible(dispatch.Element.Handle)
            || !IsWindowEnabled(dispatch.Element.Handle))
        {
            return new D045CodexSemanticDispatchResult(false);
        }
        GetWindowThreadProcessId(dispatch.Element.Handle, out var processId);
        if (processId != (uint)dispatch.Window.ProcessId)
            return new D045CodexSemanticDispatchResult(false);

        var timeout = checked((uint)Math.Min(
            dispatch.TimeoutMilliseconds,
            2_000));
        switch (dispatch.Operation)
        {
            case D045CodexOperation.UiInvoke:
            {
                var accepted = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    ButtonClick,
                    UIntPtr.Zero,
                    IntPtr.Zero,
                    timeout,
                    out _);
                return new D045CodexSemanticDispatchResult(
                    accepted,
                    semanticOutcomeObserved: false,
                    observation: accepted ? "button_dispatch_accepted" : string.Empty);
            }
            case D045CodexOperation.UiFocus:
            {
                _ = SetForegroundWindow(dispatch.Window.Handle);
                var accepted = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    MessageSetFocus,
                    UIntPtr.Zero,
                    IntPtr.Zero,
                    timeout,
                    out _);
                return new D045CodexSemanticDispatchResult(
                    accepted,
                    semanticOutcomeObserved: false,
                    observation: accepted ? "focus_dispatch_accepted" : string.Empty);
            }
            case D045CodexOperation.UiToggle:
            case D045CodexOperation.UiSelect:
            {
                var beforeRead = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    ButtonGetCheck,
                    UIntPtr.Zero,
                    IntPtr.Zero,
                    timeout,
                    out var before);
                var accepted = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    ButtonClick,
                    UIntPtr.Zero,
                    IntPtr.Zero,
                    timeout,
                    out _);
                var afterRead = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    ButtonGetCheck,
                    UIntPtr.Zero,
                    IntPtr.Zero,
                    timeout,
                    out var after);
                var observed = accepted
                    && beforeRead
                    && afterRead
                    && (dispatch.Operation == D045CodexOperation.UiToggle
                        ? before != after
                        : after != UIntPtr.Zero);
                return new D045CodexSemanticDispatchResult(
                    accepted,
                    observed,
                    observed
                        ? dispatch.Operation == D045CodexOperation.UiToggle
                            ? "toggle_state_changed"
                            : "selection_state_observed"
                        : accepted
                            ? "button_dispatch_accepted"
                            : string.Empty);
            }
            case D045CodexOperation.UiScroll:
            {
                var command = dispatch.ScrollDirection switch
                {
                    "up" => ScrollLineUp,
                    "down" => ScrollLineDown,
                    "page_up" => ScrollPageUp,
                    "page_down" => ScrollPageDown,
                    _ => -1
                };
                if (command < 0)
                    return new D045CodexSemanticDispatchResult(false);
                var accepted = TrySendBoundedMessage(
                    dispatch.Element.Handle,
                    MessageVerticalScroll,
                    (UIntPtr)(uint)command,
                    IntPtr.Zero,
                    timeout,
                    out _);
                return new D045CodexSemanticDispatchResult(
                    accepted,
                    semanticOutcomeObserved: false,
                    observation: accepted ? "scroll_dispatch_accepted" : string.Empty);
            }
            default:
                return new D045CodexSemanticDispatchResult(false);
        }
    }

    private static bool HasExactNativeWindowIdentity(D045CodexWindowTarget window)
    {
        if (!IsWindow(window.Handle) || !IsWindowVisible(window.Handle))
            return false;
        GetWindowThreadProcessId(window.Handle, out var processId);
        return processId == (uint)window.ProcessId;
    }

    private static int GetSemanticDepth(
        IntPtr root,
        IntPtr element,
        int maximumDepth)
    {
        var current = element;
        for (var depth = 1; depth <= maximumDepth; depth++)
        {
            current = GetParent(current);
            if (current == root)
                return depth;
            if (current == IntPtr.Zero)
                return -1;
        }
        return -1;
    }

    private static string GetBoundedClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(129);
        var length = GetClassName(handle, buffer, buffer.Capacity);
        return length <= 0
            ? string.Empty
            : LimitText(buffer.ToString(), 128);
    }

    private static string GetBoundedWindowName(IntPtr handle)
    {
        var length = Math.Min(GetWindowTextLength(handle), 128);
        if (length <= 0)
            return string.Empty;
        var buffer = new StringBuilder(length + 1);
        _ = GetWindowText(handle, buffer, buffer.Capacity);
        return LimitText(buffer.ToString(), 128);
    }

    private static long GetNativeWindowStyle(IntPtr handle)
        => Environment.Is64BitProcess
            ? GetWindowLongPtr64(handle, WindowStyleIndex).ToInt64()
            : GetWindowLong32(handle, WindowStyleIndex);

    private static string ResolveSemanticRole(string className, long style)
    {
        var normalized = className.ToLowerInvariant();
        if (normalized.Contains("button", StringComparison.Ordinal))
        {
            return (style & 0x0F) switch
            {
                2 or 3 or 5 or 6 => "checkbox",
                4 or 9 => "radio",
                _ => "button"
            };
        }
        if (normalized.Contains("edit", StringComparison.Ordinal))
            return (style & EditPasswordStyle) != 0
                ? "password_input"
                : "text_input";
        if (normalized.Contains("richedit", StringComparison.Ordinal)
            || normalized.Contains("scintilla", StringComparison.Ordinal)
            || normalized.Contains("document", StringComparison.Ordinal)
            || normalized.Contains("renderwidgethost", StringComparison.Ordinal)
            || normalized.Contains("internet explorer_server", StringComparison.Ordinal))
        {
            return "document";
        }
        if (normalized.Contains("static", StringComparison.Ordinal))
            return "label";
        if (normalized.Contains("combobox", StringComparison.Ordinal))
            return "combo_box";
        if (normalized.Contains("list", StringComparison.Ordinal))
            return "list";
        if (normalized.Contains("tree", StringComparison.Ordinal))
            return "tree";
        if (normalized.Contains("scrollbar", StringComparison.Ordinal))
            return "scroll_bar";
        return "control";
    }

    private static bool IsSensitiveSemanticRole(string role)
        => role is "password_input" or "text_input" or "document"
            or "combo_box" or "list" or "tree";

    private static bool CanReadSemanticName(string role)
        => role is "button" or "checkbox" or "radio" or "label";

    private static bool TrySendBoundedMessage(
        IntPtr handle,
        uint message,
        UIntPtr word,
        IntPtr parameter,
        uint timeoutMilliseconds,
        out UIntPtr result)
        => SendMessageTimeout(
            handle,
            message,
            word,
            parameter,
            SendMessageAbortIfHung,
            timeoutMilliseconds,
            out result) != IntPtr.Zero;

    private static bool DispatchPointerWindows(D045CodexPointerDispatch dispatch)
    {
        if (!OperatingSystem.IsWindows()
            || !IsWindow(dispatch.Window.Handle)
            || !IsWindowVisible(dispatch.Window.Handle))
        {
            return false;
        }
        GetWindowThreadProcessId(dispatch.Window.Handle, out var processId);
        if (processId != (uint)dispatch.Window.ProcessId
            || !TryGetClientBounds(dispatch.Window.Handle, out var currentBounds)
            || currentBounds.Left != dispatch.Window.ClientLeft
            || currentBounds.Top != dispatch.Window.ClientTop
            || currentBounds.Width != dispatch.Window.ClientWidth
            || currentBounds.Height != dispatch.Window.ClientHeight)
        {
            return false;
        }
        if (GetForegroundWindow() != dispatch.Window.Handle)
        {
            _ = SetForegroundWindow(dispatch.Window.Handle);
            Thread.Sleep(75);
        }
        if (GetForegroundWindow() != dispatch.Window.Handle)
            return false;

        var virtualLeft = GetSystemMetrics(SystemMetricVirtualLeft);
        var virtualTop = GetSystemMetrics(SystemMetricVirtualTop);
        var virtualWidth = GetSystemMetrics(SystemMetricVirtualWidth);
        var virtualHeight = GetSystemMetrics(SystemMetricVirtualHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1
            || !IsWithinScreen(
                dispatch.StartScreenX,
                dispatch.StartScreenY,
                virtualLeft,
                virtualTop,
                virtualWidth,
                virtualHeight)
            || (dispatch.Operation == D045CodexOperation.PointerDrag
                && !IsWithinScreen(
                    dispatch.EndScreenX,
                    dispatch.EndScreenY,
                    virtualLeft,
                    virtualTop,
                    virtualWidth,
                    virtualHeight)))
        {
            return false;
        }

        var inputs = BuildPointerInputs(
            dispatch,
            virtualLeft,
            virtualTop,
            virtualWidth,
            virtualHeight);
        return SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeInput>()) == inputs.Length;
    }

    private static bool IsForegroundWindow(IntPtr handle)
        => OperatingSystem.IsWindows()
            && handle != IntPtr.Zero
            && GetForegroundWindow() == handle;

    [SupportedOSPlatform("windows")]
    private static bool TryGetClientBounds(IntPtr handle, out ClientBounds bounds)
    {
        bounds = default;
        if (!GetClientRect(handle, out var rect))
            return false;
        var origin = new NativePoint { X = rect.Left, Y = rect.Top };
        if (!ClientToScreen(handle, ref origin))
            return false;
        var width = checked(rect.Right - rect.Left);
        var height = checked(rect.Bottom - rect.Top);
        if (width <= 0 || height <= 0)
            return false;
        bounds = new ClientBounds(origin.X, origin.Y, width, height);
        return true;
    }

    private static bool IsWithinScreen(
        int x,
        int y,
        int left,
        int top,
        int width,
        int height)
        => (long)x >= left
            && (long)y >= top
            && (long)x < (long)left + width
            && (long)y < (long)top + height;

    private static NativeInput[] BuildPointerInputs(
        D045CodexPointerDispatch dispatch,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
    {
        var start = CreateAbsoluteMove(
            dispatch.StartScreenX,
            dispatch.StartScreenY,
            virtualLeft,
            virtualTop,
            virtualWidth,
            virtualHeight);
        var end = CreateAbsoluteMove(
            dispatch.EndScreenX,
            dispatch.EndScreenY,
            virtualLeft,
            virtualTop,
            virtualWidth,
            virtualHeight);
        var (down, up) = PointerButtonFlags(dispatch.Button);
        return dispatch.Operation switch
        {
            D045CodexOperation.PointerMove => new[] { start },
            D045CodexOperation.PointerClick => new[]
            {
                start,
                CreateMouseButton(down),
                CreateMouseButton(up)
            },
            D045CodexOperation.PointerDoubleClick => new[]
            {
                start,
                CreateMouseButton(down),
                CreateMouseButton(up),
                CreateMouseButton(down),
                CreateMouseButton(up)
            },
            D045CodexOperation.PointerDrag => new[]
            {
                start,
                CreateMouseButton(down),
                end,
                CreateMouseButton(up)
            },
            _ => Array.Empty<NativeInput>()
        };
    }

    private static (uint Down, uint Up) PointerButtonFlags(
        D045CodexPointerButton button)
        => button switch
        {
            D045CodexPointerButton.Left => (MouseLeftDown, MouseLeftUp),
            D045CodexPointerButton.Right => (MouseRightDown, MouseRightUp),
            D045CodexPointerButton.Middle => (MouseMiddleDown, MouseMiddleUp),
            D045CodexPointerButton.None => (0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };

    private static NativeInput CreateAbsoluteMove(
        int screenX,
        int screenY,
        int virtualLeft,
        int virtualTop,
        int virtualWidth,
        int virtualHeight)
        => new()
        {
            Type = InputMouse,
            Union = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    DeltaX = checked((int)Math.Round(
                        ((long)screenX - virtualLeft) * 65_535d
                        / (virtualWidth - 1),
                        MidpointRounding.AwayFromZero)),
                    DeltaY = checked((int)Math.Round(
                        ((long)screenY - virtualTop) * 65_535d
                        / (virtualHeight - 1),
                        MidpointRounding.AwayFromZero)),
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk
                }
            }
        };

    private static NativeInput CreateMouseButton(uint flags)
        => new()
        {
            Type = InputMouse,
            Union = new NativeInputUnion
            {
                Mouse = new NativeMouseInput { Flags = flags }
            }
        };

    private static bool WriteClipboardText(string text)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = IntPtr.Zero;
        var ownershipTransferred = false;
        try
        {
            for (var attempt = 0; attempt < 10 && !OpenClipboard(IntPtr.Zero); attempt++)
                Thread.Sleep(20);
            if (!IsClipboardFormatAvailable(ClipboardUnicodeText) && !EmptyClipboard())
                return false;
            if (!EmptyClipboard())
                return false;
            memory = GlobalAlloc(GlobalMoveable, (UIntPtr)(uint)bytes.Length);
            if (memory == IntPtr.Zero)
                return false;
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
                return false;
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { _ = GlobalUnlock(memory); }
            if (SetClipboardData(ClipboardUnicodeText, memory) == IntPtr.Zero)
                return false;
            ownershipTransferred = true;
        }
        finally
        {
            _ = CloseClipboard();
            if (memory != IntPtr.Zero && !ownershipTransferred)
                _ = GlobalFree(memory);
            CryptographicOperations.ZeroMemory(bytes);
        }
        return ReadClipboardText() == text;
    }

    private static string ReadClipboardText()
    {
        for (var attempt = 0; attempt < 10 && !OpenClipboard(IntPtr.Zero); attempt++)
            Thread.Sleep(20);
        try
        {
            var handle = GetClipboardData(ClipboardUnicodeText);
            if (handle == IntPtr.Zero)
                return string.Empty;
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return string.Empty;
            try { return Marshal.PtrToStringUni(pointer) ?? string.Empty; }
            finally { _ = GlobalUnlock(handle); }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private sealed record WindowInfo(IntPtr Handle, int ProcessId, string Title);

    private readonly record struct ClientBounds(
        int Left,
        int Top,
        int Width,
        int Height);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        internal uint Type;
        internal NativeInputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        internal NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        internal int DeltaX;
        internal int DeltaY;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        IntPtr parent,
        EnumWindowsCallback callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr window,
        StringBuilder className,
        int maximum);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        UIntPtr word,
        IntPtr parameter,
        uint flags,
        uint timeoutMilliseconds,
        out UIntPtr result);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
