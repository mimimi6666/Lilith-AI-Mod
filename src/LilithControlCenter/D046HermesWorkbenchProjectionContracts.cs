using System.IO;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal enum D046HermesProjectionDecodeStage
{
    ActivationResponse = 1,
    HistoryResponse = 2,
    SessionInfoEvent = 3,
    SnapshotShape = 4
}

internal sealed record D046HermesProjectionFault(string Code)
{
    private static readonly object DecodeStageKey = new();

    // Annotate only in-process failures at known decoder boundaries. Keep the
    // original exception type/stack and never attach payloads or method strings.
    internal static void MarkDecodeStage(Exception exception, D046HermesProjectionDecodeStage stage)
    {
        if (exception is InvalidDataException or JsonException
            && !exception.Data.Contains(DecodeStageKey))
            exception.Data[DecodeStageKey] = stage;
    }

    internal string ToSafeLogLine(string phase)
    {
        var safeCode = Code is "native_tool_event_unattested" or "runtime_sequence_gap"
            or "projection_sequence_gap" or "runtime_generation_changed" or "projection_data_invalid"
            or "runtime_response_unmatched" or "runtime_response_method_mismatch"
            or "projection_event_decode_failed" or "projection_event_scope_invalid"
            or "projection_event_session_mismatch" or "projection_global_session_mismatch"
            or "projection_event_source_mismatch" or "projection_event_invalid"
            or "projection_event_type_mismatch" or "projection_authority_event_unknown"
            or "projection_event_json_invalid" or "runtime_handshake_invalid"
            or "projection_activation_invalid" or "projection_history_invalid"
            or "projection_session_info_invalid" or "projection_snapshot_invalid"
            or "session_activation_failed"
            or "runtime_timeout" or "runtime_connection_lost" or "projection_failed" ? Code : "projection_failed";
        var safePhase = phase is "snapshot_apply" or "command" or "clarification" or "turn" or "idle"
            ? phase : "unknown";
        return "WorkbenchProjectionFault Code=" + safeCode + " Phase=" + safePhase;
    }

    // Never expose arbitrary exception text, credentials or event payloads.
    // The original exception remains the pipe's private terminal cause.
    internal static D046HermesProjectionFault FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var cause = exception.GetBaseException();
        var code = cause switch
        {
            D046HermesSessionActivationException => "session_activation_failed",
            InvalidDataException { Message: "Hermes emitted an unattested tool event." }
                => "native_tool_event_unattested",
            InvalidDataException { Message: "Runtime sequence is not contiguous." }
                => "runtime_sequence_gap",
            InvalidDataException { Message: "Hermes projection sequence is not contiguous." }
                => "projection_sequence_gap",
            InvalidDataException { Message: "Runtime generation changed in place." }
                => "runtime_generation_changed",
            InvalidDataException { Message: "Runtime response did not match a request." }
                => "runtime_response_unmatched",
            InvalidDataException { Message: "Runtime response method did not match." }
                => "runtime_response_method_mismatch",
            InvalidDataException { Message: "Hermes event could not be projected without loss." }
                => "projection_event_decode_failed",
            InvalidDataException { Message: "Hermes projection event scope was rejected." }
                => "projection_event_scope_invalid",
            InvalidDataException { Message: "The Hermes event session was not correlated to the active task." }
                => "projection_event_session_mismatch",
            InvalidDataException { Message: "The Hermes global session event was not correlated to the active task." }
                => "projection_global_session_mismatch",
            InvalidDataException { Message: "Hermes source method or canonical payload was not preserved." }
                => "projection_event_source_mismatch",
            InvalidDataException { Message: "Hermes projection event was rejected." }
                => "projection_event_invalid",
            InvalidDataException { Message: "The Hermes event type was not preserved." }
                => "projection_event_type_mismatch",
            InvalidDataException { Message: "Hermes emitted an unknown authority-bearing event." }
                => "projection_authority_event_unknown",
            InvalidDataException { Message: "Hermes event JSON was rejected." }
                => "projection_event_json_invalid",
            InvalidDataException { Message: "Runtime hello must be the first frame."
                or "Runtime hello was duplicated."
                or "Runtime is not ready for the requested Workbench protocol." }
                => "runtime_handshake_invalid",
            InvalidDataException => "projection_data_invalid",
            TimeoutException => "runtime_timeout",
            IOException => "runtime_connection_lost",
            _ => "projection_failed"
        };
        // More specific existing identity/authority codes always win. For a
        // generic shape failure, retain only the closed local decoder stage.
        if (code == "projection_data_invalid" || cause is JsonException)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current.Data[DecodeStageKey] is not D046HermesProjectionDecodeStage stage)
                    continue;
                code = stage switch
                {
                    D046HermesProjectionDecodeStage.ActivationResponse => "projection_activation_invalid",
                    D046HermesProjectionDecodeStage.HistoryResponse => "projection_history_invalid",
                    D046HermesProjectionDecodeStage.SessionInfoEvent => "projection_session_info_invalid",
                    D046HermesProjectionDecodeStage.SnapshotShape => "projection_snapshot_invalid",
                    _ => code
                };
                break;
            }
        }
        return new(code);
    }
}

public enum D046HermesEventKind
{
    Session = 1,
    Message = 2,
    Reasoning = 3,
    Status = 4,
    Tool = 5,
    Clarification = 6,
    Approval = 7,
    Error = 8,
    Metadata = 9,
    Subagent = 10,
    SecureInput = 11
}

public enum D046HermesEventScopeKind
{
    Session = 1,
    Turn = 2,
    LegacyUnscoped = 3
}

public enum D046HermesSubagentState
{
    Running = 1,
    Completed = 2,
    Failed = 3,
    Interrupted = 4,
    TimedOut = 5,
    Error = 6,
    Unknown = 7
}

public enum D046HermesSubagentEventPhase
{
    Started = 1,
    Thinking = 2,
    Tool = 3,
    Completed = 4
}

public enum D046HermesSessionChange
{
    Created = 1,
    Opened = 2,
    Renamed = 3,
    ModelChanged = 4,
    Archived = 5
}

public enum D046HermesMessageRole
{
    User = 1,
    Assistant = 2,
    System = 3
}

public enum D046HermesStreamOperation
{
    Started = 1,
    Appended = 2,
    Replaced = 3,
    Completed = 4
}

public enum D046HermesTaskState
{
    Idle = 1,
    Streaming = 2,
    WaitingForClarification = 3,
    WaitingForApproval = 4,
    Stopping = 5,
    Failed = 6,
    WaitingForSecureInput = 7
}

public enum D046HermesToolPhase
{
    Proposed = 1,
    Started = 2,
    Completed = 3,
    Rejected = 4
}

public enum D046HermesClarificationState
{
    Requested = 1,
    Answered = 2,
    Cancelled = 3
}

public enum D046HermesApprovalState
{
    Requested = 1,
    Approved = 2,
    Denied = 3,
    Expired = 4
}

public enum D046HermesSecureInputKind
{
    SudoPassword = 1,
    SecretValue = 2
}

public enum D046HermesSecureInputState
{
    Requested = 1,
    Cancelled = 2
}

public abstract record D046HermesProjectionPayload(D046HermesEventKind Kind);

public sealed record D046HermesDurableMessageProvenance(
    string Source,
    long RowId,
    DateTimeOffset TimestampUtc,
    bool IsConversationActivity,
    string TurnOrigin,
    string TurnOriginKind);

public sealed record D046HermesSessionPayload(
    D046HermesSessionChange Change,
    string Title,
    string LineageId,
    string ProviderId,
    string ModelDisplayName,
    D046HermesTaskState TaskState)
    : D046HermesProjectionPayload(D046HermesEventKind.Session);

public sealed record D046HermesMessagePayload(
    string MessageId,
    D046HermesMessageRole Role,
    D046HermesStreamOperation Operation,
    string Text,
    bool CompletesTurn = false,
    bool IsInterim = false,
    bool AlreadyStreamed = false,
    bool ResponsePreviewed = false,
    string PreviewMessageId = "",
    string Warning = "",
    string PrimaryMessageId = "",
    D046HermesDurableMessageProvenance? Durable = null,
    bool MessageCompletionConfirmed = false,
    IReadOnlyList<UsageBudgetConstraint>? BudgetConstraints = null)
    : D046HermesProjectionPayload(D046HermesEventKind.Message);

public sealed record D046HermesReasoningPayload(
    string ReasoningId,
    D046HermesStreamOperation Operation,
    string Text)
    : D046HermesProjectionPayload(D046HermesEventKind.Reasoning);

public sealed record D046HermesStatusPayload(
    string StatusCode,
    string DisplayText,
    D046HermesTaskState TaskState,
    bool IsConnected,
    bool NativeSurfaceAttested,
    bool ComputerUseEnabled)
    : D046HermesProjectionPayload(D046HermesEventKind.Status);

public sealed record D046HermesToolPayload(
    string ToolCallId,
    string ToolDisplayName,
    D046HermesToolPhase Phase,
    string Summary,
    bool IsHostEffectTool,
    string AudioFilePath = "",
    D046HermesClarificationAnswer? ClarificationAnswer = null,
    D046HermesGeneratedImageAttachment? GeneratedImage = null)
    : D046HermesProjectionPayload(D046HermesEventKind.Tool);

public sealed record D046HermesClarificationAnswer(string Question, string Answer);

public sealed record D046HermesClarificationPayload(
    string ClarificationId,
    D046HermesClarificationState State,
    string Prompt,
    IReadOnlyList<string>? Choices = null,
    bool MultiSelect = false)
    : D046HermesProjectionPayload(D046HermesEventKind.Clarification);

public sealed record D046HermesApprovalPayload(
    string ApprovalId,
    D046HermesApprovalState State,
    string Summary,
    IReadOnlyList<string> Choices,
    string Command = "",
    string Description = "",
    string PatternKey = "",
    bool AllowSession = false,
    bool AllowPermanent = false,
    bool SmartDenied = false)
    : D046HermesProjectionPayload(D046HermesEventKind.Approval);

public sealed record D046HermesSecureInputPayload(
    string RequestId,
    D046HermesSecureInputState State,
    D046HermesSecureInputKind InputKind,
    string Prompt,
    string EnvironmentVariable = "")
    : D046HermesProjectionPayload(D046HermesEventKind.SecureInput);

public sealed record D046HermesErrorPayload(
    string ErrorCode,
    string DisplayText,
    bool IsTerminal,
    bool CompletesTurn = false)
    : D046HermesProjectionPayload(D046HermesEventKind.Error);

public sealed record D046HermesTurnErrorPayload(
    string AssistantMessageId,
    string AssistantText,
    string ErrorCode,
    string DisplayText,
    bool Recoverable,
    bool Partial,
    bool IsHistoryMarker = false,
    string Warning = "",
    UsageBudgetFailure? BudgetError = null,
    IReadOnlyList<UsageBudgetConstraint>? BudgetConstraints = null)
    : D046HermesProjectionPayload(D046HermesEventKind.Error);

public sealed record D046HermesMetadataPayload(
    string MetadataType,
    string DisplayText)
    : D046HermesProjectionPayload(D046HermesEventKind.Metadata);

public sealed record D046HermesBudgetConstraintsPayload(IReadOnlyList<UsageBudgetConstraint> Constraints)
    : D046HermesProjectionPayload(D046HermesEventKind.Metadata);

public sealed record D046HermesCronCompletionPayload(
    string DisplayText,
    bool Success)
    : D046HermesProjectionPayload(D046HermesEventKind.Metadata);

public sealed record D046HermesSubagentCardProjection(
    string SubagentId,
    string ParentId,
    string ChildSessionId,
    string Goal,
    string Model,
    D046HermesSubagentState State,
    int Depth,
    int ToolCount,
    DateTimeOffset? StartedUtc,
    double? DurationSeconds,
    IReadOnlyList<string> Toolsets);

public sealed record D046HermesSubagentPayload(
    D046HermesSubagentEventPhase Phase,
    D046HermesSubagentCardProjection Card,
    string DisplayText)
    : D046HermesProjectionPayload(D046HermesEventKind.Subagent);

internal static class D052HermesConversationActivityPolicy
{
    internal static bool TryGetConversationActivityUtc(
        D046HermesProjectionPayload payload,
        out DateTimeOffset activityUtc)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var durable = payload switch
        {
            D046HermesMessagePayload
            {
                Role: D046HermesMessageRole.User,
                Text.Length: > 0,
                Durable.IsConversationActivity: true,
                Durable.TurnOrigin: "user"
            } user => user.Durable,
            D046HermesMessagePayload
            {
                Role: D046HermesMessageRole.Assistant,
                Operation: D046HermesStreamOperation.Completed,
                CompletesTurn: true,
                IsInterim: false,
                Text.Length: > 0,
                Durable.IsConversationActivity: true,
                Durable.TurnOrigin: "user"
            } assistant => assistant.Durable,
            _ => null
        };
        activityUtc = durable?.TimestampUtc ?? default;
        return durable is not null;
    }
}

internal static class D046HermesTaskStateReducer
{
    internal static D046HermesTaskState Reduce(
        D046HermesTaskState current,
        D046HermesProjectionPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload switch
        {
            D046HermesMessagePayload message
                when message.Operation is D046HermesStreamOperation.Started
                    or D046HermesStreamOperation.Appended
                => D046HermesTaskState.Streaming,
            D046HermesMessagePayload { CompletesTurn: true }
                => D046HermesTaskState.Idle,
            D046HermesMessagePayload => current,
            D046HermesSessionPayload session => session.TaskState,
            D046HermesClarificationPayload clarification
                => clarification.State == D046HermesClarificationState.Requested
                    ? D046HermesTaskState.WaitingForClarification
                    : D046HermesTaskState.Idle,
            D046HermesApprovalPayload approval
                => approval.State == D046HermesApprovalState.Requested
                    ? D046HermesTaskState.WaitingForApproval
                    : D046HermesTaskState.Streaming,
            D046HermesSecureInputPayload secureInput
                => secureInput.State == D046HermesSecureInputState.Requested
                    ? D046HermesTaskState.WaitingForSecureInput
                    : D046HermesTaskState.Streaming,
            D046HermesErrorPayload { CompletesTurn: true }
                => D046HermesTaskState.Idle,
            D046HermesErrorPayload => D046HermesTaskState.Failed,
            D046HermesTurnErrorPayload => D046HermesTaskState.Idle,
            D046HermesStatusPayload status => status.TaskState,
            D046HermesToolPayload tool
                when tool.Phase is D046HermesToolPhase.Proposed
                    or D046HermesToolPhase.Started
                => D046HermesTaskState.Streaming,
            D046HermesToolPayload => current,
            _ => current
        };
    }
}

public sealed record D046HermesProjectionEvent(
    string EventId,
    long Sequence,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    DateTimeOffset OccurredUtc,
    string SourceMethod,
    D046HermesProjectionPayload Payload,
    string CanonicalPayloadJson,
    string TurnId = "",
    D046HermesEventScopeKind ScopeKind = D046HermesEventScopeKind.Session,
    D046HermesTaskState? EffectiveTaskState = null);

public sealed record D046HermesTaskProjection(
    string TaskId,
    string SessionId,
    string LineageId,
    string Title,
    string ProviderId,
    string ModelDisplayName,
    D046HermesTaskState State,
    DateTimeOffset UpdatedUtc,
    bool IsPinned = false);

internal sealed record D052HermesTaskPinRequest(
    string TaskId,
    bool ExpectedIsPinned,
    bool IsPinned);

internal sealed record D052HermesTaskPinReceipt(
    string TaskId,
    bool IsPinned,
    D046HermesWorkbenchSnapshot Snapshot);

public sealed record D046HermesPendingApprovalProjection(
    string EventId,
    string ApprovalId,
    string Summary,
    IReadOnlyList<string> Choices,
    string Command = "",
    string Description = "",
    string PatternKey = "",
    bool AllowSession = false,
    bool AllowPermanent = false,
    bool SmartDenied = false);

public sealed record D046HermesPendingClarificationProjection(
    string EventId,
    string ClarificationId,
    string Prompt,
    IReadOnlyList<string> Choices,
    bool MultiSelect);

public sealed record D046HermesPendingSecureInputProjection(
    string EventId,
    string RequestId,
    D046HermesSecureInputKind InputKind,
    string Prompt,
    string EnvironmentVariable);

public sealed record D046HermesWorkbenchSnapshot(
    string RuntimeGeneration,
    long ThroughSequence,
    bool IsConnected,
    bool NativeSurfaceAttested,
    bool NativeToolsConfigStale,
    bool ComputerUseEnabled,
    string StatusText,
    string SelectedTaskId,
    IReadOnlyList<D046HermesTaskProjection> Tasks,
    IReadOnlyList<D046HermesProjectionEvent> SelectedSessionEvents,
    IReadOnlyList<D046HermesPendingApprovalProjection> SelectedPendingApprovals,
    D046HermesPendingClarificationProjection? SelectedPendingClarification = null,
    D046HermesPendingSecureInputProjection? SelectedPendingSecureInput = null,
    bool CredentialRecoveryAvailable = false);

internal static class D046HermesWorkbenchSnapshotApprovalValidator
{
    internal const int MaximumPendingApprovalCount = 64;
    private const int MaximumIdentityLength = 1024;
    private const int MaximumSummaryUtf8Bytes = 16 * 1024;

    internal static void Validate(D046HermesWorkbenchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Tasks is null
            || snapshot.SelectedSessionEvents is null
            || snapshot.SelectedPendingApprovals is null
            || snapshot.SelectedPendingApprovals.Count > MaximumPendingApprovalCount)
        {
            throw new InvalidDataException(
                "Hermes pending-approval snapshot shape was rejected.");
        }

        D046HermesTaskProjection? selected = null;
        if (!string.IsNullOrEmpty(snapshot.SelectedTaskId))
        {
            var selectedMatches = snapshot.Tasks
                .Where(task => task is not null
                    && string.Equals(
                        task.TaskId,
                        snapshot.SelectedTaskId,
                        StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (selectedMatches.Length != 1)
            {
                throw new InvalidDataException(
                    "Hermes pending approvals did not identify one selected task.");
            }
            selected = selectedMatches[0];
        }
        var hasPending = snapshot.SelectedPendingApprovals.Count != 0;
        if (snapshot.CredentialRecoveryAvailable
            && (!snapshot.IsConnected || snapshot.NativeSurfaceAttested
                || snapshot.NativeToolsConfigStale || selected is null
                || selected.State != D046HermesTaskState.Failed
                || string.IsNullOrEmpty(selected.SessionId) || hasPending
                || snapshot.SelectedPendingClarification is not null
                || snapshot.SelectedPendingSecureInput is not null))
        {
            throw new InvalidDataException("Credential recovery snapshot was not isolated.");
        }
        if (selected is null)
        {
            if (hasPending)
            {
                throw new InvalidDataException(
                    "Hermes pending approvals had no selected task.");
            }
            return;
        }
        if (hasPending
            && selected.State != D046HermesTaskState.WaitingForApproval)
        {
            throw new InvalidDataException(
                "Hermes selected-task and pending-approval states disagreed.");
        }

        var requestedByEventId = new Dictionary<
            string,
            (D046HermesProjectionEvent Event, D046HermesApprovalPayload Approval)>(
                StringComparer.Ordinal);
        foreach (var projectionEvent in snapshot.SelectedSessionEvents)
        {
            if (projectionEvent is null
                || !string.Equals(
                    projectionEvent.SourceMethod,
                    "approval.request",
                    StringComparison.Ordinal)
                || projectionEvent.Payload is not D046HermesApprovalPayload
                {
                    State: D046HermesApprovalState.Requested
                } approval)
            {
                continue;
            }
            if (!IsIdentity(projectionEvent.EventId)
                || !string.Equals(
                    projectionEvent.TaskId,
                    selected.TaskId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    projectionEvent.SessionId,
                    selected.SessionId,
                    StringComparison.Ordinal)
                || !requestedByEventId.TryAdd(
                    projectionEvent.EventId,
                    (projectionEvent, approval)))
            {
                throw new InvalidDataException(
                    "Hermes approval history contained a duplicate event ID.");
            }
        }

        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var approvalIds = new HashSet<string>(StringComparer.Ordinal);
        long priorSequence = 0;
        foreach (var pending in snapshot.SelectedPendingApprovals)
        {
            if (pending is null
                || !IsIdentity(pending.EventId)
                || !IsIdentity(pending.ApprovalId)
                || string.IsNullOrWhiteSpace(pending.Summary)
                || Encoding.UTF8.GetByteCount(pending.Summary) > MaximumSummaryUtf8Bytes
                || pending.Choices is null
                || pending.Choices.Count is < 1 or > 4
                || pending.Choices.Any(choice => choice is not (
                    "once" or "session" or "always" or "deny"))
                || pending.Choices.Distinct(StringComparer.Ordinal).Count()
                    != pending.Choices.Count
                || !eventIds.Add(pending.EventId)
                || !approvalIds.Add(pending.ApprovalId)
                || !requestedByEventId.TryGetValue(pending.EventId, out var requested)
                || requested.Event.Sequence <= priorSequence
                || !string.Equals(
                    requested.Approval.ApprovalId,
                    pending.ApprovalId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    requested.Approval.Summary,
                    pending.Summary,
                    StringComparison.Ordinal)
                || requested.Approval.Choices is null
                || !requested.Approval.Choices.SequenceEqual(
                    pending.Choices,
                    StringComparer.Ordinal)
                || !string.Equals(requested.Approval.Command, pending.Command, StringComparison.Ordinal)
                || !string.Equals(requested.Approval.Description, pending.Description, StringComparison.Ordinal)
                || !string.Equals(requested.Approval.PatternKey, pending.PatternKey, StringComparison.Ordinal)
                || requested.Approval.AllowSession != pending.AllowSession
                || requested.Approval.AllowPermanent != pending.AllowPermanent
                || requested.Approval.SmartDenied != pending.SmartDenied)
            {
                throw new InvalidDataException(
                    "Hermes pending approval was not an exact FIFO history projection.");
            }
            priorSequence = requested.Event.Sequence;
        }
    }

    private static bool IsIdentity(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumIdentityLength
            && !value.Any(char.IsControl);
}

public sealed record D046HermesSubmitRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string Text,
    string? UiLocale = null);

public sealed record D046HermesImageUpload(
    string FileName,
    string MediaType,
    ReadOnlyMemory<byte> Content);

// Presentation-only descriptor for an image already admitted by the native
// upload path. It does not authorize arbitrary filesystem or network reads.
public sealed record D046HermesImageAttachment(
    string FilePath,
    string FileName,
    string MediaType,
    long ByteLength,
    string Sha256);

public sealed record D046HermesSubmitWithImageRequest(
    D046HermesSubmitRequest Submit,
    D046HermesImageUpload Image);

public sealed record D046HermesImageAttachReceipt(
    bool Attached,
    string Code,
    string Path,
    int Count,
    long Bytes,
    string RuntimeGeneration,
    string TaskId,
    string SessionId);

public sealed record D046HermesImageDetachReceipt(
    bool Detached,
    string Code,
    int Count,
    string RuntimeGeneration,
    string TaskId,
    string SessionId);

public sealed record D046HermesStopRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence);

public sealed record D046HermesDeleteTaskRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence);

public sealed record D046HermesDeleteTaskReceipt(
    bool Deleted,
    string Code,
    bool RecoveryRequired,
    bool TransportUncertain,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    D046HermesWorkbenchSnapshot Snapshot,
    string LocalPreferenceWarningCode = "")
{
    public const string TaskPinCleanupPendingWarningCode =
        "local.task_pin_cleanup_pending";
}

// Deletion and loading its replacement are different outcomes. Never turn an
// already verified native deletion into a retryable "not deleted" UI result.
public sealed class D046HermesDeletedTaskRecoveryException : InvalidOperationException
{
    public D046HermesDeleteTaskReceipt Receipt { get; }

    public D046HermesDeletedTaskRecoveryException(
        D046HermesDeleteTaskReceipt receipt,
        Exception innerException)
        : base("Hermes deleted the chat, but its replacement could not be loaded.",
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.Deleted || receipt.TransportUncertain)
            throw new ArgumentException("A verified deletion receipt is required.", nameof(receipt));
        Receipt = receipt;
    }
}

public sealed record D046HermesRenameTaskRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string Title);

public sealed record D046HermesRenameTaskReceipt(
    bool Accepted,
    string Code,
    bool Pending,
    bool TransportUncertain,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string Title,
    D046HermesWorkbenchSnapshot Snapshot);

public sealed record D046HermesCompressionRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string FocusTopic);

public record D046HermesCommandReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId);

public sealed record D046HermesSubmitReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string TurnId,
    string UserMessageId,
    string AssistantMessageId,
    IReadOnlyList<D046HermesImageAttachment>? Images = null)
    : D046HermesCommandReceipt(
        Accepted,
        Code,
        RuntimeGeneration,
        TaskId,
        SessionId)
{
    public bool CreatesTurn => Accepted
        && HermesTurnCorrelationContract.IsIdentity(TurnId)
        && HermesTurnCorrelationContract.IsIdentity(UserMessageId)
        && HermesTurnCorrelationContract.IsIdentity(AssistantMessageId)
        && new HashSet<string>(StringComparer.Ordinal)
        {
            TurnId,
            UserMessageId,
            AssistantMessageId
        }.Count == 3;

    public bool ConsumesWithoutTurn => Accepted
        && !CreatesTurn
        && TurnId.Length == 0
        && UserMessageId.Length == 0
        && AssistantMessageId.Length == 0
        && Code is "queued" or "steered" or "redirected" or "voice_stopped";

    public bool ConsumesInput => (CreatesTurn
            && string.Equals(Code, "streaming", StringComparison.Ordinal))
        || ConsumesWithoutTurn;
}

public sealed record D046HermesApprovalResponseRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string Choice);

public sealed record D046HermesApprovalResponseReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string Choice,
    int Resolved,
    bool RetryAllowed = false,
    string RetryApprovalId = "");

public sealed record D046HermesClarificationResponseRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string ClarificationId,
    string Answer);

public sealed record D046HermesClarificationResponseReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string ClarificationId);

public sealed record D046HermesSecureInputResponseRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string RequestId,
    D046HermesSecureInputKind InputKind,
    string Value);

public sealed record D046HermesSecureInputResponseReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string RequestId,
    D046HermesSecureInputKind InputKind);

public sealed record D046HermesModelOption(
    string ProviderId,
    string ProviderDisplayName,
    string ModelId)
{
    public string DisplayName => ModelId + " · " + ProviderDisplayName;

    public override string ToString() => DisplayName;
}

public sealed record D046HermesModelCatalog(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    IReadOnlyList<D046HermesModelOption> Options,
    string ExactProviderId = "",
    string ExactModelId = "")
{
    public bool HasExactSelection => !string.IsNullOrEmpty(ExactProviderId)
        && !string.IsNullOrEmpty(ExactModelId);
}

public enum D046HermesSessionPreflightRequirement
{
    ModelSelectionRequired = 1,
    CredentialRequired = 2,
    ModelDiscoveryUnavailable = 3
}

// Only a correlated, exact Runtime response proving no native dispatch may use
// this exception. Protocol errors and uncertain outcomes still retire the pipe.
public sealed class D046HermesSessionTransitionRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class D046HermesSessionPreflightException
    : InvalidOperationException
{
    public D046HermesSessionPreflightRequirement Requirement { get; }
    public D046HermesModelCatalog Catalog { get; }
    public D046HermesWorkbenchSnapshot? ContextSnapshot { get; }
    public string Code { get; }
    public bool IsRetryable => Requirement
        == D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable;

    public D046HermesSessionPreflightException(
        D046HermesSessionPreflightRequirement requirement,
        D046HermesModelCatalog catalog,
        D046HermesWorkbenchSnapshot? contextSnapshot = null,
        string code = "",
        Exception? innerException = null)
        : base(requirement switch
        {
            D046HermesSessionPreflightRequirement.ModelSelectionRequired
                => "Select a Hermes model before creating a session.",
            D046HermesSessionPreflightRequirement.CredentialRequired
                => "Configure a Hermes model credential before creating a session.",
            D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable
                => "Hermes model discovery is temporarily unavailable; retry is allowed.",
            _ => throw new ArgumentOutOfRangeException(nameof(requirement))
        }, innerException)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        ContextSnapshot = contextSnapshot;
        Requirement = requirement;
        Code = string.IsNullOrWhiteSpace(code)
            ? requirement switch
            {
                D046HermesSessionPreflightRequirement.ModelSelectionRequired
                    => "model.selection_required",
                D046HermesSessionPreflightRequirement.CredentialRequired
                    => "model.credential_required",
                D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable
                    => "model.discovery_unavailable",
                _ => throw new ArgumentOutOfRangeException(nameof(requirement))
            }
            : code;
    }
}

public sealed record D046HermesModelSelectionRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string ProviderId,
    string ModelId,
    bool ConfirmExpensiveModel);

public sealed record D046HermesModelSelectionReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string ProviderId,
    string ModelId,
    bool Deferred,
    string Warning,
    bool ConfirmationRequired,
    string ConfirmationMessage,
    D046HermesWorkbenchSnapshot? RecoverySnapshot = null);

public sealed record D046HermesDefaultModelSelectionRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string ProviderId,
    string ModelId,
    bool ConfirmExpensiveModel);

public sealed record D046HermesDefaultModelSelectionReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string ProviderId,
    string ModelId,
    string Warning,
    bool ConfirmationRequired,
    string ConfirmationMessage);

public enum D046HermesNativeToolsetReadiness
{
    Ready = 1,
    NeedsSetup = 2,
    NeedsAuthentication = 3,
    NeedsKeys = 4
}

public enum D046HermesNativeApprovalMode
{
    Manual = 1,
    Smart = 2,
    Off = 3
}

public sealed record D046HermesNativeToolsetOption(
    string Name,
    string Label,
    string Description,
    bool Enabled,
    bool Configured,
    D046HermesNativeToolsetReadiness Readiness,
    IReadOnlyList<string> Tools,
    IReadOnlyList<LilithRuntimeContracts.HermesNativeSetupProvider>? SetupProviders = null);

public sealed record D046HermesNativeToolsCatalog(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string Platform,
    D046HermesNativeApprovalMode ApprovalMode,
    IReadOnlyList<string> EnabledToolsets,
    IReadOnlyList<D046HermesNativeToolsetOption> Toolsets,
    bool NewSessionRequired,
    IReadOnlyList<LilithRuntimeContracts.HermesNativeMcpCatalogEntry>? McpCatalog = null,
    IReadOnlyList<LilithRuntimeContracts.HermesNativeToolPreset>? Presets = null,
    bool? ToolDiscoveryEnabled = null)
{
    // Existing compiled workbench consumers still reference this exact constructor.
    public D046HermesNativeToolsCatalog(
        string runtimeGeneration, string taskId, string sessionId, string platform,
        D046HermesNativeApprovalMode approvalMode, IReadOnlyList<string> enabledToolsets,
        IReadOnlyList<D046HermesNativeToolsetOption> toolsets, bool newSessionRequired,
        IReadOnlyList<LilithRuntimeContracts.HermesNativeMcpCatalogEntry>? mcpCatalog)
        : this(runtimeGeneration, taskId, sessionId, platform, approvalMode, enabledToolsets,
            toolsets, newSessionRequired, mcpCatalog, null, null) { }

    public void Deconstruct(
        out string runtimeGeneration, out string taskId, out string sessionId, out string platform,
        out D046HermesNativeApprovalMode approvalMode, out IReadOnlyList<string> enabledToolsets,
        out IReadOnlyList<D046HermesNativeToolsetOption> toolsets, out bool newSessionRequired,
        out IReadOnlyList<LilithRuntimeContracts.HermesNativeMcpCatalogEntry>? mcpCatalog)
    {
        runtimeGeneration = RuntimeGeneration;
        taskId = TaskId;
        sessionId = SessionId;
        platform = Platform;
        approvalMode = ApprovalMode;
        enabledToolsets = EnabledToolsets;
        toolsets = Toolsets;
        newSessionRequired = NewSessionRequired;
        mcpCatalog = McpCatalog;
    }
}

public sealed record D046HermesNativeToolsSetRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    IReadOnlyList<string> EnabledToolsets,
    D046HermesNativeApprovalMode ApprovalMode,
    LilithRuntimeContracts.HermesNativeSetupSelection? SetupSelection = null,
    LilithRuntimeContracts.HermesNativeMcpSetupSelection? McpSetup = null);

public sealed record D046HermesNativeToolsSetReceipt(
    bool Accepted,
    string Code,
    string ConfigSha256,
    D046HermesNativeToolsCatalog Catalog);

public sealed record D046HermesCompressionReceipt(
    bool Accepted,
    string Code,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    int? BeforeMessages,
    int? AfterMessages,
    long? BeforeTokens,
    long? AfterTokens,
    string Headline,
    string TokenLine,
    string Note,
    bool NoOp,
    bool Aborted,
    D046HermesWorkbenchSnapshot Snapshot);

public sealed record D046HermesUsageProjection(
    bool IsAvailable,
    string StatusCode,
    long Calls,
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    long? ContextUsed,
    long? ContextMaximum,
    double? ContextPercent,
    int Compressions,
    int ActiveSubagents,
    string Model);

public sealed record D046HermesContextCategory(
    string Id,
    string Label,
    string Color,
    long Tokens);

public sealed record D046HermesContextProjection(
    bool IsAvailable,
    string StatusCode,
    long ContextUsed,
    long ContextMaximum,
    double ContextPercent,
    long EstimatedTotal,
    string Model,
    IReadOnlyList<D046HermesContextCategory> Categories);

public sealed record D046HermesLearningItem(
    string Id,
    string Kind,
    string Label,
    string Metadata,
    string BucketLabel);

public sealed record D046HermesLearningProjection(
    bool IsAvailable,
    string StatusCode,
    int Count,
    IReadOnlyList<string> Summary,
    IReadOnlyList<D046HermesLearningItem> RecentItems);

public sealed record D046HermesObservabilitySnapshot(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    D046HermesUsageProjection Usage,
    D046HermesContextProjection Context,
    D046HermesLearningProjection Learning);

public sealed record D046HermesDelegationStatusRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence);

public sealed record D046HermesSubagentSteerRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string SubagentId,
    string Text);

public sealed record D046HermesSubagentInterruptRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string SubagentId);

public enum D046HermesPreDispatchRejectionReason
{
    StaleProjectionCursor,
    StaleTaskMapping,
    TaskNotIdle
}

public sealed class D046HermesPreDispatchRejectedException
    : InvalidOperationException
{
    public D046HermesPreDispatchRejectionReason Reason { get; }

    public D046HermesPreDispatchRejectedException(
        D046HermesPreDispatchRejectionReason reason,
        string message)
        : base(message)
        => Reason = reason;

    public D046HermesPreDispatchRejectedException(
        D046HermesPreDispatchRejectionReason reason,
        string message,
        Exception innerException)
        : base(message, innerException)
        => Reason = reason;
}

public sealed record D046HermesDelegationSnapshot(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long ThroughSequence,
    bool Paused,
    int MaxSpawnDepth,
    int MaxConcurrentChildren,
    IReadOnlyList<D046HermesSubagentCardProjection> ActiveChildren);

public sealed record D046HermesSubagentSteerReceipt(
    bool Queued,
    string Status,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string SubagentId,
    string Text);

public sealed record D046HermesSubagentInterruptReceipt(
    bool Found,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    string SubagentId);

public enum D046HermesVoiceCaptureState
{
    Off = 1,
    Idle = 2,
    Listening = 3,
    Transcribing = 4,
    Quarantined = 5
}

public enum D046HermesVoiceTranscriptKind
{
    Text = 1,
    NoSpeechLimit = 2,
    StopPhrase = 3
}

public enum D046HermesVoiceFailureKind
{
    ProtocolRejected = 1,
    TransportUncertain = 2,
    StateConflict = 3
}

public abstract record D046HermesVoiceEventPayload;

public sealed record D046HermesVoiceStatusPayload(
    D046HermesVoiceCaptureState State)
    : D046HermesVoiceEventPayload;

public sealed record D046HermesVoiceTranscriptPayload(
    D046HermesVoiceTranscriptKind Kind,
    string Text)
    : D046HermesVoiceEventPayload;

public sealed record D046HermesVoiceFailurePayload(
    D046HermesVoiceFailureKind FailureKind,
    string Code,
    string DisplayText,
    bool TransportUncertain)
    : D046HermesVoiceEventPayload;

public sealed record D046HermesVoiceEvent(
    string EventId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long RuntimeSequence,
    DateTimeOffset OccurredUtc,
    string SourceMethod,
    D046HermesVoiceEventPayload Payload,
    string CanonicalPayloadJson);

public sealed record D046HermesVoiceCommandRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId);

public sealed record D046HermesVoiceStatusReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    bool Enabled,
    string RecordKey,
    bool? Available,
    bool? AudioAvailable,
    bool? SttAvailable,
    string Details);

public sealed record D046HermesVoiceOnReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    bool Enabled,
    string RecordKey,
    string StopHint);

public sealed record D046HermesVoiceOffReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    bool Enabled,
    string RecordKey);

public sealed record D046HermesVoiceStartReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    bool Started,
    bool Busy,
    string Reason);

public sealed record D046HermesVoiceStopReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    bool Stopped);

public sealed class D046HermesVoiceOperationException
    : InvalidOperationException
{
    public D046HermesVoiceFailureKind FailureKind { get; }
    public string Code { get; }
    public bool TransportUncertain { get; }

    public D046HermesVoiceOperationException(
        D046HermesVoiceFailureKind failureKind,
        string code,
        string message,
        bool transportUncertain,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        Code = code;
        TransportUncertain = transportUncertain;
    }
}

public sealed record D046HermesRollbackListRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence);

public sealed record D046HermesRollbackDiffRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string CheckpointHash);

public sealed record D046HermesRollbackRestoreRequest(
    string ClientRequestId,
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long LastObservedSequence,
    string CheckpointHash,
    string ConfirmationToken,
    bool Confirm);

public sealed record D046HermesRollbackCheckpoint(
    string Hash,
    DateTimeOffset Timestamp,
    string Message);

public sealed record D046HermesRollbackCatalog(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long ThroughSequence,
    bool Enabled,
    IReadOnlyList<D046HermesRollbackCheckpoint> Checkpoints);

public sealed record D046HermesRollbackDiffReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long ThroughSequence,
    string CheckpointHash,
    bool Succeeded,
    string Stat,
    string Diff,
    bool Truncated,
    string ConfirmationToken);

public sealed record D046HermesRollbackRestoreReceipt(
    string RuntimeGeneration,
    string TaskId,
    string SessionId,
    long ThroughSequence,
    string CheckpointHash,
    bool Succeeded,
    string RestoredTo,
    string Reason,
    int HistoryRemoved,
    bool WorkspaceRestored,
    bool HistoryRewound,
    bool Partial,
    string Error,
    D046HermesWorkbenchSnapshot Snapshot);

public enum D046HermesRollbackFailureKind
{
    ProtocolRejected = 1,
    TransportUncertain = 2,
    StateConflict = 3
}

public sealed class D046HermesRollbackOperationException
    : InvalidOperationException
{
    public D046HermesRollbackFailureKind FailureKind { get; }
    public string Code { get; }
    public bool TransportUncertain { get; }
    public D046HermesRollbackRestoreReceipt? RestoreReceipt { get; }

    public D046HermesRollbackOperationException(
        D046HermesRollbackFailureKind failureKind,
        string code,
        string message,
        bool transportUncertain,
        Exception? innerException = null,
        D046HermesRollbackRestoreReceipt? restoreReceipt = null)
        : base(message, innerException)
    {
        FailureKind = failureKind;
        Code = code;
        TransportUncertain = transportUncertain;
        RestoreReceipt = restoreReceipt;
    }
}

internal readonly record struct D046HermesStreamingModelSelectionDecision(
    bool ShouldSend,
    bool ConfirmExpensiveModel);

internal static class D046HermesModelSelectionPreflight
{
    internal static bool RequiresConservativeConfirmation(
        D046HermesTaskState state)
        => state == D046HermesTaskState.Streaming;

    internal static D046HermesStreamingModelSelectionDecision ResolveStreaming(
        bool userConfirmed)
        => userConfirmed
            ? new(ShouldSend: true, ConfirmExpensiveModel: true)
            : new(ShouldSend: false, ConfirmExpensiveModel: false);
}

public interface ID046HermesWorkbenchProjection : IAsyncDisposable
{
    event EventHandler<D046HermesProjectionEvent>? EventReceived;

    event EventHandler<D046HermesVoiceEvent>? VoiceEventReceived;

    Task<D046HermesWorkbenchSnapshot> GetInitialSnapshotAsync(
        CancellationToken cancellationToken);

    Task<D046HermesWorkbenchSnapshot> OpenTaskAsync(
        string taskId,
        CancellationToken cancellationToken);

    Task<D046HermesWorkbenchSnapshot> CreateTaskAsync(
        CancellationToken cancellationToken);

    Task<D046HermesWorkbenchSnapshot> CreateTaskAsync(
        D046HermesModelOption selection,
        CancellationToken cancellationToken);

    Task<D046HermesDeleteTaskReceipt> DeleteTaskAsync(
        D046HermesDeleteTaskRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesRenameTaskReceipt> RenameTaskAsync(
        D046HermesRenameTaskRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesSubmitReceipt> SubmitAsync(
        D046HermesSubmitRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesSubmitReceipt> SubmitWithImageAsync(
        D046HermesSubmitWithImageRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesCommandReceipt> StopAsync(
        D046HermesStopRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesApprovalResponseReceipt> RespondApprovalAsync(
        D046HermesApprovalResponseRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesClarificationResponseReceipt> RespondClarificationAsync(
        D046HermesClarificationResponseRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesSecureInputResponseReceipt> RespondSecureInputAsync(
        D046HermesSecureInputResponseRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesCompressionReceipt> CompressAsync(
        D046HermesCompressionRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesModelCatalog> GetModelOptionsAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken);

    Task<D046HermesModelCatalog> GetDefaultModelOptionsAsync(
        CancellationToken cancellationToken);

    Task<D046HermesModelSelectionReceipt> SelectModelAsync(
        D046HermesModelSelectionRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesDefaultModelSelectionReceipt> SelectDefaultModelAsync(
        D046HermesDefaultModelSelectionRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesNativeToolsCatalog> GetNativeToolsCatalogAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken);

    Task<D046HermesNativeToolsSetReceipt> SetNativeToolsAsync(
        D046HermesNativeToolsSetRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesObservabilitySnapshot> GetObservabilityAsync(
        string taskId,
        string sessionId,
        CancellationToken cancellationToken);

    Task<D046HermesDelegationSnapshot> GetDelegationStatusAsync(
        D046HermesDelegationStatusRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesSubagentSteerReceipt> SteerSubagentAsync(
        D046HermesSubagentSteerRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesSubagentInterruptReceipt> InterruptSubagentAsync(
        D046HermesSubagentInterruptRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesVoiceStatusReceipt> GetVoiceStatusAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesVoiceOnReceipt> EnableVoiceAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesVoiceOffReceipt> DisableVoiceAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesVoiceStartReceipt> StartVoiceCaptureAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesVoiceStopReceipt> StopVoiceCaptureAsync(
        D046HermesVoiceCommandRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesRollbackCatalog> ListRollbackCheckpointsAsync(
        D046HermesRollbackListRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesRollbackDiffReceipt> GetRollbackDiffAsync(
        D046HermesRollbackDiffRequest request,
        CancellationToken cancellationToken);

    Task<D046HermesRollbackRestoreReceipt> RestoreRollbackAsync(
        D046HermesRollbackRestoreRequest request,
        CancellationToken cancellationToken);
}

internal interface ID052HermesTaskPinProjection
{
    Task<D052HermesTaskPinReceipt> SetTaskPinAsync(
        D052HermesTaskPinRequest request,
        CancellationToken cancellationToken);
}

internal interface ID052HermesTaskPinProtocol
{
    D052HermesTaskPinReceipt SetTaskPin(D052HermesTaskPinRequest request);
}

public sealed record D046HermesRuntimeRequestBinding(
    string Method,
    JsonElement HermesPayload,
    D046HermesRuntimeRequestCorrelation? Correlation = null)
{
    // Local FIFO identity frozen when an approval is bound. It is never sent
    // to Hermes and cannot grant a choice or replace native approval policy.
    public string? ApprovalId { get; init; }

    // Local-only recovery correlation; never a wire permission or readiness claim.
    public bool RequiresCredentialRecoveryAttestation { get; init; }

    // Local acceptance fence for a recovery-only history replacement, never wire data.
    internal D046HermesCredentialRecoveryHistoryFence? CredentialRecoveryHistoryFence { get; init; }

    public override string ToString()
        => $"{nameof(D046HermesRuntimeRequestBinding)} {{ Method = {Method}, "
            + "HermesPayload = <redacted>, Correlation = "
            + $"{Correlation?.ToString() ?? string.Empty} }}";
}

public sealed record D046HermesRuntimeRequestCorrelation(
    string TaskId,
    string RuntimeSessionId,
    string StoredSessionId,
    bool ReplaceHistory = false);

internal sealed record D046HermesCredentialRecoveryHistoryFence(
    string RuntimeGeneration,
    string ProviderId,
    string ModelId,
    long ThroughSequence);

public sealed record D046HermesRuntimeEnvelopeContext(
    Guid GenerationId,
    Guid RequestId,
    long Sequence,
    DateTimeOffset SentAtUtc);

// Runtime composition owns the canonical method tokens and payload schemas.
// This UI-side contract deliberately does not infer them from upstream Hermes JSON.
public interface ID046HermesWorkbenchRuntimeProtocol
{
    string RequiredCapability { get; }

    D046HermesRuntimeRequestBinding BindGetInitialSnapshot();

    D046HermesRuntimeRequestBinding BindOpenTask(string taskId);

    D046HermesRuntimeRequestBinding BindCreateTask();

    D046HermesRuntimeRequestBinding BindSubmit(D046HermesSubmitRequest request);

    D046HermesRuntimeRequestBinding BindStop(D046HermesStopRequest request);

    D046HermesRuntimeRequestBinding BindApprovalResponse(
        D046HermesApprovalResponseRequest request);

    D046HermesRuntimeRequestBinding BindClarificationResponse(
        D046HermesClarificationResponseRequest request);

    D046HermesRuntimeRequestBinding BindSecureInputResponse(
        D046HermesSecureInputResponseRequest request);

    D046HermesWorkbenchSnapshot DecodeSnapshot(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope);

    D046HermesCommandReceipt DecodeCommandReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope);

    D046HermesSubmitReceipt DecodeSubmitReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope);

    D046HermesApprovalResponseReceipt DecodeApprovalResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesClarificationResponseReceipt DecodeClarificationResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesSecureInputResponseReceipt DecodeSecureInputResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    bool TryDecodeEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope,
        out D046HermesProjectionEvent projectionEvent);
}

// Canonical B0 implementations use the exact upstream Hermes request sequence
// instead of treating one Runtime response as a synthetic aggregate snapshot.
public interface ID046CanonicalHermesWorkbenchRuntimeProtocol
    : ID046HermesWorkbenchRuntimeProtocol
{
    string RequiredHermesCommit { get; }

    IReadOnlyList<string> RequiredCapabilities { get; }

    D046HermesRuntimeRequestBinding BindCreateTask(
        string providerId,
        string modelId);

    bool TryCoalesceStartupGlobalSessionInvalidation(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope);

    D046HermesRuntimeRequestBinding BindListTasks();

    D046HermesRuntimeRequestBinding BindHistory(string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindCredentialRecoveryHistory(
        D046HermesModelSelectionRequest request);

    D046HermesRuntimeRequestBinding BindImageAttachBytes(
        D046HermesSubmitWithImageRequest request);

    D046HermesRuntimeRequestBinding BindImageDetach(
        D046HermesSubmitRequest request,
        string attachedPath);

    D046HermesRuntimeRequestBinding BindCompress(
        D046HermesCompressionRequest request);

    D046HermesRuntimeRequestBinding BindModelOptions(
        string taskId,
        string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindSessionlessModelOptions();

    D046HermesRuntimeRequestBinding BindSelectDefaultModel(
        D046HermesDefaultModelSelectionRequest request);

    D046HermesRuntimeRequestBinding BindSelectModel(
        D046HermesModelSelectionRequest request);

    D046HermesDefaultModelSelectionReceipt
        DecodeCanonicalDefaultModelSelectionReceipt(
            LilithRuntimeResponse response,
            D046HermesRuntimeEnvelopeContext envelope,
            D046HermesRuntimeRequestBinding binding);

    D046HermesRuntimeRequestBinding BindNativeToolsCatalog(
        string taskId,
        string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindNativeToolsSet(
        D046HermesNativeToolsSetRequest request);

    D046HermesRuntimeRequestBinding BindUsage(
        string taskId,
        string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindContextBreakdown(
        string taskId,
        string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindLearningFrames(
        string taskId,
        string runtimeSessionId);

    D046HermesRuntimeRequestBinding BindDelegationStatus(
        D046HermesDelegationStatusRequest request);

    D046HermesRuntimeRequestBinding BindSubagentSteer(
        D046HermesSubagentSteerRequest request);

    D046HermesRuntimeRequestBinding BindSubagentInterrupt(
        D046HermesSubagentInterruptRequest request);

    D046HermesRuntimeRequestBinding BindVoiceStatus(
        D046HermesVoiceCommandRequest request);

    D046HermesRuntimeRequestBinding BindVoiceOn(
        D046HermesVoiceCommandRequest request);

    D046HermesRuntimeRequestBinding BindVoiceOff(
        D046HermesVoiceCommandRequest request);

    D046HermesRuntimeRequestBinding BindVoiceStart(
        D046HermesVoiceCommandRequest request);

    D046HermesRuntimeRequestBinding BindVoiceStop(
        D046HermesVoiceCommandRequest request);

    D046HermesRuntimeRequestBinding BindRollbackList(
        D046HermesRollbackListRequest request);

    D046HermesRuntimeRequestBinding BindRollbackDiff(
        D046HermesRollbackDiffRequest request);

    D046HermesRuntimeRequestBinding BindRollbackRestore(
        D046HermesRollbackRestoreRequest request);

    D046HermesRuntimeRequestBinding BindRollbackHistoryRefresh(
        D046HermesRollbackRestoreRequest request);

    D046HermesRuntimeRequestBinding BindDeleteTask(
        D046HermesDeleteTaskRequest request);

    D046HermesRuntimeRequestBinding BindRenameTask(
        D046HermesRenameTaskRequest request);

    D046HermesUsageProjection ReadCurrentUsage(
        string taskId,
        string runtimeSessionId);

    D046HermesWorkbenchSnapshot DecodeCanonicalSnapshotStep(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesCommandReceipt DecodeCanonicalCommandReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesSubmitReceipt DecodeCanonicalSubmitReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesImageAttachReceipt DecodeCanonicalImageAttachReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesImageDetachReceipt DecodeCanonicalImageDetachReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesModelCatalog DecodeCanonicalModelCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesModelSelectionReceipt DecodeCanonicalModelSelectionReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesNativeToolsCatalog DecodeCanonicalNativeToolsCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesNativeToolsSetReceipt DecodeCanonicalNativeToolsSetReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesCompressionReceipt DecodeCanonicalCompressionReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesUsageProjection DecodeCanonicalUsage(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesContextProjection DecodeCanonicalContextBreakdown(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesLearningProjection DecodeCanonicalLearningFrames(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesDelegationSnapshot DecodeCanonicalDelegationStatus(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesSubagentSteerReceipt DecodeCanonicalSubagentSteerReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesSubagentInterruptReceipt DecodeCanonicalSubagentInterruptReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesVoiceStatusReceipt DecodeCanonicalVoiceStatusReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesVoiceOnReceipt DecodeCanonicalVoiceOnReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesVoiceOffReceipt DecodeCanonicalVoiceOffReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesVoiceStartReceipt DecodeCanonicalVoiceStartReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesVoiceStopReceipt DecodeCanonicalVoiceStopReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesRollbackCatalog DecodeCanonicalRollbackCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesRollbackDiffReceipt DecodeCanonicalRollbackDiffReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesRollbackRestoreReceipt DecodeCanonicalRollbackRestoreReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesDeleteTaskReceipt DecodeCanonicalDeleteTaskReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    D046HermesRenameTaskReceipt DecodeCanonicalRenameTaskReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding);

    bool TryDecodeVoiceEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope,
        out D046HermesVoiceEvent voiceEvent);

    Task WaitForNoEffectSessionInfoAsync(
        string taskId,
        string runtimeSessionId,
        CancellationToken cancellationToken);

    bool TryAdmitReadOnlySessionAfterAttestationFailure(
        string taskId,
        string runtimeSessionId,
        D054HermesCredentialCatalog credentialCatalog);
}

internal sealed class D046HermesSessionActivationException : Exception
{
    internal D046HermesSessionActivationException()
        : base("Hermes could not initialize the selected model session.")
    {
    }
}
