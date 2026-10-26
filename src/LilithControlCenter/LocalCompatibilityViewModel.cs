using System.ComponentModel;
using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

// Presentation only. Hermes owns this default-off, next-origin preference.
internal sealed class LocalCompatibilityViewModel : INotifyPropertyChanged
{
    private ILocalCompatibilityProjection? _projection;
    private CancellationTokenSource? _lifetime;
    private int _generation;
    private bool _busy, _supported, _uncertain, _enabled;
    private HermesLocalCompatibilityPreference? _saved;
    private string _statusKey = "local.compatibility.notLoaded";
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool CanEdit => _projection is not null && _supported && !_busy && !_uncertain && _saved is not null;
    public bool CanRefresh => _projection is not null && !_busy;
    public bool IsBusy => _busy;
    public bool IsDirty => !_uncertain && _saved is not null && _enabled != _saved.Enabled;
    public bool CanSave => CanEdit && IsDirty;
    public bool? Enabled
    {
        get => _saved is null || _uncertain ? null : _enabled;
        set { if (!CanEdit || value is not { } enabled || enabled == _enabled) return; _enabled = enabled; Raise(); }
    }
    public string StatusText => ControlCenterLocalization.Current[_statusKey];
    internal HermesLocalCompatibilityPreference? SavedPreference => _saved;
    public void Attach(ILocalCompatibilityProjection projection)
    {
        if (ReferenceEquals(_projection, projection)) return;
        Detach(); _projection = projection; _lifetime = new(); Raise();
    }
    public void Detach()
    {
        _generation++; _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
        _projection = null; _saved = null; _busy = _supported = _uncertain = _enabled = false;
        _statusKey = "local.compatibility.notLoaded"; Raise();
    }
    public async Task RefreshAsync(bool background = false)
    {
        if (!CanRefresh || background && IsDirty || _projection is not { } projection || _lifetime is not { } lifetime) return;
        var generation = _generation;
        _busy = true; if (!background) _statusKey = "common.loading"; Raise();
        try
        {
            var result = await projection.ReadLocalCompatibilityAsync(lifetime.Token);
            if (generation != _generation) return;
            if (!result.Supported)
            { _supported = false; _saved = null; _uncertain = false; _statusKey = "local.compatibility.unsupported"; }
            else
            {
                var preference = result.Preference ?? throw new InvalidDataException("Missing local compatibility preference.");
                _ = HermesLocalCompatibilityContract.Digest(preference);
                _saved = preference; _enabled = preference.Enabled; _supported = true; _uncertain = false;
                _statusKey = preference.Enabled ? "local.compatibility.savedOn" : "local.compatibility.savedOff";
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (generation == _generation) { _uncertain = true; _statusKey = "local.compatibility.readFailed"; }
        }
        finally { if (generation == _generation) { _busy = false; Raise(); } }
    }
    public async Task SaveAsync()
    {
        if (!CanSave || _saved is not { } saved || _projection is not { } projection || _lifetime is not { } lifetime) return;
        var generation = _generation;
        var request = new HermesLocalCompatibilityUpdate(Guid.NewGuid().ToString("N"), saved.Revision, _enabled);
        _busy = true; _statusKey = "local.compatibility.saving"; Raise();
        try
        {
            var result = await projection.SaveLocalCompatibilityAsync(request, lifetime.Token);
            if (generation != _generation) return;
            var receipt = result.Receipt;
            if (receipt.OperationId != request.OperationId || receipt.ExpectedRevision != request.ExpectedRevision
                || receipt.Committed != new HermesLocalCompatibilityPreference(request.ExpectedRevision + 1, request.Enabled)
                || receipt.PreferenceDigest != HermesLocalCompatibilityContract.Digest(receipt.Committed)
                || result.Preference.Revision < receipt.Committed.Revision
                || result.Preference.Revision == receipt.Committed.Revision && result.Preference != receipt.Committed
                || !result.Replay && result.Preference != receipt.Committed)
                throw new InvalidDataException("Local compatibility receipt mismatch.");
            _ = HermesLocalCompatibilityContract.Digest(result.Preference);
            _saved = result.Preference; _enabled = result.Preference.Enabled;
            _statusKey = result.Preference != receipt.Committed ? "local.compatibility.changed"
                : _enabled ? "local.compatibility.savedOn" : "local.compatibility.savedOff";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (generation == _generation)
            {
                _uncertain = true;
                _statusKey = error is LocalCompatibilityOperationException { Code: "local_compatibility.revision_conflict" }
                    ? "local.compatibility.conflict" : "local.compatibility.uncertain";
            }
        }
        finally { if (generation == _generation) { _busy = false; Raise(); } }
    }
    public void RefreshLanguage() => Raise();
    private void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
