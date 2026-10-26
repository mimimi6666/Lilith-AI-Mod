using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

// An editor of the native catalog, not a chat session or a second authority.
internal sealed class NativeToolsSettingsViewModel(INativeToolsSettingsProjection projection, Action saved,
    CancellationToken lifetime) : INotifyPropertyChanged
{
    private NativeToolsSettingsSnapshot? _snapshot;
    private bool _busy, _dirty, _uncertain, _newChatRequired, _applyingPreset;
    private bool _toolDiscoveryEnabled;
    private string? _pendingCommitHash;
    private string _statusKey = "common.loading";
    private D046HermesNativeApprovalModeOption? _selected;
    private static ControlCenterLocalization T => ControlCenterLocalization.Current;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<D046HermesNativeToolsetViewModel> NativeToolsets { get; } = new();
    public ObservableCollection<NativeToolGroup> NativeToolGroups { get; } = new();
    public ObservableCollection<D046HermesNativeMcpViewModel> NativeMcpCatalog { get; } = new();
    public IReadOnlyList<D046HermesNativeApprovalModeOption> NativeApprovalModes { get; } = new[]
    {
        new D046HermesNativeApprovalModeOption(D046HermesNativeApprovalMode.Manual, "要求核准"),
        new D046HermesNativeApprovalModeOption(D046HermesNativeApprovalMode.Smart, "代我核准"),
        new D046HermesNativeApprovalModeOption(D046HermesNativeApprovalMode.Off, "完整存取權")
    };
    public D046HermesNativeApprovalModeOption? SelectedNativeApprovalMode
    {
        get => _selected;
        set { if (value == _selected || !CanEditNativeTools || value is null) return; _selected = value; Changed(); }
    }
    public bool IsNativeToolsEmpty => NativeToolsets.Count == 0;
    public bool HasNativeMcpCatalog => NativeMcpCatalog.Count != 0;
    public bool SupportsToolDiscovery => _snapshot?.Catalog.ToolDiscoveryEnabled.HasValue == true;
    public bool CanEditToolDiscovery => CanEditNativeTools && SupportsToolDiscovery;
    public bool ToolDiscoveryEnabled
    {
        get => _toolDiscoveryEnabled;
        set { if (!CanEditToolDiscovery || _toolDiscoveryEnabled == value) return; _toolDiscoveryEnabled = value; Changed(); }
    }
    public string ToolDiscoveryHelp => T[SupportsToolDiscovery ? "tools.discovery.help" : "tools.discovery.unsupported"];
    // A checkbox is an editable choice, not proof that a request used folded
    // schemas. Report the native readback separately; presets, visual expansion
    // and the economy level must never imply or silently change this setting.
    public bool IsToolDiscoverySavedOff => _snapshot is not null && !_uncertain
        && _pendingCommitHash is null && _snapshot.Catalog.ToolDiscoveryEnabled == false;
    public string ToolDiscoveryStateText
    {
        get
        {
            if (_pendingCommitHash is not null) return T["tools.discovery.awaitingReadback"];
            if (_busy && _statusKey == "tools.saving") return T["tools.saving"];
            if (_uncertain || _snapshot is null) return T["tools.discovery.unconfirmed"];
            if (!SupportsToolDiscovery) return T["tools.discovery.unsupported"];
            var desired = T[_toolDiscoveryEnabled ? "tools.discovery.on" : "tools.discovery.off"];
            if (_toolDiscoveryEnabled != _snapshot.Catalog.ToolDiscoveryEnabled)
                return T.Format("tools.discovery.draft", desired,
                    T[_snapshot.Catalog.ToolDiscoveryEnabled == true ? "tools.discovery.on" : "tools.discovery.off"]);
            return T.Format(_newChatRequired ? "tools.discovery.savedNewChat" : "tools.discovery.saved", desired);
        }
    }
    public bool CanSelectDailyPreset => CanEditNativeTools && _snapshot!.Catalog.Presets?.Any(p => p.Id == "daily") == true;
    public bool CanSelectWorkPreset => CanEditNativeTools && _snapshot!.Catalog.Presets?.Any(p => p.Id == "work") == true;
    public bool HasNativePresets => _snapshot?.Catalog.Presets?.Count > 0;
    public string DesiredPresetId => NativeToolsPresentation.Classify(NativeToolsets.Where(r => r.DesiredEnabled).Select(r => r.Name), _snapshot?.Catalog.Presets);
    public string PresetStateText => _snapshot is null || _uncertain ? T["tools.mode.unknown"]
        : T.Format(_dirty ? "tools.mode.draft" : "tools.mode.saved", T["tools.mode." + DesiredPresetId]);
    public bool ShowLocalSceneNotice => NativeToolsets.Any(row => row.Name == "lilith_local_scene" && row.DesiredEnabled)
        && _snapshot?.Catalog.EnabledToolsets.Contains("lilith_local_scene") == false;
    internal bool HasPendingChanges => _dirty
        || NativeMcpCatalog.Any(row => row.DesiredEnabled != row.Source.Enabled)
        || NativeToolsets.Any(HasSetupSelectionDraft);
    private static bool HasSetupSelectionDraft(D046HermesNativeToolsetViewModel row)
    {
        // Match the row's initial presentation, including its unselected
        // first-option fallback. A default selection is not a user edit.
        var baseline = row.SetupProviders.FirstOrDefault(provider => provider.Selected)
            ?? row.SetupProviders.FirstOrDefault();
        var baselineModel = baseline?.CurrentModel is { } model
            && baseline.Models?.Contains(model, StringComparer.Ordinal) == true ? model : null;
        return row.SelectedSetupProvider?.Name != baseline?.Name || row.SelectedSetupModel != baselineModel;
    }
    internal bool IsOperationBusy => _busy;
    public bool CanEditNativeTools => !_busy && !_uncertain && _snapshot is not null;
    public bool CanRefreshNativeTools => !_busy;
    public bool CanApplyNativeTools => CanEditNativeTools && _dirty;
    public bool CanCancelNativeTools => CanEditNativeTools && HasPendingChanges;
    public bool CanStartNewChat => !_busy && !HasPendingChanges && !_uncertain;
    public bool CanStartSetupChat => CanStartNewChat && NativeToolsSetupAvailability.HasSetupHelper(_snapshot?.Catalog);
    public string NativeSetupAvailabilityText => NativeToolsSetupAvailability.HasSetupHelper(_snapshot?.Catalog)
        ? string.Empty : T["tools.setupHelperUnavailable"];
    public bool TryBeginChatSetup(bool requiresSetupHelper = false)
    {
        if (requiresSetupHelper ? !CanStartSetupChat : !CanStartNewChat) return false;
        _busy = true;
        _statusKey = "tools.openingChat";
        Raise();
        return true;
    }
    public void FinishChatSetup(bool opened)
    {
        _busy = false;
        _statusKey = opened ? "tools.summary" : "tools.chatUnavailable";
        Raise();
    }
    public bool NewChatRequired => _newChatRequired;
    public string NativeToolsStatusText => _busy || _snapshot is null || _uncertain || _statusKey != "tools.summary"
        ? T[_statusKey] : T.Format("tools.summary", NativeToolsets.Count(r => r.DesiredEnabled), NativeToolsets.Count,
            NativeToolsets.Count(r => r.DesiredEnabled && r.IsReady), NativeToolsets.Count(r => r.DesiredEnabled && !r.IsReady));
    public string NativeToolsActivationText => _busy ? "" : T[_dirty ? "tools.dirty" : _newChatRequired ? "tools.pending" : "tools.editable"];

    public void SelectPreset(string id)
    {
        if (!CanEditNativeTools || _snapshot?.Catalog.Presets?.FirstOrDefault(p => p.Id == id) is not { } preset) return;
        var managed = preset.ManagedToolsetIds.ToHashSet(StringComparer.Ordinal);
        var selected = preset.ToolsetIds.ToHashSet(StringComparer.Ordinal);
        _applyingPreset = true;
        try { foreach (var row in NativeToolsets.Where(row => managed.Contains(row.Name))) row.DesiredEnabled = selected.Contains(row.Name); }
        finally { _applyingPreset = false; Changed(); }
    }
    private void Changed() { if (_applyingPreset) return; _dirty = _snapshot is not null && (_selected?.Value != (D046HermesNativeApprovalMode)_snapshot.Catalog.ApprovalMode
        || SupportsToolDiscovery && _toolDiscoveryEnabled != _snapshot.Catalog.ToolDiscoveryEnabled
        || !NativeToolsets.Where(r => r.DesiredEnabled).Select(r => r.Name).ToHashSet(StringComparer.Ordinal)
            .SetEquals(_snapshot.Catalog.EnabledToolsets)); Raise(); }
    private void Load(NativeToolsSettingsSnapshot snapshot)
    {
        var expandedRows = NativeToolsets.ToDictionary(row => row.Name, row => row.IsDetailsExpanded, StringComparer.Ordinal);
        var expandedGroups = NativeToolGroups.ToDictionary(group => group.Id, group => group.IsExpanded, StringComparer.Ordinal);
        _snapshot = snapshot; _selected = NativeApprovalModes.Single(m => (int)m.Value == (int)snapshot.Catalog.ApprovalMode);
        _toolDiscoveryEnabled = snapshot.Catalog.ToolDiscoveryEnabled ?? false;
        NativeToolsets.Clear();
        foreach (var r in snapshot.Catalog.Toolsets)
        {
            var row = new D046HermesNativeToolsetViewModel(new(r.Name, r.Label, r.Description, r.Enabled, r.Configured,
                (D046HermesNativeToolsetReadiness)r.Readiness, r.Tools, r.SetupProviders), Changed)
                { IsDetailsExpanded = expandedRows.GetValueOrDefault(r.Name) };
            row.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName is nameof(D046HermesNativeToolsetViewModel.SelectedSetupProvider)
                    or nameof(D046HermesNativeToolsetViewModel.SelectedSetupModel)) Raise();
            };
            NativeToolsets.Add(row);
        }
        NativeToolGroups.Clear();
        foreach (var id in NativeToolsPresentation.GroupOrder)
        {
            var rows = NativeToolsets.Where(row => NativeToolsPresentation.GroupFor(row.Name) == id)
                .OrderBy(row => NativeToolsPresentation.RowOrder(row.Name)).ThenBy(row => row.Name, StringComparer.Ordinal).ToArray();
            if (rows.Length > 0) NativeToolGroups.Add(new(id, rows, expandedGroups.GetValueOrDefault(id, true)));
        }
        NativeMcpCatalog.Clear();
        foreach (var r in snapshot.Catalog.McpCatalog ?? Array.Empty<HermesNativeMcpCatalogEntry>())
        {
            var row = new D046HermesNativeMcpViewModel(r);
            row.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(D046HermesNativeMcpViewModel.DesiredEnabled)) Raise();
            };
            NativeMcpCatalog.Add(row);
        }
        _dirty = false; _uncertain = false; _statusKey = "tools.summary";
    }
    public Task RefreshAsync() => RefreshCoreAsync(background: false);
    internal bool CanObserve => !_busy && (_pendingCommitHash is not null || !HasPendingChanges);
    internal bool NeedsReadbackRecovery => _uncertain && (_pendingCommitHash is not null || !HasPendingChanges);
    internal Task ObserveAsync() => !CanObserve ? Task.CompletedTask : RefreshCoreAsync(background: true);
    private async Task RefreshCoreAsync(bool background)
    {
        if (_busy) return;
        _busy = true; if (!background) _statusKey = "common.loading"; Raise();
        try
        {
            var snapshot = await ReadBackAsync();
            var changedAfterCommit = _pendingCommitHash is not null
                && !string.Equals(_pendingCommitHash, snapshot.ConfigSha256, StringComparison.OrdinalIgnoreCase);
            if (!background || _uncertain || _snapshot?.ConfigSha256 != snapshot.ConfigSha256)
                Load(snapshot);
            _pendingCommitHash = null;
            if (changedAfterCommit) _statusKey = "tools.commitChanged";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception) { _uncertain = true; _statusKey = _pendingCommitHash is null ? "tools.readFailed" : "tools.committedAwaitingRead"; }
        finally { _busy = false; Raise(); }
    }
    private async Task<NativeToolsSettingsSnapshot> ReadBackAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await projection.GetNativeSettingsAsync(lifetime); }
            catch (Exception exception) when (attempt < 2 && !lifetime.IsCancellationRequested
                && exception is TimeoutException or IOException)
            {
                // Repeat only a read. A write with an unknown outcome is never replayed.
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), lifetime);
            }
        }
    }
    public void Cancel() { if (!CanCancelNativeTools || _snapshot is null) return; Load(_snapshot); Raise(); }
    public Task SaveProviderAsync(D046HermesNativeToolsetViewModel row) =>
        row.CanSelectSetupProvider && !_dirty && row.SelectedSetupProvider is { } p
            ? SaveAsync(new HermesNativeSetupSelection(row.Name, p.Name, row.SelectedSetupModel)) : Task.CompletedTask;
    public Task SaveMcpAsync(D046HermesNativeMcpViewModel row) =>
        row.CanApply && !_dirty ? SaveAsync(mcp: new HermesNativeMcpSetupSelection(row.Name, row.Source.CatalogSha256, row.DesiredEnabled)) : Task.CompletedTask;
    public async Task SaveAsync(HermesNativeSetupSelection? setup = null, HermesNativeMcpSetupSelection? mcp = null)
    {
        if (!CanEditNativeTools || _snapshot is null || _selected is null || (!_dirty && setup is null && mcp is null)) return;
        var request = new HermesNativeToolsSetRequest(NativeToolsets.Where(r => r.DesiredEnabled).Select(r => r.Name),
            (HermesNativeApprovalMode)_selected.Value, setup, mcp,
            SupportsToolDiscovery && _toolDiscoveryEnabled != _snapshot.Catalog.ToolDiscoveryEnabled ? _toolDiscoveryEnabled : null);
        var expectedDiscovery = SupportsToolDiscovery ? _toolDiscoveryEnabled : (bool?)null;
        var expected = _snapshot.ConfigSha256;
        _busy = true; _statusKey = "tools.saving"; Raise();
        try
        {
            var result = await projection.SetNativeSettingsAsync(expected, request, lifetime);
            _pendingCommitHash = result.ConfigSha256;
            _newChatRequired = true;
            saved(); // Invalidate old chat before any follow-up read can fail.
            var snapshot = await ReadBackAsync();
            if (!string.Equals(result.ConfigSha256, snapshot.ConfigSha256, StringComparison.OrdinalIgnoreCase)
                || snapshot.Catalog.ToolDiscoveryEnabled != expectedDiscovery
                || snapshot.Catalog.ApprovalMode != request.ApprovalMode
                || !snapshot.Catalog.EnabledToolsets.ToHashSet(StringComparer.Ordinal).SetEquals(request.EnabledToolsets))
                throw new InvalidDataException("Native settings readback changed.");
            Load(snapshot);
            _pendingCommitHash = null;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (NativeSettingsOperationException e) when (!e.Uncertain)
        { _statusKey = e.Code == "runtime.settings_busy" ? "tools.busy" : "tools.stale"; _uncertain = e.Code != "runtime.settings_busy"; }
        catch (Exception) { _uncertain = true; _statusKey = _pendingCommitHash is null ? "tools.uncertain" : "tools.committedAwaitingRead"; }
        finally { _busy = false; Raise(); }
    }
    public void MarkFreshChat() { _newChatRequired = false; Raise(); }
    public void RefreshLanguage() { foreach (var row in NativeToolsets) row.RefreshLanguage(); foreach (var mode in NativeApprovalModes) mode.RefreshLanguage(); Raise(); }
    private void Raise()
    {
        foreach (var row in NativeToolsets) row.SetControlsEnabled(CanEditNativeTools, !_dirty);
        foreach (var group in NativeToolGroups) group.Refresh();
        foreach (var row in NativeMcpCatalog) row.SetControlsEnabled(CanEditNativeTools && !_dirty);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
