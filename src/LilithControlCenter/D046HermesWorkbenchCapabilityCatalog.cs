using System.Collections.ObjectModel;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal sealed partial class D046HermesWorkbenchViewModel
{
    private readonly D046HermesCapabilityCatalogReadCache<HermesCapabilityCatalog>
        _capabilityCatalogCache = new();
    private int? _capabilitySchema;
    private int _capabilityUnavailable;
    private int _capabilitySectionCount;
    private string _capabilityCatalogStatusText =
        ControlCenterLocalization.Current.Get("ui.repair.21");
    private string? _capabilityCatalogStatusKey;
    private string _capabilityCatalogBoundaryText =
        ControlCenterLocalization.Current.Get("ui.repair.22");
    private string _capabilityCatalogSummaryText = ControlCenterLocalization.Current.Get("ui.repair.23");

    public ObservableCollection<D046HermesCapabilityCatalogSectionViewModel>
        CapabilityCatalogSections { get; } = new();

    public string CapabilityCatalogStatusText
    {
        get
        {
            var texts = ControlCenterLocalization.Current;
            if (_capabilityCatalogCache.IsStopped || _projectionFault)
                return texts["catalog.disconnected"];
            if (_capabilityCatalogCache.Snapshot is not null)
            {
                var key = _capabilityCatalogCache.IsReading ? "catalog.refreshingSnapshot"
                    : _capabilityCatalogCache.LastReadFailed ? "catalog.failedSnapshot"
                    : _capabilityCatalogCache.IsInvalidated ? "catalog.changedSnapshot"
                    : "catalog.lastSnapshot";
                return texts.Format(key, _capabilityCatalogCache.CompletedUtc!.Value
                    .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            }
            return _capabilityCatalogStatusKey is not null
                ? texts[_capabilityCatalogStatusKey]
                : _capabilityCatalogStatusText;
        }
    }
    public string CapabilityCatalogBoundaryText => _capabilityCatalogCache.Snapshot is not null
        ? ControlCenterLocalization.Current["catalog.snapshotBoundary"]
        : _capabilityCatalogBoundaryText;
    public string CapabilityCatalogSummaryText => _capabilitySchema.HasValue
        ? ControlCenterLocalization.Current.Format("ui.catalog.summary", CapabilityCatalogSections.Count, _capabilitySectionCount - _capabilityUnavailable, _capabilitySectionCount)
        : _capabilityCatalogSummaryText;
    public bool IsCapabilityCatalogEmpty => CapabilityCatalogSections.Count == 0;
    public bool CanRefreshCapabilityCatalog => !_capabilityCatalogCache.IsReading
        && !_capabilityCatalogCache.IsStopped && !_projectionFault
        && !_lifetime.IsCancellationRequested;

    internal Task EnsureCapabilityCatalogLoadedAsync()
        => _capabilityCatalogCache.CanReuse && !_projectionFault
            ? Task.CompletedTask
            : RefreshCapabilityCatalogAsync();

    internal async Task RefreshCapabilityCatalogAsync()
    {
        if (_lifetime.IsCancellationRequested || _projectionFault
            || !_capabilityCatalogCache.TryBeginRead(out var ticket))
            return;

        // Rows are a historical display only. Keep them visible during an
        // explicit refresh; the timestamp/status must never imply live truth.
        _capabilityCatalogStatusKey = "catalog.firstRead";
        if (_capabilityCatalogCache.Snapshot is null)
            _capabilityCatalogSummaryText = ControlCenterLocalization.Current.Get("ui.repair.25");
        RaiseCapabilityCatalogProperties();
        try
        {
            var projection = _projection as ID046HermesCapabilityCatalogProjection
                ?? throw new NotSupportedException(
                    ControlCenterLocalization.Current.Get("ui.repair.26"));
            var catalog = await projection.GetCapabilityCatalogAsync(
                _lifetime.Token);
            var display = D046HermesCapabilityCatalogDisplayProjector.Project(
                catalog);
            if (!_capabilityCatalogCache.TryComplete(ticket, catalog, DateTimeOffset.UtcNow))
            {
                _capabilityCatalogStatusKey = "catalog.changed";
                return;
            }

            CapabilityCatalogSections.Clear();
            foreach (var section in display.Sections)
                CapabilityCatalogSections.Add(section);
            var unavailableSections = catalog.Sections.Count(section =>
                section.State == HermesCapabilitySectionState.Unavailable);
            _capabilitySchema = catalog.SchemaVersion;
            _capabilityUnavailable = unavailableSections;
            _capabilitySectionCount = catalog.Sections.Count;
            _capabilityCatalogBoundaryText =
                ControlCenterLocalization.Current.Get("ui.repair.27");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            _capabilityCatalogCache.Stop();
            _capabilityCatalogStatusKey = null;
            _capabilitySchema = null;
            CapabilityCatalogSections.Clear();
            _capabilityCatalogStatusText = ControlCenterLocalization.Current.Get("ui.repair.28");
            _capabilityCatalogBoundaryText =
                ControlCenterLocalization.Current.Get("ui.repair.29");
            _capabilityCatalogSummaryText = ControlCenterLocalization.Current.Get("ui.repair.23");
        }
        catch (Exception)
        {
            // A previous snapshot is explicitly labelled stale after failure.
            // Never surface raw Runtime errors or promote display into authority.
            _capabilityCatalogCache.Fail(ticket);
            _capabilityCatalogStatusKey = null;
            _capabilityCatalogStatusText =
                ControlCenterLocalization.Current.Get("ui.repair.30");
            _capabilityCatalogBoundaryText =
                ControlCenterLocalization.Current.Get("ui.repair.31");
            if (_capabilityCatalogCache.Snapshot is null)
                _capabilityCatalogSummaryText = ControlCenterLocalization.Current.Get("ui.repair.32");
        }
        finally
        {
            // No-op after a settled ticket; releases a cancelled current read.
            _capabilityCatalogCache.Fail(ticket);
            RaiseCapabilityCatalogProperties();
        }
    }

    private void InvalidateCapabilityCatalog(bool clear = false, bool stop = false)
    {
        InvalidateComposerCommandCatalog(clear, stop);
        if (stop) _capabilityCatalogCache.Stop();
        else if (clear) _capabilityCatalogCache.Reset();
        else _capabilityCatalogCache.Invalidate();
        if (clear || stop)
        {
            _capabilitySchema = null;
            CapabilityCatalogSections.Clear();
            _capabilityCatalogStatusKey = "catalog.changed";
            _capabilityCatalogSummaryText = ControlCenterLocalization.Current.Get("ui.repair.23");
        }
        RaiseCapabilityCatalogProperties();
    }

    private void RaiseCapabilityCatalogProperties()
    {
        OnPropertyChanged(nameof(CapabilityCatalogStatusText));
        OnPropertyChanged(nameof(CapabilityCatalogBoundaryText));
        OnPropertyChanged(nameof(CapabilityCatalogSummaryText));
        OnPropertyChanged(nameof(IsCapabilityCatalogEmpty));
        OnPropertyChanged(nameof(CanRefreshCapabilityCatalog));
    }

    private void RefreshCapabilityCatalogLanguage()
    {
        // Re-project the existing historical snapshot; language is not a reason
        // to make another request or upgrade an old snapshot into current truth.
        if (_capabilityCatalogCache.Snapshot is not { } catalog) return;
        var expansion = CapabilityCatalogSections.Select(section => section.IsExpanded).ToArray();
        var display = D046HermesCapabilityCatalogDisplayProjector.Project(catalog);
        CapabilityCatalogSections.Clear();
        for (var index = 0; index < display.Sections.Count; index++)
        {
            var section = display.Sections[index];
            if (index < expansion.Length) section.IsExpanded = expansion[index];
            CapabilityCatalogSections.Add(section);
        }
        RaiseCapabilityCatalogProperties();
    }
}
