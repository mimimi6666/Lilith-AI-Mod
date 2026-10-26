using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

// An in-memory, non-secret draft. Native config and fresh-generation read-back
// remain the authority; editing these properties never starts I/O.
public sealed class ProviderSetupEditorState : INotifyPropertyChanged
{
    private HermesProviderSetupCatalog? _catalog;
    private HermesProviderSetupEntry? _selected;
    private HermesProviderSetupPreset? _preset;
    private string _providerId = string.Empty, _label = string.Empty, _baseUrl = string.Empty, _modelId = string.Empty;
    private string _authMode = "api_key", _statusKey = "provider.setup.notLoaded";
    private bool _busy, _reading, _hasNewKey;
    private (string? Preset, string Label, string Url, string Model, string Auth) _draftBaseline;
    private long _draftVersion;
    private IReadOnlyList<string> _models = Array.Empty<string>();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? DraftReplaced;
    public IReadOnlyList<HermesProviderSetupEntry> Entries => _catalog?.Entries ?? Array.Empty<HermesProviderSetupEntry>();
    public IReadOnlyList<HermesProviderSetupPreset> Presets => _catalog?.Presets ?? Array.Empty<HermesProviderSetupPreset>();
    public HermesProviderSetupCatalog? Catalog => _catalog;
    public HermesProviderSetupEntry? SelectedEntry
    {
        get => _selected;
        set { if (!CanEdit || ReferenceEquals(value, _selected)) return; Select(value); }
    }
    public HermesProviderSetupPreset? SelectedPreset
    {
        get => _preset;
        set
        {
            if (!CanEdit || value is null || ReferenceEquals(value, _preset)) return;
            _preset = value; _baseUrl = value.BaseUrl; _authMode = value.AuthMode;
            _models = Array.Empty<string>(); _hasNewKey = false; DraftReplaced?.Invoke(); Changed();
        }
    }
    public string Label { get => _label; set { if (_label == value) return; _label = value; DraftPropertyChanged(); } }
    public string BaseUrl { get => _baseUrl; set { if (_baseUrl == value) return; _baseUrl = value; DraftPropertyChanged(); } }
    public string ModelId { get => _modelId; set { if (_modelId == value) return; _modelId = value; DraftPropertyChanged(); } }
    public string AuthMode
    {
        get => _authMode;
        set
        {
            if (_authMode == value) return;
            _authMode = value; _hasNewKey = false; DraftReplaced?.Invoke();
            DraftPropertyChanged(); Notify(nameof(HasNewKey), nameof(CanEnterKey));
        }
    }
    public bool HasNewKey
    {
        get => _hasNewKey;
        set { if (_hasNewKey == value) return; _hasNewKey = value; DraftPropertyChanged(); }
    }
    public bool Busy
    {
        get => _busy;
        set { if (_busy == value) return; _busy = value; NotifyInteraction(); }
    }
    // Metadata reads reserve the shared operation lane, not the editable draft.
    // In particular, a periodic read must not rebind or disable every text field.
    public bool Reading
    {
        get => _reading;
        set
        {
            if (_reading == value) return;
            _reading = value;
            Notify(nameof(Reading), nameof(CanRefresh), nameof(CanSave), nameof(CanRemove), nameof(CanReadModels));
        }
    }
    public bool CanRefresh => !Busy && !Reading;
    public bool CanEdit => !Busy && _catalog is not null;
    public bool CanSave => CanEdit && !Reading && _preset is not null && _providerId.Length != 0;
    public bool CanRemove => CanEdit && !Reading && _selected is { IsDefault: false };
    public bool CanUseNoAuth => _preset?.Kind == "local";
    public bool CanEnterKey => CanEdit && AuthMode == "api_key";
    public bool CanReadModels => CanEdit && !Reading && _selected is not null && IsSavedDraft && !HasNewKey;
    public bool IsSavedDraft => _selected is { } row && row.Label == Label && row.BaseUrl == BaseUrl
        && row.ModelId == ModelId && row.AuthMode == AuthMode && row.PresetId == _preset?.Id;
    public bool HasPendingChanges => HasNewKey || _catalog is not null
        && _draftBaseline != (_preset?.Id, Label, BaseUrl, ModelId, AuthMode);
    internal long DraftVersion => _draftVersion;
    public IReadOnlyList<string> Models => _models;
    public string Status => ControlCenterLocalization.Current[_statusKey];
    public string KeyHint => ControlCenterLocalization.Current[_selected?.KeyPresent == true
        ? "provider.setup.keyKeep" : "provider.setup.keyNew"];

    public void Apply(HermesProviderSetupCatalog catalog, string? preferredProviderId = null)
    {
        _catalog = catalog;
        Select(catalog.Entries.SingleOrDefault(e => e.ProviderId == preferredProviderId));
        _statusKey = "provider.setup.loaded"; Changed();
    }
    public void NewEntry() { if (CanEdit) Select(null); }
    private void Select(HermesProviderSetupEntry? entry)
    {
        _selected = entry;
        _providerId = entry?.ProviderId ?? "custom:" + Guid.NewGuid().ToString("N");
        _preset = _catalog?.Presets.FirstOrDefault(p => p.Id == entry?.PresetId) ?? _catalog?.Presets.FirstOrDefault();
        _label = entry?.Label ?? string.Empty;
        _baseUrl = entry?.BaseUrl ?? _preset?.BaseUrl ?? string.Empty;
        _modelId = entry?.ModelId ?? string.Empty;
        _authMode = entry?.AuthMode ?? _preset?.AuthMode ?? "api_key";
        _models = Array.Empty<string>(); _hasNewKey = false;
        _draftBaseline = (_preset?.Id, Label, BaseUrl, ModelId, AuthMode);
        DraftReplaced?.Invoke(); Changed();
    }
    public void Invalidate(string statusKey = "provider.setup.unknown")
    {
        _catalog = null; _selected = null; _preset = null; _models = Array.Empty<string>();
        _draftVersion++;
        _hasNewKey = false; _statusKey = statusKey; DraftReplaced?.Invoke(); Changed();
    }
    public void SetStatus(string resourceKey)
    {
        if (_statusKey == resourceKey) return;
        _statusKey = resourceKey; Notify(nameof(Status));
    }

    internal bool IsRemovalConfirmationCurrent(HermesProviderSetupCatalog catalog,
        HermesProviderSetupEntry entry, long draftVersion)
        => ReferenceEquals(_catalog, catalog) && ReferenceEquals(_selected, entry)
            && _draftVersion == draftVersion && !entry.IsDefault;

    // The caller must first admit this catalog from a fresh Runtime generation.
    // A current catalog is still usable when the write's receipt is missing or
    // disagrees with it; only the exact receipt/postcondition can report success.
    internal bool CompleteMutationReadback(HermesProviderSetupCatalog catalog,
        HermesProviderSetupMutation? receipt, string method, JsonElement request,
        HermesProviderSetupCatalog previousCatalog)
    {
        Apply(catalog, method == HermesProviderSetupContract.SaveMethod
            ? request.GetProperty("provider_id").GetString() : null);
        SetStatus("provider.setup.unknown");
        if (receipt is null) return false;
        HermesProviderSetupContract.RequirePostcondition(catalog, receipt, method, request, previousCatalog);
        SetStatus(receipt.Action == "save" ? "provider.setup.saved" : "provider.setup.removed");
        return true;
    }
    public void ApplyModels(HermesProviderSetupModels result)
    {
        if (_selected?.ProviderId != result.ProviderId || !IsSavedDraft) throw new InvalidDataException("Provider model result is stale.");
        _models = result.Models;
        _statusKey = "provider.setup.models." + result.Status;
        Changed();
    }
    public JsonElement CreateModelsRequest()
    {
        if (!CanReadModels) throw new InvalidOperationException("Save the exact endpoint before reading its catalog.");
        return HermesProviderSetupContract.CanonicalizeRequest(HermesProviderSetupContract.ModelsMethod,
            JsonSerializer.SerializeToElement(new { provider_id = _selected!.ProviderId, expected_config_sha256 = _catalog!.ConfigSha256 }));
    }
    public JsonElement CreateSaveRequest(string apiKey, string operationId)
    {
        if (!CanSave) throw new InvalidOperationException("Provider setup is not current.");
        var action = AuthMode == "none" ? "clear" : apiKey.Length != 0 ? "replace" : "keep";
        if (action == "keep" && (_selected?.KeyPresent != true || _selected.AuthMode != "api_key"))
            throw new ProviderSetupOperationException("setup.key_required");
        if (action == "keep" && _selected!.BaseUrl != BaseUrl)
            throw new ProviderSetupOperationException("setup.endpoint_changed_requires_key");
        if (AuthMode == "none" && apiKey.Length != 0) throw new ProviderSetupOperationException("setup.invalid_request");
        return HermesProviderSetupContract.CanonicalizeRequest(HermesProviderSetupContract.SaveMethod,
            JsonSerializer.SerializeToElement(new { expected_config_sha256 = _catalog!.ConfigSha256,
                operation_id = operationId, provider_id = _providerId, label = Label, preset_id = _preset!.Id,
                kind = _preset.Kind, base_url = BaseUrl, model_id = ModelId, auth_mode = AuthMode,
                key_action = action, api_key = apiKey }));
    }
    public JsonElement CreateRemoveRequest(string operationId)
    {
        if (!CanRemove) throw new InvalidOperationException("Provider removal is unavailable.");
        return HermesProviderSetupContract.CanonicalizeRequest(HermesProviderSetupContract.RemoveMethod,
            JsonSerializer.SerializeToElement(new { expected_config_sha256 = _catalog!.ConfigSha256,
                operation_id = operationId, provider_id = _selected!.ProviderId }));
    }
    public void RefreshLanguage() => Changed();
    private void DraftPropertyChanged([CallerMemberName] string? name = null)
    {
        _draftVersion++;
        Notify(name ?? string.Empty, nameof(IsSavedDraft), nameof(HasPendingChanges), nameof(CanReadModels));
    }
    private void NotifyInteraction() => Notify(nameof(Busy), nameof(CanRefresh), nameof(CanEdit),
        nameof(CanSave), nameof(CanRemove), nameof(CanEnterKey), nameof(CanReadModels));
    private void Notify(params string[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    private void Changed([CallerMemberName] string? name = null)
    {
        if (name is nameof(Label) or nameof(BaseUrl) or nameof(ModelId) or nameof(AuthMode)
            or nameof(SelectedPreset) or nameof(HasNewKey) or nameof(Select)) _draftVersion++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
