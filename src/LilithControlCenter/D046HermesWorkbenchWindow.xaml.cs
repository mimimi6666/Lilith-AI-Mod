using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LilithRuntimeContracts;
using Microsoft.Win32;

namespace LilithControlCenter;

internal readonly record struct D046HermesSessionStatusCorrelation(
    string RuntimeGeneration,
    string TaskId,
    string SessionId)
{
    internal bool Matches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(
                RuntimeGeneration,
                runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(SessionId, sessionId, StringComparison.Ordinal);
}

internal readonly record struct D046HermesTurnStatusCorrelation(
    D046HermesSessionStatusCorrelation Session,
    string TurnId)
{
    internal static bool TryCreate(
        D046HermesProjectionEvent value,
        out D046HermesTurnStatusCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ScopeKind != D046HermesEventScopeKind.Turn
            || !HermesTurnCorrelationContract.IsIdentity(value.TurnId))
        {
            correlation = default;
            return false;
        }

        correlation = new D046HermesTurnStatusCorrelation(
            new D046HermesSessionStatusCorrelation(
                value.RuntimeGeneration,
                value.TaskId,
                value.SessionId),
            value.TurnId);
        return true;
    }

    internal static bool TryCreate(
        D046HermesSubmitReceipt receipt,
        out D046HermesTurnStatusCorrelation correlation)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.CreatesTurn)
        {
            correlation = default;
            return false;
        }

        correlation = new D046HermesTurnStatusCorrelation(
            new D046HermesSessionStatusCorrelation(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId),
            receipt.TurnId);
        return true;
    }
}

internal sealed class D046HermesTaskItemViewModel : INotifyPropertyChanged
{
    private string _sessionId;
    private string _lineageId;
    private string _title;
    private string _providerId;
    private string _modelDisplayName;
    private D046HermesTaskState _state;
    private DateTimeOffset _updatedUtc;
    private bool _isPinned;
    private long _lastAppliedSequence;
    private long _lastConversationActivitySequence;
    private bool _emergencyStopRequired;
    private bool _connectionLost;

    internal D046HermesTaskItemViewModel(
        D046HermesTaskProjection projection,
        long throughSequence = 0)
    {
        ArgumentNullException.ThrowIfNull(projection);
        TaskId = projection.TaskId;
        _sessionId = projection.SessionId;
        _lineageId = projection.LineageId;
        _title = projection.Title;
        _providerId = projection.ProviderId;
        _modelDisplayName = projection.ModelDisplayName;
        _state = projection.State;
        _updatedUtc = projection.UpdatedUtc;
        _isPinned = projection.IsPinned;
        _lastAppliedSequence = throughSequence;
    }

    public string TaskId { get; }
    public string SessionId => _sessionId;
    public string LineageId => _lineageId;
    public string Title => string.IsNullOrWhiteSpace(_title)
        ? ControlCenterLocalization.Current.Get("state.untitled.session.0")
        : _title;
    public string ProviderId => _providerId;
    public string ModelDisplayName => string.IsNullOrWhiteSpace(_modelDisplayName)
        ? ControlCenterLocalization.Current.Get("state.model.not.reported.1")
        : _modelDisplayName;
    public D046HermesTaskState State => _state;
    public DateTimeOffset UpdatedUtc => _updatedUtc;
    internal long LastConversationActivitySequence =>
        _lastConversationActivitySequence;
    public bool IsPinned => _isPinned;
    public string PinLabel => _isPinned ? ControlCenterLocalization.Current.Get("state.unpin.2") : ControlCenterLocalization.Current.Get("state.pin.chat.3");
    public string PinMark => _isPinned ? "◆" : string.Empty;
    public bool EmergencyStopRequired => _emergencyStopRequired;
    public bool CanManage => !_connectionLost && !_emergencyStopRequired
        && _state is D046HermesTaskState.Idle
            or D046HermesTaskState.Failed;
    public string SessionDisplay => string.IsNullOrWhiteSpace(_sessionId)
        ? ControlCenterLocalization.Current.Get("state.session.not.created.4")
        : "session · " + Compact(_sessionId);
    public string StateText => _connectionLost
        ? ControlCenterLocalization.Current.Get("state.disconnected.status.unconfirmed.5")
        : _emergencyStopRequired
        ? ControlCenterLocalization.Current.Get("state.stop.required.6")
        : _state switch
    {
        D046HermesTaskState.Idle => ControlCenterLocalization.Current.Get("state.idle.7"),
        D046HermesTaskState.Streaming => ControlCenterLocalization.Current.Get("state.replying.8"),
        D046HermesTaskState.WaitingForClarification => ControlCenterLocalization.Current.Get("state.waiting.for.clarification.9"),
        D046HermesTaskState.WaitingForApproval => ControlCenterLocalization.Current.Get("state.waiting.for.runtime.approval.10"),
        D046HermesTaskState.WaitingForSecureInput => ControlCenterLocalization.Current.Get("state.waiting.for.secure.input.11"),
        D046HermesTaskState.Stopping => ControlCenterLocalization.Current.Get("state.stopping.12"),
        D046HermesTaskState.Failed => ControlCenterLocalization.Current.Get("state.an.error.occurred.13"),
        _ => ControlCenterLocalization.Current.Get("state.unknown.state.14")
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void MarkConnectionLost()
    {
        _connectionLost = true;
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanManage));
    }

    internal void ApplySession(
        string sessionId,
        D046HermesSessionPayload payload)
    {
        _sessionId = sessionId;
        _lineageId = payload.LineageId;
        _title = payload.Title;
        _providerId = payload.ProviderId;
        _modelDisplayName = payload.ModelDisplayName;
        RaiseAll();
    }

    internal void ApplyStatus(D046HermesStatusPayload payload)
    {
        ApplyState(payload.TaskState);
    }

    internal bool ApplyProjection(
        D046HermesTaskProjection projection,
        long throughSequence)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!string.Equals(TaskId, projection.TaskId, StringComparison.Ordinal))
            throw new InvalidOperationException("聊天識別已變更。");
        if (throughSequence < _lastAppliedSequence)
            return false;

        _lastAppliedSequence = throughSequence;
        _sessionId = projection.SessionId;
        _lineageId = projection.LineageId;
        _title = projection.Title;
        _providerId = projection.ProviderId;
        _modelDisplayName = projection.ModelDisplayName;
        _state = projection.State;
        _isPinned = projection.IsPinned;
        if (_state == D046HermesTaskState.Idle)
            _emergencyStopRequired = false;
        _updatedUtc = projection.UpdatedUtc;
        RaiseAll();
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(EmergencyStopRequired));
        OnPropertyChanged(nameof(UpdatedUtc));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinLabel));
        OnPropertyChanged(nameof(PinMark));
        return true;
    }

    internal void ApplyPinState(bool isPinned)
    {
        if (_isPinned == isPinned)
            return;
        _isPinned = isPinned;
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinLabel));
        OnPropertyChanged(nameof(PinMark));
    }

    internal bool ApplyLive(
        long sequence,
        DateTimeOffset occurredUtc,
        D046HermesProjectionPayload payload,
        D046HermesTaskState? effectiveTaskState = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var updatesConversationActivity =
            D052HermesConversationActivityPolicy.TryGetConversationActivityUtc(
                payload,
                out var durableActivityUtc);
        if (!TryAdvance(
                sequence,
                updatesConversationActivity ? durableActivityUtc : occurredUtc,
                updatesConversationActivity))
            return false;
        ApplyState(effectiveTaskState
            ?? D046HermesTaskStateReducer.Reduce(State, payload));
        return true;
    }

    internal void ApplyState(D046HermesTaskState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanManage));
    }

    internal void RequireEmergencyStop()
    {
        if (_emergencyStopRequired)
            return;
        _emergencyStopRequired = true;
        OnPropertyChanged(nameof(EmergencyStopRequired));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanManage));
    }

    internal void ClearEmergencyStop()
    {
        if (!_emergencyStopRequired)
            return;
        _emergencyStopRequired = false;
        OnPropertyChanged(nameof(EmergencyStopRequired));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(CanManage));
    }

    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(null));

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(SessionId));
        OnPropertyChanged(nameof(LineageId));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ProviderId));
        OnPropertyChanged(nameof(ModelDisplayName));
        OnPropertyChanged(nameof(SessionDisplay));
    }

    private bool TryAdvance(
        long sequence,
        DateTimeOffset occurredUtc,
        bool updatesConversationActivity)
    {
        if (sequence <= _lastAppliedSequence)
            return false;
        _lastAppliedSequence = sequence;
        if (updatesConversationActivity)
        {
            _lastConversationActivitySequence = sequence;
            if (occurredUtc > _updatedUtc)
            {
                _updatedUtc = occurredUtc;
                OnPropertyChanged(nameof(UpdatedUtc));
            }
        }
        return true;
    }

    private static string Compact(string value)
        => value.Length <= 18
            ? value
            : value[..8] + "…" + value[^6..];

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class D046HermesSubagentCardViewModel : INotifyPropertyChanged
{
    private D046HermesSubagentCardProjection _projection;
    private string _steerText = string.Empty;
    private bool _controlsEnabled;
    private bool _interruptRequested;

    internal D046HermesSubagentCardViewModel(
        D046HermesSubagentCardProjection projection)
    {
        _projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
    }

    internal string SubagentId => _projection.SubagentId;
    internal bool IsActive => _projection.State == D046HermesSubagentState.Running;
    public string Goal => SafeDisplay(_projection.Goal, ControlCenterLocalization.Current.Get("state.no.task.objective.provided.15"));
    public string StatusText => _interruptRequested && IsActive
        ? ControlCenterLocalization.Current.Get("state.stopping.now.16")
        : StateText(_projection.State);
    public string DurationText => DurationTextFor(_projection);
    public string ToolsetsText => ToolsetsTextFor(_projection);
    public string SteerText
    {
        get => _steerText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_steerText, next, StringComparison.Ordinal))
                return;
            _steerText = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSteer));
        }
    }
    public bool CanSteer => _controlsEnabled
        && IsActive
        && !_interruptRequested
        && !string.IsNullOrWhiteSpace(SteerText);
    public bool CanEdit => _controlsEnabled
        && IsActive
        && !_interruptRequested;
    public bool CanInterrupt => _controlsEnabled
        && IsActive
        && !_interruptRequested;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Apply(D046HermesSubagentCardProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (!string.Equals(
                projection.SubagentId,
                _projection.SubagentId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Subagent identity changed.");
        }

        _projection = projection;
        if (!IsActive)
            _interruptRequested = false;
        RaiseCardProperties();
    }

    internal void SetControlsEnabled(bool value)
    {
        if (_controlsEnabled == value)
            return;
        _controlsEnabled = value;
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSteer));
        OnPropertyChanged(nameof(CanInterrupt));
    }

    internal void MarkSteerQueued()
    {
        SteerText = string.Empty;
        OnPropertyChanged(nameof(StatusText));
    }

    internal void MarkInterruptRequested()
    {
        _interruptRequested = true;
        RaiseCardProperties();
    }

    private void RaiseCardProperties()
    {
        OnPropertyChanged(nameof(Goal));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(ToolsetsText));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanSteer));
        OnPropertyChanged(nameof(CanInterrupt));
    }

    private static string StateText(D046HermesSubagentState state)
        => state switch
        {
            D046HermesSubagentState.Running => ControlCenterLocalization.Current.Get("state.in.progress.17"),
            D046HermesSubagentState.Completed => ControlCenterLocalization.Current.Get("state.completed.18"),
            D046HermesSubagentState.Failed => ControlCenterLocalization.Current.Get("state.failed.19"),
            D046HermesSubagentState.Interrupted => ControlCenterLocalization.Current.Get("state.stopped.20"),
            D046HermesSubagentState.TimedOut => ControlCenterLocalization.Current.Get("state.timed.out.21"),
            D046HermesSubagentState.Error => ControlCenterLocalization.Current.Get("state.error.22"),
            _ => ControlCenterLocalization.Current.Get("state.status.unknown.23")
        };

    private static string DurationTextFor(
        D046HermesSubagentCardProjection projection)
    {
        var seconds = projection.DurationSeconds;
        if (!seconds.HasValue && projection.StartedUtc.HasValue)
        {
            seconds = Math.Max(
                0,
                (DateTimeOffset.UtcNow - projection.StartedUtc.Value).TotalSeconds);
        }
        return seconds.HasValue
            ? Math.Max(0, seconds.Value).ToString("0.#") + ControlCenterLocalization.Current.Get("state.s.24")
            : ControlCenterLocalization.Current.Get("state.time.not.provided.25");
    }

    private static string ToolsetsTextFor(
        D046HermesSubagentCardProjection projection)
    {
        var normalized = (projection.Toolsets ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            return ControlCenterLocalization.Current.Get("state.no.tool.capabilities.26");
        return string.Join(" · ", normalized)
            + ControlCenterLocalization.Current.Get("state.tool.calls.27")
            + projection.ToolCount
            + ControlCenterLocalization.Current.Get("state..28");
    }

    private static string SafeDisplay(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180] + "…";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class D046HermesRollbackCheckpointViewModel
{
    internal D046HermesRollbackCheckpointViewModel(
        D046HermesRollbackCheckpoint checkpoint)
    {
        Source = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        var message = (checkpoint.Message ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (message.Length > 80)
            message = message[..80] + "…";
        DisplayText = checkpoint.Hash[..8]
            + " · "
            + checkpoint.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            + (message.Length == 0 ? string.Empty : " · " + message);
    }

    internal D046HermesRollbackCheckpoint Source { get; }
    public string Hash => Source.Hash;
    public string DisplayText { get; }
}

internal sealed class D046HermesNativeApprovalModeOption : INotifyPropertyChanged
{
    internal D046HermesNativeApprovalModeOption(
        D046HermesNativeApprovalMode value,
        string displayName)
    {
        Value = value;
    }

    internal D046HermesNativeApprovalMode Value { get; }
    public string DisplayName => ControlCenterLocalization.Current[Value switch
    {
        D046HermesNativeApprovalMode.Manual => "approval.manual",
        D046HermesNativeApprovalMode.Smart => "approval.smart",
        _ => "approval.off"
    }];
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
}

internal sealed class D046HermesNativeMcpViewModel : INotifyPropertyChanged
{
    private bool _controlsEnabled;
    private bool _desiredEnabled;
    internal HermesNativeMcpCatalogEntry Source { get; }
    internal D046HermesNativeMcpViewModel(HermesNativeMcpCatalogEntry source)
    {
        Source = source;
        _desiredEnabled = source.Enabled;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name => Source.Name;
    public string Description => Source.Description;
    public string Endpoint => Source.Transport == "http" ? Source.Url : ControlCenterLocalization.Current.Get("state.local.mcp.requires.separate.installation.29");
    public bool DesiredEnabled
    {
        get => _desiredEnabled;
        set
        {
            if (_desiredEnabled == value || !CanConfigure) return;
            _desiredEnabled = value;
            PropertyChanged?.Invoke(this, new(nameof(DesiredEnabled)));
            PropertyChanged?.Invoke(this, new(nameof(CanApply)));
        }
    }
    public bool CanConfigure => _controlsEnabled && Source.Transport == "http"
        && Source.SetupSupport == "catalog_registration"
        && (!Source.Registered || Source.ConfigMatchesCatalog);
    public bool CanApply => CanConfigure && (!Source.Registered || Source.Enabled != DesiredEnabled);
    public string StatusText => Source.SetupSupport != "catalog_registration"
        ? ControlCenterLocalization.Current.Get("state.separate.installation.required.this.screen.does.not.run.30")
        : Source.Registered && !Source.ConfigMatchesCatalog
            ? ControlCenterLocalization.Current.Get("state.custom.settings.differ.from.the.native.catalog.and.31")
            : (Source.Registered ? ControlCenterLocalization.Current.Get("state.registered.32") : ControlCenterLocalization.Current.Get("state.not.registered.33"))
                + " · " + (Source.Enabled ? ControlCenterLocalization.Current.Get("state.enabled.34") : ControlCenterLocalization.Current.Get("state.disabled.35"))
                + (Source.Auth == "oauth" ? ControlCenterLocalization.Current.Get("state.complete.oauth.sign.in.yourself.36") : ControlCenterLocalization.Current.Get("state.the.external.service.must.be.running.37"))
                + ControlCenterLocalization.Current.Get("state.registration.does.not.prove.connection.or.loaded.tools.38");
    internal void SetControlsEnabled(bool enabled)
    {
        _controlsEnabled = enabled;
        PropertyChanged?.Invoke(this, new(nameof(CanConfigure)));
        PropertyChanged?.Invoke(this, new(nameof(CanApply)));
    }
}

internal sealed class D046HermesNativeToolsetViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private D046HermesNativeToolsetOption _source;
    private bool _desiredEnabled;
    private bool _controlsEnabled;
    private bool _setupControlsEnabled;
    private bool _suppressChange;
    private bool _isDetailsExpanded;
    private HermesNativeSetupProvider? _selectedSetupProvider;
    private string? _selectedSetupModel;

    internal D046HermesNativeToolsetViewModel(
        D046HermesNativeToolsetOption source,
        Action changed)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _desiredEnabled = source.Enabled;
        _selectedSetupProvider = source.SetupProviders?.FirstOrDefault(p => p.Selected)
            ?? source.SetupProviders?.FirstOrDefault();
        ResetSetupModel();
    }

    internal string Name => _source.Name;
    public string Label => ControlCenterLocalization.Current.Get("tool." + Name + ".label", _source.Label);
    public string Description => ControlCenterLocalization.Current.Get("tool." + Name + ".description", _source.Description);
    public string NativeName => Name;
    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        set { if (_isDetailsExpanded == value) return; _isDetailsExpanded = value; OnPropertyChanged(); }
    }
    public bool DesiredEnabled
    {
        get => _desiredEnabled;
        set
        {
            if (_desiredEnabled == value)
                return;
            _desiredEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionText));
            if (!_suppressChange)
                _changed();
        }
    }
    public bool CanToggle => _controlsEnabled;
    public IReadOnlyList<HermesNativeSetupProvider> SetupProviders =>
        _source.SetupProviders ?? Array.Empty<HermesNativeSetupProvider>();
    public bool HasSetupProviders => SetupProviders.Count > 0;
    public HermesNativeSetupProvider? SelectedSetupProvider
    {
        get => _selectedSetupProvider;
        set
        {
            if (value == _selectedSetupProvider || (value is not null && !SetupProviders.Contains(value))) return;
            _selectedSetupProvider = value;
            ResetSetupModel();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SetupModels));
            OnPropertyChanged(nameof(HasSetupModels));
            OnPropertyChanged(nameof(SelectedSetupModel));
            OnPropertyChanged(nameof(CanSelectSetupProvider));
            OnPropertyChanged(nameof(SetupStatusText));
        }
    }
    public bool CanSelectSetupProvider => _controlsEnabled && _setupControlsEnabled
        && SelectedSetupProvider is { } provider
        && provider.SetupSupport is "provider_selection" or "model_selection"
        && (provider.Models is null ? !provider.Selected : SelectedSetupModel is not null
            && (!provider.Selected || SelectedSetupModel != provider.CurrentModel));
    public IReadOnlyList<string> SetupModels => SelectedSetupProvider?.Models ?? Array.Empty<string>();
    public bool HasSetupModels => SelectedSetupProvider?.Models is not null;
    public string? SelectedSetupModel
    {
        get => _selectedSetupModel;
        set
        {
            if (value == _selectedSetupModel || value is not null && !SetupModels.Contains(value, StringComparer.Ordinal)) return;
            _selectedSetupModel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectSetupProvider));
        }
    }
    private void ResetSetupModel() => _selectedSetupModel = SelectedSetupProvider?.CurrentModel is { } current
        && SetupModels.Contains(current, StringComparer.Ordinal) ? current : null;
    public string SetupStatusText => ControlCenterLocalization.Current[SelectedSetupProvider?.SetupSupport switch
    {
        "provider_selection" => "tools.providerChoiceHelp",
        "model_selection" => "tools.modelChoiceHelp",
        "secure_fields" or "spotify_oauth" or "no_setup_fields" or "dependency_setup" => "tools.setupHint",
        _ => "tools.providerUnsupported"
    }];
    public string SelectionText => DesiredEnabled
        ? ControlCenterLocalization.Current["tools.selected"]
        : ControlCenterLocalization.Current["tools.unselected"];
    public bool IsReady => _source.Readiness == D046HermesNativeToolsetReadiness.Ready;
    public string ReadinessText => _source.Readiness switch
    {
        D046HermesNativeToolsetReadiness.Ready => ControlCenterLocalization.Current["tools.ready"],
        D046HermesNativeToolsetReadiness.NeedsAuthentication => ControlCenterLocalization.Current["tools.needsAuth"],
        D046HermesNativeToolsetReadiness.NeedsKeys => ControlCenterLocalization.Current["tools.needsKeys"],
        _ => ControlCenterLocalization.Current["tools.needsSetup"]
    };
    public string ToolsText => _source.Tools.Count == 0
        ? ControlCenterLocalization.Current["tools.noTools"]
        : string.Join(" · ", _source.Tools);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetControlsEnabled(bool value, bool setupAllowed = true)
    {
        if (_controlsEnabled == value && _setupControlsEnabled == setupAllowed)
            return;
        _controlsEnabled = value;
        _setupControlsEnabled = setupAllowed;
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanSelectSetupProvider));
    }

    internal void ResetDesired()
    {
        _suppressChange = true;
        try
        {
            DesiredEnabled = _source.Enabled;
        }
        finally
        {
            _suppressChange = false;
        }
    }

    internal void RefreshLanguage() => OnPropertyChanged(null);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class D046HermesClarificationChoiceViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    internal D046HermesClarificationChoiceViewModel(string text) => Text = text;

    public string Text { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed partial class D046HermesWorkbenchViewModel
    : INotifyPropertyChanged,
      IDisposable
{
    private readonly ID046HermesWorkbenchProjection _projection;
    private readonly ID052HermesTaskPinProjection? _taskPinProjection;
    private readonly D046HermesSnapshotHandoffCallbacks _snapshotHandoff;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<D046HermesProjectionEvent> _receivedEvents = new();
    private readonly D052HermesTurnPresentationCollection _turnPresentation = new();
    private readonly Dictionary<string, D046HermesSubagentCardViewModel>
        _subagentsById = new(StringComparer.Ordinal);
    private D046HermesTaskItemViewModel? _selectedTask;
    private D046HermesModelOption? _selectedModelOption;
    private string _activeTaskId = string.Empty;
    private D046HermesSessionStatusCorrelation? _submitStatusScope;
    private D046HermesTurnStatusCorrelation? _completedTurnAwaitingIdle;
    private D046HermesTurnStatusCorrelation? _settledTurnAwaitingReceipt;
    private bool _submitStatusReceiptPending;
    private bool _turnReadyStatusProtected;
    private string _modelCatalogTaskId = string.Empty;
    private string _modelCatalogSessionId = string.Empty;
    private string _runtimeGeneration = string.Empty;
    private string _draftText = string.Empty;
    private readonly D046HermesFaultRecoveryDraftSlot _faultRecoveryDrafts;
    private D046HermesPendingImage? _pendingImage;
    private string _statusText = ControlCenterLocalization.Current.Get("state.not.connected.39");
    // Preserve the result of the player's explicit selection over the generic
    // missing-key hint, but only while it is still the current status.
    private string? _modelSelectionResultStatus;
    private string _compressionStatusText = ControlCenterLocalization.Current.Get("state.no.native.context.compression.yet.40");
    private string _observabilityStatusText = ControlCenterLocalization.Current.Get("state.native.runtime.observations.not.loaded.yet.41");
    private string _usageStatusText = ControlCenterLocalization.Current.Get("state.total.usage.not.loaded.42");
    private string _contextStatusText = ControlCenterLocalization.Current.Get("state.context.not.loaded.43");
    private string _compressionCountStatusText = ControlCenterLocalization.Current.Get("state.compression.not.loaded.44");
    private string _learningStatusText = ControlCenterLocalization.Current.Get("state.learning.records.not.loaded.45");
    private string _delegationStatusText = ControlCenterLocalization.Current.Get("state.delegated.task.status.not.loaded.yet.46");
    private long _lastObservedSequence;
    private bool _isConnected;
    private bool _nativeSurfaceAttested;
    private bool _credentialRecoveryAvailable;
    private bool _activeSessionConfigStale;
    private bool _computerUseEnabled;
    private string _pendingApprovalSummary = string.Empty;
    private string _approvalStatusText = ControlCenterLocalization.Current.Get("state.no.pending.task.approval.47");
    private readonly Queue<D046HermesPendingApprovalProjection> _pendingApprovals = new();
    private bool _approvalAttentionRequired;
    private bool _approvalResponseInFlight;
    private D046HermesPendingClarificationProjection? _pendingClarification;
    private bool _clarificationResponseInFlight;
    private D046HermesPendingSecureInputProjection? _pendingSecureInput;
    private bool _secureInputResponseInFlight;
    private bool _commandInFlight;
    private bool _stopInFlight;
    private bool _sequenceGapDetected;
    private bool _projectionFault;
    private string? _transportFaultStatus;
    private bool _confirmedDeletionNeedsRecovery;
    private bool _isApplyingSnapshot;
    private bool _isApplyingModelSelection;
    private bool _taskPinInFlight;
    private bool _modelCatalogReady;
    private bool _preSessionModelSelectionRequired;
    private string _nativeToolsCatalogTaskId = string.Empty;
    private string _nativeToolsCatalogSessionId = string.Empty;
    private string _nativeToolsStatusText = ControlCenterLocalization.Current.Get("state.tools.and.approvals.not.loaded.yet.48");
    private string _nativeToolsActivationText = ControlCenterLocalization.Current.Get("state.settings.apply.only.to.new.sessions.49");
    private D046HermesNativeApprovalModeOption? _selectedNativeApprovalMode;
    private D046HermesNativeApprovalMode _savedNativeApprovalMode =
        D046HermesNativeApprovalMode.Manual;
    private bool _isApplyingNativeToolsCatalog;
    private bool _nativeToolsCatalogReady;
    private bool _nativeToolsDirty;
    private bool _nativeToolsInFlight;
    private bool _observabilityInFlight;
    private bool _delegationStatusInFlight;
    private bool _delegationControlInFlight;
    private bool _delegationUnavailable;
    private bool _delegationControlQuarantined;
    private D046HermesVoiceCaptureState _voiceCaptureState =
        D046HermesVoiceCaptureState.Off;
    private string _voiceStatusOverride = string.Empty;
    private string _voiceScopeTaskId = string.Empty;
    private string _voiceScopeSessionId = string.Empty;
    private long _lastVoiceRuntimeSequence;
    private bool _voiceLifecycleInFlight;
    private bool _voiceStartInFlight;
    private bool _voiceStopInFlight;
    private bool _voiceStopRequested;
    private bool _voiceShuttingDown;
    private D046HermesRollbackCheckpointViewModel? _selectedRollbackCheckpoint;
    private string _rollbackStatusText = ControlCenterLocalization.Current.Get("state.workspace.checkpoints.not.loaded.yet.50");
    private string _rollbackDiffText = string.Empty;
    private string _rollbackConfirmationToken = string.Empty;
    private string _rollbackDiffHash = string.Empty;
    private string _rollbackCatalogGeneration = string.Empty;
    private string _rollbackCatalogTaskId = string.Empty;
    private string _rollbackCatalogSessionId = string.Empty;
    private bool _rollbackCatalogEnabled;
    private bool _rollbackListInFlight;
    private bool _rollbackDiffInFlight;
    private bool _rollbackRestoreInFlight;
    private bool _rollbackRestoreQuarantined;
    private int _timelineMutationDepth;
    private int _taskRailMutationDepth;

    internal D046HermesWorkbenchViewModel(
        ID046HermesWorkbenchProjection projection,
        D046HermesSnapshotHandoffCallbacks snapshotHandoff,
        D046HermesFaultRecoveryDraft? faultRecoveryDraft = null)
    {
        _projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        _taskPinProjection = projection as ID052HermesTaskPinProjection;
        _snapshotHandoff = snapshotHandoff
            ?? throw new ArgumentNullException(nameof(snapshotHandoff));
        ArgumentNullException.ThrowIfNull(_snapshotHandoff.Begin);
        ArgumentNullException.ThrowIfNull(_snapshotHandoff.Commit);
        ArgumentNullException.ThrowIfNull(_snapshotHandoff.Abort);
        ArgumentNullException.ThrowIfNull(_snapshotHandoff.Fail);
        _faultRecoveryDrafts = new D046HermesFaultRecoveryDraftSlot(
            faultRecoveryDraft);
        ControlCenterLocalization.Current.LanguageChanged += RefreshLanguage;
    }

    public ObservableCollection<D046HermesTaskItemViewModel> Tasks { get; } = new();
    public ObservableCollection<D052HermesTurnPresentationViewModel> Turns
        => _turnPresentation.Turns;
    public ObservableCollection<D046HermesModelOption> ModelOptions { get; } = new();
    public ObservableCollection<D046HermesNativeToolsetViewModel> NativeToolsets { get; }
        = new();
    public ObservableCollection<D046HermesNativeMcpViewModel> NativeMcpCatalog { get; } = new();
    public bool HasNativeMcpCatalog => NativeMcpCatalog.Count > 0;
    public ObservableCollection<D046HermesNativeApprovalModeOption>
        NativeApprovalModes { get; } = new()
        {
            new(D046HermesNativeApprovalMode.Manual, ControlCenterLocalization.Current.Get("state.ask.for.approval.manual.51")),
            new(D046HermesNativeApprovalMode.Smart, ControlCenterLocalization.Current.Get("state.approve.for.me.smart.52")),
            new(D046HermesNativeApprovalMode.Off, ControlCenterLocalization.Current.Get("state.full.access.off.53"))
        };
    public ObservableCollection<string> LearningItems { get; } = new();
    public ObservableCollection<D046HermesSubagentCardViewModel> Subagents { get; } = new();
    public ObservableCollection<D046HermesClarificationChoiceViewModel>
        ClarificationChoices { get; } = new();
    public ObservableCollection<D046HermesRollbackCheckpointViewModel>
        RollbackCheckpoints { get; } = new();
    internal IReadOnlyList<D046HermesProjectionEvent> ReceivedEvents => _receivedEvents;
    internal string ActiveTaskId => _activeTaskId;
    internal bool IsApplyingSnapshot => _isApplyingSnapshot;
    internal bool IsApplyingModelSelection => _isApplyingModelSelection;
    internal bool HasProjectionFault => _projectionFault;
    internal string ProjectionDiagnosticPhase => _isApplyingSnapshot ? "snapshot_apply"
        : _clarificationResponseInFlight ? "clarification"
        : _commandInFlight ? "command" : HasRunningConversation ? "turn" : "idle";
    internal bool RequiresPreSessionModelSelection =>
        _preSessionModelSelectionRequired;

    public D046HermesTaskItemViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (ReferenceEquals(_selectedTask, value))
                return;
            if (!_isApplyingSnapshot
                && !CanSwitchTask
                && !string.Equals(
                    value?.TaskId,
                    _activeTaskId,
                    StringComparison.Ordinal))
            {
                // A disabled task rail can still surface a stale keyboard
                // selection notification. Re-publish the authoritative item
                // so the target cannot diverge from the active task.
                OnPropertyChanged();
                return;
            }
            var previousTaskId = _selectedTask?.TaskId ?? string.Empty;
            var previousSessionId = _selectedTask?.SessionId ?? string.Empty;
            var conversationScopeChanging = !string.Equals(
                    previousTaskId,
                    value?.TaskId ?? string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousSessionId,
                    value?.SessionId ?? string.Empty,
                    StringComparison.Ordinal);
            if (conversationScopeChanging)
                ConversationScopeChanging?.Invoke(this, EventArgs.Empty);
            _selectedTask = value;
            if (conversationScopeChanging)
            {
                ResetTurnSettlementStatusCorrelation();
            }
            if (!string.Equals(previousTaskId, value?.TaskId, StringComparison.Ordinal))
            {
                ResetObservabilityDisplay();
                ResetRollbackDisplay();
                ClearNativeToolsCatalog();
            }
            OnPropertyChanged();
            SynchronizeModelSelectionFromSessionInfo();
            RaiseSelectedTaskProperties();
        }
    }

    public D046HermesModelOption? SelectedModelOption
    {
        get => _selectedModelOption;
        set
        {
            if (ReferenceEquals(_selectedModelOption, value))
                return;
            _selectedModelOption = value;
            OnPropertyChanged();
        }
    }

    public D046HermesNativeApprovalModeOption? SelectedNativeApprovalMode
    {
        get => _selectedNativeApprovalMode;
        set
        {
            if (ReferenceEquals(_selectedNativeApprovalMode, value))
                return;
            _selectedNativeApprovalMode = value;
            OnPropertyChanged();
            if (!_isApplyingNativeToolsCatalog)
                MarkNativeToolsDirty();
        }
    }

    public D046HermesRollbackCheckpointViewModel? SelectedRollbackCheckpoint
    {
        get => _selectedRollbackCheckpoint;
        set
        {
            if (ReferenceEquals(_selectedRollbackCheckpoint, value))
                return;
            _selectedRollbackCheckpoint = value;
            ClearRollbackDiff();
            OnPropertyChanged();
            RaiseRollbackGateProperties();
        }
    }

    public string DraftText
    {
        get => _draftText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_draftText, next, StringComparison.Ordinal))
                return;
            _draftText = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSend));
        }
    }

    public string SelectedTaskTitle => SelectedTask?.Title ?? ControlCenterLocalization.Current.Get("state.lilith.session.54");
    public string ModelDisplayName => SelectedTask?.ModelDisplayName
        ?? (_preSessionModelSelectionRequired ? ControlCenterLocalization.Current.Get("state.choose.a.model.first.55") : ControlCenterLocalization.Current.Get("state.model.not.reported.1"));
    public string SessionDisplay => SelectedTask?.SessionDisplay ?? ControlCenterLocalization.Current.Get("state.no.session.selected.56");
    public string RuntimeGenerationDisplay => string.IsNullOrWhiteSpace(_runtimeGeneration)
        ? ControlCenterLocalization.Current.Get("state.not.reported.57")
        : Compact(_runtimeGeneration);
    public string StatusText => _confirmedDeletionNeedsRecovery
        ? ControlCenterLocalization.Current["chat.delete.confirmedRecovery"]
        : _transportFaultStatus ?? (!_commandInFlight && !_projectionFault
            && !_sequenceGapDetected && _isConnected && CanRecoverModelSelection
                && !string.Equals(_statusText, _modelSelectionResultStatus, StringComparison.Ordinal)
                ? ControlCenterLocalization.Current["chat.credentialRecovery"] : _statusText);
    public string CompressionStatusText => _compressionStatusText;
    public string ObservabilityStatusText => _observabilityStatusText;
    public string UsageStatusText => _usageStatusText;
    public string ContextStatusText => _contextStatusText;
    public string CompressionCountStatusText => _compressionCountStatusText;
    public string LearningStatusText => _learningStatusText;
    public string DelegationStatusText => _delegationStatusText;
    public string RollbackStatusText => _rollbackStatusText;
    public string RollbackDiffText => _rollbackDiffText;
    public string VoiceStatusText => string.IsNullOrWhiteSpace(_voiceStatusOverride)
        ? _voiceCaptureState switch
        {
            D046HermesVoiceCaptureState.Idle => ControlCenterLocalization.Current.Get("state.voice.ready.58"),
            D046HermesVoiceCaptureState.Listening => ControlCenterLocalization.Current.Get("state.listening.59"),
            D046HermesVoiceCaptureState.Transcribing => ControlCenterLocalization.Current.Get("state.transcribing.60"),
            D046HermesVoiceCaptureState.Quarantined => ControlCenterLocalization.Current.Get("state.voice.currently.unavailable.61"),
            _ => ControlCenterLocalization.Current.Get("state.voice.off.62")
        }
        : _voiceStatusOverride;
    public bool IsVoiceCaptureBusy => _voiceCaptureState is
        D046HermesVoiceCaptureState.Listening
        or D046HermesVoiceCaptureState.Transcribing;
    internal bool NativeVoiceOwnsSendCue => _voiceCaptureState != D046HermesVoiceCaptureState.Off;
    public bool IsDelegationEmpty => Subagents.Count == 0;
    public bool HasActiveSubagents => Subagents.Any(card => card.IsActive);
    public D046HermesPendingImage? PendingImage => _pendingImage;
    public bool HasPendingImage => _pendingImage is not null;
    public bool IsFaultRecoveryDraftPending => _faultRecoveryDrafts.HasPending;
    public bool IsFaultRecoveryDraftBlockingCurrentTask =>
        _faultRecoveryDrafts.IsBoundTo(
            SelectedTask is not null
                && string.Equals(
                    SelectedTask.TaskId,
                    _activeTaskId,
                    StringComparison.Ordinal)
                    ? SelectedTask.TaskId
                    : null,
            SelectedTask?.LineageId);
    public bool CanDiscardFaultRecoveryDraft => IsFaultRecoveryDraftPending
        && !_stopInFlight
        && !_commandInFlight
        && !IsRollbackOperationInFlight;
    public bool IsRollbackOperationInFlight => _rollbackListInFlight
        || _rollbackDiffInFlight
        || _rollbackRestoreInFlight;
    public bool IsTimelineEmpty => Turns.Count == 0;
    public bool IsTimelineScopeCurrent => SelectedTask is not null
        && string.Equals(
            SelectedTask.TaskId,
            _activeTaskId,
            StringComparison.Ordinal)
        && _turnPresentation.ScopeMatches(
            _runtimeGeneration,
            SelectedTask.TaskId,
            SelectedTask.SessionId);
    public bool IsTimelinePlaceholderVisible => !IsTimelineScopeCurrent
        || IsTimelineEmpty;
    public string TimelinePlaceholderText => SelectedTask is null
        ? ControlCenterLocalization.Current.Get("state.select.a.lilith.session.to.view.the.conversation.63")
        : !IsTimelineScopeCurrent
            ? ControlCenterLocalization.Current.Get("state.loading.this.session.64")
            : ControlCenterLocalization.Current.Get("state.no.conversation.history.in.this.session.yet.65");
    public string EventCountText => _receivedEvents.Count + ControlCenterLocalization.Current.Get("state.events.in.memory.projection.only.66");
    public string EffectToolsStatusText => _activeSessionConfigStale
        ? "工具與授權設定已變更。這段聊天已改為唯讀，紀錄仍保留；請建立新聊天。"
        : _nativeSurfaceAttested
            ? "查看已選取的工具，以及各項工具是否就緒。"
            : ControlCenterLocalization.Current.Get("state.tool.authority.is.not.attested.tool.settings.and.67");
    public string NativeToolsStatusText => _nativeToolsStatusText;
    public string NativeToolsActivationText => _nativeToolsActivationText;
    public bool HasNativeToolsAttention => _activeSessionConfigStale
        || (_nativeToolsCatalogReady
            && NativeToolsets.Any(row => row.DesiredEnabled && !row.IsReady));
    public bool IsNativeToolsEmpty => NativeToolsets.Count == 0;
    public bool CanRefreshNativeTools => (CanUseNativeToolsScope()
            || CanUseConfigStaleNativeToolsReadScope())
        && !_nativeToolsInFlight;
    public bool CanEditNativeTools => CanUseNativeToolsScope()
        && !_nativeToolsInFlight;
    public bool CanApplyNativeTools => CanEditNativeTools
        && _nativeToolsCatalogReady
        && _nativeToolsDirty
        && SelectedNativeApprovalMode is not null;
    public bool CanCancelNativeTools => CanEditNativeTools
        && _nativeToolsCatalogReady
        && _nativeToolsDirty
        && !_nativeToolsInFlight;
    public string ApprovalSummaryText => string.IsNullOrWhiteSpace(_pendingApprovalSummary)
        ? ControlCenterLocalization.Current.Get("state.when.lilith.proposes.a.local.operation.it.waits.68")
        : _pendingApprovalSummary;
    public string ApprovalStatusText => _approvalStatusText;
    public bool HasPendingApproval => _pendingApprovals.Count > 0;
    public bool HasApprovalAttention => HasPendingApproval || _approvalAttentionRequired;
    public bool HasApprovalAllowOnce => PendingApprovalOffers("once");
    public bool HasApprovalAllowSession => PendingApprovalOffers("session");
    public bool HasApprovalAllowAlways => PendingApprovalOffers("always");
    public bool HasApprovalDeny => PendingApprovalOffers("deny");
    public string ApprovalDescriptionText => _pendingApprovals.Count == 0
        ? string.Empty
        : _pendingApprovals.Peek().Description;
    public string ApprovalCommandText => _pendingApprovals.Count == 0
        ? string.Empty
        : _pendingApprovals.Peek().Command;
    public bool HasApprovalDescription => !string.IsNullOrWhiteSpace(ApprovalDescriptionText);
    public bool HasApprovalCommand => !string.IsNullOrWhiteSpace(ApprovalCommandText);
    public string ApprovalScopeWarningText
    {
        get
        {
            if (_pendingApprovals.Count == 0)
                return string.Empty;
            var pending = _pendingApprovals.Peek();
            var allowsSession = pending.AllowSession
                || pending.Choices.Contains("session", StringComparer.Ordinal);
            var allowsPermanent = pending.AllowPermanent
                || pending.Choices.Contains("always", StringComparer.Ordinal);
            return (allowsSession, allowsPermanent) switch
            {
                (true, true)
                    => "本次聊天的授權持續到聊天結束；永遠允許會保存授權，並套用到新聊天。",
                (false, true)
                    => ControlCenterLocalization.Current.Get("state.always.allow.changes.native.hermes.settings.for.new.69"),
                (true, false)
                    => ControlCenterLocalization.Current.Get("state.session.approval.lasts.until.this.chat.session.ends.70"),
                _ => string.Empty
            };
        }
    }
    public bool HasApprovalScopeWarning => !string.IsNullOrWhiteSpace(ApprovalScopeWarningText);
    public bool HasPendingClarification => _pendingClarification is not null;
    public string ClarificationPrompt => _pendingClarification?.Prompt ?? string.Empty;
    public bool IsClarificationMultiSelect => _pendingClarification?.MultiSelect == true;
    public bool HasClarificationChoices => ClarificationChoices.Count > 0;
    public bool IsClarificationSingleSelect => HasClarificationChoices
        && !IsClarificationMultiSelect;
    public bool CanRespondClarification => !_stopInFlight
        && !_commandInFlight
        && !_clarificationResponseInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && HasPendingClarification
        && string.Equals(SelectedTask?.TaskId, _activeTaskId, StringComparison.Ordinal)
        && SelectedTask?.State == D046HermesTaskState.WaitingForClarification;
    public bool CanSubmitClarificationSelection => CanRespondClarification
        && IsClarificationMultiSelect
        && ClarificationChoices.Any(choice => choice.IsSelected);
    public bool HasPendingSecureInput => _pendingSecureInput is not null;
    internal (string RequestId, D046HermesSecureInputKind InputKind)? SecureInputIdentity
        => _pendingSecureInput is { } pending ? (pending.RequestId, pending.InputKind) : null;
    public string SecureInputTitle => _pendingSecureInput?.InputKind
        == D046HermesSecureInputKind.SudoPassword
            ? ControlCenterLocalization.Current.Get("state.administrator.password.71")
            : ControlCenterLocalization.Current.Get("state.secure.input.72");
    public string SecureInputPrompt => _pendingSecureInput?.Prompt ?? string.Empty;
    public string SecureInputEnvironmentVariable => _pendingSecureInput?.EnvironmentVariable ?? string.Empty;
    public bool HasSecureInputEnvironmentVariable =>
        !string.IsNullOrWhiteSpace(SecureInputEnvironmentVariable);
    public bool CanRespondSecureInput => !_stopInFlight
        && !_commandInFlight
        && !_secureInputResponseInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && HasPendingSecureInput
        && string.Equals(SelectedTask?.TaskId, _activeTaskId, StringComparison.Ordinal)
        && SelectedTask?.State == D046HermesTaskState.WaitingForSecureInput;
    public bool CanRespondApproval => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _isConnected
        && (_nativeSurfaceAttested || _activeSessionConfigStale)
        && !_sequenceGapDetected
        && !_projectionFault
        && HasPendingApproval
        && SelectedTask is not null
        && SelectedTask.State == D046HermesTaskState.WaitingForApproval
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);
    public bool CanCreateTask => !HasRunningConversation && !_stopInFlight
        && !_commandInFlight
        && !_clarificationResponseInFlight
        && !_secureInputResponseInFlight
        && !_taskPinInFlight
        && !_nativeToolsInFlight
        && !IsRollbackOperationInFlight
        && !_delegationStatusInFlight
        && !_delegationControlInFlight
        && !HasPendingApproval
        && !HasPendingClarification
        && !HasPendingSecureInput
        && !HasPendingImage
        && !HasActiveSubagents
        && !IsVoiceCaptureBusy;
    public bool HasRunningConversation => Tasks.Any(task => task.TaskId == _activeTaskId
        && (task.EmergencyStopRequired || task.State is not (D046HermesTaskState.Idle or D046HermesTaskState.Failed)));
    public bool CanOpenTaskMenu => !_commandInFlight && !_stopInFlight;
    public bool CanSwitchTask => !HasRunningConversation && !_stopInFlight
        && !_commandInFlight
        && !_clarificationResponseInFlight
        && !_secureInputResponseInFlight
        && !_taskPinInFlight
        && !_nativeToolsInFlight
        && !IsRollbackOperationInFlight
        && !_delegationStatusInFlight
        && !_delegationControlInFlight
        && !HasPendingApproval
        && !HasPendingClarification
        && !HasPendingSecureInput
        && !HasPendingImage
        && !HasActiveSubagents
        && !IsVoiceCaptureBusy;
    public bool CanAddImage => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && !IsFaultRecoveryDraftBlockingCurrentTask
        && !IsVoiceCaptureBusy
        && !HasPendingApproval
        && !HasPendingClarification
        && !HasPendingSecureInput
        && SelectedTask is not null
        && SelectedTask.State == D046HermesTaskState.Idle
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);
    public bool CanRemoveImage => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        // Removing an unsent local attachment needs no native authority.
        // A settled fault must still let the user explicitly remove it before
        // reconnecting; recovery itself never discards attachment bytes.
        && (_nativeSurfaceAttested || CanReconnect)
        && !IsVoiceCaptureBusy
        && HasPendingImage;
    public bool CanSend => !_stopInFlight
        && !_commandInFlight
        && !_clarificationResponseInFlight
        && !_secureInputResponseInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && !IsFaultRecoveryDraftBlockingCurrentTask
        && !IsVoiceCaptureBusy
        && SelectedTask is not null
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && ((SelectedTask.State == D046HermesTaskState.Idle
                && !HasPendingApproval
                && !HasPendingClarification
                && !HasPendingSecureInput)
            || (SelectedTask.State == D046HermesTaskState.WaitingForClarification
                && HasPendingClarification
                && !HasPendingApproval
                && !HasPendingSecureInput))
        && !string.IsNullOrWhiteSpace(SelectedTask.ModelDisplayName)
        && (!string.IsNullOrWhiteSpace(DraftText)
            || (!HasPendingClarification && _pendingImage is { } image
                && image.IsOwnedBy(_runtimeGeneration, SelectedTask.TaskId, SelectedTask.SessionId)));
    public bool CanStop => !_stopInFlight
        && _isConnected
        && SelectedTask is not null
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedTask.SessionId)
        && (SelectedTask.EmergencyStopRequired
            || SelectedTask.State is D046HermesTaskState.Streaming
                or D046HermesTaskState.WaitingForApproval
                or D046HermesTaskState.WaitingForClarification
                or D046HermesTaskState.WaitingForSecureInput
                or D046HermesTaskState.Stopping);
    public bool CanCompress => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && !IsVoiceCaptureBusy
        && SelectedTask is not null
        && SelectedTask.State == D046HermesTaskState.Idle
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal);
    public bool CanSelectModel => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _modelCatalogReady
        && _isConnected
        && !_sequenceGapDetected
        && !_projectionFault
        && !IsVoiceCaptureBusy
        && ModelOptions.Count > 0
        && (_preSessionModelSelectionRequired
            ? SelectedTask is null
                && _modelCatalogTaskId.Length == 0
                && _modelCatalogSessionId.Length == 0
            : (_nativeSurfaceAttested || CanRecoverModelSelection)
                && SelectedTask is not null
                && SelectedTask.State is D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed
                    or D046HermesTaskState.Streaming
                && string.Equals(
                    SelectedTask.TaskId,
                    _activeTaskId,
                    StringComparison.Ordinal)
                && string.Equals(
                    SelectedTask.TaskId,
                    _modelCatalogTaskId,
                    StringComparison.Ordinal)
                && string.Equals(
                    SelectedTask.SessionId,
                    _modelCatalogSessionId,
                    StringComparison.Ordinal));
    private bool CanRecoverModelSelection => _credentialRecoveryAvailable
        && !_activeSessionConfigStale && !_rollbackRestoreQuarantined
        && !_delegationControlQuarantined && !HasPendingApproval
        && !HasPendingClarification && !HasPendingSecureInput && !HasPendingImage
        && !HasActiveSubagents && _voiceCaptureState is D046HermesVoiceCaptureState.Off
            or D046HermesVoiceCaptureState.Idle
        && SelectedTask?.State == D046HermesTaskState.Failed;
    public bool CanStartVoiceCapture => !_stopInFlight
        && !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && !_voiceLifecycleInFlight
        && !_voiceStartInFlight
        && !_voiceStopInFlight
        && _voiceCaptureState == D046HermesVoiceCaptureState.Idle
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && !IsFaultRecoveryDraftBlockingCurrentTask
        && !HasPendingApproval
        && !HasPendingClarification
        && !HasPendingSecureInput
        && SelectedTask is not null
        && SelectedTask.State == D046HermesTaskState.Idle
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);
    public bool CanUsePushToTalk => CanStartVoiceCapture || IsVoiceCaptureBusy;
    public bool CanRefreshObservability => !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_observabilityInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && SelectedTask is not null
        && SelectedTask.State is D046HermesTaskState.Idle
            or D046HermesTaskState.Failed
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal);
    public bool CanRefreshDelegation => !_commandInFlight
        && !_taskPinInFlight
        && !IsRollbackOperationInFlight
        && !_delegationStatusInFlight
        && !_delegationControlInFlight
        && _isConnected
        && _nativeSurfaceAttested
        && !_sequenceGapDetected
        && !_projectionFault
        && SelectedTask is not null
        && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);
    public bool CanRefreshRollback => CanUseRollbackScope()
        && !IsRollbackOperationInFlight;
    public bool CanSelectRollbackCheckpoint => CanRefreshRollback
        && _rollbackCatalogEnabled
        && RollbackCheckpoints.Count > 0;
    public bool CanGetRollbackDiff => CanRefreshRollback
        && _rollbackCatalogEnabled
        && SelectedRollbackCheckpoint is not null
        && RollbackCatalogScopeMatchesCurrent();
    public bool CanRestoreRollback => CanGetRollbackDiff
        && !_rollbackRestoreQuarantined
        && !string.IsNullOrWhiteSpace(_rollbackConfirmationToken)
        && string.Equals(
            _rollbackDiffHash,
            SelectedRollbackCheckpoint?.Hash,
            StringComparison.Ordinal);

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? ConversationScopeChanging;
    internal event EventHandler? TimelineChanging;
    internal event EventHandler? TimelineChanged;
    internal event EventHandler? TaskRailChanging;
    internal event EventHandler? TaskRailChanged;

    internal async Task InitializeAsync()
    {
        await RunSnapshotCommandAsync(
            () => _commandInFlight && !_lifetime.IsCancellationRequested,
            () => _projection.GetInitialSnapshotAsync(_lifetime.Token),
            ControlCenterLocalization.Current.Get("state.connecting.to.lilith.runtime.73"));
    }

    internal async Task CreateTaskAsync()
    {
        _ = await CreateTaskWithSelectionAsync(null);
    }

    internal Task<bool> CreateTaskFromPreSessionSelectionAsync(
        D046HermesModelOption selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!_preSessionModelSelectionRequired || !CanSelectModel)
            return Task.FromResult(false);
        return CreateTaskWithSelectionAsync(selection);
    }

    private async Task<bool> CreateTaskWithSelectionAsync(
        D046HermesModelOption? selection)
    {
        if (!CanCreateTask)
            return false;
        return await RunSnapshotCommandAsync(
            CanCreateOrSwitchTaskDuringSnapshotCommand,
            () => selection is null
                ? _projection.CreateTaskAsync(_lifetime.Token)
                : _projection.CreateTaskAsync(selection, _lifetime.Token),
            ControlCenterLocalization.Current.Get("state.creating.a.lilith.session.74"));
    }

    internal async Task<bool> OpenTaskAsync(string taskId)
    {
        if (HasRunningConversation && taskId != _activeTaskId)
        {
            _statusText = ControlCenterLocalization.Current["chat.switchBusy"];
            OnPropertyChanged(nameof(StatusText));
        }
        if (!CanSwitchTask
            || string.IsNullOrWhiteSpace(taskId)
            || string.Equals(taskId, _activeTaskId, StringComparison.Ordinal))
        {
            return string.Equals(taskId, _activeTaskId, StringComparison.Ordinal);
        }

        var authoritativeSelection = Tasks.FirstOrDefault(task => string.Equals(
            task.TaskId,
            _activeTaskId,
            StringComparison.Ordinal));
        var opened = await RunSnapshotCommandAsync(
            () => CanCreateOrSwitchTaskDuringSnapshotCommand()
                && !string.Equals(taskId, _activeTaskId, StringComparison.Ordinal),
            () => _projection.OpenTaskAsync(taskId, _lifetime.Token),
            ControlCenterLocalization.Current.Get("state.switching.lilith.sessions.75"));
        if (!opened)
        {
            _isApplyingSnapshot = true;
            try
            {
                SelectedTask = authoritativeSelection;
            }
            finally
            {
                _isApplyingSnapshot = false;
            }
        }
        return opened;
    }

    internal async Task<bool> SetTaskPinnedAsync(
        D046HermesTaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_taskPinProjection is null
            || _taskPinInFlight
            || _stopInFlight
            || _commandInFlight
            || IsRollbackOperationInFlight
            || _projectionFault
            || !Tasks.Contains(task)
            || !task.CanManage)
        {
            return false;
        }

        var request = new D052HermesTaskPinRequest(
            task.TaskId,
            task.IsPinned,
            !task.IsPinned);
        _taskPinInFlight = true;
        RaiseProjectionProperties();
        try
        {
            var receipt = await ExecuteSnapshotHandoffAsync(
                () => !_lifetime.IsCancellationRequested
                    && !_projectionFault
                    && Tasks.Any(candidate => string.Equals(
                        candidate.TaskId,
                        request.TaskId,
                        StringComparison.Ordinal)
                        && candidate.IsPinned == request.ExpectedIsPinned),
                () => _taskPinProjection.SetTaskPinAsync(
                    request,
                    _lifetime.Token),
                candidate => string.Equals(
                        candidate.TaskId,
                        request.TaskId,
                        StringComparison.Ordinal)
                    && candidate.IsPinned == request.IsPinned
                        ? candidate.Snapshot
                        : null,
                ApplyTaskPinSnapshot);
            if (receipt is null)
                return false;
            _statusText = receipt.IsPinned
                ? ControlCenterLocalization.Current.Get("state.chat.pinned.76")
                : ControlCenterLocalization.Current.Get("state.unpinned.chat.restored.to.its.conversation.activity.order.77");
            OnPropertyChanged(nameof(StatusText));
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            _statusText = ControlCenterLocalization.Current.Get("state.pin.preference.not.saved.chat.order.is.unchanged.78");
            OnPropertyChanged(nameof(StatusText));
            return false;
        }
        finally
        {
            _taskPinInFlight = false;
            RaiseProjectionProperties();
        }
    }

    internal async Task<D046HermesRenameTaskReceipt?> RenameTaskAsync(
        string taskId,
        string title)
    {
        if (taskId != _activeTaskId && SelectedTask is not null)
            return (await ManageOtherTaskAsync(taskId, title))?.Rename;
        if (!await ActivateTaskForMutationAsync(taskId))
            return null;

        var task = SelectedTask!;
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length == 0 || normalizedTitle.Any(char.IsControl))
        {
            _statusText = ControlCenterLocalization.Current.Get("state.a.chat.name.must.not.be.empty.or.79");
            OnPropertyChanged(nameof(StatusText));
            return null;
        }

        var request = new D046HermesRenameTaskRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            task.TaskId,
            task.SessionId,
            _lastObservedSequence,
            normalizedTitle);
        SetCommandState(true, ControlCenterLocalization.Current.Get("state.renaming.chat.80"));
        try
        {
            var receipt = await ExecuteSnapshotHandoffAsync(
                () => TaskMutationDispatchScopeStillValid(
                    request.RuntimeGeneration,
                    request.TaskId,
                    request.SessionId,
                    request.LastObservedSequence),
                () => _projection.RenameTaskAsync(request, _lifetime.Token),
                candidate => candidate is not null
                    && TaskMutationReceiptMatches(
                        candidate.RuntimeGeneration,
                        candidate.TaskId,
                        candidate.SessionId,
                        request)
                        ? candidate.Snapshot
                        : null);
            if (receipt is null)
                return null;
            if (!TaskMutationReceiptMatches(
                    receipt.RuntimeGeneration,
                    receipt.TaskId,
                    receipt.SessionId,
                    request))
            {
                SetProjectionFault(ControlCenterLocalization.Current.Get("state.rename.receipt.did.not.match.the.chat.result.81"));
                return null;
            }

            await RefreshSnapshotAuxiliaryStateAsync();
            _statusText = receipt.Accepted
                ? receipt.Pending
                    ? ControlCenterLocalization.Current.Get("state.name.accepted.waiting.for.hermes.to.synchronize.82")
                    : ControlCenterLocalization.Current.Get("state.chat.renamed.83")
                : ControlCenterLocalization.Current.Get("state.rename.rejected.84") + SafeCode(receipt.Code);
            OnPropertyChanged(nameof(StatusText));
            return receipt;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus(ControlCenterLocalization.Current.Get("ui.rename.chat"), exception));
            return null;
        }
        finally
        {
            SetCommandState(false);
        }
    }

    internal async Task<D046HermesDeleteTaskReceipt?> DeleteTaskAsync(string taskId)
    {
        // Freeze outcome before activation/voice checks; a failure there must
        // never be described as an already-dispatched deletion.
        if (_commandInFlight || _stopInFlight || _taskPinInFlight)
            return null;
        BeginDeleteOutcome();
        if (_projectionFault || _sequenceGapDetected || !_isConnected)
            return null;
        if (taskId != _activeTaskId && SelectedTask is not null)
            return (await ManageOtherTaskAsync(taskId, null))?.Delete;
        if (!await ActivateTaskForMutationAsync(taskId))
            return null;

        var task = SelectedTask!;
        var frozenVoiceState = _voiceCaptureState;
        FreezeVoiceScope(task);
        var request = new D046HermesDeleteTaskRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            task.TaskId,
            task.SessionId,
            _lastObservedSequence);
        var deleteDispatched = false;
        D046HermesDeleteTaskReceipt? confirmedDelete = null;
        SetCommandState(true, ControlCenterLocalization.Current.Get("state.permanently.deleting.chat.85"));
        try
        {
            LastDeleteStage = D046HermesDeleteStage.VoicePreparation;
            var outcome = await D052SessionLifecycleMutationCoordinator.ExecuteAsync(
                frozenVoiceState,
                async cancellationToken =>
                {
                    var status = await _projection.GetVoiceStatusAsync(
                        CreateVoiceRequest(
                            request.RuntimeGeneration,
                            request.TaskId,
                            request.SessionId),
                        cancellationToken);
                    RequireVoiceMutationScope(
                        status.RuntimeGeneration,
                        status.TaskId,
                        status.SessionId,
                        request,
                        "voice_status_scope_mismatch");
                    return status.Enabled;
                },
                async cancellationToken =>
                {
                    var disabled = await _projection.DisableVoiceAsync(
                        CreateVoiceRequest(
                            request.RuntimeGeneration,
                            request.TaskId,
                            request.SessionId),
                        cancellationToken);
                    RequireVoiceMutationScope(
                        disabled.RuntimeGeneration,
                        disabled.TaskId,
                        disabled.SessionId,
                        request,
                        "voice_off_scope_mismatch");
                    if (disabled.Enabled)
                        return false;

                    var verified = await _projection.GetVoiceStatusAsync(
                        CreateVoiceRequest(
                            request.RuntimeGeneration,
                            request.TaskId,
                            request.SessionId),
                        cancellationToken);
                    RequireVoiceMutationScope(
                        verified.RuntimeGeneration,
                        verified.TaskId,
                        verified.SessionId,
                        request,
                        "voice_off_verify_scope_mismatch");
                    if (verified.Enabled)
                        return false;
                    SetVoiceCaptureState(D046HermesVoiceCaptureState.Off);
                    return true;
                },
                async cancellationToken =>
                {
                    // Voice preparation can await several native operations.
                    // Revalidate the SAME confirmed target before binding the
                    // current observed cursor. Never reuse the pre-voice cursor
                    // or carry a delete into another conversation/generation.
                    var currentSequence = _lastObservedSequence;
                    if (currentSequence < request.LastObservedSequence
                        || !TaskMutationDispatchScopeStillValid(
                            request.RuntimeGeneration, request.TaskId,
                            request.SessionId, currentSequence)
                        || _stopInFlight || IsRollbackOperationInFlight
                        || HasPendingClarification || HasPendingSecureInput
                        || HasPendingImage || HasActiveSubagents
                        || IsVoiceCaptureBusy)
                    {
                        return null!; // Proven no-dispatch, not protocol corruption.
                    }
                    request = request with { LastObservedSequence = currentSequence };
                    var receipt = await ExecuteSnapshotHandoffAsync(
                        () => TaskMutationDispatchScopeStillValid(
                            request.RuntimeGeneration,
                            request.TaskId,
                            request.SessionId,
                            request.LastObservedSequence),
                        () =>
                        {
                            deleteDispatched = true;
                            LastDeleteOutcome = D046HermesDeleteOutcome.AwaitingResult;
                            LastDeleteStage = D046HermesDeleteStage.Dispatch;
                            return _projection.DeleteTaskAsync(request, cancellationToken);
                        },
                        candidate =>
                        {
                            if (candidate is null || !TaskMutationReceiptMatches(
                                candidate.RuntimeGeneration,
                                candidate.TaskId,
                                candidate.SessionId,
                                request)) return null;
                            LastDeleteStage = D046HermesDeleteStage.Receipt;
                            if (IsConfirmedDeleteReceipt(candidate))
                            {
                                confirmedDelete = candidate;
                                LastDeleteOutcome = D046HermesDeleteOutcome.ConfirmedDeleted;
                            }
                            else if (candidate.Deleted)
                                throw new InvalidDataException(
                                    "刪除回覆包含互相矛盾的結果。");
                            else if (!candidate.Deleted && !candidate.TransportUncertain)
                                LastDeleteOutcome = D046HermesDeleteOutcome.Rejected;
                            return candidate.Snapshot;
                        });
                    if (receipt is null)
                    {
                        if (!deleteDispatched)
                            return null!;
                        throw new InvalidDataException(
                            "刪除後未取得可確認的聊天資料。");
                    }
                    LastDeleteStage = D046HermesDeleteStage.Replacement;
                    await RefreshSnapshotAuxiliaryStateAsync(
                        synchronizeVoice: false);
                    return receipt;
                },
                async cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await SynchronizeVoiceStateAsync();
                    if (SelectedTask is not null
                        && _voiceCaptureState != D046HermesVoiceCaptureState.Idle)
                    {
                        throw new InvalidDataException(
                            "Voice was not safely restored for the replacement session.");
                    }
                },
                _lifetime.Token);

            if (_lifetime.IsCancellationRequested)
                return null;
            if (!outcome.Eligible)
            {
                _statusText = ControlCenterLocalization.Current.Get("state.chat.not.deleted.finish.recording.or.wait.for.86");
                OnPropertyChanged(nameof(StatusText));
                return null;
            }
            if (!outcome.VoiceOffVerified || outcome.PreparationError is not null)
            {
                if (outcome.PreparationError is not null)
                    RecordDeleteFailure(outcome.PreparationError);
                SetVoiceMutationFault(
                    outcome.PreparationError,
                    "voice_off_unverified",
                    ControlCenterLocalization.Current.Get("state.voice.shutdown.unconfirmed.no.chat.was.deleted.87"));
                return null;
            }
            if (outcome.MutationError is not null)
                throw outcome.MutationError;

            var receipt = outcome.Value;
            if (receipt is null)
            {
                if (outcome.RestoreError is not null)
                    SetVoiceMutationFault(outcome.RestoreError, "voice_restore_failed",
                        ControlCenterLocalization.Current["chat.delete.notDispatchedVoice"]);
                _statusText = ControlCenterLocalization.Current["chat.delete.notDispatched"];
                OnPropertyChanged(nameof(StatusText));
                return null;
            }
            if (!TaskMutationReceiptMatches(
                    receipt.RuntimeGeneration,
                    receipt.TaskId,
                    receipt.SessionId,
                    request))
            {
                SetProjectionFault(ControlCenterLocalization.Current.Get("state.delete.receipt.did.not.match.the.target.result.88"));
                LastDeleteOutcome = D046HermesDeleteOutcome.Unknown;
                return null;
            }

            if (outcome.RestoreError is not null)
            {
                SetVoiceMutationFault(
                    outcome.RestoreError,
                    "voice_restore_failed",
                    ControlCenterLocalization.Current.Get("state.chat.operation.finished.but.voice.restoration.is.unconfirmed.89"));
            }
            _statusText = receipt.Deleted
                ? receipt.LocalPreferenceWarningCode.Length != 0
                    ? outcome.RestoreError is null
                        ? ControlCenterLocalization.Current.Get("state.chat.permanently.deleted.local.pin.cleanup.is.pending.90")
                        : ControlCenterLocalization.Current.Get("state.chat.permanently.deleted.voice.is.off.and.pin.91")
                    : outcome.RestoreError is null
                        ? ControlCenterLocalization.Current.Get("state.hermes.permanently.deleted.the.chat.92")
                        : ControlCenterLocalization.Current.Get("state.chat.permanently.deleted.voice.remains.off.93")
                : receipt.RecoveryRequired
                    ? ControlCenterLocalization.Current.Get("state.chat.not.deleted.original.session.safely.restored.94")
                    : ControlCenterLocalization.Current.Get("state.delete.rejected.95") + SafeCode(receipt.Code);
            OnPropertyChanged(nameof(StatusText));
            return receipt;
        }
        catch (Exception exception) when (confirmedDelete is not null)
        {
            // The correlated deletion is already known. A later auxiliary,
            // voice, or preflight failure cannot turn it into an unknown result.
            RecordDeleteFailure(exception);
            ShowConfirmedDeletionRecovery(exception);
            return confirmedDelete;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (D046HermesDeletedTaskRecoveryException exception)
        {
            if (!TaskMutationReceiptMatches(
                    exception.Receipt.RuntimeGeneration, exception.Receipt.TaskId,
                    exception.Receipt.SessionId, request)
                || !IsConfirmedDeleteReceipt(exception.Receipt))
            {
                LastDeleteOutcome = D046HermesDeleteOutcome.Unknown;
                RecordDeleteFailure(exception);
                SetProjectionFault(ControlCenterLocalization.Current["chat.delete.unconfirmed"]);
                return null;
            }
            LastDeleteOutcome = D046HermesDeleteOutcome.ConfirmedDeleted;
            LastDeleteStage = D046HermesDeleteStage.Replacement;
            RecordDeleteFailure(exception);
            ShowConfirmedDeletionRecovery(exception.InnerException);
            return exception.Receipt; // Known deletion survives replacement UI failure.
        }
        catch (D046HermesPreDispatchRejectedException exception)
        {
            // Native binding guarantees this rejection occurred before send.
            // A newer cursor is not an uncertain deletion or a broken pipe.
            LastDeleteOutcome = D046HermesDeleteOutcome.NotDispatched;
            RecordDeleteFailure(exception);
            _statusText = ControlCenterLocalization.Current["chat.delete.notDispatched"];
            OnPropertyChanged(nameof(StatusText));
            return null;
        }
        catch (D046HermesSessionPreflightException exception)
        {
            ApplySessionPreflight(exception);
            return null;
        }
        catch (Exception exception)
        {
            RecordDeleteFailure(exception);
            if (confirmedDelete is not null)
            {
                ShowConfirmedDeletionRecovery(exception);
                return confirmedDelete;
            }
            SetProjectionFault(ProjectionFailureStatus(ControlCenterLocalization.Current.Get("ui.delete.chat"), exception));
            return null;
        }
        finally
        {
            SetCommandState(false);
        }
    }

    private void ShowConfirmedDeletionRecovery(Exception? error)
    {
        if (error is D046HermesSessionPreflightException preflight)
            ApplySessionPreflight(preflight);
        else
            SetProjectionFault(ControlCenterLocalization.Current["chat.delete.confirmedRecovery"]);
        // Keep transport failure gates intact; display the stronger, correlated
        // delete receipt instead of a generic "possibly still running" notice.
        _confirmedDeletionNeedsRecovery = true;
        OnPropertyChanged(nameof(StatusText));
    }

    private static void RequireVoiceMutationScope(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesDeleteTaskRequest request,
        string code)
    {
        if (string.Equals(
                runtimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(taskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(sessionId, request.SessionId, StringComparison.Ordinal))
        {
            return;
        }
        throw new D046HermesVoiceOperationException(
            D046HermesVoiceFailureKind.StateConflict,
            code,
            "Voice lifecycle response did not match the frozen delete scope.",
            transportUncertain: false);
    }

    private void SetVoiceMutationFault(
        Exception? exception,
        string fallbackCode,
        string fallbackDisplay)
    {
        if (exception is D046HermesVoiceOperationException voice)
        {
            SetVoiceFault(
                voice.FailureKind,
                voice.Code,
                fallbackDisplay);
            return;
        }
        SetVoiceFault(
            exception is null
                ? D046HermesVoiceFailureKind.ProtocolRejected
                : D046HermesVoiceFailureKind.TransportUncertain,
            fallbackCode,
            fallbackDisplay);
    }

    private async Task<bool> ActivateTaskForMutationAsync(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)
            || _stopInFlight
            || _commandInFlight
            || _taskPinInFlight)
            return false;

        if (!string.Equals(taskId, _activeTaskId, StringComparison.Ordinal)
            && !await OpenTaskAsync(taskId))
        {
            return false;
        }

        var task = SelectedTask;
        var ready = !_commandInFlight
            && task is not null
            && task.CanManage
            && string.Equals(task.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(task.TaskId, _activeTaskId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(task.SessionId);
        if (!ready)
        {
            _statusText = ControlCenterLocalization.Current.Get("state.wait.until.this.chat.is.idle.before.managing.96");
            OnPropertyChanged(nameof(StatusText));
        }
        return ready;
    }

    internal void AddImage(string sourcePath)
    {
        if (!CanAddImage || SelectedTask is null)
            throw new InvalidOperationException(ControlCenterLocalization.Current.Get("state.this.session.is.not.ready.to.receive.images.97"));

        var replacement = D046HermesPendingImage.Load(
            sourcePath,
            _runtimeGeneration,
            SelectedTask.TaskId,
            SelectedTask.SessionId);
        var previous = _pendingImage;
        _pendingImage = replacement;
        previous?.Dispose();
        _statusText = replacement.Preview is null
            ? ControlCenterLocalization.Current.Get("state.image.added.webp.preview.is.unavailable.here.but.98")
            : ControlCenterLocalization.Current.Get("state.image.added.it.will.be.sent.to.lilith.99");
        RaisePendingImageProperties();
    }

    internal void RemovePendingImage()
    {
        if (!CanRemoveImage)
            return;
        ClearPendingImage("圖片已移除。");
    }

    internal D046HermesFaultRecoveryDraft? CaptureFaultRecoveryDraft()
    {
        if (_faultRecoveryDrafts.PendingForRecapture is { } pending)
            return pending;

        var task = SelectedTask;
        if (task is null
            || !string.Equals(task.TaskId, _activeTaskId, StringComparison.Ordinal))
        {
            return null;
        }

        if (D046HermesFaultRecoveryDraft.TryCreate(
                task.TaskId,
                task.LineageId,
                DraftText,
                HasPendingImage,
                out var recovery))
        {
            return recovery;
        }

        if (DraftText.Length != 0 || HasPendingImage)
        {
            _statusText =
                "無法安全保留故障前的完整草稿；原內容未變更。";
            OnPropertyChanged(nameof(StatusText));
        }
        return null;
    }

    internal void DiscardFaultRecoveryDraft()
    {
        if (!CanDiscardFaultRecoveryDraft)
            return;
        _faultRecoveryDrafts.Clear();
        _statusText =
            "已放棄故障前保留的草稿；目前輸入不會被覆寫，也不會自動送出。";
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        RaiseProjectionProperties();
    }

    internal async Task SynchronizeVoiceStateAsync()
    {
        if (_voiceShuttingDown || _voiceLifecycleInFlight)
            return;

        var task = SelectedTask;
        if (!_isConnected
            || !_nativeSurfaceAttested
            || task is null
            || string.IsNullOrWhiteSpace(task.SessionId))
        {
            SetVoiceCaptureState(
                D046HermesVoiceCaptureState.Off,
                "語音：等待聊天就緒");
            return;
        }

        _voiceLifecycleInFlight = true;
        FreezeVoiceScope(task);
        RaiseVoiceProperties();
        var request = CreateVoiceRequest(
            _runtimeGeneration,
            task.TaskId,
            task.SessionId);
        try
        {
            var status = await _projection.GetVoiceStatusAsync(
                request,
                _lifetime.Token);
            if (!VoiceScopeMatches(
                    status.RuntimeGeneration,
                    status.TaskId,
                    status.SessionId))
            {
                SetVoiceFault(
                    D046HermesVoiceFailureKind.StateConflict,
                    "status_scope_mismatch",
                    "語音狀態與目前工作階段不一致。");
                return;
            }

            if (status.Available is false
                || status.AudioAvailable is false
                || status.SttAvailable is false)
            {
                SetVoiceCaptureState(
                    D046HermesVoiceCaptureState.Off,
                    "語音：此環境目前無法使用收音或轉錄");
                return;
            }

            if (status.Enabled)
            {
                SetVoiceCaptureState(D046HermesVoiceCaptureState.Idle);
                return;
            }

            var enabled = await _projection.EnableVoiceAsync(
                CreateVoiceRequest(
                    _runtimeGeneration,
                    task.TaskId,
                    task.SessionId),
                _lifetime.Token);
            if (!VoiceScopeMatches(
                    enabled.RuntimeGeneration,
                    enabled.TaskId,
                    enabled.SessionId))
            {
                SetVoiceFault(
                    D046HermesVoiceFailureKind.StateConflict,
                    "enable_scope_mismatch",
                    "語音啟用結果與目前工作階段不一致。");
                return;
            }

            if (!enabled.Enabled)
            {
                SetVoiceFault(
                    D046HermesVoiceFailureKind.ProtocolRejected,
                    "voice_not_enabled",
                "莉莉絲目前無法使用語音。");
                return;
            }

            SetVoiceCaptureState(D046HermesVoiceCaptureState.Idle);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (D046HermesVoiceOperationException exception)
        {
            SetVoiceFault(
                exception.FailureKind,
                exception.Code,
                exception.Message);
        }
        catch (Exception exception)
        {
            SetVoiceFault(
                D046HermesVoiceFailureKind.TransportUncertain,
                "voice_initialize_failed",
                exception.Message);
        }
        finally
        {
            _voiceLifecycleInFlight = false;
            RaiseVoiceProperties();
        }
    }

    internal async Task<bool> BeginVoiceCaptureAsync()
    {
        if (!CanStartVoiceCapture || SelectedTask is null)
            return false;

        var task = SelectedTask;
        FreezeVoiceScope(task);
        _voiceStartInFlight = true;
        _voiceStopRequested = false;
        SetVoiceCaptureState(D046HermesVoiceCaptureState.Listening);
        try
        {
            var receipt = await _projection.StartVoiceCaptureAsync(
                CreateVoiceRequest(
                    _runtimeGeneration,
                    task.TaskId,
                    task.SessionId),
                _lifetime.Token);
            if (!VoiceScopeMatches(
                    receipt.RuntimeGeneration,
                    receipt.TaskId,
                    receipt.SessionId))
            {
                SetVoiceFault(
                    D046HermesVoiceFailureKind.StateConflict,
                    "start_scope_mismatch",
                    "收音結果與目前工作階段不一致。");
                return false;
            }

            if (!receipt.Started)
            {
                var reason = SafeVoiceText(receipt.Reason);
                SetVoiceCaptureState(
                    D046HermesVoiceCaptureState.Idle,
                    receipt.Busy
                        ? "語音：目前忙碌，未開始收音"
                        : "語音：未開始" + (reason.Length == 0 ? string.Empty : " · " + reason));
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesVoiceOperationException exception)
        {
            SetVoiceFault(
                exception.FailureKind,
                exception.Code,
                exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            SetVoiceFault(
                D046HermesVoiceFailureKind.TransportUncertain,
                "voice_start_failed",
                exception.Message);
            return false;
        }
        finally
        {
            _voiceStartInFlight = false;
            RaiseVoiceProperties();
        }
    }

    internal async Task<bool> EndVoiceCaptureAsync()
    {
        if (_voiceStopInFlight || !IsVoiceCaptureBusy || SelectedTask is null)
            return false;

        _voiceStopRequested = true;
        _voiceStopInFlight = true;
        SetVoiceCaptureState(D046HermesVoiceCaptureState.Transcribing);
        try
        {
            var receipt = await _projection.StopVoiceCaptureAsync(
                CreateVoiceRequest(
                    _runtimeGeneration,
                    _voiceScopeTaskId,
                    _voiceScopeSessionId),
                _lifetime.Token);
            if (!VoiceScopeMatches(
                    receipt.RuntimeGeneration,
                    receipt.TaskId,
                    receipt.SessionId))
            {
                SetVoiceFault(
                    D046HermesVoiceFailureKind.StateConflict,
                    "stop_scope_mismatch",
                    "停止收音結果與目前工作階段不一致。");
                return false;
            }

            // The receipt only acknowledges the stop request. Keep every
            // affected control frozen until the typed voice.status idle event.
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesVoiceOperationException exception)
        {
            if (exception.FailureKind == D046HermesVoiceFailureKind.StateConflict
                && !exception.TransportUncertain
                && string.Equals(
                    exception.Code,
                    "voice.capture_not_active",
                    StringComparison.Ordinal))
            {
                _voiceStopRequested = false;
                SetVoiceCaptureState(D046HermesVoiceCaptureState.Idle);
                return false;
            }
            SetVoiceFault(
                exception.FailureKind,
                exception.Code,
                exception.Message);
            return false;
        }
        catch (Exception exception)
        {
            SetVoiceFault(
                D046HermesVoiceFailureKind.TransportUncertain,
                "voice_stop_failed",
                exception.Message);
            return false;
        }
        finally
        {
            _voiceStopInFlight = false;
            RaiseVoiceProperties();
        }
    }

    internal void ConsumeVoiceEvent(D046HermesVoiceEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_transportFaultStatus is not null)
            return;
        if (string.IsNullOrWhiteSpace(value.EventId)
            || value.RuntimeSequence <= 0
            || string.IsNullOrWhiteSpace(value.SourceMethod)
            || value.Payload is null
            || string.IsNullOrWhiteSpace(value.CanonicalPayloadJson))
        {
            SetVoiceFault(
                D046HermesVoiceFailureKind.ProtocolRejected,
                "malformed_voice_event",
                "收到無法辨識的語音狀態。");
            return;
        }

        if (!VoiceScopeMatches(
                value.RuntimeGeneration,
                value.TaskId,
                value.SessionId))
        {
            return;
        }
        if (value.RuntimeSequence <= _lastVoiceRuntimeSequence)
            return;
        _lastVoiceRuntimeSequence = value.RuntimeSequence;

        switch (value.Payload)
        {
            case D046HermesVoiceStatusPayload status:
            {
                var next = _voiceStopRequested
                    && status.State == D046HermesVoiceCaptureState.Listening
                        ? D046HermesVoiceCaptureState.Transcribing
                        : status.State;
                if (next is D046HermesVoiceCaptureState.Idle
                    or D046HermesVoiceCaptureState.Off
                    or D046HermesVoiceCaptureState.Quarantined)
                {
                    _voiceStopRequested = false;
                }
                SetVoiceCaptureState(next);
                break;
            }
            case D046HermesVoiceTranscriptPayload transcript:
                if (transcript.Kind == D046HermesVoiceTranscriptKind.Text)
                    AppendVoiceTranscript(transcript.Text);
                break;
            case D046HermesVoiceFailurePayload failure:
                SetVoiceFault(
                    failure.FailureKind,
                    failure.Code,
                    failure.DisplayText);
                break;
        }
    }

    internal async Task ShutdownVoiceAsync()
    {
        if (_voiceShuttingDown)
            return;
        _voiceShuttingDown = true;

        var taskId = string.IsNullOrWhiteSpace(_voiceScopeTaskId)
            ? SelectedTask?.TaskId ?? string.Empty
            : _voiceScopeTaskId;
        var sessionId = string.IsNullOrWhiteSpace(_voiceScopeSessionId)
            ? SelectedTask?.SessionId ?? string.Empty
            : _voiceScopeSessionId;
        try
        {
            if (!string.IsNullOrWhiteSpace(_runtimeGeneration)
                && !string.IsNullOrWhiteSpace(taskId)
                && !string.IsNullOrWhiteSpace(sessionId))
            {
                _ = await _projection.DisableVoiceAsync(
                    CreateVoiceRequest(
                        _runtimeGeneration,
                        taskId,
                        sessionId),
                    CancellationToken.None);
            }
        }
        catch (D046HermesVoiceOperationException)
        {
            // Closing must continue even when the voice facade quarantines an
            // uncertain cleanup result.
        }
        catch (Exception)
        {
            // Voice cleanup is best effort and must never hold the window open.
        }
        finally
        {
            _voiceStopRequested = false;
            SetVoiceCaptureState(D046HermesVoiceCaptureState.Off);
        }
    }

    internal async Task<bool> SubmitDraftAsync()
    {
        if (!CanSend || SelectedTask is null)
            return false;

        if (HasPendingClarification)
            return await RespondClarificationAsync(DraftText);

        var slashResult = await TryExecuteComposerCommandAsync();
        if (slashResult.HasValue)
            return slashResult.Value;

        var frozenTask = SelectedTask;
        var frozenImage = _pendingImage;
        if (frozenImage is not null
            && !frozenImage.IsOwnedBy(
                _runtimeGeneration,
                frozenTask.TaskId,
                frozenTask.SessionId))
        {
            ClearPendingImage("工作階段已變更，請重新加入圖片。");
            return false;
        }
        var request = new D046HermesSubmitRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId,
            _lastObservedSequence,
            DraftText,
            ControlCenterLocalization.Current.NativeCommandLocale);

        BeginSubmitStatusCorrelation(frozenTask);
        SetCommandState(true, "正在交給莉莉絲…");
        try
        {
            var receipt = frozenImage is null
                ? await _projection.SubmitAsync(request, _lifetime.Token)
                : await _projection.SubmitWithImageAsync(
                    new D046HermesSubmitWithImageRequest(
                        request,
                        frozenImage.CreateUpload()),
                    _lifetime.Token);
            if (!ReceiptMatches(receipt, frozenTask))
            {
                SetProjectionFault("回覆與目前聊天不一致；送出結果未採用。");
                return false;
            }
            if (!receipt.Accepted)
            {
                ProtectTurnSettlementStatus();
                _statusText = "送出被拒絕 · " + SafeCode(receipt.Code);
                OnPropertyChanged(nameof(StatusText));
                return false;
            }
            var preserveSettledStatus = ObserveSubmitReceipt(receipt);
            if (receipt.ConsumesWithoutTurn)
            {
                DraftText = string.Empty;
                if (ReferenceEquals(_pendingImage, frozenImage))
                    ClearPendingImage();
                if (!preserveSettledStatus)
                {
                    _statusText = receipt.Code switch
                    {
                        "queued" => "已接受並排入下一回合。",
                        "steered" => "已接受為目前回合的補充指示。",
                        "redirected" => "已接受並調整目前回合的方向。",
                        "voice_stopped" => "已接受；語音對話已停止。",
                        _ => throw new InvalidDataException(
                            "無法辨識訊息的處理結果。")
                    };
                }
                OnPropertyChanged(nameof(StatusText));
                return true;
            }
            if (!receipt.CreatesTurn
                || string.Equals(
                    receipt.TurnId,
                    receipt.UserMessageId,
                    StringComparison.Ordinal))
            {
                SetProjectionFault(
                    "送出回覆缺少可驗證的回合識別；草稿與聊天內容均未變更。");
                return false;
            }

            BeginTimelineMutation();
            try
            {
                var upsert = _turnPresentation.UpsertAcceptedUserMessage(
                    receipt.TurnId,
                    receipt.UserMessageId,
                    request.Text,
                    DateTimeOffset.UtcNow);
                if (upsert != D052HermesPresentationUpsertResult.Applied)
                {
                    SetProjectionFault(
                        "送出回覆與既有訊息識別衝突；草稿仍保留。");
                    return false;
                }
                _turnPresentation.SetMessageImages(receipt.TurnId, receipt.UserMessageId, receipt.Images);
            }
            finally
            {
                EndTimelineMutation();
            }

            DraftText = string.Empty;
            if (ReferenceEquals(_pendingImage, frozenImage))
                ClearPendingImage();
            if (!preserveSettledStatus)
                _statusText = "已接受；等待處理進度。";
            OnPropertyChanged(nameof(StatusText));
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus("送出", exception));
            return false;
        }
        finally
        {
            SetCommandState(false);
        }
    }

    internal async Task<bool> StopAsync()
    {
        if (!CanStop || SelectedTask is null)
            return false;

        var frozenTask = SelectedTask;
        var request = new D046HermesStopRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId,
            _lastObservedSequence);

        ProtectTurnSettlementStatus();
        frozenTask.ApplyState(D046HermesTaskState.Stopping);
        SetStopState(true, "正在要求莉莉絲停止…");
        try
        {
            var receipt = await _projection.StopAsync(request, _lifetime.Token);
            if (!ReceiptMatches(receipt, frozenTask))
            {
                SetProjectionFault("停止回覆與目前工作階段不一致；結果未採用。");
                return false;
            }

            if (receipt.Accepted)
            {
                if (frozenTask.State == D046HermesTaskState.Idle)
                    frozenTask.ClearEmergencyStop();
                _statusText = frozenTask.State == D046HermesTaskState.Idle
                    ? "工作已停止；可以繼續輸入。"
                    : "停止要求已接受；等待停止完成。";
                ClearPendingApprovals("工作已停止；等待中的授權已取消。");
                ClearPendingClarification();
                ClearPendingSecureInput();
            }
            else if (string.Equals(
                         receipt.Code,
                         "runtime.session_interrupt_in_flight",
                         StringComparison.Ordinal))
            {
                _statusText = "正在處理停止要求，請稍候。";
            }
            else if (string.Equals(
                         receipt.Code,
                         "runtime.session_interrupt_deferred",
                         StringComparison.Ordinal))
            {
                _statusText = "停止要求尚未送出；前一項安全操作仍在收尾，請再按一次停止。";
            }
            else
            {
                frozenTask.ApplyState(D046HermesTaskState.Failed);
                _statusText = "停止要求失敗；工作已隔離 · " + SafeCode(receipt.Code);
                ClearPendingApprovals(
                    "停止要求失敗；等待中的授權已取消。");
            }
            OnPropertyChanged(nameof(StatusText));
            return receipt.Accepted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus(ControlCenterLocalization.Current.Get("ui.stop"), exception));
            return false;
        }
        finally
        {
            SetStopState(false);
        }
    }

    internal async Task<bool> RespondClarificationAsync(string answer)
    {
        if (!CanRespondClarification
            || SelectedTask is null
            || _pendingClarification is not { } pending
            || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }
        var frozenTask = SelectedTask;
        _clarificationResponseInFlight = true;
        RaiseClarificationProperties();
        try
        {
            var receipt = await _projection.RespondClarificationAsync(
                new D046HermesClarificationResponseRequest(
                    Guid.NewGuid().ToString("N"),
                    _runtimeGeneration,
                    frozenTask.TaskId,
                    frozenTask.SessionId,
                    _lastObservedSequence,
                    pending.ClarificationId,
                    answer),
                _lifetime.Token);
            if (!ReceiptMatches(receipt, frozenTask)
                || !string.Equals(
                    receipt.ClarificationId,
                    pending.ClarificationId,
                    StringComparison.Ordinal))
            {
                SetProjectionFault(ControlCenterLocalization.Current.Get("state.clarification.mismatch"));
                return false;
            }
            if (!receipt.Accepted)
            {
                _statusText = ControlCenterLocalization.Current.Get("state.clarification.expired");
                OnPropertyChanged(nameof(StatusText));
                if (_pendingClarification?.ClarificationId == pending.ClarificationId)
                    ClearPendingClarification();
                return false;
            }
            BeginTimelineMutation();
            try
            {
                _turnPresentation.ShowAcceptedClarificationAnswer(
                    pending.EventId, pending.ClarificationId, answer, DateTimeOffset.UtcNow);
            }
            finally { EndTimelineMutation(); }
            // A user may type a new draft or the agent may ask its next question
            // before the native acknowledgement reaches the UI.
            if (string.Equals(DraftText, answer, StringComparison.Ordinal))
                DraftText = string.Empty;
            if (_pendingClarification?.ClarificationId == pending.ClarificationId)
            {
                ClearPendingClarification();
                if (frozenTask.State == D046HermesTaskState.WaitingForClarification)
                    frozenTask.ApplyState(D046HermesTaskState.Streaming);
            }
            _statusText = ControlCenterLocalization.Current.Get("state.clarification.accepted");
            OnPropertyChanged(nameof(StatusText));
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus(
                ControlCenterLocalization.Current.Get("state.clarification.failedAction"), exception));
            return false;
        }
        finally
        {
            _clarificationResponseInFlight = false;
            RaiseClarificationProperties();
        }
    }

    internal Task<bool> RespondSelectedClarificationAsync()
    {
        var selected = ClarificationChoices
            .Where(choice => choice.IsSelected)
            .Select(choice => "\"" + JsonEncodedText.Encode(choice.Text) + "\"")
            .ToArray();
        return selected.Length == 0
            ? Task.FromResult(false)
            : RespondClarificationAsync("[" + string.Join(",", selected) + "]");
    }

    internal async Task<bool> RespondSecureInputAsync(
        string value,
        bool isCancellation = false)
    {
        if (!CanRespondSecureInput
            || SelectedTask is null
            || _pendingSecureInput is not { } pending)
        {
            return false;
        }
        var frozenTask = SelectedTask;
        _secureInputResponseInFlight = true;
        RaiseSecureInputProperties();
        try
        {
            var receipt = await _projection.RespondSecureInputAsync(
                new D046HermesSecureInputResponseRequest(
                    Guid.NewGuid().ToString("N"),
                    _runtimeGeneration,
                    frozenTask.TaskId,
                    frozenTask.SessionId,
                    _lastObservedSequence,
                    pending.RequestId,
                    pending.InputKind,
                    value),
                _lifetime.Token);
            if (!ReceiptMatches(receipt, frozenTask)
                || receipt.InputKind != pending.InputKind
                || !string.Equals(receipt.RequestId, pending.RequestId, StringComparison.Ordinal))
            {
                SetProjectionFault(ControlCenterLocalization.Current.Get("state.secureInput.mismatch"));
                return false;
            }
            if (_pendingSecureInput?.RequestId == pending.RequestId
                && _pendingSecureInput.InputKind == pending.InputKind)
            {
                ClearPendingSecureInput();
                if (frozenTask.State == D046HermesTaskState.WaitingForSecureInput)
                    frozenTask.ApplyState(D046HermesTaskState.Streaming);
            }
            _statusText = !receipt.Accepted
                ? ControlCenterLocalization.Current.Get("state.secureInput.expired")
                : isCancellation
                    ? ControlCenterLocalization.Current.Get("state.secureInput.cancelled")
                    : ControlCenterLocalization.Current.Get("state.secureInput.accepted");
            OnPropertyChanged(nameof(StatusText));
            return receipt.Accepted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus(
                ControlCenterLocalization.Current.Get("state.secureInput.failedAction"), exception));
            return false;
        }
        finally
        {
            _secureInputResponseInFlight = false;
            RaiseSecureInputProperties();
        }
    }

    internal async Task<bool> RespondApprovalAsync(string choice)
    {
        if (!CanRespondApproval || SelectedTask is null || _pendingApprovals.Count == 0)
            return false;
        var pending = _pendingApprovals.Peek();
        if (!pending.Choices.Contains(choice, StringComparer.Ordinal))
            return false;

        var frozenTask = SelectedTask;
        var request = new D046HermesApprovalResponseRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId,
            _lastObservedSequence,
            choice);

        _approvalStatusText = "正在送出「" + ApprovalChoiceLabel(choice) + "」…";
        RaiseApprovalProperties();
        _approvalResponseInFlight = true;
        SetCommandState(true);
        try
        {
            var receipt = await _projection.RespondApprovalAsync(
                request,
                _lifetime.Token);
            if (!ApprovalReceiptMatches(receipt, frozenTask, choice))
            {
                SetProjectionFault("工作授權回覆與目前工作階段不一致；不會自動重送。");
                return false;
            }
            if (!receipt.Accepted || receipt.Resolved != 1)
            {
                if (receipt.RetryAllowed
                    && receipt.Code == RuntimeApprovalRetryProof.FailureCode
                    && !receipt.Accepted && receipt.Resolved == 0
                    && ReferenceEquals(frozenTask, SelectedTask)
                    && frozenTask.State == D046HermesTaskState.WaitingForApproval
                    && _nativeSurfaceAttested && !_activeSessionConfigStale
                    && _pendingApprovals.Count > 0
                    && ReferenceEquals(_pendingApprovals.Peek(), pending)
                    && string.Equals(receipt.RetryApprovalId, pending.ApprovalId, StringComparison.Ordinal))
                {
                    _approvalStatusText = "本次授權尚未送出或已完整回復，沒有執行操作。請重新選擇授權，或按停止。";
                    _statusText = _approvalStatusText;
                    RaiseApprovalProperties();
                    RaiseProjectionProperties();
                    return false;
                }
                if (IsDefinitiveExpiredApprovalFailure(receipt.Code))
                {
                    ProtectTurnSettlementStatus();
                    UpdateApprovalTimeline(
                        pending.EventId,
                        "授權已過期",
                        "工作已先結束；沒有執行這次授權的操作。");
                    ClearPendingApprovals(
                        "工作已結束；未回覆的工作授權已過期，沒有執行。");
                    if (frozenTask.State == D046HermesTaskState.WaitingForApproval)
                        frozenTask.ApplyState(D046HermesTaskState.Streaming);
                    _statusText = "工作授權已過期；沒有執行這次操作。";
                    RaiseProjectionProperties();
                    return false;
                }

                ProtectTurnSettlementStatus();
                frozenTask.RequireEmergencyStop();
                frozenTask.ApplyState(D046HermesTaskState.Failed);
                var failureCode = SafeCode(receipt.Code);
                UpdateApprovalTimeline(
                    pending.EventId,
                    "授權未完成",
                    "操作已被拒絕 · " + failureCode);
                ClearPendingApprovals(
                    "授權回覆失敗；等待中的授權已取消，請按停止結束仍可能執行的工作。");
                _statusText = "工作授權失敗；請立即按停止 · " + failureCode;
                RaiseProjectionProperties();
                return false;
            }

            _pendingApprovals.Dequeue();
            var nativeConfigChanged = string.Equals(
                receipt.Choice,
                "always",
                StringComparison.Ordinal);
            var approved = choice != "deny";
            UpdateApprovalTimeline(
                pending.EventId,
                approved ? "已" + ApprovalChoiceLabel(choice) : "已拒絕",
                ApprovalChoiceDetail(choice));
            if (nativeConfigChanged)
            {
                _activeSessionConfigStale = true;
                _nativeSurfaceAttested = false;
                if (HasPendingImage)
                {
                    ClearPendingImage(
                        "授權設定已變更；請在新的工作階段重新加入圖片。");
                }
                _nativeToolsActivationText =
                    "授權已永久保存。這段聊天仍為唯讀；請建立新聊天。";
            }
            var settledWhileResponseInFlight =
                frozenTask.State == D046HermesTaskState.Idle;
            if (settledWhileResponseInFlight)
            {
                ClearPendingApprovals(
                    "工作已結束；其他未回覆的工作授權已過期，沒有執行。");
            }
            else if (_pendingApprovals.Count > 0)
            {
                frozenTask.ApplyState(D046HermesTaskState.WaitingForApproval);
            }
            else if (frozenTask.State == D046HermesTaskState.WaitingForApproval)
            {
                frozenTask.ApplyState(D046HermesTaskState.Streaming);
            }
            if (!settledWhileResponseInFlight)
            {
                RefreshPendingApprovalDisplay(
                    nativeConfigChanged
                        ? "已永遠允許；請建立新聊天以套用授權。"
                        : approved
                        ? "已" + ApprovalChoiceLabel(choice) + "；莉莉絲正在繼續工作。"
                        : "已拒絕；莉莉絲正在收尾。");
            }
            _statusText = _approvalStatusText;
            RaiseProjectionProperties();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _approvalAttentionRequired = true;
            _approvalStatusText =
                "工作授權傳送中斷；為避免重複操作，不會自動重送。";
            RaiseApprovalProperties();
            SetProjectionFault(ProjectionFailureStatus("工作授權", exception));
            return false;
        }
        finally
        {
            _approvalResponseInFlight = false;
            SetCommandState(false);
        }
    }

    internal async Task<bool> CompressAsync()
    {
        if (!CanCompress || SelectedTask is null)
            return false;

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var request = new D046HermesCompressionRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence,
            FocusTopic: string.Empty);

        SetCommandState(true, "正在要求壓縮對話內容…");
        try
        {
            var receipt = await ExecuteSnapshotHandoffAsync(
                () => CompressionDispatchScopeStillValid(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId,
                    request.LastObservedSequence),
                () => _projection.CompressAsync(request, _lifetime.Token),
                candidate => CompressionReceiptMatches(
                        candidate,
                        frozenGeneration,
                        frozenTaskId,
                        frozenSessionId)
                    ? candidate.Snapshot
                    : null);
            if (receipt is null)
                return false;
            if (!CompressionReceiptMatches(
                    receipt,
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId))
            {
                SetProjectionFault("內容壓縮回覆與目前工作階段不一致；結果未採用。");
                return false;
            }

            _ = await D046HermesSnapshotCompanionRefreshPolicy.RunCompressionAsync(
                CaptureSnapshotCompanionAuthority(),
                CreateSnapshotCompanionOperations(),
                refreshNativeTools: false);
            _compressionStatusText = FormatCompressionStatus(receipt);
            _statusText = receipt.Accepted
                ? receipt.NoOp
                    ? "壓縮檢查完成；內容不需變更。"
                    : "對話內容已壓縮。"
                : receipt.Aborted
                    ? "壓縮已中止；原內容保留。"
                    : "未執行壓縮 · " + SafeCode(receipt.Code);
            OnPropertyChanged(nameof(CompressionStatusText));
            OnPropertyChanged(nameof(StatusText));
            return receipt.Accepted;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus("內容壓縮", exception));
            return false;
        }
        finally
        {
            SetCommandState(false);
        }
    }

    internal async Task<bool> RefreshObservabilityAsync()
    {
        if (!CanRefreshObservability || SelectedTask is null)
            return false;

        var frozenTask = SelectedTask;
        _observabilityInFlight = true;
        _observabilityStatusText = "正在讀取詳細狀態…";
        RaiseObservabilityProperties();
        try
        {
            var snapshot = await _projection.GetObservabilityAsync(
                frozenTask.TaskId,
                frozenTask.SessionId,
                _lifetime.Token);
            if (!string.Equals(snapshot.RuntimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
                || !string.Equals(snapshot.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
                || !string.Equals(snapshot.SessionId, frozenTask.SessionId, StringComparison.Ordinal)
                || SelectedTask is null
                || !string.Equals(SelectedTask.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
                || !string.Equals(SelectedTask.SessionId, frozenTask.SessionId, StringComparison.Ordinal))
            {
                _observabilityStatusText = "聊天已變更；先前的狀態資料未採用。";
                RaiseObservabilityProperties();
                return false;
            }

            ApplyObservabilitySnapshot(snapshot);
            return snapshot.Usage.IsAvailable
                || snapshot.Context.IsAvailable
                || snapshot.Learning.IsAvailable;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            _observabilityStatusText = "暫時無法讀取詳細狀態；對話仍可使用。";
            RaiseObservabilityProperties();
            return false;
        }
        finally
        {
            _observabilityInFlight = false;
            RaiseObservabilityProperties();
        }
    }

    internal async Task<bool> RefreshRollbackCheckpointsAsync()
    {
        if (!CanRefreshRollback || SelectedTask is null)
            return false;

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var request = new D046HermesRollbackListRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence);
        _rollbackListInFlight = true;
        _rollbackStatusText = "正在讀取工作區檢查點…";
        ClearRollbackCatalog();
        RaiseRollbackProperties();
        try
        {
            var catalog = await _projection.ListRollbackCheckpointsAsync(
                request,
                _lifetime.Token);
            if (!RollbackScopeStillMatches(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId)
                || !string.Equals(catalog.RuntimeGeneration, frozenGeneration,
                    StringComparison.Ordinal)
                || !string.Equals(catalog.TaskId, frozenTaskId, StringComparison.Ordinal)
                || !string.Equals(catalog.SessionId, frozenSessionId,
                    StringComparison.Ordinal)
                || catalog.ThroughSequence < _lastObservedSequence
                || catalog.Checkpoints is null)
            {
                _rollbackStatusText = "工作狀態已變更；檢查點清單未採用。";
                return false;
            }

            _rollbackCatalogGeneration = catalog.RuntimeGeneration;
            _rollbackCatalogTaskId = catalog.TaskId;
            _rollbackCatalogSessionId = catalog.SessionId;
            _rollbackCatalogEnabled = catalog.Enabled;
            foreach (var checkpoint in catalog.Checkpoints)
                RollbackCheckpoints.Add(
                    new D046HermesRollbackCheckpointViewModel(checkpoint));
            _rollbackStatusText = !catalog.Enabled
                ? "工作區檢查點目前未啟用。"
                : RollbackCheckpoints.Count == 0
                    ? "目前沒有可還原的工作區檢查點。"
                    : "請選擇檢查點，再預覽完整工作區差異。";
            return catalog.Enabled;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _rollbackStatusText = "暫時無法讀取工作區檢查點；對話仍可使用。"
                + RollbackFailureSuffix(exception);
            return false;
        }
        finally
        {
            _rollbackListInFlight = false;
            RaiseRollbackProperties();
        }
    }

    internal async Task<bool> GetRollbackDiffAsync()
    {
        if (!CanGetRollbackDiff
            || SelectedTask is null
            || SelectedRollbackCheckpoint is null)
        {
            return false;
        }

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var frozenHash = SelectedRollbackCheckpoint.Hash;
        var request = new D046HermesRollbackDiffRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence,
            frozenHash);
        _rollbackDiffInFlight = true;
        _rollbackStatusText = "正在產生工作區差異預覽…";
        ClearRollbackDiff();
        RaiseRollbackProperties();
        try
        {
            var receipt = await _projection.GetRollbackDiffAsync(
                request,
                _lifetime.Token);
            if (!RollbackScopeStillMatches(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId)
                || SelectedRollbackCheckpoint is null
                || !string.Equals(SelectedRollbackCheckpoint.Hash, frozenHash,
                    StringComparison.Ordinal)
                || !string.Equals(receipt.RuntimeGeneration, frozenGeneration,
                    StringComparison.Ordinal)
                || !string.Equals(receipt.TaskId, frozenTaskId, StringComparison.Ordinal)
                || !string.Equals(receipt.SessionId, frozenSessionId,
                    StringComparison.Ordinal)
                || !string.Equals(receipt.CheckpointHash, frozenHash,
                    StringComparison.Ordinal)
                || receipt.ThroughSequence < _lastObservedSequence)
            {
                _rollbackStatusText = "工作狀態已變更；差異預覽未採用。";
                return false;
            }

            _rollbackDiffText = FormatRollbackDiff(receipt);
            if (!receipt.Succeeded
                || string.IsNullOrWhiteSpace(receipt.ConfirmationToken))
            {
                _rollbackStatusText = "尚未取得可確認的差異；可重新預覽。";
                return false;
            }

            _rollbackConfirmationToken = receipt.ConfirmationToken;
            _rollbackDiffHash = receipt.CheckpointHash;
            _rollbackStatusText = receipt.Truncated
                ? "差異內容已截短；請確認後再還原整個工作區。"
                : "差異已就緒；還原前仍會再次要求確認。";
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _rollbackStatusText = "暫時無法取得工作區差異；可稍後重試，對話仍可使用。"
                + RollbackFailureSuffix(exception);
            return false;
        }
        finally
        {
            _rollbackDiffInFlight = false;
            RaiseRollbackProperties();
        }
    }

    internal async Task<bool> RestoreRollbackAsync()
    {
        if (!CanRestoreRollback
            || SelectedTask is null
            || SelectedRollbackCheckpoint is null)
        {
            return false;
        }

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var frozenHash = SelectedRollbackCheckpoint.Hash;
        var frozenToken = _rollbackConfirmationToken;
        var preservedDraft = DraftText;
        var preservedPendingImage = _pendingImage;
        var request = new D046HermesRollbackRestoreRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence,
            frozenHash,
            frozenToken,
            Confirm: true);
        _rollbackRestoreInFlight = true;
        _rollbackStatusText = "正在還原整個莉莉絲工作區…";
        RaiseRollbackProperties();
        try
        {
            var receipt = await ExecuteSnapshotHandoffAsync(
                () => RollbackRestoreDispatchScopeStillValid(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId,
                    frozenHash,
                    frozenToken,
                    request.LastObservedSequence),
                () => _projection.RestoreRollbackAsync(
                    request,
                    _lifetime.Token),
                candidate => RollbackRestoreReceiptMatches(
                        candidate,
                        frozenGeneration,
                        frozenTaskId,
                        frozenSessionId,
                        frozenHash)
                    && candidate.Succeeded
                        ? candidate.Snapshot
                        : null);
            if (receipt is null)
                return false;
            if (!RollbackRestoreReceiptMatches(
                    receipt,
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId,
                    frozenHash))
            {
                if (receipt.Succeeded || receipt.HistoryRewound)
                {
                    SetRollbackProjectionFault(
                        "工作區已還原，但回覆無法安全套用；請重新連線，對話已停用。");
                }
                else
                {
                    _rollbackRestoreQuarantined = true;
                    _rollbackStatusText = "還原回覆與目前工作狀態不一致；已鎖定再次還原，對話仍可使用。";
                }
                return false;
            }

            if (!receipt.Succeeded)
            {
                if (receipt is
                    {
                        Partial: true,
                        WorkspaceRestored: true,
                        HistoryRewound: false
                    })
                {
                    _rollbackRestoreQuarantined = true;
                    _rollbackStatusText = "工作區只完成部分還原；已永久鎖定再次還原，對話仍可使用。";
                    return false;
                }
                if (receipt.Partial
                    || receipt.WorkspaceRestored
                    || receipt.HistoryRewound)
                {
                    SetRollbackProjectionFault(
                        "工作區回報了不一致的還原結果；請重新連線，對話已停用。");
                    return false;
                }

                ClearRollbackCatalog();
                _rollbackStatusText = "工作區未還原；請重新整理檢查點並重新預覽差異。"
                    + RollbackReceiptSuffix(receipt);
                return false;
            }

            DraftText = preservedDraft;
            if (!ReferenceEquals(_pendingImage, preservedPendingImage))
            {
                throw new InvalidDataException(
                    "Rollback changed the pending image outside its frozen scope.");
            }
            ClearRollbackCatalog();
            _rollbackStatusText = "工作區已還原；對話歷史已重新載入。";
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesRollbackOperationException exception)
        {
            var partial = exception.RestoreReceipt is
            {
                Partial: true,
                WorkspaceRestored: true,
                HistoryRewound: false
            };
            if (partial)
            {
                _rollbackRestoreQuarantined = true;
                _rollbackStatusText = "工作區只完成部分還原；已永久鎖定再次還原，對話仍可使用。";
            }
            else if (exception.TransportUncertain
                     || exception.FailureKind ==
                        D046HermesRollbackFailureKind.ProtocolRejected)
            {
                SetRollbackProjectionFault(
                    "無法安全確認工作區還原後的對話歷史；請重新連線，對話已停用。");
            }
            else if (string.Equals(
                         exception.Code,
                         "rollback.restore.quarantined",
                         StringComparison.Ordinal))
            {
                _rollbackRestoreQuarantined = true;
                _rollbackStatusText = "工作區還原已鎖定；請重新整理工作階段後再試，對話仍可使用。";
            }
            else
            {
                ClearRollbackCatalog();
                _rollbackStatusText = "工作狀態已變更；請重新整理檢查點後再試。";
            }
            return false;
        }
        catch (Exception exception)
        {
            SetRollbackProjectionFault(
                "工作區還原後無法更新聊天內容；對話已停用，請重新連線。",
                exception);
            return false;
        }
        finally
        {
            _rollbackRestoreInFlight = false;
            ClearRollbackDiff();
            RaiseRollbackProperties();
        }
    }

    internal Task<bool> RefreshDelegationAsync()
        => RefreshDelegationCoreAsync(allowCommandInFlight: false);

    private async Task<bool> RefreshDelegationCoreAsync(bool allowCommandInFlight)
    {
        if ((!allowCommandInFlight && _commandInFlight)
            || _delegationStatusInFlight
            || _delegationControlInFlight
            || !_isConnected
            || !_nativeSurfaceAttested
            || _sequenceGapDetected
            || _projectionFault
            || SelectedTask is null
            || !string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(SelectedTask.SessionId))
        {
            return false;
        }

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var request = new D046HermesDelegationStatusRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence);
        _delegationStatusInFlight = true;
        _delegationStatusText = "正在讀取分身工作狀態…";
        RaiseDelegationProperties();
        try
        {
            var snapshot = await _projection.GetDelegationStatusAsync(
                request,
                _lifetime.Token);
            if (!DelegationScopeStillMatches(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId)
                || !string.Equals(
                    snapshot.RuntimeGeneration,
                    frozenGeneration,
                    StringComparison.Ordinal)
                || !string.Equals(snapshot.TaskId, frozenTaskId, StringComparison.Ordinal)
                || !string.Equals(snapshot.SessionId, frozenSessionId, StringComparison.Ordinal)
                || snapshot.ActiveChildren is null
                || snapshot.ActiveChildren.Count > 2
                || snapshot.ThroughSequence < request.LastObservedSequence)
            {
                _delegationUnavailable = true;
                _delegationStatusText = "分身工作狀態與目前工作階段不一致；結果未採用。";
                return false;
            }

            if (snapshot.ThroughSequence < _lastObservedSequence
                || request.LastObservedSequence < _lastObservedSequence)
            {
                _delegationUnavailable = false;
                _delegationStatusText = "已有較新的分身工作事件；保留目前狀態。";
                return true;
            }

            ReplaceDelegationCards(snapshot.ActiveChildren);
            _delegationUnavailable = false;
            _delegationStatusText = _delegationControlQuarantined
                ? "狀態已更新；先前控制結果不確定，本工作階段不會再自動送出控制。"
                : snapshot.Paused
                    ? "分身工作目前暫停。"
                    : snapshot.ActiveChildren.Count == 0
                        ? ControlCenterLocalization.Current.Get("ui.delegation.none")
                        : snapshot.ActiveChildren.Count + " 個分身工作進行中。";
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
            _delegationUnavailable = true;
            _delegationStatusText = "暫時無法讀取分身工作狀態；對話仍可使用。";
            return false;
        }
        finally
        {
            _delegationStatusInFlight = false;
            RaiseDelegationProperties();
        }
    }

    internal async Task<bool> SteerSubagentAsync(
        D046HermesSubagentCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanControlSubagents()
            || !_subagentsById.TryGetValue(card.SubagentId, out var currentCard)
            || !ReferenceEquals(currentCard, card))
        {
            return false;
        }

        var text = card.SteerText.Trim();
        if (text.Length == 0 || SelectedTask is null)
            return false;

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var request = new D046HermesSubagentSteerRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence,
            card.SubagentId,
            text);
        _delegationControlInFlight = true;
        _delegationStatusText = "正在送出補充方向…";
        RaiseDelegationProperties();
        try
        {
            var receipt = await _projection.SteerSubagentAsync(request, _lifetime.Token);
            if (!DelegationScopeStillMatches(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId)
                || !SubagentSteerReceiptMatches(receipt, request))
            {
                throw new InvalidDataException("Subagent steer receipt scope mismatch.");
            }

            if (!receipt.Queued)
            {
                _delegationStatusText = "這次補充方向未被接受；不會自動重送。";
                return false;
            }

            card.MarkSteerQueued();
            _delegationStatusText = "補充方向已送出。";
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesPreDispatchRejectedException)
        {
            await ReconcilePreDispatchDelegationRejectionAsync(
                card,
                "工作狀態已更新或已完成，這次補充未送出。");
            return false;
        }
        catch (Exception)
        {
            QuarantineDelegationControls(
                "補充方向的傳送結果不確定；不會自動重送，對話仍可使用。");
            return false;
        }
        finally
        {
            _delegationControlInFlight = false;
            RaiseDelegationProperties();
        }
    }

    internal async Task<bool> InterruptSubagentAsync(
        D046HermesSubagentCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!CanControlSubagents()
            || !_subagentsById.TryGetValue(card.SubagentId, out var currentCard)
            || !ReferenceEquals(currentCard, card)
            || SelectedTask is null)
        {
            return false;
        }

        var frozenGeneration = _runtimeGeneration;
        var frozenTaskId = SelectedTask.TaskId;
        var frozenSessionId = SelectedTask.SessionId;
        var request = new D046HermesSubagentInterruptRequest(
            Guid.NewGuid().ToString("N"),
            frozenGeneration,
            frozenTaskId,
            frozenSessionId,
            _lastObservedSequence,
            card.SubagentId);
        _delegationControlInFlight = true;
        _delegationStatusText = "正在停止分身工作…";
        RaiseDelegationProperties();
        try
        {
            var receipt = await _projection.InterruptSubagentAsync(
                request,
                _lifetime.Token);
            if (!DelegationScopeStillMatches(
                    frozenGeneration,
                    frozenTaskId,
                    frozenSessionId)
                || !SubagentInterruptReceiptMatches(receipt, request))
            {
                throw new InvalidDataException("Subagent interrupt receipt scope mismatch.");
            }

            if (!receipt.Found)
            {
                RemoveSubagent(card.SubagentId);
                _delegationStatusText = "這個分身工作已不在進行中；不會自動重送。";
                return false;
            }

            card.MarkInterruptRequested();
            _delegationStatusText = "停止要求已送出；等待停止完成。";
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesPreDispatchRejectedException)
        {
            await ReconcilePreDispatchDelegationRejectionAsync(
                card,
                "工作狀態已更新或已完成，這次停止未送出。");
            return false;
        }
        catch (Exception)
        {
            QuarantineDelegationControls(
                "停止要求的傳送結果不確定；不會自動重送，對話仍可使用。");
            return false;
        }
        finally
        {
            _delegationControlInFlight = false;
            RaiseDelegationProperties();
        }
    }

    internal async Task<D046HermesModelSelectionReceipt?> SelectModelAsync(
        D046HermesModelOption option,
        bool confirmExpensiveModel = false)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (!CanSelectModel || SelectedTask is null)
        {
            SynchronizeModelSelectionFromSessionInfo();
            return null;
        }
        if (string.Equals(SelectedTask.ProviderId, option.ProviderId, StringComparison.Ordinal)
            && string.Equals(
                SelectedTask.ModelDisplayName,
                option.ModelId,
                StringComparison.Ordinal))
        {
            SynchronizeModelSelectionFromSessionInfo();
            return null;
        }

        var frozenTask = SelectedTask;
        var recoveringCredential = CanRecoverModelSelection;
        var request = new D046HermesModelSelectionRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId,
            _lastObservedSequence,
            option.ProviderId,
            option.ModelId,
            confirmExpensiveModel);
        SetCommandState(true, "正在切換莉莉絲使用的模型…");
        try
        {
            var receipt = recoveringCredential
                ? await ExecuteSnapshotHandoffAsync(
                    () => _isConnected && !_projectionFault && !_sequenceGapDetected
                        && CanRecoverModelSelection && ReferenceEquals(SelectedTask, frozenTask)
                        && _runtimeGeneration == request.RuntimeGeneration
                        && _lastObservedSequence == request.LastObservedSequence,
                    () => _projection.SelectModelAsync(request, _lifetime.Token),
                    value => value.RecoverySnapshot)
                : await _projection.SelectModelAsync(request, _lifetime.Token);
            if (receipt is null)
            {
                if (!_projectionFault && !_sequenceGapDetected && _isConnected)
                {
                    _statusText = _modelSelectionResultStatus =
                        ControlCenterLocalization.Current["chat.modelSelectionNotSent"];
                    OnPropertyChanged(nameof(StatusText));
                }
                return null;
            }
            if (!ModelReceiptMatches(receipt, frozenTask, option))
            {
                SetProjectionFault("模型切換回覆與目前工作階段不一致；結果未採用。");
                return null;
            }

            if (recoveringCredential && receipt.Accepted)
            {
                if (receipt.RecoverySnapshot is null)
                    throw new InvalidDataException("Credential recovery omitted its verified snapshot.");
                await RefreshSnapshotAuxiliaryStateAsync();
            }

            var sessionInfoAlreadyConfirmed = receipt.Accepted
                && !receipt.Deferred
                && string.Equals(
                    frozenTask.ProviderId,
                    option.ProviderId,
                    StringComparison.Ordinal)
                && string.Equals(
                    frozenTask.ModelDisplayName,
                    option.ModelId,
                    StringComparison.Ordinal);
            _statusText = receipt.Accepted
                ? receipt.Deferred
                    ? "模型切換已排入下一輪；目前仍顯示已確認的模型。"
                    : sessionInfoAlreadyConfirmed
                        ? "模型切換已確認生效。"
                        : "模型切換已接受；等待確認生效。"
                : receipt.ConfirmationRequired
                    ? "模型需要額外確認；本次未切換。"
                    : "模型切換被拒絕 · " + SafeCode(receipt.Code);
            _modelSelectionResultStatus = _statusText;
            OnPropertyChanged(nameof(StatusText));
            return receipt;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus("模型切換", exception));
            return null;
        }
        finally
        {
            SynchronizeModelSelectionFromSessionInfo();
            SetCommandState(false);
        }
    }

    internal void DeclineModelSelectionConfirmation()
    {
        _statusText = "已取消模型切換；目前模型未變更。";
        _modelSelectionResultStatus = _statusText;
        SynchronizeModelSelectionFromSessionInfo();
        OnPropertyChanged(nameof(StatusText));
    }

    internal void Consume(D046HermesProjectionEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_transportFaultStatus is not null)
            return;
        _receivedEvents.Add(value);
        OnPropertyChanged(nameof(EventCountText));

        if (!IsValidEnvelope(value))
        {
            SetProjectionFault("收到無法驗證的資料；已停用送出。");
            return;
        }
        if (!string.Equals(
                value.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal))
        {
            ClearPendingImage();
            SetProjectionFault("收到其他服務連線的資料；請重新載入聊天。");
            return;
        }
        if (_pendingImage is not null
            && string.Equals(value.TaskId, _activeTaskId, StringComparison.Ordinal)
            && !_pendingImage.IsOwnedBy(
                value.RuntimeGeneration,
                value.TaskId,
                value.SessionId))
        {
            ClearPendingImage("工作階段已變更，請重新加入圖片。");
        }
        if (value.Sequence <= _lastObservedSequence)
        {
            // A response-backed snapshot can overtake already queued UI event
            // callbacks. Keep the original envelope for observability, but do
            // not let that expected replay overwrite the response status.
            return;
        }
        if (_lastObservedSequence > 0
            && value.Sequence != _lastObservedSequence + 1)
        {
            _sequenceGapDetected = true;
            ProtectTurnSettlementStatus();
            _statusText = "聊天資料不連續；已停用送出，等待重新同步。";
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanCompress));
            RaiseDelegationProperties();
        }

        _lastObservedSequence = value.Sequence;
        ProjectEvent(value);
    }

    public void Dispose()
    {
        _recoveryDisposed = true;
        ControlCenterLocalization.Current.LanguageChanged -= RefreshLanguage;
        InvalidateCapabilityCatalog(clear: true, stop: true);
        ResetTurnSettlementStatusCorrelation();
        _faultRecoveryDrafts.Clear();
        DraftText = string.Empty;
        ClearPendingImage();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task<bool> RunSnapshotCommandAsync(
        Func<bool> canDispatch,
        Func<Task<D046HermesWorkbenchSnapshot>> operation,
        string progressText)
    {
        ArgumentNullException.ThrowIfNull(canDispatch);
        ArgumentNullException.ThrowIfNull(operation);
        if (_commandInFlight)
            return false;

        SetCommandState(true, progressText);
        try
        {
            var snapshot = await ExecuteSnapshotHandoffAsync(
                canDispatch,
                operation,
                candidate => candidate);
            if (snapshot is not null)
            {
                await RefreshSnapshotAuxiliaryStateAsync();
                return true;
            }
            return false;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (D046HermesSessionPreflightException exception)
        {
            ApplySessionPreflight(exception);
            return false;
        }
        catch (D046HermesSessionTransitionRejectedException)
        {
            _statusText = ControlCenterLocalization.Current["chat.switchBusy"];
            RaiseProjectionProperties();
            return false;
        }
        catch (Exception exception)
        {
            SetProjectionFault(ProjectionFailureStatus("工作階段載入", exception));
            return false;
        }
        finally
        {
            SetCommandState(false);
        }
    }

    private async Task<T?> ExecuteSnapshotHandoffAsync<T>(
        Func<bool> canDispatch,
        Func<Task<T>> operation,
        Func<T, D046HermesWorkbenchSnapshot?> snapshotSelector,
        Action<D046HermesWorkbenchSnapshot>? snapshotApplier = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(canDispatch);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(snapshotSelector);
        var applySnapshot = snapshotApplier ?? ApplySnapshot;

        var epoch = _snapshotHandoff.Begin();
        var settled = false;
        var applyStarted = false;
        try
        {
            if (!canDispatch())
            {
                settled = true;
                _snapshotHandoff.Abort(
                    epoch,
                    new InvalidOperationException(
                        "連線交接後，這次聊天資料讀取已失效。"));
                return null;
            }

            var result = await operation();
            var snapshot = snapshotSelector(result);
            if (snapshot is null)
            {
                settled = true;
                _snapshotHandoff.Abort(
                    epoch,
                    new InvalidDataException(
                        "回覆未提供可確認的聊天資料。"));
                return result;
            }

            applyStarted = true;
            applySnapshot(snapshot);
            FreezeVoiceScope(SelectedTask);
            settled = true;
            _snapshotHandoff.Commit(epoch);
            return result;
        }
        catch (D046HermesDeletedTaskRecoveryException exception)
            when (!settled && !applyStarted
                && exception.InnerException is D046HermesSessionPreflightException
                    { ContextSnapshot: not null })
        {
            var preflight = (D046HermesSessionPreflightException)exception.InnerException!;
            applyStarted = true;
            applySnapshot(preflight.ContextSnapshot!);
            FreezeVoiceScope(SelectedTask);
            settled = true;
            _snapshotHandoff.Commit(epoch);
            throw;
        }
        catch (D046HermesSessionPreflightException exception)
            when (!settled
                && !applyStarted
                && exception.ContextSnapshot is not null)
        {
            applyStarted = true;
            applySnapshot(exception.ContextSnapshot);
            FreezeVoiceScope(SelectedTask);
            settled = true;
            _snapshotHandoff.Commit(epoch);
            throw;
        }
        catch (Exception exception)
        {
            if (!settled)
            {
                settled = true;
                if (applyStarted)
                    _snapshotHandoff.Fail(epoch, exception);
                else
                    _snapshotHandoff.Abort(epoch, exception);
            }
            throw;
        }
    }

    private async Task RefreshSnapshotAuxiliaryStateAsync(
        bool synchronizeVoice = true)
    {
        _ = await D046HermesSnapshotCompanionRefreshPolicy.RunFullAsync(
            CaptureSnapshotCompanionAuthority(),
            CreateSnapshotCompanionOperations(synchronizeVoice),
            refreshNativeTools: false);
    }

    private D046HermesSnapshotCompanionRefreshPolicy.AuthorityState
        CaptureSnapshotCompanionAuthority()
        => new(
            _isConnected,
            _nativeSurfaceAttested,
            _activeSessionConfigStale,
            _sequenceGapDetected,
            _projectionFault,
            _rollbackRestoreQuarantined,
            _delegationControlQuarantined,
            _voiceCaptureState,
            HasPendingApproval,
            SelectedTask?.State,
            CredentialRecoveryAvailable: CanRecoverModelSelection);

    private D046HermesSnapshotCompanionRefreshPolicy.Operations
        CreateSnapshotCompanionOperations(bool synchronizeVoice = true)
        => new(
            async () =>
            {
                _ = await RefreshDelegationCoreAsync(allowCommandInFlight: true);
            },
            LoadModelOptionsAsync,
            async () =>
            {
                _ = await RefreshNativeToolsCoreAsync(allowCommandInFlight: true);
            },
            synchronizeVoice
                ? SynchronizeVoiceStateAsync
                : static () => Task.CompletedTask);

    private static bool TaskMutationReceiptMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesDeleteTaskRequest request)
        => string.Equals(runtimeGeneration, request.RuntimeGeneration, StringComparison.Ordinal)
            && string.Equals(taskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(sessionId, request.SessionId, StringComparison.Ordinal);

    private bool CanCreateOrSwitchTaskDuringSnapshotCommand()
        => _commandInFlight
            && !HasRunningConversation
            && !_taskPinInFlight
            && _isConnected
            && !_sequenceGapDetected
            && !_projectionFault
            && !_nativeToolsInFlight
            && !IsRollbackOperationInFlight
            && !_delegationStatusInFlight
            && !_delegationControlInFlight
            && !HasPendingApproval
            && !HasPendingImage
            && !HasActiveSubagents
            && !IsVoiceCaptureBusy;

    private bool TaskMutationDispatchScopeStillValid(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        var task = SelectedTask;
        return _commandInFlight
            && _isConnected
            && !_sequenceGapDetected
            && !_projectionFault
            && !HasPendingApproval
            && _lastObservedSequence == lastObservedSequence
            && task is not null
            && task.CanManage
            && string.Equals(_runtimeGeneration, runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(_activeTaskId, taskId, StringComparison.Ordinal)
            && string.Equals(task.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(task.SessionId, sessionId, StringComparison.Ordinal);
    }

    private bool CompressionDispatchScopeStillValid(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        long lastObservedSequence)
    {
        var task = SelectedTask;
        return _commandInFlight
            && !IsRollbackOperationInFlight
            && !_observabilityInFlight
            && _isConnected
            && _nativeSurfaceAttested
            && !_sequenceGapDetected
            && !_projectionFault
            && !HasPendingApproval
            && !IsVoiceCaptureBusy
            && _lastObservedSequence == lastObservedSequence
            && task is not null
            && task.State == D046HermesTaskState.Idle
            && string.Equals(_runtimeGeneration, runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(_activeTaskId, taskId, StringComparison.Ordinal)
            && string.Equals(task.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(task.SessionId, sessionId, StringComparison.Ordinal);
    }

    private static bool TaskMutationReceiptMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        D046HermesRenameTaskRequest request)
        => string.Equals(runtimeGeneration, request.RuntimeGeneration, StringComparison.Ordinal)
            && string.Equals(taskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(sessionId, request.SessionId, StringComparison.Ordinal);

    private void ApplyTaskPinSnapshot(D046HermesWorkbenchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        D046HermesWorkbenchSnapshotApprovalValidator.Validate(snapshot);
        ValidateSnapshotPresentationScope(snapshot);
        if (!string.Equals(
                snapshot.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            || snapshot.ThroughSequence < _lastObservedSequence
            || !string.Equals(
                snapshot.SelectedTaskId,
                _activeTaskId,
                StringComparison.Ordinal)
            || snapshot.Tasks.Count != Tasks.Count)
        {
            throw new InvalidDataException(
                "The local task-pin snapshot escaped the active presentation scope.");
        }

        var existing = Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        if (snapshot.Tasks.Any(projection =>
                !existing.TryGetValue(projection.TaskId, out var task)
                || !string.Equals(
                    projection.LineageId,
                    task.LineageId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    projection.SessionId,
                    task.SessionId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "置頂清單中的聊天識別不一致。");
        }

        BeginTaskRailMutation();
        try
        {
            var authoritativeOrder = new List<D046HermesTaskItemViewModel>(
                snapshot.Tasks.Count);
            foreach (var projection in snapshot.Tasks)
            {
                var task = existing[projection.TaskId];
                task.ApplyPinState(projection.IsPinned);
                authoritativeOrder.Add(task);
            }
            ApplySnapshotOrder(authoritativeOrder);
        }
        finally
        {
            EndTaskRailMutation();
        }
    }

    private void ApplySnapshot(D046HermesWorkbenchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_transportFaultStatus is not null)
            throw new InvalidOperationException("A closed projection requires a fresh host.");
        if (string.IsNullOrWhiteSpace(snapshot.RuntimeGeneration)
            || snapshot.ThroughSequence < 0
            || snapshot.Tasks is null
            || snapshot.SelectedSessionEvents is null
            || snapshot.SelectedPendingApprovals is null)
        {
            throw new InvalidDataException("聊天資料未通過驗證。");
        }
        D046HermesWorkbenchSnapshotApprovalValidator.Validate(snapshot);
        ValidateSnapshotPresentationScope(snapshot);
        if (string.Equals(
                snapshot.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && snapshot.ThroughSequence < _lastObservedSequence)
        {
            throw new InvalidDataException(
                "收到的聊天資料早於目前狀態。");
        }

        _confirmedDeletionNeedsRecovery = false;
        var previousRuntimeGeneration = _runtimeGeneration;
        var previousActiveTaskId = _activeTaskId;
        var previousSessionId = SelectedTask?.SessionId ?? string.Empty;
        if (!string.IsNullOrEmpty(previousRuntimeGeneration)
            && !string.Equals(
                previousRuntimeGeneration,
                snapshot.RuntimeGeneration,
                StringComparison.Ordinal))
        {
            ConversationScopeChanging?.Invoke(this, EventArgs.Empty);
        }
        ResetTurnSettlementStatusCorrelation();
        _isApplyingSnapshot = true;
        BeginTaskRailMutation();
        BeginTimelineMutation();
        try
        {
            _runtimeGeneration = snapshot.RuntimeGeneration;
            _isConnected = snapshot.IsConnected;
            _activeSessionConfigStale = snapshot.NativeToolsConfigStale;
            _nativeSurfaceAttested = snapshot.NativeSurfaceAttested;
            _credentialRecoveryAvailable = snapshot.CredentialRecoveryAvailable;
            _computerUseEnabled = snapshot.ComputerUseEnabled;
            ClearPendingApprovals(ControlCenterLocalization.Current.Get("state.no.pending.task.approval.47"));
            ClearPendingClarification();
            ClearPendingSecureInput();
            _sequenceGapDetected = false;
            _projectionFault = false;
            _modelSelectionResultStatus = null;
            _statusText = string.IsNullOrWhiteSpace(snapshot.StatusText)
                ? snapshot.IsConnected ? "背景服務已連線" : "背景服務未連線"
                : snapshot.StatusText;

            // Retain only visual option rows, never catalog authority. A fresh
            // session-bound catalog must attest before model controls enable.
            ClearModelCatalog(retainDisplayItems: true);
            ClearNativeToolsCatalog();

            ReconcileTasks(snapshot.Tasks, snapshot.ThroughSequence);

            var selected = string.IsNullOrEmpty(snapshot.SelectedTaskId)
                ? null
                : Tasks.Single(value => string.Equals(
                    value.TaskId,
                    snapshot.SelectedTaskId,
                    StringComparison.Ordinal));
            SelectedTask = selected;
            _activeTaskId = selected?.TaskId ?? string.Empty;
            if (!string.Equals(
                    previousRuntimeGeneration,
                    _runtimeGeneration,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousActiveTaskId,
                    _activeTaskId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousSessionId,
                    selected?.SessionId ?? string.Empty,
                    StringComparison.Ordinal))
            {
                ResetDelegationDisplay();
                ResetRollbackDisplay();
                InvalidateCapabilityCatalog(clear: !string.Equals(
                    previousRuntimeGeneration, _runtimeGeneration, StringComparison.Ordinal));
            }
            if (_pendingImage is not null
                && (selected is null
                    || !_pendingImage.IsOwnedBy(
                        _runtimeGeneration,
                        selected.TaskId,
                        selected.SessionId)))
            {
                ClearPendingImage("工作階段已變更，請重新加入圖片。");
            }
            if (!string.Equals(
                    previousActiveTaskId,
                    _activeTaskId,
                    StringComparison.Ordinal))
            {
                _compressionStatusText = ControlCenterLocalization.Current.Get("state.no.native.context.compression.yet.40");
            }

            _turnPresentation.ResetScope(
                _runtimeGeneration,
                selected?.TaskId ?? string.Empty,
                selected?.SessionId ?? string.Empty);
            _receivedEvents.Clear();
            foreach (var value in snapshot.SelectedSessionEvents)
            {
                _receivedEvents.Add(value);
                if (!IsValidEnvelope(value)
                    || !string.Equals(
                        value.RuntimeGeneration,
                        _runtimeGeneration,
                        StringComparison.Ordinal)
                    || selected is null
                    || !string.Equals(
                        value.TaskId,
                        selected.TaskId,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        value.SessionId,
                        selected.SessionId,
                        StringComparison.Ordinal))
                {
                    _projectionFault = true;
                    continue;
                }
                ProjectEvent(value, applyLiveState: false);
            }
            RestorePendingApprovals(snapshot.SelectedPendingApprovals);
            if (snapshot.SelectedPendingClarification is { } clarification)
                SetPendingClarification(clarification);
            if (snapshot.SelectedPendingSecureInput is { } secureInput)
                SetPendingSecureInput(secureInput);
            RestoreFaultRecoveryDraftIfAuthoritative(selected);

            _lastObservedSequence = snapshot.ThroughSequence;
        }
        finally
        {
            EndTimelineMutation();
            EndTaskRailMutation();
            _isApplyingSnapshot = false;
        }

        RaiseProjectionProperties();
    }

    private void RestoreFaultRecoveryDraftIfAuthoritative(
        D046HermesTaskItemViewModel? selected)
    {
        if (!_faultRecoveryDrafts.HasPending || _projectionFault)
            return;

        var disposition = _faultRecoveryDrafts.TryRestore(
            selected is not null
                && string.Equals(
                    selected.TaskId,
                    _activeTaskId,
                    StringComparison.Ordinal)
                    ? selected.TaskId
                    : null,
            selected?.LineageId,
            _draftText,
            out var restoredDraftText,
            out var hadPendingImage);
        if (disposition
            == D046HermesFaultRecoveryRestoreDisposition.WaitingForDurableTask)
        {
            _statusText =
                "故障前草稿仍綁定原聊天；切回原聊天後才會恢復。"
                + "上一個送出結果未確認，草稿未自動重送。";
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        if (disposition
            == D046HermesFaultRecoveryRestoreDisposition.DestinationConflict)
        {
            _statusText =
                "目前輸入已有內容；故障前草稿未覆寫目前輸入，也未自動重送。";
            OnPropertyChanged(nameof(StatusText));
            return;
        }
        if (disposition != D046HermesFaultRecoveryRestoreDisposition.Restored)
            return;

        DraftText = restoredDraftText;
        _statusText =
            "上一個送出結果未確認，草稿未自動重送；請先確認聊天紀錄再決定是否送出。"
            + (hadPendingImage
                ? " 原先加入的圖片未跨連線保留，請重新附上。"
                : string.Empty);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
    }

    private static void ValidateSnapshotPresentationScope(
        D046HermesWorkbenchSnapshot snapshot)
    {
        if (snapshot.Tasks.Any(task => task is null
                || string.IsNullOrWhiteSpace(task.TaskId))
            || snapshot.Tasks.Select(task => task.TaskId)
                .Distinct(StringComparer.Ordinal)
                .Count() != snapshot.Tasks.Count)
        {
            throw new InvalidDataException(
                "聊天資料包含無效或重複的識別。");
        }

        var selected = string.IsNullOrWhiteSpace(snapshot.SelectedTaskId)
            ? null
            : snapshot.Tasks.SingleOrDefault(task => string.Equals(
                task.TaskId,
                snapshot.SelectedTaskId,
                StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(snapshot.SelectedTaskId) && selected is null)
            throw new InvalidDataException("聊天資料選取了不存在的聊天。");
        if (selected is null && snapshot.SelectedSessionEvents.Count != 0)
            throw new InvalidDataException("聊天紀錄缺少對應的聊天。");

        long priorSequence = 0;
        foreach (var value in snapshot.SelectedSessionEvents)
        {
            if (!IsValidEnvelope(value)
                || value.Sequence <= priorSequence
                || value.Sequence > snapshot.ThroughSequence
                || selected is null
                || !string.Equals(
                    value.RuntimeGeneration,
                    snapshot.RuntimeGeneration,
                    StringComparison.Ordinal)
                || !string.Equals(value.TaskId, selected.TaskId, StringComparison.Ordinal)
                || !string.Equals(
                    value.SessionId,
                    selected.SessionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "收到的紀錄不屬於目前選取的聊天。");
            }
            priorSequence = value.Sequence;
        }
    }

    private void ReconcileTasks(
        IReadOnlyList<D046HermesTaskProjection> projections,
        long throughSequence)
    {
        if (projections.Any(projection => projection is null
                || string.IsNullOrWhiteSpace(projection.TaskId))
            || projections.Select(projection => projection.TaskId)
                .Distinct(StringComparer.Ordinal)
                .Count() != projections.Count)
        {
            throw new InvalidDataException(
                "聊天清單包含無效或重複的識別。");
        }

        var incoming = projections.ToDictionary(
            projection => projection.TaskId,
            StringComparer.Ordinal);
        for (var index = Tasks.Count - 1; index >= 0; index--)
        {
            if (!incoming.ContainsKey(Tasks[index].TaskId))
                Tasks.RemoveAt(index);
        }

        var existing = Tasks.ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var authoritativeOrder = new List<D046HermesTaskItemViewModel>(projections.Count);
        foreach (var projection in projections)
        {
            if (existing.TryGetValue(projection.TaskId, out var task))
            {
                _ = task.ApplyProjection(projection, throughSequence);
            }
            else
            {
                task = new D046HermesTaskItemViewModel(projection, throughSequence);
                Tasks.Add(task);
                existing.Add(task.TaskId, task);
            }
            authoritativeOrder.Add(task);
        }
        ApplySnapshotOrder(authoritativeOrder);
    }

    private void ApplySnapshotOrder(
        IReadOnlyList<D046HermesTaskItemViewModel> authoritativeOrder)
    {
        for (var targetIndex = 0; targetIndex < authoritativeOrder.Count; targetIndex++)
        {
            var currentIndex = Tasks.IndexOf(authoritativeOrder[targetIndex]);
            if (currentIndex != targetIndex)
                Tasks.Move(currentIndex, targetIndex);
        }
    }

    private void PromoteLiveTask(D046HermesTaskItemViewModel task)
    {
        var currentIndex = Tasks.IndexOf(task);
        if (currentIndex < 0)
            return;
        var targetIndex = 0;
        foreach (var candidate in Tasks)
        {
            if (!ReferenceEquals(candidate, task)
                && TaskRailComesBefore(candidate, task))
            {
                targetIndex++;
            }
        }
        if (currentIndex != targetIndex)
            Tasks.Move(currentIndex, targetIndex);
    }

    private static bool TaskRailComesBefore(
        D046HermesTaskItemViewModel left,
        D046HermesTaskItemViewModel right)
    {
        if (left.IsPinned != right.IsPinned)
            return left.IsPinned;
        var activity = left.UpdatedUtc.CompareTo(right.UpdatedUtc);
        if (activity != 0)
            return activity > 0;
        var activitySequence = left.LastConversationActivitySequence.CompareTo(
            right.LastConversationActivitySequence);
        if (activitySequence != 0)
            return activitySequence > 0;
        return string.Compare(
            left.TaskId,
            right.TaskId,
            StringComparison.Ordinal) < 0;
    }

    private void BeginTimelineMutation()
    {
        if (_timelineMutationDepth++ == 0)
            TimelineChanging?.Invoke(this, EventArgs.Empty);
    }

    private void EndTimelineMutation()
    {
        if (_timelineMutationDepth <= 0)
            throw new InvalidOperationException("Timeline mutation scope was not balanced.");
        if (--_timelineMutationDepth == 0)
        {
            RaiseTimelineScopeProperties();
            TimelineChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void BeginTaskRailMutation()
    {
        if (_taskRailMutationDepth++ == 0)
            TaskRailChanging?.Invoke(this, EventArgs.Empty);
    }

    private void EndTaskRailMutation()
    {
        if (_taskRailMutationDepth <= 0)
            throw new InvalidOperationException("Task rail mutation scope was not balanced.");
        if (--_taskRailMutationDepth == 0)
            TaskRailChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadModelOptionsAsync()
    {
        var task = SelectedTask;
        if (task is null || string.IsNullOrWhiteSpace(task.SessionId))
        {
            ClearModelCatalog();
            return;
        }

        var catalog = await _projection.GetModelOptionsAsync(
            task.TaskId,
            task.SessionId,
            _lifetime.Token);
        if (catalog is null
            || !string.Equals(
                catalog.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            || !string.Equals(catalog.TaskId, task.TaskId, StringComparison.Ordinal)
            || !string.Equals(catalog.SessionId, task.SessionId, StringComparison.Ordinal)
            || catalog.Options is null)
        {
            throw new InvalidDataException("模型目錄已過期或格式不正確。");
        }

        _isApplyingModelSelection = true;
        try
        {
            // Keep the selected item/container stable when the verified catalog
            // has not changed; session scope is still rebound below on every read.
            if (!ModelOptions.SequenceEqual(catalog.Options))
            {
                ModelOptions.Clear();
                foreach (var option in catalog.Options)
                    ModelOptions.Add(option);
            }
            _modelCatalogTaskId = catalog.TaskId;
            _modelCatalogSessionId = catalog.SessionId;
            _modelCatalogReady = true;
            SynchronizeModelSelectionFromSessionInfo();
        }
        finally
        {
            _isApplyingModelSelection = false;
        }
        OnPropertyChanged(nameof(CanSelectModel));
    }

    private void ClearModelCatalog(bool retainDisplayItems = false)
    {
        _isApplyingModelSelection = true;
        try
        {
            _modelCatalogReady = false;
            _preSessionModelSelectionRequired = false;
            _modelCatalogTaskId = string.Empty;
            _modelCatalogSessionId = string.Empty;
            if (!retainDisplayItems)
                ModelOptions.Clear();
            SelectedModelOption = null;
        }
        finally
        {
            _isApplyingModelSelection = false;
        }
        OnPropertyChanged(nameof(CanSelectModel));
    }

    private void ApplySessionPreflight(
        D046HermesSessionPreflightException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var catalog = exception.Catalog;
        if (!string.Equals(
                catalog.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            || catalog.TaskId.Length != 0
            || catalog.SessionId.Length != 0
            || catalog.Options is null)
        {
            SetProjectionFault("模型檢查回覆與目前連線不一致；結果未採用。");
            return;
        }

        if (exception.Requirement
            == D046HermesSessionPreflightRequirement.ModelDiscoveryUnavailable)
        {
            if (catalog.Options.Count != 0 || catalog.HasExactSelection)
            {
                SetProjectionFault("模型檢查回覆包含矛盾資料；結果未採用。");
                return;
            }
            if (SelectedTask is null
                && _modelCatalogTaskId.Length == 0
                && _modelCatalogSessionId.Length == 0)
            {
                ClearModelCatalog();
            }
            _statusText = "暫時無法讀取可用模型；請稍後再建立新聊天。";
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(CanCreateTask));
            OnPropertyChanged(nameof(CanSend));
            return;
        }

        if (exception.Requirement
            == D046HermesSessionPreflightRequirement.ModelSelectionRequired)
        {
            // A pre-session catalog is not owned by the previously active
            // task. Detach that historical selection before exposing the
            // catalog; otherwise CanSelectModel correctly sees a selected
            // task and the required new-session model picker deadlocks itself.
            // This is an authoritative Runtime preflight transition delivered
            // while the snapshot command gate is still held, so temporarily
            // use the same guarded mutation path as an adopted snapshot. A
            // normal rail selection still cannot bypass CanSwitchTask.
            var wasApplyingSnapshot = _isApplyingSnapshot;
            _isApplyingSnapshot = true;
            try
            {
                SelectedTask = null;
            }
            finally
            {
                _isApplyingSnapshot = wasApplyingSnapshot;
            }
        }

        _isApplyingModelSelection = true;
        try
        {
            ModelOptions.Clear();
            foreach (var option in catalog.Options)
                ModelOptions.Add(option);
            _modelCatalogTaskId = string.Empty;
            _modelCatalogSessionId = string.Empty;
            _modelCatalogReady = true;
            _preSessionModelSelectionRequired =
                exception.Requirement
                    == D046HermesSessionPreflightRequirement.ModelSelectionRequired;
            SelectedModelOption = null;
        }
        finally
        {
            _isApplyingModelSelection = false;
        }

        _statusText = exception.Requirement switch
        {
            D046HermesSessionPreflightRequirement.ModelSelectionRequired
                => "請先選擇要使用的模型，再建立聊天。",
            D046HermesSessionPreflightRequirement.CredentialRequired
                => "尚未設定可用的模型 API Key；請先在控制中心新增。",
            _ => throw new InvalidDataException("模型檢查回覆的格式不正確。")
        };
        OnPropertyChanged(nameof(ModelDisplayName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanSelectModel));
    }

    internal Task<bool> RefreshNativeToolsAsync()
        => RefreshNativeToolsCoreAsync(allowCommandInFlight: false);

    internal Task<bool> ApplyNativeToolsAsync() => ApplyNativeToolsCoreAsync(null);

    internal Task<bool> ApplyNativeSetupAsync(D046HermesNativeToolsetViewModel row)
    {
        if (!NativeToolsets.Contains(row) || !row.CanSelectSetupProvider
            || row.SelectedSetupProvider is not { } provider || _nativeToolsDirty)
            return Task.FromResult(false);
        return ApplyNativeToolsCoreAsync(new HermesNativeSetupSelection(row.Name, provider.Name));
    }

    internal Task<bool> ApplyNativeMcpSetupAsync(D046HermesNativeMcpViewModel row)
    {
        if (!NativeMcpCatalog.Contains(row) || !row.CanApply || _nativeToolsDirty)
            return Task.FromResult(false);
        return ApplyNativeToolsCoreAsync(null,
            new HermesNativeMcpSetupSelection(row.Source.Name, row.Source.CatalogSha256, row.DesiredEnabled));
    }

    private async Task<bool> ApplyNativeToolsCoreAsync(HermesNativeSetupSelection? setup,
        HermesNativeMcpSetupSelection? mcpSetup = null)
    {
        if (!(setup is null && mcpSetup is null ? CanApplyNativeTools : CanEditNativeTools && _nativeToolsCatalogReady && !_nativeToolsDirty)
            || SelectedTask is null
            || SelectedNativeApprovalMode is null)
        {
            return false;
        }

        var frozenTask = SelectedTask;
        var request = new D046HermesNativeToolsSetRequest(
            Guid.NewGuid().ToString("N"),
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId,
            _lastObservedSequence,
            NativeToolsets
                .Where(row => row.DesiredEnabled)
                .Select(row => row.Name)
                .ToArray(),
            SelectedNativeApprovalMode.Value,
            setup,
            mcpSetup);
        _nativeToolsInFlight = true;
        _nativeToolsStatusText = "正在保存工具與授權設定…";
        RaiseNativeToolsProperties();
        SetCommandState(true);
        try
        {
            var receipt = await _projection.SetNativeToolsAsync(
                request,
                _lifetime.Token);
            if (!NativeToolsSetReceiptMatches(receipt, request))
                throw new InvalidDataException("工具與授權設定回覆不一致。");
            if (!receipt.Accepted)
            {
                _nativeToolsStatusText = "設定未變更（" + SafeCode(receipt.Code)
                    + "）；請重新整理後再決定是否重試。";
                RaiseNativeToolsProperties();
                return false;
            }

            _activeSessionConfigStale = true;
            _nativeSurfaceAttested = false;
            InvalidateCapabilityCatalog();
            if (HasPendingImage)
            {
                ClearPendingImage(
                    "工具與授權設定已變更；請在新的工作階段重新加入圖片。");
            }
            ApplyNativeToolsCatalog(receipt.Catalog);
            var savedStatus = "設定已保存 · " + Compact(receipt.ConfigSha256)
                + "；新工作階段生效。";
            try
            {
                var refreshed = await _projection.GetNativeToolsCatalogAsync(
                    frozenTask.TaskId,
                    frozenTask.SessionId,
                    _lifetime.Token);
                if (!NativeToolsScopeMatches(
                        refreshed.RuntimeGeneration,
                        refreshed.TaskId,
                        refreshed.SessionId))
                {
                    throw new InvalidDataException(
                        "工具設定重新讀取時工作階段已變更。");
                }
                ApplyNativeToolsCatalog(refreshed);
                if (setup is not null && !refreshed.Toolsets.Any(row => row.Name == setup.Toolset
                    && row.SetupProviders?.Any(p => p.Name == setup.Provider && p.Selected) == true))
                    throw new InvalidDataException("保存後讀回的供應商與選擇不一致。");
                if (mcpSetup is not null && !HermesNativeToolsContract.McpSetupMatchesCatalog(
                    mcpSetup, refreshed.McpCatalog, requireRegistered: true))
                    throw new InvalidDataException("保存後讀回的 MCP 設定與選擇不一致。");
                _nativeToolsStatusText = savedStatus;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                return true;
            }
            catch (Exception exception)
            {
                _nativeToolsStatusText = savedStatus
                    + " 但無法確認重新啟動後的工具設定。";
                SetProjectionFault(
                    ProjectionFailureStatus("工具設定後重新讀取", exception)
                    + " · 為避免在不確定狀態繼續操作，聊天已停止；請重新連線。");
                return false;
            }
            _nativeToolsActivationText =
                "設定已保存，將在新聊天生效；目前工作保持不變。";
            RaiseNativeToolsProperties();
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _nativeToolsStatusText = ProjectionFailureStatus("工具與授權設定", exception);
            SetProjectionFault(
                _nativeToolsStatusText
                + " · 設定是否已套用無法安全確認，聊天已停止；請重新連線。");
            return false;
        }
        finally
        {
            _nativeToolsInFlight = false;
            SetCommandState(false);
            SetNativeToolRowControls();
            RaiseNativeToolsProperties();
        }
    }

    internal void CancelNativeToolsChanges()
    {
        if (!CanCancelNativeTools)
            return;
        _isApplyingNativeToolsCatalog = true;
        try
        {
            foreach (var row in NativeToolsets)
                row.ResetDesired();
            SelectedNativeApprovalMode = NativeApprovalModes.First(value =>
                value.Value == _savedNativeApprovalMode);
            _nativeToolsDirty = false;
            _nativeToolsStatusText = "尚未儲存的工具與授權變更已取消。";
        }
        finally
        {
            _isApplyingNativeToolsCatalog = false;
        }
        RaiseNativeToolsProperties();
    }

    private async Task<bool> RefreshNativeToolsCoreAsync(bool allowCommandInFlight)
    {
        var task = SelectedTask;
        if (task is null
            || string.IsNullOrWhiteSpace(task.SessionId)
            || _nativeToolsInFlight
            || (!allowCommandInFlight && !CanRefreshNativeTools))
        {
            return false;
        }

        _nativeToolsInFlight = true;
        _nativeToolsStatusText = "正在讀取工具與授權設定…";
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
        try
        {
            var catalog = await _projection.GetNativeToolsCatalogAsync(
                task.TaskId,
                task.SessionId,
                _lifetime.Token);
            if (!NativeToolsScopeMatches(
                    catalog.RuntimeGeneration,
                    catalog.TaskId,
                    catalog.SessionId))
            {
                throw new InvalidDataException("工具設定與目前工作階段不一致。");
            }
            ApplyNativeToolsCatalog(catalog);
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            ClearNativeToolsCatalog();
            _nativeToolsStatusText = ProjectionFailureStatus("工具設定", exception);
            RaiseNativeToolsProperties();
            return false;
        }
        finally
        {
            _nativeToolsInFlight = false;
            SetNativeToolRowControls();
            RaiseNativeToolsProperties();
        }
    }

    private void ApplyNativeToolsCatalog(D046HermesNativeToolsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var activeSessionConfigStale = _activeSessionConfigStale;
        if (catalog.NewSessionRequired && !_nativeSurfaceAttested)
            activeSessionConfigStale = true;
        _activeSessionConfigStale = activeSessionConfigStale;
        _isApplyingNativeToolsCatalog = true;
        try
        {
            NativeToolsets.Clear();
            foreach (var row in catalog.Toolsets)
            {
                NativeToolsets.Add(new D046HermesNativeToolsetViewModel(
                    row,
                    MarkNativeToolsDirty));
            }
            NativeMcpCatalog.Clear();
            foreach (var row in catalog.McpCatalog ?? Array.Empty<HermesNativeMcpCatalogEntry>())
                NativeMcpCatalog.Add(new D046HermesNativeMcpViewModel(row));
            _savedNativeApprovalMode = catalog.ApprovalMode;
            SelectedNativeApprovalMode = NativeApprovalModes.First(value =>
                value.Value == catalog.ApprovalMode);
            _nativeToolsCatalogTaskId = catalog.TaskId;
            _nativeToolsCatalogSessionId = catalog.SessionId;
            _nativeToolsCatalogReady = true;
            _nativeToolsDirty = false;
            var ready = catalog.Toolsets.Count(row => row.Enabled && row.Configured);
            var waiting = catalog.Toolsets.Count(row => row.Enabled && !row.Configured);
            _nativeToolsStatusText = catalog.EnabledToolsets.Count
                + " / " + catalog.Toolsets.Count + " 已選取 · "
                + ready + " 已就緒"
                + (waiting == 0 ? string.Empty : " · " + waiting + " 尚需設定");
            _nativeToolsActivationText = _activeSessionConfigStale
                ? "工具與授權設定已保存。這段聊天仍為唯讀；請建立新聊天。"
                : catalog.NewSessionRequired
                    ? "設定已保存，將在新聊天生效；目前工作保持不變。"
                    : "目前沒有等待套用的工具變更。";
        }
        finally
        {
            _isApplyingNativeToolsCatalog = false;
        }
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
    }

    private void ClearNativeToolsCatalog()
    {
        _isApplyingNativeToolsCatalog = true;
        try
        {
            _nativeToolsCatalogReady = false;
            _nativeToolsDirty = false;
            _nativeToolsCatalogTaskId = string.Empty;
            _nativeToolsCatalogSessionId = string.Empty;
            NativeToolsets.Clear();
            NativeMcpCatalog.Clear();
            SelectedNativeApprovalMode = null;
            _nativeToolsStatusText = "工具清單尚未載入；開啟「工具與授權」時會讀取目前設定。";
            _nativeToolsActivationText = ControlCenterLocalization.Current.Get("state.settings.apply.only.to.new.sessions.49");
        }
        finally
        {
            _isApplyingNativeToolsCatalog = false;
        }
        RaiseNativeToolsProperties();
    }

    private void MarkNativeToolsDirty()
    {
        if (!_nativeToolsCatalogReady || _isApplyingNativeToolsCatalog)
            return;
        _nativeToolsDirty = true;
        _nativeToolsStatusText = "有尚未儲存的工具與授權變更。";
        RaiseNativeToolsProperties();
    }

    private bool CanUseNativeToolsScope()
        => !_stopInFlight
            && !_commandInFlight
            && !_taskPinInFlight
            && !IsRollbackOperationInFlight
            && !_observabilityInFlight
            && _isConnected
            && _nativeSurfaceAttested
            && !_sequenceGapDetected
            && !_projectionFault
            && SelectedTask is not null
            && SelectedTask.State is D046HermesTaskState.Idle
                or D046HermesTaskState.Failed
            && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);

    private bool CanUseConfigStaleNativeToolsReadScope()
        => !_stopInFlight
            && !_commandInFlight
            && !_taskPinInFlight
            && !IsRollbackOperationInFlight
            && !_observabilityInFlight
            && _isConnected
            && _activeSessionConfigStale
            && !_sequenceGapDetected
            && !_projectionFault
            && SelectedTask is not null
            && SelectedTask.State is D046HermesTaskState.Idle
                or D046HermesTaskState.Failed
            && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);

    private bool NativeToolsScopeMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(runtimeGeneration, _runtimeGeneration, StringComparison.Ordinal)
            && SelectedTask is not null
            && string.Equals(taskId, SelectedTask.TaskId, StringComparison.Ordinal)
            && string.Equals(sessionId, SelectedTask.SessionId, StringComparison.Ordinal);

    private static bool NativeToolsSetReceiptMatches(
        D046HermesNativeToolsSetReceipt receipt,
        D046HermesNativeToolsSetRequest request)
        => receipt is not null
            && receipt.Catalog is not null
            && string.Equals(
                receipt.Catalog.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.Catalog.TaskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(receipt.Catalog.SessionId, request.SessionId,
                StringComparison.Ordinal);

    private void SetNativeToolRowControls()
    {
        var enabled = _nativeToolsCatalogReady
            && !_nativeToolsInFlight
            && CanUseNativeToolsScope();
        foreach (var row in NativeToolsets)
            row.SetControlsEnabled(enabled, !_nativeToolsDirty);
        foreach (var row in NativeMcpCatalog)
            row.SetControlsEnabled(enabled && !_nativeToolsDirty);
    }

    private void RaiseNativeToolsProperties()
    {
        SetNativeToolRowControls();
        RaiseChatApprovalProperties();
        OnPropertyChanged(nameof(EffectToolsStatusText));
        OnPropertyChanged(nameof(NativeToolsStatusText));
        OnPropertyChanged(nameof(NativeToolsActivationText));
        OnPropertyChanged(nameof(HasNativeToolsAttention));
        OnPropertyChanged(nameof(IsNativeToolsEmpty));
        OnPropertyChanged(nameof(HasNativeMcpCatalog));
        OnPropertyChanged(nameof(CanRefreshNativeTools));
        OnPropertyChanged(nameof(CanEditNativeTools));
        OnPropertyChanged(nameof(CanApplyNativeTools));
        OnPropertyChanged(nameof(CanCancelNativeTools));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
    }

    private void SynchronizeModelSelectionFromSessionInfo()
    {
        var prior = _isApplyingModelSelection;
        _isApplyingModelSelection = true;
        try
        {
            SelectedModelOption = SelectedTask is null
                ? null
                : ModelOptions.FirstOrDefault(option =>
                    string.Equals(
                        option.ProviderId,
                        SelectedTask.ProviderId,
                        StringComparison.Ordinal)
                    && string.Equals(
                        option.ModelId,
                        SelectedTask.ModelDisplayName,
                        StringComparison.Ordinal));
        }
        finally
        {
            _isApplyingModelSelection = prior;
        }
    }

    private void ProjectEvent(D046HermesProjectionEvent value)
        => ProjectEvent(value, applyLiveState: true);

    private void ProjectEvent(
        D046HermesProjectionEvent value,
        bool applyLiveState)
    {
        var task = Tasks.FirstOrDefault(candidate => string.Equals(
            candidate.TaskId,
            value.TaskId,
            StringComparison.Ordinal));
        var targetsActiveTask = string.Equals(
            value.TaskId,
            _activeTaskId,
            StringComparison.Ordinal);
        if ((task is not null
                && !string.Equals(
                    value.SessionId,
                    task.SessionId,
                    StringComparison.Ordinal)
                && !(string.IsNullOrWhiteSpace(task.SessionId)
                    && value.Payload is D046HermesSessionPayload))
            || (targetsActiveTask
                && (SelectedTask is null
                    || !string.Equals(
                        value.SessionId,
                        SelectedTask.SessionId,
                        StringComparison.Ordinal))))
        {
            RejectPresentationEvent(
                applyLiveState,
                "收到不屬於目前聊天範圍的事件；內容未合併。");
            return;
        }
        var stopTerminalObserved = false;
        if (applyLiveState && task is not null)
        {
            BeginTaskRailMutation();
            try
            {
                var wasStopping = task.State == D046HermesTaskState.Stopping;
                if (task.ApplyLive(
                        value.Sequence,
                        value.OccurredUtc,
                        value.Payload,
                        value.EffectiveTaskState))
                {
                    stopTerminalObserved = wasStopping
                        && IsAuthoritativeStopTerminal(value, task.State);
                    switch (value.Payload)
                    {
                        case D046HermesSessionPayload session:
                            var previousSessionId = task.SessionId;
                            task.ApplySession(value.SessionId, session);
                            if (!string.Equals(
                                    previousSessionId,
                                    task.SessionId,
                                    StringComparison.Ordinal))
                            {
                                ResetTurnSettlementStatusCorrelation();
                            }
                            if (targetsActiveTask
                                && session.TaskState == D046HermesTaskState.Idle
                                && !_approvalResponseInFlight
                                && HasPendingApproval)
                            {
                                ClearPendingApprovals(
                                    "工作已結束；未回覆的工作授權已過期，沒有執行。");
                            }
                            if (targetsActiveTask
                                && session.TaskState == D046HermesTaskState.Idle)
                            {
                                ClearPendingClarification();
                                ClearPendingSecureInput();
                            }
                            if (string.Equals(
                                    task.TaskId,
                                    _activeTaskId,
                                    StringComparison.Ordinal))
                            {
                                SynchronizeModelSelectionFromSessionInfo();
                            }
                            break;
                        case D046HermesStatusPayload status:
                            task.ApplyStatus(status);
                            break;
                    }
                    if (D052HermesConversationActivityPolicy
                        .TryGetConversationActivityUtc(value.Payload, out _))
                    {
                        PromoteLiveTask(task);
                    }
                }
            }
            finally
            {
                EndTaskRailMutation();
            }
        }

        if (!targetsActiveTask)
            return;
        if (SelectedTask is null
            || !string.Equals(
                value.SessionId,
                SelectedTask.SessionId,
                StringComparison.Ordinal)
            || !_turnPresentation.ScopeMatches(
                value.RuntimeGeneration,
                value.TaskId,
                value.SessionId)
            || !ValidatePresentationScope(value, applyLiveState))
        {
            RejectPresentationEvent(
                applyLiveState,
                "收到不屬於目前聊天範圍的事件；內容未合併。");
            return;
        }

        BeginTimelineMutation();
        try
        {
            if (stopTerminalObserved)
            {
                ProtectTurnSettlementStatus();
                task!.ClearEmergencyStop();
                _statusText = "工作已停止；可以繼續輸入。";
                OnPropertyChanged(nameof(StatusText));
            }
            switch (value.Payload)
            {
                case D046HermesSessionPayload session:
                    AddEventCard(
                        value,
                        "event:" + value.EventId,
                        "黑色筆記本",
                        SessionChangeText(session.Change),
                        session.Title + " · " + session.ModelDisplayName);
                    if (applyLiveState && task is not null)
                        ObserveSettledIdleSession(value, task);
                    break;
                case D046HermesMessagePayload message:
                    ApplyMessage(value, message, applyLiveState);
                    break;
                case D046HermesBudgetConstraintsPayload constraints:
                    AddBudgetConstraints(value, constraints.Constraints);
                    break;
                case D046HermesCronCompletionPayload cronCompletion:
                    AddCronCompletionCard(value, cronCompletion);
                    break;
                case D046HermesReasoningPayload reasoning:
                    ApplyReasoning(value, reasoning);
                    break;
                case D046HermesStatusPayload status:
                    var statusText = string.IsNullOrWhiteSpace(status.DisplayText)
                        ? SafeCode(status.StatusCode)
                        : status.DisplayText;
                    if (applyLiveState)
                    {
                        if (task?.State == D046HermesTaskState.Failed)
                            ProtectTurnSettlementStatus();
                        _isConnected = status.IsConnected;
                        _nativeSurfaceAttested = !_activeSessionConfigStale
                            && status.NativeSurfaceAttested;
                        _computerUseEnabled = status.ComputerUseEnabled;
                        _statusText = statusText;
                    }
                    AddEventCard(
                        value,
                        "event:" + value.EventId,
                        "鐘錶報告",
                        statusText,
                        SafeCode(status.StatusCode));
                    if (applyLiveState)
                        RaiseProjectionProperties();
                    break;
                case D046HermesToolPayload tool:
                    AddEventCard(
                        value,
                        "tool:" + tool.ToolCallId,
                        "逐夢之典 · " + tool.ToolDisplayName,
                        tool.Summary,
                        ToolPhaseText(tool.Phase)
                            + (tool.IsHostEffectTool
                                ? " · 可能影響本機（依授權方式處理）"
                                : " · 工具活動"));
                    if (!applyLiveState && tool.ClarificationAnswer is { } savedAnswer)
                        _turnPresentation.ShowAcceptedClarificationAnswer(
                            value.EventId, "history:" + tool.ToolCallId, savedAnswer.Answer, value.OccurredUtc);
                    if (tool.Phase == D046HermesToolPhase.Completed && tool.GeneratedImage is { } generatedImage
                        && value.ScopeKind == D046HermesEventScopeKind.Turn)
                        _turnPresentation.UpsertGeneratedImage(value.TurnId, tool.ToolCallId, generatedImage);
                    if (!string.IsNullOrWhiteSpace(tool.AudioFilePath)
                        && D052HermesAudioPlaybackPolicy.TryResolve(
                            tool.AudioFilePath,
                            Environment.GetFolderPath(
                                Environment.SpecialFolder.LocalApplicationData),
                            out var safeAudioFilePath))
                    {
                        var audioResult = value.ScopeKind == D046HermesEventScopeKind.Turn
                            ? _turnPresentation.UpsertAudioAttachment(
                                value.TurnId,
                                tool.ToolCallId,
                                safeAudioFilePath)
                            : D052HermesPresentationUpsertResult.InvalidIdentity;
                        if (audioResult != D052HermesPresentationUpsertResult.Applied)
                        {
                            RejectPresentationEvent(
                                applyLiveState,
                                "音訊未通過驗證，未顯示此音訊。");
                        }
                    }
                    RaiseProjectionProperties();
                    break;
                case D046HermesClarificationPayload clarification:
                    if (applyLiveState)
                    {
                        if (clarification.State == D046HermesClarificationState.Requested)
                        {
                            SetPendingClarification(new D046HermesPendingClarificationProjection(
                                value.EventId,
                                clarification.ClarificationId,
                                clarification.Prompt,
                                clarification.Choices?.ToArray() ?? Array.Empty<string>(),
                                clarification.MultiSelect));
                        }
                        else if (string.Equals(
                            _pendingClarification?.ClarificationId,
                            clarification.ClarificationId,
                            StringComparison.Ordinal))
                        {
                            ClearPendingClarification();
                        }
                    }
                    AddEventCard(
                        value,
                        "clarification:" + clarification.ClarificationId,
                        "莉莉絲的提問",
                        clarification.Prompt,
                        ClarificationStateText(clarification.State));
                    break;
                case D046HermesSecureInputPayload secureInput:
                    if (applyLiveState)
                    {
                        if (secureInput.State == D046HermesSecureInputState.Requested)
                        {
                            SetPendingSecureInput(new D046HermesPendingSecureInputProjection(
                                value.EventId,
                                secureInput.RequestId,
                                secureInput.InputKind,
                                secureInput.Prompt,
                                secureInput.EnvironmentVariable));
                        }
                        else if (string.Equals(
                            _pendingSecureInput?.RequestId,
                            secureInput.RequestId,
                            StringComparison.Ordinal))
                        {
                            ClearPendingSecureInput();
                        }
                    }
                    AddEventCard(
                        value,
                        "secure-input:" + secureInput.RequestId,
                        secureInput.InputKind == D046HermesSecureInputKind.SudoPassword
                            ? "需要系統管理員密碼"
                            : "需要安全輸入",
                        secureInput.Prompt,
                        secureInput.State == D046HermesSecureInputState.Requested
                            ? "等待安全輸入（內容不記錄）"
                            : "已取消或逾期");
                    break;
                case D046HermesApprovalPayload approval:
                    if (applyLiveState
                        && string.Equals(
                            value.SourceMethod,
                            "approval.request",
                            StringComparison.Ordinal)
                        && approval.State == D046HermesApprovalState.Requested)
                    {
                        EnqueuePendingApproval(value, approval);
                    }
                    AddEventCard(
                        value,
                        "approval:" + approval.ApprovalId,
                        ControlCenterLocalization.Current.Get("ui.your.permission"),
                        approval.Summary,
                        ApprovalStateText(approval.State)
                            + " · 仍需通過授權檢查");
                    break;
                case D046HermesTurnErrorPayload turnError:
                    ApplyTurnError(value, turnError, applyLiveState);
                    break;
                case D046HermesErrorPayload error:
                    if (error.CompletesTurn
                        && value.ScopeKind == D046HermesEventScopeKind.Turn)
                    {
                        _turnPresentation.SealStreamingAssistantMessages(value.TurnId);
                        _turnPresentation.SealStreamingReasoning(value.TurnId);
                    }
                    AddEventCard(
                        value,
                        "event:" + value.EventId,
                        "黑色筆記本 · 警告",
                        error.DisplayText,
                        SafeCode(error.ErrorCode));
                    if (applyLiveState)
                    {
                        ProtectTurnSettlementStatus();
                        _statusText = error.DisplayText;
                        if (error.IsTerminal)
                            _projectionFault = true;
                        RaiseProjectionProperties();
                    }
                    break;
                case D046HermesMetadataPayload metadata:
                    if (!string.Equals(
                            metadata.MetadataType,
                            "thinking.delta",
                            StringComparison.Ordinal))
                    {
                        AddEventCard(
                            value,
                            "event:" + value.EventId,
                            "收音機",
                            metadata.DisplayText,
                            metadata.MetadataType);
                    }
                    break;
                case D046HermesSubagentPayload subagent:
                    ApplySubagentEvent(subagent);
                    break;
                default:
                    SetProjectionFault("收到無法辨識的資料；已停用送出。");
                    break;
            }
        }
        finally
        {
            EndTimelineMutation();
        }

        RaiseSelectedTaskProperties();
    }

    private static bool IsAuthoritativeStopTerminal(
        D046HermesProjectionEvent value,
        D046HermesTaskState taskState)
    {
        if (taskState == D046HermesTaskState.Stopping)
            return false;

        return value.Payload switch
        {
            D046HermesMessagePayload message =>
                value.ScopeKind == D046HermesEventScopeKind.Turn
                && message.CompletesTurn,
            D046HermesTurnErrorPayload =>
                value.ScopeKind == D046HermesEventScopeKind.Turn,
            D046HermesErrorPayload error =>
                value.ScopeKind == D046HermesEventScopeKind.Turn
                && error.CompletesTurn,
            D046HermesSessionPayload session =>
                session.TaskState is D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed,
            D046HermesStatusPayload status =>
                status.TaskState is D046HermesTaskState.Idle
                    or D046HermesTaskState.Failed,
            _ => false
        };
    }

    private void ApplyMessage(
        D046HermesProjectionEvent value,
        D046HermesMessagePayload message,
        bool applyLiveState)
    {
        D052HermesPresentationUpsertResult result;
        if (value.ScopeKind == D046HermesEventScopeKind.LegacyUnscoped)
        {
            result = _turnPresentation.AddLegacyUnscopedMessage(
                value.EventId,
                message.MessageId,
                message.Role,
                message.Operation,
                message.Text,
                value.OccurredUtc);
        }
        else if (value.ScopeKind == D046HermesEventScopeKind.Turn)
        {
            if (message.IsInterim && message.AlreadyStreamed)
            {
                result = _turnPresentation.PromoteStreamingPrimaryToInterim(
                    value.TurnId,
                    message.PrimaryMessageId,
                    message.MessageId,
                    message.Text,
                    confirmedCompletion: message.MessageCompletionConfirmed);
            }
            else if (message.ResponsePreviewed)
            {
                result = _turnPresentation.SettlePreviewedFinal(
                    value.TurnId,
                    message.PreviewMessageId,
                    message.MessageId,
                    message.Text,
                    confirmedCompletion: message.MessageCompletionConfirmed);
            }
            else
            {
                result = _turnPresentation.UpsertMessage(
                    value.TurnId,
                    message.MessageId,
                    message.Role,
                    message.Operation,
                    message.Text,
                    value.OccurredUtc,
                    confirmedCompletion: message.MessageCompletionConfirmed);
            }
        }
        else
        {
            result = D052HermesPresentationUpsertResult.InvalidIdentity;
        }

        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState: true,
                "收到無法安全合併的訊息識別；內容未顯示。");
            return;
        }
        if (message.Role == D046HermesMessageRole.Assistant
            && message.CompletesTurn
            && value.ScopeKind == D046HermesEventScopeKind.Turn)
        {
            _turnPresentation.SealStreamingReasoning(value.TurnId);
            if (applyLiveState)
                ObserveSuccessfulTurnCompletion(value);
        }
        if (!string.IsNullOrWhiteSpace(message.Warning))
            AddTurnWarning(value, message.Warning, "message.warning");
        AddBudgetConstraints(value, message.BudgetConstraints);
    }

    private void ApplyTurnError(
        D046HermesProjectionEvent value,
        D046HermesTurnErrorPayload error,
        bool applyLiveState)
    {
        if (value.ScopeKind != D046HermesEventScopeKind.Turn)
        {
            RejectPresentationEvent(
                applyLiveState,
                "收到未綁定回合的錯誤資料；內容未顯示。");
            return;
        }
        var result = error.Partial
            ? _turnPresentation.UpsertMessage(
                value.TurnId,
                error.AssistantMessageId,
                D046HermesMessageRole.Assistant,
                D046HermesStreamOperation.Completed,
                error.AssistantText,
                value.OccurredUtc,
                confirmedCompletion: false)
            : _turnPresentation.SealExactAssistantMessage(
                value.TurnId,
                error.AssistantMessageId);
        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState,
                "收到無法安全合併的錯誤回合識別；內容未顯示。");
            return;
        }

        _turnPresentation.SealStreamingReasoning(value.TurnId);
        var errorDisplay = error.BudgetError is { } budgetFailure
            ? UsageBudgetErrorPresentation.Display(budgetFailure) : error.DisplayText;
        result = _turnPresentation.UpsertActivity(
            value.TurnId,
            "turn-error:" + error.AssistantMessageId,
            value.EventId,
            D046HermesEventKind.Error,
            "黑色筆記本 · 警告",
            errorDisplay,
            SafeCode(error.ErrorCode),
            value.OccurredUtc,
            budgetFailure: error.BudgetError);
        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState,
                "收到重複或衝突的錯誤回合；內容未顯示。");
            return;
        }
        if (!string.IsNullOrWhiteSpace(error.Warning))
            AddTurnWarning(value, error.Warning, "message.warning");
        AddBudgetConstraints(value, error.BudgetConstraints);
        if (applyLiveState)
        {
            ProtectTurnSettlementStatus();
            _statusText = errorDisplay;
            RaiseProjectionProperties();
        }
    }

    private void AddBudgetConstraints(D046HermesProjectionEvent value,
        IReadOnlyList<UsageBudgetConstraint>? constraints)
    {
        if (constraints is not { Count: > 0 }) return;
        if (value.ScopeKind != D046HermesEventScopeKind.Turn) return;
        var result = _turnPresentation.UpsertActivity(value.TurnId, "budget-constraints:" + value.TurnId,
            value.EventId + "-budget-constraints", D046HermesEventKind.Metadata,
            ControlCenterLocalization.Current["budget.constraints.title"],
            UsageBudgetConstraintPresentation.Display(constraints), "budget.constraints", value.OccurredUtc,
            budgetConstraints: constraints);
        if (result != D052HermesPresentationUpsertResult.Applied)
            RejectPresentationEvent(applyLiveState: true, "收到無法安全合併的回合提醒；內容未顯示。");
    }

    private void AddTurnWarning(
        D046HermesProjectionEvent value,
        string warning,
        string warningCode)
    {
        var result = _turnPresentation.UpsertActivity(
            value.TurnId,
            "warning:" + value.EventId,
            value.EventId + "-warning",
            D046HermesEventKind.Error,
            "黑色筆記本 · 提醒",
            warning,
            warningCode,
            value.OccurredUtc);
        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState: true,
                "收到無法安全合併的回合提醒；內容未顯示。");
        }
    }

    private void ApplyReasoning(
        D046HermesProjectionEvent value,
        D046HermesReasoningPayload reasoning)
    {
        if (value.ScopeKind == D046HermesEventScopeKind.Turn)
        {
            var result = _turnPresentation.UpsertReasoning(
                value.TurnId,
                reasoning.ReasoningId,
                reasoning.Operation,
                reasoning.Text,
                value.OccurredUtc,
                value.EventId);
            if (result != D052HermesPresentationUpsertResult.Applied)
            {
                RejectPresentationEvent(
                    applyLiveState: true,
                    "收到無法安全合併的思緒識別；內容未顯示。");
            }
            return;
        }
        AddEventCard(
            value,
            "event:" + value.EventId,
            "鏡中的思緒",
            reasoning.Text,
            "未綁定回合的工作階段資料");
    }

    private void AddEventCard(
        D046HermesProjectionEvent value,
        string semanticIdentity,
        string heading,
        string text,
        string detail)
    {
        // Session-wide telemetry has no causal turn and no durable history
        // carrier. Keep applying it to task/header state, but never insert a
        // transient standalone row into the conversation transcript.
        if (!D052HermesTurnPresentationCollection.IsConversationTimelineScope(
                value.ScopeKind))
        {
            return;
        }

        var turnId = value.ScopeKind == D046HermesEventScopeKind.Turn
            ? value.TurnId
            : null;
        var result = _turnPresentation.UpsertActivity(
            turnId,
            semanticIdentity,
            value.EventId,
            value.Payload.Kind,
            heading,
            text,
            detail,
            value.OccurredUtc);
        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState: true,
                "收到無法安全合併的工作過程識別；內容未顯示。");
        }
    }

    private void AddCronCompletionCard(
        D046HermesProjectionEvent value,
        D046HermesCronCompletionPayload completion)
    {
        if (value.ScopeKind != D046HermesEventScopeKind.Session
            || !string.IsNullOrEmpty(value.TurnId)
            || !string.Equals(
                value.SourceMethod,
                HermesTurnCorrelationContract.CronCompletionEventType,
                StringComparison.Ordinal))
        {
            RejectPresentationEvent(
                applyLiveState: true,
                "收到無法安全合併的計時通知；內容未顯示。");
            return;
        }

        var result = _turnPresentation.UpsertActivity(
            turnId: null,
            semanticIdentity: "cron-completion:" + value.EventId,
            eventId: value.EventId,
            D046HermesEventKind.Metadata,
            "鐘錶報告",
            completion.DisplayText,
            completion.Success ? "計時工作已完成" : "計時工作未成功",
            value.OccurredUtc);
        if (result != D052HermesPresentationUpsertResult.Applied)
        {
            RejectPresentationEvent(
                applyLiveState: true,
                "收到重複或衝突的計時通知；內容未顯示。");
        }
    }

    private static bool ValidatePresentationScope(
        D046HermesProjectionEvent value,
        bool applyLiveState)
        => value.ScopeKind switch
        {
            D046HermesEventScopeKind.Turn
                => D052HermesTurnPresentationCollection.IsLowercaseHex32(
                    value.TurnId),
            D046HermesEventScopeKind.Session
                => string.IsNullOrEmpty(value.TurnId),
            D046HermesEventScopeKind.LegacyUnscoped
                => !applyLiveState && string.IsNullOrEmpty(value.TurnId),
            _ => false
        };

    private void RejectPresentationEvent(bool applyLiveState, string status)
    {
        ProtectTurnSettlementStatus();
        _projectionFault = true;
        _statusText = status;
        if (applyLiveState)
            RaiseProjectionProperties();
    }

    private void EnqueuePendingApproval(
        D046HermesProjectionEvent value,
        D046HermesApprovalPayload approval)
        => EnqueuePendingApproval(new D046HermesPendingApprovalProjection(
            value.EventId,
            approval.ApprovalId,
            approval.Summary,
            approval.Choices,
            approval.Command,
            approval.Description,
            approval.PatternKey,
            approval.AllowSession,
            approval.AllowPermanent,
            approval.SmartDenied));

    private void EnqueuePendingApproval(
        D046HermesPendingApprovalProjection approval)
    {
        if (_pendingApprovals.Any(candidate => string.Equals(
                candidate.ApprovalId,
                approval.ApprovalId,
                StringComparison.Ordinal)))
        {
            return;
        }

        if (approval.Choices is null
            || approval.Choices.Count == 0
            || approval.Choices.Any(choice => choice is not (
                "once" or "session" or "always" or "deny"))
            || approval.Choices.Distinct(StringComparer.Ordinal).Count()
                != approval.Choices.Count)
        {
            _approvalAttentionRequired = true;
            _pendingApprovalSummary = SafeCode(approval.Summary);
            _approvalStatusText =
                "收到無法安全顯示的許可選項；工作已停止，請重新開啟工作階段。";
            RaiseApprovalProperties();
            SetProjectionFault("收到無效的工作授權選項；不會顯示或自動回覆。");
            return;
        }

        _approvalAttentionRequired = false;
        _pendingApprovals.Enqueue(new D046HermesPendingApprovalProjection(
            approval.EventId,
            approval.ApprovalId,
            SafeCode(approval.Summary),
            approval.Choices.ToArray(),
            approval.Command,
            approval.Description,
            approval.PatternKey,
            approval.AllowSession,
            approval.AllowPermanent,
            approval.SmartDenied));
        RefreshPendingApprovalDisplay("正在等待你的決定；不會自動執行。");
    }

    private void RestorePendingApprovals(
        IReadOnlyList<D046HermesPendingApprovalProjection> pendingApprovals)
    {
        foreach (var pending in pendingApprovals)
        {
            _pendingApprovals.Enqueue(pending with
            {
                Summary = SafeCode(pending.Summary),
                Choices = pending.Choices.ToArray()
            });
        }
        RefreshPendingApprovalDisplay(
            pendingApprovals.Count == 0
                ? ControlCenterLocalization.Current.Get("state.no.pending.task.approval.47")
                : "正在等待你的決定；不會自動執行。");
    }

    private void RefreshPendingApprovalDisplay(string fallbackStatus)
    {
        if (_pendingApprovals.Count == 0)
        {
            _approvalAttentionRequired = false;
            _pendingApprovalSummary = string.Empty;
            _approvalStatusText = fallbackStatus;
        }
        else
        {
            var pending = _pendingApprovals.Peek();
            _pendingApprovalSummary = pending.Summary;
            _approvalStatusText = _pendingApprovals.Count == 1
                ? "等待你的決定；不會自動執行。"
                : "等待你的決定；後面還有 "
                    + (_pendingApprovals.Count - 1).ToString()
                    + " 筆工作授權。";
        }
        RaiseApprovalProperties();
    }

    private void ClearPendingApprovals(string status)
    {
        _pendingApprovals.Clear();
        _approvalAttentionRequired = false;
        _pendingApprovalSummary = string.Empty;
        _approvalStatusText = status;
        RaiseApprovalProperties();
    }

    private void UpdateApprovalTimeline(
        string eventId,
        string text,
        string detail)
    {
        BeginTimelineMutation();
        try
        {
            _ = _turnPresentation.TryUpdateEventActivity(eventId, text, detail);
        }
        finally
        {
            EndTimelineMutation();
        }
    }

    private void RaiseApprovalProperties()
    {
        OnPropertyChanged(nameof(ApprovalSummaryText));
        OnPropertyChanged(nameof(ApprovalStatusText));
        OnPropertyChanged(nameof(HasPendingApproval));
        OnPropertyChanged(nameof(HasApprovalAttention));
        OnPropertyChanged(nameof(HasApprovalAllowOnce));
        OnPropertyChanged(nameof(HasApprovalAllowSession));
        OnPropertyChanged(nameof(HasApprovalAllowAlways));
        OnPropertyChanged(nameof(HasApprovalDeny));
        OnPropertyChanged(nameof(ApprovalDescriptionText));
        OnPropertyChanged(nameof(ApprovalCommandText));
        OnPropertyChanged(nameof(HasApprovalDescription));
        OnPropertyChanged(nameof(HasApprovalCommand));
        OnPropertyChanged(nameof(ApprovalScopeWarningText));
        OnPropertyChanged(nameof(HasApprovalScopeWarning));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanRespondSecureInput));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        RaiseRollbackGateProperties();
    }

    private void SetPendingClarification(
        D046HermesPendingClarificationProjection clarification)
    {
        foreach (var choice in ClarificationChoices)
            choice.PropertyChanged -= ClarificationChoice_PropertyChanged;
        _pendingClarification = clarification;
        ClarificationChoices.Clear();
        foreach (var choice in clarification.Choices)
        {
            var item = new D046HermesClarificationChoiceViewModel(choice);
            item.PropertyChanged += ClarificationChoice_PropertyChanged;
            ClarificationChoices.Add(item);
        }
        RaiseClarificationProperties();
    }

    private void ClearPendingClarification()
    {
        foreach (var choice in ClarificationChoices)
            choice.PropertyChanged -= ClarificationChoice_PropertyChanged;
        _pendingClarification = null;
        ClarificationChoices.Clear();
        RaiseClarificationProperties();
    }

    private void ClarificationChoice_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(D046HermesClarificationChoiceViewModel.IsSelected))
            OnPropertyChanged(nameof(CanSubmitClarificationSelection));
    }

    private void RaiseClarificationProperties()
    {
        OnPropertyChanged(nameof(HasPendingClarification));
        OnPropertyChanged(nameof(ClarificationPrompt));
        OnPropertyChanged(nameof(IsClarificationMultiSelect));
        OnPropertyChanged(nameof(HasClarificationChoices));
        OnPropertyChanged(nameof(IsClarificationSingleSelect));
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanStop));
    }

    private void SetPendingSecureInput(D046HermesPendingSecureInputProjection secureInput)
    {
        _pendingSecureInput = secureInput;
        RaiseSecureInputProperties();
    }

    private void ClearPendingSecureInput()
    {
        _pendingSecureInput = null;
        RaiseSecureInputProperties();
    }

    private void RaiseSecureInputProperties()
    {
        OnPropertyChanged(nameof(SecureInputIdentity));
        OnPropertyChanged(nameof(HasPendingSecureInput));
        OnPropertyChanged(nameof(SecureInputTitle));
        OnPropertyChanged(nameof(SecureInputPrompt));
        OnPropertyChanged(nameof(SecureInputEnvironmentVariable));
        OnPropertyChanged(nameof(HasSecureInputEnvironmentVariable));
        OnPropertyChanged(nameof(CanRespondSecureInput));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanStop));
    }

    private void BeginSubmitStatusCorrelation(
        D046HermesTaskItemViewModel frozenTask)
    {
        ArgumentNullException.ThrowIfNull(frozenTask);
        ResetTurnSettlementStatusCorrelation();
        _submitStatusScope = new D046HermesSessionStatusCorrelation(
            _runtimeGeneration,
            frozenTask.TaskId,
            frozenTask.SessionId);
        _submitStatusReceiptPending = true;
    }

    private bool ObserveSubmitReceipt(D046HermesSubmitReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!_submitStatusReceiptPending
            || _submitStatusScope is not { } submitScope
            || !submitScope.Matches(
                receipt.RuntimeGeneration,
                receipt.TaskId,
                receipt.SessionId))
        {
            ProtectTurnSettlementStatus();
            return true;
        }

        _submitStatusReceiptPending = false;
        _submitStatusScope = null;
        if (_turnReadyStatusProtected)
        {
            _settledTurnAwaitingReceipt = null;
            return true;
        }

        var settledBeforeReceipt =
            _settledTurnAwaitingReceipt is { } settled
            && (D046HermesTurnStatusCorrelation.TryCreate(
                    receipt,
                    out var acceptedTurn)
                ? settled == acceptedTurn
                : receipt.ConsumesWithoutTurn
                    && settled.Session == submitScope);
        _settledTurnAwaitingReceipt = null;
        return settledBeforeReceipt;
    }

    private void ObserveSuccessfulTurnCompletion(
        D046HermesProjectionEvent value)
    {
        if (_turnReadyStatusProtected
            || !D046HermesTurnStatusCorrelation.TryCreate(
                value,
                out var completedTurn)
            || SelectedTask is null
            || !completedTurn.Session.Matches(
                _runtimeGeneration,
                SelectedTask.TaskId,
                SelectedTask.SessionId))
        {
            return;
        }

        _completedTurnAwaitingIdle = completedTurn;
    }

    private void ObserveSettledIdleSession(
        D046HermesProjectionEvent value,
        D046HermesTaskItemViewModel task)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(task);
        if (_turnReadyStatusProtected
            || value.Payload is not D046HermesSessionPayload
                {
                    TaskState: D046HermesTaskState.Idle
                }
            || task.State != D046HermesTaskState.Idle
            || _completedTurnAwaitingIdle is not { } completedTurn
            || !completedTurn.Session.Matches(
                value.RuntimeGeneration,
                value.TaskId,
                value.SessionId))
        {
            return;
        }

        _completedTurnAwaitingIdle = null;
        _settledTurnAwaitingReceipt = _submitStatusReceiptPending
            && _submitStatusScope is { } submitScope
            && submitScope == completedTurn.Session
                ? completedTurn
                : null;
        _statusText = "ready";
        OnPropertyChanged(nameof(StatusText));
    }

    private void ProtectTurnSettlementStatus()
    {
        _turnReadyStatusProtected = true;
        _completedTurnAwaitingIdle = null;
        _settledTurnAwaitingReceipt = null;
    }

    private void ResetTurnSettlementStatusCorrelation()
    {
        _submitStatusScope = null;
        _completedTurnAwaitingIdle = null;
        _settledTurnAwaitingReceipt = null;
        _submitStatusReceiptPending = false;
        _turnReadyStatusProtected = false;
    }

    private bool ReceiptMatches(
        D046HermesCommandReceipt receipt,
        D046HermesTaskItemViewModel frozenTask)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                frozenTask.SessionId,
                StringComparison.Ordinal);

    private bool ReceiptMatches(
        D046HermesClarificationResponseReceipt receipt,
        D046HermesTaskItemViewModel frozenTask)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                frozenTask.SessionId,
                StringComparison.Ordinal);

    private bool ReceiptMatches(
        D046HermesSecureInputResponseReceipt receipt,
        D046HermesTaskItemViewModel frozenTask)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                frozenTask.SessionId,
                StringComparison.Ordinal);

    private bool ApprovalReceiptMatches(
        D046HermesApprovalResponseReceipt receipt,
        D046HermesTaskItemViewModel frozenTask,
        string choice)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                frozenTask.SessionId,
                StringComparison.Ordinal)
            && string.Equals(receipt.Choice, choice, StringComparison.Ordinal);

    private static bool IsDefinitiveExpiredApprovalFailure(string code)
        => code is "runtime.pending_native_approval_expired"
            or "runtime.pending_native_approval_required";

    private bool ModelReceiptMatches(
        D046HermesModelSelectionReceipt receipt,
        D046HermesTaskItemViewModel frozenTask,
        D046HermesModelOption option)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, frozenTask.TaskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                frozenTask.SessionId,
                StringComparison.Ordinal)
            && string.Equals(receipt.ProviderId, option.ProviderId, StringComparison.Ordinal)
            && string.Equals(receipt.ModelId, option.ModelId, StringComparison.Ordinal);

    private bool CompressionReceiptMatches(
        D046HermesCompressionReceipt receipt,
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => receipt is not null
            && receipt.Snapshot is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(
                receipt.SessionId,
                sessionId,
                StringComparison.Ordinal)
            && string.Equals(
                receipt.Snapshot.RuntimeGeneration,
                runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(
                receipt.Snapshot.SelectedTaskId,
                taskId,
                StringComparison.Ordinal);

    private static string FormatCompressionStatus(D046HermesCompressionReceipt receipt)
    {
        var headline = SafeCode(receipt.Headline);
        if (receipt.BeforeMessages.HasValue
            && receipt.AfterMessages.HasValue
            && receipt.BeforeTokens.HasValue
            && receipt.AfterTokens.HasValue)
        {
            var beforeAfter = "訊息 "
                + receipt.BeforeMessages.Value
                + " → "
                + receipt.AfterMessages.Value
                + " · 約 "
                + receipt.BeforeTokens.Value.ToString("N0")
                + " → "
                + receipt.AfterTokens.Value.ToString("N0")
                + " tokens";
            var note = string.IsNullOrWhiteSpace(receipt.Note)
                ? string.Empty
                : " · " + SafeCode(receipt.Note);
            return headline + " · " + beforeAfter + note;
        }

        return SafeCode(receipt.Code) + " · " + headline;
    }

    private void ApplyObservabilitySnapshot(D046HermesObservabilitySnapshot snapshot)
    {
        if (snapshot.Usage.IsAvailable)
        {
            _usageStatusText = "累計用量："
                + snapshot.Usage.TotalTokens.ToString("N0")
                + " tokens · "
                + snapshot.Usage.Calls.ToString("N0")
                + " 次呼叫\n輸入 "
                + snapshot.Usage.InputTokens.ToString("N0")
                + " · 輸出 "
                + snapshot.Usage.OutputTokens.ToString("N0");
            _compressionCountStatusText = "內容壓縮："
                + snapshot.Usage.Compressions.ToString("N0")
                + " 次";
        }
        else
        {
            _usageStatusText = "累計用量：暫時無法讀取";
            _compressionCountStatusText = "內容壓縮：暫時無法讀取";
        }

        _contextStatusText = snapshot.Context.IsAvailable
            ? "上下文："
                + snapshot.Context.ContextUsed.ToString("N0")
                + " / "
                + snapshot.Context.ContextMaximum.ToString("N0")
                + "（"
                + snapshot.Context.ContextPercent.ToString("0.#")
                + "%）\n"
                + string.Join(
                    " · ",
                    snapshot.Context.Categories.Select(category =>
                        category.Label + " " + category.Tokens.ToString("N0")))
            : "上下文：暫時無法讀取";

        LearningItems.Clear();
        if (snapshot.Learning.IsAvailable)
        {
            var skills = snapshot.Learning.RecentItems.Count(item =>
                string.Equals(item.Kind, "skill", StringComparison.Ordinal));
            var memories = snapshot.Learning.RecentItems.Count(item =>
                string.Equals(item.Kind, "memory", StringComparison.Ordinal));
            var summary = snapshot.Learning.Summary.Count == 0
                ? string.Empty
                : "\n" + string.Join("\n", snapshot.Learning.Summary.Select(SafeCode));
            _learningStatusText = snapshot.Learning.Count == 0
                ? "尚無學習內容。建立的技能與記憶會顯示在這裡。"
                : snapshot.Learning.Count.ToString("N0")
                    + " 項學習內容 · 近期 "
                    + skills.ToString("N0")
                    + " 個技能 · "
                    + memories.ToString("N0")
                    + " 筆記憶"
                    + summary;
            foreach (var item in snapshot.Learning.RecentItems)
            {
                LearningItems.Add(
                    (string.Equals(item.Kind, "memory", StringComparison.Ordinal)
                        ? "記憶 · "
                        : "技能 · ")
                    + SafeCode(item.Label)
                    + (string.IsNullOrWhiteSpace(item.Metadata)
                        ? string.Empty
                        : " · " + SafeCode(item.Metadata)));
            }
        }
        else
        {
            _learningStatusText = "暫時無法載入學習紀錄；對話仍可使用。";
        }

        _observabilityStatusText = snapshot.Usage.IsAvailable
            && snapshot.Context.IsAvailable
            && snapshot.Learning.IsAvailable
                ? "目前聊天與設定的詳細狀態已更新。"
                : "部分詳細狀態暫時無法讀取；對話仍可使用。";
        RaiseObservabilityProperties();
    }

    private void ResetObservabilityDisplay()
    {
        _observabilityStatusText = ControlCenterLocalization.Current.Get("state.native.runtime.observations.not.loaded.yet.41");
        _usageStatusText = ControlCenterLocalization.Current.Get("state.total.usage.not.loaded.42");
        _contextStatusText = ControlCenterLocalization.Current.Get("state.context.not.loaded.43");
        _compressionCountStatusText = ControlCenterLocalization.Current.Get("state.compression.not.loaded.44");
        _learningStatusText = ControlCenterLocalization.Current.Get("state.learning.records.not.loaded.45");
        LearningItems.Clear();
        RaiseObservabilityProperties();
    }

    private bool CanUseRollbackScope()
        => !_stopInFlight
            && !_commandInFlight
            && !_taskPinInFlight
            && !_observabilityInFlight
            && !_delegationStatusInFlight
            && !_delegationControlInFlight
            && !_voiceLifecycleInFlight
            && !_voiceStartInFlight
            && !_voiceStopInFlight
            && _isConnected
            && _nativeSurfaceAttested
            && !_sequenceGapDetected
            && !_projectionFault
            && !HasPendingApproval
            && !HasPendingImage
            && !HasActiveSubagents
            && !IsVoiceCaptureBusy
            && SelectedTask is not null
            && SelectedTask.State == D046HermesTaskState.Idle
            && string.Equals(SelectedTask.TaskId, _activeTaskId,
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);

    private bool RollbackCatalogScopeMatchesCurrent()
        => SelectedTask is not null
            && string.Equals(_rollbackCatalogGeneration, _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(_rollbackCatalogTaskId, SelectedTask.TaskId,
                StringComparison.Ordinal)
            && string.Equals(_rollbackCatalogSessionId, SelectedTask.SessionId,
                StringComparison.Ordinal);

    private bool RollbackScopeStillMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(_runtimeGeneration, runtimeGeneration, StringComparison.Ordinal)
            && string.Equals(_activeTaskId, taskId, StringComparison.Ordinal)
            && SelectedTask is not null
            && string.Equals(SelectedTask.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(SelectedTask.SessionId, sessionId, StringComparison.Ordinal);

    private bool RollbackRestoreDispatchScopeStillValid(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        string checkpointHash,
        string confirmationToken,
        long lastObservedSequence)
        => _rollbackRestoreInFlight
            && !_rollbackListInFlight
            && !_rollbackDiffInFlight
            && CanUseRollbackScope()
            && _lastObservedSequence == lastObservedSequence
            && !_rollbackRestoreQuarantined
            && _rollbackCatalogEnabled
            && RollbackCatalogScopeMatchesCurrent()
            && RollbackScopeStillMatches(runtimeGeneration, taskId, sessionId)
            && SelectedRollbackCheckpoint is not null
            && string.Equals(
                SelectedRollbackCheckpoint.Hash,
                checkpointHash,
                StringComparison.Ordinal)
            && string.Equals(_rollbackDiffHash, checkpointHash,
                StringComparison.Ordinal)
            && string.Equals(_rollbackConfirmationToken, confirmationToken,
                StringComparison.Ordinal);

    private bool RollbackRestoreReceiptMatches(
        D046HermesRollbackRestoreReceipt receipt,
        string runtimeGeneration,
        string taskId,
        string sessionId,
        string checkpointHash)
        => receipt is not null
            && RollbackScopeStillMatches(runtimeGeneration, taskId, sessionId)
            && string.Equals(receipt.RuntimeGeneration, runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(receipt.SessionId, sessionId, StringComparison.Ordinal)
            && string.Equals(receipt.CheckpointHash, checkpointHash,
                StringComparison.Ordinal);

    private void ClearRollbackDiff()
    {
        _rollbackConfirmationToken = string.Empty;
        _rollbackDiffHash = string.Empty;
        _rollbackDiffText = string.Empty;
        OnPropertyChanged(nameof(RollbackDiffText));
    }

    private void ClearRollbackCatalog()
    {
        _rollbackCatalogEnabled = false;
        _rollbackCatalogGeneration = string.Empty;
        _rollbackCatalogTaskId = string.Empty;
        _rollbackCatalogSessionId = string.Empty;
        RollbackCheckpoints.Clear();
        _selectedRollbackCheckpoint = null;
        ClearRollbackDiff();
        OnPropertyChanged(nameof(SelectedRollbackCheckpoint));
    }

    private void ResetRollbackDisplay()
    {
        ClearRollbackCatalog();
        _rollbackStatusText = ControlCenterLocalization.Current.Get("state.workspace.checkpoints.not.loaded.yet.50");
        RaiseRollbackProperties();
    }

    private void SetRollbackProjectionFault(
        string rollbackStatus,
        Exception? exception = null)
    {
        _rollbackRestoreQuarantined = true;
        _rollbackStatusText = rollbackStatus;
        SetProjectionFault(exception is null
            ? "工作區還原後已停止更新聊天內容；請重新連線。"
            : ProjectionFailureStatus("工作區還原後重新載入", exception)
                + " · 請重新連線。");
    }

    private static string FormatRollbackDiff(D046HermesRollbackDiffReceipt receipt)
    {
        var stat = receipt.Stat ?? string.Empty;
        var diff = receipt.Diff ?? string.Empty;
        var value = stat.Length == 0
            ? diff
            : diff.Length == 0
                ? stat
                : stat + Environment.NewLine + Environment.NewLine + diff;
        const int maximumCharacters = 128 * 1024;
        return value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters] + Environment.NewLine + "…差異顯示已截短…";
    }

    private static string RollbackFailureSuffix(Exception exception)
    {
        var value = exception is D046HermesRollbackOperationException rollback
            ? rollback.Code
            : exception.GetType().Name;
        var safe = SafeRollbackText(value);
        return safe.Length == 0 ? string.Empty : " · " + safe;
    }

    private static string RollbackReceiptSuffix(
        D046HermesRollbackRestoreReceipt receipt)
    {
        var safe = SafeRollbackText(
            string.IsNullOrWhiteSpace(receipt.Error)
                ? receipt.Reason
                : receipt.Error);
        return safe.Length == 0 ? string.Empty : " · " + safe;
    }

    private static string SafeRollbackText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (normalized.IndexOfAny(new[] { '{', '}', '[', ']', '"' }) >= 0
            || normalized.Contains("payload", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("directory", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("file_path", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        return normalized.Length <= 120 ? normalized : normalized[..120] + "…";
    }

    private void RaiseRollbackGateProperties()
    {
        OnPropertyChanged(nameof(CanRefreshRollback));
        OnPropertyChanged(nameof(CanSelectRollbackCheckpoint));
        OnPropertyChanged(nameof(CanGetRollbackDiff));
        OnPropertyChanged(nameof(CanRestoreRollback));
    }

    private void RaiseRollbackProperties()
    {
        OnPropertyChanged(nameof(RollbackStatusText));
        OnPropertyChanged(nameof(RollbackDiffText));
        OnPropertyChanged(nameof(IsRollbackOperationInFlight));
        RaiseRollbackGateProperties();
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanRespondSecureInput));
        OnPropertyChanged(nameof(CanRefreshDelegation));
        foreach (var card in Subagents)
            card.SetControlsEnabled(CanControlSubagents());
    }

    private bool CanControlSubagents()
        => !_stopInFlight
            && !IsRollbackOperationInFlight
            && !_delegationStatusInFlight
            && !_delegationControlInFlight
            && !_delegationUnavailable
            && !_delegationControlQuarantined
            && _isConnected
            && _nativeSurfaceAttested
            && !_sequenceGapDetected
            && !_projectionFault
            && SelectedTask is not null
            && string.Equals(SelectedTask.TaskId, _activeTaskId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(SelectedTask.SessionId);

    private bool DelegationScopeStillMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(_runtimeGeneration, runtimeGeneration, StringComparison.Ordinal)
            && string.Equals(_activeTaskId, taskId, StringComparison.Ordinal)
            && SelectedTask is not null
            && string.Equals(SelectedTask.TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(SelectedTask.SessionId, sessionId, StringComparison.Ordinal);

    private static bool SubagentSteerReceiptMatches(
        D046HermesSubagentSteerReceipt receipt,
        D046HermesSubagentSteerRequest request)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            && string.Equals(receipt.SubagentId, request.SubagentId, StringComparison.Ordinal)
            && string.Equals(receipt.Text, request.Text, StringComparison.Ordinal);

    private static bool SubagentInterruptReceiptMatches(
        D046HermesSubagentInterruptReceipt receipt,
        D046HermesSubagentInterruptRequest request)
        => receipt is not null
            && string.Equals(
                receipt.RuntimeGeneration,
                request.RuntimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(receipt.TaskId, request.TaskId, StringComparison.Ordinal)
            && string.Equals(receipt.SessionId, request.SessionId, StringComparison.Ordinal)
            && string.Equals(receipt.SubagentId, request.SubagentId, StringComparison.Ordinal);

    private void ReplaceDelegationCards(
        IReadOnlyList<D046HermesSubagentCardProjection> cards)
    {
        if (cards.Count > 2
            || cards.Any(card => !IsValidSubagentCard(card))
            || cards.Select(card => card.SubagentId).Distinct(StringComparer.Ordinal).Count()
                != cards.Count)
        {
            throw new InvalidDataException("分身工作資料的格式不正確。");
        }

        Subagents.Clear();
        _subagentsById.Clear();
        foreach (var projection in cards)
        {
            var card = new D046HermesSubagentCardViewModel(projection);
            _subagentsById.Add(card.SubagentId, card);
            Subagents.Add(card);
        }
    }

    private void ApplySubagentEvent(D046HermesSubagentPayload payload)
    {
        if (payload.Card is null || !IsValidSubagentCard(payload.Card))
        {
            _delegationUnavailable = true;
            _delegationStatusText = "收到無法顯示的分身工作狀態；對話仍可使用。";
            RaiseDelegationProperties();
            return;
        }

        _delegationUnavailable = false;
        if (!UpsertSubagent(payload.Card))
        {
            _delegationUnavailable = true;
            _delegationStatusText = "同時進行的分身工作超過可顯示範圍；請重新整理。";
        }
        else
        {
            _delegationStatusText = HasActiveSubagents
                ? Subagents.Count(card => card.IsActive) + " 個分身工作進行中。"
                : "分身工作狀態已更新。";
        }
        RaiseDelegationProperties();
    }

    private bool UpsertSubagent(D046HermesSubagentCardProjection projection)
    {
        if (_subagentsById.TryGetValue(projection.SubagentId, out var existing))
        {
            existing.Apply(projection);
            return true;
        }

        if (Subagents.Count >= 2)
        {
            var replaceable = Subagents.FirstOrDefault(card => !card.IsActive);
            if (replaceable is null)
                return false;
            RemoveSubagent(replaceable.SubagentId);
        }

        var card = new D046HermesSubagentCardViewModel(projection);
        _subagentsById.Add(card.SubagentId, card);
        Subagents.Add(card);
        return true;
    }

    private void RemoveSubagent(string subagentId)
    {
        if (!_subagentsById.Remove(subagentId, out var card))
            return;
        Subagents.Remove(card);
    }

    private void ResetDelegationDisplay()
    {
        Subagents.Clear();
        _subagentsById.Clear();
        _delegationStatusInFlight = false;
        _delegationControlInFlight = false;
        _delegationUnavailable = false;
        _delegationControlQuarantined = false;
        _delegationStatusText = ControlCenterLocalization.Current.Get("state.delegated.task.status.not.loaded.yet.46");
        RaiseDelegationProperties();
    }

    private void QuarantineDelegationControls(string status)
    {
        _delegationUnavailable = true;
        _delegationControlQuarantined = true;
        _delegationStatusText = status;
    }

    private async Task ReconcilePreDispatchDelegationRejectionAsync(
        D046HermesSubagentCardViewModel card,
        string status)
    {
        // The pipe raises EventReceived synchronously after advancing its cursor,
        // while the window posts the projection to this dispatcher. A same-turn
        // control click can therefore see the newer pipe cursor before its card
        // has consumed the already-queued terminal event. This DataBind barrier
        // lets those earlier callbacks settle before a read-only status fallback.
        _delegationControlInFlight = false;
        RaiseDelegationProperties();
        await Dispatcher.Yield(DispatcherPriority.DataBind);

        if (_subagentsById.TryGetValue(card.SubagentId, out var current)
            && ReferenceEquals(current, card)
            && card.IsActive)
        {
            await RefreshDelegationCoreAsync(allowCommandInFlight: false);
        }

        _delegationStatusText = status;
        RaiseDelegationProperties();
    }

    private static bool IsValidSubagentCard(
        D046HermesSubagentCardProjection? card)
        => card is not null
            && !string.IsNullOrWhiteSpace(card.SubagentId)
            && card.Depth >= 0
            && card.ToolCount >= 0
            && card.DurationSeconds is null or >= 0;

    private void RaiseDelegationProperties()
    {
        var controlsEnabled = CanControlSubagents();
        foreach (var card in Subagents)
            card.SetControlsEnabled(controlsEnabled);
        OnPropertyChanged(nameof(DelegationStatusText));
        OnPropertyChanged(nameof(IsDelegationEmpty));
        OnPropertyChanged(nameof(HasActiveSubagents));
        OnPropertyChanged(nameof(CanRefreshDelegation));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        RaiseRollbackGateProperties();
    }

    private void RaiseObservabilityProperties()
    {
        OnPropertyChanged(nameof(ObservabilityStatusText));
        OnPropertyChanged(nameof(UsageStatusText));
        OnPropertyChanged(nameof(ContextStatusText));
        OnPropertyChanged(nameof(CompressionCountStatusText));
        OnPropertyChanged(nameof(LearningStatusText));
        OnPropertyChanged(nameof(CanRefreshObservability));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanRefreshObservability));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanRespondSecureInput));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
        RaiseRollbackGateProperties();
    }

    private static bool IsValidEnvelope(D046HermesProjectionEvent value)
        => !string.IsNullOrWhiteSpace(value.EventId)
            && value.Sequence > 0
            && !string.IsNullOrWhiteSpace(value.RuntimeGeneration)
            && !string.IsNullOrWhiteSpace(value.TaskId)
            && !string.IsNullOrWhiteSpace(value.SessionId)
            && !string.IsNullOrWhiteSpace(value.SourceMethod)
            && value.Payload is not null
            && !string.IsNullOrWhiteSpace(value.CanonicalPayloadJson)
            && (value.ScopeKind switch
            {
                D046HermesEventScopeKind.Turn
                    => D052HermesTurnPresentationCollection.IsLowercaseHex32(
                        value.TurnId),
                D046HermesEventScopeKind.Session
                    or D046HermesEventScopeKind.LegacyUnscoped
                    => string.IsNullOrEmpty(value.TurnId),
                _ => false
            });

    private void ClearPendingImage(string? status = null)
    {
        var pending = _pendingImage;
        _pendingImage = null;
        pending?.Dispose();
        if (status is not null)
            _statusText = status;
        RaisePendingImageProperties();
    }

    private void RaisePendingImageProperties()
    {
        OnPropertyChanged(nameof(PendingImage));
        OnPropertyChanged(nameof(HasPendingImage));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        RaiseRollbackGateProperties();
    }

    private void FreezeVoiceScope(D046HermesTaskItemViewModel? task)
    {
        if (task is null)
        {
            if (_voiceScopeTaskId.Length != 0 || _voiceScopeSessionId.Length != 0)
                _lastVoiceRuntimeSequence = 0;
            _voiceScopeTaskId = string.Empty;
            _voiceScopeSessionId = string.Empty;
            return;
        }
        if (!string.Equals(
                _voiceScopeTaskId,
                task.TaskId,
                StringComparison.Ordinal)
            || !string.Equals(
                _voiceScopeSessionId,
                task.SessionId,
                StringComparison.Ordinal))
        {
            _lastVoiceRuntimeSequence = 0;
        }
        _voiceScopeTaskId = task.TaskId;
        _voiceScopeSessionId = task.SessionId;
    }

    private static D046HermesVoiceCommandRequest CreateVoiceRequest(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => new(
            Guid.NewGuid().ToString("N"),
            runtimeGeneration,
            taskId,
            sessionId);

    private bool VoiceScopeMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(
                runtimeGeneration,
                _runtimeGeneration,
                StringComparison.Ordinal)
            && string.Equals(taskId, _voiceScopeTaskId, StringComparison.Ordinal)
            && string.Equals(sessionId, _voiceScopeSessionId, StringComparison.Ordinal);

    private void SetVoiceCaptureState(
        D046HermesVoiceCaptureState state,
        string? statusOverride = null)
    {
        _voiceCaptureState = state;
        _voiceStatusOverride = statusOverride ?? string.Empty;
        RaiseVoiceProperties();
    }

    private void SetVoiceFault(
        D046HermesVoiceFailureKind failureKind,
        string code,
        string displayText)
    {
        _voiceStopRequested = false;
        var safeDisplay = SafeVoiceText(displayText);
        var safeCode = SafeVoiceText(code);
        var detail = safeDisplay.Length > 0
            ? safeDisplay
            : safeCode.Length > 0
                ? safeCode
                : failureKind.ToString();
        SetVoiceCaptureState(
            D046HermesVoiceCaptureState.Quarantined,
            "Fault · " + detail);
    }

    private void AppendVoiceTranscript(string value)
    {
        var transcript = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (transcript.Length == 0)
            return;

        var current = DraftText;
        if (current.Length == 0 || char.IsWhiteSpace(current[^1]))
        {
            DraftText = current + transcript;
            return;
        }

        var separator = NeedsVoiceWordSeparator(current[^1], transcript[0])
            ? " "
            : string.Empty;
        DraftText = current + separator + transcript;
    }

    private static bool NeedsVoiceWordSeparator(char left, char right)
        => left <= 0x7f
            && right <= 0x7f
            && char.IsLetterOrDigit(left)
            && char.IsLetterOrDigit(right);

    private static string SafeVoiceText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (normalized.IndexOfAny(new[] { '{', '}', '[', ']', '"' }) >= 0
            || normalized.Contains("payload", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }
        return normalized.Length <= 120 ? normalized : normalized[..120] + "…";
    }

    private void RaiseVoiceProperties()
    {
        OnPropertyChanged(nameof(VoiceStatusText));
        OnPropertyChanged(nameof(IsVoiceCaptureBusy));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(CanCompress));
        RaiseRollbackGateProperties();
    }

    internal void NotifyProjectionFault(D046HermesProjectionFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        if (_transportFaultStatus is not null)
            return;
        InvalidateCapabilityCatalog(clear: true, stop: true);
        _transportFaultStatus = ControlCenterLocalization.Current.Format(
            "chat.reconnect.connectionLost", fault.Code);
        ProtectTurnSettlementStatus();
        _projectionFault = true;
        _isConnected = false;
        _nativeSurfaceAttested = false;
        _turnPresentation.MarkConnectionLost();
        foreach (var task in Tasks)
            task.MarkConnectionLost();
        RaiseProjectionProperties();
        RaiseVoiceProperties();
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanRespondSecureInput));
    }

    private void SetProjectionFault(string status)
    {
        ProtectTurnSettlementStatus();
        _projectionFault = true;
        _statusText = status;
        InvalidateCapabilityCatalog(clear: true);
        RaiseProjectionProperties();
    }

    private static string ProjectionFailureStatus(string stage, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (message.IndexOfAny(new[] { '{', '}', '[', ']', '"' }) >= 0
            || message.Contains("payload", StringComparison.OrdinalIgnoreCase))
        {
            message = string.Empty;
        }
        else if (message.Length > 160)
        {
            message = message[..160] + "…";
        }

        return "背景服務資料更新失敗 · "
            + stage
            + " · "
            + exception.GetType().Name
            + (message.Length == 0 ? string.Empty : " · " + message);
    }

    private void SetCommandState(bool value, string? status = null)
    {
        if (value)
            _modelSelectionResultStatus = null;
        _commandInFlight = value;
        if (status is not null)
            _statusText = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanRefreshObservability));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanRespondClarification));
        OnPropertyChanged(nameof(CanSubmitClarificationSelection));
        OnPropertyChanged(nameof(CanRespondSecureInput));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        RaiseRollbackGateProperties();
        RaiseDelegationProperties();
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
    }

    private void SetStopState(bool value, string? status = null)
    {
        _stopInFlight = value;
        if (status is not null)
            _statusText = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        RaiseRollbackGateProperties();
        RaiseDelegationProperties();
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
    }

    private void RaiseProjectionProperties()
    {
        OnPropertyChanged(nameof(NeedsConnectionRecovery));
        OnPropertyChanged(nameof(CanReconnect));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CompressionStatusText));
        OnPropertyChanged(nameof(EffectToolsStatusText));
        OnPropertyChanged(nameof(RuntimeGenerationDisplay));
        OnPropertyChanged(nameof(EventCountText));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanRefreshObservability));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        RaiseTimelineScopeProperties();
        OnPropertyChanged(nameof(VoiceStatusText));
        OnPropertyChanged(nameof(IsVoiceCaptureBusy));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        RaiseSelectedTaskProperties();
    }

    private void RaiseSelectedTaskProperties()
    {
        OnPropertyChanged(nameof(SelectedTaskTitle));
        OnPropertyChanged(nameof(ModelDisplayName));
        OnPropertyChanged(nameof(SessionDisplay));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanCompress));
        OnPropertyChanged(nameof(CanSelectModel));
        OnPropertyChanged(nameof(CanRefreshObservability));
        OnPropertyChanged(nameof(CanRespondApproval));
        OnPropertyChanged(nameof(CanCreateTask));
        OnPropertyChanged(nameof(CanSwitchTask));
        OnPropertyChanged(nameof(CanOpenTaskMenu));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(CanRemoveImage));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftPending));
        OnPropertyChanged(nameof(IsFaultRecoveryDraftBlockingCurrentTask));
        OnPropertyChanged(nameof(CanDiscardFaultRecoveryDraft));
        OnPropertyChanged(nameof(CanStartVoiceCapture));
        OnPropertyChanged(nameof(CanUsePushToTalk));
        RaiseTimelineScopeProperties();
        RaiseRollbackGateProperties();
        RaiseDelegationProperties();
        SetNativeToolRowControls();
        RaiseNativeToolsProperties();
    }

    private void RaiseTimelineScopeProperties()
    {
        OnPropertyChanged(nameof(IsTimelineEmpty));
        OnPropertyChanged(nameof(IsTimelineScopeCurrent));
        OnPropertyChanged(nameof(IsTimelinePlaceholderVisible));
        OnPropertyChanged(nameof(TimelinePlaceholderText));
    }

    private static string SafeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unspecified";
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }

    private static string Compact(string value)
        => value.Length <= 24
            ? value
            : value[..10] + "…" + value[^8..];

    private static string SessionChangeText(D046HermesSessionChange value)
        => value switch
        {
            D046HermesSessionChange.Created => "已建立",
            D046HermesSessionChange.Opened => "已開啟",
            D046HermesSessionChange.Renamed => "已重新命名",
            D046HermesSessionChange.ModelChanged => "模型已變更",
            D046HermesSessionChange.Archived => "已封存",
            _ => "狀態已更新"
        };

    private static string ToolPhaseText(D046HermesToolPhase value)
        => value switch
        {
            D046HermesToolPhase.Proposed => "已提出",
            D046HermesToolPhase.Started => "執行中",
            D046HermesToolPhase.Completed => ControlCenterLocalization.Current.Get("state.completed.18"),
            D046HermesToolPhase.Rejected => "已拒絕",
            _ => "未知"
        };

    private static string ClarificationStateText(
        D046HermesClarificationState value)
        => value switch
        {
            D046HermesClarificationState.Requested => "等待玩家補充",
            D046HermesClarificationState.Answered => "已回答",
            D046HermesClarificationState.Cancelled => "已取消",
            _ => "未知"
        };

    private static string ApprovalStateText(D046HermesApprovalState value)
        => value switch
        {
            D046HermesApprovalState.Requested => "等待授權檢查",
            D046HermesApprovalState.Approved => "已核准",
            D046HermesApprovalState.Denied => "已拒絕",
            D046HermesApprovalState.Expired => "已逾期",
            _ => "未知"
        };

    private bool PendingApprovalOffers(string choice)
        => _pendingApprovals.Count > 0
            && _pendingApprovals.Peek().Choices.Contains(choice, StringComparer.Ordinal);

    private static string ApprovalChoiceLabel(string choice)
        => choice switch
        {
            "once" => ControlCenterLocalization.Current.Get("ui.allow.once"),
            "session" => ControlCenterLocalization.Current.Get("ui.allow.this.session"),
            "always" => ControlCenterLocalization.Current.Get("ui.always.permit"),
            "deny" => ControlCenterLocalization.Current.Get("ui.deny"),
            _ => "未知選項"
        };

    private static string ApprovalChoiceDetail(string choice)
        => choice switch
        {
            "once" => "只允許本次工作；不會永久記住。",
            "session" => "只在目前工作階段記住這項授權規則。",
            "always" => "這項授權會永久保存，可在工具設定中管理。",
            _ => "本次工作不會執行。"
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (_projectionFault && propertyName != nameof(CanReconnect))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanReconnect)));
    }
}

internal static class D052HermesPixelWheelScrollPolicy
{
    private const double PixelsPerLine = 16;
    private const double MinimumPixelsPerDetent = 24;
    private const double MaximumPixelsPerEvent = 72;

    internal static bool TryGetVerticalOffset(
        double currentOffset,
        double scrollableHeight,
        double viewportHeight,
        int wheelDelta,
        int wheelScrollLines,
        bool precisionMode,
        out double targetOffset)
    {
        targetOffset = currentOffset;
        if (!double.IsFinite(currentOffset)
            || !double.IsFinite(scrollableHeight)
            || !double.IsFinite(viewportHeight)
            || currentOffset < 0
            || scrollableHeight < 0
            || viewportHeight < 0
            || wheelDelta == 0)
        {
            return false;
        }

        var detents = wheelDelta / 120d;
        var pixelsPerDetent = wheelScrollLines switch
        {
            < 0 => Math.Min(Math.Max(viewportHeight * 0.25, MinimumPixelsPerDetent),
                MaximumPixelsPerEvent),
            0 => 0,
            _ => Math.Min(
                Math.Max(wheelScrollLines * PixelsPerLine, MinimumPixelsPerDetent),
                MaximumPixelsPerEvent)
        };
        if (pixelsPerDetent <= 0)
            return false;
        if (precisionMode)
            pixelsPerDetent *= 0.5;

        var pixelDelta = Math.Clamp(
            -detents * pixelsPerDetent,
            -MaximumPixelsPerEvent,
            MaximumPixelsPerEvent);
        targetOffset = Math.Clamp(currentOffset + pixelDelta, 0, scrollableHeight);
        return Math.Abs(targetOffset - currentOffset) >= 0.01;
    }
}

public partial class D046HermesWorkbenchWindow : Window
{
    private readonly ControlCenterSendCue _sendCue = new();
    internal void StopSendCue() => _sendCue.Stop();
    private D046HermesWorkbenchHost _host;
    private D046HermesWorkbenchViewModel _viewModel;
    private readonly bool _ownsHost;
    private readonly MediaPlayer _hermesAudioPlayer = new();
    private bool _voiceMousePressed;
    private bool _voiceKeyboardPressed;
    private bool _timelineAutoFollow = true;
    private string _renameTargetTaskId = string.Empty;
    private string _deleteTargetTaskId = string.Empty;
    private string _deleteTargetTitle = string.Empty;
    private bool _deleteTaskBusy;
    private bool _runtimeGenerationRetired;
    private IInputElement? _focusBeforeRename;
    private IInputElement? _focusBeforeDelete;
    private D052ListScrollAnchor? _timelineAnchor;
    private D052ListScrollAnchor? _taskRailAnchor;
    private bool _timelineFollowAfterMutation;
    private bool _restoringTimelineAnchor;
    private long _timelineRenderRequest;
    private long _taskRailRenderRequest;

    public D046HermesWorkbenchWindow(
        ID046HermesWorkbenchProjection projection)
        : this(
            new D046HermesWorkbenchHost(
                projection,
                Dispatcher.CurrentDispatcher),
            ownsHost: true)
    {
    }

    internal D046HermesWorkbenchWindow(D046HermesWorkbenchHost host)
        : this(host, ownsHost: false)
    {
    }

    private D046HermesWorkbenchWindow(
        D046HermesWorkbenchHost host,
        bool ownsHost)
    {
        InitializeComponent();
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _viewModel = _host.ViewModel;
        _ownsHost = ownsHost;
        DataContext = _viewModel;
        InitializeSlashCommands();
        _hermesAudioPlayer.MediaFailed += HermesAudioPlayer_MediaFailed;
        AttachConnectionCallbacks();
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        Closing += ConnectionRecoveryWindow_Closing;
        Closed += (_, _) => _sendCue.Dispose();
        Deactivated += Window_Deactivated;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        var host = _host;
        try
        {
            await host.InitializeAsync();
            if (!ReferenceEquals(host, _host) || _connectionWindowClosed || _connectionRecoveryPending)
                return;
            FocusPendingApprovalOrComposer();
            ScrollTimelineToEnd();
        }
        catch (Exception)
        {
            // The view model has already failed closed and published a safe
            // connection error. Keep the window visible so a temporarily
            // unavailable Runtime does not look like a Workbench crash. The
            // existing reopen/refresh path replaces this faulted host.
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _connectionWindowClosed = true;
        DisposeSlashCommands();
        ++_connectionBindingEpoch;
        ++_timelineRenderRequest;
        ++_taskRailRenderRequest;
        _hermesAudioPlayer.MediaFailed -= HermesAudioPlayer_MediaFailed;
        _hermesAudioPlayer.Stop();
        _hermesAudioPlayer.Close();
        DetachConnectionCallbacks();
        Closing -= ConnectionRecoveryWindow_Closing;
        Deactivated -= Window_Deactivated;
        _voiceMousePressed = false;
        _voiceKeyboardPressed = false;
        if (!_runtimeGenerationRetired)
            await _viewModel.EndVoiceCaptureAsync();
        if (_ownsHost)
            await _host.DisposeAsync();
    }

    private void PlayHermesAudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveHermesAudioAttachment(sender, out var resolvedPath, out _))
            return;

        try
        {
            _hermesAudioPlayer.Stop();
            _hermesAudioPlayer.Close();
            _hermesAudioPlayer.Open(new Uri(resolvedPath, UriKind.Absolute));
            _hermesAudioPlayer.Play();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or UriFormatException)
        {
            MessageBox.Show(
                this,
                "目前無法播放這段音訊。",
                "無法播放音訊",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void StopHermesAudioButton_Click(object sender, RoutedEventArgs e)
        => _hermesAudioPlayer.Stop();

    private void ViewModel_ConversationScopeChanging(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _viewModel) || !_connectionCallbacksAttached)
            return;
        _hermesAudioPlayer.Stop();
        _hermesAudioPlayer.Close();
    }

    private void HermesAudioPlayer_MediaFailed(
        object? sender,
        ExceptionEventArgs e)
    {
        _hermesAudioPlayer.Stop();
        _hermesAudioPlayer.Close();
        MessageBox.Show(
            this,
            "目前無法播放這段音訊。",
            "無法播放音訊",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SaveHermesAudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryResolveHermesAudioAttachment(
                sender,
                out var resolvedPath,
                out var attachment))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            CheckPathExists = true,
            DefaultExt = Path.GetExtension(resolvedPath),
            FileName = attachment.FileName,
            Filter = "音訊檔案|*.mp3;*.wav;*.ogg;*.m4a;*.flac|所有檔案|*.*",
            OverwritePrompt = true,
            Title = "另存音訊"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var destination = Path.GetFullPath(dialog.FileName);
            if (!string.Equals(
                    resolvedPath,
                    destination,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(resolvedPath, destination, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "無法將這段音訊另存到指定位置。",
                "另存失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private bool TryResolveHermesAudioAttachment(
        object sender,
        out string resolvedPath,
        out D052HermesAudioAttachmentViewModel attachment)
    {
        attachment = null!;
        resolvedPath = string.Empty;
        if (sender is FrameworkElement
            {
                DataContext: D052HermesAudioAttachmentViewModel candidate
            }
            && D052HermesAudioPlaybackPolicy.TryResolve(
                candidate.FilePath,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                out resolvedPath))
        {
            attachment = candidate;
            return true;
        }

        MessageBox.Show(
            this,
            "這段音訊已不存在，或未通過安全檢查。",
            "無法使用音訊",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    internal void RetireRuntimeGeneration()
    {
        _runtimeGenerationRetired = true;
        Close();
    }

    private void Host_ProjectionApplied(D046HermesProjectionEvent value)
    {
        if (!_connectionCallbacksAttached || _connectionWindowClosed || _connectionRecoveryPending)
            return;
        if (value.Payload is D046HermesApprovalPayload
            && _viewModel.HasPendingApproval
            && IsActive)
        {
            FocusPendingApprovalOrComposer();
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private async void NewSessionButton_Click(object sender, RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        _hermesAudioPlayer.Stop();
        _hermesAudioPlayer.Close();
        await _viewModel.CreateTaskAsync();
        HermesMessageInput.Focus();
        ScrollTimelineToEnd();
    }

    private void TaskItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { ContextMenu: { } menu } item) return;
        // The context target is not the displayed/active conversation. Suppress
        // ListBoxItem's default right-button selection before it can navigate.
        e.Handled = true;
        menu.PlacementTarget = item;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private async void TaskList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (_viewModel.IsApplyingSnapshot
            || e.AddedItems.Count != 1
            || e.AddedItems[0] is not D046HermesTaskItemViewModel task
            || string.Equals(
                task.TaskId,
                _viewModel.ActiveTaskId,
                StringComparison.Ordinal))
        {
            return;
        }

        await _viewModel.OpenTaskAsync(task.TaskId);
        if (_viewModel.ActiveTaskId == task.TaskId)
        {
            _hermesAudioPlayer.Stop();
            _hermesAudioPlayer.Close();
        }
        // A rejected/no-effect transition leaves the old task authoritative.
        // OneWay prevents speculative mouse selection from resetting its state.
        if (sender is ListBox list)
            list.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, _viewModel.SelectedTask);
        HermesMessageInput.Focus();
        ScrollTimelineToEnd();
    }

    private async void PinTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (sender is not MenuItem
            {
                DataContext: D046HermesTaskItemViewModel task
            })
        {
            return;
        }

        _ = await _viewModel.SetTaskPinnedAsync(task);
    }

    private void RenameTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                DataContext: D046HermesTaskItemViewModel task
            }
            || !task.CanManage)
        {
            return;
        }

        CloseDeleteTaskOverlay(restoreFocus: false);
        _focusBeforeRename = HermesTaskList.ItemContainerGenerator
            .ContainerFromItem(task) as IInputElement
            ?? Keyboard.FocusedElement;
        _renameTargetTaskId = task.TaskId;
        HermesRenameTaskInput.Text = task.Title;
        HermesRenameTaskOverlay.Visibility = Visibility.Visible;
        var bindingEpoch = _connectionBindingEpoch;
        Dispatcher.BeginInvoke(() =>
        {
            if (bindingEpoch != _connectionBindingEpoch || _connectionWindowClosed)
                return;
            HermesRenameTaskInput.Focus();
            HermesRenameTaskInput.SelectAll();
        });
    }

    private void DeleteTaskMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem
            {
                DataContext: D046HermesTaskItemViewModel task
            }
            || !task.CanManage)
        {
            return;
        }

        CloseRenameTaskOverlay(restoreFocus: false);
        _focusBeforeDelete = HermesTaskList.ItemContainerGenerator
            .ContainerFromItem(task) as IInputElement
            ?? Keyboard.FocusedElement;
        _deleteTargetTaskId = task.TaskId;
        _deleteTargetTitle = task.Title;
        _deleteTaskBusy = false;
        HermesDeleteTaskTargetText.Text = "「" + _deleteTargetTitle + "」";
        HermesDeleteTaskBusyText.Visibility = Visibility.Collapsed;
        HermesDeleteTaskResultText.ClearValue(TextBlock.TextProperty);
        HermesDeleteTaskResultText.Visibility = Visibility.Collapsed;
        HermesDeleteTaskCancelButton.IsEnabled = true;
        HermesDeleteTaskConfirmButton.IsEnabled = true;
        HermesDeleteTaskOverlay.Visibility = Visibility.Visible;
        var bindingEpoch = _connectionBindingEpoch;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (bindingEpoch == _connectionBindingEpoch && !_connectionWindowClosed)
                    HermesDeleteTaskCancelButton.Focus();
            }));
    }

    private void CancelRenameTaskButton_Click(object sender, RoutedEventArgs e)
        => CloseRenameTaskOverlay();

    private void RenameTaskOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        CloseRenameTaskOverlay();
    }

    private async void ConfirmRenameTaskButton_Click(object sender, RoutedEventArgs e)
        => await ConfirmRenameTaskAsync();

    private async void RenameTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;
        e.Handled = true;
        await ConfirmRenameTaskAsync();
    }

    private async Task ConfirmRenameTaskAsync()
    {
        using var bindingOperation = TrackBoundOperation();
        if (string.IsNullOrWhiteSpace(_renameTargetTaskId))
            return;

        HermesRenameTaskConfirmButton.IsEnabled = false;
        try
        {
            var receipt = await _viewModel.RenameTaskAsync(
                _renameTargetTaskId,
                HermesRenameTaskInput.Text);
            if (receipt?.Accepted == true)
            {
                CloseRenameTaskOverlay();
                return;
            }

            MessageBox.Show(
                this,
                "重新命名沒有完成。請確認聊天已待命，且名稱不是空白。",
                "無法重新命名",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            HermesRenameTaskInput.Focus();
            HermesRenameTaskInput.SelectAll();
        }
        finally
        {
            HermesRenameTaskConfirmButton.IsEnabled = true;
        }
    }

    private void CloseRenameTaskOverlay(bool restoreFocus = true)
    {
        _renameTargetTaskId = string.Empty;
        HermesRenameTaskInput.Clear();
        HermesRenameTaskOverlay.Visibility = Visibility.Collapsed;
        if (restoreFocus)
            RestoreOverlayFocus(ref _focusBeforeRename);
        else
            _focusBeforeRename = null;
    }

    private void CancelDeleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_deleteTaskBusy)
            CloseDeleteTaskOverlay();
    }

    private void DeleteTaskOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _deleteTaskBusy)
            return;
        e.Handled = true;
        CloseDeleteTaskOverlay();
    }

    private async void ConfirmDeleteTaskButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (_deleteTaskBusy || string.IsNullOrWhiteSpace(_deleteTargetTaskId))
            return;

        var frozenTaskId = _deleteTargetTaskId;
        _deleteTaskBusy = true;
        HermesDeleteTaskBusyText.Visibility = Visibility.Visible;
        HermesDeleteTaskResultText.Visibility = Visibility.Collapsed;
        HermesDeleteTaskCancelButton.IsEnabled = false;
        HermesDeleteTaskConfirmButton.IsEnabled = false;
        try
        {
            var receipt = await _viewModel.DeleteTaskAsync(frozenTaskId);
            if (receipt?.Deleted == true
                && string.Equals(
                    receipt.TaskId,
                    frozenTaskId,
                    StringComparison.Ordinal))
            {
                _deleteTaskBusy = false;
                CloseDeleteTaskOverlay();
                return;
            }

            HermesDeleteTaskResultText.Text = receipt is null
                ? _viewModel.DeleteResultText
                : receipt.RecoveryRequired
                    ? ControlCenterLocalization.Current["chat.delete.rejectedRestored"]
                    : ControlCenterLocalization.Current["chat.delete.rejected"];
            HermesDeleteTaskResultText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (HermesDeleteTaskOverlay.Visibility == Visibility.Visible)
            {
                _deleteTaskBusy = false;
                HermesDeleteTaskBusyText.Visibility = Visibility.Collapsed;
                HermesDeleteTaskCancelButton.IsEnabled = true;
                HermesDeleteTaskConfirmButton.IsEnabled = !_viewModel.HasProjectionFault;
                HermesDeleteTaskCancelButton.Focus();
            }
        }
    }

    private void CloseDeleteTaskOverlay(bool restoreFocus = true)
    {
        if (_deleteTaskBusy)
            return;
        _deleteTargetTaskId = string.Empty;
        _deleteTargetTitle = string.Empty;
        HermesDeleteTaskTargetText.ClearValue(TextBlock.TextProperty);
        HermesDeleteTaskBusyText.Visibility = Visibility.Collapsed;
        HermesDeleteTaskResultText.ClearValue(TextBlock.TextProperty);
        HermesDeleteTaskResultText.Visibility = Visibility.Collapsed;
        HermesDeleteTaskOverlay.Visibility = Visibility.Collapsed;
        if (restoreFocus)
            RestoreOverlayFocus(ref _focusBeforeDelete);
        else
            _focusBeforeDelete = null;
    }

    private void RestoreOverlayFocus(ref IInputElement? priorFocus)
    {
        var target = priorFocus;
        priorFocus = null;
        var bindingEpoch = _connectionBindingEpoch;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (bindingEpoch != _connectionBindingEpoch || _connectionWindowClosed)
                    return;
                if (target is UIElement { IsVisible: true, IsEnabled: true } element
                    && element.Focus())
                {
                    return;
                }
                FocusPendingApprovalOrComposer();
            }));
    }

    private async void ModelSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (_viewModel.IsApplyingModelSelection
            || e.AddedItems.Count != 1
            || e.AddedItems[0] is not D046HermesModelOption option)
        {
            return;
        }

        if (_viewModel.RequiresPreSessionModelSelection)
        {
            _ = await _viewModel.CreateTaskFromPreSessionSelectionAsync(option);
            return;
        }

        if (_viewModel.SelectedTask is not null
            && D046HermesModelSelectionPreflight.RequiresConservativeConfirmation(
                _viewModel.SelectedTask.State))
        {
            var streamingConfirmation = MessageBox.Show(
                this,
                "模型切換會排入下一回合，並可能涉及費用或資料政策。是否仍要繼續？",
                "確認排入模型切換",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            var streamingDecision = D046HermesModelSelectionPreflight.ResolveStreaming(
                streamingConfirmation == MessageBoxResult.Yes);
            if (!streamingDecision.ShouldSend)
            {
                _viewModel.DeclineModelSelectionConfirmation();
                return;
            }

            _ = await _viewModel.SelectModelAsync(
                option,
                streamingDecision.ConfirmExpensiveModel);
            return;
        }

        var receipt = await _viewModel.SelectModelAsync(option);
        if (receipt?.ConfirmationRequired != true)
            return;

        var message = string.IsNullOrWhiteSpace(receipt.ConfirmationMessage)
            ? "這個模型可能產生較高費用，是否仍要切換？"
            : receipt.ConfirmationMessage;
        var confirmation = MessageBox.Show(
            this,
            message,
            "確認模型切換",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation == MessageBoxResult.Yes)
        {
            _ = await _viewModel.SelectModelAsync(
                option,
                confirmExpensiveModel: true);
        }
        else
        {
            _viewModel.DeclineModelSelectionConfirmation();
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await SubmitDraftAsync();

    private void AddImageButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = ControlCenterLocalization.Current.Get("chat.image.pick.title"),
            Filter = ControlCenterLocalization.Current.Get("chat.image.pick.filter") + "|*.png;*.jpg;*.jpeg;*.webp",
            Multiselect = false,
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true)
            return;

        try
        {
            _viewModel.AddImage(picker.FileName);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or FileFormatException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            ShowImageMessage(exception.Message);
        }
    }

    private void RemoveImageButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.RemovePendingImage();

    private void DiscardFaultRecoveryDraftButton_Click(
        object sender,
        RoutedEventArgs e)
        => _viewModel.DiscardFaultRecoveryDraft();

    private async void StopButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.StopAsync();

    private async void ApprovalAllowOnceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        await _viewModel.RespondApprovalAsync("once");
        FocusPendingApprovalOrComposer();
    }

    private async void ApprovalAllowSessionButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        await _viewModel.RespondApprovalAsync("session");
        FocusPendingApprovalOrComposer();
    }

    private async void ApprovalAllowAlwaysButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        await _viewModel.RespondApprovalAsync("always");
        FocusPendingApprovalOrComposer();
    }

    private async void ApprovalDenyButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        await _viewModel.RespondApprovalAsync("deny");
        FocusPendingApprovalOrComposer();
    }

    private async void ClarificationChoiceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (sender is Button { Tag: string answer })
            await _viewModel.RespondClarificationAsync(answer);
        HermesMessageInput.Focus();
    }

    private async void ClarificationMultiSubmitButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        await _viewModel.RespondSelectedClarificationAsync();
        HermesMessageInput.Focus();
    }

    private async void SecureInputSubmitButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        var value = HermesSecureInputPasswordBox.Password;
        if (value.Length == 0)
        {
            HermesSecureInputPasswordBox.Focus();
            return;
        }
        // A later native card may arrive before this response is acknowledged.
        // Remove the submitted value now, not a newly typed value after await.
        HermesSecureInputPasswordBox.Clear();
        await _viewModel.RespondSecureInputAsync(value);
        HermesMessageInput.Focus();
    }

    private async void SecureInputCancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        HermesSecureInputPasswordBox.Clear();
        await _viewModel.RespondSecureInputAsync(
            string.Empty,
            isCancellation: true);
        HermesMessageInput.Focus();
    }

    private async void OpenNativeToolsSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (Owner is MainWindow owner)
            await owner.OpenHermesNativeToolsSettingsAsync();
    }

    private async void CompressButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.CompressAsync();

    private async void ObservabilityRefreshButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.RefreshObservabilityAsync();

    private async void DelegationRefreshButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.RefreshDelegationAsync();

    private async void RollbackRefreshButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.RefreshRollbackCheckpointsAsync();

    private async void RollbackDiffButton_Click(
        object sender,
        RoutedEventArgs e)
        => await _viewModel.GetRollbackDiffAsync();

    private async void RollbackRestoreButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (!_viewModel.CanRestoreRollback)
            return;

        var confirmation = MessageBox.Show(
            this,
            "將整個莉莉絲工作區還原到此檢查點，並移除目前對話最後一個使用者回合及其後內容。還原前會先建立可復原的備份。",
            "確認還原整個工作區",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        await _viewModel.RestoreRollbackAsync();
    }

    private async void SubagentSteerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (sender is Button
            {
                DataContext: D046HermesSubagentCardViewModel card
            })
        {
            await _viewModel.SteerSubagentAsync(card);
        }
    }

    private async void SubagentInterruptButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (sender is Button
            {
                DataContext: D046HermesSubagentCardViewModel card
            })
        {
            await _viewModel.InterruptSubagentAsync(card);
        }
    }

    private async void PushToTalkButton_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (e.LeftButton != MouseButtonState.Pressed || _voiceMousePressed)
            return;

        var shouldStart = !_voiceKeyboardPressed;
        _voiceMousePressed = true;
        e.Handled = true;
        if (sender is not UIElement element || !element.CaptureMouse())
        {
            _voiceMousePressed = false;
            return;
        }
        if (shouldStart)
            await _viewModel.BeginVoiceCaptureAsync();
    }

    private async void PushToTalkButton_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (!_voiceMousePressed)
            return;

        _voiceMousePressed = false;
        e.Handled = true;
        if (sender is UIElement element && element.IsMouseCaptured)
            element.ReleaseMouseCapture();
        if (!_voiceKeyboardPressed)
            await _viewModel.EndVoiceCaptureAsync();
    }

    private async void PushToTalkButton_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (!_voiceMousePressed)
            return;

        _voiceMousePressed = false;
        if (!_voiceKeyboardPressed)
            await _viewModel.EndVoiceCaptureAsync();
    }

    private async void PushToTalkButton_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (e.Key == Key.Escape
            && (_voiceMousePressed || _voiceKeyboardPressed))
        {
            e.Handled = true;
            _voiceMousePressed = false;
            _voiceKeyboardPressed = false;
            if (HermesPushToTalkButton.IsMouseCaptured)
                HermesPushToTalkButton.ReleaseMouseCapture();
            await _viewModel.EndVoiceCaptureAsync();
            return;
        }
        if (e.Key != Key.Space || e.IsRepeat || _voiceKeyboardPressed)
            return;

        var shouldStart = !_voiceMousePressed;
        _voiceKeyboardPressed = true;
        e.Handled = true;
        if (shouldStart)
            await _viewModel.BeginVoiceCaptureAsync();
    }

    private async void PushToTalkButton_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (e.Key != Key.Space || !_voiceKeyboardPressed)
            return;

        _voiceKeyboardPressed = false;
        e.Handled = true;
        if (!_voiceMousePressed)
            await _viewModel.EndVoiceCaptureAsync();
    }

    private async void PushToTalkButton_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (!_voiceKeyboardPressed)
            return;

        _voiceKeyboardPressed = false;
        if (!_voiceMousePressed)
            await _viewModel.EndVoiceCaptureAsync();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (!_voiceMousePressed && !_voiceKeyboardPressed)
            return;

        _voiceMousePressed = false;
        _voiceKeyboardPressed = false;
        if (HermesPushToTalkButton.IsMouseCaptured)
            HermesPushToTalkButton.ReleaseMouseCapture();
        await _viewModel.EndVoiceCaptureAsync();
    }

    private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        using var bindingOperation = TrackBoundOperation();
        if (e.Key != Key.Enter
            || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        await SubmitDraftAsync();
    }

    private async Task SubmitDraftAsync()
    {
        using var bindingOperation = TrackBoundOperation();
        if (!await _viewModel.SubmitDraftAsync())
            return;
        _sendCue.Play(_viewModel.NativeVoiceOwnsSendCue);
        HermesMessageInput.Focus();
    }

    private void ViewModel_TimelineChanging(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _viewModel) || !_connectionCallbacksAttached)
            return;
        _timelineFollowAfterMutation = _timelineAutoFollow;
        _timelineAnchor = _timelineFollowAfterMutation
            ? null
            : CaptureListScrollAnchor(HermesTimeline);
    }

    private void ViewModel_TimelineChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _viewModel) || !_connectionCallbacksAttached)
            return;
        var request = ++_timelineRenderRequest;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (request != _timelineRenderRequest)
                    return;
                if (_timelineFollowAfterMutation)
                {
                    ScrollTimelineToEnd();
                    _timelineAutoFollow = true;
                }
                else if (_timelineAnchor is not null)
                {
                    _restoringTimelineAnchor = true;
                    try
                    {
                        RestoreListScrollAnchor(HermesTimeline, _timelineAnchor);
                    }
                    finally
                    {
                        _restoringTimelineAnchor = false;
                    }
                }
                _timelineAnchor = null;
            }));
    }

    private void ViewModel_TaskRailChanging(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _viewModel) && _connectionCallbacksAttached)
            _taskRailAnchor = CaptureListScrollAnchor(HermesTaskList);
    }

    private void ViewModel_TaskRailChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _viewModel) || !_connectionCallbacksAttached)
            return;
        var request = ++_taskRailRenderRequest;
        var anchor = _taskRailAnchor;
        _taskRailAnchor = null;
        if (anchor is null)
            return;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                if (request == _taskRailRenderRequest)
                    RestoreListScrollAnchor(HermesTaskList, anchor);
            }));
    }

    private void ScrollTimelineToEnd()
    {
        FindVisualDescendant<ScrollViewer>(HermesTimeline)?.ScrollToEnd();
    }

    private void Timeline_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scrollViewer)
            return;
        if (_restoringTimelineAnchor)
            return;
        _timelineAutoFollow = scrollViewer.ExtentHeight
            - scrollViewer.VerticalOffset
            - scrollViewer.ViewportHeight <= 40;
    }

    private void BoundedPixelScroll_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (sender is not ListBox list
            || FindVisualDescendant<ScrollViewer>(list) is not { } scrollViewer)
        {
            return;
        }

        if (!D052HermesPixelWheelScrollPolicy.TryGetVerticalOffset(
                scrollViewer.VerticalOffset,
                scrollViewer.ScrollableHeight,
                scrollViewer.ViewportHeight,
                e.Delta,
                SystemParameters.WheelScrollLines,
                (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
                out var targetOffset))
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private static D052ListScrollAnchor? CaptureListScrollAnchor(ListBox list)
    {
        var scrollViewer = FindVisualDescendant<ScrollViewer>(list);
        if (scrollViewer is null)
            return null;
        foreach (var item in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item)
                    is not FrameworkElement container
                || container.ActualHeight <= 0)
            {
                continue;
            }
            var y = container.TransformToAncestor(scrollViewer)
                .Transform(new Point(0, 0)).Y;
            if (y + container.ActualHeight >= 0
                && y <= scrollViewer.ViewportHeight)
            {
                return new D052ListScrollAnchor(
                    item,
                    y,
                    scrollViewer.VerticalOffset);
            }
        }
        return null;
    }

    private static void RestoreListScrollAnchor(
        ListBox list,
        D052ListScrollAnchor anchor)
    {
        var scrollViewer = FindVisualDescendant<ScrollViewer>(list);
        if (scrollViewer is null || !list.Items.Contains(anchor.Item))
            return;

        scrollViewer.ScrollToVerticalOffset(anchor.VerticalOffset);
        list.UpdateLayout();
        var container = list.ItemContainerGenerator.ContainerFromItem(anchor.Item)
            as FrameworkElement;
        if (container is null)
        {
            list.ScrollIntoView(anchor.Item);
            list.UpdateLayout();
            container = list.ItemContainerGenerator.ContainerFromItem(anchor.Item)
                as FrameworkElement;
        }
        if (container is null)
            return;

        var currentY = container.TransformToAncestor(scrollViewer)
            .Transform(new Point(0, 0)).Y;
        scrollViewer.ScrollToVerticalOffset(Math.Max(
            0,
            scrollViewer.VerticalOffset + currentY - anchor.Y));
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;
            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }
        return null;
    }

    private void FocusPendingApprovalOrComposer()
    {
        if (!_viewModel.HasPendingApproval)
        {
            HermesMessageInput.Focus();
            return;
        }

        if (HermesApprovalDenyButton.Visibility == Visibility.Visible)
            HermesApprovalDenyButton.Focus();
        else if (HermesApprovalAllowOnceButton.Visibility == Visibility.Visible)
            HermesApprovalAllowOnceButton.Focus();
        else if (HermesApprovalAllowSessionButton.Visibility == Visibility.Visible)
            HermesApprovalAllowSessionButton.Focus();
        else if (HermesApprovalAllowAlwaysButton.Visibility == Visibility.Visible)
            HermesApprovalAllowAlwaysButton.Focus();
    }

    private sealed record D052ListScrollAnchor(
        object Item,
        double Y,
        double VerticalOffset);
}
