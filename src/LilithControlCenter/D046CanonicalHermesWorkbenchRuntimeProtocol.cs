using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

/// <summary>
/// Binds the D046 Workbench projection to the canonical Hermes JSON-RPC
/// contract at the pinned upstream revision. The class intentionally keeps the
/// durable Hermes session key separate from the process-local runtime session.
/// </summary>
public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : ID046CanonicalHermesWorkbenchRuntimeProtocol,
      ID052HermesTaskPinProtocol
{
    public const string PinnedHermesCommit =
        "3c5fd918e3e2537cd74f4f88c990c5de5cbd9f63";

    public const string RuntimeStatusMethod = "runtime.status";
    public const string SessionListMethod = "session.list";
    public const string SessionCreateMethod = "session.create";
    public const string SessionResumeMethod = "session.resume";
    public const string SessionHistoryMethod = "session.history";
    public const string SessionDeleteMethod = "session.delete";
    public const string SessionTitleMethod = "session.title";
    public const string SessionInterruptMethod = "session.interrupt";
    public const string SessionCompressMethod = "session.compress";
    public const string SessionUsageMethod = "session.usage";
    public const string SessionContextBreakdownMethod = "session.context_breakdown";
    public const string LearningFramesMethod = "learning.frames";
    public const string PromptSubmitMethod = "prompt.submit";
    public const string ImageAttachBytesMethod = "image.attach_bytes";
    public const string ImageDetachMethod = "image.detach";
    public const string ModelOptionsMethod = "model.options";
    public const string DefaultModelSetMethod = "model.default.set";
    public const string CredentialStatusMethod = "model.credential_status";
    public const string CredentialReplaceMethod = "model.replace_key";
    public const string CredentialRemoveMethod = "model.remove_key";
    public const string TelegramStatusMethod = "messaging.telegram.status";
    public const string TelegramReplaceMethod = "messaging.telegram.replace";
    public const string TelegramRemoveMethod = "messaging.telegram.remove";
    public const string TelegramPairingListMethod =
        "messaging.telegram.pairing.list";
    public const string TelegramPairingApproveMethod =
        "messaging.telegram.pairing.approve";
    public const string TelegramPairingRevokeMethod =
        "messaging.telegram.pairing.revoke";
    public const string ConfigSetMethod = "config.set";
    public const string NativeToolsCatalogMethod = "tools.catalog";
    public const string NativeToolsSetMethod = "tools.set";
    public const string ApprovalRespondMethod = "approval.respond";
    public const string ClarificationRespondMethod = "clarify.respond";
    public const string SudoRespondMethod = "sudo.respond";
    public const string SecretRespondMethod = "secret.respond";
    public const string DelegationStatusMethod = "delegation.status";
    public const string SubagentSteerMethod = "subagent.steer";
    public const string SubagentInterruptMethod = "subagent.interrupt";
    public const string VoiceToggleMethod = "voice.toggle";
    public const string VoiceRecordMethod = "voice.record";
    public const string RollbackListMethod = "rollback.list";
    public const string RollbackDiffMethod = "rollback.diff";
    public const string RollbackRestoreMethod = "rollback.restore";
    public const string RollbackRuntimeWorkspaceOnlyCapability =
        "rollback.runtime-workspace-only";
    public const string StrictLilithIdentityPackageCapability =
        "identity.strict-lilith-package-v1";
    public const string HermesNativeToolsConfigCapability =
        "hermes.native-tools-config-v1";
    public const string NativeEffectsHermesConfiguredCapability =
        "native.effects.hermes-configured";
    public const string NativeSurfaceAttestationCapability =
        "session.native-surface-attestation";
    public const string HermesEventsLosslessCapability = "hermes.events.lossless";

    private static readonly IReadOnlyList<string> CanonicalCapabilities =
        Array.AsReadOnly(new[]
        {
            RuntimeStatusMethod,
            HermesCapabilityCatalogContract.Method,
            HermesCommandCatalogContract.Method,
            SessionListMethod,
            SessionCreateMethod,
            SessionResumeMethod,
            SessionHistoryMethod,
            SessionDeleteMethod,
            SessionTitleMethod,
            SessionInterruptMethod,
            SessionCompressMethod,
            SessionUsageMethod,
            SessionContextBreakdownMethod,
            LearningFramesMethod,
            PromptSubmitMethod,
            ImageAttachBytesMethod,
            ImageDetachMethod,
            ModelOptionsMethod,
            DefaultModelSetMethod,
            CredentialStatusMethod,
            CredentialReplaceMethod,
            CredentialRemoveMethod,
            ConfigSetMethod,
            NativeToolsCatalogMethod,
            NativeToolsSetMethod,
            ApprovalRespondMethod,
            ClarificationRespondMethod,
            SudoRespondMethod,
            SecretRespondMethod,
            DelegationStatusMethod,
            SubagentSteerMethod,
            SubagentInterruptMethod,
            VoiceToggleMethod,
            VoiceRecordMethod,
            RollbackListMethod,
            RollbackDiffMethod,
            RollbackRestoreMethod,
            RollbackRuntimeWorkspaceOnlyCapability,
            StrictLilithIdentityPackageCapability,
            HermesNativeToolsConfigCapability,
            NativeEffectsHermesConfiguredCapability,
            NativeSurfaceAttestationCapability,
            HermesEventsLosslessCapability
        });

    private readonly object _gate = new();
    private readonly Dictionary<string, TaskBinding> _byTaskId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskBinding> _byStoredSessionId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskBinding> _byRuntimeSessionId =
        new(StringComparer.Ordinal);
    private readonly ID052HermesTaskPinStore? _taskPinStore;
    private readonly HashSet<string> _pinnedTaskIds;
    private HashSet<ModelPair> _sessionlessCertifiedModelPairs = new();
    private List<TaskBinding> _orderedTasks = new();
    private string _runtimeGeneration = string.Empty;
    private string _selectedTaskId = string.Empty;
    private string? _statusAttestedRuntimeSessionId;
    private string? _runtimeInvalidatedNativeToolsSessionId;
    private string _statusText = "disconnected";
    private long _nextProjectionSequence;
    private bool _runtimeReady;
    private bool _nativeApprovalPolicyReady;
    private bool _computerUseEnabled;
    private const int MaximumCompressionFocusUtf8Bytes = 4 * 1024;
    private const int MaximumTaskTitleUtf8Bytes = 4 * 1024;
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private const int MaximumRollbackCheckpoints = 200;
    private const int MaximumRollbackMessageUtf8Bytes = 16 * 1024;
    private const int MaximumRollbackStatUtf8Bytes = 64 * 1024;
    private const int MaximumRollbackDiffUtf8Bytes = 1024 * 1024;
    private const int MaximumRollbackReasonUtf8Bytes = 16 * 1024;
    private const int MaximumNativeSurfaceToolsets = 256;
    private const int MaximumNativeSurfaceTools = 4096;
    private const int MaximumNativeMcpServers = 256;
    private const int MaximumNativeSurfaceNameCharacters = 256;
    private const int MaximumNativeMcpErrorCharacters = 16 * 1024;

    public D046CanonicalHermesWorkbenchRuntimeProtocol()
        : this(taskPinStore: null)
    {
    }

    internal D046CanonicalHermesWorkbenchRuntimeProtocol(
        ID052HermesTaskPinStore? taskPinStore)
    {
        _taskPinStore = taskPinStore;
        if (taskPinStore is null)
        {
            _pinnedTaskIds = new HashSet<string>(StringComparer.Ordinal);
            return;
        }

        var loaded = taskPinStore.Load();
        _pinnedTaskIds = new HashSet<string>(
            loaded.PinnedTaskIds,
            StringComparer.Ordinal);
    }

    public string RequiredCapability => RuntimeStatusMethod;

    public string RequiredHermesCommit => PinnedHermesCommit;

    public IReadOnlyList<string> RequiredCapabilities => CanonicalCapabilities;

    public D046HermesRuntimeRequestBinding BindGetInitialSnapshot()
        => Bind(RuntimeStatusMethod, new { });

    public D046HermesRuntimeRequestBinding BindListTasks()
        => Bind(SessionListMethod, new { limit = 200 });

    D052HermesTaskPinReceipt ID052HermesTaskPinProtocol.SetTaskPin(
        D052HermesTaskPinRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TaskId)
            || request.TaskId.Length > D052HermesTaskPinStore.MaximumTaskIdCharacters
            || request.TaskId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Task ID must be a bounded identity.",
                nameof(request));
        }
        if (request.ExpectedIsPinned == request.IsPinned)
        {
            throw new ArgumentException(
                "Task pin request must change the current state.",
                nameof(request));
        }

        lock (_gate)
        {
            if (!_byTaskId.TryGetValue(request.TaskId, out var task))
                throw new InvalidOperationException("The Hermes task is not known.");
            if (task.IsPinned != request.ExpectedIsPinned)
            {
                throw new InvalidOperationException(
                    "The Hermes task pin state changed before the request was applied.");
            }
            var nextPinnedTaskIds = new HashSet<string>(
                _pinnedTaskIds,
                StringComparer.Ordinal);
            if (request.IsPinned)
            {
                if (!nextPinnedTaskIds.Add(task.TaskId))
                    throw new InvalidOperationException("The task was already pinned.");
            }
            else if (!nextPinnedTaskIds.Remove(task.TaskId))
            {
                throw new InvalidOperationException("The task was not pinned.");
            }

            // Persist before publishing the new UI state. A real store applies
            // one identity mutation against a fresh cross-process read so a
            // second Control Center cannot be overwritten by this cache.
            var committedPinnedTaskIds = _taskPinStore is null
                ? nextPinnedTaskIds
                : _taskPinStore.SetTaskPin(
                    task.TaskId,
                    request.ExpectedIsPinned,
                    request.IsPinned);
            if (committedPinnedTaskIds.Contains(task.TaskId) != request.IsPinned)
            {
                throw new InvalidDataException(
                    "The local task-pin store returned a contradictory mutation.");
            }
            AdoptPinnedTaskIdsLocked(committedPinnedTaskIds);
            return new D052HermesTaskPinReceipt(
                task.TaskId,
                request.IsPinned,
                SnapshotLocked());
        }
    }

    public D046HermesRuntimeRequestBinding BindOpenTask(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task ID must not be empty.", nameof(taskId));

        lock (_gate)
        {
            if (!_byTaskId.TryGetValue(taskId, out var task))
                throw new InvalidOperationException("The Hermes task is not known.");
            return Bind(
                SessionResumeMethod,
                new
                {
                    session_id = task.StoredSessionId,
                    omit_messages = true
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindCreateTask()
        => Bind(SessionCreateMethod, new { });

    public D046HermesRuntimeRequestBinding BindCreateTask(
        string providerId,
        string modelId)
    {
        var provider = RequireIdentity(providerId, nameof(providerId));
        var model = RequireIdentity(modelId, nameof(modelId));
        return Bind(SessionCreateMethod, new { provider, model });
    }

    public D046HermesRuntimeRequestBinding BindDeleteTask(
        D046HermesDeleteTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ValidateTaskMutationRequestLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            var task = RequireTaskMutationTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            return Bind(
                SessionDeleteMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    stored_session_id = task.StoredSessionId
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindRenameTask(
        D046HermesRenameTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ValidateTaskMutationRequestLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            var task = RequireTaskMutationTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var title = ValidateTaskTitle(request.Title);
            return Bind(
                SessionTitleMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    title
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindHistory(string runtimeSessionId)
    {
        RequireIdentity(runtimeSessionId, nameof(runtimeSessionId));
        lock (_gate)
        {
            if (!_byRuntimeSessionId.TryGetValue(runtimeSessionId, out var task)
                || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Hermes runtime session is not the selected task.");
            }

            return Bind(
                SessionHistoryMethod,
                new { session_id = runtimeSessionId },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindCredentialRecoveryHistory(
        D046HermesModelSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.RuntimeGeneration, request.TaskId, request.SessionId);
            // session.info has advanced the cursor since the model-selection request.
            // Freeze the newly attested state, not that request's pre-recovery cursor.
            var fence = new D046HermesCredentialRecoveryHistoryFence(
                request.RuntimeGeneration, request.ProviderId, request.ModelId,
                _nextProjectionSequence);
            var correlation = Correlation(task) with { ReplaceHistory = true };
            RequireCredentialRecoveryHistoryTaskLocked(correlation, fence);
            return Bind(SessionHistoryMethod, new { session_id = task.RuntimeSessionId },
                correlation) with { CredentialRecoveryHistoryFence = fence };
        }
    }

    public D046HermesRuntimeRequestBinding BindImageAttachBytes(
        D046HermesSubmitWithImageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Submit);
        ArgumentNullException.ThrowIfNull(request.Image);
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.Submit.RuntimeGeneration,
                request.Submit.TaskId,
                request.Submit.SessionId);
            RequirePromptReadyTask(task);
            if (task.PendingAttachedImagePaths.Count != 0)
            {
                throw new InvalidOperationException(
                    "The Hermes session already has a pending image attachment.");
            }
            ValidateImageUpload(request.Image);
            return Bind(
                ImageAttachBytesMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    content_base64 = Convert.ToBase64String(request.Image.Content.Span),
                    filename = request.Image.FileName
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindImageDetach(
        D046HermesSubmitRequest request,
        string attachedPath)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            RequirePromptReadyTask(task);
            if (string.IsNullOrWhiteSpace(attachedPath)
                || attachedPath.Any(char.IsControl)
                || !task.PendingAttachedImagePaths.Contains(attachedPath))
            {
                throw new InvalidOperationException(
                    "The Hermes image detach path was not issued by this session.");
            }
            return Bind(
                ImageDetachMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    path = attachedPath
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindModelOptions(
        string taskId,
        string runtimeSessionId)
    {
        RequireIdentity(taskId, nameof(taskId));
        RequireIdentity(runtimeSessionId, nameof(runtimeSessionId));
        lock (_gate)
        {
            var task = RequireModelSelectionTaskLocked(
                _runtimeGeneration, taskId, runtimeSessionId);
            return Bind(
                ModelOptionsMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    explicit_only = true,
                    include_unconfigured = false,
                    refresh = false
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindSessionlessModelOptions()
        => Bind(
            ModelOptionsMethod,
            new
            {
                explicit_only = true,
                include_unconfigured = false,
                refresh = false
            });

    public D046HermesRuntimeRequestBinding BindSelectDefaultModel(
        D046HermesDefaultModelSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (!string.Equals(
                    request.RuntimeGeneration,
                    _runtimeGeneration,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Runtime generation changed before default model selection.");
            }
            var provider = RequireIdentity(
                request.ProviderId,
                nameof(request.ProviderId));
            var model = RequireIdentity(request.ModelId, nameof(request.ModelId));
            if (!_sessionlessCertifiedModelPairs.Contains(
                    new ModelPair(provider, model)))
            {
                throw new InvalidOperationException(
                    "The default provider/model pair was not certified by sessionless model.options.");
            }
            if (string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                throw new ArgumentException(
                    "Client request ID must not be empty.",
                    nameof(request));
            }

            return Bind(
                DefaultModelSetMethod,
                new
                {
                    provider,
                    model,
                    confirm_expensive_model = request.ConfirmExpensiveModel
                });
        }
    }

    public D046HermesRuntimeRequestBinding BindNativeToolsCatalog(
        string taskId,
        string runtimeSessionId)
    {
        RequireIdentity(taskId, nameof(taskId));
        RequireIdentity(runtimeSessionId, nameof(runtimeSessionId));
        lock (_gate)
        {
            var task = RequireNativeToolsCatalogTaskLocked(taskId, runtimeSessionId);
            return Bind(
                NativeToolsCatalogMethod,
                new { session_id = task.RuntimeSessionId },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindUsage(
        string taskId,
        string runtimeSessionId)
        => BindSelectedSessionRead(
            SessionUsageMethod,
            taskId,
            runtimeSessionId,
            includeFrameShape: false);

    public D046HermesRuntimeRequestBinding BindContextBreakdown(
        string taskId,
        string runtimeSessionId)
        => BindSelectedSessionRead(
            SessionContextBreakdownMethod,
            taskId,
            runtimeSessionId,
            includeFrameShape: false);

    public D046HermesRuntimeRequestBinding BindLearningFrames(
        string taskId,
        string runtimeSessionId)
        => BindSelectedSessionRead(
            LearningFramesMethod,
            taskId,
            runtimeSessionId,
            includeFrameShape: true);

    public D046HermesRuntimeRequestBinding BindDelegationStatus(
        D046HermesDelegationStatusRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireDelegationCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateDelegationCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            return Bind(
                DelegationStatusMethod,
                new { session_id = task.RuntimeSessionId },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindSubagentSteer(
        D046HermesSubagentSteerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireDelegationCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateDelegationCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            return Bind(
                SubagentSteerMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    subagent_id = RequireSubagentId(request.SubagentId),
                    text = RequireControlText(request.Text, "text")
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindSubagentInterrupt(
        D046HermesSubagentInterruptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireDelegationCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateDelegationCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            return Bind(
                SubagentInterruptMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    subagent_id = RequireSubagentId(request.SubagentId)
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindVoiceStatus(
        D046HermesVoiceCommandRequest request)
        => BindVoiceToggle(request, "status");

    public D046HermesRuntimeRequestBinding BindVoiceOn(
        D046HermesVoiceCommandRequest request)
        => BindVoiceToggle(request, "on");

    public D046HermesRuntimeRequestBinding BindVoiceOff(
        D046HermesVoiceCommandRequest request)
        => BindVoiceToggle(request, "off");

    public D046HermesRuntimeRequestBinding BindVoiceStart(
        D046HermesVoiceCommandRequest request)
        => BindVoiceRecord(request, "start");

    public D046HermesRuntimeRequestBinding BindVoiceStop(
        D046HermesVoiceCommandRequest request)
        => BindVoiceRecord(request, "stop");

    private D046HermesRuntimeRequestBinding BindVoiceToggle(
        D046HermesVoiceCommandRequest request,
        string action)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireVoiceCommandTaskLocked(request);
            return Bind(
                VoiceToggleMethod,
                new
                {
                    action,
                    session_id = task.RuntimeSessionId
                },
                Correlation(task));
        }
    }

    private D046HermesRuntimeRequestBinding BindVoiceRecord(
        D046HermesVoiceCommandRequest request,
        string action)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireVoiceCommandTaskLocked(request);
            return Bind(
                VoiceRecordMethod,
                new
                {
                    action,
                    session_id = task.RuntimeSessionId
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindRollbackList(
        D046HermesRollbackListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireRollbackCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateRollbackCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            return Bind(
                RollbackListMethod,
                new { session_id = task.RuntimeSessionId },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindRollbackDiff(
        D046HermesRollbackDiffRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireRollbackCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateRollbackCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            var hash = RequireRollbackHash(request.CheckpointHash, "checkpoint hash");
            if (!task.RollbackCatalogAvailable
                || !task.RollbackCheckpointHashes.Contains(hash))
            {
                throw RollbackStateConflict(
                    "rollback.checkpoint_not_catalogued",
                    "The rollback checkpoint is not in the latest catalog.");
            }
            return Bind(
                RollbackDiffMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    hash
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindRollbackRestore(
        D046HermesRollbackRestoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireRollbackCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            ValidateRollbackCommandLocked(
                request.ClientRequestId,
                request.LastObservedSequence);
            if (task.State is not (D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed))
            {
                throw RollbackStateConflict(
                    "rollback.task_not_idle",
                    "Rollback cannot restore while the task is active.");
            }
            if (!request.Confirm)
            {
                throw RollbackStateConflict(
                    "rollback.confirmation_required",
                    "Rollback requires explicit confirmation before dispatch.");
            }
            var hash = RequireRollbackHash(request.CheckpointHash, "checkpoint hash");
            var token = RequireIdentity(
                request.ConfirmationToken,
                "confirmation token");
            if (!task.RollbackCatalogAvailable
                || !task.RollbackCheckpointHashes.Contains(hash)
                || !string.Equals(task.RollbackConfirmationHash, hash,
                    StringComparison.Ordinal)
                || !string.Equals(task.RollbackConfirmationToken, token,
                    StringComparison.Ordinal))
            {
                throw RollbackStateConflict(
                    "rollback.confirmation_stale",
                    "The rollback confirmation no longer matches the latest diff.");
            }
            return Bind(
                RollbackRestoreMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    hash,
                    confirmation_token = token
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindRollbackHistoryRefresh(
        D046HermesRollbackRestoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireRollbackCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            return Bind(
                SessionHistoryMethod,
                new { session_id = task.RuntimeSessionId },
                Correlation(task) with { ReplaceHistory = true });
        }
    }

    public D046HermesUsageProjection ReadCurrentUsage(
        string taskId,
        string runtimeSessionId)
    {
        RequireIdentity(taskId, nameof(taskId));
        RequireIdentity(runtimeSessionId, nameof(runtimeSessionId));
        lock (_gate)
            return RequireSelectedTaskLocked(taskId, runtimeSessionId).Usage;
    }

    public D046HermesRuntimeRequestBinding BindSelectModel(
        D046HermesModelSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireModelSelectionTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            if (task.State is not (D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed
                    or D046HermesTaskState.Streaming))
            {
                throw new InvalidOperationException(
                    "The Hermes task cannot switch models in its current state.");
            }
            var provider = RequireIdentity(request.ProviderId, nameof(request.ProviderId));
            var model = RequireIdentity(request.ModelId, nameof(request.ModelId));
            if (!task.CertifiedModelPairs.Contains(new ModelPair(provider, model)))
            {
                throw new InvalidOperationException(
                    "The Hermes provider/model pair was not certified by model.options.");
            }

            var recovering = task.CredentialUnavailable;
            if (recovering)
            {
                ValidateTaskMutationRequestLocked(request.ClientRequestId, request.LastObservedSequence);
                if (request.LastObservedSequence != _nextProjectionSequence)
                    throw new InvalidOperationException("The recovery projection cursor changed.");
                task.PendingCredentialRecovery = new ModelPair(provider, model);
                task.NoEffectAttestation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            return Bind(
                ConfigSetMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    provider,
                    model,
                    confirm_expensive_model = request.ConfirmExpensiveModel
                },
                Correlation(task)) with { RequiresCredentialRecoveryAttestation = recovering };
        }
    }

    public D046HermesRuntimeRequestBinding BindNativeToolsSet(
        D046HermesNativeToolsSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            if (task.State is not (D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed))
            {
                throw new InvalidOperationException(
                    "Hermes native tools cannot be changed while the task is active.");
            }
            var catalog = task.NativeToolsCatalog
                ?? throw new InvalidOperationException(
                    "The Hermes native tool catalog has not been certified.");
            var enabled = ValidateNativeToolsSelection(
                request.EnabledToolsets,
                catalog.Toolsets.Select(value => value.Name));
            if (!IsSupportedNativeApprovalMode(request.ApprovalMode))
                throw new ArgumentException("Approval mode was rejected.", nameof(request));
            if (request.SetupSelection is not null && request.McpSetup is not null)
                throw new InvalidOperationException("原生供應商與 MCP 設定必須分開提交。");

            var payload = new Dictionary<string, object>
            {
                ["session_id"] = task.RuntimeSessionId,
                ["enabled_toolsets"] = enabled,
                ["approval_mode"] = NativeApprovalModeWire(request.ApprovalMode)
            };
            if (request.SetupSelection is { } setup)
            {
                if (request.ApprovalMode != catalog.ApprovalMode
                    || !new HashSet<string>(catalog.EnabledToolsets, StringComparer.Ordinal).SetEquals(enabled)
                    || !catalog.Toolsets.Any(row => row.Name == setup.Toolset
                        && row.SetupProviders?.Any(p => p.Name == setup.Provider
                            && p.SetupSupport is "provider_selection" or "model_selection"
                            && (p.SetupSupport != "model_selection" || setup.Model is not null)
                            && (setup.Model is null || p.Models?.Contains(setup.Model, StringComparer.Ordinal) == true)) == true))
                    throw new InvalidOperationException("原生供應商設定不能混合工具開關或核准變更，且必須來自目前目錄。");
                payload["setup_selection"] = setup;
            }
            if (request.McpSetup is { } mcpSetup)
            {
                if (request.ApprovalMode != catalog.ApprovalMode
                    || !new HashSet<string>(catalog.EnabledToolsets, StringComparer.Ordinal).SetEquals(enabled)
                    || !HermesNativeToolsContract.McpSetupMatchesCatalog(mcpSetup, catalog.McpCatalog, requireRegistered: false))
                    throw new InvalidOperationException("MCP 設定必須來自目前原生目錄，且不能混合其他工具或核准變更。");
                payload["mcp_setup"] = mcpSetup;
            }

            return Bind(
                NativeToolsSetMethod,
                payload,
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindSubmit(
        D046HermesSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.UiLocale is not (null or "zh-TW" or "zh-CN" or "ja-JP" or "en-US"))
            throw new ArgumentException("Unsupported Workbench UI locale.", nameof(request));
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            RequirePromptReadyTask(task);
            if (string.IsNullOrWhiteSpace(request.Text) && task.PendingAttachedImagePaths.Count == 0)
                throw new ArgumentException("Message text must not be empty.", nameof(request));
            if (task.SubmitPending || task.AwaitingQueuedTurn)
                throw new InvalidOperationException("A Hermes prompt submit is already pending.");
            task.SubmitPending = true;
            task.PendingTurnTerminalObserved = false;
            task.PendingTurnId = string.Empty;
            task.PendingUserMessageId = string.Empty;
            task.PendingAssistantMessageId = string.Empty;
            var payload = new Dictionary<string, object>
            {
                ["session_id"] = task.RuntimeSessionId,
                ["text"] = request.Text
            };
            if (request.UiLocale is not null) payload["ui_locale"] = request.UiLocale;
            return Bind(PromptSubmitMethod, payload, Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindStop(D046HermesStopRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireInterruptibleTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            task.State = D046HermesTaskState.Stopping;
            return Bind(
                SessionInterruptMethod,
                new { session_id = task.RuntimeSessionId },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindApprovalResponse(
        D046HermesApprovalResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireApprovalResponseTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            if (task.State != D046HermesTaskState.WaitingForApproval
                || task.PendingApprovals.Count == 0
                || task.ApprovalResponseInFlight
                || task.ApprovalResponseQuarantined)
            {
                throw new InvalidOperationException(
                    "The Hermes native approval is not available for a response.");
            }
            var pending = task.PendingApprovals.Peek();
            if (!pending.Choices.Contains(request.Choice, StringComparer.Ordinal))
                throw new ArgumentException("Approval choice was rejected.", nameof(request));

            task.ApprovalResponseInFlight = true;
            return Bind(
                ApprovalRespondMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    choice = request.Choice
                },
                Correlation(task)) with { ApprovalId = pending.ApprovalId };
        }
    }

    public D046HermesRuntimeRequestBinding BindClarificationResponse(
        D046HermesClarificationResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireApprovalResponseTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var pending = task.PendingClarification;
            if (task.State != D046HermesTaskState.WaitingForClarification
                || pending is null
                || task.ClarificationResponseInFlight
                || !string.Equals(
                    pending.ClarificationId,
                    request.ClarificationId,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(request.Answer))
            {
                throw new InvalidOperationException(
                    "The Hermes clarification is not available for a response.");
            }
            task.ClarificationResponseInFlight = true;
            task.ClarificationResponseRequestId = pending.ClarificationId;
            return Bind(
                ClarificationRespondMethod,
                new
                {
                    session_id = task.RuntimeSessionId,
                    request_id = pending.ClarificationId,
                    answer = request.Answer
                },
                Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindSecureInputResponse(
        D046HermesSecureInputResponseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireApprovalResponseTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId,
                request.LastObservedSequence);
            var pending = task.PendingSecureInput;
            if (task.State != D046HermesTaskState.WaitingForSecureInput
                || pending is null
                || task.SecureInputResponseInFlight
                || pending.InputKind != request.InputKind
                || !string.Equals(
                    pending.RequestId,
                    request.RequestId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Hermes secure input is not available for a response.");
            }
            task.SecureInputResponseInFlight = true;
            task.SecureInputResponseRequestId = pending.RequestId;
            task.SecureInputResponseKind = pending.InputKind;
            var method = request.InputKind == D046HermesSecureInputKind.SudoPassword
                ? SudoRespondMethod
                : SecretRespondMethod;
            var payload = request.InputKind == D046HermesSecureInputKind.SudoPassword
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["session_id"] = task.RuntimeSessionId,
                    ["request_id"] = pending.RequestId,
                    ["password"] = request.Value
                }
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["session_id"] = task.RuntimeSessionId,
                    ["request_id"] = pending.RequestId,
                    ["value"] = request.Value
                };
            return Bind(method, payload, Correlation(task));
        }
    }

    public D046HermesRuntimeRequestBinding BindCompress(
        D046HermesCompressionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            var task = RequireCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
            if (task.State != D046HermesTaskState.Idle)
            {
                throw new InvalidOperationException(
                    "The Hermes task cannot compress while it is not idle.");
            }

            var focusTopic = ValidateCompressionFocus(request.FocusTopic);
            return focusTopic.Length == 0
                ? Bind(
                    SessionCompressMethod,
                    new { session_id = task.RuntimeSessionId },
                    Correlation(task))
                : Bind(
                    SessionCompressMethod,
                    new
                    {
                        session_id = task.RuntimeSessionId,
                        focus_topic = focusTopic
                    },
                    Correlation(task));
        }
    }

    public D046HermesWorkbenchSnapshot DecodeCanonicalSnapshotStep(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
            throw new InvalidDataException("The Hermes response method was not correlated.");
        if (!response.Succeeded && response.Method is SessionCreateMethod or SessionResumeMethod)
        {
            lock (_gate)
            {
                ObserveGenerationLocked(envelope);
                var failure = RequireObject(response.HermesPayload, "session transition failure");
                ValidateExactProperties(failure, new[] { "code", "message", "hermesCode" }, "session transition failure");
                var code = RequireString(failure, "code");
                _ = RequireString(failure, "message");
                if (failure.GetProperty("hermesCode").ValueKind == JsonValueKind.Null
                    && code is "runtime.image_lifecycle_blocked" or "runtime.session_lifecycle_busy"
                        or "runtime.voice_lifecycle_blocked" or "runtime.checkpoint_restore_active")
                    throw new D046HermesSessionTransitionRejectedException(code);
            }
        }
        RequireSucceeded(response);

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            switch (response.Method)
            {
                case RuntimeStatusMethod:
                    DecodeRuntimeStatusLocked(response.HermesPayload, envelope);
                    break;
                case SessionListMethod:
                    DecodeSessionListLocked(response.HermesPayload, envelope);
                    break;
                case SessionCreateMethod:
                    DecodeSessionCreateLocked(response.HermesPayload, envelope, binding);
                    break;
                case SessionResumeMethod:
                    DecodeSessionResumeLocked(response.HermesPayload, envelope, binding);
                    break;
                case SessionHistoryMethod:
                    DecodeSessionHistoryLocked(response.HermesPayload, envelope, binding);
                    break;
                default:
                    throw new InvalidDataException(
                        "The response is not a canonical snapshot step.");
            }

            return SnapshotLocked();
        }
    }

    public D046HermesWorkbenchSnapshot DecodeSnapshot(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope)
        => throw new InvalidOperationException(
            "Canonical Hermes snapshots require the correlated multi-step binding.");

    public D046HermesDeleteTaskReceipt DecodeCanonicalDeleteTaskReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, SessionDeleteMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, SessionDeleteMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes task-delete response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!response.Succeeded)
            {
                var code = DecodeRuntimeFailureCode(response.HermesPayload);
                var recoveryRequired = code is
                    "runtime.session_delete_delete_not_sent"
                    or "runtime.session_delete_delete_rejected";
                var transportUncertain = code is
                    "runtime.session_delete_close_unverified"
                    or "runtime.session_delete_outcome_uncertain";
                return new D046HermesDeleteTaskReceipt(
                    Deleted: false,
                    code,
                    recoveryRequired,
                    transportUncertain,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    SnapshotLocked());
            }

            var result = RequireObject(response.HermesPayload, "session.delete result");
            ValidateExactProperties(
                result,
                new[] { "closed", "deleted" },
                "session.delete result");
            _ = RequireBoolean(result, "closed");
            var deleted = RequireIdentity(
                RequireString(result, "deleted"),
                "deleted");
            if (!string.Equals(deleted, task.StoredSessionId, StringComparison.Ordinal))
                throw new InvalidDataException("session.delete removed a different durable session.");

            var taskId = task.TaskId;
            var runtimeSessionId = task.RuntimeSessionId;
            RemoveTaskLocked(task);
            _pinnedTaskIds.Remove(taskId);
            var localPreferenceWarningCode = string.Empty;
            if (_taskPinStore is not null)
            {
                try
                {
                    var committedPinnedTaskIds =
                        _taskPinStore.RemoveDeletedTask(taskId);
                    if (committedPinnedTaskIds.Contains(taskId))
                    {
                        throw new InvalidDataException(
                            "The local task-pin cleanup retained the deleted task.");
                    }
                    AdoptPinnedTaskIdsLocked(committedPinnedTaskIds);
                }
                catch (Exception)
                {
                    // Hermes deletion is already authoritative. A local
                    // presentation-preference cleanup failure is observable,
                    // but can never rewrite Deleted=true into a false failure.
                    localPreferenceWarningCode =
                        D046HermesDeleteTaskReceipt.TaskPinCleanupPendingWarningCode;
                }
            }
            return new D046HermesDeleteTaskReceipt(
                Deleted: true,
                "deleted",
                RecoveryRequired: false,
                TransportUncertain: false,
                _runtimeGeneration,
                taskId,
                runtimeSessionId,
                SnapshotLocked(),
                localPreferenceWarningCode);
        }
    }

    public D046HermesRenameTaskReceipt DecodeCanonicalRenameTaskReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, SessionTitleMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, SessionTitleMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes task-title response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var request = RequireObject(binding.HermesPayload, "session.title request");
            var requestedTitle = RequireString(request, "title");
            if (!response.Succeeded)
            {
                var code = DecodeRuntimeFailureCode(response.HermesPayload);
                return new D046HermesRenameTaskReceipt(
                    Accepted: false,
                    code,
                    Pending: false,
                    TransportUncertain: string.Equals(
                        code,
                        "runtime.session_title_outcome_uncertain",
                        StringComparison.Ordinal),
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    task.Title,
                    SnapshotLocked());
            }

            var result = RequireObject(response.HermesPayload, "session.title result");
            ValidateExactProperties(
                result,
                new[] { "pending", "title" },
                "session.title result");
            var pending = RequireBoolean(result, "pending");
            var title = RequireString(result, "title");
            if (!string.Equals(title, requestedTitle, StringComparison.Ordinal))
                throw new InvalidDataException("session.title did not echo the requested title.");
            task.Title = title;
            return new D046HermesRenameTaskReceipt(
                Accepted: true,
                pending ? "rename_pending" : "renamed",
                pending,
                TransportUncertain: false,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                title,
                SnapshotLocked());
        }
    }

    public D046HermesCommandReceipt DecodeCanonicalCommandReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
            throw new InvalidDataException("The Hermes receipt method was not correlated.");

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var correlation = RequireCorrelation(binding);
            var task = RequireCorrelatedTaskLocked(correlation);
            if (response.Method == PromptSubmitMethod)
            {
                throw new InvalidDataException(
                    "Hermes prompt submissions require the typed submit receipt decoder.");
            }
            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                var code = RequireString(failure, "code");
                _ = RequireString(failure, "message");
                if (!failure.TryGetProperty("hermesCode", out var hermesCode)
                    || hermesCode.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number)
                    || (hermesCode.ValueKind == JsonValueKind.Number
                        && !hermesCode.TryGetInt32(out _)))
                {
                    throw new InvalidDataException(
                        "The Runtime failure Hermes code was malformed.");
                }

                if (response.Method == SessionInterruptMethod)
                {
                    if (code is "runtime.session_interrupt_in_flight"
                        or "runtime.session_interrupt_deferred")
                    {
                        // A second Stop can race the exact in-flight Stop, or
                        // an earlier protected mutation can exceed the bounded
                        // Stop window before Hermes dispatch. Neither receipt
                        // represents a failed interrupt. Preserve Stopping (or
                        // an already-adopted Idle state), approvals, and the
                        // ability to retry without quarantining the session.
                        return new D046HermesCommandReceipt(
                            Accepted: false,
                            code,
                            _runtimeGeneration,
                            task.TaskId,
                            task.RuntimeSessionId);
                    }
                    task.PendingApprovals.Clear();
                    task.ApprovalResponseInFlight = false;
                    task.ApprovalResponseQuarantined = true;
                    task.State = D046HermesTaskState.Failed;
                    task.NativeToolsConfigStaleTurnActive = false;
                }
                return new D046HermesCommandReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId);
            }

            var result = RequireObject(response.HermesPayload, "Hermes command result");
            var status = RequireString(result, "status");
            var expected = response.Method switch
            {
                SessionInterruptMethod => "interrupted",
                _ => throw new InvalidDataException(
                    "The response is not a canonical Workbench command.")
            };
            if (!string.Equals(status, expected, StringComparison.Ordinal))
                throw new InvalidDataException("The Hermes command status was rejected.");

            task.State = D046HermesTaskState.Idle;
            if (response.Method == SessionInterruptMethod)
            {
                task.PendingApprovals.Clear();
                task.ApprovalResponseInFlight = false;
                task.ApprovalResponseQuarantined = false;
                task.NativeToolsConfigStaleTurnActive = false;
            }
            return new D046HermesCommandReceipt(
                Accepted: true,
                status,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId);
        }
    }

    public D046HermesSubmitReceipt DecodeCanonicalSubmitReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, PromptSubmitMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, PromptSubmitMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes prompt receipt was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!task.SubmitPending)
                throw new InvalidDataException("The Hermes prompt receipt had no pending submit.");
            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                var code = RequireString(failure, "code");
                _ = RequireString(failure, "message");
                if (!failure.TryGetProperty("hermesCode", out var hermesCode)
                    || hermesCode.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number)
                    || (hermesCode.ValueKind == JsonValueKind.Number
                        && !hermesCode.TryGetInt32(out _)))
                {
                    throw new InvalidDataException(
                        "The Runtime failure Hermes code was malformed.");
                }
                ClearPendingSubmitLocked(task);
                return new D046HermesSubmitReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    TurnId: string.Empty,
                    UserMessageId: string.Empty,
                    AssistantMessageId: string.Empty);
            }

            var result = HermesTurnCorrelationContract.DecodePromptSubmitResult(
                RequireObject(response.HermesPayload, "prompt.submit result"));
            if (!result.CreatesTurn)
            {
                var queuedTurnAlreadyObserved = task.PendingTurnId.Length != 0;
                if (queuedTurnAlreadyObserved
                    && result.Disposition != HermesPromptSubmitDisposition.Queued)
                {
                    throw new InvalidDataException(
                        "The Hermes no-turn receipt followed a newly claimed turn event.");
                }
                ClearPendingSubmitLocked(task);
                task.AwaitingQueuedTurn =
                    result.Disposition == HermesPromptSubmitDisposition.Queued
                    && !queuedTurnAlreadyObserved;
                if (task.AwaitingQueuedTurn
                    || result.Disposition is HermesPromptSubmitDisposition.Steered
                        or HermesPromptSubmitDisposition.Redirected)
                {
                    task.State = D046HermesTaskState.Streaming;
                }
                return new D046HermesSubmitReceipt(
                    Accepted: true,
                    PromptDispositionCode(result.Disposition),
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    TurnId: string.Empty,
                    UserMessageId: string.Empty,
                    AssistantMessageId: string.Empty);
            }
            if ((task.PendingTurnId.Length != 0
                    && !string.Equals(
                        task.PendingTurnId,
                        result.TurnId,
                        StringComparison.Ordinal))
                || (task.PendingUserMessageId.Length != 0
                    && !string.Equals(
                        task.PendingUserMessageId,
                        result.UserMessageId,
                        StringComparison.Ordinal))
                || (task.PendingAssistantMessageId.Length != 0
                    && !string.Equals(
                        task.PendingAssistantMessageId,
                        result.AssistantMessageId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "The Hermes prompt receipt disagreed with the accepted turn event.");
            }

            var eventClaimedTurn = string.Equals(
                task.PendingTurnId,
                result.TurnId,
                StringComparison.Ordinal);
            if (task.KnownTurnIds.Contains(result.TurnId) && !eventClaimedTurn)
            {
                throw new InvalidDataException(
                    "The Hermes prompt receipt reused an existing turn identity.");
            }
            if (task.ClaimedAuxiliaryAssistantMessageIds.Contains(
                    (result.TurnId, result.UserMessageId))
                || task.ClaimedAuxiliaryAssistantMessageIds.Contains(
                    (result.TurnId, result.AssistantMessageId)))
            {
                throw new InvalidDataException(
                    "The Hermes prompt receipt aliased an auxiliary assistant identity.");
            }

            task.KnownTurnIds.Add(result.TurnId);
            if (task.ClaimedUserMessageIds.TryGetValue(
                    result.TurnId,
                    out var claimedUserMessageId)
                && !string.Equals(
                    claimedUserMessageId,
                    result.UserMessageId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Hermes prompt receipt changed its user-message identity.");
            }
            task.ClaimedUserMessageIds[result.TurnId] = result.UserMessageId;
            if (task.ClaimedAssistantMessageIds.TryGetValue(
                    result.TurnId,
                    out var claimedAssistantMessageId)
                && !string.Equals(
                    claimedAssistantMessageId,
                    result.AssistantMessageId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Hermes prompt receipt changed its assistant-message identity.");
            }
            task.ClaimedAssistantMessageIds[result.TurnId] =
                result.AssistantMessageId;
            task.PendingAttachedImagePaths.Clear();
            task.State = task.PendingTurnTerminalObserved
                ? D046HermesTaskState.Idle
                : D046HermesTaskState.Streaming;
            task.AwaitingQueuedTurn = false;
            ClearPendingSubmitLocked(task);
            return new D046HermesSubmitReceipt(
                Accepted: true,
                "streaming",
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                result.TurnId,
                result.UserMessageId,
                result.AssistantMessageId);
        }
    }

    private static string PromptDispositionCode(HermesPromptSubmitDisposition disposition)
        => disposition switch
        {
            HermesPromptSubmitDisposition.Queued => "queued",
            HermesPromptSubmitDisposition.Steered => "steered",
            HermesPromptSubmitDisposition.Redirected => "redirected",
            HermesPromptSubmitDisposition.VoiceStopped => "voice_stopped",
            HermesPromptSubmitDisposition.Streaming => "streaming",
            _ => throw new InvalidDataException(
                "The Hermes prompt disposition was not recognized.")
        };

    public D046HermesImageAttachReceipt DecodeCanonicalImageAttachReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ImageAttachBytesMethod, StringComparison.Ordinal)
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The image attach response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!response.Succeeded)
            {
                var code = DecodeRuntimeFailureCode(response.HermesPayload);
                return new D046HermesImageAttachReceipt(
                    Attached: false,
                    code,
                    Path: string.Empty,
                    Count: 0,
                    Bytes: 0,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId);
            }

            var result = RequireObject(response.HermesPayload, "image.attach_bytes result");
            if (!RequireBoolean(result, "attached"))
                throw new InvalidDataException("Hermes did not attach the image.");
            var path = RequireString(result, "path");
            var count = RequireNonNegativeInteger(result, "count");
            if (count < 1)
                throw new InvalidDataException("Hermes image count was rejected.");
            var bytes = RequireNonNegativeLong(result, "bytes");
            if (bytes <= 0 || bytes != AttachedRequestByteLength(binding.HermesPayload))
                throw new InvalidDataException("Hermes image byte count was rejected.");
            if (!task.PendingAttachedImagePaths.Add(path))
                throw new InvalidDataException("Hermes reused an attached image path.");

            return new D046HermesImageAttachReceipt(
                Attached: true,
                Code: "attached",
                path,
                count,
                bytes,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId);
        }
    }

    public D046HermesImageDetachReceipt DecodeCanonicalImageDetachReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ImageDetachMethod, StringComparison.Ordinal)
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The image detach response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var request = RequireObject(binding.HermesPayload, "image.detach request");
            var path = RequireString(request, "path");
            if (!task.PendingAttachedImagePaths.Contains(path))
                throw new InvalidDataException("The image detach reservation was stale.");
            if (!response.Succeeded)
            {
                var code = DecodeRuntimeFailureCode(response.HermesPayload);
                return new D046HermesImageDetachReceipt(
                    Detached: false,
                    code,
                    Count: task.PendingAttachedImagePaths.Count,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId);
            }

            var result = RequireObject(response.HermesPayload, "image.detach result");
            var detached = RequireBoolean(result, "detached");
            var count = RequireNonNegativeInteger(result, "count");
            if (detached)
                task.PendingAttachedImagePaths.Remove(path);
            return new D046HermesImageDetachReceipt(
                detached,
                detached ? "detached" : "not_detached",
                count,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId);
        }
    }

    public D046HermesCommandReceipt DecodeCommandReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope)
        => throw new InvalidOperationException(
            "Canonical Hermes receipts require the correlated request binding.");

    public D046HermesSubmitReceipt DecodeSubmitReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope)
        => throw new InvalidOperationException(
            "Canonical Hermes submit receipts require the correlated request binding.");

    public D046HermesApprovalResponseReceipt DecodeApprovalResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ApprovalRespondMethod, StringComparison.Ordinal)
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The approval response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var requestPayload = RequireObject(binding.HermesPayload, "Approval request");
            var choice = RequireString(requestPayload, "choice");
            if (!task.ApprovalResponseInFlight
                || task.PendingApprovals.Count == 0
                || !task.PendingApprovals.Peek().Choices.Contains(
                    choice,
                    StringComparer.Ordinal))
                throw new InvalidDataException("The approval response state was stale.");

            task.ApprovalResponseInFlight = false;
            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                var code = RequireString(failure, "code");
                _ = RequireString(failure, "message");
                if (failure.TryGetProperty("retry_proof", out var retryProof))
                {
                    var wasQuarantined = task.ApprovalResponseQuarantined;
                    task.ApprovalResponseQuarantined = true;
                    ValidateExactProperties(failure,
                        new[] { "code", "message", "retry_proof" }, "Approval retry failure");
                    var pending = task.PendingApprovals.Peek();
                    if (code != RuntimeApprovalRetryProof.FailureCode
                        || task.State != D046HermesTaskState.WaitingForApproval
                        || wasQuarantined
                        || task.NativeToolsConfigStale
                        || !string.Equals(binding.ApprovalId, pending.ApprovalId, StringComparison.Ordinal)
                        || !RuntimeApprovalRetryProof.Matches(retryProof,
                            _runtimeGeneration, task.RuntimeSessionId, choice,
                            pending.PatternKey, pending.Choices))
                        throw new InvalidDataException("The no-mutation approval proof was stale or invalid.");
                    task.ApprovalResponseQuarantined = false;
                    return new D046HermesApprovalResponseReceipt(
                        false, code, _runtimeGeneration, task.TaskId,
                        task.RuntimeSessionId, choice, 0,
                        RetryAllowed: true, RetryApprovalId: pending.ApprovalId);
                }
                task.PendingApprovals.Clear();
                task.NativeToolsConfigStaleTurnActive = false;
                if (IsDefinitiveExpiredApprovalFailure(code))
                {
                    task.ApprovalResponseQuarantined = false;
                    if (task.State == D046HermesTaskState.WaitingForApproval)
                        task.State = D046HermesTaskState.Streaming;
                }
                else
                {
                    task.ApprovalResponseQuarantined = true;
                    task.State = D046HermesTaskState.Failed;
                }
                return new D046HermesApprovalResponseReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    choice,
                    Resolved: 0);
            }

            var result = RequireObject(response.HermesPayload, "Approval response");
            if (result.EnumerateObject().Count() != 1)
                throw new InvalidDataException("The approval response shape was malformed.");
            var resolved = RequireNonNegativeInteger(result, "resolved");
            if (resolved != 1 || task.PendingApprovals.Count == 0)
                throw new InvalidDataException("Hermes did not resolve one native approval.");
            var resolvedForActiveTurn = task.State is
                D046HermesTaskState.Streaming
                or D046HermesTaskState.WaitingForApproval;
            task.PendingApprovals.Dequeue();
            if (task.State == D046HermesTaskState.Idle)
            {
                task.PendingApprovals.Clear();
            }
            else if (task.PendingApprovals.Count > 0)
            {
                task.State = D046HermesTaskState.WaitingForApproval;
            }
            else if (task.State == D046HermesTaskState.WaitingForApproval)
            {
                task.State = D046HermesTaskState.Streaming;
            }
            if (string.Equals(choice, "always", StringComparison.Ordinal))
            {
                task.NativeToolsConfigStale = true;
                task.NativeToolsConfigStaleTurnActive = resolvedForActiveTurn;
                _runtimeInvalidatedNativeToolsSessionId = task.RuntimeSessionId;
                task.NoEffectAttestation = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            return new D046HermesApprovalResponseReceipt(
                Accepted: true,
                "resolved",
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                choice,
                resolved);
        }
    }

    private static bool IsDefinitiveExpiredApprovalFailure(string code)
        => code is "runtime.pending_native_approval_expired"
            or "runtime.pending_native_approval_required";

    public D046HermesClarificationResponseReceipt DecodeClarificationResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ClarificationRespondMethod, StringComparison.Ordinal)
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The clarification response was not correlated.");
        }
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var requestPayload = RequireObject(binding.HermesPayload, "Clarification request");
            var requestId = RequireIdentity(
                RequireString(requestPayload, "request_id"),
                "request_id");
            if (!task.ClarificationResponseInFlight
                || !string.Equals(task.ClarificationResponseRequestId, requestId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The clarification response state was stale.");
            }
            var accepted = DecodeBlockingResponseStatus(response, out var code);
            task.ClarificationResponseInFlight = false;
            task.ClarificationResponseRequestId = string.Empty;
            // Native wakes the waiting agent before writing this receipt. A new
            // question or terminal event may already have arrived. Settle only
            // the exact answered question; never erase the newer live state.
            if (string.Equals(task.PendingClarification?.ClarificationId, requestId, StringComparison.Ordinal))
            {
                task.PendingClarification = null;
                if (task.State == D046HermesTaskState.WaitingForClarification)
                    task.State = D046HermesTaskState.Streaming;
            }
            return new D046HermesClarificationResponseReceipt(
                accepted,
                code,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                requestId);
        }
    }

    public D046HermesSecureInputResponseReceipt DecodeSecureInputResponseReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (response.Method is not (SudoRespondMethod or SecretRespondMethod)
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The secure-input response was not correlated.");
        }
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var requestPayload = RequireObject(binding.HermesPayload, "Secure-input request");
            var requestId = RequireIdentity(
                RequireString(requestPayload, "request_id"),
                "request_id");
            var inputKind = response.Method == SudoRespondMethod
                ? D046HermesSecureInputKind.SudoPassword
                : D046HermesSecureInputKind.SecretValue;
            if (!task.SecureInputResponseInFlight
                || task.SecureInputResponseKind != inputKind
                || !string.Equals(task.SecureInputResponseRequestId, requestId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The secure-input response state was stale.");
            }
            var accepted = DecodeBlockingResponseStatus(response, out var code);
            task.SecureInputResponseInFlight = false;
            task.SecureInputResponseRequestId = string.Empty;
            task.SecureInputResponseKind = null;
            // Native may expire the request, ask for another value, or finish
            // the turn before acknowledging this response. Keep only its
            // immutable identity in flight, never its secret value, and settle
            // only the matching live card without reviving a completed turn.
            if (task.PendingSecureInput is { } pending
                && pending.InputKind == inputKind
                && string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            {
                task.PendingSecureInput = null;
                if (task.State == D046HermesTaskState.WaitingForSecureInput)
                    task.State = D046HermesTaskState.Streaming;
            }
            return new D046HermesSecureInputResponseReceipt(
                accepted,
                code,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                requestId,
                inputKind);
        }
    }

    private static bool DecodeBlockingResponseStatus(
        LilithRuntimeResponse response,
        out string code)
    {
        if (!response.Succeeded)
        {
            var failure = RequireObject(response.HermesPayload, "Runtime failure");
            code = RequireString(failure, "code");
            _ = RequireString(failure, "message");
            return false;
        }
        var result = RequireObject(response.HermesPayload, "blocking response");
        ValidateExactProperties(result, new[] { "status" }, "blocking response");
        code = RequireString(result, "status");
        if (code is not ("ok" or "expired"))
            throw new InvalidDataException("The blocking response status was malformed.");
        return code == "ok";
    }

    public D046HermesModelCatalog DecodeCanonicalModelCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ModelOptionsMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, ModelOptionsMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes model catalog was not correlated.");
        }
        RequireSucceeded(response);

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            TaskBinding? task = null;
            if (binding.Correlation is null)
            {
                var request = RequireObject(
                    binding.HermesPayload,
                    "sessionless model.options request");
                if (request.TryGetProperty("session_id", out _))
                {
                    throw new InvalidDataException(
                        "The sessionless Hermes model catalog escaped into a session.");
                }
            }
            else
            {
                task = RequireCorrelatedTaskLocked(binding.Correlation);
                if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The Hermes model catalog task was not active.");
                }
            }

            var result = RequireObject(response.HermesPayload, "model.options result");
            var exactProvider = RequireString(result, "provider", allowEmpty: true);
            var exactModel = RequireString(result, "model", allowEmpty: true);
            if ((exactProvider.Length == 0) != (exactModel.Length == 0))
            {
                throw new InvalidDataException(
                    "model.options returned a partial provider/model selection.");
            }
            if (exactProvider.Length != 0)
            {
                exactProvider = RequireIdentity(exactProvider, "provider");
                exactModel = RequireIdentity(exactModel, "model");
            }
            var providers = RequireArray(result, "providers");
            var providerIds = new HashSet<string>(StringComparer.Ordinal);
            var certifiedPairs = new HashSet<ModelPair>();
            var unavailablePairs = new HashSet<ModelPair>();
            var options = new List<D046HermesModelOption>();
            foreach (var item in providers.EnumerateArray())
            {
                var provider = RequireObject(item, "model.options provider");
                var providerId = RequireIdentity(
                    RequireString(provider, "slug"),
                    "provider slug");
                if (!providerIds.Add(providerId))
                    throw new InvalidDataException("model.options duplicated a provider.");
                var providerName = RequireString(provider, "name");
                var authenticated = RequireBoolean(provider, "authenticated");
                var models = RequireArray(provider, "models");
                var modelIds = new HashSet<string>(StringComparer.Ordinal);
                var orderedModelIds = new List<string>();
                foreach (var modelValue in models.EnumerateArray())
                {
                    var modelId = RequireIdentity(
                        RequireStringValue(modelValue, "model", allowEmpty: false),
                        "model");
                    if (!modelIds.Add(modelId))
                        throw new InvalidDataException("model.options duplicated a model.");
                    orderedModelIds.Add(modelId);
                }

                var unavailableModelIds = new HashSet<string>(StringComparer.Ordinal);
                if (provider.TryGetProperty("unavailable_models", out var unavailable))
                {
                    if (unavailable.ValueKind != JsonValueKind.Array)
                    {
                        throw new InvalidDataException(
                            "model.options unavailable models were malformed.");
                    }
                    foreach (var unavailableValue in unavailable.EnumerateArray())
                    {
                        var unavailableModelId = RequireIdentity(
                            RequireStringValue(
                                unavailableValue,
                                "unavailable model",
                                allowEmpty: false),
                            "unavailable model");
                        if (!unavailableModelIds.Add(unavailableModelId)
                            || !modelIds.Contains(unavailableModelId))
                        {
                            throw new InvalidDataException(
                                "model.options unavailable models escaped the provider catalog.");
                        }
                        if (authenticated)
                        {
                            unavailablePairs.Add(
                                new ModelPair(providerId, unavailableModelId));
                        }
                    }
                }

                foreach (var modelId in orderedModelIds)
                {
                    if (!authenticated)
                        continue;
                    if (unavailableModelIds.Contains(modelId))
                        continue;
                    var pair = new ModelPair(providerId, modelId);
                    if (!certifiedPairs.Add(pair))
                        throw new InvalidDataException("model.options duplicated a provider/model pair.");
                    options.Add(new D046HermesModelOption(
                        providerId,
                        providerName,
                        modelId));
                }
            }

            if (exactProvider.Length != 0
                && !certifiedPairs.Contains(new ModelPair(exactProvider, exactModel)))
            {
                if (unavailablePairs.Contains(
                        new ModelPair(exactProvider, exactModel))
                    || task is not null && CanRecoverCredential(task)
                        && string.Equals(exactProvider, task.ProviderId, StringComparison.Ordinal)
                        && string.Equals(exactModel, task.ModelDisplayName, StringComparison.Ordinal))
                {
                    exactProvider = string.Empty;
                    exactModel = string.Empty;
                }
                else
                {
                    throw new InvalidDataException(
                        "model.options selected a provider/model pair that was not authenticated and available.");
                }
            }
            if (task is not null)
                task.CertifiedModelPairs = certifiedPairs;
            else
                _sessionlessCertifiedModelPairs = certifiedPairs;
            return new D046HermesModelCatalog(
                _runtimeGeneration,
                task?.TaskId ?? string.Empty,
                task?.RuntimeSessionId ?? string.Empty,
                options.AsReadOnly(),
                exactProvider,
                exactModel);
        }
    }

    public D046HermesDefaultModelSelectionReceipt
        DecodeCanonicalDefaultModelSelectionReceipt(
            LilithRuntimeResponse response,
            D046HermesRuntimeEnvelopeContext envelope,
            D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(
                response.Method,
                DefaultModelSetMethod,
                StringComparison.Ordinal)
            || !string.Equals(
                binding.Method,
                DefaultModelSetMethod,
                StringComparison.Ordinal)
            || binding.Correlation is not null)
        {
            throw new InvalidDataException(
                "The Hermes default model selection was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var request = RequireObject(
                binding.HermesPayload,
                "model.default.set request");
            var provider = RequireIdentity(
                RequireString(request, "provider"),
                "provider");
            var model = RequireIdentity(
                RequireString(request, "model"),
                "model");
            _ = RequireBoolean(request, "confirm_expensive_model");
            if (!_sessionlessCertifiedModelPairs.Contains(
                    new ModelPair(provider, model)))
            {
                throw new InvalidDataException(
                    "The default model selection pair was not certified.");
            }

            if (!response.Succeeded)
            {
                var failure = RequireObject(
                    response.HermesPayload,
                    "Runtime failure");
                var code = RequireString(failure, "code");
                var message = RequireString(failure, "message");
                ValidateHermesFailureCode(failure);
                return new D046HermesDefaultModelSelectionReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    provider,
                    model,
                    message,
                    ConfirmationRequired: false,
                    ConfirmationMessage: string.Empty);
            }

            var result = RequireObject(
                response.HermesPayload,
                "model.default.set result");
            if (!string.Equals(
                    RequireString(result, "key"),
                    "model",
                    StringComparison.Ordinal)
                || !string.Equals(
                    RequireString(result, "value"),
                    model,
                    StringComparison.Ordinal)
                || !string.Equals(
                    RequireString(result, "scope"),
                    "global",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes did not acknowledge an exact global model selection.");
            }
            var warning = RequireString(result, "warning", allowEmpty: true);
            var confirmationRequired = RequireBoolean(
                result,
                "confirm_required");
            var confirmationMessage = RequireString(
                result,
                "confirm_message",
                allowEmpty: true);
            return new D046HermesDefaultModelSelectionReceipt(
                Accepted: !confirmationRequired,
                confirmationRequired
                    ? "confirmation_required"
                    : "accepted",
                _runtimeGeneration,
                provider,
                model,
                warning,
                confirmationRequired,
                confirmationMessage);
        }
    }

    public D046HermesNativeToolsCatalog DecodeCanonicalNativeToolsCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, NativeToolsCatalogMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, NativeToolsCatalogMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes native tool catalog was not correlated.");
        }
        RequireSucceeded(response);

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var request = RequireObject(binding.HermesPayload, "tools.catalog request");
            if (!string.Equals(
                    RequireString(request, "session_id"),
                    task.RuntimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("tools.catalog escaped the active session.");
            }
            var catalog = DecodeNativeToolsCatalogResult(
                response.HermesPayload,
                task);
            task.NativeToolsCatalog = catalog;
            return catalog;
        }
    }

    public D046HermesNativeToolsSetReceipt DecodeCanonicalNativeToolsSetReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, NativeToolsSetMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, NativeToolsSetMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes native tool update was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var prior = task.NativeToolsCatalog
                ?? throw new InvalidDataException(
                    "tools.set had no certified catalog predecessor.");
            var request = RequireObject(binding.HermesPayload, "tools.set request");
            if (!string.Equals(
                    RequireString(request, "session_id"),
                    task.RuntimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("tools.set escaped the active session.");
            }
            var requestedEnabled = ReadUniqueIdentityArray(
                request,
                "enabled_toolsets",
                maximumCount: 256);
            var requestedMode = ParseNativeApprovalMode(
                RequireString(request, "approval_mode"));

            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                ValidateExactProperties(
                    failure,
                    new[] { "code", "message", "hermesCode" },
                    "tools.set Runtime failure");
                var code = RequireString(failure, "code");
                _ = RequireString(failure, "message");
                var hermesCode = RequireHermesFailureCode(failure);
                if (hermesCode is not (-32602 or 4090 or 5035))
                    throw new InvalidDataException("tools.set returned an unsupported failure.");
                return new D046HermesNativeToolsSetReceipt(
                    Accepted: false,
                    code,
                    ConfigSha256: string.Empty,
                    prior);
            }

            var result = RequireObject(response.HermesPayload, "tools.set result");
            var requestedSetup = HermesNativeToolsContract.DecodeSetupSelection(request);
            var requestedMcpSetup = HermesNativeToolsContract.DecodeMcpSetup(request);
            ValidateExactProperties(
                result,
                (new[]
                {
                    "platform",
                    "approval_mode",
                    "enabled_toolsets",
                    "config_sha256",
                    "new_session_required"
                }).Concat(requestedSetup is null ? Array.Empty<string>() : new[] { "setup_selection" })
                    .Concat(requestedMcpSetup is null ? Array.Empty<string>() : new[] { "mcp_setup" }).ToArray(),
                "tools.set result");
            if (!string.Equals(
                    RequireString(result, "platform"),
                    "cli",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("tools.set returned an unsupported platform.");
            }
            var acknowledgedMode = ParseNativeApprovalMode(
                RequireString(result, "approval_mode"));
            var acknowledgedEnabled = ReadUniqueIdentityArray(
                result,
                "enabled_toolsets",
                maximumCount: 256);
            if (!RequireBoolean(result, "new_session_required")
                || HermesNativeToolsContract.DecodeSetupSelection(result) != requestedSetup
                || HermesNativeToolsContract.DecodeMcpSetup(result) != requestedMcpSetup
                || acknowledgedMode != requestedMode
                || !new HashSet<string>(
                        acknowledgedEnabled,
                        StringComparer.Ordinal).SetEquals(requestedEnabled))
            {
                throw new InvalidDataException(
                    "tools.set did not acknowledge the exact replacement settings.");
            }
            var configSha256 = RequireSha256(
                RequireString(result, "config_sha256"),
                "config_sha256");
            var acknowledgedSet = new HashSet<string>(
                acknowledgedEnabled,
                StringComparer.Ordinal);
            var catalog = prior with
            {
                ApprovalMode = acknowledgedMode,
                EnabledToolsets = acknowledgedEnabled,
                Toolsets = prior.Toolsets
                    .Select(row => row with
                    {
                        Enabled = acknowledgedSet.Contains(row.Name)
                    })
                    .ToArray(),
                NewSessionRequired = true
            };
            task.NativeToolsCatalog = catalog;
            task.NativeApprovalOnlyReactivation = requestedSetup is null && requestedMcpSetup is null && new HashSet<string>(
                prior.EnabledToolsets, StringComparer.Ordinal).SetEquals(acknowledgedEnabled);
            task.NativeToolsConfigStale = true;
            task.NativeToolsConfigStaleTurnActive = false;
            _runtimeInvalidatedNativeToolsSessionId = task.RuntimeSessionId;
            task.NoEffectAttestation = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return new D046HermesNativeToolsSetReceipt(
                Accepted: true,
                "accepted",
                configSha256,
                catalog);
        }
    }

    public D046HermesModelSelectionReceipt DecodeCanonicalModelSelectionReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, ConfigSetMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, ConfigSetMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes model selection was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var request = RequireObject(binding.HermesPayload, "config.set request");
            var runtimeSessionId = RequireIdentity(
                RequireString(request, "session_id"),
                "session_id");
            var provider = RequireIdentity(
                RequireString(request, "provider"),
                "provider");
            var model = RequireIdentity(RequireString(request, "model"), "model");
            _ = RequireBoolean(request, "confirm_expensive_model");
            if (!string.Equals(runtimeSessionId, task.RuntimeSessionId, StringComparison.Ordinal)
                || !task.CertifiedModelPairs.Contains(new ModelPair(provider, model)))
            {
                throw new InvalidDataException(
                    "The Hermes model selection pair was not certified.");
            }

            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                var code = RequireString(failure, "code");
                var message = RequireString(failure, "message");
                ValidateHermesFailureCode(failure);
                if (binding.RequiresCredentialRecoveryAttestation)
                {
                    var hermesCode = failure.GetProperty("hermesCode");
                    var noDispatch = hermesCode.ValueKind == JsonValueKind.Null
                        && code is "runtime.native_surface_attestation_required"
                            or "runtime.gateway_not_sent";
                    if (!noDispatch)
                        throw new InvalidDataException("Credential recovery setter outcome was not proven effect-free.");
                    CancelCredentialRecoveryLocked(task);
                }
                return new D046HermesModelSelectionReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    provider,
                    model,
                    Deferred: false,
                    message,
                    ConfirmationRequired: false,
                    ConfirmationMessage: string.Empty);
            }

            var result = RequireObject(response.HermesPayload, "config.set result");
            if (!string.Equals(RequireString(result, "key"), "model", StringComparison.Ordinal))
                throw new InvalidDataException("config.set did not acknowledge the model key.");
            _ = RequireString(result, "value");
            var warning = RequireString(result, "warning", allowEmpty: true);
            var confirmationRequired = RequireBoolean(result, "confirm_required");
            var confirmationMessage = RequireString(
                result,
                "confirm_message",
                allowEmpty: true);
            if (!string.Equals(RequireString(result, "scope"), "session", StringComparison.Ordinal))
                throw new InvalidDataException("config.set escaped the active session scope.");
            var deferred = result.TryGetProperty("deferred", out var deferredValue)
                ? deferredValue.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw new InvalidDataException("config.set deferred flag was malformed.")
                }
                : false;
            var accepted = !confirmationRequired;
            if (binding.RequiresCredentialRecoveryAttestation)
            {
                if (deferred)
                    throw new InvalidDataException("Idle credential recovery cannot be deferred.");
                if (!accepted)
                    CancelCredentialRecoveryLocked(task);
            }
            return new D046HermesModelSelectionReceipt(
                accepted,
                confirmationRequired
                    ? "confirmation_required"
                    : deferred ? "deferred" : "accepted",
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                provider,
                model,
                deferred,
                warning,
                confirmationRequired,
                confirmationMessage);
        }
    }

    public D046HermesCompressionReceipt DecodeCanonicalCompressionReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, SessionCompressMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, SessionCompressMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes compression response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            var request = RequireObject(binding.HermesPayload, "session.compress request");
            ValidateCompressionRequestPayload(request, task.RuntimeSessionId);

            if (!response.Succeeded)
            {
                var failure = RequireObject(response.HermesPayload, "Runtime failure");
                var code = RequireString(failure, "code");
                var message = RequireString(failure, "message");
                ValidateHermesFailureCode(failure);
                return new D046HermesCompressionReceipt(
                    Accepted: false,
                    code,
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    BeforeMessages: null,
                    AfterMessages: null,
                    BeforeTokens: null,
                    AfterTokens: null,
                    message,
                    TokenLine: string.Empty,
                    Note: string.Empty,
                    NoOp: false,
                    Aborted: false,
                    SnapshotLocked());
            }

            var result = RequireObject(response.HermesPayload, "session.compress result");
            if (result.TryGetProperty("lock_held", out var lockHeldValue))
            {
                if (lockHeldValue.ValueKind != JsonValueKind.True
                    || !result.TryGetProperty("compressed", out var compressed)
                    || compressed.ValueKind != JsonValueKind.False)
                {
                    throw new InvalidDataException(
                        "session.compress lock acknowledgement was malformed.");
                }
                var message = RequireString(result, "message");
                return new D046HermesCompressionReceipt(
                    Accepted: false,
                    Code: "lock_held",
                    _runtimeGeneration,
                    task.TaskId,
                    task.RuntimeSessionId,
                    BeforeMessages: null,
                    AfterMessages: null,
                    BeforeTokens: null,
                    AfterTokens: null,
                    message,
                    TokenLine: string.Empty,
                    Note: string.Empty,
                    NoOp: false,
                    Aborted: false,
                    SnapshotLocked());
            }

            var status = RequireString(result, "status");
            if (status is not ("compressed" or "aborted"))
                throw new InvalidDataException("session.compress status was not recognized.");
            var removed = RequireNonNegativeInteger(result, "removed");
            var beforeMessages = RequireNonNegativeInteger(result, "before_messages");
            var afterMessages = RequireNonNegativeInteger(result, "after_messages");
            var beforeTokens = RequireNonNegativeLong(result, "before_tokens");
            var afterTokens = RequireNonNegativeLong(result, "after_tokens");
            if (removed > beforeMessages)
                throw new InvalidDataException("session.compress removed count was malformed.");

            var summary = RequireObjectProperty(result, "summary");
            var noOp = RequireBoolean(summary, "noop");
            var aborted = RequireBoolean(summary, "aborted");
            _ = RequireBoolean(summary, "fallback_used");
            var headline = RequireString(summary, "headline");
            var tokenLine = RequireString(summary, "token_line");
            var note = RequireNullableString(summary, "note");
            if (aborted != string.Equals(status, "aborted", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "session.compress status and summary did not agree.");
            }

            _ = RequireObjectProperty(result, "usage");
            var info = RequireObjectProperty(result, "info");
            _ = DecodeSessionInfoPayloadLocked(info, task);
            var messages = RequireArray(result, "messages");
            if (messages.GetArrayLength() > afterMessages)
                throw new InvalidDataException("session.compress message count was malformed.");

            task.Events.Clear();
            task.ActiveMessageIds.Clear();
            task.ActiveReasoningIds.Clear();
            task.KnownTurnIds.Clear();
            task.CompletedTurnIds.Clear();
            task.ClaimedUserMessageIds.Clear();
            task.ClaimedAssistantMessageIds.Clear();
            task.ClaimedAuxiliaryAssistantMessageIds.Clear();
            task.ClaimedInterimAssistantMessageIds.Clear();
            task.LastInterimAssistantMessageIds.Clear();
            task.TerminalErrorMarkerTurnIds.Clear();
            task.TerminalErrorDisclosedTurnIds.Clear();
            var raw = responseRaw(response.HermesPayload);
            var itemIndex = 0;
            foreach (var item in messages.EnumerateArray())
            {
                DecodeHistoryMessageLocked(
                    task,
                    item,
                    itemIndex,
                    envelope,
                    raw,
                    SessionCompressMethod);
                itemIndex++;
            }
            return new D046HermesCompressionReceipt(
                Accepted: !aborted,
                status,
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                beforeMessages,
                afterMessages,
                beforeTokens,
                afterTokens,
                headline,
                tokenLine,
                note,
                noOp,
                aborted,
                SnapshotLocked());
        }
    }

    public D046HermesUsageProjection DecodeCanonicalUsage(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginObservabilityDecode(
            response,
            envelope,
            binding,
            SessionUsageMethod,
            out _,
            out var statusCode);
        if (!string.Equals(statusCode, "ready", StringComparison.Ordinal))
            return UnavailableUsage(statusCode);
        return ParseUsage(result);
    }

    public D046HermesContextProjection DecodeCanonicalContextBreakdown(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginObservabilityDecode(
            response,
            envelope,
            binding,
            SessionContextBreakdownMethod,
            out _,
            out var statusCode);
        if (!string.Equals(statusCode, "ready", StringComparison.Ordinal))
        {
            return new D046HermesContextProjection(
                IsAvailable: false,
                statusCode,
                0,
                0,
                0,
                0,
                string.Empty,
                Array.Empty<D046HermesContextCategory>());
        }
        var contextMaximum = RequireNonNegativeLong(result, "context_max");
        var contextUsed = RequireNonNegativeLong(result, "context_used");
        var contextPercent = RequirePercentage(result, "context_percent");
        var estimatedTotal = RequireNonNegativeLong(result, "estimated_total");
        var model = RequireIdentity(RequireString(result, "model"), "context model");

        var categories = new List<D046HermesContextCategory>();
        var categoryIds = new HashSet<string>(StringComparer.Ordinal);
        var knownCategoryIds = new HashSet<string>(
            new[]
            {
                "system_prompt",
                "tool_definitions",
                "rules",
                "skills",
                "mcp",
                "subagent_definitions",
                "memory",
                "conversation"
            },
            StringComparer.Ordinal);
        foreach (var item in RequireArray(result, "categories").EnumerateArray())
        {
            var category = RequireObject(item, "context category");
            var id = RequireIdentity(RequireString(category, "id"), "context category id");
            if (!knownCategoryIds.Contains(id) || !categoryIds.Add(id))
                throw new InvalidDataException("Context breakdown duplicated a category.");
            categories.Add(new D046HermesContextCategory(
                id,
                RequireString(category, "label"),
                RequireString(category, "color"),
                RequireNonNegativeLong(category, "tokens")));
        }

        return new D046HermesContextProjection(
            IsAvailable: true,
            StatusCode: "ready",
            contextUsed,
            contextMaximum,
            contextPercent,
            estimatedTotal,
            model,
            categories.AsReadOnly());
    }

    public D046HermesLearningProjection DecodeCanonicalLearningFrames(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginObservabilityDecode(
            response,
            envelope,
            binding,
            LearningFramesMethod,
            out _,
            out var statusCode);
        if (!string.Equals(statusCode, "ready", StringComparison.Ordinal))
        {
            return new D046HermesLearningProjection(
                IsAvailable: false,
                statusCode,
                0,
                Array.Empty<string>(),
                Array.Empty<D046HermesLearningItem>());
        }
        var count = RequireNonNegativeInteger(result, "count");
        var frames = RequireArray(result, "frames");
        if (RequireNonNegativeInteger(result, "cols") != 80
            || RequireNonNegativeInteger(result, "rows") != 16
            || frames.GetArrayLength() != 2)
        {
            throw new InvalidDataException("learning.frames did not honor the bounded shape.");
        }
        var lastVisible = 0;
        foreach (var frameValue in frames.EnumerateArray())
            lastVisible = ValidateLearningFrame(frameValue, count);
        if (lastVisible != count)
            throw new InvalidDataException("learning.frames final visibility did not match count.");

        var summary = new List<string>();
        foreach (var item in RequireArray(result, "summary").EnumerateArray())
        {
            var text = RequireStringValue(item, "learning summary");
            if (text.Length > 4096 || summary.Count >= 16)
                throw new InvalidDataException("learning.frames summary was too large.");
            summary.Add(text);
        }

        var allItems = new List<D046HermesLearningItem>();
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        var buckets = RequireArray(result, "buckets");
        if (buckets.GetArrayLength() > 64)
            throw new InvalidDataException("learning.frames had too many buckets.");
        foreach (var bucketValue in buckets.EnumerateArray())
        {
            var bucket = RequireObject(bucketValue, "learning bucket");
            var bucketLabel = RequireString(bucket, "label");
            foreach (var nodeValue in RequireArray(bucket, "nodes").EnumerateArray())
            {
                var node = RequireObject(nodeValue, "learning node");
                var id = RequireIdentity(RequireString(node, "id"), "learning node id");
                if (!itemIds.Add(id))
                    throw new InvalidDataException("learning.frames duplicated a node.");
                var kind = RequireString(node, "style");
                if (kind is not ("memory" or "skill"))
                    throw new InvalidDataException("learning.frames node kind was rejected.");
                var fullLabel = RequireString(node, "fullLabel");
                var metadata = RequireString(node, "meta", allowEmpty: true);
                if (fullLabel.Length > 1024 || metadata.Length > 4096)
                    throw new InvalidDataException("learning.frames node text was too large.");
                allItems.Add(new D046HermesLearningItem(
                    id,
                    kind,
                    fullLabel,
                    metadata,
                    bucketLabel));
            }
        }
        if (allItems.Count != count)
            throw new InvalidDataException("learning.frames node count did not agree.");

        return new D046HermesLearningProjection(
            IsAvailable: true,
            StatusCode: "ready",
            count,
            summary.AsReadOnly(),
            allItems.TakeLast(8).ToList().AsReadOnly());
    }

    public D046HermesDelegationSnapshot DecodeCanonicalDelegationStatus(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginDelegationDecode(
            response,
            envelope,
            binding,
            DelegationStatusMethod,
            new[] { "session_id" },
            out var task);
        ValidateExactProperties(
            result,
            new[] { "active", "paused", "max_spawn_depth", "max_concurrent_children" },
            DelegationStatusMethod + " result");
        var active = RequireArray(result, "active");
        if (active.GetArrayLength() > 2)
            throw new InvalidDataException("Hermes exceeded the two-child delegation ceiling.");

        var children = new List<D046HermesSubagentCardProjection>(
            active.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in active.EnumerateArray())
        {
            var item = RequireObject(value, "delegation.status child");
            var ownerSession = RequireIdentity(
                RequireString(item, "session_id"),
                "session_id");
            if (!string.Equals(ownerSession, task.RuntimeSessionId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes delegation child escaped the active session.");
            }

            var subagentId = RequireIdentity(
                RequireString(item, "subagent_id"),
                "subagent_id");
            if (!seen.Add(subagentId))
                throw new InvalidDataException("Hermes returned a duplicate subagent ID.");
            var status = RequireString(item, "status");
            if (!string.Equals(status, "running", StringComparison.Ordinal))
                throw new InvalidDataException("Hermes active child was not running.");

            var startedSeconds = RequireFiniteNumber(item, "started_at");
            if (startedSeconds < 0 || startedSeconds > 253_402_300_799)
                throw new InvalidDataException("Hermes subagent start time was invalid.");
            DateTimeOffset startedUtc;
            try
            {
                startedUtc = DateTimeOffset.FromUnixTimeMilliseconds(
                    checked((long)(startedSeconds * 1000)));
            }
            catch (Exception exception) when (exception is OverflowException
                                              or ArgumentOutOfRangeException)
            {
                throw new InvalidDataException(
                    "Hermes subagent start time was invalid.",
                    exception);
            }

            children.Add(new D046HermesSubagentCardProjection(
                subagentId,
                OptionalNullableIdentity(item, "parent_id"),
                string.Empty,
                RequireContentString(item, "goal", allowEmpty: true),
                OptionalNullableIdentity(item, "model"),
                D046HermesSubagentState.Running,
                RequireNonNegativeInteger(item, "depth"),
                RequireNonNegativeInteger(item, "tool_count"),
                startedUtc,
                null,
                RequireExactDelegationChildToolsets(item)));
        }

        var paused = RequireBoolean(result, "paused");
        var maxDepth = RequirePositiveInteger(result, "max_spawn_depth");
        var maxChildren = RequirePositiveInteger(result, "max_concurrent_children");
        if (maxChildren > 2 || children.Count > maxChildren)
            throw new InvalidDataException("Hermes delegation concurrency exceeded policy.");
        return new D046HermesDelegationSnapshot(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            _nextProjectionSequence,
            paused,
            maxDepth,
            maxChildren,
            children.AsReadOnly());
    }

    public D046HermesSubagentSteerReceipt DecodeCanonicalSubagentSteerReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginDelegationDecode(
            response,
            envelope,
            binding,
            SubagentSteerMethod,
            new[] { "session_id", "subagent_id", "text" },
            out var task);
        ValidateExactProperties(
            result,
            new[] { "status", "subagent_id", "text" },
            SubagentSteerMethod + " result");
        var status = RequireString(result, "status");
        if (status is not ("queued" or "rejected"))
            throw new InvalidDataException("Hermes subagent steer status was invalid.");
        var subagentId = RequireExactEcho(result, binding.HermesPayload, "subagent_id");
        var text = RequireExactEcho(result, binding.HermesPayload, "text", content: true);
        return new D046HermesSubagentSteerReceipt(
            status == "queued",
            status,
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            subagentId,
            text);
    }

    public D046HermesSubagentInterruptReceipt DecodeCanonicalSubagentInterruptReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginDelegationDecode(
            response,
            envelope,
            binding,
            SubagentInterruptMethod,
            new[] { "session_id", "subagent_id" },
            out var task);
        ValidateExactProperties(
            result,
            new[] { "found", "subagent_id" },
            SubagentInterruptMethod + " result");
        var subagentId = RequireExactEcho(result, binding.HermesPayload, "subagent_id");
        return new D046HermesSubagentInterruptReceipt(
            RequireBoolean(result, "found"),
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            subagentId);
    }

    public D046HermesVoiceStatusReceipt DecodeCanonicalVoiceStatusReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginVoiceDecode(
            response,
            envelope,
            binding,
            VoiceToggleMethod,
            "status",
            includeSessionId: true,
            out var task);
        var names = PropertyNames(result, VoiceToggleMethod + " status result");
        var baseNames = new[] { "enabled", "record_key", "tts" };
        var fullNames = new[]
        {
            "enabled", "record_key", "tts", "available",
            "audio_available", "stt_available", "details"
        };
        var hasRequirements = names.SetEquals(fullNames);
        if (!hasRequirements && !names.SetEquals(baseNames))
            throw new InvalidDataException("voice.toggle status result shape was rejected.");

        _ = RequireBoolean(result, "tts");
        return new D046HermesVoiceStatusReceipt(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            RequireBoolean(result, "enabled"),
            RequireIdentity(RequireString(result, "record_key"), "record_key"),
            hasRequirements ? RequireBoolean(result, "available") : null,
            hasRequirements ? RequireBoolean(result, "audio_available") : null,
            hasRequirements ? RequireBoolean(result, "stt_available") : null,
            hasRequirements ? RequireVoiceDetails(result) : string.Empty);
    }

    public D046HermesVoiceOnReceipt DecodeCanonicalVoiceOnReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginVoiceDecode(
            response,
            envelope,
            binding,
            VoiceToggleMethod,
            "on",
            includeSessionId: true,
            out var task);
        ValidateExactProperties(
            result,
            new[] { "enabled", "record_key", "tts", "stop_hint" },
            VoiceToggleMethod + " on result");
        if (!RequireBoolean(result, "enabled"))
            throw new InvalidDataException("voice.toggle on did not enable voice.");
        _ = RequireBoolean(result, "tts");
        return new D046HermesVoiceOnReceipt(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            Enabled: true,
            RequireIdentity(RequireString(result, "record_key"), "record_key"),
            RequireString(result, "stop_hint", allowEmpty: true));
    }

    public D046HermesVoiceOffReceipt DecodeCanonicalVoiceOffReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginVoiceDecode(
            response,
            envelope,
            binding,
            VoiceToggleMethod,
            "off",
            includeSessionId: true,
            out var task);
        ValidateExactProperties(
            result,
            new[] { "enabled", "record_key", "tts", "stop_hint" },
            VoiceToggleMethod + " off result");
        if (RequireBoolean(result, "enabled"))
            throw new InvalidDataException("voice.toggle off did not disable voice.");
        _ = RequireBoolean(result, "tts");
        if (RequireString(result, "stop_hint", allowEmpty: true).Length != 0)
            throw new InvalidDataException("voice.toggle off returned a stop hint.");
        return new D046HermesVoiceOffReceipt(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            Enabled: false,
            RequireIdentity(RequireString(result, "record_key"), "record_key"));
    }

    public D046HermesVoiceStartReceipt DecodeCanonicalVoiceStartReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginVoiceDecode(
            response,
            envelope,
            binding,
            VoiceRecordMethod,
            "start",
            includeSessionId: true,
            out var task);
        var names = PropertyNames(result, VoiceRecordMethod + " start result");
        var status = RequireString(result, "status");
        var reason = string.Empty;
        if (status == "recording")
        {
            if (!names.SetEquals(new[] { "status" }))
                throw new InvalidDataException("voice.record recording result shape was rejected.");
        }
        else if (status == "busy")
        {
            if (names.SetEquals(new[] { "status", "reason" }))
            {
                reason = RequireString(result, "reason");
                if (reason != "wake_owned")
                    throw new InvalidDataException("voice.record busy reason was rejected.");
            }
            else if (!names.SetEquals(new[] { "status" }))
            {
                throw new InvalidDataException("voice.record busy result shape was rejected.");
            }
        }
        else
        {
            throw new InvalidDataException("voice.record start status was rejected.");
        }

        return new D046HermesVoiceStartReceipt(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            Started: status == "recording",
            Busy: status == "busy",
            reason);
    }

    public D046HermesVoiceStopReceipt DecodeCanonicalVoiceStopReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var result = BeginVoiceDecode(
            response,
            envelope,
            binding,
            VoiceRecordMethod,
            "stop",
            includeSessionId: true,
            out var task);
        ValidateExactProperties(
            result,
            new[] { "status" },
            VoiceRecordMethod + " stop result");
        if (RequireString(result, "status") != "stopped")
            throw new InvalidDataException("voice.record stop status was rejected.");
        return new D046HermesVoiceStopReceipt(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            Stopped: true);
    }

    public D046HermesRollbackCatalog DecodeCanonicalRollbackCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        lock (_gate)
        {
            var result = BeginRollbackDecodeLocked(
                response,
                envelope,
                binding,
                RollbackListMethod,
                new[] { "session_id" },
                out var task);
            ValidateExactProperties(
                result,
                new[] { "enabled", "checkpoints" },
                RollbackListMethod + " result");
            var enabled = RequireBoolean(result, "enabled");
            var values = RequireArray(result, "checkpoints");
            if (values.GetArrayLength() > MaximumRollbackCheckpoints)
                throw new InvalidDataException("rollback.list returned too many checkpoints.");

            var checkpoints = new List<D046HermesRollbackCheckpoint>(
                values.GetArrayLength());
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values.EnumerateArray())
            {
                var item = RequireObject(value, "rollback checkpoint");
                ValidateExactProperties(
                    item,
                    new[] { "hash", "timestamp", "message" },
                    "rollback checkpoint");
                var hash = RequireRollbackHash(
                    RequireString(item, "hash"),
                    "checkpoint hash");
                if (!hashes.Add(hash))
                    throw new InvalidDataException("rollback.list duplicated a checkpoint hash.");
                var timestampText = RequireString(item, "timestamp");
                if (!DateTimeOffset.TryParse(
                        timestampText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out var timestamp))
                {
                    throw new InvalidDataException(
                        "rollback checkpoint timestamp was malformed.");
                }
                var message = RequireBoundedContent(
                    item,
                    "message",
                    MaximumRollbackMessageUtf8Bytes,
                    allowEmpty: true);
                checkpoints.Add(new D046HermesRollbackCheckpoint(
                    hash,
                    timestamp,
                    message));
            }
            if (!enabled && checkpoints.Count != 0)
            {
                throw new InvalidDataException(
                    "rollback.list returned checkpoints while rollback was disabled.");
            }

            task.RollbackCheckpointHashes.Clear();
            task.RollbackCheckpointHashes.UnionWith(hashes);
            task.RollbackCatalogAvailable = true;
            ClearRollbackConfirmationLocked(task);
            return new D046HermesRollbackCatalog(
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                _nextProjectionSequence,
                enabled,
                checkpoints.AsReadOnly());
        }
    }

    public D046HermesRollbackDiffReceipt DecodeCanonicalRollbackDiffReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        lock (_gate)
        {
            var result = BeginRollbackDecodeLocked(
                response,
                envelope,
                binding,
                RollbackDiffMethod,
                new[] { "session_id", "hash" },
                out var task);
            var hash = RequireRollbackHash(
                RequireString(binding.HermesPayload, "hash"),
                "checkpoint hash");
            if (!task.RollbackCatalogAvailable
                || !task.RollbackCheckpointHashes.Contains(hash))
            {
                throw new InvalidDataException(
                    "rollback.diff was no longer bound to the latest catalog.");
            }
            ValidateExactProperties(
                result,
                new[]
                {
                    "success", "stat", "diff", "truncated", "confirmation_token"
                },
                RollbackDiffMethod + " result");
            var succeeded = RequireBoolean(result, "success");
            var stat = RequireBoundedContent(
                result,
                "stat",
                MaximumRollbackStatUtf8Bytes,
                allowEmpty: true);
            var diff = RequireBoundedContent(
                result,
                "diff",
                MaximumRollbackDiffUtf8Bytes,
                allowEmpty: true);
            var truncated = RequireBoolean(result, "truncated");
            var token = RequireString(result, "confirmation_token", allowEmpty: true);
            if (succeeded)
            {
                token = RequireIdentity(token, "confirmation_token");
                task.RollbackConfirmationHash = hash;
                task.RollbackConfirmationToken = token;
            }
            else
            {
                if (token.Length != 0)
                {
                    throw new InvalidDataException(
                        "rollback.diff failure returned a confirmation token.");
                }
                ClearRollbackConfirmationLocked(task);
            }

            return new D046HermesRollbackDiffReceipt(
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                _nextProjectionSequence,
                hash,
                succeeded,
                stat,
                diff,
                truncated,
                token);
        }
    }

    public D046HermesRollbackRestoreReceipt DecodeCanonicalRollbackRestoreReceipt(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        lock (_gate)
        {
            var result = BeginRollbackDecodeLocked(
                response,
                envelope,
                binding,
                RollbackRestoreMethod,
                new[] { "session_id", "hash", "confirmation_token" },
                out var task);
            var hash = RequireRollbackHash(
                RequireString(binding.HermesPayload, "hash"),
                "checkpoint hash");
            var token = RequireIdentity(
                RequireString(binding.HermesPayload, "confirmation_token"),
                "confirmation_token");
            if (!task.RollbackCatalogAvailable
                || !task.RollbackCheckpointHashes.Contains(hash)
                || !string.Equals(task.RollbackConfirmationHash, hash,
                    StringComparison.Ordinal)
                || !string.Equals(task.RollbackConfirmationToken, token,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "rollback.restore confirmation was stale at receipt time.");
            }

            var succeeded = RequireBoolean(result, "success");
            ValidateExactProperties(
                result,
                new[]
                {
                    "success", "restored_to", "reason", "history_removed",
                    "workspace_restored", "history_rewound", "partial", "error"
                },
                RollbackRestoreMethod + " result");
            var restoredTo = RequireString(result, "restored_to", allowEmpty: !succeeded);
            var reason = RequireBoundedContent(
                result,
                "reason",
                MaximumRollbackReasonUtf8Bytes,
                allowEmpty: true);
            var historyRemoved = RequireNonNegativeInteger(result, "history_removed");
            var workspaceRestored = RequireBoolean(result, "workspace_restored");
            var historyRewound = RequireBoolean(result, "history_rewound");
            var partial = RequireBoolean(result, "partial");
            var error = RequireBoundedContent(
                result,
                "error",
                MaximumRollbackReasonUtf8Bytes,
                allowEmpty: succeeded);
            if (succeeded)
            {
                if (!IsRollbackHashPrefix(restoredTo, hash))
                {
                    throw new InvalidDataException(
                        "rollback.restore target did not match the requested checkpoint.");
                }
                if (!workspaceRestored
                    || !historyRewound
                    || partial
                    || string.IsNullOrWhiteSpace(reason)
                    || error.Length != 0)
                {
                    throw new InvalidDataException(
                        "rollback.restore success effects were inconsistent.");
                }
            }
            else if (partial)
            {
                if (!workspaceRestored
                    || historyRewound
                    || historyRemoved != 0
                    || !IsRollbackHashPrefix(restoredTo, hash)
                    || string.IsNullOrWhiteSpace(reason))
                {
                    throw new InvalidDataException(
                        "rollback.restore partial effects were inconsistent.");
                }
            }
            else if (restoredTo.Length != 0
                     || reason.Length != 0
                     || workspaceRestored
                     || historyRewound
                     || historyRemoved != 0)
            {
                throw new InvalidDataException(
                    "rollback.restore failure reported mutation effects.");
            }

            ClearRollbackStateLocked(task);
            return new D046HermesRollbackRestoreReceipt(
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                _nextProjectionSequence,
                hash,
                succeeded,
                restoredTo,
                reason,
                historyRemoved,
                workspaceRestored,
                historyRewound,
                partial,
                error,
                SnapshotLocked());
        }
    }

    public bool TryDecodeVoiceEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope,
        out D046HermesVoiceEvent voiceEvent)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Method is not ("voice.status" or "voice.transcript"))
        {
            voiceEvent = null!;
            return false;
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var outer = RequireObject(message.HermesPayload, "Hermes voice event");
            ValidateExactProperties(
                outer,
                new[] { "type", "session_id", "payload" },
                message.Method + " event");
            if (!string.Equals(
                    RequireString(outer, "type"),
                    message.Method,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("Hermes voice event type was not preserved.");
            }
            var runtimeSessionId = RequireIdentity(
                RequireString(outer, "session_id"),
                "session_id");
            if (!_byRuntimeSessionId.TryGetValue(runtimeSessionId, out var task)
                || task is null
                || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
                || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            {
                throw new InvalidDataException(
                    "Hermes voice event was not correlated to the active task.");
            }

            var payload = RequirePayloadObject(outer, message.Method);
            D046HermesVoiceEventPayload decoded = message.Method switch
            {
                "voice.status" => DecodeVoiceStatusPayload(payload),
                "voice.transcript" => DecodeVoiceTranscriptPayload(payload),
                _ => throw new InvalidDataException("Hermes voice event was not recognized.")
            };
            voiceEvent = new D046HermesVoiceEvent(
                envelope.RequestId.ToString("N"),
                _runtimeGeneration,
                task.TaskId,
                task.RuntimeSessionId,
                envelope.Sequence,
                envelope.SentAtUtc,
                message.Method,
                decoded,
                message.HermesPayload.GetRawText());
            return true;
        }
    }

    public Task WaitForNoEffectSessionInfoAsync(
        string taskId,
        string runtimeSessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_byTaskId.TryGetValue(taskId, out var task)
                || !string.Equals(
                    task.RuntimeSessionId,
                    runtimeSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(_selectedTaskId, taskId, StringComparison.Ordinal)
                || task.NoEffectAttestation is null)
            {
                throw new InvalidOperationException(
                    "The no-effect session-info wait was not correlated.");
            }

            return task.NoEffectAttestation.Task.WaitAsync(cancellationToken);
        }
    }

    public bool TryAdmitReadOnlySessionAfterAttestationFailure(
        string taskId,
        string runtimeSessionId,
        D054HermesCredentialCatalog credentialCatalog)
    {
        ArgumentNullException.ThrowIfNull(credentialCatalog);
        lock (_gate)
        {
            if (!_byTaskId.TryGetValue(taskId, out var task)
                || !string.Equals(
                    task.RuntimeSessionId,
                    runtimeSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(_selectedTaskId, taskId, StringComparison.Ordinal)
                || task.NoEffectAttestation is null)
            {
                throw new InvalidOperationException(
                    "The read-only credential recovery was not correlated.");
            }

            if (task.NoEffectAttestation.Task.IsCompletedSuccessfully)
                return true;

            if (task.SubmitPending || task.AwaitingQueuedTurn
                || task.PendingInflight is not null || task.PendingTurnId.Length != 0
                || task.ActiveMessageIds.Count != 0 || task.ActiveReasoningIds.Count != 0
                || task.PendingApprovals.Count != 0 || task.PendingClarification is not null
                || task.PendingSecureInput is not null || task.PendingAttachedImagePaths.Count != 0
                || task.ApprovalResponseInFlight || task.ApprovalResponseQuarantined
                || task.ClarificationResponseInFlight || task.SecureInputResponseInFlight
                || task.NativeToolsConfigStale)
                return false;

            if (task.NoEffectAttestation.Task.IsFaulted
                && task.NoEffectAttestation.Task.Exception?.InnerException
                    is not D046HermesSessionActivationException)
                return false;

            var provider = credentialCatalog.Providers.SingleOrDefault(value =>
                string.Equals(
                    value.ProviderId,
                    task.ProviderId,
                    StringComparison.Ordinal));
            var noProviderConfigured = credentialCatalog.Providers.Count > 0
                && credentialCatalog.Providers.All(value => !value.Configured);
            if (provider?.Configured != false && !noProviderConfigured)
                return false;

            task.NoEffectAttestation.TrySetCanceled();
            task.CredentialUnavailable = true;
            task.State = D046HermesTaskState.Failed;
            task.CertifiedNativeToolNames.Clear();
            task.PendingApprovals.Clear();
            var providerName = !string.IsNullOrWhiteSpace(provider?.DisplayName)
                ? provider.DisplayName
                : !string.IsNullOrWhiteSpace(task.ProviderId)
                    ? task.ProviderId
                    : "原本的模型服務";
            _statusText = "這個聊天原本使用 "
                + providerName
                + "，目前尚未設定它的 API Key；請先在控制中心新增。";
            return true;
        }
    }

    public bool TryCoalesceStartupGlobalSessionInvalidation(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.Equals(message.Method, "sessions.changed", StringComparison.Ordinal))
            return false;

        lock (_gate)
        {
            // A selected task gives this global invalidation an exact projection
            // target, so the ordinary lossless event path must retain it.
            if (!string.IsNullOrEmpty(_selectedTaskId))
                return false;

            ObserveGenerationLocked(envelope);
            var outer = RequireObject(
                message.HermesPayload,
                "Hermes startup session invalidation");
            if (!string.Equals(
                    RequireString(outer, "type"),
                    message.Method,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Hermes startup session invalidation type was not preserved.");
            }
            if (RequireString(outer, "session_id", allowEmpty: true).Length != 0)
            {
                throw new InvalidDataException(
                    "The Hermes startup session invalidation was not global.");
            }
            _ = HermesTurnCorrelationContract.ValidateGatewayEvent(message.Method, outer);
            _ = RequirePayloadObject(outer, message.Method);
            return true;
        }
    }

    public bool TryDecodeEvent(
        LilithRuntimeHermesEvent message,
        D046HermesRuntimeEnvelopeContext envelope,
        out D046HermesProjectionEvent projectionEvent)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var outer = RequireObject(message.HermesPayload, "Hermes event");
            var type = RequireString(outer, "type");
            if (!string.Equals(type, message.Method, StringComparison.Ordinal))
                throw new InvalidDataException("The Hermes event type was not preserved.");

            if (!IsSupportedEvent(type))
            {
                if (IsAuthorityBearingEventFamily(type))
                {
                    throw new InvalidDataException(
                        "Hermes emitted an unknown authority-bearing event.");
                }
                projectionEvent = null!;
                return false;
            }

            var turnId = HermesTurnCorrelationContract.ValidateGatewayEvent(
                type,
                outer);

            string runtimeSessionId;
            TaskBinding task;
            if (type == "sessions.changed")
            {
                var globalSessionId = RequireString(outer, "session_id", allowEmpty: true);
                if (globalSessionId.Length != 0
                    || string.IsNullOrWhiteSpace(_selectedTaskId)
                    || !_byTaskId.TryGetValue(_selectedTaskId, out var selectedTask)
                    || selectedTask is null
                    || string.IsNullOrWhiteSpace(selectedTask.RuntimeSessionId))
                {
                    throw new InvalidDataException(
                        "The Hermes global session event was not correlated to the active task.");
                }
                task = selectedTask;
                runtimeSessionId = task.RuntimeSessionId;
            }
            else
            {
                runtimeSessionId = RequireIdentity(
                    RequireString(outer, "session_id"),
                    "session_id");
                if (!_byRuntimeSessionId.TryGetValue(runtimeSessionId, out var selectedTask)
                    || selectedTask is null
                    || !string.Equals(selectedTask.TaskId, _selectedTaskId, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The Hermes event session was not correlated to the active task.");
                }
                task = selectedTask;
            }

            var mutation = TaskEventMutationSnapshot.Capture(task);
            D046HermesProjectionPayload payload;
            D046HermesTaskState? effectiveTaskState;
            try
            {
                if (turnId.Length != 0)
                    AdmitTurnEventLocked(task, type, turnId, outer);

                payload = DecodeEventPayloadLocked(
                    type,
                    outer,
                    task,
                    envelope,
                    turnId);
                if (string.Equals(type, "message.complete", StringComparison.Ordinal))
                {
                    if (!task.CompletedTurnIds.Add(turnId))
                        throw new InvalidDataException("Hermes completed a turn more than once.");
                }
                if (PayloadCompletesTurn(payload)
                    && task.SubmitPending
                    && string.Equals(task.PendingTurnId, turnId, StringComparison.Ordinal))
                {
                    task.PendingTurnTerminalObserved = true;
                }
                effectiveTaskState = UpdateTaskStateFromPayloadLocked(
                    task,
                    payload,
                    preserveCompletedTurnState:
                        turnId.Length != 0
                        && task.CompletedTurnIds.Contains(turnId)
                        && !string.Equals(
                            type,
                            "message.complete",
                            StringComparison.Ordinal));
            }
            catch
            {
                mutation.Restore(task);
                throw;
            }
            var sequence = checked(++_nextProjectionSequence);
            projectionEvent = new D046HermesProjectionEvent(
                envelope.RequestId.ToString("N"),
                sequence,
                _runtimeGeneration,
                task.TaskId,
                runtimeSessionId,
                envelope.SentAtUtc,
                message.Method,
                payload,
                message.HermesPayload.GetRawText(),
                turnId,
                turnId.Length == 0
                    ? D046HermesEventScopeKind.Session
                    : D046HermesEventScopeKind.Turn,
                effectiveTaskState);
            task.Events.Add(projectionEvent);
            if (D052HermesConversationActivityPolicy
                .TryGetConversationActivityUtc(payload, out var activityUtc))
            {
                ObserveTaskActivityLocked(
                    task,
                    activityUtc,
                    sequence);
            }
            return true;
        }
    }

    private static bool PayloadCompletesTurn(D046HermesProjectionPayload payload)
        => payload is D046HermesMessagePayload { CompletesTurn: true }
            or D046HermesErrorPayload { CompletesTurn: true }
            or D046HermesTurnErrorPayload;

    private void DecodeRuntimeStatusLocked(
        JsonElement value,
        D046HermesRuntimeEnvelopeContext envelope)
    {
        var status = RequireObject(value, "Runtime status");
        var generation = RequireString(status, "generationId");
        if (!string.Equals(generation, envelope.GenerationId.ToString("N"), StringComparison.Ordinal))
            throw new InvalidDataException("Runtime status generation did not match.");
        var state = RequireString(status, "state");
        if (!string.Equals(state, "ready", StringComparison.Ordinal))
            throw new InvalidDataException("Runtime status was not ready.");
        if (!string.Equals(
                RequireString(status, "hermesCommit"),
                PinnedHermesCommit,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Runtime status used a different Hermes commit.");
        }
        var effectPolicy = RequireObjectProperty(status, "effectPolicy");
        ValidateExactProperties(
            effectPolicy,
            new[]
            {
                "mode",
                "approvalMode",
                "enabledToolsets",
                "newSessionRequired"
            },
            "Runtime native-effect policy");
        if (!string.Equals(
                RequireString(effectPolicy, "mode"),
                "hermes_native_config",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Runtime native-effect policy did not match the approved surface.");
        }
        _ = ParseNativeApprovalMode(RequireString(effectPolicy, "approvalMode"));
        var enabledToolsets = ReadUniqueIdentityArray(
            effectPolicy,
            "enabledToolsets",
            maximumCount: 256);
        var newSessionRequired = RequireBoolean(
            effectPolicy,
            "newSessionRequired");

        string? active = null;
        if (!status.TryGetProperty("activeSessionId", out var activeValue))
            throw new InvalidDataException("Runtime active session was missing.");
        if (activeValue.ValueKind == JsonValueKind.String)
            active = RequireIdentity(activeValue.GetString()!, "activeSessionId");
        else if (activeValue.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Runtime active session was malformed.");
        var attested = RequireBoolean(status, "activeSessionSurfaceAttested");

        _runtimeReady = true;
        _nativeApprovalPolicyReady = true;
        _computerUseEnabled = enabledToolsets.Contains(
            "computer_use",
            StringComparer.Ordinal);
        _statusText = state;
        _statusAttestedRuntimeSessionId = attested ? active : null;
        _runtimeInvalidatedNativeToolsSessionId = newSessionRequired
            ? active
            : null;
    }

    private void DecodeSessionListLocked(
        JsonElement value,
        D046HermesRuntimeEnvelopeContext envelope)
    {
        RequireRuntimeReadyLocked();
        var result = RequireObject(value, "session.list result");
        var sessions = RequireArray(result, "sessions");
        var seenStored = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<TaskBinding>();
        foreach (var item in sessions.EnumerateArray())
        {
            var session = RequireObject(item, "session.list item");
            var stored = RequireIdentity(RequireString(session, "id"), "id");
            if (!seenStored.Add(stored))
                throw new InvalidDataException("session.list contained a duplicate session.");
            var title = RequireString(session, "title", allowEmpty: true);
            var preview = RequireString(session, "preview", allowEmpty: true);
            _ = RequireUnixSeconds(session, "started_at", DateTimeOffset.UnixEpoch);
            _ = RequireUnixSeconds(
                session,
                "last_active_at",
                DateTimeOffset.UnixEpoch);
            var lastConversationAt = RequireUnixSeconds(
                session,
                "last_conversation_at",
                DateTimeOffset.UnixEpoch);
            _ = RequireNonNegativeInteger(session, "message_count");
            _ = RequireString(session, "source", allowEmpty: true);

            if (!_byStoredSessionId.TryGetValue(stored, out var task))
            {
                task = new TaskBinding(TaskIdFor(stored), stored);
                AddTaskLocked(task);
            }
            task.IsPinned = _pinnedTaskIds.Contains(task.TaskId);
            task.Title = DisplayTitle(title, preview);
            if (lastConversationAt > task.UpdatedUtc)
                task.UpdatedUtc = lastConversationAt;
            ordered.Add(task);
        }

        var selected = SelectedTaskLocked();
        if (selected is not null && !seenStored.Contains(selected.StoredSessionId))
            ordered.Add(selected);
        _orderedTasks = OrderTasks(ordered);
    }

    private void DecodeSessionCreateLocked(
        JsonElement value,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        RequireRuntimeReadyLocked();
        if (binding.Correlation is not null)
            throw new InvalidDataException("session.create carried an unexpected correlation.");
        var result = RequireObject(value, "session.create result");
        var runtime = RequireIdentity(
            RequireString(result, "session_id"),
            "session_id");
        var stored = RequireIdentity(
            RequireString(result, "stored_session_id"),
            "stored_session_id");
        var messageCount = RequireNonNegativeInteger(result, "message_count");
        var messages = RequireArray(result, "messages");
        if (messageCount < messages.GetArrayLength())
            throw new InvalidDataException("session.create message count was malformed.");
        var info = RequireObjectProperty(result, "info");
        var inspected = InspectActivationInfo(info);
        var createRequest = RequireObject(binding.HermesPayload, "session.create request");
        var hasRequestedProvider = createRequest.TryGetProperty("provider", out _);
        var hasRequestedModel = createRequest.TryGetProperty("model", out _);
        if (hasRequestedProvider != hasRequestedModel)
        {
            throw new InvalidDataException(
                "session.create carried a partial provider/model selection.");
        }
        if (hasRequestedProvider)
        {
            var requestedProvider = RequireIdentity(
                RequireString(createRequest, "provider"),
                "provider");
            var requestedModel = RequireIdentity(
                RequireString(createRequest, "model"),
                "model");
            if (!string.Equals(
                    inspected.Provider,
                    requestedProvider,
                    StringComparison.Ordinal)
                || !string.Equals(
                    inspected.Model,
                    requestedModel,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "session.create did not preserve the certified provider/model selection.");
            }
        }

        if (_byStoredSessionId.ContainsKey(stored))
            throw new InvalidDataException("session.create reused a durable session ID.");
        var task = new TaskBinding(TaskIdFor(stored), stored)
        {
            RuntimeSessionId = runtime,
            Title = "Untitled",
            ProviderId = inspected.Provider,
            ModelDisplayName = inspected.Model,
            State = D046HermesTaskState.Idle,
            UpdatedUtc = envelope.SentAtUtc,
            IsPinned = _pinnedTaskIds.Contains(TaskIdFor(stored)),
            CertifiedNativeToolNames = new HashSet<string>(
                inspected.ToolNames,
                StringComparer.Ordinal)
        };
        AttachRuntimeSessionLocked(task, runtime);
        AddTaskLocked(task);
        SelectActivatedTaskLocked(task, inspected.NoEffectAttested);
    }

    private void DecodeSessionResumeLocked(
        JsonElement value,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        RequireRuntimeReadyLocked();
        var correlation = RequireCorrelation(binding);
        var requestedTask = RequireCorrelatedTaskLocked(correlation, requireRuntime: false);
        var result = RequireObject(value, "session.resume result");
        var runtime = RequireIdentity(
            RequireString(result, "session_id"),
            "session_id");
        var resumed = RequireIdentity(RequireString(result, "resumed"), "resumed");
        var sessionKey = RequireIdentity(
            RequireString(result, "session_key"),
            "session_key");
        if (!string.Equals(resumed, sessionKey, StringComparison.Ordinal)
            || !string.Equals(resumed, requestedTask.StoredSessionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "session.resume did not echo the requested durable session.");
        }
        var messageCount = RequireNonNegativeInteger(result, "message_count");
        var messages = RequireArray(result, "messages");
        if (!RequireBoolean(result, "messages_omitted")
            || messages.GetArrayLength() != 0
            || messageCount < messages.GetArrayLength())
        {
            throw new InvalidDataException("session.resume omit_messages was not honored.");
        }
        _ = RequireBoolean(result, "running");
        var status = RequireString(result, "status");
        _ = RequireUnixSeconds(result, "started_at", envelope.SentAtUtc);
        var pendingInflight = DecodePendingInflightTurn(result);
        var pendingTerminalError = DecodePendingTerminalError(result);
        var pendingConstraints = DecodePendingBudgetConstraints(result);
        if (pendingInflight is not null && pendingTerminalError is not null)
        {
            throw new InvalidDataException(
                "session.resume returned both inflight and terminal_error state.");
        }
        var pendingQueuedPrompt = DecodePendingQueuedPrompt(result);
        var info = RequireObjectProperty(result, "info");
        var inspected = InspectActivationInfo(info);

        AttachRuntimeSessionLocked(requestedTask, runtime);
        requestedTask.ProviderId = inspected.Provider;
        requestedTask.ModelDisplayName = inspected.Model;
        requestedTask.CertifiedNativeToolNames = new HashSet<string>(
            inspected.ToolNames,
            StringComparer.Ordinal);
        var resumedState = status switch
        {
            "idle" => D046HermesTaskState.Idle,
            "streaming" or "working" or "waiting" or "starting" =>
                D046HermesTaskState.Streaming,
            _ => throw new InvalidDataException("session.resume status was malformed.")
        };
        requestedTask.State = pendingQueuedPrompt.Length == 0
            ? resumedState
            : D046HermesTaskState.Streaming;
        SelectActivatedTaskLocked(requestedTask, inspected.NoEffectAttested);
        requestedTask.PendingInflight = pendingInflight;
        requestedTask.PendingTerminalError = pendingTerminalError;
        requestedTask.PendingBudgetConstraints = pendingConstraints;
        requestedTask.PendingQueuedPromptText = pendingQueuedPrompt;
        requestedTask.AwaitingQueuedTurn = pendingQueuedPrompt.Length != 0;
        if (pendingInflight is not null)
        {
            requestedTask.KnownTurnIds.Add(pendingInflight.TurnId);
            requestedTask.ClaimedUserMessageIds[pendingInflight.TurnId] =
                pendingInflight.UserMessageId;
            requestedTask.ClaimedAssistantMessageIds[pendingInflight.TurnId] =
                pendingInflight.AssistantMessageId;
            if (pendingInflight.Error.Length != 0)
                requestedTask.CompletedTurnIds.Add(pendingInflight.TurnId);
        }
        if (pendingTerminalError is not null)
        {
            requestedTask.KnownTurnIds.Add(pendingTerminalError.TurnId);
            requestedTask.CompletedTurnIds.Add(pendingTerminalError.TurnId);
        }
    }

    private static PendingTerminalError? DecodePendingTerminalError(JsonElement result)
    {
        if (!result.TryGetProperty("terminal_error", out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        var terminal = RequireObject(value, "session.resume terminal_error");
        ValidateAllowedProperties(
            terminal,
            required: new[] { "error", "recoverable", "status", "turn_id" },
            allowed: new[] { "error", "recoverable", "status", "turn_id", "budget_error", "budget_constraints" },
            "session.resume terminal_error");
        var turnId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(terminal, "turn_id"),
            "terminal_error turn_id");
        if (!string.Equals(
                RequireString(terminal, "status"),
                "error",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("session.resume terminal_error status was malformed.");
        }
        return new PendingTerminalError(
            turnId,
            RequireContentString(terminal, "error"),
            RequireBoolean(terminal, "recoverable"),
            result.GetRawText(),
            DecodeBudgetError(terminal, isError: true),
            UsageBudgetConstraintPresentation.Decode(terminal));
    }

    private void DecodeSessionHistoryLocked(
        JsonElement value,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var correlation = RequireCorrelation(binding);
        var task = RequireCorrelatedTaskLocked(correlation);
        if (binding.CredentialRecoveryHistoryFence is { } recoveryFence)
            RequireCredentialRecoveryHistoryTaskLocked(correlation, recoveryFence);
        if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal))
            throw new InvalidDataException("session.history was not for the active task.");
        if (task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true
            && !task.CredentialUnavailable)
            throw new InvalidDataException("session.history preceded no-effect session.info.");

        var result = RequireObject(value, "session.history result");
        var count = RequireNonNegativeInteger(result, "count");
        var messages = RequireArray(result, "messages");
        if (count < messages.GetArrayLength())
            throw new InvalidDataException("session.history count was malformed.");

        var raw = responseRaw(value);
        if (correlation.ReplaceHistory)
        {
            var replacement = new TaskBinding(task.TaskId, task.StoredSessionId)
            {
                RuntimeSessionId = task.RuntimeSessionId,
                Title = task.Title,
                ProviderId = task.ProviderId,
                ModelDisplayName = task.ModelDisplayName,
                State = task.CredentialUnavailable
                    ? D046HermesTaskState.Failed
                    : D046HermesTaskState.Idle,
                UpdatedUtc = task.UpdatedUtc,
                IsPinned = task.IsPinned,
                NoEffectAttestation = task.NoEffectAttestation,
                CredentialUnavailable = task.CredentialUnavailable,
                PendingCredentialRecovery = task.PendingCredentialRecovery,
                PendingInflight = task.PendingInflight,
                PendingTerminalError = task.PendingTerminalError
            };
            var priorSequence = _nextProjectionSequence;
            try
            {
                var replacementIndex = 0;
                var historyUserTurns = new HashSet<string>(StringComparer.Ordinal);
                var historyPrimaryAssistantTurns = new HashSet<string>(StringComparer.Ordinal);
                var historyAuxiliaryAssistantMessages =
                    new HashSet<(string TurnId, string MessageId)>();
                foreach (var item in messages.EnumerateArray())
                {
                    DecodeHistoryMessageLocked(
                        replacement,
                        item,
                        replacementIndex,
                        envelope,
                        raw,
                        historyUserTurns: historyUserTurns,
                        historyPrimaryAssistantTurns: historyPrimaryAssistantTurns,
                        historyAuxiliaryAssistantMessages:
                            historyAuxiliaryAssistantMessages);
                    replacementIndex++;
                }
            }
            catch
            {
                _nextProjectionSequence = priorSequence;
                throw;
            }

            task.Events.Clear();
            task.Events.AddRange(replacement.Events);
            task.ActiveMessageIds.Clear();
            task.ActiveReasoningIds.Clear();
            task.KnownTurnIds.Clear();
            task.KnownTurnIds.UnionWith(replacement.KnownTurnIds);
            task.ClaimedUserMessageIds.Clear();
            foreach (var claim in replacement.ClaimedUserMessageIds)
                task.ClaimedUserMessageIds.Add(claim.Key, claim.Value);
            task.ClaimedAssistantMessageIds.Clear();
            foreach (var claim in replacement.ClaimedAssistantMessageIds)
                task.ClaimedAssistantMessageIds.Add(claim.Key, claim.Value);
            task.ClaimedAuxiliaryAssistantMessageIds.Clear();
            task.ClaimedAuxiliaryAssistantMessageIds.UnionWith(
                replacement.ClaimedAuxiliaryAssistantMessageIds);
            task.ClaimedInterimAssistantMessageIds.Clear();
            task.ClaimedInterimAssistantMessageIds.UnionWith(
                replacement.ClaimedInterimAssistantMessageIds);
            task.LastInterimAssistantMessageIds.Clear();
            foreach (var claim in replacement.LastInterimAssistantMessageIds)
                task.LastInterimAssistantMessageIds.Add(claim.Key, claim.Value);
            task.CompletedTurnIds.Clear();
            task.CompletedTurnIds.UnionWith(replacement.CompletedTurnIds);
            task.TerminalErrorMarkerTurnIds.Clear();
            task.TerminalErrorMarkerTurnIds.UnionWith(
                replacement.TerminalErrorMarkerTurnIds);
            task.TerminalErrorDisclosedTurnIds.Clear();
            task.TerminalErrorDisclosedTurnIds.UnionWith(
                replacement.TerminalErrorDisclosedTurnIds);
            task.PendingTerminalError = replacement.PendingTerminalError;
            task.PendingApprovals.Clear();
            task.State = task.CredentialUnavailable
                ? D046HermesTaskState.Failed : D046HermesTaskState.Idle;
            AppendPendingInflightLocked(task, envelope);
            RequirePendingTerminalErrorConsumed(task);
            AppendPendingQueuedPromptLocked(task, envelope);
            AppendPendingBudgetConstraintsLocked(task, envelope);
            return;
        }

        var itemIndex = 0;
        var appendedHistoryUserTurns = new HashSet<string>(StringComparer.Ordinal);
        var appendedHistoryPrimaryAssistantTurns =
            new HashSet<string>(StringComparer.Ordinal);
        var appendedHistoryAuxiliaryAssistantMessages =
            new HashSet<(string TurnId, string MessageId)>();
        foreach (var item in messages.EnumerateArray())
        {
            DecodeHistoryMessageLocked(
                task,
                item,
                itemIndex,
                envelope,
                raw,
                historyUserTurns: appendedHistoryUserTurns,
                historyPrimaryAssistantTurns: appendedHistoryPrimaryAssistantTurns,
                historyAuxiliaryAssistantMessages:
                    appendedHistoryAuxiliaryAssistantMessages);
            itemIndex++;
        }
        AppendPendingInflightLocked(task, envelope);
        RequirePendingTerminalErrorConsumed(task);
        AppendPendingQueuedPromptLocked(task, envelope);
        AppendPendingBudgetConstraintsLocked(task, envelope);
    }

    private static PendingBudgetConstraints? DecodePendingBudgetConstraints(JsonElement result)
    {
        var hasValues = result.TryGetProperty("budget_constraints", out _);
        var hasTurn = result.TryGetProperty("budget_constraints_turn_id", out _);
        if (!hasValues && !hasTurn) return null;
        if (!hasValues || !hasTurn
            || result.EnumerateObject().Count(p => p.Name == "budget_constraints_turn_id") != 1)
            throw new InvalidDataException("Native economic constraint source was malformed.");
        var turnId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(result, "budget_constraints_turn_id"), "budget_constraints_turn_id");
        return new(turnId, UsageBudgetConstraintPresentation.Decode(result), result.GetRawText());
    }

    private void AppendPendingBudgetConstraintsLocked(TaskBinding task, D046HermesRuntimeEnvelopeContext envelope)
    {
        var pending = task.PendingBudgetConstraints;
        task.PendingBudgetConstraints = null;
        if (pending is null || pending.Constraints.Count == 0) return;
        // This is read-only terminal metadata, not completion or message content.
        // A resume-side annotation must match a turn independently recovered from history.
        if (!task.KnownTurnIds.Contains(pending.TurnId))
            throw new InvalidDataException("Native economic constraint source turn was not recovered.");
        AddHistoryEventLocked(task, envelope, int.MaxValue, "budget-constraints",
            new D046HermesBudgetConstraintsPayload(pending.Constraints), pending.CanonicalPayloadJson,
            SessionResumeMethod, pending.TurnId, D046HermesEventScopeKind.Turn);
    }

    private static void RequirePendingTerminalErrorConsumed(TaskBinding task)
    {
        if (task.PendingTerminalError is not null)
        {
            throw new InvalidDataException(
                "session.resume terminal_error had no exact durable assistant marker.");
        }
    }

    private static string DecodePendingQueuedPrompt(JsonElement result)
    {
        if (!result.TryGetProperty("queued", out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        var queued = RequireObject(value, "session.resume queued");
        ValidateExactProperties(
            queued,
            new[] { "user" },
            "session.resume queued");
        return RequireContentString(queued, "user");
    }

    private static PendingInflightTurn? DecodePendingInflightTurn(JsonElement result)
    {
        if (!result.TryGetProperty("inflight", out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        var inflight = RequireObject(value, "session.resume inflight");
        foreach (var property in inflight.EnumerateObject())
        {
            if (property.Name is not ("assistant" or "streaming" or "turn_id"
                or "user_message_id" or "assistant_message_id" or "user"
                or "corrections" or "error" or "status" or "recoverable" or "budget_error" or "budget_constraints"))
            {
                throw new InvalidDataException("session.resume inflight was not exact.");
            }
        }
        var turnId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(inflight, "turn_id"),
            "inflight turn_id");
        var userMessageId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(inflight, "user_message_id"),
            "inflight user_message_id");
        var assistantMessageId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(inflight, "assistant_message_id"),
            "inflight assistant_message_id");
        if (new HashSet<string>(StringComparer.Ordinal)
            {
                turnId,
                userMessageId,
                assistantMessageId
            }.Count != 3)
        {
            throw new InvalidDataException(
                "Inflight turn, user-message, and assistant-message identities must differ.");
        }
        var user = RequireContentString(inflight, "user", allowEmpty: true);
        var assistant = RequireContentString(inflight, "assistant", allowEmpty: true);
        var streaming = RequireBoolean(inflight, "streaming");
        if (user.Length == 0 && assistant.Length == 0 && !streaming)
            throw new InvalidDataException("session.resume inflight was empty.");
        IReadOnlyList<string> decodedCorrections = Array.Empty<string>();
        if (inflight.TryGetProperty("corrections", out var corrections))
        {
            if (corrections.ValueKind != JsonValueKind.Array
                || corrections.EnumerateArray().Any(item =>
                    item.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(item.GetString())))
            {
                throw new InvalidDataException("session.resume inflight corrections were malformed.");
            }
            decodedCorrections = corrections.EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray();
        }
        var hasError = inflight.TryGetProperty("error", out var errorValue);
        var error = hasError
            ? RequireContentStringValue(errorValue, "error")
            : string.Empty;
        var hasStatus = inflight.TryGetProperty("status", out var statusValue);
        var hasRecoverable = inflight.TryGetProperty("recoverable", out var recoverableValue);
        if (hasError)
        {
            if (!hasStatus
                || statusValue.ValueKind != JsonValueKind.String
                || !string.Equals(statusValue.GetString(), "error", StringComparison.Ordinal)
                || !hasRecoverable
                || recoverableValue.ValueKind is not (
                    JsonValueKind.True or JsonValueKind.False))
            {
                throw new InvalidDataException(
                    "session.resume inflight error union was malformed.");
            }
        }
        else if (hasStatus || hasRecoverable)
        {
            throw new InvalidDataException(
                "session.resume inflight carried orphaned error state.");
        }
        return new PendingInflightTurn(
            turnId,
            userMessageId,
            assistantMessageId,
            user,
            assistant,
            streaming,
            decodedCorrections,
            error,
            hasRecoverable && recoverableValue.GetBoolean(),
            result.GetRawText(),
            DecodeBudgetError(inflight, isError: hasError),
            UsageBudgetConstraintPresentation.Decode(inflight));
    }

    private void AppendPendingInflightLocked(
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope)
    {
        var inflight = task.PendingInflight;
        task.PendingInflight = null;
        if (inflight is null)
            return;

        task.KnownTurnIds.Add(inflight.TurnId);
        task.ClaimedUserMessageIds[inflight.TurnId] = inflight.UserMessageId;
        task.ClaimedAssistantMessageIds[inflight.TurnId] =
            inflight.AssistantMessageId;
        if (inflight.User.Length != 0
            && !task.Events.Any(value => value.Payload is D046HermesMessagePayload
                {
                    Role: D046HermesMessageRole.User
                } message
                && string.Equals(
                    value.TurnId,
                    inflight.TurnId,
                    StringComparison.Ordinal)
                && string.Equals(
                    message.MessageId,
                    inflight.UserMessageId,
                    StringComparison.Ordinal)))
        {
            AddHistoryEventLocked(
                task,
                envelope,
                int.MaxValue - 1,
                "inflight-user",
                new D046HermesMessagePayload(
                    inflight.UserMessageId,
                    D046HermesMessageRole.User,
                    D046HermesStreamOperation.Completed,
                    inflight.User),
                inflight.CanonicalPayloadJson,
                SessionResumeMethod,
                inflight.TurnId,
                D046HermesEventScopeKind.Turn);
        }
        for (var correctionIndex = 0;
             correctionIndex < inflight.Corrections.Count;
             correctionIndex++)
        {
            AddHistoryEventLocked(
                task,
                envelope,
                int.MaxValue - inflight.Corrections.Count + correctionIndex,
                "inflight-correction-"
                    + correctionIndex.ToString(CultureInfo.InvariantCulture),
                new D046HermesMetadataPayload(
                    "prompt.correction",
                    inflight.Corrections[correctionIndex]),
                inflight.CanonicalPayloadJson,
                SessionResumeMethod,
                inflight.TurnId,
                D046HermesEventScopeKind.Turn);
        }
        if (inflight.Assistant.Length != 0)
        {
            AddHistoryEventLocked(
                task,
                envelope,
                int.MaxValue,
                "inflight-assistant",
                new D046HermesMessagePayload(
                    inflight.AssistantMessageId,
                    D046HermesMessageRole.Assistant,
                    inflight.Streaming && inflight.Error.Length == 0
                        ? D046HermesStreamOperation.Replaced
                        : D046HermesStreamOperation.Completed,
                    inflight.Assistant,
                    MessageCompletionConfirmed: !inflight.Streaming && inflight.Error.Length == 0,
                    BudgetConstraints: inflight.Error.Length == 0 ? inflight.BudgetConstraints : null),
                inflight.CanonicalPayloadJson,
                SessionResumeMethod,
                inflight.TurnId,
                D046HermesEventScopeKind.Turn);
            if (inflight.Streaming && inflight.Error.Length == 0)
            {
                // The resume snapshot may contain the primary segment that was
                // already streamed on the previous transport. Rebuild its live
                // identity so the causally next already_streamed interim can
                // rekey that exact segment instead of faulting the projection.
                task.ActiveMessageIds[inflight.TurnId] = inflight.AssistantMessageId;
            }
        }
        if (inflight.Error.Length != 0)
        {
            task.CompletedTurnIds.Add(inflight.TurnId);
            if (task.TerminalErrorDisclosedTurnIds.Add(inflight.TurnId))
            {
                AddHistoryEventLocked(
                    task,
                    envelope,
                    int.MaxValue,
                    "inflight-error",
                    new D046HermesTurnErrorPayload(
                        inflight.AssistantMessageId,
                        inflight.Assistant,
                        "inflight.error",
                        inflight.Error,
                        inflight.Recoverable,
                        Partial: inflight.Assistant.Length != 0,
                        BudgetError: inflight.BudgetError,
                        BudgetConstraints: inflight.BudgetConstraints),
                    inflight.CanonicalPayloadJson,
                    SessionResumeMethod,
                    inflight.TurnId,
                    D046HermesEventScopeKind.Turn);
            }
        }
    }

    private void AppendPendingQueuedPromptLocked(
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope)
    {
        var text = task.PendingQueuedPromptText;
        task.PendingQueuedPromptText = string.Empty;
        if (text.Length == 0)
            return;

        AddHistoryEventLocked(
            task,
            envelope,
            int.MaxValue,
            "queued-prompt",
            new D046HermesMetadataPayload("prompt.queued", text),
            JsonSerializer.Serialize(new { queued = new { user = text } }),
            SessionResumeMethod,
            turnId: string.Empty,
            turnScope: D046HermesEventScopeKind.Session);
    }

    private void DecodeHistoryMessageLocked(
        TaskBinding task,
        JsonElement value,
        int itemIndex,
        D046HermesRuntimeEnvelopeContext envelope,
        string canonicalPayloadJson,
        string sourceMethod = SessionHistoryMethod,
        HashSet<string>? historyUserTurns = null,
        HashSet<string>? historyPrimaryAssistantTurns = null,
        HashSet<(string TurnId, string MessageId)>?
            historyAuxiliaryAssistantMessages = null)
    {
        var item = RequireObject(value, "session.history message");
        var occurredUtc = OptionalHistoryUnixSeconds(
            item,
            "timestamp",
            envelope.SentAtUtc);
        var role = RequireString(item, "role");
        var hasTurnId = item.TryGetProperty("turn_id", out var turnIdValue);
        var hasMessageId = item.TryGetProperty("message_id", out var messageIdValue);
        if (hasTurnId != hasMessageId)
        {
            throw new InvalidDataException(
                "Stored Hermes message correlation was partially missing.");
        }
        var turnId = hasTurnId
            ? HermesTurnCorrelationContract.RequireIdentity(
                RequireStringValue(turnIdValue, "turn_id"),
                "history turn_id")
            : string.Empty;
        var messageId = hasMessageId
            ? HermesTurnCorrelationContract.RequireIdentity(
                RequireStringValue(messageIdValue, "message_id"),
                "history message_id")
            : HistoryMessageId(item, itemIndex);
        if (turnId.Length != 0
            && string.Equals(turnId, messageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Stored Hermes message and turn identities must differ.");
        }
        if (turnId.Length == 0
            && !string.Equals(sourceMethod, SessionHistoryMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Only durable session.history rows may use legacy unscoped projection.");
        }
        var turnScope = turnId.Length == 0
            ? D046HermesEventScopeKind.LegacyUnscoped
            : D046HermesEventScopeKind.Turn;
        if (turnId.Length != 0)
            task.KnownTurnIds.Add(turnId);
        var messageKind = item.TryGetProperty("message_kind", out var kindValue)
            ? RequireStringValue(kindValue, "message_kind")
            : string.Empty;
        if (messageKind.Length != 0
            && messageKind is not ("interim" or "primary" or "tool"
                or "tool_call" or "user"))
        {
            throw new InvalidDataException(
                "Stored Hermes message kind was not recognized.");
        }
        if (messageKind.Length != 0 && turnId.Length == 0)
        {
            throw new InvalidDataException(
                "Stored Hermes message_kind lacked canonical correlation.");
        }

        if (string.Equals(role, "tool", StringComparison.Ordinal))
        {
            _ = DecodeHistoryTerminalErrorMarker(
                item,
                D046HermesMessageRole.System,
                messageKind,
                turnId,
                messageId,
                task);
            if (messageKind.Length != 0
                && !string.Equals(messageKind, "tool", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Stored Hermes tool role disagreed with message_kind.");
            }
            var name = RequireString(item, "name");
            var context = RequireString(item, "context", allowEmpty: true);
            if (item.TryGetProperty("args", out var args)
                && args.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Stored tool arguments were malformed.");
            }
            string toolCallId;
            if (turnScope == D046HermesEventScopeKind.LegacyUnscoped)
            {
                if (item.TryGetProperty("status", out var legacyStatus)
                    && (legacyStatus.ValueKind != JsonValueKind.String
                        || !string.Equals(
                            legacyStatus.GetString(),
                            "complete",
                            StringComparison.Ordinal)))
                {
                    throw new InvalidDataException("Stored legacy tool status was malformed.");
                }
                var legacyToolId = item.TryGetProperty("tool_call_id", out var legacyToolIdValue)
                    ? RequireIdentity(
                        RequireStringValue(legacyToolIdValue, "tool_call_id"),
                        "tool_call_id")
                    : "history-tool-" + itemIndex.ToString(CultureInfo.InvariantCulture);
                toolCallId = messageId + "-" + legacyToolId;
            }
            else
            {
                _ = RequireObjectProperty(item, "args");
                if (!string.Equals(
                        RequireString(item, "status"),
                        "complete",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Stored tool status was not complete.");
                }
                toolCallId = RequireIdentity(
                    RequireString(item, "tool_call_id"),
                    "tool_call_id");
            }
            var summary = item.TryGetProperty("summary", out var summaryValue)
                ? RequireContentStringValue(summaryValue, "summary", allowEmpty: true)
                : context;
            if (summary.Length == 0)
                summary = context;
            var certifiedStoredName = RequireIdentity(name, "stored tool name");
            var audioFilePath = item.TryGetProperty("result", out var storedResult)
                ? TryDecodeTtsAudioFilePath(certifiedStoredName, storedResult)
                : string.Empty;
            AddHistoryEventLocked(
                task,
                envelope,
                itemIndex,
                "tool-" + messageId,
                new D046HermesToolPayload(
                    toolCallId,
                    name,
                    D046HermesToolPhase.Completed,
                    summary,
                    IsHostEffectTool: IsPotentialHostEffectTool(
                        certifiedStoredName),
                    AudioFilePath: audioFilePath,
                    ClarificationAnswer: D052HermesClarificationResultDisplay.TryRead(certifiedStoredName, storedResult),
                    GeneratedImage: turnScope == D046HermesEventScopeKind.Turn
                        ? D046HermesGeneratedImagePresentation.TryRead(certifiedStoredName, storedResult) : null),
                canonicalPayloadJson,
                sourceMethod,
                turnId,
                turnScope,
                occurredUtc);
            return;
        }

        var projectionRole = role switch
        {
            "user" => D046HermesMessageRole.User,
            "assistant" => D046HermesMessageRole.Assistant,
            "system" => D046HermesMessageRole.System,
            _ => throw new InvalidDataException("Stored message role was not recognized.")
        };
        var text = RequireContentString(item, "text", allowEmpty: true);
        if ((projectionRole == D046HermesMessageRole.User
                && messageKind.Length != 0
                && !string.Equals(messageKind, "user", StringComparison.Ordinal))
            || (projectionRole == D046HermesMessageRole.Assistant
                && messageKind.Length != 0
                && messageKind is not ("primary" or "interim" or "tool_call"))
            || (projectionRole == D046HermesMessageRole.System
                && messageKind.Length != 0))
        {
            throw new InvalidDataException(
                "Stored Hermes role disagreed with message_kind.");
        }
        if (projectionRole == D046HermesMessageRole.User && turnId.Length != 0)
        {
            if (historyUserTurns is not null && !historyUserTurns.Add(turnId))
            {
                throw new InvalidDataException(
                    "Stored Hermes turn contained multiple user rows.");
            }
            if (task.ClaimedAuxiliaryAssistantMessageIds.Contains((turnId, messageId))
                || (task.ClaimedAssistantMessageIds.TryGetValue(
                    turnId,
                    out var claimedAssistantMessageId)
                && string.Equals(
                    claimedAssistantMessageId,
                    messageId,
                    StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Stored Hermes user and primary assistant identities were aliased.");
            }
            if (task.ClaimedUserMessageIds.TryGetValue(turnId, out var claimedMessageId)
                && !string.Equals(claimedMessageId, messageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Stored Hermes turn contained multiple user-message identities.");
            }
            task.ClaimedUserMessageIds[turnId] = messageId;
        }
        if (projectionRole == D046HermesMessageRole.Assistant && turnId.Length != 0)
        {
            if (string.Equals(messageKind, "primary", StringComparison.Ordinal))
            {
                if (historyPrimaryAssistantTurns is not null
                    && !historyPrimaryAssistantTurns.Add(turnId))
                {
                    throw new InvalidDataException(
                        "Stored Hermes turn contained multiple primary assistant rows.");
                }
                if (task.ClaimedAuxiliaryAssistantMessageIds.Contains((turnId, messageId))
                    || (task.ClaimedUserMessageIds.TryGetValue(
                        turnId,
                        out var claimedUserMessageId)
                    && string.Equals(
                        claimedUserMessageId,
                        messageId,
                        StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(
                        "Stored Hermes primary assistant identity was aliased.");
                }
                if (task.ClaimedAssistantMessageIds.TryGetValue(
                        turnId,
                        out var claimedAssistantMessageId)
                    && !string.Equals(
                        claimedAssistantMessageId,
                        messageId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Stored Hermes turn contained multiple primary assistant identities.");
                }
                task.ClaimedAssistantMessageIds[turnId] = messageId;
                task.CompletedTurnIds.Add(turnId);
            }
            else if (messageKind.Length == 0
                && task.ClaimedAssistantMessageIds.TryGetValue(
                    turnId,
                    out var legacyClaimedPrimaryMessageId)
                && string.Equals(
                    legacyClaimedPrimaryMessageId,
                    messageId,
                    StringComparison.Ordinal))
            {
                // Patch-2-era rows carried a turn identity but no message_kind.
                // A resume receipt may still provide the exact primary identity.
            }
            else if (task.ClaimedUserMessageIds.TryGetValue(
                         turnId,
                         out var userMessageId)
                     && string.Equals(userMessageId, messageId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Stored Hermes auxiliary assistant identity was duplicated or aliased.");
            }
            else
            {
                var claim = (TurnId: turnId, MessageId: messageId);
                if (historyAuxiliaryAssistantMessages is not null
                    && !historyAuxiliaryAssistantMessages.Add(claim))
                {
                    throw new InvalidDataException(
                        "Stored Hermes auxiliary assistant identity was duplicated or aliased.");
                }
                if (!task.ClaimedAuxiliaryAssistantMessageIds.Add(claim))
                {
                    var isInterim = string.Equals(
                        messageKind,
                        "interim",
                        StringComparison.Ordinal);
                    var exactReplay = task.Events.Any(value =>
                        string.Equals(value.TurnId, turnId, StringComparison.Ordinal)
                        && value.Payload is D046HermesMessagePayload
                        {
                            Role: D046HermesMessageRole.Assistant
                        } existing
                        && string.Equals(
                            existing.MessageId,
                            messageId,
                            StringComparison.Ordinal)
                        && existing.IsInterim == isInterim
                        && string.Equals(existing.Text, text, StringComparison.Ordinal));
                    if (!exactReplay)
                    {
                        throw new InvalidDataException(
                            "Stored Hermes auxiliary assistant identity changed during replay.");
                    }
                }
                if (string.Equals(messageKind, "interim", StringComparison.Ordinal))
                {
                    task.ClaimedInterimAssistantMessageIds.Add(claim);
                    task.LastInterimAssistantMessageIds[turnId] = messageId;
                }
            }
        }
        var terminalErrorMarker = DecodeHistoryTerminalErrorMarker(
            item,
            projectionRole,
            messageKind,
            turnId,
            messageId,
            task);
        if (projectionRole == D046HermesMessageRole.Assistant)
        {
            var reasoning = HistoryReasoningText(item);
            if (!string.IsNullOrEmpty(reasoning))
            {
                AddHistoryEventLocked(
                    task,
                    envelope,
                    itemIndex,
                    "reasoning-" + messageId,
                    new D046HermesReasoningPayload(
                        messageId + "-reasoning",
                        D046HermesStreamOperation.Completed,
                        reasoning),
                    canonicalPayloadJson,
                    sourceMethod,
                    turnId,
                    turnScope,
                    occurredUtc);
            }

            if (item.TryGetProperty("tool_calls", out var toolCalls))
            {
                if (toolCalls.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Stored assistant tool calls were malformed.");
                var toolIndex = 0;
                foreach (var toolCallValue in toolCalls.EnumerateArray())
                {
                    var toolCall = RequireObject(toolCallValue, "stored assistant tool call");
                    ValidateExactProperties(
                        toolCall,
                        new[] { "tool_id", "name", "args", "context" },
                        "stored assistant tool call");
                    var originalToolCallId = RequireIdentity(
                        RequireString(toolCall, "tool_id"),
                        "tool_id");
                    var toolCallId = turnScope == D046HermesEventScopeKind.LegacyUnscoped
                        ? messageId + "-" + originalToolCallId
                        : originalToolCallId;
                    var name = RequireString(toolCall, "name");
                    _ = RequireObjectProperty(toolCall, "args");
                    var context = RequireString(toolCall, "context", allowEmpty: true);
                    AddHistoryEventLocked(
                        task,
                        envelope,
                        itemIndex,
                        "tool-call-"
                            + toolIndex.ToString(CultureInfo.InvariantCulture)
                            + "-"
                            + messageId,
                        new D046HermesToolPayload(
                            toolCallId,
                            name,
                            D046HermesToolPhase.Started,
                            context,
                            IsHostEffectTool: IsPotentialHostEffectTool(
                                RequireIdentity(name, "stored tool name"))),
                        canonicalPayloadJson,
                        sourceMethod,
                        turnId,
                        turnScope,
                        occurredUtc);
                    toolIndex++;
                }
            }
        }

        if (text.Length != 0)
        {
            AddHistoryEventLocked(
                task,
                envelope,
                itemIndex,
                "message",
                new D046HermesMessagePayload(
                    messageId,
                    projectionRole,
                    D046HermesStreamOperation.Completed,
                    text,
                    IsInterim: string.Equals(
                        messageKind,
                        "interim",
                        StringComparison.Ordinal),
                    MessageCompletionConfirmed: !terminalErrorMarker
                        && turnScope == D046HermesEventScopeKind.Turn
                        && projectionRole == D046HermesMessageRole.Assistant
                        && (messageKind is "primary" or "interim")),
                canonicalPayloadJson,
                sourceMethod,
                turnId,
                turnScope,
                occurredUtc);
        }
        if (terminalErrorMarker)
        {
            AddHistoryTerminalErrorLocked(
                task,
                envelope,
                itemIndex,
                messageId,
                canonicalPayloadJson,
                sourceMethod,
                turnId,
                turnScope,
                occurredUtc);
        }
    }

    private static bool DecodeHistoryTerminalErrorMarker(
        JsonElement item,
        D046HermesMessageRole role,
        string messageKind,
        string turnId,
        string messageId,
        TaskBinding task)
    {
        if (!item.TryGetProperty("display_metadata", out var metadata)
            || metadata.ValueKind == JsonValueKind.Null)
        {
            return false;
        }
        if (metadata.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Stored display_metadata was malformed.");
        if (!metadata.TryGetProperty("terminal_error_turn_id", out var marker))
            return false;
        var markedTurnId = HermesTurnCorrelationContract.RequireIdentity(
            RequireStringValue(marker, "terminal_error_turn_id"),
            "terminal_error_turn_id");
        if (role != D046HermesMessageRole.Assistant
            || turnId.Length == 0
            || !string.Equals(markedTurnId, turnId, StringComparison.Ordinal)
            || (messageKind.Length != 0
                && !string.Equals(messageKind, "primary", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Stored terminal-error marker was not bound to its primary assistant row.");
        }
        if ((task.ClaimedUserMessageIds.TryGetValue(turnId, out var userMessageId)
                && string.Equals(userMessageId, messageId, StringComparison.Ordinal))
            || !task.TerminalErrorMarkerTurnIds.Add(turnId))
        {
            throw new InvalidDataException(
                "Stored terminal-error marker was duplicated or aliased.");
        }
        task.CompletedTurnIds.Add(turnId);
        return true;
    }

    private void AddHistoryTerminalErrorLocked(
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope,
        int itemIndex,
        string assistantMessageId,
        string canonicalPayloadJson,
        string sourceMethod,
        string turnId,
        D046HermesEventScopeKind turnScope,
        DateTimeOffset occurredUtc)
    {
        var pendingInflight = task.PendingInflight;
        var pendingTerminal = task.PendingTerminalError;
        var matchedInflight = pendingInflight is not null
            && pendingInflight.Error.Length != 0
            && string.Equals(pendingInflight.TurnId, turnId, StringComparison.Ordinal);
        var matchedTerminal = pendingTerminal is not null
            && string.Equals(pendingTerminal.TurnId, turnId, StringComparison.Ordinal);
        var errorText = matchedInflight
            ? pendingInflight!.Error
            : matchedTerminal
                ? pendingTerminal!.Error
                : "這個回合未完整完成；已保留可安全顯示的內容。";
        var recoverable = matchedInflight
            ? pendingInflight!.Recoverable
            : matchedTerminal
                ? pendingTerminal!.Recoverable
                : true;
        if (!task.TerminalErrorDisclosedTurnIds.Add(turnId))
            return;
        AddHistoryEventLocked(
            task,
            envelope,
            itemIndex,
            "terminal-error",
            new D046HermesTurnErrorPayload(
                assistantMessageId,
                string.Empty,
                "history.terminal_error",
                errorText,
                recoverable,
                Partial: false,
                IsHistoryMarker: true,
                BudgetError: matchedInflight ? pendingInflight!.BudgetError
                    : matchedTerminal ? pendingTerminal!.BudgetError : null,
                BudgetConstraints: matchedInflight ? pendingInflight!.BudgetConstraints
                    : matchedTerminal ? pendingTerminal!.BudgetConstraints : null),
            canonicalPayloadJson,
            sourceMethod,
            turnId,
            turnScope,
            occurredUtc);
        if (matchedTerminal)
            task.PendingTerminalError = null;
    }

    private void AddHistoryEventLocked(
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope,
        int itemIndex,
        string suffix,
        D046HermesProjectionPayload payload,
        string canonicalPayloadJson,
        string sourceMethod,
        string turnId,
        D046HermesEventScopeKind turnScope,
        DateTimeOffset? occurredUtc = null)
    {
        var sequence = checked(++_nextProjectionSequence);
        task.Events.Add(new D046HermesProjectionEvent(
            envelope.RequestId.ToString("N")
                + "-"
                + itemIndex.ToString(CultureInfo.InvariantCulture)
                + "-"
                + suffix,
            sequence,
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            occurredUtc ?? envelope.SentAtUtc,
            sourceMethod,
            payload,
            canonicalPayloadJson,
            turnId,
            turnScope));
    }

    private D046HermesProjectionPayload DecodeEventPayloadLocked(
        string type,
        JsonElement outer,
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope,
        string turnId)
        => type switch
        {
            "session.info" => DecodeSessionInfoEventLocked(outer, task),
            "session.title" => DecodeSessionTitleEventLocked(outer, task),
            HermesTurnCorrelationContract.CronCompletionEventType
                => DecodeCronCompletionEvent(outer),
            "message.user" => DecodeUserMessageEvent(outer, turnId),
            "message.start" or "message.delta" or "message.interim" or "message.complete"
                => DecodeMessageEventLocked(type, outer, task, envelope, turnId),
            "thinking.delta" => DecodeMetadataEvent(type, outer),
            "reasoning.delta" or "reasoning.available"
                => DecodeReasoningEventLocked(type, outer, task, envelope, turnId),
            "status.update" => DecodeStatusEvent(outer, task),
            "tool.start" or "tool.progress" or "tool.complete" or "tool.generating"
                or "tool.output_risk" => DecodeToolEvent(type, outer, envelope, task),
            "subagent.start" or "subagent.thinking" or "subagent.tool"
                or "subagent.progress" or "subagent.complete"
                => DecodeSubagentEvent(type, outer),
            "clarify.request" or "clarify.expire"
                => DecodeClarificationEvent(type, outer, envelope, task),
            "approval.request" or "sudo.request" or "sudo.expire"
                or "secret.request" or "secret.expire"
                => DecodeApprovalEventLocked(type, outer, envelope, task),
            "error" => DecodeErrorEventLocked(outer, task, turnId),
            "notification.show" or "notification.clear" or "reaction"
                or "sessions.changed"
                or "background.complete" or "review.summary"
                => DecodeMetadataEvent(type, outer),
            _ => throw new InvalidDataException("The Hermes event was not recognized.")
        };

    private static D046HermesProjectionPayload DecodeUserMessageEvent(
        JsonElement outer,
        string turnId)
    {
        var payload = RequirePayloadObject(outer, "message.user");
        ValidateExactProperties(
            payload,
            new[] { "message_id", "text", "durable" },
            "message.user payload");
        var messageId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(payload, "message_id"),
            "message.user message_id");
        if (string.Equals(messageId, turnId, StringComparison.Ordinal))
            throw new InvalidDataException("Hermes user message and turn identities must differ.");
        return new D046HermesMessagePayload(
            messageId,
            D046HermesMessageRole.User,
            D046HermesStreamOperation.Completed,
            RequireContentString(payload, "text"),
            Durable: DecodeDurableMessageProvenance(
                RequireObjectProperty(payload, "durable"),
                "message.user durable provenance"));
    }

    private static D046HermesSubagentPayload DecodeSubagentEvent(
        string type,
        JsonElement outer)
    {
        var payload = RequirePayloadObject(outer, type);
        var goal = RequireContentString(payload, "goal", allowEmpty: true);
        var taskCount = RequirePositiveInteger(payload, "task_count");
        var taskIndex = RequireNonNegativeInteger(payload, "task_index");
        if (taskIndex >= taskCount)
            throw new InvalidDataException("Hermes subagent task index was invalid.");

        var subagentId = payload.TryGetProperty("subagent_id", out var subagentValue)
            ? RequireIdentity(
                RequireStringValue(subagentValue, "subagent_id"),
                "subagent_id")
            : throw new InvalidDataException("Hermes subagent event omitted its identity.");
        var phase = type switch
        {
            "subagent.start" => D046HermesSubagentEventPhase.Started,
            "subagent.thinking" => D046HermesSubagentEventPhase.Thinking,
            "subagent.tool" or "subagent.progress"
                => D046HermesSubagentEventPhase.Tool,
            "subagent.complete" => D046HermesSubagentEventPhase.Completed,
            _ => throw new InvalidDataException("Hermes subagent event was not recognized.")
        };
        var state = phase == D046HermesSubagentEventPhase.Completed
            ? DecodeSubagentCompletionState(OptionalText(payload, "status"))
            : D046HermesSubagentState.Running;
        double? duration = null;
        if (payload.TryGetProperty("duration_seconds", out _))
        {
            duration = RequireFiniteNumber(payload, "duration_seconds");
            if (duration < 0)
                throw new InvalidDataException("Hermes subagent duration was invalid.");
        }

        var display = FirstContentString(
            payload,
            "summary",
            "text",
            "tool_preview")
            ?? FirstString(payload, "tool_name")
            ?? goal;
        if (display.Length > 512)
            display = display[..512];
        return new D046HermesSubagentPayload(
            phase,
            new D046HermesSubagentCardProjection(
                subagentId,
                OptionalNullableIdentity(payload, "parent_id"),
                OptionalNullableIdentity(payload, "child_session_id"),
                goal,
                OptionalNullableIdentity(payload, "model"),
                state,
                OptionalNonNegativeInteger(payload, "depth"),
                OptionalNonNegativeInteger(payload, "tool_count"),
                null,
                duration,
                RequireExactDelegationChildToolsets(payload)),
            display);
    }

    private D046HermesProjectionPayload DecodeSessionInfoEventLocked(
        JsonElement outer,
        TaskBinding task)
    {
        var info = RequirePayloadObject(outer, "session.info");
        return DecodeSessionInfoPayloadLocked(info, task);
    }

    private D046HermesSessionPayload DecodeSessionInfoPayloadLocked(
        JsonElement info,
        TaskBinding task)
    {
        var stored = RequireIdentity(
            RequireString(info, "stored_session_id"),
            "stored_session_id");
        if (!string.Equals(stored, task.StoredSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("session.info durable session did not match.");
        var model = RequireIdentity(RequireString(info, "model"), "model");
        var provider = RequireIdentity(
            RequireString(info, "provider"),
            "provider");
        var running = RequireBoolean(info, "running");
        var title = RequireString(info, "title", allowEmpty: true);
        task.Usage = info.TryGetProperty("usage", out var usage)
            ? ParseUsage(usage)
            : UnavailableUsage("awaiting_session_info");
        var tools = RequireObjectProperty(info, "tools");
        var mcpServers = RequireArray(info, "mcp_servers");
        IReadOnlyList<string> certifiedNativeToolNames;
        try
        {
            certifiedNativeToolNames = ReadAttestedNativeToolSurface(tools);
            ValidateAttestedNativeMcpSurface(mcpServers);
        }
        catch (InvalidDataException)
        {
            RejectNativeSurfaceLocked(
                task,
                "Hermes exposed a malformed native tool or MCP surface.");
            throw;
        }
        if (info.TryGetProperty("lazy", out var lazy)
            && lazy.ValueKind != JsonValueKind.False)
        {
            RejectNativeSurfaceLocked(task, "Hermes session.info was lazy or malformed.");
            throw new InvalidDataException("Hermes session.info was not authoritative.");
        }

        if (task.CredentialUnavailable
            && (task.PendingCredentialRecovery is not { } expected
                || expected != new ModelPair(provider, model) || running))
        {
            throw new InvalidDataException("Credential recovery session.info did not match the selected model.");
        }

        var change = !string.Equals(task.ProviderId, provider, StringComparison.Ordinal)
                || !string.Equals(task.ModelDisplayName, model, StringComparison.Ordinal)
            ? D046HermesSessionChange.ModelChanged
            : !string.Equals(task.Title, DisplayTitle(title, string.Empty), StringComparison.Ordinal)
                ? D046HermesSessionChange.Renamed
                : D046HermesSessionChange.Opened;
        task.ProviderId = provider;
        task.ModelDisplayName = model;
        task.Title = DisplayTitle(title, string.Empty);
        task.State = running
            ? D046HermesTaskState.Streaming
            : D046HermesTaskState.Idle;
        if (!running && !task.ApprovalResponseInFlight)
            task.PendingApprovals.Clear();
        task.CertifiedNativeToolNames = new HashSet<string>(
            certifiedNativeToolNames,
            StringComparer.Ordinal);
        task.CredentialUnavailable = false;
        task.PendingCredentialRecovery = null;
        _statusText = "ready";
        task.NoEffectAttestation?.TrySetResult(true);
        return new D046HermesSessionPayload(
            change,
            task.Title,
            task.StoredSessionId,
            task.ProviderId,
            task.ModelDisplayName,
            task.State);
    }

    private static D046HermesProjectionPayload DecodeSessionTitleEventLocked(
        JsonElement outer,
        TaskBinding task)
    {
        var payload = RequirePayloadObject(outer, "session.title");
        var storedSessionId = RequireIdentity(
            RequireString(payload, "session_id"),
            "session_id");
        if (!string.Equals(storedSessionId, task.StoredSessionId, StringComparison.Ordinal))
            throw new InvalidDataException("session.title durable session did not match.");
        var title = RequireString(payload, "title");
        task.Title = DisplayTitle(title, string.Empty);
        return new D046HermesSessionPayload(
            D046HermesSessionChange.Renamed,
            task.Title,
            task.StoredSessionId,
            task.ProviderId,
            task.ModelDisplayName,
            task.State);
    }

    private static D046HermesProjectionPayload DecodeMetadataEvent(
        string type,
        JsonElement outer)
    {
        var payload = RequirePayloadObject(outer, type);
        var displayText = FirstString(
            payload,
            "text",
            "message",
            "title",
            "kind",
            "key",
            "status") ?? type;
        if (displayText.Length > 256)
            displayText = displayText[..256];
        return new D046HermesMetadataPayload(type, displayText);
    }

    private static D046HermesProjectionPayload DecodeCronCompletionEvent(
        JsonElement outer)
    {
        var payload = RequirePayloadObject(
            outer,
            HermesTurnCorrelationContract.CronCompletionEventType);
        ValidateExactProperties(
            payload,
            new[] { "schema", "success", "text" },
            "cron.complete payload");
        if (!string.Equals(
                RequireString(payload, "schema"),
                HermesTurnCorrelationContract.CronCompletionSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "cron.complete payload schema was malformed.");
        }
        return new D046HermesCronCompletionPayload(
            RequireContentString(payload, "text"),
            RequireBoolean(payload, "success"));
    }

    private static D046HermesProjectionPayload DecodeMessageEventLocked(
        string type,
        JsonElement outer,
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope,
        string turnId)
    {
        var payload = RequirePayloadObject(outer, type);
        var messageId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(payload, "message_id"),
            type + " message_id");
        if (type == "message.start")
        {
            ValidateExactProperties(
                payload,
                new[] { "message_id" },
                "message.start payload");
            if (task.ActiveMessageIds.TryGetValue(turnId, out var startedMessageId)
                && !string.Equals(
                    startedMessageId,
                    messageId,
                    StringComparison.Ordinal))
                throw new InvalidDataException("message.start payload was malformed.");
            task.ActiveReasoningIds.Remove(turnId);
            task.ActiveMessageIds[turnId] = messageId;
            return new D046HermesMessagePayload(
                messageId,
                D046HermesMessageRole.Assistant,
                D046HermesStreamOperation.Started,
                string.Empty);
        }
        if (type == "message.interim")
        {
            ValidateExactProperties(
                payload,
                new[] { "message_id", "text", "already_streamed" },
                "message.interim payload");
            var interimText = RequireContentString(payload, "text", allowEmpty: true);
            var alreadyStreamed = RequireBoolean(payload, "already_streamed");
            var primaryMessageId = string.Empty;
            if (alreadyStreamed
                && (!task.ClaimedAssistantMessageIds.TryGetValue(
                        turnId,
                        out primaryMessageId)
                    || !task.ActiveMessageIds.TryGetValue(
                        turnId,
                        out var activePrimaryMessageId)
                    || !string.Equals(
                        primaryMessageId,
                        activePrimaryMessageId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "message.interim could not identify the streamed primary segment.");
            }
            if (alreadyStreamed)
                task.ActiveMessageIds.Remove(turnId);
            return new D046HermesMessagePayload(
                messageId,
                D046HermesMessageRole.Assistant,
                D046HermesStreamOperation.Completed,
                interimText,
                IsInterim: true,
                AlreadyStreamed: alreadyStreamed,
                PrimaryMessageId: primaryMessageId,
                MessageCompletionConfirmed: true);
        }

        var text = RequireContentString(payload, "text", allowEmpty: true);

        if (task.ActiveMessageIds.TryGetValue(turnId, out var activeMessageId)
            && !string.Equals(activeMessageId, messageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The primary assistant message identity changed mid-stream.");
        }
        task.ActiveMessageIds[turnId] = messageId;
        if (type == "message.delta")
        {
            ValidateAllowedProperties(
                payload,
                required: new[] { "message_id", "text" },
                allowed: new[] { "message_id", "text", "rendered" },
                "message.delta payload");
            if (payload.TryGetProperty("rendered", out var rendered)
                && rendered.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("message.delta rendered text was malformed.");
            return new D046HermesMessagePayload(
                messageId,
                D046HermesMessageRole.Assistant,
                D046HermesStreamOperation.Appended,
                text);
        }

        if (type != "message.complete")
            throw new InvalidDataException("Hermes message event was not recognized.");
        ValidateAllowedProperties(
            payload,
            required: new[] { "message_id", "text", "status" },
            allowed: new[]
            {
                "message_id", "text", "status", "rendered", "usage", "reasoning",
                "warning", "response_previewed", "billing", "failure_reason",
                "error", "recoverable", "partial", "durable", "budget_error", "budget_constraints"
            },
            "message.complete payload");
        if (payload.TryGetProperty("rendered", out var completeRendered)
            && completeRendered.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("message.complete rendered text was malformed.");
        }
        if (payload.TryGetProperty("usage", out var usage)
            && usage.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("message.complete usage was malformed.");
        }
        if (payload.TryGetProperty("reasoning", out var reasoning)
            && reasoning.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("message.complete reasoning was malformed.");
        }
        var warning = payload.TryGetProperty("warning", out var warningValue)
            ? RequireContentStringValue(
                warningValue,
                "message.complete warning",
                allowEmpty: true)
            : string.Empty;
        var hasResponsePreviewed = payload.TryGetProperty(
            "response_previewed",
            out var responsePreviewedValue);
        var responsePreviewed = hasResponsePreviewed
            ? RequireBooleanValue(
                responsePreviewedValue,
                "message.complete response_previewed")
            : false;
        if (payload.TryGetProperty("billing", out var billing)
            && billing.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("message.complete billing was malformed.");
        }
        if (payload.TryGetProperty("failure_reason", out var failureReason)
            && failureReason.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException("message.complete failure reason was malformed.");
        }

        var status = RequireString(payload, "status");
        if (status is not ("complete" or "interrupted" or "error"))
            throw new InvalidDataException("message.complete status was not recognized.");
        var budgetError = DecodeBudgetError(payload, isError: status == "error");
        var budgetConstraints = UsageBudgetConstraintPresentation.Decode(payload);
        D046HermesDurableMessageProvenance? durable = null;
        if (payload.TryGetProperty("durable", out var durableValue))
        {
            if (status != "complete")
            {
                throw new InvalidDataException(
                    "Only a completed assistant message may carry durable conversation provenance.");
            }
            durable = DecodeDurableMessageProvenance(
                durableValue,
                "message.complete durable provenance");
            if (text.Length == 0)
            {
                throw new InvalidDataException(
                    "An empty assistant message cannot carry durable conversation provenance.");
            }
        }
        if (status == "error")
        {
            if (hasResponsePreviewed)
            {
                throw new InvalidDataException(
                    "message.complete error cannot claim a previewed response.");
            }
            var errorText = RequireContentString(payload, "error");
            var recoverable = RequireBoolean(payload, "recoverable");
            var partial = false;
            if (payload.TryGetProperty("partial", out var partialValue))
            {
                partial = RequireBooleanValue(
                    partialValue,
                    "message.complete partial");
                if (!partial)
                {
                    throw new InvalidDataException(
                        "message.complete partial must be true when present.");
                }
                if (text.Length == 0)
                {
                    throw new InvalidDataException(
                        "message.complete partial text was empty.");
                }
            }
            task.ActiveMessageIds.Remove(turnId);
            task.ActiveReasoningIds.Remove(turnId);
            task.TerminalErrorDisclosedTurnIds.Add(turnId);
            return new D046HermesTurnErrorPayload(
                messageId,
                text,
                "message.error",
                errorText,
                recoverable,
                partial,
                Warning: warning,
                BudgetError: budgetError,
                BudgetConstraints: budgetConstraints);
        }
        if (payload.TryGetProperty("error", out _)
            || payload.TryGetProperty("recoverable", out _)
            || payload.TryGetProperty("partial", out _))
        {
            throw new InvalidDataException(
                "message.complete terminal union was malformed.");
        }

        var previewMessageId = string.Empty;
        if (responsePreviewed)
        {
            if (!task.LastInterimAssistantMessageIds.TryGetValue(
                    turnId,
                    out previewMessageId)
                || !task.ClaimedInterimAssistantMessageIds.Contains(
                    (turnId, previewMessageId)))
            {
                throw new InvalidDataException(
                    "message.complete preview had no causally registered interim message.");
            }
        }

        task.ActiveMessageIds.Remove(turnId);
        task.ActiveReasoningIds.Remove(turnId);
        return new D046HermesMessagePayload(
            messageId,
            D046HermesMessageRole.Assistant,
            D046HermesStreamOperation.Completed,
            text,
            CompletesTurn: true,
            ResponsePreviewed: responsePreviewed,
            PreviewMessageId: previewMessageId,
            Warning: warning,
            Durable: durable,
            MessageCompletionConfirmed: status == "complete",
            BudgetConstraints: budgetConstraints);
    }

    private static D046HermesDurableMessageProvenance DecodeDurableMessageProvenance(
        JsonElement value,
        string context)
    {
        var durable = RequireObject(value, context);
        ValidateExactProperties(
            durable,
            new[]
            {
                "source", "row_id", "timestamp", "conversation_activity",
                "turn_origin", "turn_origin_kind"
            },
            context);
        if (!string.Equals(
                RequireString(durable, "source"),
                "state.db",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(context + " source was not authoritative.");
        }
        if (!durable.TryGetProperty("row_id", out var rowValue)
            || rowValue.ValueKind != JsonValueKind.Number
            || !rowValue.TryGetInt64(out var rowId)
            || rowId <= 0)
        {
            throw new InvalidDataException(context + " row id was malformed.");
        }
        var timestamp = RequireFiniteNumber(durable, "timestamp");
        if (timestamp <= 0)
            throw new InvalidDataException(context + " timestamp was malformed.");
        DateTimeOffset timestampUtc;
        try
        {
            var ticks = checked((long)Math.Round(
                timestamp * TimeSpan.TicksPerSecond,
                MidpointRounding.AwayFromZero));
            timestampUtc = DateTimeOffset.UnixEpoch.AddTicks(ticks);
        }
        catch (Exception exception) when (
            exception is OverflowException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                context + " timestamp was outside the supported range.",
                exception);
        }
        var conversationActivity = RequireBoolean(
            durable,
            "conversation_activity");
        var turnOrigin = RequireString(durable, "turn_origin");
        if (turnOrigin is not ("user" or "synthetic"))
            throw new InvalidDataException(context + " turn origin was malformed.");
        if (conversationActivity && turnOrigin != "user")
        {
            throw new InvalidDataException(
                context + " synthetic origin cannot claim conversation activity.");
        }
        var turnOriginKind = RequireString(
            durable,
            "turn_origin_kind",
            allowEmpty: true);
        return new D046HermesDurableMessageProvenance(
            "state.db",
            rowId,
            timestampUtc,
            conversationActivity,
            turnOrigin,
            turnOriginKind);
    }

    private static D046HermesProjectionPayload DecodeReasoningEventLocked(
        string type,
        JsonElement outer,
        TaskBinding task,
        D046HermesRuntimeEnvelopeContext envelope,
        string turnId)
    {
        var payload = RequirePayloadObject(outer, type);
        var text = RequireContentString(payload, "text", allowEmpty: true);
        if (type == "reasoning.available"
            && payload.TryGetProperty("verbose", out var verbose)
            && verbose.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("reasoning.available verbose flag was malformed.");
        }
        if (!task.ActiveReasoningIds.TryGetValue(turnId, out var activeReasoningId))
        {
            activeReasoningId = envelope.RequestId.ToString("N");
            task.ActiveReasoningIds[turnId] = activeReasoningId;
        }
        var operation = type == "reasoning.available"
            ? D046HermesStreamOperation.Completed
            : D046HermesStreamOperation.Appended;
        var id = activeReasoningId;
        if (operation == D046HermesStreamOperation.Completed)
            task.ActiveReasoningIds.Remove(turnId);
        return new D046HermesReasoningPayload(id, operation, text);
    }

    private D046HermesProjectionPayload DecodeStatusEvent(
        JsonElement outer,
        TaskBinding task)
    {
        var payload = RequirePayloadObject(outer, "status.update");
        var kind = RequireString(payload, "kind");
        var text = RequireContentString(payload, "text");
        return new D046HermesStatusPayload(
            kind,
            text,
            task.State,
            _runtimeReady,
            task.NoEffectAttestation?.Task.IsCompletedSuccessfully == true
                && !task.NativeToolsConfigStale,
            _computerUseEnabled);
    }

    private static D046HermesProjectionPayload DecodeToolEvent(
        string type,
        JsonElement outer,
        D046HermesRuntimeEnvelopeContext envelope,
        TaskBinding task)
    {
        var payload = RequirePayloadObject(outer, type);
        var fallbackId = envelope.RequestId.ToString("N");
        return type switch
        {
            "tool.generating" => DecodeToolGenerating(payload, fallbackId, task),
            "tool.start" => DecodeToolStart(payload, task),
            "tool.complete" => DecodeToolComplete(payload, task),
            "tool.progress" => DecodeToolProgress(payload, fallbackId, task),
            "tool.output_risk" => DecodeToolOutputRisk(payload, fallbackId, task),
            _ => throw new InvalidDataException("Tool event was not recognized.")
        };
    }

    private static D046HermesProjectionPayload DecodeToolGenerating(
        JsonElement payload,
        string fallbackId,
        TaskBinding task)
    {
        var name = RequireIdentity(RequireString(payload, "name"), "tool name");
        // Generation is untrusted model intent, emitted before Hermes validates
        // the tool call. Observe an unknown proposal without certifying it as a
        // tool lifecycle event; actual start/progress/complete remain attested.
        if (!task.CertifiedNativeToolNames.Contains(name))
        {
            return new D046HermesMetadataPayload(
                "tool.unavailable_proposal",
                "工具「" + name + "」目前不可用，尚未執行。");
        }
        return new D046HermesToolPayload(
            fallbackId,
            name,
            D046HermesToolPhase.Proposed,
            "Generating tool call",
            IsHostEffectTool: IsPotentialHostEffectTool(
                RequireCertifiedNativeToolName(task, name)));
    }

    private static D046HermesToolPayload DecodeToolStart(
        JsonElement payload,
        TaskBinding task)
    {
        var toolId = RequireIdentity(RequireString(payload, "tool_id"), "tool_id");
        var name = RequireString(payload, "name");
        var context = RequireString(payload, "context", allowEmpty: true);
        if (payload.TryGetProperty("args", out var args)
            && args.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("tool.start arguments were malformed.");
        return new D046HermesToolPayload(
            toolId,
            name,
            D046HermesToolPhase.Started,
            context,
            IsHostEffectTool: IsPotentialHostEffectTool(
                RequireCertifiedNativeToolName(task, name)));
    }

    private static D046HermesToolPayload DecodeToolComplete(
        JsonElement payload,
        TaskBinding task)
    {
        var toolId = RequireIdentity(RequireString(payload, "tool_id"), "tool_id");
        var name = RequireString(payload, "name");
        var args = RequireObjectProperty(payload, "args");
        _ = args;
        if (!payload.TryGetProperty("result", out var result)
            || result.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("tool.complete result was missing.");
        var summary = payload.TryGetProperty("summary", out var summaryValue)
            ? RequireStringValue(summaryValue, "summary", allowEmpty: true)
            : SafeDisplay(result);
        var certifiedName = RequireCertifiedNativeToolName(task, name);
        return new D046HermesToolPayload(
            toolId,
            name,
            D046HermesToolPhase.Completed,
            summary,
            IsHostEffectTool: IsPotentialHostEffectTool(certifiedName),
            AudioFilePath: TryDecodeTtsAudioFilePath(certifiedName, result),
            GeneratedImage: D046HermesGeneratedImagePresentation.TryRead(certifiedName, result));
    }

    private static string TryDecodeTtsAudioFilePath(
        string certifiedToolName,
        JsonElement result)
    {
        if (!string.Equals(
                certifiedToolName,
                "text_to_speech",
                StringComparison.Ordinal))
        {
            return string.Empty;
        }

        JsonDocument? parsed = null;
        try
        {
            var value = result;
            if (value.ValueKind == JsonValueKind.String)
            {
                var encoded = value.GetString();
                if (string.IsNullOrWhiteSpace(encoded))
                    return string.Empty;
                parsed = JsonDocument.Parse(encoded);
                value = parsed.RootElement;
            }

            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("success", out var success)
                || success.ValueKind != JsonValueKind.True)
            {
                return string.Empty;
            }

            if (value.TryGetProperty("delivery_file_count", out var deliveryCount)
                && (deliveryCount.ValueKind != JsonValueKind.Number
                    || !deliveryCount.TryGetInt32(out var count)
                    || count != 1))
            {
                return string.Empty;
            }

            var filePath = value.TryGetProperty("file_path", out var pathValue)
                && pathValue.ValueKind == JsonValueKind.String
                    ? pathValue.GetString() ?? string.Empty
                    : string.Empty;
            if (value.TryGetProperty("file_paths", out var pathValues))
            {
                if (pathValues.ValueKind != JsonValueKind.Array
                    || pathValues.GetArrayLength() != 1)
                {
                    return string.Empty;
                }
                var onlyPath = pathValues[0];
                if (onlyPath.ValueKind != JsonValueKind.String)
                    return string.Empty;
                var arrayPath = onlyPath.GetString() ?? string.Empty;
                if (filePath.Length == 0)
                    filePath = arrayPath;
                else if (!string.Equals(
                    filePath,
                    arrayPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
            }
            var mediaPath = string.Empty;
            if (value.TryGetProperty("media_tag", out var mediaValue))
            {
                if (mediaValue.ValueKind != JsonValueKind.String)
                    return string.Empty;
                var mediaTag = mediaValue.GetString() ?? string.Empty;
                var mediaLines = mediaTag.Replace(
                        "\r\n",
                        "\n",
                        StringComparison.Ordinal)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var mediaLineIndex = mediaLines.Length == 2
                    && string.Equals(
                        mediaLines[0].Trim(),
                        "[[audio_as_voice]]",
                        StringComparison.Ordinal)
                        ? 1
                        : 0;
                if (mediaLines.Length != mediaLineIndex + 1
                    || !mediaLines[mediaLineIndex].StartsWith(
                        "MEDIA:",
                        StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                mediaPath = mediaLines[mediaLineIndex]["MEDIA:".Length..];
                if (string.IsNullOrWhiteSpace(mediaPath))
                    return string.Empty;
            }

            if (filePath.Length == 0)
                filePath = mediaPath;
            else if (mediaPath.Length != 0
                && !string.Equals(
                    filePath,
                    mediaPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (filePath.Length == 0
                || filePath.Length > 1024
                || !Path.IsPathFullyQualified(filePath))
            {
                return string.Empty;
            }

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension is not (".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac"))
            {
                return string.Empty;
            }

            return Path.GetFullPath(filePath);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (NotSupportedException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (System.Security.SecurityException)
        {
            return string.Empty;
        }
        finally
        {
            parsed?.Dispose();
        }
    }

    private static D046HermesToolPayload DecodeToolProgress(
        JsonElement payload,
        string fallbackId,
        TaskBinding task)
    {
        var name = payload.TryGetProperty("name", out var nameValue)
            ? RequireStringValue(nameValue, "name", allowEmpty: true)
            : string.Empty;
        var preview = payload.TryGetProperty("preview", out var previewValue)
            ? RequireStringValue(previewValue, "preview", allowEmpty: true)
            : string.Empty;
        if (name.Length == 0 && preview.Length == 0)
            throw new InvalidDataException("tool.progress did not contain progress.");
        return new D046HermesToolPayload(
            fallbackId,
            name.Length == 0 ? "tool" : name,
            D046HermesToolPhase.Started,
            preview,
            IsHostEffectTool: name.Length != 0
                && IsPotentialHostEffectTool(
                    RequireCertifiedNativeToolName(task, name)));
    }

    private static D046HermesToolPayload DecodeToolOutputRisk(
        JsonElement payload,
        string fallbackId,
        TaskBinding task)
    {
        var toolId = payload.TryGetProperty("tool_id", out var idValue)
            ? RequireStringValue(idValue, "tool_id", allowEmpty: true)
            : fallbackId;
        var name = RequireString(payload, "name");
        var risk = RequireString(payload, "risk");
        _ = RequireArray(payload, "findings");
        _ = RequireBoolean(payload, "redacted");
        return new D046HermesToolPayload(
            toolId.Length == 0 ? fallbackId : toolId,
            name,
            D046HermesToolPhase.Rejected,
            risk,
            IsHostEffectTool: IsPotentialHostEffectTool(
                RequireCertifiedNativeToolName(task, name)));
    }

    private static D046HermesProjectionPayload DecodeClarificationEvent(
        string type,
        JsonElement outer,
        D046HermesRuntimeEnvelopeContext envelope,
        TaskBinding task)
    {
        var payload = RequirePayloadObject(outer, type);
        var requestId = RequireIdentity(
            RequireString(payload, "request_id"),
            "request_id");
        if (type == "clarify.expire")
        {
            ValidateExactProperties(
                payload,
                new[] { "request_id" },
                "clarify.expire payload");
            if (task.PendingClarification is { } pending
                && string.Equals(
                    pending.ClarificationId,
                    requestId,
                    StringComparison.Ordinal))
            {
                task.PendingClarification = null;
            }
            return new D046HermesClarificationPayload(
                requestId,
                D046HermesClarificationState.Cancelled,
                string.Empty,
                Array.Empty<string>(),
                MultiSelect: false);
        }

        ValidateAllowedProperties(
            payload,
            required: new[] { "request_id", "question", "choices" },
            allowed: new[] { "request_id", "question", "choices", "multi_select" },
            "clarify.request payload");
        var question = RequireString(payload, "question");
        if (!payload.TryGetProperty("choices", out var choices)
            || choices.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
            throw new InvalidDataException("clarify.request choices were malformed.");
        var orderedChoices = new List<string>();
        if (choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                var value = RequireStringValue(choice, "choice");
                if (string.IsNullOrWhiteSpace(value)
                    || orderedChoices.Contains(value, StringComparer.Ordinal))
                {
                    throw new InvalidDataException("clarify.request choices were malformed.");
                }
                orderedChoices.Add(value);
            }
        }
        var isMultiSelect = false;
        if (payload.TryGetProperty("multi_select", out var multiSelect)
            && multiSelect.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("clarify.request multi-select flag was malformed.");
        if (multiSelect.ValueKind is JsonValueKind.True or JsonValueKind.False)
            isMultiSelect = multiSelect.GetBoolean();
        if (isMultiSelect && orderedChoices.Count == 0)
            throw new InvalidDataException("clarify.request multi-select omitted its choices.");
        var projectedChoices = orderedChoices.AsReadOnly();
        var clarification = new D046HermesClarificationPayload(
            requestId,
            D046HermesClarificationState.Requested,
            question,
            projectedChoices,
            isMultiSelect);
        task.PendingClarification = new D046HermesPendingClarificationProjection(
            envelope.RequestId.ToString("N"),
            requestId,
            question,
            projectedChoices,
            isMultiSelect);
        return clarification;
    }

    private D046HermesProjectionPayload DecodeApprovalEventLocked(
        string type,
        JsonElement outer,
        D046HermesRuntimeEnvelopeContext envelope,
        TaskBinding task)
    {
        var payload = RequirePayloadObject(outer, type);
        if (type == "approval.expire")
        {
            return new D046HermesApprovalPayload(
                RequireIdentity(RequireString(payload, "request_id"), "request_id"),
                D046HermesApprovalState.Expired,
                string.Empty,
                Array.Empty<string>());
        }
        if (type == "approval.request")
        {
            if (task.NativeToolsConfigStale
                && !task.NativeToolsConfigStaleTurnActive)
            {
                throw new InvalidDataException(
                    "Hermes emitted a native approval after the config-stale turn ended.");
            }
            ValidateAllowedProperties(
                payload,
                required: new[] { "pattern_key", "choices" },
                allowed: new[]
                {
                    "pattern_key",
                    "pattern_keys",
                    "command",
                    "description",
                    "allow_permanent",
                    "allow_session",
                    "smart_denied",
                    "choices"
                },
                "approval.request payload");
            var patternKey = RequireBoundedIdentity(payload, "pattern_key", 256);
            var choices = RequireArray(payload, "choices");
            var observed = new HashSet<string>(StringComparer.Ordinal);
            var orderedChoices = new List<string>();
            foreach (var choice in choices.EnumerateArray())
            {
                var value = RequireStringValue(choice, "choice");
                if (!observed.Add(value)
                    || value is not ("once" or "session" or "always" or "deny"))
                {
                    throw new InvalidDataException("approval.request choices were malformed.");
                }
                orderedChoices.Add(value);
            }
            if (orderedChoices.Count == 0)
                throw new InvalidDataException("approval.request omitted its choices.");

            ValidateOptionalApprovalPatternKeys(payload, patternKey);
            ValidateOptionalBoundedApprovalText(payload, "command", 16 * 1024);
            ValidateOptionalBoundedApprovalText(payload, "description", 16 * 1024);
            ValidateOptionalBoolean(payload, "allow_permanent");
            ValidateOptionalBoolean(payload, "allow_session");
            ValidateOptionalBoolean(payload, "smart_denied");

            var command = OptionalContentString(payload, "command") ?? string.Empty;
            var description = OptionalContentString(payload, "description") ?? string.Empty;
            var allowPermanent = OptionalBooleanValue(payload, "allow_permanent");
            var allowSession = OptionalBooleanValue(payload, "allow_session");
            var smartDenied = OptionalBooleanValue(payload, "smart_denied");
            var summary = FirstContentString(payload, "description", "command")
                ?? patternKey;
            var approvalId = envelope.RequestId.ToString("N");
            if (task.ApprovalResponseQuarantined)
                throw new InvalidDataException("Native approval delivery was quarantined.");
            if (task.PendingApprovals.Count
                    >= D046HermesWorkbenchSnapshotApprovalValidator
                        .MaximumPendingApprovalCount
                || task.PendingApprovals.Any(candidate => string.Equals(
                        candidate.EventId,
                        approvalId,
                        StringComparison.Ordinal)
                    || string.Equals(
                        candidate.ApprovalId,
                        approvalId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Native approval delivery exceeded its unique FIFO bound.");
            }
            var approvedChoices = orderedChoices.AsReadOnly();
            task.PendingApprovals.Enqueue(new D046HermesPendingApprovalProjection(
                approvalId,
                approvalId,
                summary,
                approvedChoices,
                command,
                description,
                patternKey,
                allowSession,
                allowPermanent,
                smartDenied));
            return new D046HermesApprovalPayload(
                approvalId,
                D046HermesApprovalState.Requested,
                summary,
                approvedChoices,
                command,
                description,
                patternKey,
                allowSession,
                allowPermanent,
                smartDenied);
        }

        var requestId = RequireIdentity(
            RequireString(payload, "request_id"),
            "request_id");
        if (type.EndsWith(".expire", StringComparison.Ordinal))
        {
            ValidateExactProperties(
                payload,
                new[] { "request_id" },
                type + " payload");
            if (task.PendingSecureInput is { } pending
                && string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            {
                task.PendingSecureInput = null;
            }
            return new D046HermesSecureInputPayload(
                requestId,
                D046HermesSecureInputState.Cancelled,
                type.StartsWith("sudo.", StringComparison.Ordinal)
                    ? D046HermesSecureInputKind.SudoPassword
                    : D046HermesSecureInputKind.SecretValue,
                string.Empty);
        }
        if (type == "sudo.request")
        {
            ValidateExactProperties(
                payload,
                new[] { "request_id" },
                "sudo.request payload");
            var projected = new D046HermesPendingSecureInputProjection(
                envelope.RequestId.ToString("N"),
                requestId,
                D046HermesSecureInputKind.SudoPassword,
                "需要系統管理員密碼才能繼續。",
                string.Empty);
            task.PendingSecureInput = projected;
            return new D046HermesSecureInputPayload(
                requestId,
                D046HermesSecureInputState.Requested,
                projected.InputKind,
                projected.Prompt);
        }
        ValidateAllowedProperties(
            payload,
            required: new[] { "request_id", "prompt", "env_var" },
            allowed: new[] { "request_id", "prompt", "env_var", "metadata" },
            "secret.request payload");
        var prompt = RequireString(payload, "prompt");
        var environmentVariable = RequireString(payload, "env_var");
        var secret = new D046HermesPendingSecureInputProjection(
            envelope.RequestId.ToString("N"),
            requestId,
            D046HermesSecureInputKind.SecretValue,
            prompt,
            environmentVariable);
        task.PendingSecureInput = secret;
        return new D046HermesSecureInputPayload(
            requestId,
            D046HermesSecureInputState.Requested,
            secret.InputKind,
            prompt,
            environmentVariable);
    }

    private static D046HermesProjectionPayload DecodeErrorEventLocked(
        JsonElement outer,
        TaskBinding task,
        string turnId)
    {
        var payload = RequirePayloadObject(outer, "error");
        var completesTurn = turnId.Length != 0;
        if (completesTurn)
        {
            task.ActiveMessageIds.Remove(turnId);
            task.ActiveReasoningIds.Remove(turnId);
        }
        var decoded = new D046HermesErrorPayload(
            "hermes.error",
            RequireString(payload, "message"),
            IsTerminal: false,
            CompletesTurn: completesTurn);
        if (task.NoEffectAttestation is { Task.IsCompleted: false })
        {
            task.NoEffectAttestation.TrySetException(
                new D046HermesSessionActivationException());
        }
        return decoded;
    }

    private void SelectActivatedTaskLocked(TaskBinding task, bool resultAttested)
    {
        _selectedTaskId = task.TaskId;
        // Approval is read dynamically by the native gate. When tools.set
        // changed no enabled toolsets, an authenticated, freshly attested
        // resume can retain the same live agent (including an unsaved empty
        // conversation). Toolset changes still require a new native session.
        if (resultAttested && task.NativeApprovalOnlyReactivation
            && string.Equals(_runtimeInvalidatedNativeToolsSessionId,
                task.RuntimeSessionId, StringComparison.Ordinal))
        {
            _runtimeInvalidatedNativeToolsSessionId = null;
        }
        task.NativeApprovalOnlyReactivation = false;
        task.NativeToolsConfigStale = string.Equals(
            _runtimeInvalidatedNativeToolsSessionId,
            task.RuntimeSessionId,
            StringComparison.Ordinal);
        task.NativeToolsConfigStaleTurnActive = task.NativeToolsConfigStale
            && task.State is (D046HermesTaskState.Streaming
                or D046HermesTaskState.WaitingForApproval
                or D046HermesTaskState.Stopping);
        task.Events.Clear();
        task.ActiveMessageIds.Clear();
        task.ActiveReasoningIds.Clear();
        task.KnownTurnIds.Clear();
        task.CompletedTurnIds.Clear();
        task.ClaimedUserMessageIds.Clear();
        task.ClaimedAssistantMessageIds.Clear();
        task.ClaimedAuxiliaryAssistantMessageIds.Clear();
        task.ClaimedInterimAssistantMessageIds.Clear();
        task.LastInterimAssistantMessageIds.Clear();
        task.TerminalErrorMarkerTurnIds.Clear();
        task.TerminalErrorDisclosedTurnIds.Clear();
        ClearPendingSubmitLocked(task);
        task.AwaitingQueuedTurn = false;
        task.PendingInflight = null;
        task.PendingTerminalError = null;
        task.PendingBudgetConstraints = null;
        task.PendingApprovals.Clear();
        task.PendingClarification = null;
        task.PendingSecureInput = null;
        task.PendingAttachedImagePaths.Clear();
        task.ApprovalResponseInFlight = false;
        task.ClarificationResponseInFlight = false;
        task.ClarificationResponseRequestId = string.Empty;
        task.SecureInputResponseInFlight = false;
        task.SecureInputResponseRequestId = string.Empty;
        task.SecureInputResponseKind = null;
        task.ApprovalResponseQuarantined = false;
        ClearRollbackStateLocked(task);
        task.NoEffectAttestation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        // A pending, unknown route cannot reuse an older status attestation;
        // only the subsequent exact session.info can establish its model/surface.
        if (resultAttested
            || task.ModelDisplayName.Length != 0 && string.Equals(
                _statusAttestedRuntimeSessionId,
                task.RuntimeSessionId,
                StringComparison.Ordinal))
        {
            task.NoEffectAttestation.TrySetResult(true);
        }
        foreach (var candidate in _orderedTasks)
        {
            if (!ReferenceEquals(candidate, task))
                candidate.RuntimeSessionId = string.Empty;
        }
        _byRuntimeSessionId.Clear();
        _byRuntimeSessionId.Add(task.RuntimeSessionId, task);
    }

    private void AttachRuntimeSessionLocked(TaskBinding task, string runtimeSessionId)
    {
        if (_byRuntimeSessionId.TryGetValue(runtimeSessionId, out var existing)
            && !ReferenceEquals(existing, task))
            throw new InvalidDataException("Hermes reused a runtime session ID.");
        if (task.RuntimeSessionId.Length != 0)
            _byRuntimeSessionId.Remove(task.RuntimeSessionId);
        task.RuntimeSessionId = runtimeSessionId;
        task.NativeToolsConfigStale = false;
        task.NativeToolsConfigStaleTurnActive = false;
        task.CertifiedModelPairs.Clear();
        task.PendingAttachedImagePaths.Clear();
        ClearRollbackStateLocked(task);
        _byRuntimeSessionId[runtimeSessionId] = task;
    }

    private void AddTaskLocked(TaskBinding task)
    {
        if (!_byTaskId.TryAdd(task.TaskId, task)
            || !_byStoredSessionId.TryAdd(task.StoredSessionId, task))
            throw new InvalidDataException("Hermes session mapping was duplicated.");
        _orderedTasks.Add(task);
    }

    private static List<TaskBinding> OrderTasks(IEnumerable<TaskBinding> tasks)
        => tasks
            .Distinct()
            .OrderByDescending(task => task.IsPinned)
            .ThenByDescending(task => task.UpdatedUtc)
            .ThenByDescending(task => task.LastActivitySequence)
            .ThenBy(task => task.TaskId, StringComparer.Ordinal)
            .ToList();

    private void AdoptPinnedTaskIdsLocked(
        IEnumerable<string> pinnedTaskIds)
    {
        ArgumentNullException.ThrowIfNull(pinnedTaskIds);
        _pinnedTaskIds.Clear();
        _pinnedTaskIds.UnionWith(pinnedTaskIds);
        foreach (var task in _orderedTasks)
            task.IsPinned = _pinnedTaskIds.Contains(task.TaskId);
        _orderedTasks = OrderTasks(_orderedTasks);
    }

    private void ObserveTaskActivityLocked(
        TaskBinding task,
        DateTimeOffset occurredUtc,
        long sequence)
    {
        if (occurredUtc > task.UpdatedUtc)
            task.UpdatedUtc = occurredUtc;
        if (sequence > task.LastActivitySequence)
            task.LastActivitySequence = sequence;
        _orderedTasks = OrderTasks(_orderedTasks);
    }

    private static void ClearPendingSubmitLocked(TaskBinding task)
    {
        task.SubmitPending = false;
        task.PendingTurnId = string.Empty;
        task.PendingUserMessageId = string.Empty;
        task.PendingAssistantMessageId = string.Empty;
        task.PendingTurnTerminalObserved = false;
    }

    private static void AdmitTurnEventLocked(
        TaskBinding task,
        string type,
        string turnId,
        JsonElement outer)
    {
        if (type is "message.user" or "message.start" or "message.delta"
                or "message.interim" or "message.complete"
            && task.CompletedTurnIds.Contains(turnId))
        {
            throw new InvalidDataException(
                "Hermes emitted a message event after the turn was complete.");
        }
        var known = task.KnownTurnIds.Contains(turnId);
        if (!known)
        {
            if (!task.SubmitPending && !task.AwaitingQueuedTurn)
            {
                throw new InvalidDataException(
                    "Hermes event carried a foreign turn identity.");
            }
            if (task.PendingTurnId.Length != 0
                && !string.Equals(task.PendingTurnId, turnId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes pending prompt emitted multiple turn identities.");
            }
            if (task.SubmitPending)
                task.PendingTurnId = turnId;
            else
                task.AwaitingQueuedTurn = false;
            task.KnownTurnIds.Add(turnId);
            task.State = D046HermesTaskState.Streaming;
        }

        if (!string.Equals(type, "message.user", StringComparison.Ordinal))
        {
            if (type is not ("message.start" or "message.delta"
                or "message.interim" or "message.complete"))
            {
                return;
            }
            var assistantPayload = RequirePayloadObject(outer, type);
            var assistantMessageId = HermesTurnCorrelationContract.RequireIdentity(
                RequireString(assistantPayload, "message_id"),
                type + " message_id");
            if (string.Equals(
                    assistantMessageId,
                    turnId,
                    StringComparison.Ordinal)
                || (task.ClaimedUserMessageIds.TryGetValue(
                        turnId,
                        out var claimedUserMessageId)
                    && string.Equals(
                        assistantMessageId,
                        claimedUserMessageId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Hermes assistant message identity was aliased.");
            }
            if (string.Equals(type, "message.interim", StringComparison.Ordinal))
            {
                if (task.ClaimedAssistantMessageIds.TryGetValue(
                        turnId,
                        out var primaryAssistantMessageId)
                    && string.Equals(
                        primaryAssistantMessageId,
                        assistantMessageId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Hermes interim assistant reused the primary identity.");
                }
                if (!task.ClaimedAuxiliaryAssistantMessageIds.Add(
                        (turnId, assistantMessageId)))
                {
                    throw new InvalidDataException(
                        "Hermes interim assistant identity was duplicated.");
                }
                task.ClaimedInterimAssistantMessageIds.Add(
                    (turnId, assistantMessageId));
                task.LastInterimAssistantMessageIds[turnId] = assistantMessageId;
                return;
            }
            if (task.ClaimedAuxiliaryAssistantMessageIds.Contains(
                    (turnId, assistantMessageId)))
            {
                throw new InvalidDataException(
                    "Hermes primary assistant reused an auxiliary identity.");
            }
            if (task.ClaimedAssistantMessageIds.TryGetValue(
                    turnId,
                    out var expectedAssistantMessageId)
                && !string.Equals(
                    expectedAssistantMessageId,
                    assistantMessageId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes primary assistant identity changed mid-turn.");
            }
            task.ClaimedAssistantMessageIds[turnId] = assistantMessageId;
            if (task.SubmitPending
                && string.Equals(
                    task.PendingTurnId,
                    turnId,
                    StringComparison.Ordinal))
            {
                task.PendingAssistantMessageId = assistantMessageId;
            }
            return;
        }
        var payload = RequirePayloadObject(outer, type);
        var messageId = HermesTurnCorrelationContract.RequireIdentity(
            RequireString(payload, "message_id"),
            "message.user message_id");
        if (string.Equals(messageId, turnId, StringComparison.Ordinal)
            || task.ClaimedAuxiliaryAssistantMessageIds.Contains((turnId, messageId))
            || (task.ClaimedAssistantMessageIds.TryGetValue(
                    turnId,
                    out var claimedAssistantMessageId)
                && string.Equals(
                    claimedAssistantMessageId,
                    messageId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Hermes user message identity was aliased.");
        }
        if (task.ClaimedUserMessageIds.TryGetValue(turnId, out var expected)
            && !string.Equals(expected, messageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes user event disagreed with the turn's claimed message identity.");
        }
        task.ClaimedUserMessageIds[turnId] = messageId;
        if (task.SubmitPending
            && string.Equals(
                task.PendingTurnId,
                turnId,
                StringComparison.Ordinal))
        {
            task.PendingUserMessageId = messageId;
        }
    }

    private void RemoveTaskLocked(TaskBinding task)
    {
        if (!_byTaskId.Remove(task.TaskId)
            || !_byStoredSessionId.Remove(task.StoredSessionId))
        {
            throw new InvalidDataException("Hermes task deletion lost its durable mapping.");
        }
        if (task.RuntimeSessionId.Length != 0)
            _byRuntimeSessionId.Remove(task.RuntimeSessionId);
        _orderedTasks.RemoveAll(candidate => ReferenceEquals(candidate, task));
        if (string.Equals(_selectedTaskId, task.TaskId, StringComparison.Ordinal))
            _selectedTaskId = string.Empty;
        if (string.Equals(
                _statusAttestedRuntimeSessionId,
                task.RuntimeSessionId,
                StringComparison.Ordinal))
        {
            _statusAttestedRuntimeSessionId = null;
        }
        if (string.Equals(
                _runtimeInvalidatedNativeToolsSessionId,
                task.RuntimeSessionId,
                StringComparison.Ordinal))
        {
            _runtimeInvalidatedNativeToolsSessionId = null;
        }
    }

    private TaskBinding RequireTaskMutationTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId,
        long lastObservedSequence)
    {
        if (!string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(task.RuntimeSessionId, runtimeSessionId, StringComparison.Ordinal))
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.StaleTaskMapping,
                "The Hermes task mutation mapping changed before dispatch.");
        }
        if (lastObservedSequence != _nextProjectionSequence)
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.StaleProjectionCursor,
                "The Hermes task mutation projection changed before dispatch.");
        }
        if (task.State is not (D046HermesTaskState.Idle or D046HermesTaskState.Failed))
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.TaskNotIdle,
                "The Hermes task may be changed only while idle or failed.");
        }
        return task;
    }

    private static void ValidateTaskMutationRequestLocked(
        string clientRequestId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)
            || clientRequestId.Length > 1024
            || clientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The Hermes task-mutation client request ID was invalid.",
                nameof(clientRequestId));
        }
        if (lastObservedSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(lastObservedSequence));
    }

    private TaskBinding RequireCommandTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId)
    {
        if (!string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(
                task.RuntimeSessionId,
                runtimeSessionId,
                StringComparison.Ordinal)
            || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true
            || task.NativeToolsConfigStale)
        {
            throw new InvalidOperationException("The Hermes command mapping is stale.");
        }
        return task;
    }

    private TaskBinding RequireInterruptibleTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId,
        long lastObservedSequence)
    {
        if (!string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(
                task.RuntimeSessionId,
                runtimeSessionId,
                StringComparison.Ordinal)
                // Cancellation is monotonic for this exact generation/task/
                // session. A newer event may have reached the protocol while
                // its UI callback is still queued, so an older cursor must not
                // turn Stop into a pre-dispatch no-op.
                || lastObservedSequence > _nextProjectionSequence
                || (task.State is not (D046HermesTaskState.Streaming
                        or D046HermesTaskState.WaitingForApproval
                        or D046HermesTaskState.WaitingForClarification
                        or D046HermesTaskState.WaitingForSecureInput
                        or D046HermesTaskState.Stopping)
                    && !(task.State == D046HermesTaskState.Failed
                        && task.ApprovalResponseQuarantined)))
        {
            throw new InvalidOperationException("The Hermes command mapping is stale.");
        }

        if (task.NativeToolsConfigStale)
        {
            if (!task.NativeToolsConfigStaleTurnActive
                || !string.Equals(
                    _runtimeInvalidatedNativeToolsSessionId,
                    runtimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Hermes command mapping is stale.");
            }

            return task;
        }

        if (task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            throw new InvalidOperationException("The Hermes command mapping is stale.");

        return task;
    }

    private TaskBinding RequireApprovalResponseTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId,
        long lastObservedSequence)
    {
        if (!string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(
                task.RuntimeSessionId,
                runtimeSessionId,
                StringComparison.Ordinal)
            || lastObservedSequence != _nextProjectionSequence)
        {
            throw new InvalidOperationException("The Hermes command mapping is stale.");
        }

        if (task.NativeToolsConfigStale)
        {
            if (!task.NativeToolsConfigStaleTurnActive
                || !string.Equals(
                    _runtimeInvalidatedNativeToolsSessionId,
                    runtimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Hermes command mapping is stale.");
            }

            return task;
        }

        if (task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            throw new InvalidOperationException("The Hermes command mapping is stale.");

        return task;
    }

    private TaskBinding RequireDelegationCommandTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId)
    {
        try
        {
            return RequireCommandTaskLocked(
                runtimeGeneration,
                taskId,
                runtimeSessionId);
        }
        catch (InvalidOperationException exception)
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.StaleTaskMapping,
                "The Hermes delegation task mapping changed before dispatch.",
                exception);
        }
    }

    private TaskBinding RequireVoiceCommandTaskLocked(
        D046HermesVoiceCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientRequestId)
            || request.ClientRequestId.Length > 1024
            || request.ClientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The Hermes voice client request ID was invalid.",
                nameof(request));
        }
        try
        {
            return RequireCommandTaskLocked(
                request.RuntimeGeneration,
                request.TaskId,
                request.SessionId);
        }
        catch (InvalidOperationException exception)
        {
            throw new D046HermesVoiceOperationException(
                D046HermesVoiceFailureKind.StateConflict,
                "voice.mapping_changed",
                "The Hermes voice task mapping changed before dispatch.",
                transportUncertain: false,
                exception);
        }
    }

    private TaskBinding RequireRollbackCommandTaskLocked(
        string runtimeGeneration,
        string taskId,
        string runtimeSessionId)
    {
        try
        {
            return RequireCommandTaskLocked(
                runtimeGeneration,
                taskId,
                runtimeSessionId);
        }
        catch (InvalidOperationException exception)
        {
            throw new D046HermesRollbackOperationException(
                D046HermesRollbackFailureKind.StateConflict,
                "rollback.mapping_changed",
                "The rollback task mapping changed before dispatch.",
                transportUncertain: false,
                exception);
        }
    }

    private void ValidateDelegationCommandLocked(
        string clientRequestId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)
            || clientRequestId.Length > 1024
            || clientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The Hermes delegation client request ID was invalid.",
                nameof(clientRequestId));
        }
        if (lastObservedSequence != _nextProjectionSequence)
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.StaleProjectionCursor,
                "The Hermes delegation projection changed before dispatch.");
        }
    }

    private void ValidateRollbackCommandLocked(
        string clientRequestId,
        long lastObservedSequence)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId)
            || clientRequestId.Length > 1024
            || clientRequestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The rollback client request ID was invalid.",
                nameof(clientRequestId));
        }
        if (lastObservedSequence != _nextProjectionSequence)
        {
            throw new D046HermesPreDispatchRejectedException(
                D046HermesPreDispatchRejectionReason.StaleProjectionCursor,
                "The rollback projection changed before dispatch.");
        }
    }

    private TaskBinding RequireSelectedTaskLocked(
        string taskId,
        string runtimeSessionId)
    {
        if (!_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(task.RuntimeSessionId, runtimeSessionId, StringComparison.Ordinal)
            || task.NativeToolsConfigStale
            || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
        {
            throw new InvalidOperationException("The Hermes task mapping is stale.");
        }
        return task;
    }

    private void RequireCredentialRecoveryHistoryTaskLocked(
        D046HermesRuntimeRequestCorrelation correlation,
        D046HermesCredentialRecoveryHistoryFence fence)
    {
        RequireRuntimeReadyLocked();
        var task = RequireCorrelatedTaskLocked(correlation);
        // Reject before decoding or replacing anything: an intervening event belongs
        // to the current projection and must never disappear behind an older read.
        if (!correlation.ReplaceHistory
            || !string.Equals(fence.RuntimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || fence.ThroughSequence != _nextProjectionSequence
            || !string.Equals(task.ProviderId, fence.ProviderId, StringComparison.Ordinal)
            || !string.Equals(task.ModelDisplayName, fence.ModelId, StringComparison.Ordinal)
            || !task.CertifiedModelPairs.Contains(new ModelPair(fence.ProviderId, fence.ModelId))
            || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true
            || task.CredentialUnavailable || task.NativeToolsConfigStale
            || task.PendingCredentialRecovery is not null
            || task.State != D046HermesTaskState.Idle
            || task.SubmitPending || task.AwaitingQueuedTurn
            || task.PendingTurnId.Length != 0 || task.PendingInflight is not null
            || task.PendingTerminalError is not null || task.PendingQueuedPromptText.Length != 0
            || task.ActiveMessageIds.Count != 0 || task.ActiveReasoningIds.Count != 0
            || task.PendingApprovals.Count != 0 || task.PendingClarification is not null
            || task.PendingSecureInput is not null || task.PendingAttachedImagePaths.Count != 0
            || task.ApprovalResponseInFlight || task.ApprovalResponseQuarantined
            || task.ClarificationResponseInFlight || task.SecureInputResponseInFlight)
        {
            throw new InvalidDataException("Credential recovery history mapping changed before replacement.");
        }
    }

    private static bool CanRecoverCredential(TaskBinding task)
        => task.CredentialUnavailable && task.State == D046HermesTaskState.Failed
            && !task.NativeToolsConfigStale && task.PendingCredentialRecovery is null
            && !task.SubmitPending && !task.AwaitingQueuedTurn
            && task.PendingTurnId.Length == 0 && task.PendingInflight is null
            && task.ActiveMessageIds.Count == 0 && task.ActiveReasoningIds.Count == 0
            && task.PendingApprovals.Count == 0 && task.PendingClarification is null
            && task.PendingSecureInput is null && task.PendingAttachedImagePaths.Count == 0
            && !task.ApprovalResponseInFlight && !task.ApprovalResponseQuarantined
            && !task.ClarificationResponseInFlight && !task.SecureInputResponseInFlight;

    private TaskBinding RequireModelSelectionTaskLocked(
        string runtimeGeneration, string taskId, string runtimeSessionId)
    {
        RequireRuntimeReadyLocked();
        if (!string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            || !_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(task.RuntimeSessionId, runtimeSessionId, StringComparison.Ordinal)
            || task.NativeToolsConfigStale
            || (task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true
                && !CanRecoverCredential(task)))
            throw new InvalidOperationException("The Hermes model recovery mapping is stale.");
        return task;
    }

    private static void CancelCredentialRecoveryLocked(TaskBinding task)
    {
        task.PendingCredentialRecovery = null;
        task.CredentialUnavailable = true;
        task.State = D046HermesTaskState.Failed;
        task.CertifiedNativeToolNames.Clear();
        task.NoEffectAttestation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        task.NoEffectAttestation.TrySetCanceled();
    }

    private TaskBinding RequireNativeToolsCatalogTaskLocked(
        string taskId,
        string runtimeSessionId)
    {
        if (!_byTaskId.TryGetValue(taskId, out var task)
            || !string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || !string.Equals(task.RuntimeSessionId, runtimeSessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The Hermes native tool catalog mapping is stale.");
        }

        if (task.NativeToolsConfigStale)
        {
            if (task.State is not (D046HermesTaskState.Idle
                or D046HermesTaskState.Failed))
            {
                throw new InvalidOperationException(
                    "The Hermes native tool catalog mapping is stale.");
            }

            return task;
        }

        if (task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
        {
            throw new InvalidOperationException(
                "The Hermes native tool catalog mapping is stale.");
        }

        return task;
    }

    private TaskBinding RequireCorrelatedTaskLocked(
        D046HermesRuntimeRequestCorrelation correlation,
        bool requireRuntime = true)
    {
        if (!_byTaskId.TryGetValue(correlation.TaskId, out var task)
            || !string.Equals(
                task.StoredSessionId,
                correlation.StoredSessionId,
                StringComparison.Ordinal)
            || (requireRuntime
                && !string.Equals(
                    task.RuntimeSessionId,
                    correlation.RuntimeSessionId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The Hermes request correlation was stale.");
        }
        return task;
    }

    private static D046HermesRuntimeRequestCorrelation RequireCorrelation(
        D046HermesRuntimeRequestBinding binding)
        => binding.Correlation
            ?? throw new InvalidDataException("The Hermes request correlation was missing.");

    private static D046HermesRuntimeRequestCorrelation Correlation(TaskBinding task)
        => new(task.TaskId, task.RuntimeSessionId, task.StoredSessionId);

    private static D046HermesRollbackOperationException RollbackStateConflict(
        string code,
        string message)
        => new(
            D046HermesRollbackFailureKind.StateConflict,
            code,
            message,
            transportUncertain: false);

    private static void ClearRollbackConfirmationLocked(TaskBinding task)
    {
        task.RollbackConfirmationHash = string.Empty;
        task.RollbackConfirmationToken = string.Empty;
    }

    private static void ClearRollbackStateLocked(TaskBinding task)
    {
        task.RollbackCatalogAvailable = false;
        task.RollbackCheckpointHashes.Clear();
        ClearRollbackConfirmationLocked(task);
    }

    private void ObserveGenerationLocked(D046HermesRuntimeEnvelopeContext envelope)
    {
        var generation = envelope.GenerationId.ToString("N");
        if (_runtimeGeneration.Length == 0)
            _runtimeGeneration = generation;
        else if (!string.Equals(_runtimeGeneration, generation, StringComparison.Ordinal))
            throw new InvalidDataException("Runtime generation changed in the protocol binding.");
    }

    private void RequireRuntimeReadyLocked()
    {
        if (!_runtimeReady || !_nativeApprovalPolicyReady || _runtimeGeneration.Length == 0)
            throw new InvalidDataException("Runtime status was not established.");
    }

    private D046HermesWorkbenchSnapshot SnapshotLocked()
    {
        var selected = SelectedTaskLocked();
        var nativeSurfaceAttested = _nativeApprovalPolicyReady
            && selected?.NoEffectAttestation?.Task.IsCompletedSuccessfully == true
            && !selected.NativeToolsConfigStale;
        var tasks = OrderTasks(_orderedTasks)
            .Select(task => new D046HermesTaskProjection(
                task.TaskId,
                ReferenceEquals(task, selected) ? task.RuntimeSessionId : string.Empty,
                task.StoredSessionId,
                task.Title,
                task.ProviderId,
                task.ModelDisplayName,
                ReferenceEquals(task, selected) && task.PendingApprovals.Count > 0
                    ? D046HermesTaskState.WaitingForApproval
                    : task.State,
                task.UpdatedUtc,
                task.IsPinned))
            .ToArray();
        var events = selected is null
            ? Array.Empty<D046HermesProjectionEvent>()
            : selected.Events.ToArray();
        var pendingApprovals = selected is null
            ? Array.Empty<D046HermesPendingApprovalProjection>()
            : selected.PendingApprovals
                .Select(pending => pending with
                {
                    Choices = pending.Choices.ToArray()
                })
                .ToArray();
        var snapshot = new D046HermesWorkbenchSnapshot(
            _runtimeGeneration,
            _nextProjectionSequence,
            _runtimeReady,
            nativeSurfaceAttested,
            selected?.NativeToolsConfigStale == true,
            _computerUseEnabled,
            _statusText,
            selected?.TaskId ?? string.Empty,
            tasks,
            events,
            pendingApprovals,
            selected?.PendingClarification is { } clarification
                ? clarification with { Choices = clarification.Choices.ToArray() }
                : null,
            selected?.PendingSecureInput,
            CredentialRecoveryAvailable: _runtimeReady && _nativeApprovalPolicyReady
                && !nativeSurfaceAttested && selected is not null
                && CanRecoverCredential(selected));
        D046HermesWorkbenchSnapshotApprovalValidator.Validate(snapshot);
        return snapshot;
    }

    private TaskBinding? SelectedTaskLocked()
        => _selectedTaskId.Length != 0
            && _byTaskId.TryGetValue(_selectedTaskId, out var task)
                ? task
                : null;

    private static ActivationInfo InspectActivationInfo(JsonElement info)
    {
        var lazy = info.TryGetProperty("lazy", out var lazyValue)
            ? lazyValue.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException("Hermes activation lazy flag was malformed.")
            }
            : false;
        // A stored chat may have no model override. Native cold resume then
        // reports an empty model while building, without guessing the global
        // default. Preserve that unknown route until exact ready session.info;
        // missing/non-string/whitespace identities and ready empties stay invalid.
        var model = RequireString(info, "model", allowEmpty: lazy);
        if (!lazy || model.Length != 0)
            model = RequireIdentity(model, "model");
        var provider = info.TryGetProperty("provider", out var providerValue)
            ? RequireStringValue(providerValue, "provider", allowEmpty: true)
            : string.Empty;
        if (provider.Length != 0)
            provider = RequireIdentity(provider, "provider");
        var tools = RequireObjectProperty(info, "tools");
        var hasMcp = info.TryGetProperty("mcp_servers", out var mcpServers);
        if (hasMcp)
            ValidateAttestedNativeMcpSurface(mcpServers);

        // Hermes session.create and cold session.resume intentionally return a
        // lightweight lazy record before the deferred AIAgent build completes.
        // That record must expose no tools and is never authority to submit a
        // prompt. The later non-lazy session.info event supplies the active
        // Runtime-attested native tool/MCP surface and releases the wait gate.
        if (lazy)
        {
            if (tools.EnumerateObject().Any())
            {
                throw new InvalidDataException(
                    "Hermes lazy activation exposed a tool surface.");
            }
            return new ActivationInfo(
                provider,
                model,
                NoEffectAttested: false,
                Array.Empty<string>());
        }

        var toolNames = ReadAttestedNativeToolSurface(tools);
        if (!lazy && !hasMcp)
            throw new InvalidDataException("Hermes activation omitted MCP attestation.");
        return new ActivationInfo(provider, model, NoEffectAttested: hasMcp, toolNames);
    }

    private static IReadOnlyList<string> ReadAttestedNativeToolSurface(
        JsonElement tools)
    {
        if (tools.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes activation tools were malformed.");
        var seenToolsets = new HashSet<string>(StringComparer.Ordinal);
        var seenTools = new HashSet<string>(StringComparer.Ordinal);
        var orderedTools = new List<string>();
        foreach (var toolset in tools.EnumerateObject())
        {
            var toolsetName = RequireIdentity(toolset.Name, "toolset name");
            if (toolsetName.Length > MaximumNativeSurfaceNameCharacters
                || !seenToolsets.Add(toolsetName)
                || seenToolsets.Count > MaximumNativeSurfaceToolsets
                || toolset.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "Hermes activation exposed a malformed native tool surface.");
            }

            foreach (var tool in toolset.Value.EnumerateArray())
            {
                var actual = RequireIdentity(
                    RequireStringValue(
                        tool,
                        toolset.Name + " tool",
                        allowEmpty: false),
                    toolset.Name + " tool");
                if (actual.Length > MaximumNativeSurfaceNameCharacters
                    || !seenTools.Add(actual)
                    || seenTools.Count > MaximumNativeSurfaceTools)
                {
                    throw new InvalidDataException(
                        "Hermes activation exposed a malformed native tool surface.");
                }
                orderedTools.Add(actual);
            }
        }
        return orderedTools.AsReadOnly();
    }

    private static void ValidateAttestedNativeMcpSurface(JsonElement servers)
    {
        if (servers.ValueKind != JsonValueKind.Array
            || servers.GetArrayLength() > MaximumNativeMcpServers)
        {
            throw new InvalidDataException(
                "Hermes activation MCP servers were malformed.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in servers.EnumerateArray())
        {
            var server = RequireObject(value, "Hermes activation MCP server");
            ValidateAllowedProperties(
                server,
                required: new[]
                {
                    "name",
                    "transport",
                    "tools",
                    "connected",
                    "disabled",
                    "status"
                },
                allowed: new[]
                {
                    "name",
                    "transport",
                    "tools",
                    "connected",
                    "disabled",
                    "status",
                    "error",
                    "sampling"
                },
                "Hermes activation MCP server");
            var name = RequireNativeSurfaceName(server, "name");
            if (!names.Add(name))
                throw new InvalidDataException("Hermes activation duplicated an MCP server.");
            _ = RequireNativeSurfaceName(server, "transport");
            _ = RequireNativeSurfaceName(server, "status");
            if (RequireNonNegativeInteger(server, "tools") > MaximumNativeSurfaceTools)
                throw new InvalidDataException("Hermes activation MCP tool count was too large.");
            _ = RequireBoolean(server, "connected");
            _ = RequireBoolean(server, "disabled");

            if (server.TryGetProperty("error", out var error))
            {
                var text = RequireContentStringValue(
                    error,
                    "MCP error",
                    allowEmpty: true);
                if (text.Length > MaximumNativeMcpErrorCharacters)
                    throw new InvalidDataException("Hermes activation MCP error was too large.");
            }
            if (server.TryGetProperty("sampling", out var sampling)
                && sampling.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Hermes activation MCP sampling state was malformed.");
            }
        }
    }

    private static string RequireNativeSurfaceName(JsonElement value, string name)
    {
        var result = RequireString(value, name);
        if (result.Length > MaximumNativeSurfaceNameCharacters)
            throw new InvalidDataException(name + " exceeded its native-surface limit.");
        return result;
    }

    private static string RequireCertifiedNativeToolName(
        TaskBinding task,
        string name)
    {
        name = RequireIdentity(name, "tool name");
        if (!task.CertifiedNativeToolNames.Contains(name))
            throw new InvalidDataException("Hermes emitted an unattested tool event.");
        return name;
    }

    private static bool IsPotentialHostEffectTool(string name)
        => name is not ("memory"
            or "skills_list"
            or "skill_view"
            or "skill_manage"
            or "session_search"
            or "delegate_task");

    private static D046HermesTaskState? UpdateTaskStateFromPayloadLocked(
        TaskBinding task,
        D046HermesProjectionPayload payload,
        bool preserveCompletedTurnState = false)
    {
        if (preserveCompletedTurnState)
            return null;
        var reduced = D046HermesTaskStateReducer.Reduce(task.State, payload);
        D046HermesTaskState? effectiveTaskState = null;
        if (task.AwaitingQueuedTurn && reduced == D046HermesTaskState.Idle)
        {
            reduced = D046HermesTaskState.Streaming;
            effectiveTaskState = reduced;
        }
        task.State = reduced;
        if (task.State is D046HermesTaskState.Idle or D046HermesTaskState.Failed)
            task.NativeToolsConfigStaleTurnActive = false;
        return effectiveTaskState;
    }

    private static void RejectNativeSurfaceLocked(TaskBinding task, string message)
        => task.NoEffectAttestation?.TrySetException(new InvalidDataException(message));

    private static bool IsSupportedEvent(string type)
        => type is "session.info"
            or "session.title"
            or HermesTurnCorrelationContract.CronCompletionEventType
            or "message.user" or "message.start" or "message.delta"
            or "message.interim" or "message.complete"
            or "thinking.delta" or "reasoning.delta" or "reasoning.available"
            or "status.update"
            or "tool.start" or "tool.progress" or "tool.complete" or "tool.generating"
            or "tool.output_risk"
            or "subagent.start" or "subagent.thinking" or "subagent.tool"
            or "subagent.progress" or "subagent.complete"
            or "clarify.request" or "clarify.expire"
            or "approval.request" or "sudo.request" or "sudo.expire"
            or "secret.request" or "secret.expire"
            or "notification.show" or "notification.clear" or "reaction"
            or "sessions.changed"
            or "background.complete" or "review.summary"
            or "error";

    private static bool IsAuthorityBearingEventFamily(string type)
        => type.StartsWith("tool.", StringComparison.Ordinal)
            || type.StartsWith("subagent.", StringComparison.Ordinal)
            || type.StartsWith("delegation.", StringComparison.Ordinal)
            || type.StartsWith("approval.", StringComparison.Ordinal)
            || type.StartsWith("sudo.", StringComparison.Ordinal)
            || type.StartsWith("secret.", StringComparison.Ordinal)
            || type.StartsWith("clarify.", StringComparison.Ordinal);

    private D046HermesRuntimeRequestBinding BindSelectedSessionRead(
        string method,
        string taskId,
        string runtimeSessionId,
        bool includeFrameShape)
    {
        RequireIdentity(taskId, nameof(taskId));
        RequireIdentity(runtimeSessionId, nameof(runtimeSessionId));
        lock (_gate)
        {
            var task = RequireSelectedTaskLocked(taskId, runtimeSessionId);
            return includeFrameShape
                ? Bind(
                    method,
                    new { cols = 80, rows = 16, frames = 2 },
                    Correlation(task))
                : Bind(
                    method,
                    new { session_id = task.RuntimeSessionId },
                    Correlation(task));
        }
    }

    private JsonElement BeginDelegationDecode(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding,
        string expectedMethod,
        IReadOnlyCollection<string> expectedRequestProperties,
        out TaskBinding task)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, expectedMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, expectedMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Hermes delegation response was not correlated.");
        }
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
                || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            {
                throw new InvalidDataException(
                    "The Hermes delegation request was not for the active attested task.");
            }
            ValidateExactProperties(
                binding.HermesPayload,
                expectedRequestProperties,
                expectedMethod + " request");
            if (!string.Equals(
                    RequireIdentity(
                        RequireString(binding.HermesPayload, "session_id"),
                        "session_id"),
                    task.RuntimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Hermes delegation request escaped the active session.");
            }
            RequireSucceeded(response);
            return RequireObject(response.HermesPayload, expectedMethod + " result");
        }
    }

    private JsonElement BeginVoiceDecode(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding,
        string expectedMethod,
        string expectedAction,
        bool includeSessionId,
        out TaskBinding task)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, expectedMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, expectedMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes voice response was not correlated.");
        }

        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
                || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            {
                throw new InvalidDataException(
                    "The Hermes voice request was not for the active attested task.");
            }

            ValidateExactProperties(
                binding.HermesPayload,
                includeSessionId
                    ? new[] { "action", "session_id" }
                    : new[] { "action" },
                expectedMethod + " request");
            if (!string.Equals(
                    RequireString(binding.HermesPayload, "action"),
                    expectedAction,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Hermes voice action was not preserved.");
            }
            if (includeSessionId
                && !string.Equals(
                    RequireIdentity(
                        RequireString(binding.HermesPayload, "session_id"),
                        "session_id"),
                    task.RuntimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Hermes voice request escaped the active session.");
            }

            RequireSucceeded(response);
            return RequireObject(response.HermesPayload, expectedMethod + " result");
        }
    }

    private JsonElement BeginRollbackDecodeLocked(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding,
        string expectedMethod,
        IReadOnlyCollection<string> expectedRequestProperties,
        out TaskBinding task)
    {
        if (!string.Equals(response.Method, expectedMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, expectedMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The rollback response was not correlated.");
        }
        ObserveGenerationLocked(envelope);
        task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
        if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
            || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
        {
            throw new InvalidDataException(
                "The rollback request was not for the active attested task.");
        }
        ValidateExactProperties(
            binding.HermesPayload,
            expectedRequestProperties,
            expectedMethod + " request");
        if (!string.Equals(
                RequireIdentity(
                    RequireString(binding.HermesPayload, "session_id"),
                    "session_id"),
                task.RuntimeSessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The rollback request escaped the active session.");
        }
        if (!response.Succeeded
            && string.Equals(
                expectedMethod,
                RollbackRestoreMethod,
                StringComparison.Ordinal))
        {
            ThrowIfSafeRollbackRestoreFailureLocked(response, task);
        }
        RequireSucceeded(response);
        return RequireObject(response.HermesPayload, expectedMethod + " result");
    }

    private void ThrowIfSafeRollbackRestoreFailureLocked(
        LilithRuntimeResponse response,
        TaskBinding task)
    {
        var failure = RequireObject(response.HermesPayload, "Runtime failure");
        ValidateExactProperties(
            failure,
            new[] { "code", "message", "hermesCode" },
            "rollback.restore Runtime failure");
        var code = RequireString(failure, "code");
        var message = RequireString(failure, "message");
        ValidateHermesFailureCode(failure);
        int? hermesCode = null;
        var value = failure.GetProperty("hermesCode");
        if (value.ValueKind == JsonValueKind.Number)
            hermesCode = value.GetInt32();

        var provedNotMutated = code switch
        {
            "runtime.checkpoint_restore_active"
                or "runtime.checkpoint_catalog_member_required"
                or "runtime.checkpoint_confirmation_required"
                or "runtime.checkpoint_restore_not_ready"
                or "runtime.checkpoint_delegation_active"
                or "runtime.checkpoint_preflight_failed"
                or "runtime.checkpoint_restore_state_changed"
                or "runtime.checkpoint_workspace_attestation_required"
                or "runtime.image_lifecycle_blocked"
                or "runtime.workspace_path_unsafe"
                or "runtime.gateway_not_sent" => hermesCode is null,
            "runtime.checkpoint_preflight_rejected" => hermesCode is not null,
            "hermes.rpc_rejected" => hermesCode is 4004 or 4009 or 4014,
            _ => false
        };
        if (!provedNotMutated)
        {
            if (string.Equals(
                    code,
                    "runtime.checkpoint_post_mutation_uncertain",
                    StringComparison.Ordinal)
                && hermesCode is null)
            {
                ClearRollbackStateLocked(task);
                throw new D046HermesRollbackOperationException(
                    D046HermesRollbackFailureKind.TransportUncertain,
                    "rollback.restore.post_mutation_uncertain",
                    message,
                    transportUncertain: true);
            }
            return;
        }

        ClearRollbackStateLocked(task);
        throw RollbackStateConflict(code, message);
    }

    private static D046HermesVoiceStatusPayload DecodeVoiceStatusPayload(
        JsonElement payload)
    {
        ValidateExactProperties(payload, new[] { "state" }, "voice.status payload");
        var state = RequireString(payload, "state") switch
        {
            "idle" => D046HermesVoiceCaptureState.Idle,
            "listening" => D046HermesVoiceCaptureState.Listening,
            "transcribing" => D046HermesVoiceCaptureState.Transcribing,
            _ => throw new InvalidDataException("voice.status state was rejected.")
        };
        return new D046HermesVoiceStatusPayload(state);
    }

    private static string RequireVoiceDetails(JsonElement result)
    {
        var details = RequireContentString(result, "details", allowEmpty: true);
        if (Encoding.UTF8.GetByteCount(details) > 64 * 1024)
            throw new InvalidDataException("voice.toggle details exceeded its size limit.");
        return details;
    }

    private static D046HermesVoiceTranscriptPayload DecodeVoiceTranscriptPayload(
        JsonElement payload)
    {
        var names = PropertyNames(payload, "voice.transcript payload");
        if (names.SetEquals(new[] { "text" }))
        {
            return new D046HermesVoiceTranscriptPayload(
                D046HermesVoiceTranscriptKind.Text,
                RequireControlText(RequireString(payload, "text"), "text"));
        }
        if (names.SetEquals(new[] { "no_speech_limit" }))
        {
            if (!RequireBoolean(payload, "no_speech_limit"))
                throw new InvalidDataException("voice.transcript silence flag was false.");
            return new D046HermesVoiceTranscriptPayload(
                D046HermesVoiceTranscriptKind.NoSpeechLimit,
                string.Empty);
        }
        if (names.SetEquals(new[] { "stop_phrase", "text" }))
        {
            if (!RequireBoolean(payload, "stop_phrase"))
                throw new InvalidDataException("voice.transcript stop flag was false.");
            return new D046HermesVoiceTranscriptPayload(
                D046HermesVoiceTranscriptKind.StopPhrase,
                RequireControlText(RequireString(payload, "text"), "text"));
        }
        throw new InvalidDataException("voice.transcript payload shape was rejected.");
    }

    private static HashSet<string> PropertyNames(JsonElement value, string name)
    {
        value = RequireObject(value, name);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException(name + " contained a duplicate property.");
        }
        return names;
    }

    private JsonElement BeginObservabilityDecode(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding,
        string expectedMethod,
        out string expectedModel,
        out string statusCode)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(binding);
        if (!string.Equals(response.Method, expectedMethod, StringComparison.Ordinal)
            || !string.Equals(binding.Method, expectedMethod, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Hermes observability response was not correlated.");
        }
        lock (_gate)
        {
            ObserveGenerationLocked(envelope);
            var task = RequireCorrelatedTaskLocked(RequireCorrelation(binding));
            if (!string.Equals(task.TaskId, _selectedTaskId, StringComparison.Ordinal)
                || task.NoEffectAttestation?.Task.IsCompletedSuccessfully != true)
            {
                throw new InvalidDataException("The Hermes observability task was not active.");
            }
            ValidateObservabilityRequest(binding.HermesPayload, expectedMethod, task);
            expectedModel = task.ModelDisplayName;
        }
        if (!response.Succeeded)
        {
            var failure = RequireObject(response.HermesPayload, "Runtime failure");
            statusCode = RequireString(failure, "code");
            _ = RequireString(failure, "message");
            ValidateHermesFailureCode(failure);
            return failure;
        }
        statusCode = "ready";
        return RequireObject(response.HermesPayload, expectedMethod + " result");
    }

    private static void ValidateObservabilityRequest(
        JsonElement request,
        string method,
        TaskBinding task)
    {
        var properties = request.EnumerateObject().ToArray();
        if (method is SessionUsageMethod or SessionContextBreakdownMethod)
        {
            if (properties.Length != 1
                || !string.Equals(properties[0].Name, "session_id", StringComparison.Ordinal)
                || !string.Equals(
                    RequireIdentity(
                        RequireStringValue(properties[0].Value, "session_id"),
                        "session_id"),
                    task.RuntimeSessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The session observability request was malformed.");
            }
            return;
        }

        if (method != LearningFramesMethod
            || properties.Length != 3
            || RequireNonNegativeInteger(request, "cols") != 80
            || RequireNonNegativeInteger(request, "rows") != 16
            || RequireNonNegativeInteger(request, "frames") != 2)
        {
            throw new InvalidDataException("The learning.frames request was malformed.");
        }
        var names = properties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.SetEquals(new[] { "cols", "rows", "frames" }))
            throw new InvalidDataException("The learning.frames request was malformed.");
    }

    private static int ValidateLearningFrame(JsonElement value, int count)
    {
        var frame = RequireObject(value, "learning frame");
        var reveal = RequireFiniteNumber(frame, "reveal");
        if (reveal is < 0 or > 1)
            throw new InvalidDataException("learning.frames reveal was outside its range.");
        _ = RequireString(frame, "date", allowEmpty: true);
        var visible = RequireNonNegativeInteger(frame, "visible");
        if (visible > count)
            throw new InvalidDataException("learning.frames visibility exceeded count.");

        var grid = RequireArray(frame, "grid");
        if (grid.GetArrayLength() is < 1 or > 32)
            throw new InvalidDataException("learning.frames grid row count was rejected.");
        foreach (var row in grid.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() > 256)
                throw new InvalidDataException("learning.frames grid row was malformed.");
            foreach (var run in row.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Array
                    || run.GetArrayLength() is < 3 or > 4)
                {
                    throw new InvalidDataException("learning.frames grid run was malformed.");
                }
                var values = run.EnumerateArray().ToArray();
                // Native learning frames use whitespace-only runs to paint the
                // background. They are valid drawing data, not missing text.
                var text = RequireStringValue(values[0], "grid text", allowEmpty: true);
                _ = RequireStringValue(values[1], "grid style");
                var alpha = RequireFiniteNumberValue(values[2], "grid alpha");
                if (text.Length > 1024 || alpha is < 0 or > 1)
                    throw new InvalidDataException("learning.frames grid run was rejected.");
                if (values.Length == 4
                    && values[3].ValueKind != JsonValueKind.Null
                    && values[3].ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("learning.frames grid color was malformed.");
                }
            }
        }

        var labels = RequireArray(frame, "labels");
        if (labels.GetArrayLength() > 8)
            throw new InvalidDataException("learning.frames had too many labels.");
        foreach (var labelValue in labels.EnumerateArray())
        {
            var label = RequireObject(labelValue, "learning frame label");
            _ = RequireString(label, "key");
            _ = RequireString(label, "glyph");
            _ = RequireString(label, "label");
            _ = RequireString(label, "meta", allowEmpty: true);
            _ = RequireString(label, "style");
            var alpha = RequireFiniteNumber(label, "alpha");
            if (alpha is < 0 or > 1)
                throw new InvalidDataException("learning.frames label alpha was rejected.");
        }
        return visible;
    }

    private static D046HermesRuntimeRequestBinding Bind(
        string method,
        object payload,
        D046HermesRuntimeRequestCorrelation? correlation = null)
        => new(method, JsonSerializer.SerializeToElement(payload), correlation);

    private static string ValidateTaskTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)
            || !string.Equals(title, title.Trim(), StringComparison.Ordinal)
            || title.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(title) > MaximumTaskTitleUtf8Bytes)
        {
            throw new ArgumentException("The Hermes task title was invalid.", nameof(title));
        }
        return title;
    }

    private static void RequirePromptReadyTask(TaskBinding task)
    {
        if (task.State != D046HermesTaskState.Idle)
        {
            throw new InvalidOperationException(
                "The Hermes task cannot accept a prompt in its current state.");
        }
    }

    private static void ValidateImageUpload(D046HermesImageUpload image)
    {
        if (string.IsNullOrWhiteSpace(image.FileName)
            || image.FileName.Any(char.IsControl)
            || !string.Equals(
                image.FileName,
                Path.GetFileName(image.FileName),
                StringComparison.Ordinal)
            || Encoding.UTF8.GetByteCount(image.FileName) > 255)
        {
            throw new ArgumentException("Image filename was rejected.", nameof(image));
        }
        if (image.Content.Length is <= 0 or > MaximumImageBytes)
            throw new ArgumentException("Image size was rejected.", nameof(image));

        var expectedMediaType = Path.GetExtension(image.FileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new ArgumentException("Image extension was rejected.", nameof(image))
        };
        if (!string.Equals(image.MediaType, expectedMediaType, StringComparison.Ordinal))
            throw new ArgumentException("Image media type was rejected.", nameof(image));

        var bytes = image.Content.Span;
        var valid = image.MediaType switch
        {
            "image/png" => bytes.Length >= 8
                && bytes[..8].SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.Length >= 4
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[^2] == 0xFF
                && bytes[^1] == 0xD9,
            "image/webp" => bytes.Length >= 12
                && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid)
            throw new ArgumentException("Image signature was rejected.", nameof(image));
    }

    private static string DecodeRuntimeFailureCode(JsonElement payload)
    {
        var failure = RequireObject(payload, "Runtime failure");
        var code = RequireString(failure, "code");
        _ = RequireString(failure, "message");
        ValidateHermesFailureCode(failure);
        return code;
    }

    private static long AttachedRequestByteLength(JsonElement payload)
    {
        payload = RequireObject(payload, "image.attach_bytes request");
        var properties = payload.EnumerateObject().Select(value => value.Name).ToArray();
        if (properties.Length != 3
            || !properties.Contains("session_id", StringComparer.Ordinal)
            || !properties.Contains("content_base64", StringComparer.Ordinal)
            || !properties.Contains("filename", StringComparer.Ordinal))
        {
            throw new InvalidDataException("image.attach_bytes request shape was rejected.");
        }
        _ = RequireString(payload, "session_id");
        _ = RequireString(payload, "filename");
        var contentBase64 = RequireString(payload, "content_base64");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "image.attach_bytes request base64 was rejected.",
                exception);
        }
        try
        {
            if (bytes.Length is <= 0 or > MaximumImageBytes)
                throw new InvalidDataException("image.attach_bytes request size was rejected.");
            return bytes.LongLength;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string ValidateCompressionFocus(string? value)
    {
        var focusTopic = (value ?? string.Empty).Trim();
        if (focusTopic.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(focusTopic) > MaximumCompressionFocusUtf8Bytes)
        {
            throw new ArgumentException("Compression focus was not valid text.", nameof(value));
        }
        return focusTopic;
    }

    private static void ValidateCompressionRequestPayload(
        JsonElement request,
        string runtimeSessionId)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in request.EnumerateObject())
        {
            if (!names.Add(property.Name)
                || property.Name is not ("session_id" or "focus_topic"))
            {
                throw new InvalidDataException(
                    "session.compress request contained an unknown or duplicate parameter.");
            }
        }
        if (names.Count is < 1 or > 2
            || !string.Equals(
                RequireIdentity(RequireString(request, "session_id"), "session_id"),
                runtimeSessionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("session.compress request escaped its active session.");
        }
        if (request.TryGetProperty("focus_topic", out var focusTopic))
        {
            var focus = RequireStringValue(focusTopic, "focus_topic");
            if (!string.Equals(focus, ValidateCompressionFocus(focus), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "session.compress focus was not canonically normalized.");
            }
        }
    }

    private static void RequireSucceeded(LilithRuntimeResponse response)
    {
        if (response.Succeeded)
            return;
        var failure = RequireObject(response.HermesPayload, "Runtime failure");
        var code = RequireString(failure, "code");
        var message = RequireString(failure, "message");
        ValidateHermesFailureCode(failure);
        throw new InvalidDataException(code + ": " + message);
    }

    private static void ValidateHermesFailureCode(JsonElement failure)
    {
        if (!failure.TryGetProperty("hermesCode", out var hermesCode)
            || hermesCode.ValueKind is not (JsonValueKind.Null or JsonValueKind.Number)
            || (hermesCode.ValueKind == JsonValueKind.Number
                && !hermesCode.TryGetInt32(out _)))
            throw new InvalidDataException("Runtime failure shape was malformed.");
    }

    private static JsonElement RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(name + " must be an object.");
        return value;
    }

    private static JsonElement RequireObjectProperty(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(name + " must be an object.");
        return property;
    }

    private static JsonElement RequireArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(name + " must be an array.");
        return property;
    }

    private static JsonElement RequirePayloadObject(JsonElement outer, string type)
    {
        if (!outer.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(type + " payload must be an object.");
        return payload;
    }

    private static string RequireString(
        JsonElement value,
        string name,
        bool allowEmpty = false)
    {
        if (!value.TryGetProperty(name, out var property))
            throw new InvalidDataException(name + " was missing.");
        return RequireStringValue(property, name, allowEmpty);
    }

    private static string RequireStringValue(
        JsonElement value,
        string name,
        bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(name + " must be a string.");
        var text = value.GetString()!;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text))
            || text.Any(char.IsControl))
            throw new InvalidDataException(name + " was not valid text.");
        return text;
    }

    private static string RequireNullableString(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
            throw new InvalidDataException(name + " was missing.");
        return property.ValueKind == JsonValueKind.Null
            ? string.Empty
            : RequireStringValue(property, name, allowEmpty: true);
    }

    private static string RequireContentString(
        JsonElement value,
        string name,
        bool allowEmpty = false)
    {
        if (!value.TryGetProperty(name, out var property))
            throw new InvalidDataException(name + " was missing.");
        return RequireContentStringValue(property, name, allowEmpty);
    }

    private static string RequireContentStringValue(
        JsonElement value,
        string name,
        bool allowEmpty = false)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException(name + " must be a string.");
        var text = value.GetString()!;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text))
            || text.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException(name + " was not valid content text.");
        }
        return text;
    }

    private static bool RequireBoolean(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(name + " must be a boolean.");
        return property.GetBoolean();
    }

    private static bool RequireBooleanValue(JsonElement value, string name)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(name + " must be a boolean.");
        return value.GetBoolean();
    }

    private static int RequireNonNegativeInteger(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var number)
            || number < 0)
            throw new InvalidDataException(name + " must be a non-negative integer.");
        return number;
    }

    private static int RequirePositiveInteger(JsonElement value, string name)
    {
        var number = RequireNonNegativeInteger(value, name);
        if (number == 0)
            throw new InvalidDataException(name + " must be a positive integer.");
        return number;
    }

    private static long RequireNonNegativeLong(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var number)
            || number < 0)
            throw new InvalidDataException(name + " must be a non-negative integer.");
        return number;
    }

    private static int OptionalNonNegativeInteger(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
            return 0;
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var number)
            || number < 0)
        {
            throw new InvalidDataException(name + " must be a non-negative integer.");
        }
        return number;
    }

    private static D046HermesUsageProjection ParseUsage(JsonElement result)
    {
        result = RequireObject(result, "Hermes usage");
        var contextUsed = OptionalNonNegativeLong(result, "context_used");
        var contextMaximum = OptionalNonNegativeLong(result, "context_max");
        var contextPercent = OptionalPercentage(result, "context_percent");
        if (contextPercent.HasValue
            && (!contextUsed.HasValue || !contextMaximum.HasValue))
        {
            throw new InvalidDataException(
                "Hermes usage context percentage was not accompanied by counts.");
        }
        return new D046HermesUsageProjection(
            IsAvailable: true,
            StatusCode: "ready",
            RequireNonNegativeLong(result, "calls"),
            RequireNonNegativeLong(result, "input"),
            RequireNonNegativeLong(result, "output"),
            RequireNonNegativeLong(result, "total"),
            contextUsed,
            contextMaximum,
            contextPercent,
            OptionalNonNegativeInteger(result, "compressions"),
            OptionalNonNegativeInteger(result, "active_subagents"),
            OptionalText(result, "model"));
    }

    private static D046HermesUsageProjection UnavailableUsage(string statusCode)
        => new(
            IsAvailable: false,
            statusCode,
            0,
            0,
            0,
            0,
            null,
            null,
            null,
            0,
            0,
            string.Empty);

    private static long? OptionalNonNegativeLong(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
            return null;
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var number)
            || number < 0)
        {
            throw new InvalidDataException(name + " must be a non-negative integer.");
        }
        return number;
    }

    private static double RequirePercentage(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var number)
            || !double.IsFinite(number)
            || number is < 0 or > 100)
        {
            throw new InvalidDataException(name + " must be a percentage.");
        }
        return number;
    }

    private static double? OptionalPercentage(JsonElement value, string name)
        => value.TryGetProperty(name, out _)
            ? RequirePercentage(value, name)
            : null;

    private static double RequireFiniteNumber(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property))
            throw new InvalidDataException(name + " was missing.");
        return RequireFiniteNumberValue(property, name);
    }

    private static double RequireFiniteNumberValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number)
            || !double.IsFinite(number))
        {
            throw new InvalidDataException(name + " must be a finite number.");
        }
        return number;
    }

    private static string OptionalText(JsonElement value, string name)
        => value.TryGetProperty(name, out var property)
            ? RequireStringValue(property, name, allowEmpty: true)
            : string.Empty;

    private static DateTimeOffset RequireUnixSeconds(
        JsonElement value,
        string name,
        DateTimeOffset fallback)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0
            || seconds > 253_402_300_799)
            throw new InvalidDataException(name + " must be a Unix timestamp.");
        if (seconds == 0)
            return fallback;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(seconds * 1000)));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(name + " was outside the supported range.", exception);
        }
    }

    private static DateTimeOffset OptionalHistoryUnixSeconds(
        JsonElement value,
        string name,
        DateTimeOffset fallback)
    {
        if (!value.TryGetProperty(name, out var property))
            return fallback;
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var seconds)
            || !double.IsFinite(seconds)
            || seconds < 0
            || seconds > 253_402_300_799)
        {
            throw new InvalidDataException(name + " must be a Unix timestamp.");
        }
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(
                checked((long)(seconds * 1000)));
        }
        catch (Exception exception)
            when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException(
                name + " was outside the supported range.",
                exception);
        }
    }

    private static string RequireIdentity(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 1024
            || value.Any(char.IsControl))
            throw new InvalidDataException(name + " was not a valid identity.");
        return value;
    }

    private D046HermesNativeToolsCatalog DecodeNativeToolsCatalogResult(
        JsonElement value,
        TaskBinding task)
    {
        value = RequireObject(value, "Hermes native tool catalog");
        var expected = new[]
            {
                "platform",
                "approval_mode",
                "enabled_toolsets",
                "toolsets",
                "new_session_required"
            };
        ValidateExactProperties(value, expected.Concat(new[] { "mcp_catalog", "presets", "tool_discovery_enabled" }
            .Where(name => value.TryGetProperty(name, out _))).ToArray(), "Hermes native tool catalog");

        var platform = RequireString(value, "platform");
        if (!string.Equals(platform, "cli", StringComparison.Ordinal))
            throw new InvalidDataException("tools.catalog returned an unsupported platform.");
        var approvalMode = ParseNativeApprovalMode(
            RequireString(value, "approval_mode"));
        var enabled = ReadUniqueIdentityArray(
            value,
            "enabled_toolsets",
            maximumCount: 256);
        var enabledSet = new HashSet<string>(enabled, StringComparer.Ordinal);
        var rowsValue = RequireArray(value, "toolsets");
        if (rowsValue.GetArrayLength() > 256)
            throw new InvalidDataException("tools.catalog returned too many toolsets.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<D046HermesNativeToolsetOption>();
        foreach (var item in rowsValue.EnumerateArray())
        {
            var row = RequireObject(item, "tools.catalog toolset");
            ValidateExactProperties(
                row,
                (new[]
                {
                    "name",
                    "label",
                    "description",
                    "enabled",
                    "configured",
                    "readiness",
                    "tools"
                }).Concat(row.TryGetProperty("setup_providers", out _) ? new[] { "setup_providers" } : Array.Empty<string>()).ToArray(),
                "tools.catalog toolset");
            var name = RequireBoundedIdentity(row, "name", 512);
            if (!names.Add(name))
                throw new InvalidDataException("tools.catalog duplicated a toolset.");
            var label = RequireBoundedDisplayText(row, "label", 2 * 1024, allowEmpty: false);
            var description = RequireBoundedDisplayText(
                row,
                "description",
                16 * 1024,
                allowEmpty: true);
            var isEnabled = RequireBoolean(row, "enabled");
            var configured = RequireBoolean(row, "configured");
            var readiness = ParseNativeToolsetReadiness(
                RequireString(row, "readiness"));
            if (configured != (readiness == D046HermesNativeToolsetReadiness.Ready))
            {
                throw new InvalidDataException(
                    "tools.catalog configured/readiness invariant was rejected.");
            }
            if (isEnabled != enabledSet.Contains(name))
            {
                throw new InvalidDataException(
                    "tools.catalog enabled rows did not match enabled_toolsets.");
            }
            var tools = ReadUniqueIdentityArray(
                row,
                "tools",
                maximumCount: 512,
                maximumItemUtf8Bytes: 1024);
            rows.Add(new D046HermesNativeToolsetOption(
                name,
                label,
                description,
                isEnabled,
                configured,
                readiness,
                tools,
                HermesNativeToolsContract.DecodeSetupProviders(row)));
        }
        if (!enabledSet.IsSubsetOf(names))
            throw new InvalidDataException("tools.catalog enabled an unknown toolset.");

        return new D046HermesNativeToolsCatalog(
            _runtimeGeneration,
            task.TaskId,
            task.RuntimeSessionId,
            platform,
            approvalMode,
            enabled,
            rows.AsReadOnly(),
            RequireBoolean(value, "new_session_required"),
            HermesNativeToolsContract.DecodeMcpCatalog(value),
            HermesNativeToolsContract.DecodePresets(value, names),
            HermesNativeToolsContract.DecodeToolDiscoveryEnabled(value));
    }

    private static IReadOnlyList<string> ValidateNativeToolsSelection(
        IReadOnlyList<string>? selected,
        IEnumerable<string> catalogNames)
    {
        if (selected is null || selected.Count > 256)
            throw new ArgumentException("Native toolset selection was malformed.", nameof(selected));
        var available = new HashSet<string>(catalogNames, StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>(selected.Count);
        foreach (var value in selected)
        {
            var name = RequireIdentity(value, "enabled toolset");
            if (Encoding.UTF8.GetByteCount(name) > 512
                || !observed.Add(name)
                || !available.Contains(name))
            {
                throw new ArgumentException(
                    "Native toolset selection was not certified by tools.catalog.",
                    nameof(selected));
            }
            ordered.Add(name);
        }
        return ordered.AsReadOnly();
    }

    private static IReadOnlyList<string> ReadUniqueIdentityArray(
        JsonElement value,
        string name,
        int maximumCount,
        int maximumItemUtf8Bytes = 512)
    {
        var property = RequireArray(value, name);
        if (property.GetArrayLength() > maximumCount)
            throw new InvalidDataException(name + " exceeded its item limit.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<string>(property.GetArrayLength());
        foreach (var item in property.EnumerateArray())
        {
            var identity = RequireIdentity(
                RequireStringValue(item, name + " item"),
                name + " item");
            if (Encoding.UTF8.GetByteCount(identity) > maximumItemUtf8Bytes
                || !seen.Add(identity))
            {
                throw new InvalidDataException(name + " contained an invalid item.");
            }
            values.Add(identity);
        }
        return values.AsReadOnly();
    }

    private static string RequireBoundedDisplayText(
        JsonElement value,
        string name,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        var text = RequireContentString(value, name, allowEmpty);
        if (Encoding.UTF8.GetByteCount(text) > maximumUtf8Bytes)
            throw new InvalidDataException(name + " exceeded its size limit.");
        return text;
    }

    private static D046HermesNativeToolsetReadiness ParseNativeToolsetReadiness(
        string value)
        => value switch
        {
            "ready" => D046HermesNativeToolsetReadiness.Ready,
            "needs_setup" => D046HermesNativeToolsetReadiness.NeedsSetup,
            "needs_auth" => D046HermesNativeToolsetReadiness.NeedsAuthentication,
            "needs_keys" => D046HermesNativeToolsetReadiness.NeedsKeys,
            _ => throw new InvalidDataException("tools.catalog readiness was not recognized.")
        };

    private static D046HermesNativeApprovalMode ParseNativeApprovalMode(string value)
        => value switch
        {
            "manual" => D046HermesNativeApprovalMode.Manual,
            "smart" => D046HermesNativeApprovalMode.Smart,
            "off" => D046HermesNativeApprovalMode.Off,
            _ => throw new InvalidDataException("Hermes native approval mode was not recognized.")
        };

    private static string NativeApprovalModeWire(D046HermesNativeApprovalMode value)
        => value switch
        {
            D046HermesNativeApprovalMode.Manual => "manual",
            D046HermesNativeApprovalMode.Smart => "smart",
            D046HermesNativeApprovalMode.Off => "off",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static bool IsSupportedNativeApprovalMode(D046HermesNativeApprovalMode value)
        => value is D046HermesNativeApprovalMode.Manual
            or D046HermesNativeApprovalMode.Smart
            or D046HermesNativeApprovalMode.Off;

    private static int RequireHermesFailureCode(JsonElement failure)
    {
        ValidateHermesFailureCode(failure);
        var value = failure.GetProperty("hermesCode");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var code))
            throw new InvalidDataException("Runtime failure omitted its Hermes error code.");
        return code;
    }

    private static string RequireSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException(name + " was not a SHA-256 digest.");
        return value.ToUpperInvariant();
    }

    private static string RequireBoundedIdentity(
        JsonElement value,
        string name,
        int maximumUtf8Bytes)
    {
        var identity = RequireIdentity(RequireString(value, name), name);
        if (Encoding.UTF8.GetByteCount(identity) > maximumUtf8Bytes)
            throw new InvalidDataException(name + " exceeded its size limit.");
        return identity;
    }

    private static void ValidateOptionalApprovalPatternKeys(
        JsonElement payload,
        string primaryPatternKey)
    {
        if (!payload.TryGetProperty("pattern_keys", out var values))
            return;
        if (values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() is < 1 or > 64)
        {
            throw new InvalidDataException(
                "approval.request pattern_keys were malformed.");
        }

        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            var pattern = RequireIdentity(
                RequireStringValue(value, "pattern_keys item"),
                "pattern_keys item");
            if (Encoding.UTF8.GetByteCount(pattern) > 256
                || !observed.Add(pattern))
            {
                throw new InvalidDataException(
                    "approval.request pattern_keys were malformed.");
            }
        }
        if (!observed.Contains(primaryPatternKey))
        {
            throw new InvalidDataException(
                "approval.request pattern_keys omitted the primary pattern.");
        }
    }

    private static void ValidateOptionalBoundedApprovalText(
        JsonElement payload,
        string name,
        int maximumUtf8Bytes)
    {
        if (!payload.TryGetProperty(name, out var value))
            return;
        var text = RequireContentStringValue(value, name, allowEmpty: true);
        if (Encoding.UTF8.GetByteCount(text) > maximumUtf8Bytes)
            throw new InvalidDataException(name + " exceeded its size limit.");
    }

    private static void ValidateOptionalBoolean(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out var property)
            && property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(name + " must be a boolean.");
        }
    }

    private static string RequireControlText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Encoding.UTF8.GetByteCount(value) > 64 * 1024
            || value.Any(character => char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException(name + " was not valid control text.");
        }
        return value;
    }

    private static string RequireBoundedContent(
        JsonElement value,
        string name,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        var text = RequireContentString(value, name, allowEmpty);
        if (Encoding.UTF8.GetByteCount(text) > maximumUtf8Bytes)
            throw new InvalidDataException(name + " exceeded its size limit.");
        return text;
    }

    private static string RequireRollbackHash(string value, string name)
    {
        if (value.Length != 40
            || value.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                name + " must be a full lowercase 40-hex commit hash.");
        }
        return value;
    }

    private static bool IsRollbackHashPrefix(string value, string fullHash)
        => value.Length is >= 7 and <= 40
            && fullHash.StartsWith(value, StringComparison.Ordinal)
            && value.All(character => character is (>= '0' and <= '9')
                or (>= 'a' and <= 'f'));

    private static string RequireSubagentId(string value)
    {
        var identity = RequireIdentity(value, "subagent_id");
        if (Encoding.UTF8.GetByteCount(identity) > 1024)
            throw new InvalidDataException("subagent_id exceeded its size limit.");
        return identity;
    }

    private static string OptionalNullableIdentity(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }
        return RequireIdentity(
            RequireStringValue(property, name),
            name);
    }

    private static IReadOnlyList<string> RequireExactDelegationChildToolsets(
        JsonElement value)
    {
        const string name = "toolsets";
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Array
            || property.GetArrayLength() != 2)
        {
            throw new InvalidDataException(
                "Hermes child toolsets did not match the exact safe capability set.");
        }
        var values = new List<string>(property.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateArray())
        {
            var text = RequireStringValue(item, name + " item");
            if (!seen.Add(text))
                throw new InvalidDataException(name + " contained a duplicate.");
            values.Add(text);
        }
        if (!seen.SetEquals(new[] { "skills", "session_search" }))
        {
            throw new InvalidDataException(
                "Hermes child toolsets did not match the exact safe capability set.");
        }
        return values.AsReadOnly();
    }

    private static D046HermesSubagentState DecodeSubagentCompletionState(
        string value)
        => value switch
        {
            "completed" => D046HermesSubagentState.Completed,
            "failed" => D046HermesSubagentState.Failed,
            "interrupted" => D046HermesSubagentState.Interrupted,
            "timeout" => D046HermesSubagentState.TimedOut,
            "error" => D046HermesSubagentState.Error,
            _ => D046HermesSubagentState.Unknown
        };

    private static void ValidateExactProperties(
        JsonElement value,
        IReadOnlyCollection<string> expected,
        string name)
    {
        value = RequireObject(value, name);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!observed.Add(property.Name))
                throw new InvalidDataException(name + " contained a duplicate property.");
        }
        if (!observed.SetEquals(expected))
            throw new InvalidDataException(name + " shape was rejected.");
    }

    private static void ValidateAllowedProperties(
        JsonElement value,
        IReadOnlyCollection<string> required,
        IReadOnlyCollection<string> allowed,
        string name)
    {
        value = RequireObject(value, name);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!observed.Add(property.Name) || !allowedSet.Contains(property.Name))
                throw new InvalidDataException(name + " shape was rejected.");
        }
        if (required.Any(property => !observed.Contains(property)))
            throw new InvalidDataException(name + " shape was rejected.");
    }

    private static string RequireExactEcho(
        JsonElement result,
        JsonElement request,
        string name,
        bool content = false)
    {
        var expected = content
            ? RequireContentString(request, name)
            : RequireString(request, name);
        var actual = content
            ? RequireContentString(result, name)
            : RequireString(result, name);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException(name + " did not exactly match the request.");
        return actual;
    }

    private static string TaskIdFor(string storedSessionId)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(storedSessionId))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return "hermes:" + encoded;
    }

    private static string DisplayTitle(string title, string preview)
        => !string.IsNullOrWhiteSpace(title)
            ? title
            : !string.IsNullOrWhiteSpace(preview)
                ? preview
                : "Untitled";

    private static string? FirstString(JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!value.TryGetProperty(name, out var property))
                continue;
            var text = RequireStringValue(property, name, allowEmpty: true);
            if (text.Length != 0)
                return text;
        }
        return null;
    }

    private static string? FirstContentString(JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!value.TryGetProperty(name, out var property))
                continue;
            var text = RequireContentStringValue(property, name, allowEmpty: true);
            if (text.Length != 0)
                return text;
        }
        return null;
    }

    private static string? OptionalContentString(JsonElement value, string name)
        => value.TryGetProperty(name, out var property)
            ? RequireContentStringValue(property, name, allowEmpty: true)
            : null;

    private static bool OptionalBooleanValue(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) && property.GetBoolean();

    private static string SafeDisplay(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.GetRawText();
        return text.Length <= 256 ? text : text[..256];
    }

    private static string HistoryReasoningText(JsonElement item)
    {
        foreach (var name in new[]
                 {
                     "reasoning",
                     "reasoning_content",
                     "reasoning_details",
                     "codex_reasoning_items"
                 })
        {
            if (!item.TryGetProperty(name, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : value.GetRawText();
        }
        return string.Empty;
    }

    private static string HistoryMessageId(JsonElement item, int itemIndex)
    {
        if (item.TryGetProperty("row_id", out var rowId))
        {
            if (rowId.ValueKind == JsonValueKind.Number && rowId.TryGetInt64(out var number))
                return "history-row-" + number.ToString(CultureInfo.InvariantCulture);
            if (rowId.ValueKind == JsonValueKind.String)
                return "history-row-" + RequireIdentity(rowId.GetString()!, "row_id");
            throw new InvalidDataException("Stored message row ID was malformed.");
        }
        return "history-message-" + itemIndex.ToString(CultureInfo.InvariantCulture);
    }

    private static string responseRaw(JsonElement value) => value.GetRawText();

    private sealed class TaskBinding
    {
        internal TaskBinding(string taskId, string storedSessionId)
        {
            TaskId = taskId;
            StoredSessionId = storedSessionId;
        }

        internal string TaskId { get; }
        internal string StoredSessionId { get; }
        internal string RuntimeSessionId { get; set; } = string.Empty;
        internal string Title { get; set; } = "Untitled";
        internal string ProviderId { get; set; } = string.Empty;
        internal string ModelDisplayName { get; set; } = string.Empty;
        internal D046HermesTaskState State { get; set; } = D046HermesTaskState.Idle;
        internal DateTimeOffset UpdatedUtc { get; set; }
        internal long LastActivitySequence { get; set; }
        internal bool IsPinned { get; set; }
        internal List<D046HermesProjectionEvent> Events { get; } = new();
        internal Dictionary<string, string> ActiveMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> ActiveReasoningIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<string> KnownTurnIds { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> CompletedTurnIds { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> ClaimedUserMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal Dictionary<string, string> ClaimedAssistantMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<(string TurnId, string MessageId)>
            ClaimedAuxiliaryAssistantMessageIds { get; } = new();
        internal HashSet<(string TurnId, string MessageId)>
            ClaimedInterimAssistantMessageIds { get; } = new();
        internal Dictionary<string, string> LastInterimAssistantMessageIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<string> TerminalErrorMarkerTurnIds { get; } =
            new(StringComparer.Ordinal);
        internal HashSet<string> TerminalErrorDisclosedTurnIds { get; } =
            new(StringComparer.Ordinal);
        internal bool SubmitPending { get; set; }
        internal bool AwaitingQueuedTurn { get; set; }
        internal bool PendingTurnTerminalObserved { get; set; }
        internal string PendingTurnId { get; set; } = string.Empty;
        internal string PendingUserMessageId { get; set; } = string.Empty;
        internal string PendingAssistantMessageId { get; set; } = string.Empty;
        internal PendingInflightTurn? PendingInflight { get; set; }
        internal PendingTerminalError? PendingTerminalError { get; set; }
        internal PendingBudgetConstraints? PendingBudgetConstraints { get; set; }
        internal string PendingQueuedPromptText { get; set; } = string.Empty;
        internal TaskCompletionSource<bool>? NoEffectAttestation { get; set; }
        internal bool CredentialUnavailable { get; set; }
        internal ModelPair? PendingCredentialRecovery { get; set; }
        internal Queue<D046HermesPendingApprovalProjection> PendingApprovals { get; } =
            new();
        internal HashSet<string> PendingAttachedImagePaths { get; } =
            new(StringComparer.Ordinal);
        internal bool ApprovalResponseInFlight { get; set; }
        internal bool ApprovalResponseQuarantined { get; set; }
        internal D046HermesPendingClarificationProjection? PendingClarification { get; set; }
        internal bool ClarificationResponseInFlight { get; set; }
        internal string ClarificationResponseRequestId { get; set; } = string.Empty;
        internal D046HermesPendingSecureInputProjection? PendingSecureInput { get; set; }
        internal bool SecureInputResponseInFlight { get; set; }
        internal string SecureInputResponseRequestId { get; set; } = string.Empty;
        internal D046HermesSecureInputKind? SecureInputResponseKind { get; set; }
        internal HashSet<ModelPair> CertifiedModelPairs { get; set; } = new();
        internal HashSet<string> CertifiedNativeToolNames { get; set; } =
            new(StringComparer.Ordinal);
        internal D046HermesNativeToolsCatalog? NativeToolsCatalog { get; set; }
        internal bool NativeToolsConfigStale { get; set; }
        internal bool NativeApprovalOnlyReactivation { get; set; }
        internal bool NativeToolsConfigStaleTurnActive { get; set; }
        internal bool RollbackCatalogAvailable { get; set; }
        internal HashSet<string> RollbackCheckpointHashes { get; } =
            new(StringComparer.Ordinal);
        internal string RollbackConfirmationHash { get; set; } = string.Empty;
        internal string RollbackConfirmationToken { get; set; } = string.Empty;
        internal D046HermesUsageProjection Usage { get; set; } =
            UnavailableUsage("awaiting_session_info");
    }

    private readonly record struct ModelPair(string ProviderId, string ModelId);

    private static UsageBudgetFailure? DecodeBudgetError(JsonElement payload, bool isError)
    {
        if (!payload.TryGetProperty("budget_error", out var value)) return null;
        if (!isError || value.ValueKind != JsonValueKind.String
            || payload.EnumerateObject().Count(property => property.Name == "budget_error") != 1)
            throw new InvalidDataException("Native economic failure metadata was malformed.");
        return UsageBudgetErrorPresentation.Parse(value.GetString()!);
    }

    private sealed record PendingInflightTurn(
        string TurnId,
        string UserMessageId,
        string AssistantMessageId,
        string User,
        string Assistant,
        bool Streaming,
        IReadOnlyList<string> Corrections,
        string Error,
        bool Recoverable,
        string CanonicalPayloadJson,
        UsageBudgetFailure? BudgetError = null,
        IReadOnlyList<UsageBudgetConstraint>? BudgetConstraints = null);

    private sealed record PendingTerminalError(
        string TurnId,
        string Error,
        bool Recoverable,
        string CanonicalPayloadJson,
        UsageBudgetFailure? BudgetError = null,
        IReadOnlyList<UsageBudgetConstraint>? BudgetConstraints = null);

    private sealed record PendingBudgetConstraints(string TurnId,
        IReadOnlyList<UsageBudgetConstraint> Constraints, string CanonicalPayloadJson);

    private sealed record TaskEventMutationSnapshot(
        D046HermesTaskState State,
        bool AwaitingQueuedTurn,
        bool PendingTurnTerminalObserved,
        string PendingTurnId,
        string PendingUserMessageId,
        string PendingAssistantMessageId,
        bool NativeToolsConfigStaleTurnActive,
        D046HermesPendingClarificationProjection? PendingClarification,
        bool ClarificationResponseInFlight,
        string ClarificationResponseRequestId,
        D046HermesPendingSecureInputProjection? PendingSecureInput,
        bool SecureInputResponseInFlight,
        string SecureInputResponseRequestId,
        D046HermesSecureInputKind? SecureInputResponseKind,
        Dictionary<string, string> ActiveMessageIds,
        Dictionary<string, string> ActiveReasoningIds,
        HashSet<string> KnownTurnIds,
        HashSet<string> CompletedTurnIds,
        Dictionary<string, string> ClaimedUserMessageIds,
        Dictionary<string, string> ClaimedAssistantMessageIds,
        HashSet<(string TurnId, string MessageId)> ClaimedAuxiliaryAssistantMessageIds,
        HashSet<(string TurnId, string MessageId)> ClaimedInterimAssistantMessageIds,
        Dictionary<string, string> LastInterimAssistantMessageIds,
        HashSet<string> TerminalErrorMarkerTurnIds,
        HashSet<string> TerminalErrorDisclosedTurnIds)
    {
        internal static TaskEventMutationSnapshot Capture(TaskBinding task)
            => new(
                task.State,
                task.AwaitingQueuedTurn,
                task.PendingTurnTerminalObserved,
                task.PendingTurnId,
                task.PendingUserMessageId,
                task.PendingAssistantMessageId,
                task.NativeToolsConfigStaleTurnActive,
                task.PendingClarification,
                task.ClarificationResponseInFlight,
                task.ClarificationResponseRequestId,
                task.PendingSecureInput,
                task.SecureInputResponseInFlight,
                task.SecureInputResponseRequestId,
                task.SecureInputResponseKind,
                new(task.ActiveMessageIds, StringComparer.Ordinal),
                new(task.ActiveReasoningIds, StringComparer.Ordinal),
                new(task.KnownTurnIds, StringComparer.Ordinal),
                new(task.CompletedTurnIds, StringComparer.Ordinal),
                new(task.ClaimedUserMessageIds, StringComparer.Ordinal),
                new(task.ClaimedAssistantMessageIds, StringComparer.Ordinal),
                new(task.ClaimedAuxiliaryAssistantMessageIds),
                new(task.ClaimedInterimAssistantMessageIds),
                new(task.LastInterimAssistantMessageIds, StringComparer.Ordinal),
                new(task.TerminalErrorMarkerTurnIds, StringComparer.Ordinal),
                new(task.TerminalErrorDisclosedTurnIds, StringComparer.Ordinal));

        internal void Restore(TaskBinding task)
        {
            task.State = State;
            task.AwaitingQueuedTurn = AwaitingQueuedTurn;
            task.PendingTurnTerminalObserved = PendingTurnTerminalObserved;
            task.PendingTurnId = PendingTurnId;
            task.PendingUserMessageId = PendingUserMessageId;
            task.PendingAssistantMessageId = PendingAssistantMessageId;
            task.NativeToolsConfigStaleTurnActive = NativeToolsConfigStaleTurnActive;
            task.PendingClarification = PendingClarification;
            task.ClarificationResponseInFlight = ClarificationResponseInFlight;
            task.ClarificationResponseRequestId = ClarificationResponseRequestId;
            task.PendingSecureInput = PendingSecureInput;
            task.SecureInputResponseInFlight = SecureInputResponseInFlight;
            task.SecureInputResponseRequestId = SecureInputResponseRequestId;
            task.SecureInputResponseKind = SecureInputResponseKind;
            Replace(task.ActiveMessageIds, ActiveMessageIds);
            Replace(task.ActiveReasoningIds, ActiveReasoningIds);
            Replace(task.KnownTurnIds, KnownTurnIds);
            Replace(task.CompletedTurnIds, CompletedTurnIds);
            Replace(task.ClaimedUserMessageIds, ClaimedUserMessageIds);
            Replace(task.ClaimedAssistantMessageIds, ClaimedAssistantMessageIds);
            Replace(
                task.ClaimedAuxiliaryAssistantMessageIds,
                ClaimedAuxiliaryAssistantMessageIds);
            Replace(
                task.ClaimedInterimAssistantMessageIds,
                ClaimedInterimAssistantMessageIds);
            Replace(task.LastInterimAssistantMessageIds, LastInterimAssistantMessageIds);
            Replace(task.TerminalErrorMarkerTurnIds, TerminalErrorMarkerTurnIds);
            Replace(task.TerminalErrorDisclosedTurnIds, TerminalErrorDisclosedTurnIds);
        }

        private static void Replace<TKey, TValue>(
            Dictionary<TKey, TValue> target,
            Dictionary<TKey, TValue> source)
            where TKey : notnull
        {
            target.Clear();
            foreach (var pair in source)
                target.Add(pair.Key, pair.Value);
        }

        private static void Replace<T>(HashSet<T> target, HashSet<T> source)
        {
            target.Clear();
            target.UnionWith(source);
        }
    }

    private sealed record ActivationInfo(
        string Provider,
        string Model,
        bool NoEffectAttested,
        IReadOnlyList<string> ToolNames);
}
