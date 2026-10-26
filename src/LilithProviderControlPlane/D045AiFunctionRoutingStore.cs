using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LilithProviderControlPlane;

internal enum D045AiFunctionKind
{
    Invalid = 0,
    TextConversation = 1,
    SpeechRecognition = 2,
    ComputerActionPlanning = 3,
    HigherReasoning = 4,
    HigherReasoningImageGeneration = 5
}

internal sealed class D045AiFunctionRouteSelection
{
    internal D045AiFunctionKind FunctionKind { get; }
    internal string InstanceId { get; }
    internal string ModelId { get; }
    internal long ExpectedConfigurationGeneration { get; }

    internal D045AiFunctionRouteSelection(
        D045AiFunctionKind functionKind,
        string instanceId,
        string modelId,
        long expectedConfigurationGeneration)
    {
        if (!Enum.IsDefined(typeof(D045AiFunctionKind), functionKind)
            || functionKind == D045AiFunctionKind.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(functionKind));
        }
        FunctionKind = functionKind;
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
        ExpectedConfigurationGeneration = expectedConfigurationGeneration;
    }
}

internal sealed class D045AiFunctionRoute
{
    internal D045AiFunctionKind FunctionKind { get; }
    internal string InstanceId { get; }
    internal string ModelId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }

    internal D045AiFunctionRoute(
        D045AiFunctionKind functionKind,
        string instanceId,
        string modelId,
        long configurationGeneration,
        string configurationFingerprint)
    {
        if (!Enum.IsDefined(typeof(D045AiFunctionKind), functionKind)
            || functionKind == D045AiFunctionKind.Invalid)
            throw new ArgumentOutOfRangeException(nameof(functionKind));
        FunctionKind = functionKind;
        InstanceId = UnifiedProviderConfiguredInstance.Identifier(
            instanceId,
            96,
            nameof(instanceId));
        ModelId = UnifiedProviderCatalogCommit.ModelId(modelId, nameof(modelId));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        if (!UnifiedProviderCatalogCommit.IsSha256(configurationFingerprint))
            throw new ArgumentException(
                "A configuration SHA-256 is required.",
                nameof(configurationFingerprint));
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = configurationFingerprint;
    }

    internal bool Matches(UnifiedProviderConfiguredInstance instance)
        => instance != null
            && string.Equals(InstanceId, instance.InstanceId, StringComparison.Ordinal)
            && instance.ConfigurationGeneration == ConfigurationGeneration
            && string.Equals(
                ConfigurationFingerprint,
                instance.ConfigurationFingerprint,
                StringComparison.Ordinal)
            && instance.CatalogModels.Contains(ModelId, StringComparer.Ordinal)
            && !UnifiedProviderProfileRegistry.IsHardRejectedModel(
                instance.ProviderKind,
                ModelId);
}

internal sealed class D045AiFunctionRoutingSnapshot
{
    internal long Generation { get; }
    internal IReadOnlyList<D045AiFunctionRoute> Routes { get; }

    internal D045AiFunctionRoutingSnapshot(
        long generation,
        IEnumerable<D045AiFunctionRoute>? routes)
    {
        if (generation <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        var frozen = (routes ?? Array.Empty<D045AiFunctionRoute>())
            .OrderBy(value => value.FunctionKind)
            .ToArray();
        if (frozen.Length > 5
            || frozen.Select(value => value.FunctionKind).Distinct().Count()
                != frozen.Length)
        {
            throw new ArgumentException("Function routes are invalid.", nameof(routes));
        }
        Generation = generation;
        Routes = new ReadOnlyCollection<D045AiFunctionRoute>(frozen);
    }

    internal D045AiFunctionRoute? Find(D045AiFunctionKind functionKind)
        => Routes.SingleOrDefault(value => value.FunctionKind == functionKind);
}

internal enum D045AiFunctionRoutingAdmission
{
    Accepted = 1,
    InvalidState = 2,
    InstanceNotFound = 3,
    StaleGeneration = 4,
    ModelNotCatalogued = 5
}

internal sealed class D045AiFunctionRoutingResult
{
    internal D045AiFunctionRoutingAdmission Admission { get; }
    internal D045AiFunctionRoutingSnapshot? Snapshot { get; }
    internal bool Accepted => Admission == D045AiFunctionRoutingAdmission.Accepted;

    internal D045AiFunctionRoutingResult(
        D045AiFunctionRoutingAdmission admission,
        D045AiFunctionRoutingSnapshot? snapshot = null)
    {
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal sealed class D045AiFunctionRoutingStore
{
    private const int SchemaVersion = 1;
    private const int MaximumBytes = 64 * 1024;
    private static readonly object MutationGate = new();
    private readonly string _current;
    private readonly string _previous;

    internal D045AiFunctionRoutingStore(string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
            throw new ArgumentException(
                "Local application data is required.",
                nameof(localApplicationDataDirectory));
        var root = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            "LilithAiMod",
            "Config",
            "ai-function-routing-v1");
        _current = Path.Combine(root, "current-v1.json");
        _previous = _current + ".previous";
    }

    internal static D045AiFunctionRoutingStore ForCurrentUser()
        => new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal D045AiFunctionRoutingResult Load()
    {
        if (!File.Exists(_current))
        {
            return new D045AiFunctionRoutingResult(
                D045AiFunctionRoutingAdmission.Accepted,
                new D045AiFunctionRoutingSnapshot(1, null));
        }
        var current = Read(_current);
        if (current.Accepted)
            return current;
        var previous = Read(_previous);
        return previous.Accepted ? previous : current;
    }

    internal D045AiFunctionRoutingResult Select(
        D045AiFunctionKind functionKind,
        string instanceId,
        string modelId,
        long expectedConfigurationGeneration,
        UnifiedProviderControlPlaneSnapshot providers)
    {
        if (providers == null)
            throw new ArgumentNullException(nameof(providers));
        return SelectMany(
            new[]
            {
                new D045AiFunctionRouteSelection(
                    functionKind,
                    instanceId,
                    modelId,
                    expectedConfigurationGeneration)
            },
            providers);
    }

    internal D045AiFunctionRoutingResult SelectMany(
        IEnumerable<D045AiFunctionRouteSelection> selections,
        UnifiedProviderControlPlaneSnapshot providers)
    {
        if (selections == null)
            throw new ArgumentNullException(nameof(selections));
        if (providers == null)
            throw new ArgumentNullException(nameof(providers));
        lock (MutationGate)
        {
            var loaded = Load();
            if (!loaded.Accepted || loaded.Snapshot == null)
                return loaded;
            var frozenSelections = selections.ToArray();
            if (frozenSelections.Length is < 1 or > 5
                || frozenSelections.Select(value => value.FunctionKind)
                    .Distinct().Count() != frozenSelections.Length)
            {
                return Reject(D045AiFunctionRoutingAdmission.InvalidState);
            }
            var replacements = new List<D045AiFunctionRoute>(frozenSelections.Length);
            foreach (var selection in frozenSelections)
            {
                var instance = providers.Instances.SingleOrDefault(value =>
                    string.Equals(
                        value.InstanceId,
                        selection.InstanceId,
                        StringComparison.Ordinal));
                if (instance == null)
                    return Reject(D045AiFunctionRoutingAdmission.InstanceNotFound);
                if (instance.ConfigurationGeneration
                    != selection.ExpectedConfigurationGeneration)
                {
                    return Reject(D045AiFunctionRoutingAdmission.StaleGeneration);
                }
                if (!instance.CatalogModels.Contains(
                        selection.ModelId,
                        StringComparer.Ordinal)
                    || UnifiedProviderProfileRegistry.IsHardRejectedModel(
                        instance.ProviderKind,
                        selection.ModelId))
                {
                    return Reject(D045AiFunctionRoutingAdmission.ModelNotCatalogued);
                }
                replacements.Add(new D045AiFunctionRoute(
                    selection.FunctionKind,
                    instance.InstanceId,
                    selection.ModelId,
                    instance.ConfigurationGeneration,
                    instance.ConfigurationFingerprint));
            }
            var replacedKinds = replacements
                .Select(value => value.FunctionKind)
                .ToHashSet();
            var routes = loaded.Snapshot.Routes
                .Where(value => !replacedKinds.Contains(value.FunctionKind))
                .Concat(replacements);
            return Persist(new D045AiFunctionRoutingSnapshot(
                loaded.Snapshot.Generation + 1,
                routes));
        }
    }

    internal bool TryResolve(
        D045AiFunctionKind functionKind,
        UnifiedProviderControlPlaneSnapshot providers,
        out D045AiFunctionRoute? route,
        out UnifiedProviderConfiguredInstance? instance)
    {
        route = null;
        instance = null;
        var loaded = Load();
        if (!loaded.Accepted || loaded.Snapshot == null)
            return false;
        var foundRoute = loaded.Snapshot.Find(functionKind);
        if (foundRoute == null)
            return false;
        route = foundRoute;
        instance = providers.Instances.SingleOrDefault(value =>
            string.Equals(
                value.InstanceId,
                foundRoute.InstanceId,
                StringComparison.Ordinal));
        return instance != null && foundRoute.Matches(instance);
    }

    internal D045AiFunctionRoutingResult EnsureLegacyTextAssignment(
        UnifiedProviderControlPlaneSnapshot providers)
    {
        var loaded = Load();
        if (!loaded.Accepted || loaded.Snapshot == null
            || loaded.Snapshot.Find(D045AiFunctionKind.TextConversation) != null
            || providers.TextAssignment == null)
        {
            return loaded;
        }
        return Select(
            D045AiFunctionKind.TextConversation,
            providers.TextAssignment.InstanceId,
            providers.TextAssignment.ModelId,
            providers.TextAssignment.ConfigurationGeneration,
            providers);
    }

    private D045AiFunctionRoutingResult Persist(
        D045AiFunctionRoutingSnapshot snapshot)
    {
        var payload = new Payload
        {
            SchemaVersion = SchemaVersion,
            Generation = snapshot.Generation,
            Routes = snapshot.Routes.Select(value => new RoutePayload
            {
                FunctionKind = (int)value.FunctionKind,
                InstanceId = value.InstanceId,
                ModelId = value.ModelId,
                ConfigurationGeneration = value.ConfigurationGeneration,
                ConfigurationFingerprint = value.ConfigurationFingerprint
            }).ToArray()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length > MaximumBytes)
            return Reject(D045AiFunctionRoutingAdmission.InvalidState);
        var directory = Path.GetDirectoryName(_current)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            ".routing-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(_current))
                File.Replace(temporary, _current, _previous, true);
            else
                File.Move(temporary, _current);
            return new D045AiFunctionRoutingResult(
                D045AiFunctionRoutingAdmission.Accepted,
                snapshot);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private D045AiFunctionRoutingResult Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumBytes)
                return Reject(D045AiFunctionRoutingAdmission.InvalidState);
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllBytes(path));
            if (payload == null
                || payload.SchemaVersion != SchemaVersion
                || payload.Routes == null)
            {
                return Reject(D045AiFunctionRoutingAdmission.InvalidState);
            }
            var routes = payload.Routes.Select(value => new D045AiFunctionRoute(
                (D045AiFunctionKind)value.FunctionKind,
                value.InstanceId,
                value.ModelId,
                value.ConfigurationGeneration,
                value.ConfigurationFingerprint));
            return new D045AiFunctionRoutingResult(
                D045AiFunctionRoutingAdmission.Accepted,
                new D045AiFunctionRoutingSnapshot(payload.Generation, routes));
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or InvalidOperationException)
        {
            return Reject(D045AiFunctionRoutingAdmission.InvalidState);
        }
    }

    private static D045AiFunctionRoutingResult Reject(
        D045AiFunctionRoutingAdmission admission)
        => new(admission);

    private sealed class Payload
    {
        public int SchemaVersion { get; set; }
        public long Generation { get; set; }
        public RoutePayload[]? Routes { get; set; }
    }

    private sealed class RoutePayload
    {
        public int FunctionKind { get; set; }
        public string InstanceId { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public long ConfigurationGeneration { get; set; }
        public string ConfigurationFingerprint { get; set; } = string.Empty;
    }
}
