using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace LilithControlCenter;

internal sealed class D057MemoryCategoryViewModel : INotifyPropertyChanged
{
    private int _count;
    private bool _isSelected;

    internal D057MemoryCategoryViewModel(
        D057MemoryCategory category,
        string label,
        string description)
    {
        Category = category;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public D057MemoryCategory Category { get; }
    public string Label => D057MemoryPresentation.CategoryLabel(Category);
    public string Description => D057MemoryPresentation.CategoryDescription(Category);

    public int Count
    {
        get => _count;
        internal set => SetField(ref _count, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }

    internal void RefreshLanguage()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class D057MemoryEntryViewModel : INotifyPropertyChanged
{
    private D057MemoryEntry _entry;

    internal D057MemoryEntryViewModel(D057MemoryEntry entry)
        => _entry = entry ?? throw new ArgumentNullException(nameof(entry));

    public event PropertyChangedEventHandler? PropertyChanged;

    internal D057MemoryEntry Entry => _entry;
    public string EntryId => _entry.EntryId;
    public long EntryVersion => _entry.EntryVersion;
    public D057MemoryCategory Category => _entry.Category;
    public string Content => _entry.Content;
    public string EvidenceExcerpt => _entry.EvidenceExcerpt ?? string.Empty;
    public bool HasEvidence => !string.IsNullOrWhiteSpace(_entry.EvidenceExcerpt);
    public bool IsPinned => _entry.Pinned;
    public bool IsHidden => _entry.State == D057MemoryState.Hidden;
    public bool IsCandidate => _entry.Category == D057MemoryCategory.Candidate
        && _entry.State == D057MemoryState.Active;
    public bool IsTemporary => _entry.Category == D057MemoryCategory.Temporary
        && _entry.State == D057MemoryState.Active;
    public bool IsRelationshipEpisode =>
        _entry.Category == D057MemoryCategory.RelationshipEpisode;
    public bool CanPin => _entry.Category == D057MemoryCategory.RelationshipCore
        && _entry.State == D057MemoryState.Active;
    public bool CanCorrect => !IsRelationshipEpisode
        && !IsHidden
        && _entry.State == D057MemoryState.Active;
    public bool CanForget => !IsRelationshipEpisode
        && _entry.State is D057MemoryState.Active or D057MemoryState.Hidden;
    public bool CanPermanentlyDelete => !IsRelationshipEpisode
        && (IsHidden
            || _entry.State is D057MemoryState.Rejected
                or D057MemoryState.Expired
                or D057MemoryState.Superseded);
    public string PinActionText => IsPinned ? ControlCenterLocalization.Current.Get("memory.unpin") : ControlCenterLocalization.Current.Get("memory.pin");
    public string VisibilityActionText => IsHidden ? ControlCenterLocalization.Current.Get("memory.restore") : ControlCenterLocalization.Current.Get("memory.forget");
    public string CategoryText => D057MemoryPresentation.CategoryLabel(Category);
    public string SourceText => D057MemoryPresentation.SourceLabel(_entry.Source);
    public string StateText => D057MemoryPresentation.StateLabel(_entry.State);
    public string StateBrush => D057MemoryPresentation.StateBrush(_entry.State);
    public string ObservedTimeText => ControlCenterLocalization.Current.Format(
        "memory.observed", _entry.ObservedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
    public string UpdatedTimeText => _entry.UpdatedAtUtc == _entry.ObservedAtUtc
        ? string.Empty
        : ControlCenterLocalization.Current.Format("memory.updated", _entry.UpdatedAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
    public string ExpiryText => _entry.ExpiresAtUtc is { } expires
        ? ControlCenterLocalization.Current.Format("memory.expires", expires.ToLocalTime().ToString("yyyy/MM/dd HH:mm"))
        : string.Empty;

    internal void RefreshLanguage()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    internal void Update(D057MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!string.Equals(entry.EntryId, EntryId, StringComparison.Ordinal))
            throw new ArgumentException("Memory entry identity changed.", nameof(entry));
        _entry = entry;
        foreach (var property in typeof(D057MemoryEntryViewModel)
                     .GetProperties()
                     .Where(static property => property.Name != nameof(Entry)))
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(property.Name));
        }
    }
}

internal enum D057MemoryEditorMode
{
    None = 0,
    Add = 1,
    Correct = 2
}

internal sealed record D057MemoryEditorCategoryChoice(
    D057MemoryCategory Category,
    string InitialLabel) : INotifyPropertyChanged
{
    public string Label => D057MemoryPresentation.CategoryLabel(Category);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
}

internal sealed class D057MemoryManagementViewModel : INotifyPropertyChanged
{
    private readonly List<D057MemoryEntryViewModel> _allEntries = new();
    private ID057MemoryManagementProjection? _projection;
    private D057MemoryCategory _selectedCategory =
        D057MemoryCategory.RelationshipCore;
    private string _searchText = string.Empty;
    private string _statusKey = "memory.disconnected";
    private object[] _statusArguments = Array.Empty<object>();
    private string _statusBrush = "#936110";
    private bool _isBusy;
    private bool _hasAuthoritativeCatalog;
    private bool _preferencesAvailable;
    private bool _autoMemoryEnabled;
    private bool _exportSupported;
    private long _catalogRevision;
    private long _preferencesRevision;
    private D057MemoryEditorMode _editorMode;
    private D057MemoryEntryViewModel? _editorTarget;
    private string _editorContent = string.Empty;
    private D057MemoryCategory _editorCategory = D057MemoryCategory.LongTerm;
    private bool _editorPinned;
    private D057MemoryEntryViewModel? _pendingPermanentDelete;

    internal D057MemoryManagementViewModel()
    {
        Categories = new ReadOnlyCollection<D057MemoryCategoryViewModel>(
            new[]
            {
                new D057MemoryCategoryViewModel(
                    D057MemoryCategory.RelationshipCore,
                    ControlCenterLocalization.Current.Get("memory.categoryCore"),
                    ControlCenterLocalization.Current.Get("memory.descriptionCore")),
                new D057MemoryCategoryViewModel(
                    D057MemoryCategory.LongTerm,
                    ControlCenterLocalization.Current.Get("memory.categoryLongTerm"),
                    ControlCenterLocalization.Current.Get("memory.descriptionLongTerm")),
                new D057MemoryCategoryViewModel(
                    D057MemoryCategory.Candidate,
                    ControlCenterLocalization.Current.Get("memory.categoryCandidate"),
                    ControlCenterLocalization.Current.Get("memory.descriptionCandidate")),
                new D057MemoryCategoryViewModel(
                    D057MemoryCategory.Temporary,
                    ControlCenterLocalization.Current.Get("memory.categoryTemporary"),
                    ControlCenterLocalization.Current.Get("memory.descriptionTemporary")),
                new D057MemoryCategoryViewModel(
                    D057MemoryCategory.RelationshipEpisode,
                    ControlCenterLocalization.Current.Get("memory.categoryEpisode"),
                    ControlCenterLocalization.Current.Get("memory.descriptionEpisode"))
            });
        Categories[0].IsSelected = true;
        EditorCategoryChoices = new ReadOnlyCollection<
            D057MemoryEditorCategoryChoice>(
            new[]
            {
                new D057MemoryEditorCategoryChoice(
                    D057MemoryCategory.RelationshipCore,
                    ControlCenterLocalization.Current.Get("memory.categoryCore")),
                new D057MemoryEditorCategoryChoice(
                    D057MemoryCategory.LongTerm,
                    ControlCenterLocalization.Current.Get("memory.categoryLongTerm")),
                new D057MemoryEditorCategoryChoice(
                    D057MemoryCategory.Temporary,
                    ControlCenterLocalization.Current.Get("memory.categoryTemporary"))
            });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<D057MemoryCategoryViewModel> Categories { get; }
    public IReadOnlyList<D057MemoryEditorCategoryChoice>
        EditorCategoryChoices { get; }
    public ObservableCollection<D057MemoryEntryViewModel> Entries { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetField(ref _searchText, value ?? string.Empty))
                return;
            ApplyFilter();
        }
    }

    public string StatusText => ControlCenterLocalization.Current.Format(_statusKey, _statusArguments);

    private void SetStatus(string key, params object[] arguments)
    {
        _statusKey = key;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(StatusText));
    }

    public string StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                RaiseAvailability();
        }
    }

    public bool HasAuthoritativeCatalog
    {
        get => _hasAuthoritativeCatalog;
        private set
        {
            if (SetField(ref _hasAuthoritativeCatalog, value))
                RaiseAvailability();
        }
    }

    public bool CanManage => HasAuthoritativeCatalog && !IsBusy;
    public bool CanRefresh => _projection is not null && !IsBusy;
    public bool CanExport => CanManage && _exportSupported;
    public bool CanToggleAutoMemory =>
        CanManage && _preferencesAvailable;
    public bool HasEntries => Entries.Count != 0;
    public bool HasNoEntries => HasAuthoritativeCatalog && Entries.Count == 0;
    public string RevisionText => HasAuthoritativeCatalog
        ? ControlCenterLocalization.Current.Format("memory.revision", _catalogRevision)
        : ControlCenterLocalization.Current.Get("memory.noRevision");

    public bool AutoMemoryEnabled
    {
        get => _autoMemoryEnabled;
        private set => SetField(ref _autoMemoryEnabled, value);
    }

    public bool IsEditorOpen => _editorMode != D057MemoryEditorMode.None;
    public string EditorTitle => _editorMode == D057MemoryEditorMode.Correct
        ? ControlCenterLocalization.Current.Get("memory.correct")
        : ControlCenterLocalization.Current.Get("memory.add");
    public string EditorSubmitText => _editorMode == D057MemoryEditorMode.Correct
        ? ControlCenterLocalization.Current.Get("memory.saveCorrection")
        : ControlCenterLocalization.Current.Get("memory.submitAdd");
    public bool IsAddEditor => _editorMode == D057MemoryEditorMode.Add;

    public string EditorContent
    {
        get => _editorContent;
        set
        {
            if (SetField(ref _editorContent, value ?? string.Empty))
                OnPropertyChanged(nameof(CanSubmitEditor));
        }
    }

    public D057MemoryCategory EditorCategory
    {
        get => _editorCategory;
        set
        {
            if (!SetField(ref _editorCategory, value))
                return;
            if (value != D057MemoryCategory.RelationshipCore)
                EditorPinned = false;
            OnPropertyChanged(nameof(CanPinEditorEntry));
        }
    }

    public bool EditorPinned
    {
        get => _editorPinned;
        set => SetField(ref _editorPinned, value);
    }

    public bool CanPinEditorEntry =>
        EditorCategory == D057MemoryCategory.RelationshipCore;
    public bool CanSubmitEditor =>
        CanManage
        && !string.IsNullOrWhiteSpace(EditorContent)
        && Encoding.UTF8.GetByteCount(EditorContent.Trim())
            <= D057MemoryContractLimits.MaximumContentUtf8Bytes;

    public bool IsPermanentDeleteOpen => _pendingPermanentDelete is not null;
    public string PermanentDeleteSummary => _pendingPermanentDelete is null
        ? string.Empty
        : "「" + TrimForSummary(_pendingPermanentDelete.Content) + "」";

    internal void RefreshLanguage()
    {
        foreach (var category in Categories) category.RefreshLanguage();
        foreach (var category in EditorCategoryChoices) category.RefreshLanguage();
        foreach (var entry in _allEntries) entry.RefreshLanguage();
        OnPropertyChanged(null);
        OnPropertyChanged(nameof(EditorCategoryChoices));
    }

    internal void AttachProjection(ID057MemoryManagementProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (ReferenceEquals(_projection, projection))
            return;
        _projection = projection;
        ResetAuthoritativeState(
            "memory.connected",
            "#52627A");
        OnPropertyChanged(nameof(CanRefresh));
    }

    internal void DetachProjection()
    {
        _projection = null;
        ResetAuthoritativeState(
            "memory.connectionChanged",
            "#936110");
        OnPropertyChanged(nameof(CanRefresh));
    }

    internal void SelectCategory(D057MemoryCategory category)
    {
        if (!Enum.IsDefined(typeof(D057MemoryCategory), category))
            throw new ArgumentOutOfRangeException(nameof(category));
        _selectedCategory = category;
        foreach (var item in Categories)
            item.IsSelected = item.Category == category;
        ApplyFilter();
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var projection = _projection;
        if (projection is null || IsBusy)
            return;

        IsBusy = true;
        HasAuthoritativeCatalog = false;
        _preferencesAvailable = false;
        SetStatus("memory.reading");
        StatusBrush = "#52627A";
        _allEntries.Clear();
        Entries.Clear();
        RefreshCategoryCounts();
        RaiseCollectionState();
        try
        {
            var catalog = await projection.GetMemoryCatalogAsync(cancellationToken);
            D057MemoryPreferences? preferences = null;
            string? preferencesWarning = null;
            try
            {
                preferences = await projection.GetMemoryPreferencesAsync(
                    cancellationToken);
            }
            catch (D057MemoryManagementUnavailableException)
            {
                preferencesWarning = "memory.countPreferencesUnavailable";
            }
            catch (D057MemoryManagementOperationException)
            {
                preferencesWarning = "memory.countPreferencesUnconfirmed";
            }

            ApplyCatalog(catalog);
            if (preferences is not null)
                ApplyPreferences(preferences);
            else
                _preferencesAvailable = false;

            SetStatus(preferencesWarning ?? "memory.count", catalog.Entries.Count);
            StatusBrush = preferencesWarning is null ? "#1D795A" : "#936110";
        }
        catch (D057MemoryManagementUnavailableException)
        {
            ResetAuthoritativeState(
                "memory.unavailable",
                "#936110");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetAuthoritativeState(
                "memory.cancelled",
                "#936110");
        }
        catch (Exception)
        {
            ResetAuthoritativeState(
                "memory.readFailed",
                "#BD354B");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void BeginAdd()
    {
        if (!CanManage)
            return;
        _editorMode = D057MemoryEditorMode.Add;
        _editorTarget = null;
        _editorContent = string.Empty;
        _editorCategory = D057MemoryCategory.LongTerm;
        _editorPinned = false;
        RaiseEditorState();
    }

    internal void BeginCorrection(D057MemoryEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.CanCorrect)
            return;
        _editorMode = D057MemoryEditorMode.Correct;
        _editorTarget = entry;
        _editorContent = entry.Content;
        _editorCategory = entry.Category;
        _editorPinned = entry.IsPinned;
        RaiseEditorState();
    }

    internal void CancelEditor()
    {
        _editorMode = D057MemoryEditorMode.None;
        _editorTarget = null;
        _editorContent = string.Empty;
        _editorPinned = false;
        RaiseEditorState();
    }

    internal async Task CommitEditorAsync(CancellationToken cancellationToken)
    {
        if (!CanSubmitEditor || _projection is null)
            return;
        var content = EditorContent.Trim();
        if (_editorMode == D057MemoryEditorMode.Add)
        {
            var request = new D057MemoryAddRequest(
                NewOperationId(),
                _catalogRevision,
                EditorCategory,
                content,
                EditorPinned && CanPinEditorEntry);
            await ExecuteMutationAsync(
                token => _projection.AddMemoryAsync(request, token),
                originalEntryId: null,
                "memory.added",
                cancellationToken);
        }
        else if (_editorMode == D057MemoryEditorMode.Correct
                 && _editorTarget is { } target)
        {
            var request = new D057MemoryCorrectRequest(
                NewOperationId(),
                _catalogRevision,
                target.EntryId,
                target.EntryVersion,
                content);
            await ExecuteMutationAsync(
                token => _projection.CorrectMemoryAsync(request, token),
                target.EntryId,
                "memory.corrected",
                cancellationToken);
        }
    }

    internal async Task TogglePinAsync(
        D057MemoryEntryViewModel entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.CanPin || _projection is null)
            return;
        var request = new D057MemoryPinRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion,
            !entry.IsPinned);
        await ExecuteMutationAsync(
            token => _projection.SetMemoryPinAsync(request, token),
            entry.EntryId,
            request.Pinned ? "memory.pinned" : "memory.unpinned",
            cancellationToken);
    }

    internal async Task AcceptCandidateAsync(
        D057MemoryEntryViewModel entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.IsCandidate || _projection is null)
            return;
        var request = new D057MemoryPromoteRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion,
            D057MemoryCategory.LongTerm);
        await ExecuteMutationAsync(
            token => _projection.PromoteMemoryAsync(request, token),
            entry.EntryId,
            "memory.accepted",
            cancellationToken);
    }

    internal async Task PromoteTemporaryAsync(
        D057MemoryEntryViewModel entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.IsTemporary || _projection is null)
            return;
        var request = new D057MemoryPromoteRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion,
            D057MemoryCategory.LongTerm);
        await ExecuteMutationAsync(
            token => _projection.PromoteMemoryAsync(request, token),
            entry.EntryId,
            "memory.promoted",
            cancellationToken);
    }

    internal async Task RejectCandidateAsync(
        D057MemoryEntryViewModel entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.IsCandidate || _projection is null)
            return;
        var request = new D057MemoryEntryMutationRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion);
        await ExecuteMutationAsync(
            token => _projection.RejectMemoryAsync(request, token),
            entry.EntryId,
            "memory.rejected",
            cancellationToken);
    }

    internal async Task ToggleVisibilityAsync(
        D057MemoryEntryViewModel entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.CanForget || _projection is null)
            return;
        var request = new D057MemoryVisibilityRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion,
            Hidden: !entry.IsHidden);
        await ExecuteMutationAsync(
            token => _projection.SetMemoryVisibilityAsync(request, token),
            entry.EntryId,
            request.Hidden
                ? "memory.forgotten"
                : "memory.restored",
            cancellationToken);
    }

    internal void BeginPermanentDelete(D057MemoryEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!CanManage || !entry.CanPermanentlyDelete)
            return;
        _pendingPermanentDelete = entry;
        RaisePermanentDeleteState();
    }

    internal void CancelPermanentDelete()
    {
        _pendingPermanentDelete = null;
        RaisePermanentDeleteState();
    }

    internal async Task ConfirmPermanentDeleteAsync(
        CancellationToken cancellationToken)
    {
        if (!CanManage
            || _pendingPermanentDelete is not { } entry
            || _projection is null)
        {
            return;
        }

        var request = new D057MemoryPermanentDeleteRequest(
            NewOperationId(),
            _catalogRevision,
            entry.EntryId,
            entry.EntryVersion);
        IsBusy = true;
        try
        {
            var receipt = await _projection.PermanentlyDeleteMemoryAsync(
                request,
                cancellationToken);
            if (!receipt.Deleted
                || !string.Equals(
                    receipt.EntryId,
                    entry.EntryId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Runtime did not confirm the requested permanent deletion.");
            }
            _catalogRevision = receipt.CatalogRevision;
            _allEntries.RemoveAll(item => string.Equals(
                item.EntryId,
                receipt.EntryId,
                StringComparison.Ordinal));
            _pendingPermanentDelete = null;
            RefreshCategoryCounts();
            ApplyFilter();
            SetStatus("memory.deleted");
            StatusBrush = "#1D795A";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("memory.deleteCancelled");
            StatusBrush = "#936110";
        }
        catch (Exception)
        {
            SetStatus("memory.deleteUnconfirmed");
            StatusBrush = "#BD354B";
        }
        finally
        {
            IsBusy = false;
            RaisePermanentDeleteState();
        }
    }

    internal async Task SetAutoMemoryEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        if (!CanToggleAutoMemory || _projection is null)
            return;
        var previous = AutoMemoryEnabled;
        IsBusy = true;
        try
        {
            var request = new D057MemoryPreferencesSetRequest(
                NewOperationId(),
                _preferencesRevision,
                enabled);
            var receipt = await _projection.SetMemoryPreferencesAsync(
                request,
                cancellationToken);
            ApplyPreferences(receipt);
            SetStatus(enabled ? "memory.autoOn" : "memory.autoOff");
            StatusBrush = "#1D795A";
        }
        catch (Exception)
        {
            AutoMemoryEnabled = previous;
            OnPropertyChanged(nameof(AutoMemoryEnabled));
            SetStatus("memory.settingsUnconfirmed");
            StatusBrush = "#BD354B";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (!CanExport || _projection is null)
            return;
        IsBusy = true;
        try
        {
            var request = new D057MemoryExportRequest(
                NewOperationId(),
                _catalogRevision);
            var receipt = await _projection.ExportMemoryAsync(
                request,
                cancellationToken);
            SetStatus("memory.exported", receipt.EntryCount, receipt.Path);
            StatusBrush = "#1D795A";
        }
        catch (Exception)
        {
            SetStatus("memory.exportUnconfirmed");
            StatusBrush = "#BD354B";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteMutationAsync(
        Func<CancellationToken, Task<D057MemoryMutationReceipt>> operation,
        string? originalEntryId,
        string successText,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            var receipt = await operation(cancellationToken);
            ApplyMutationReceipt(receipt, originalEntryId);
            CancelEditor();
            SetStatus(successText);
            StatusBrush = "#1D795A";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("memory.mutationCancelled");
            StatusBrush = "#936110";
        }
        catch (Exception)
        {
            SetStatus("memory.mutationUnconfirmed");
            StatusBrush = "#BD354B";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyCatalog(D057MemoryCatalog catalog)
    {
        _catalogRevision = catalog.CatalogRevision;
        _exportSupported = catalog.ExportSupported;
        _allEntries.Clear();
        _allEntries.AddRange(catalog.Entries
            .OrderByDescending(static entry => entry.Pinned)
            .ThenByDescending(static entry => entry.UpdatedAtUtc)
            .Select(static entry => new D057MemoryEntryViewModel(entry)));
        HasAuthoritativeCatalog = true;
        RefreshCategoryCounts();
        ApplyFilter();
        RaiseAvailability();
    }

    private void ApplyPreferences(D057MemoryPreferences preferences)
    {
        _preferencesRevision = preferences.PreferencesRevision;
        _preferencesAvailable = true;
        AutoMemoryEnabled = preferences.AutoMemoryEnabled;
        RaiseAvailability();
    }

    private void ApplyMutationReceipt(
        D057MemoryMutationReceipt receipt,
        string? originalEntryId)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _catalogRevision = receipt.CatalogRevision;
        if (originalEntryId is not null
            && !string.Equals(
                originalEntryId,
                receipt.Entry.EntryId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "記憶修改結果不屬於原本選取的項目。");
        }
        var existing = _allEntries.SingleOrDefault(item => string.Equals(
            item.EntryId,
            receipt.Entry.EntryId,
            StringComparison.Ordinal));
        if (existing is null)
            _allEntries.Add(new D057MemoryEntryViewModel(receipt.Entry));
        else
            existing.Update(receipt.Entry);
        _allEntries.Sort(static (left, right) =>
        {
            var pinned = right.IsPinned.CompareTo(left.IsPinned);
            return pinned != 0
                ? pinned
                : right.Entry.UpdatedAtUtc.CompareTo(left.Entry.UpdatedAtUtc);
        });
        RefreshCategoryCounts();
        ApplyFilter();
        OnPropertyChanged(nameof(RevisionText));
    }

    private void ApplyFilter()
    {
        Entries.Clear();
        var query = SearchText.Trim();
        foreach (var item in _allEntries.Where(item =>
                     item.Category == _selectedCategory
                     && (query.Length == 0
                         || item.Content.Contains(
                             query,
                             StringComparison.CurrentCultureIgnoreCase)
                         || item.EvidenceExcerpt.Contains(
                             query,
                             StringComparison.CurrentCultureIgnoreCase))))
        {
            Entries.Add(item);
        }
        RaiseCollectionState();
    }

    private void RefreshCategoryCounts()
    {
        foreach (var category in Categories)
        {
            category.Count = _allEntries.Count(item =>
                item.Category == category.Category);
        }
    }

    private void ResetAuthoritativeState(string status, string brush)
    {
        _catalogRevision = 0;
        _preferencesRevision = 0;
        _preferencesAvailable = false;
        _exportSupported = false;
        _allEntries.Clear();
        Entries.Clear();
        HasAuthoritativeCatalog = false;
        AutoMemoryEnabled = false;
        CancelEditor();
        CancelPermanentDelete();
        RefreshCategoryCounts();
        SetStatus(status);
        StatusBrush = brush;
        RaiseCollectionState();
        RaiseAvailability();
    }

    private void RaiseAvailability()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanToggleAutoMemory));
        OnPropertyChanged(nameof(CanSubmitEditor));
        OnPropertyChanged(nameof(RevisionText));
    }

    private void RaiseCollectionState()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(HasNoEntries));
    }

    private void RaiseEditorState()
    {
        OnPropertyChanged(nameof(IsEditorOpen));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorSubmitText));
        OnPropertyChanged(nameof(IsAddEditor));
        OnPropertyChanged(nameof(EditorContent));
        OnPropertyChanged(nameof(EditorCategory));
        OnPropertyChanged(nameof(EditorPinned));
        OnPropertyChanged(nameof(CanPinEditorEntry));
        OnPropertyChanged(nameof(CanSubmitEditor));
    }

    private void RaisePermanentDeleteState()
    {
        OnPropertyChanged(nameof(IsPermanentDeleteOpen));
        OnPropertyChanged(nameof(PermanentDeleteSummary));
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string NewOperationId()
        => "cc-memory-" + Guid.NewGuid().ToString("N");

    private static string TrimForSummary(string content)
        => content.Length <= 80 ? content : content[..77] + "…";
}

internal static class D057MemoryPresentation
{
    internal static string CategoryLabel(D057MemoryCategory value)
        => value switch
        {
            D057MemoryCategory.RelationshipCore => ControlCenterLocalization.Current.Get("memory.categoryCore"),
            D057MemoryCategory.LongTerm => ControlCenterLocalization.Current.Get("memory.categoryLongTerm"),
            D057MemoryCategory.Candidate => ControlCenterLocalization.Current.Get("memory.categoryCandidate"),
            D057MemoryCategory.Temporary => ControlCenterLocalization.Current.Get("memory.categoryTemporary"),
            D057MemoryCategory.RelationshipEpisode => ControlCenterLocalization.Current.Get("memory.categoryEpisode"),
            _ => ControlCenterLocalization.Current.Get("memory.categoryUnknown")
        };

    internal static string CategoryDescription(D057MemoryCategory value)
        => ControlCenterLocalization.Current.Get(value switch
        {
            D057MemoryCategory.RelationshipCore => "memory.descriptionCore",
            D057MemoryCategory.LongTerm => "memory.descriptionLongTerm",
            D057MemoryCategory.Candidate => "memory.descriptionCandidate",
            D057MemoryCategory.Temporary => "memory.descriptionTemporary",
            D057MemoryCategory.RelationshipEpisode => "memory.descriptionEpisode",
            _ => "memory.categoryUnknown"
        });

    internal static string SourceLabel(D057MemorySource value)
        => value switch
        {
            D057MemorySource.ExplicitUser => ControlCenterLocalization.Current.Get("memory.sourceUser"),
            D057MemorySource.Inferred => ControlCenterLocalization.Current.Get("memory.sourceInferred"),
            D057MemorySource.Operator => ControlCenterLocalization.Current.Get("memory.sourceOperator"),
            D057MemorySource.RelationshipReview => ControlCenterLocalization.Current.Get("memory.sourceReview"),
            _ => ControlCenterLocalization.Current.Get("memory.sourceUnknown")
        };

    internal static string StateLabel(D057MemoryState value)
        => value switch
        {
            D057MemoryState.Active => ControlCenterLocalization.Current.Get("memory.stateActive"),
            D057MemoryState.Hidden => ControlCenterLocalization.Current.Get("memory.stateHidden"),
            D057MemoryState.Rejected => ControlCenterLocalization.Current.Get("memory.stateRejected"),
            D057MemoryState.Expired => ControlCenterLocalization.Current.Get("memory.stateExpired"),
            D057MemoryState.Superseded => ControlCenterLocalization.Current.Get("memory.stateSuperseded"),
            _ => ControlCenterLocalization.Current.Get("memory.stateUnknown")
        };

    internal static string StateBrush(D057MemoryState value)
        => value switch
        {
            D057MemoryState.Active => "#1D795A",
            D057MemoryState.Hidden => "#936110",
            D057MemoryState.Rejected => "#BD354B",
            D057MemoryState.Expired => "#52627A",
            D057MemoryState.Superseded => "#52627A",
            _ => "#52627A"
        };
}
