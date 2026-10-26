using System.ComponentModel;
using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal sealed record UsageBudgetLimitRow(string Label, string Value);

// Ephemeral editor only. Hermes owns the profile preference and its revision.
internal sealed class UsageBudgetViewModel : INotifyPropertyChanged
{
    private IUsageBudgetProjection? _projection;
    private CancellationTokenSource? _lifetime;
    private int _generation, _previewLevel = 5;
    private long _previewSelectionVersion, _previewReadVersion;
    private bool _busy, _saving, _supported, _uncertain;
    private HermesUsageBudgetPreview _preview = HermesUsageBudgetPreviewContract.InformationReferencePreview;
    private string _statusKey = "budget.disconnected";
    private HermesUsageBudgetPreference? _saved;
    internal Task PreviewReadCompletion { get; private set; } = Task.CompletedTask;
    private static ControlCenterLocalization T => ControlCenterLocalization.Current;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanEdit => _projection is not null && _supported && !_busy && !_uncertain && _saved is not null;
    public bool CanPreview => !_saving;
    public bool CanRefresh => _projection is not null && !_busy;
    public bool IsDirty => _saved is not null && _previewLevel != _saved.Level;
    public bool CanSave => CanEdit && IsDirty;
    public bool IsBusy => _busy;
    public int PreviewLevel
    {
        get => _previewLevel;
        set { if (!CanPreview || value is < 1 or > 6 || value == _previewLevel) return;
            _previewSelectionVersion++; _previewLevel = value;
            if (CanEdit) _statusKey = IsDirty ? "budget.preview" : "budget.loaded";
            Raise(); }
    }
    private string LevelName(int level) => T[level == 5 && _preview.Levels[4].Strategy?.HistoryTurns is null
        ? "budget.legacyLevel5" : level == 6 && _preview.Levels[5].Strategy?.HistoryTurns is null
        ? "budget.legacyLevel6" : "budget.level" + level];
    private bool StructuredSix => _previewLevel == 6 && _preview.Levels[5].Strategy?.HistoryTurns is not null;
    public string LevelText => T.Format("budget.level", _previewLevel, LevelName(_previewLevel));
    public string ModeName => LevelName(_previewLevel);
    public string ModeIndex => $"{_previewLevel:00} / 06";
    public string ModeSummary => _preview.Levels[_previewLevel - 1].Strategy is { HistoryTurns: { } turns } strategy
        ? T.Format("budget.strategy.overview", turns, strategy.RelationshipEvery)
        : _previewLevel == 5 && _preview.Levels[0].Strategy is not null
        ? T["budget.strategy.normalOverview"] : T["budget.summary" + _previewLevel];
    public string PreviewCaption => T["budget.previewCaption"];
    public bool ShowCostWarning => _previewLevel == 6;
    public bool ShowEstimateNote => _previewLevel <= 4 && _preview.Levels[0].Strategy is null;
    public bool ShowSavedSummary => _saved is not null && _supported && !_uncertain;
    public bool ShowStatusNotice => !ShowSavedSummary || _busy || IsDirty || _statusKey == "budget.changed";
    public string CompactSavedText => ShowSavedSummary ? T.Format("budget.savedCompact", _saved!.Level) : string.Empty;
    public string SavedText => _saved is null || _uncertain ? T["budget.savedUnknown"]
        : T.Format("budget.savedLevel", _saved.Level, LevelName(_saved.Level));
    public string StatusText => T[_statusKey];
    // No adopted-turn receipt exists in this UI. Never infer applied from saved.
    public string ActivationText => _saved is null || !_supported || _uncertain ? string.Empty : T["budget.nextTurn"];
    public string LevelHelp => T[StructuredSix ? "budget.strategy.richHelp" : _previewLevel == 6 ? "budget.unboundedHelp"
        : _preview.Levels[0].Strategy is not null ? "budget.strategy.note" : _previewLevel == 5
        ? "budget.normalHelp" : "budget.economyHelp"];
    public string PreviewSourceText => T[_preview.Scope == "configured_preview"
        ? "budget.limits.configured" : "budget.limits.reference"];
    public string LimitsNote => T[StructuredSix ? "budget.strategy.richNote" : _previewLevel == 6 ? "budget.limits.note6"
        : _preview.Levels[0].Strategy is not null ? "budget.strategy.note"
        : _previewLevel == 5 ? "budget.limits.note5" : "budget.limits.noteEconomy"];
    public IReadOnlyList<UsageBudgetLimitRow> LimitRows
    {
        get
        {
            var row = _preview.Levels.Single(value => value.Level == _previewLevel);
            if (row.Strategy is { } strategy)
            {
                string Every(long n) => T.Format("budget.strategy.every", n);
                string NativeLimit(long? value, bool tokens) => _previewLevel == 6 ? T["budget.limits.noHumanCap"]
                    : value is { } count ? T.Format(tokens ? "budget.limits.tokens" : "budget.limits.times", count)
                    : T["budget.limits.noAddedCap"];
                return new[]
                {
                    new UsageBudgetLimitRow(T["budget.strategy.historyLabel"], strategy.HistoryTurns is { } history
                        ? T.Format("budget.strategy.history", history) : T["budget.strategy.nativeHistory"]),
                    new UsageBudgetLimitRow(T["budget.strategy.relationship"], Every(strategy.RelationshipEvery)),
                    new UsageBudgetLimitRow(T["budget.strategy.summary"], strategy.HistoryTurns is null
                        ? T["budget.strategy.nativeSummary"] : Every(strategy.SummaryEvery)),
                    new UsageBudgetLimitRow(T["budget.strategy.summarySize"], strategy.SummaryTargetTokens is { } tokens
                        ? T.Format("budget.strategy.targetTokens", tokens) : T["budget.limits.noAddedCap"]),
                    new UsageBudgetLimitRow(T["budget.strategy.tools"], strategy.OldToolChars is { } chars
                        ? T.Format("budget.strategy.chars", chars) : T["budget.limits.noAddedCap"]),
                    new UsageBudgetLimitRow(T["budget.strategy.background"], strategy.HistoryTurns is null
                        ? T["budget.strategy.enabledNative"] : T.Format("budget.strategy.enabledEvery", strategy.BackgroundEvery)),
                    new UsageBudgetLimitRow(T["budget.strategy.profile"], T["budget.strategy.immediate"]),
                    new UsageBudgetLimitRow(T["budget.limits.output"], NativeLimit(row.RequestOutputTokens, true)),
                    new UsageBudgetLimitRow(T["budget.limits.iterations"], NativeLimit(row.LogicalIterations, false))
                };
            }
            string Format(long? number, bool tokens, bool shared = false) => number is { } value
                ? T.Format(tokens ? "budget.limits.tokens" : "budget.limits.times", value)
                : T[_previewLevel == 6 ? "budget.limits.noHumanCap"
                    : _previewLevel == 5 && shared ? "budget.limits.noAddedCap"
                    : _preview.Scope == "reference_preview" ? "budget.limits.originalUnknown" : "budget.limits.nativeDefault"];
            return new[]
            {
                new UsageBudgetLimitRow(T["budget.limits.input"], Format(row.RequestInputTokens, true)),
                new UsageBudgetLimitRow(T["budget.limits.output"], Format(row.RequestOutputTokens, true)),
                new UsageBudgetLimitRow(T["budget.limits.thinking"], Format(row.RequestThinkingTokens, true)),
                new UsageBudgetLimitRow(T["budget.limits.iterations"], Format(row.LogicalIterations, false)),
                new UsageBudgetLimitRow(T["budget.limits.calls"], Format(row.TurnActualCallAllowance, false, shared: true)),
                new UsageBudgetLimitRow(T["budget.limits.total"], Format(row.TurnTokenAllowance, true, shared: true)),
                new UsageBudgetLimitRow(T["budget.limits.memory"], T["budget.limits.noCountCap"]),
                new UsageBudgetLimitRow(T["budget.limits.history"], T["budget.limits.noCountCap"])
            };
        }
    }
    internal HermesUsageBudgetPreference? SavedPreference => _saved;

    public void Attach(IUsageBudgetProjection projection)
    {
        if (ReferenceEquals(_projection, projection)) return;
        Detach(); _projection = projection; _lifetime = new(); _statusKey = "budget.notLoaded"; Raise();
    }
    public void Detach()
    {
        _generation++; _previewReadVersion++; _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        _projection = null; _saved = null; _supported = _busy = _saving = _uncertain = false;
        _preview = HermesUsageBudgetPreviewContract.InformationReferencePreview;
        _statusKey = "budget.disconnected"; Raise();
    }
    public async Task RefreshAsync(bool background = false)
    {
        if (!CanRefresh || _projection is not { } projection || _lifetime is not { } lifetime) return;
        var generation = _generation;
        var selectionVersion = _previewSelectionVersion;
        var previewReadVersion = ++_previewReadVersion;
        var readPreference = false;
        if (!background) _preview = HermesUsageBudgetPreviewContract.InformationReferencePreview;
        _busy = true; if (!background) _statusKey = "common.loading"; Raise();
        try
        {
            var snapshot = await projection.ReadUsageBudgetAsync(lifetime.Token);
            if (generation != _generation) return;
            if (!snapshot.Supported) { _saved = null; _supported = false; _uncertain = false; _statusKey = "budget.unsupported"; }
            else
            {
                var preference = snapshot.Preference ?? throw new InvalidDataException("Missing economic preference.");
                _ = HermesUsageBudgetContract.Digest(preference);
                _saved = preference;
                if (selectionVersion == _previewSelectionVersion) _previewLevel = preference.Level;
                _supported = true; _uncertain = false;
                if (snapshot.Preview is { } preview) _preview = preview;
                _statusKey = IsDirty ? "budget.preview" : "budget.loaded";
                readPreference = true;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                _uncertain = true;
                _statusKey = error switch
                {
                    UsageBudgetOperationException { Code: "budget.host_required" } => "budget.readHostRequired",
                    TimeoutException => "budget.readTimedOut",
                    _ => "budget.readFailed"
                };
            }
        }
        finally { if (generation == _generation) { _busy = false; Raise(); } }
        // Preference availability is not coupled to preview latency. A failed
        // or late read-only description cannot invalidate the saved CAS state.
        if (readPreference && generation == _generation && !background)
            PreviewReadCompletion = RefreshPreviewAsync(projection, lifetime.Token, generation, previewReadVersion);
    }
    private async Task RefreshPreviewAsync(IUsageBudgetProjection projection, CancellationToken cancellationToken,
        int generation, long readVersion)
    {
        try
        {
            var preview = await projection.ReadUsageBudgetPreviewAsync(cancellationToken);
            if (preview is not null && generation == _generation && readVersion == _previewReadVersion)
            { _preview = preview; Raise(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (generation == _generation && readVersion == _previewReadVersion)
            { _preview = HermesUsageBudgetPreviewContract.InformationReferencePreview; Raise(); }
        }
    }
    public async Task SaveAsync()
    {
        if (!CanSave || _saved is not { } saved || _projection is not { } projection || _lifetime is not { } lifetime) return;
        var generation = _generation;
        var request = new HermesUsageBudgetUpdate(Guid.NewGuid().ToString("N"), saved.Revision, _previewLevel);
        _busy = _saving = true; _statusKey = "budget.saving"; Raise();
        try
        {
            var commit = await projection.SaveUsageBudgetAsync(request, lifetime.Token);
            if (generation != _generation) return;
            var receipt = commit.Receipt;
            if (receipt.OperationId != request.OperationId || receipt.ExpectedRevision != request.ExpectedRevision
                || receipt.Committed != new HermesUsageBudgetPreference(request.ExpectedRevision + 1, request.Level)
                || receipt.PreferenceDigest != HermesUsageBudgetContract.Digest(receipt.Committed)
                || commit.Preference.Revision < receipt.Committed.Revision
                || commit.Preference.Revision == receipt.Committed.Revision && commit.Preference != receipt.Committed
                || !commit.Replay && commit.Preference != receipt.Committed)
                throw new InvalidDataException("Economic receipt mismatch.");
            _ = HermesUsageBudgetContract.Digest(commit.Preference);
            _saved = commit.Preference; _previewLevel = commit.Preference.Level;
            _statusKey = commit.Preference == receipt.Committed ? "budget.saved" : "budget.changed";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                _uncertain = true;
                _statusKey = error is UsageBudgetOperationException { Code: "budget.revision_conflict" }
                    ? "budget.conflict" : "budget.uncertain";
            }
        }
        finally { if (generation == _generation) { _busy = _saving = false; Raise(); } }
    }
    public void RefreshLanguage() => Raise();
    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
