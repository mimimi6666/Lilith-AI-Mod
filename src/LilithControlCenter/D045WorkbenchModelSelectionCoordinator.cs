using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LilithProviderControlPlane;

namespace LilithControlCenter;

internal enum D045WorkbenchModelSelectionSource
{
    Refresh = 1,
    MainWindow = 2,
    Workbench = 3
}

internal sealed class D045WorkbenchModelCatalogEntry
{
    internal string InstanceId { get; }
    internal string ModelId { get; }
    internal long ConfigurationGeneration { get; }
    internal string DisplayName { get; }

    internal D045WorkbenchModelCatalogEntry(
        string instanceId,
        string modelId,
        long configurationGeneration,
        string displayName)
    {
        InstanceId = instanceId;
        ModelId = modelId;
        ConfigurationGeneration = configurationGeneration;
        DisplayName = displayName;
    }

    internal bool HasIdentity(string instanceId, string modelId)
        => string.Equals(InstanceId, instanceId, StringComparison.Ordinal)
            && string.Equals(ModelId, modelId, StringComparison.Ordinal);
}

internal sealed class D045WorkbenchModelCatalogSnapshot
{
    internal IReadOnlyList<D045WorkbenchModelCatalogEntry> Models { get; }
    internal D045WorkbenchModelCatalogEntry? GlobalSelection { get; }

    internal D045WorkbenchModelCatalogSnapshot(
        IEnumerable<D045WorkbenchModelCatalogEntry> models,
        D045WorkbenchModelCatalogEntry? globalSelection)
    {
        Models = new ReadOnlyCollection<D045WorkbenchModelCatalogEntry>(
            (models ?? throw new ArgumentNullException(nameof(models))).ToArray());
        GlobalSelection = globalSelection;
    }
}

internal sealed class D045WorkbenchModelSelectionChangedEventArgs : EventArgs
{
    internal D045WorkbenchModelCatalogSnapshot Snapshot { get; }
    internal D045WorkbenchModelSelectionSource Source { get; }
    internal bool CatalogChanged { get; }
    internal bool SelectionChanged { get; }

    internal D045WorkbenchModelSelectionChangedEventArgs(
        D045WorkbenchModelCatalogSnapshot snapshot,
        D045WorkbenchModelSelectionSource source,
        bool catalogChanged,
        bool selectionChanged)
    {
        Snapshot = snapshot;
        Source = source;
        CatalogChanged = catalogChanged;
        SelectionChanged = selectionChanged;
    }
}

internal sealed class D045WorkbenchModelSelectionCoordinator
{
    private readonly UnifiedProviderControlPlaneStore _providerStore;
    private readonly D045AiFunctionRoutingStore _routingStore;
    private readonly object _gate = new();
    private D045WorkbenchModelCatalogSnapshot? _lastSnapshot;

    internal event EventHandler<D045WorkbenchModelSelectionChangedEventArgs>?
        SelectionChanged;

    internal D045WorkbenchModelSelectionCoordinator(
        UnifiedProviderControlPlaneStore providerStore,
        D045AiFunctionRoutingStore routingStore)
    {
        _providerStore = providerStore
            ?? throw new ArgumentNullException(nameof(providerStore));
        _routingStore = routingStore
            ?? throw new ArgumentNullException(nameof(routingStore));
    }

    internal static D045WorkbenchModelSelectionCoordinator ForCurrentUser()
        => new(
            UnifiedProviderControlPlaneStore.ForCurrentUser(),
            D045AiFunctionRoutingStore.ForCurrentUser());

    internal D045WorkbenchModelCatalogSnapshot Refresh()
    {
        var snapshot = LoadSnapshot();
        PublishIfChanged(snapshot, D045WorkbenchModelSelectionSource.Refresh);
        return snapshot;
    }

    internal D045AiFunctionRoutingResult SelectGlobalWorkbenchModel(
        string instanceId,
        string modelId,
        long expectedConfigurationGeneration,
        D045WorkbenchModelSelectionSource source,
        D045AiFunctionRouteSelection? additionalSelection = null)
    {
        if (source is not (D045WorkbenchModelSelectionSource.MainWindow
            or D045WorkbenchModelSelectionSource.Workbench))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        D045AiFunctionRoutingResult result;
        lock (_gate)
        {
            var loaded = _providerStore.Load();
            if (!loaded.Accepted || loaded.Snapshot == null)
            {
                return new D045AiFunctionRoutingResult(
                    D045AiFunctionRoutingAdmission.InvalidState);
            }

            var instance = loaded.Snapshot.Instances.SingleOrDefault(value =>
                string.Equals(value.InstanceId, instanceId, StringComparison.Ordinal));
            if (instance == null)
            {
                return new D045AiFunctionRoutingResult(
                    D045AiFunctionRoutingAdmission.InstanceNotFound);
            }
            if (instance.ConfigurationGeneration != expectedConfigurationGeneration)
            {
                return new D045AiFunctionRoutingResult(
                    D045AiFunctionRoutingAdmission.StaleGeneration);
            }
            if (!D045WorkbenchModelCatalogRules.IsSelectableHigherReasoningModel(
                    instance,
                    modelId,
                    DateTimeOffset.UtcNow))
            {
                return new D045AiFunctionRoutingResult(
                    D045AiFunctionRoutingAdmission.ModelNotCatalogued);
            }

            var selections = new List<D045AiFunctionRouteSelection>
            {
                new(
                    D045AiFunctionKind.HigherReasoning,
                    instance.InstanceId,
                    modelId,
                    instance.ConfigurationGeneration),
                new(
                    D045AiFunctionKind.ComputerActionPlanning,
                    instance.InstanceId,
                    modelId,
                    instance.ConfigurationGeneration)
            };
            if (additionalSelection != null)
                selections.Add(additionalSelection);

            result = _routingStore.SelectMany(selections, loaded.Snapshot);
        }

        if (result.Accepted)
        {
            var snapshot = LoadSnapshot();
            Publish(snapshot, source, force: true);
        }
        return result;
    }

    private D045WorkbenchModelCatalogSnapshot LoadSnapshot()
    {
        var loaded = _providerStore.Load();
        if (!loaded.Accepted || loaded.Snapshot == null)
        {
            return new D045WorkbenchModelCatalogSnapshot(
                Array.Empty<D045WorkbenchModelCatalogEntry>(),
                null);
        }

        var now = DateTimeOffset.UtcNow;
        var models = loaded.Snapshot.Instances
            .SelectMany(instance => instance.CatalogModels
                .Where(model => D045WorkbenchModelCatalogRules
                    .IsSelectableHigherReasoningModel(instance, model, now))
                .Select(model => new D045WorkbenchModelCatalogEntry(
                    instance.InstanceId,
                    model,
                    instance.ConfigurationGeneration,
                    UnifiedProviderProfileRegistry.Get(instance.ProviderKind)
                        .DisplayName
                    + " · "
                    + model.Replace("models/", string.Empty, StringComparison.Ordinal))))
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ThenBy(value => value.ModelId, StringComparer.Ordinal)
            .ToArray();

        D045WorkbenchModelCatalogEntry? global = null;
        if (_routingStore.TryResolve(
                D045AiFunctionKind.HigherReasoning,
                loaded.Snapshot,
                out var route,
                out _)
            && route != null)
        {
            global = models.SingleOrDefault(value => value.HasIdentity(
                route.InstanceId,
                route.ModelId));
        }

        return new D045WorkbenchModelCatalogSnapshot(models, global);
    }

    private void PublishIfChanged(
        D045WorkbenchModelCatalogSnapshot snapshot,
        D045WorkbenchModelSelectionSource source)
        => Publish(snapshot, source, force: false);

    private void Publish(
        D045WorkbenchModelCatalogSnapshot snapshot,
        D045WorkbenchModelSelectionSource source,
        bool force)
    {
        bool catalogChanged;
        bool selectionChanged;
        lock (_gate)
        {
            catalogChanged = !SameCatalog(_lastSnapshot, snapshot);
            selectionChanged = !SameSelection(
                _lastSnapshot?.GlobalSelection,
                snapshot.GlobalSelection);
            if (!force && !catalogChanged && !selectionChanged)
                return;
            _lastSnapshot = snapshot;
        }

        SelectionChanged?.Invoke(
            this,
            new D045WorkbenchModelSelectionChangedEventArgs(
                snapshot,
                source,
                catalogChanged,
                selectionChanged));
    }

    private static bool SameCatalog(
        D045WorkbenchModelCatalogSnapshot? left,
        D045WorkbenchModelCatalogSnapshot right)
        => left != null
            && left.Models.Count == right.Models.Count
            && left.Models.Zip(right.Models).All(pair =>
                pair.First.HasIdentity(pair.Second.InstanceId, pair.Second.ModelId)
                && pair.First.ConfigurationGeneration
                    == pair.Second.ConfigurationGeneration);

    private static bool SameSelection(
        D045WorkbenchModelCatalogEntry? left,
        D045WorkbenchModelCatalogEntry? right)
        => left == null
            ? right == null
            : right != null
                && left.HasIdentity(right.InstanceId, right.ModelId)
                && left.ConfigurationGeneration == right.ConfigurationGeneration;
}
