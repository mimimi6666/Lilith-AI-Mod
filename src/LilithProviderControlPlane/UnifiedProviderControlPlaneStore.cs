using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace LilithProviderControlPlane;

internal enum UnifiedProviderStoreAdmission
{
    Invalid = 0,
    Accepted = 1,
    Missing = 2,
    InvalidInstance = 3,
    InvalidEndpoint = 4,
    InvalidCredential = 5,
    InvalidCatalog = 6,
    InvalidModel = 7,
    ModelNotCatalogued = 8,
    InstanceNotFound = 9,
    StaleGeneration = 10,
    UnsupportedPlatform = 11,
    ProtectionFailed = 12,
    StorageFailure = 13,
    Corrupt = 14
}

internal sealed class UnifiedProviderCatalogCapabilityEvidence
{
    internal string ModelId { get; }
    internal ProviderModelCapabilityKind Kind { get; }
    internal ProviderCapabilityState State { get; }
    internal ProviderCapabilityProvenance Provenance { get; }

    internal UnifiedProviderCatalogCapabilityEvidence(
        string modelId,
        ProviderModelCapabilityKind kind,
        ProviderCapabilityState state,
        ProviderCapabilityProvenance provenance)
    {
        ModelId = UnifiedProviderCatalogCommit.ModelId(
            modelId,
            nameof(modelId));
        Kind = UnifiedProviderConfiguredInstance.Defined(kind, nameof(kind));
        State = UnifiedProviderConfiguredInstance.Defined(state, nameof(state));
        Provenance = UnifiedProviderConfiguredInstance.Defined(
            provenance,
            nameof(provenance));
        if (State != ProviderCapabilityState.Advertised)
        {
            throw new ArgumentException(
                "Catalog evidence may only retain advertised capability state.",
                nameof(state));
        }
        if (Provenance is not (
            ProviderCapabilityProvenance.OfficialModelCatalog
            or ProviderCapabilityProvenance.OfficialModelMetadataEndpoint))
        {
            throw new ArgumentException(
                "Catalog evidence requires official catalog provenance.",
                nameof(provenance));
        }
    }
}

internal static class UnifiedProviderCatalogCapabilityRules
{
    internal static bool AdvertisesTextConversation(
        IEnumerable<UnifiedProviderCatalogCapabilityEvidence> capabilityEvidence,
        string modelId)
    {
        if (capabilityEvidence == null)
            throw new ArgumentNullException(nameof(capabilityEvidence));
        var exactModel = UnifiedProviderCatalogCommit.ModelId(
            modelId,
            nameof(modelId));
        var textInput = false;
        var textOutput = false;
        var textGeneration = false;
        foreach (var evidence in capabilityEvidence)
        {
            if (!string.Equals(
                    evidence.ModelId,
                    exactModel,
                    StringComparison.Ordinal)
                || evidence.State != ProviderCapabilityState.Advertised)
            {
                continue;
            }
            switch (evidence.Kind)
            {
                case ProviderModelCapabilityKind.TextInput:
                    textInput = true;
                    break;
                case ProviderModelCapabilityKind.TextOutput:
                    textOutput = true;
                    break;
                case ProviderModelCapabilityKind.TextGeneration:
                    textGeneration = true;
                    break;
            }
        }
        return textGeneration || (textInput && textOutput);
    }
}

internal sealed class UnifiedProviderCatalogCommit
{
    internal IReadOnlyList<string> Models { get; }
    internal IReadOnlyList<UnifiedProviderCatalogCapabilityEvidence>
        CapabilityEvidence { get; }
    internal string CapabilityEvidenceFingerprint { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string CatalogFingerprint { get; }

    internal bool AdvertisesTextCapability(string modelId)
        => UnifiedProviderCatalogCapabilityRules.AdvertisesTextConversation(
            CapabilityEvidence,
            modelId);

    internal UnifiedProviderCatalogCommit(
        IEnumerable<string> models,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        string catalogFingerprint,
        IEnumerable<UnifiedProviderCatalogCapabilityEvidence>?
            capabilityEvidence = null)
    {
        var frozen = (models ?? throw new ArgumentNullException(nameof(models)))
            .Select(value => ModelId(value, nameof(models)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .Take(UnifiedProviderControlPlaneStore.MaximumModels + 1)
            .ToArray();
        if (frozen.Length == 0
            || frozen.Length > UnifiedProviderControlPlaneStore.MaximumModels)
        {
            throw new ArgumentOutOfRangeException(nameof(models));
        }
        if (observedAt == default || expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        if (!IsSha256(catalogFingerprint))
            throw new ArgumentException("A catalog SHA-256 is required.", nameof(catalogFingerprint));
        var evidence = (capabilityEvidence
                ?? Array.Empty<UnifiedProviderCatalogCapabilityEvidence>())
            .OrderBy(value => value?.ModelId, StringComparer.Ordinal)
            .ThenBy(value => value?.Kind)
            .ToArray();
        if (evidence.Any(value => value == null)
            || evidence.Any(value => !frozen.Contains(
                value.ModelId,
                StringComparer.Ordinal))
            || evidence.GroupBy(value => (value.ModelId, value.Kind)).Any(
                group => group.Count() != 1)
            || evidence.GroupBy(value => value.ModelId).Any(group =>
                group.Count()
                    > UnifiedProviderCatalogAdapters.MaximumCapabilitiesPerModel))
        {
            throw new ArgumentException(
                "Catalog capability evidence is invalid.",
                nameof(capabilityEvidence));
        }
        Models = new ReadOnlyCollection<string>(frozen);
        CapabilityEvidence = new ReadOnlyCollection<
            UnifiedProviderCatalogCapabilityEvidence>(evidence);
        CapabilityEvidenceFingerprint =
            UnifiedProviderConfiguredInstance.Hash(
                "D033/CATALOG-CAPABILITY-EVIDENCE/1",
                string.Join(
                    "\u001e",
                    evidence.Select(value => string.Join(
                        "\u001d",
                        value.ModelId,
                        ((int)value.Kind).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ((int)value.State).ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ((int)value.Provenance).ToString(
                            System.Globalization.CultureInfo.InvariantCulture)))));
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        CatalogFingerprint = catalogFingerprint;
    }

    internal static string ModelId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > UnifiedProviderControlPlaneStore.MaximumModelIdCharacters
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded model identifier is required.", parameterName);
        }
        return value.Trim();
    }

    internal static bool IsSha256(string? value)
        => value?.Length == 64
           && value.All(character => character is >= '0' and <= '9'
               or >= 'A' and <= 'F');
}

internal sealed class UnifiedProviderConfiguredInstance
{
    internal string InstanceId { get; }
    internal ProviderControlKind ProviderKind { get; }
    internal string ProfileId { get; }
    internal Uri BaseUri { get; }
    internal string EndpointProfileId { get; }
    internal string WorkspaceId { get; }
    internal ConfiguredEndpointRouteIntent RouteIntent { get; }
    internal long ConfigurationGeneration { get; }
    internal string SelectedModel { get; }
    internal IReadOnlyList<string> CatalogModels { get; }
    internal IReadOnlyList<UnifiedProviderCatalogCapabilityEvidence>
        CatalogCapabilityEvidence { get; }
    internal DateTimeOffset CatalogObservedAt { get; }
    internal DateTimeOffset CatalogExpiresAt { get; }
    internal string CatalogFingerprint { get; }
    internal bool CredentialConfigured { get; }
    internal string CredentialFile { get; }
    internal string CredentialSha256 { get; }
    internal string ConfigurationFingerprint { get; }

    internal UnifiedProviderConfiguredInstance(
        string instanceId,
        ProviderControlKind providerKind,
        string profileId,
        Uri baseUri,
        string endpointProfileId,
        string workspaceId,
        long configurationGeneration,
        string selectedModel,
        IEnumerable<string> catalogModels,
        DateTimeOffset catalogObservedAt,
        DateTimeOffset catalogExpiresAt,
        string catalogFingerprint,
        bool credentialConfigured,
        string credentialFile,
        string credentialSha256)
        : this(
            instanceId,
            providerKind,
            profileId,
            baseUri,
            endpointProfileId,
            workspaceId,
            configurationGeneration,
            selectedModel,
            catalogModels,
            catalogObservedAt,
            catalogExpiresAt,
            catalogFingerprint,
            credentialConfigured,
            credentialFile,
            credentialSha256,
            ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                providerKind,
                baseUri))
    {
    }

    internal UnifiedProviderConfiguredInstance(
        string instanceId,
        ProviderControlKind providerKind,
        string profileId,
        Uri baseUri,
        string endpointProfileId,
        string workspaceId,
        long configurationGeneration,
        string selectedModel,
        IEnumerable<string> catalogModels,
        DateTimeOffset catalogObservedAt,
        DateTimeOffset catalogExpiresAt,
        string catalogFingerprint,
        bool credentialConfigured,
        string credentialFile,
        string credentialSha256,
        ConfiguredEndpointRouteIntent routeIntent,
        IEnumerable<UnifiedProviderCatalogCapabilityEvidence>?
            catalogCapabilityEvidence = null)
    {
        InstanceId = Identifier(instanceId, 96, nameof(instanceId));
        ProviderKind = Defined(providerKind, nameof(providerKind));
        ProfileId = Identifier(profileId, 64, nameof(profileId));
        BaseUri = NormalizeBaseUri(baseUri, nameof(baseUri));
        EndpointProfileId = OptionalIdentifier(
            endpointProfileId,
            96,
            nameof(endpointProfileId));
        WorkspaceId = OptionalIdentifier(workspaceId, 128, nameof(workspaceId));
        RouteIntent = routeIntent
            ?? throw new ArgumentNullException(nameof(routeIntent));
        var routeAdmission = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            ProviderKind,
            BaseUri,
            RouteIntent,
            EndpointProfileId,
            WorkspaceId);
        if (routeAdmission.Admission
            == UnifiedProviderEndpointAdmission.Rejected)
        {
            throw new ArgumentException(
                "The configured endpoint does not match its route intent.",
                nameof(routeIntent));
        }
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        ConfigurationGeneration = configurationGeneration;
        var catalog = new UnifiedProviderCatalogCommit(
            catalogModels,
            catalogObservedAt,
            catalogExpiresAt,
            catalogFingerprint,
            catalogCapabilityEvidence);
        SelectedModel = UnifiedProviderCatalogCommit.ModelId(
            selectedModel,
            nameof(selectedModel));
        if (!catalog.Models.Contains(SelectedModel, StringComparer.Ordinal))
            throw new ArgumentException("The selected model must be catalogued.", nameof(selectedModel));
        CatalogModels = catalog.Models;
        CatalogCapabilityEvidence = catalog.CapabilityEvidence;
        CatalogObservedAt = catalog.ObservedAt;
        CatalogExpiresAt = catalog.ExpiresAt;
        CatalogFingerprint = catalog.CatalogFingerprint;
        CredentialConfigured = credentialConfigured;
        CredentialFile = credentialConfigured
            ? SafeFileName(credentialFile, ".bin", nameof(credentialFile))
            : string.Empty;
        CredentialSha256 = credentialConfigured
            && UnifiedProviderCatalogCommit.IsSha256(credentialSha256)
                ? credentialSha256
                : credentialConfigured
                    ? throw new ArgumentException(
                        "A credential SHA-256 is required.",
                        nameof(credentialSha256))
                    : string.Empty;
        var fingerprintValues = new List<string>
        {
            CatalogCapabilityEvidence.Count == 0
                ? "D033/CONFIGURED-INSTANCE/2"
                : "D033/CONFIGURED-INSTANCE/3",
            InstanceId,
            ((int)ProviderKind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ProfileId,
            BaseUri.AbsoluteUri,
            EndpointProfileId,
            WorkspaceId,
            RouteIntent.Fingerprint,
            ConfigurationGeneration.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            SelectedModel,
            CatalogFingerprint,
            CredentialConfigured ? "1" : "0",
            CredentialSha256
        };
        if (CatalogCapabilityEvidence.Count != 0)
            fingerprintValues.Add(catalog.CapabilityEvidenceFingerprint);
        ConfigurationFingerprint = Hash(fingerprintValues.ToArray());
    }

    internal bool CatalogIsCurrent(DateTimeOffset now)
        => now >= CatalogObservedAt && now < CatalogExpiresAt;

    internal bool MatchesGeneration(long generation, string fingerprint)
        => generation == ConfigurationGeneration
           && string.Equals(
               fingerprint,
               ConfigurationFingerprint,
               StringComparison.Ordinal);

    internal static Uri NormalizeBaseUri(Uri? value, string parameterName)
    {
        if (value == null
            || !value.IsAbsoluteUri
            || value.AbsoluteUri.Length > 1024
            || value.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(value.UserInfo)
            || !string.IsNullOrEmpty(value.Fragment))
        {
            throw new ArgumentException("An absolute HTTP base URI is required.", parameterName);
        }
        var normalized = value.AbsoluteUri.TrimEnd('/') + "/";
        return new Uri(normalized, UriKind.Absolute);
    }

    internal static string Identifier(string? value, int maximum, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximum
            || value.Any(character => !(char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.')))
        {
            throw new ArgumentException("A bounded identifier is required.", parameterName);
        }
        return value.Trim();
    }

    internal static string OptionalIdentifier(string? value, int maximum, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Identifier(value, maximum, parameterName);

    internal static string SafeFileName(string? value, string suffix, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 180
            || Path.GetFileName(value) != value
            || !value.EndsWith(suffix, StringComparison.Ordinal))
        {
            throw new ArgumentException("A safe file name is required.", parameterName);
        }
        return value;
    }

    internal static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value) == 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static string Hash(params string[] values)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", values));
        try
        {
            return Convert.ToHexString(sha.ComputeHash(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

internal sealed class UnifiedProviderTextAssignment
{
    internal string InstanceId { get; }
    internal string ModelId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }

    internal UnifiedProviderTextAssignment(
        string instanceId,
        string modelId,
        long configurationGeneration,
        string configurationFingerprint)
    {
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
           && string.Equals(ModelId, instance.SelectedModel, StringComparison.Ordinal)
           && instance.MatchesGeneration(
               ConfigurationGeneration,
               ConfigurationFingerprint);
}

internal sealed class UnifiedProviderControlPlaneSnapshot
{
    internal long StoreGeneration { get; }
    internal IReadOnlyList<UnifiedProviderConfiguredInstance> Instances { get; }
    internal UnifiedProviderTextAssignment? TextAssignment { get; }
    internal string SnapshotFingerprint { get; }

    internal UnifiedProviderControlPlaneSnapshot(
        long storeGeneration,
        IEnumerable<UnifiedProviderConfiguredInstance> instances,
        UnifiedProviderTextAssignment? textAssignment)
    {
        if (storeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeGeneration));
        var frozen = (instances ?? throw new ArgumentNullException(nameof(instances)))
            .OrderBy(value => value.ProviderKind)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (frozen.Length > UnifiedProviderControlPlaneStore.MaximumInstances
            || frozen.Any(value => value == null)
            || frozen.Select(value => value.InstanceId)
                .Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        {
            throw new ArgumentException("Configured instances are invalid.", nameof(instances));
        }
        if (textAssignment != null
            && !frozen.Any(textAssignment.Matches))
        {
            throw new ArgumentException(
                "The text assignment must match an exact configured generation.",
                nameof(textAssignment));
        }
        StoreGeneration = storeGeneration;
        Instances = new ReadOnlyCollection<UnifiedProviderConfiguredInstance>(frozen);
        TextAssignment = textAssignment;
        SnapshotFingerprint = UnifiedProviderConfiguredInstance.Hash(
            "D033/CONTROL-PLANE-SNAPSHOT/1",
            StoreGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Join(",", frozen.Select(value => value.ConfigurationFingerprint)),
            textAssignment == null
                ? string.Empty
                : string.Join(
                    ":",
                    textAssignment.InstanceId,
                    textAssignment.ModelId,
                    textAssignment.ConfigurationFingerprint));
    }
}

internal sealed class UnifiedProviderStoreResult
{
    internal UnifiedProviderStoreAdmission Admission { get; }
    internal UnifiedProviderControlPlaneSnapshot? Snapshot { get; }
    internal bool Accepted => Admission == UnifiedProviderStoreAdmission.Accepted;

    internal UnifiedProviderStoreResult(
        UnifiedProviderStoreAdmission admission,
        UnifiedProviderControlPlaneSnapshot? snapshot = null)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderStoreAdmission), admission)
            || admission == UnifiedProviderStoreAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == UnifiedProviderStoreAdmission.Accepted) != (snapshot != null))
            throw new ArgumentException("Only accepted results may carry a snapshot.");
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal sealed class UnifiedProviderCredentialLease : IDisposable
{
    private char[]? _secret;

    internal UnifiedProviderCredentialLease(char[] secret)
        => _secret = secret ?? throw new ArgumentNullException(nameof(secret));

    internal char[] Take()
        => Interlocked.Exchange(ref _secret, null)
           ?? throw new InvalidOperationException("Credential lease was already consumed.");

    public void Dispose()
    {
        var secret = Interlocked.Exchange(ref _secret, null);
        if (secret != null)
            Array.Clear(secret, 0, secret.Length);
    }
}

internal sealed class UnifiedProviderControlPlaneStore
{
    internal const int MaximumInstances =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.MaximumInstances;
    internal const int MaximumModels =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.MaximumModels;
    internal const int MaximumModelIdCharacters =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec
            .MaximumModelIdCharacters;
    internal const int MaximumCredentialCharacters = 16 * 1024;
    private const int MaximumManifestBytes =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.MaximumManifestBytes;
    private const int MaximumPayloadBytes =
        UnifiedProviderControlPlaneCanonicalSnapshotCodec.MaximumPayloadBytes;
    private const int MaximumCredentialCipherBytes = 64 * 1024;
    private static readonly object MutationGate = new();
    private readonly string _root;
    private readonly string _generations;
    private readonly string _secrets;
    private readonly string _current;
    private readonly string _previous;
    private readonly string _credentialAuthorityRetirementMarker;
    private readonly IDeepSeekCurrentUserProtection _protection;

    internal UnifiedProviderControlPlaneStore(
        string localApplicationDataDirectory,
        IDeepSeekCurrentUserProtection? protection = null)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
            throw new ArgumentException("Local application data is required.", nameof(localApplicationDataDirectory));
        var playerRoot = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            "LilithAiMod");
        _root = Below(playerRoot, "Config", "provider-control-v2");
        _generations = Below(_root, "generations");
        _secrets = Below(playerRoot, "Secrets", "provider-control-v2");
        _current = Below(_root, "current-v1.json");
        _previous = _current + ".previous";
        _credentialAuthorityRetirementMarker =
            LegacyProviderCredentialAuthorityRetirement.MarkerPath(
                localApplicationDataDirectory);
        _protection = protection ?? new WindowsCurrentUserProtection();
    }

    internal static UnifiedProviderControlPlaneStore ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        LegacyProviderCredentialAuthorityRetirement
            .EnforceForCurrentUserWithoutBlockingStartup();
        return new UnifiedProviderControlPlaneStore(localApplicationData);
    }

    internal UnifiedProviderStoreResult Load()
    {
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return EmptyRetiredSnapshot();
        }
        return LoadBeforeCredentialAuthorityRetirement();
    }

    internal UnifiedProviderStoreResult LoadBeforeCredentialAuthorityRetirement()
    {
        if (!File.Exists(_current))
        {
            return new UnifiedProviderStoreResult(
                UnifiedProviderStoreAdmission.Accepted,
                new UnifiedProviderControlPlaneSnapshot(
                    1,
                    Array.Empty<UnifiedProviderConfiguredInstance>(),
                    null));
        }
        var current = Read(_current);
        if (current.Accepted)
            return current;
        var previous = Read(_previous);
        return previous.Accepted ? previous : current;
    }

    internal UnifiedProviderStoreResult ApplyValidated(
        ProviderControlProfile profile,
        string instanceId,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        ProviderCustomAuthenticationKind customAuthentication,
        UnifiedProviderCredentialLease credentialLease,
        UnifiedProviderCatalogCommit catalog,
        string selectedModel,
        long expectedStoreGeneration,
        DateTimeOffset now,
        bool assignTextRoute = true)
        => ApplyValidated(
            profile,
            instanceId,
            baseUri,
            endpointProfileId,
            workspaceId,
            ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                profile?.Kind ?? ProviderControlKind.Invalid,
                baseUri),
            customAuthentication,
            credentialLease,
            catalog,
            selectedModel,
            expectedStoreGeneration,
            now,
            assignTextRoute);

    internal UnifiedProviderStoreResult ApplyValidated(
        ProviderControlProfile profile,
        string instanceId,
        Uri baseUri,
        string? endpointProfileId,
        string? workspaceId,
        ConfiguredEndpointRouteIntent routeIntent,
        ProviderCustomAuthenticationKind customAuthentication,
        UnifiedProviderCredentialLease credentialLease,
        UnifiedProviderCatalogCommit catalog,
        string selectedModel,
        long expectedStoreGeneration,
        DateTimeOffset now,
        bool assignTextRoute = true)
    {
        using var ownedCredentialLease = credentialLease
            ?? throw new ArgumentNullException(nameof(credentialLease));
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return Reject(UnifiedProviderStoreAdmission.InvalidCredential);
        }
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
        if (expectedStoreGeneration <= 0)
            return Reject(UnifiedProviderStoreAdmission.StaleGeneration);
        if (!AuthenticationIsAdmitted(profile, customAuthentication))
            return Reject(UnifiedProviderStoreAdmission.InvalidCredential);
        if (profile.IsHardRejectedModel(selectedModel)
            || !catalog.Models.Contains(selectedModel, StringComparer.Ordinal))
        {
            return Reject(UnifiedProviderStoreAdmission.ModelNotCatalogued);
        }
        string id;
        Uri endpoint;
        try
        {
            id = UnifiedProviderConfiguredInstance.Identifier(
                instanceId,
                96,
                nameof(instanceId));
            endpoint = UnifiedProviderConfiguredInstance.NormalizeBaseUri(
                baseUri,
                nameof(baseUri));
            ValidateEndpoint(
                profile,
                endpoint,
                endpointProfileId,
                workspaceId,
                routeIntent);
        }
        catch (ArgumentException)
        {
            return Reject(UnifiedProviderStoreAdmission.InvalidEndpoint);
        }

        lock (MutationGate)
        {
        var loaded = Load();
        if (!loaded.Accepted || loaded.Snapshot == null)
            return loaded;
        if (loaded.Snapshot.StoreGeneration != expectedStoreGeneration)
            return Reject(UnifiedProviderStoreAdmission.StaleGeneration);
        var existing = loaded.Snapshot.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, id, StringComparison.Ordinal));
        if (existing != null && existing.ProviderKind != profile.Kind)
            return Reject(UnifiedProviderStoreAdmission.InvalidInstance);

        char[]? credentialCharacters = null;
        byte[]? plain = null;
        byte[]? entropy = null;
        byte[]? cipher = null;
        try
        {
            credentialCharacters = ownedCredentialLease.Take();
            var credentialStart = 0;
            var credentialLength = credentialCharacters.Length;
            while (credentialLength > 0
                   && char.IsWhiteSpace(credentialCharacters[credentialStart]))
            {
                credentialStart++;
                credentialLength--;
            }
            while (credentialLength > 0
                   && char.IsWhiteSpace(
                       credentialCharacters[credentialStart + credentialLength - 1]))
            {
                credentialLength--;
            }
            if (credentialLength > MaximumCredentialCharacters
                || (profile.CredentialRequirement == ProviderCredentialRequirement.Required
                    && credentialLength == 0))
            {
                return Reject(UnifiedProviderStoreAdmission.InvalidCredential);
            }
            if (profile.Kind == ProviderControlKind.CustomOpenAICompatible
                && ((customAuthentication == ProviderCustomAuthenticationKind.None
                        && credentialLength != 0)
                    || (customAuthentication == ProviderCustomAuthenticationKind.Bearer
                        && credentialLength == 0)))
            {
                return Reject(UnifiedProviderStoreAdmission.InvalidCredential);
            }
            if (credentialLength > 0 && !_protection.Supported)
                return Reject(UnifiedProviderStoreAdmission.UnsupportedPlatform);

            var credentialConfigured = credentialLength > 0;
            var credentialFile = string.Empty;
            var credentialHash = string.Empty;
            if (credentialLength > 0)
            {
                var credentialSpan = credentialCharacters.AsSpan(
                    credentialStart,
                    credentialLength);
                plain = new byte[Encoding.UTF8.GetByteCount(credentialSpan)];
                Encoding.UTF8.GetBytes(credentialSpan, plain);
                entropy = Entropy(profile.Kind, id);
                cipher = _protection.Protect(plain, entropy);
                credentialHash = Convert.ToHexString(SHA256.HashData(cipher));
                credentialFile = profile.ProviderId
                    + "-"
                    + id
                    + "-"
                    + credentialHash[..16].ToLowerInvariant()
                    + ".bin";
                WriteImmutable(Path.Combine(_secrets, credentialFile), cipher);
            }
            if (profile.CredentialRequirement == ProviderCredentialRequirement.Required
                && !credentialConfigured)
            {
                return Reject(UnifiedProviderStoreAdmission.InvalidCredential);
            }

            var instanceGeneration = (existing?.ConfigurationGeneration ?? 0) + 1;
            var updated = new UnifiedProviderConfiguredInstance(
                id,
                profile.Kind,
                profile.ProviderId,
                endpoint,
                endpointProfileId ?? string.Empty,
                workspaceId ?? string.Empty,
                instanceGeneration,
                selectedModel,
                catalog.Models,
                catalog.ObservedAt,
                catalog.ExpiresAt,
                catalog.CatalogFingerprint,
                credentialConfigured,
                credentialFile,
                credentialHash,
                routeIntent,
                catalog.CapabilityEvidence);
            var instances = loaded.Snapshot.Instances
                .Where(value => !string.Equals(value.InstanceId, id, StringComparison.Ordinal))
                .Append(updated)
                .ToArray();
            var assignment = loaded.Snapshot.TextAssignment;
            var updatesCurrentAssignment = existing != null
                && assignment != null
                && string.Equals(assignment.InstanceId, id, StringComparison.Ordinal);
            if (updatesCurrentAssignment && !assignTextRoute)
            {
                assignment = null;
            }
            else if (assignTextRoute
                     && (assignment == null || updatesCurrentAssignment))
            {
                assignment = new UnifiedProviderTextAssignment(
                    updated.InstanceId,
                    updated.SelectedModel,
                    updated.ConfigurationGeneration,
                    updated.ConfigurationFingerprint);
            }
            return Persist(
                new UnifiedProviderControlPlaneSnapshot(
                    loaded.Snapshot.StoreGeneration + 1,
                    instances,
                    assignment),
                now);
        }
        catch (CryptographicException)
        {
            return Reject(UnifiedProviderStoreAdmission.ProtectionFailed);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Reject(UnifiedProviderStoreAdmission.StorageFailure);
        }
        finally
        {
            if (credentialCharacters != null)
                Array.Clear(credentialCharacters, 0, credentialCharacters.Length);
            if (plain != null)
                CryptographicOperations.ZeroMemory(plain);
            if (entropy != null)
                CryptographicOperations.ZeroMemory(entropy);
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
        }
        }
    }

    internal UnifiedProviderStoreResult SelectTextModel(
        string instanceId,
        string modelId,
        long expectedConfigurationGeneration,
        DateTimeOffset now)
    {
        lock (MutationGate)
        {
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return Reject(UnifiedProviderStoreAdmission.StaleGeneration);
        }
        var loaded = Load();
        if (!loaded.Accepted || loaded.Snapshot == null)
            return loaded;
        var instance = loaded.Snapshot.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, instanceId, StringComparison.Ordinal));
        if (instance == null)
            return Reject(UnifiedProviderStoreAdmission.InstanceNotFound);
        if (instance.ConfigurationGeneration != expectedConfigurationGeneration)
            return Reject(UnifiedProviderStoreAdmission.StaleGeneration);
        if (!instance.CatalogModels.Contains(modelId, StringComparer.Ordinal)
            || UnifiedProviderProfileRegistry.IsHardRejectedModel(
                instance.ProviderKind,
                modelId))
        {
            return Reject(UnifiedProviderStoreAdmission.ModelNotCatalogued);
        }
        var selectedInstance = string.Equals(
                instance.SelectedModel,
                modelId,
                StringComparison.Ordinal)
            ? instance
            : new UnifiedProviderConfiguredInstance(
                instance.InstanceId,
                instance.ProviderKind,
                instance.ProfileId,
                instance.BaseUri,
                instance.EndpointProfileId,
                instance.WorkspaceId,
                instance.ConfigurationGeneration + 1,
                modelId,
                instance.CatalogModels,
                instance.CatalogObservedAt,
                instance.CatalogExpiresAt,
                instance.CatalogFingerprint,
                instance.CredentialConfigured,
                instance.CredentialFile,
                instance.CredentialSha256,
                instance.RouteIntent,
                instance.CatalogCapabilityEvidence);
        var instances = loaded.Snapshot.Instances
            .Where(value => !string.Equals(value.InstanceId, instanceId, StringComparison.Ordinal))
            .Append(selectedInstance)
            .ToArray();
        return Persist(
            new UnifiedProviderControlPlaneSnapshot(
                loaded.Snapshot.StoreGeneration + 1,
                instances,
                new UnifiedProviderTextAssignment(
                    selectedInstance.InstanceId,
                    selectedInstance.SelectedModel,
                    selectedInstance.ConfigurationGeneration,
                    selectedInstance.ConfigurationFingerprint)),
            now);
        }
    }

    internal (UnifiedProviderStoreAdmission Admission, UnifiedProviderCredentialLease? Lease)
        OpenCredential(UnifiedProviderConfiguredInstance expected)
    {
        if (expected == null)
            throw new ArgumentNullException(nameof(expected));
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return (UnifiedProviderStoreAdmission.StaleGeneration, null);
        }
        if (!expected.CredentialConfigured)
        {
            return (
                UnifiedProviderStoreAdmission.Accepted,
                new UnifiedProviderCredentialLease(Array.Empty<char>()));
        }
        if (!_protection.Supported)
            return (UnifiedProviderStoreAdmission.UnsupportedPlatform, null);
        var loaded = Load();
        var active = loaded.Snapshot?.Instances.SingleOrDefault(value =>
            string.Equals(value.InstanceId, expected.InstanceId, StringComparison.Ordinal));
        if (!loaded.Accepted
            || active == null
            || !active.MatchesGeneration(
                expected.ConfigurationGeneration,
                expected.ConfigurationFingerprint)
            || !string.Equals(
                active.CredentialSha256,
                expected.CredentialSha256,
                StringComparison.Ordinal))
        {
            return (UnifiedProviderStoreAdmission.StaleGeneration, null);
        }
        byte[]? cipher = null;
        byte[]? entropy = null;
        byte[]? plain = null;
        try
        {
            cipher = ReadBounded(
                Path.Combine(_secrets, active.CredentialFile),
                MaximumCredentialCipherBytes);
            var hash = Convert.ToHexString(SHA256.HashData(cipher));
            if (!string.Equals(hash, active.CredentialSha256, StringComparison.Ordinal))
                return (UnifiedProviderStoreAdmission.Corrupt, null);
            entropy = Entropy(active.ProviderKind, active.InstanceId);
            plain = _protection.Unprotect(cipher, entropy);
            return (
                UnifiedProviderStoreAdmission.Accepted,
                new UnifiedProviderCredentialLease(Encoding.UTF8.GetChars(plain)));
        }
        catch (CryptographicException)
        {
            return (UnifiedProviderStoreAdmission.ProtectionFailed, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return (UnifiedProviderStoreAdmission.StorageFailure, null);
        }
        finally
        {
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
            if (entropy != null)
                CryptographicOperations.ZeroMemory(entropy);
            if (plain != null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    private UnifiedProviderStoreResult Persist(
        UnifiedProviderControlPlaneSnapshot snapshot,
        DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_generations);
            Directory.CreateDirectory(_secrets);
            var payload = ToCanonicalPayload(snapshot);
            var bytes = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .SerializePayload(payload);
            if (bytes.Length <= 0 || bytes.Length > MaximumPayloadBytes)
                return Reject(UnifiedProviderStoreAdmission.StorageFailure);
            var payloadHash = Convert.ToHexString(SHA256.HashData(bytes));
            var payloadFile = "providers-g"
                + snapshot.StoreGeneration
                + "-"
                + payloadHash[..16].ToLowerInvariant()
                + ".json";
            WriteImmutable(Path.Combine(_generations, payloadFile), bytes);
            var manifest = new UnifiedProviderControlPlaneCanonicalManifest
            {
                Schema = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .CurrentSchemaVersion,
                StoreGeneration = snapshot.StoreGeneration,
                PayloadFile = payloadFile,
                PayloadSha256 = payloadHash,
                SnapshotFingerprint = snapshot.SnapshotFingerprint,
                CreatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds()
            };
            AtomicWrite(
                _current,
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .SerializeManifest(manifest),
                _previous);
            return Read(_current);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or ArgumentOutOfRangeException)
        {
            return Reject(UnifiedProviderStoreAdmission.StorageFailure);
        }
    }

    private static UnifiedProviderStoreResult EmptyRetiredSnapshot()
        => new(
            UnifiedProviderStoreAdmission.Accepted,
            new UnifiedProviderControlPlaneSnapshot(
                1,
                Array.Empty<UnifiedProviderConfiguredInstance>(),
                null));

    private UnifiedProviderStoreResult Read(string path)
    {
        if (!File.Exists(path))
            return Reject(UnifiedProviderStoreAdmission.Missing);
        try
        {
            var manifestBytes = ReadBounded(path, MaximumManifestBytes);
            var manifest = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .DecodeManifest(manifestBytes, requireCurrentSchema: false);
            var payloadBytes = ReadBounded(
                Path.Combine(_generations, manifest.PayloadFile),
                MaximumPayloadBytes);
            var payload = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .DecodePayload(
                    payloadBytes,
                    manifest,
                    requireCurrentSchema: false);
            var instances = (payload.Instances
                    ?? Array.Empty<UnifiedProviderControlPlaneCanonicalInstance>())
                .Select(value => FromDto(value, payload.Schema))
                .ToArray();
            UnifiedProviderTextAssignment? assignment = null;
            if (payload.TextAssignment != null)
            {
                var configurationFingerprint =
                    payload.TextAssignment.ConfigurationFingerprint;
                if (payload.Schema
                    == UnifiedProviderControlPlaneCanonicalSnapshotCodec
                        .LegacySchemaVersion)
                {
                    var exact = instances.SingleOrDefault(value =>
                        string.Equals(
                            value.InstanceId,
                            payload.TextAssignment.InstanceId,
                            StringComparison.Ordinal)
                        && string.Equals(
                            value.SelectedModel,
                            payload.TextAssignment.ModelId,
                            StringComparison.Ordinal)
                        && value.ConfigurationGeneration
                            == payload.TextAssignment.ConfigurationGeneration
                        && string.Equals(
                            LegacyConfigurationFingerprint(value),
                            payload.TextAssignment.ConfigurationFingerprint,
                            StringComparison.Ordinal));
                    if (exact == null)
                        return Reject(UnifiedProviderStoreAdmission.Corrupt);
                    configurationFingerprint = exact.ConfigurationFingerprint;
                }
                assignment = new UnifiedProviderTextAssignment(
                    payload.TextAssignment.InstanceId,
                    payload.TextAssignment.ModelId,
                    payload.TextAssignment.ConfigurationGeneration,
                    configurationFingerprint);
            }
            var snapshot = new UnifiedProviderControlPlaneSnapshot(
                payload.StoreGeneration,
                instances,
                assignment);
            var persistedSnapshotFingerprint = payload.Schema
                == UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .LegacySchemaVersion
                ? LegacySnapshotFingerprint(
                    payload.StoreGeneration,
                    instances,
                    payload.TextAssignment)
                : snapshot.SnapshotFingerprint;
            if (!string.Equals(
                persistedSnapshotFingerprint,
                manifest.SnapshotFingerprint,
                StringComparison.Ordinal))
            {
                return Reject(UnifiedProviderStoreAdmission.Corrupt);
            }
            return new UnifiedProviderStoreResult(
                UnifiedProviderStoreAdmission.Accepted,
                snapshot);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or ArgumentOutOfRangeException)
        {
            return Reject(UnifiedProviderStoreAdmission.Corrupt);
        }
    }

    private static bool AuthenticationIsAdmitted(
        ProviderControlProfile profile,
        ProviderCustomAuthenticationKind customAuthentication)
    {
        if (!Enum.IsDefined(
                typeof(ProviderCustomAuthenticationKind),
                customAuthentication)
            || customAuthentication == ProviderCustomAuthenticationKind.Invalid)
        {
            return false;
        }
        return profile.Kind == ProviderControlKind.CustomOpenAICompatible
            ? customAuthentication is ProviderCustomAuthenticationKind.None
                or ProviderCustomAuthenticationKind.Bearer
            : customAuthentication == ProviderCustomAuthenticationKind.ProfileDefault;
    }

    private static void ValidateEndpoint(
        ProviderControlProfile profile,
        Uri endpoint,
        string? endpointProfileId,
        string? workspaceId,
        ConfiguredEndpointRouteIntent routeIntent)
    {
        var routeAdmission = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            profile.Kind,
            endpoint,
            routeIntent ?? throw new ArgumentNullException(nameof(routeIntent)),
            endpointProfileId,
            workspaceId);
        if (routeAdmission.Admission
            == UnifiedProviderEndpointAdmission.Rejected)
        {
            throw new ArgumentException(
                "The endpoint does not match its configured route intent.",
                nameof(endpoint));
        }
        if (!profile.PlayerMayEditEndpoint
            && profile.DefaultBaseUri != null
            && !string.Equals(
                endpoint.AbsoluteUri,
                UnifiedProviderConfiguredInstance.NormalizeBaseUri(
                    profile.DefaultBaseUri,
                    nameof(profile.DefaultBaseUri)).AbsoluteUri,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The fixed provider endpoint changed.");
        }
        if (!profile.RequiresExplicitEndpointProfile)
            return;
        var endpointProfile = profile.EndpointProfiles.SingleOrDefault(value =>
            string.Equals(value.ProfileId, endpointProfileId, StringComparison.Ordinal))
            ?? throw new ArgumentException("An exact endpoint profile is required.");
        if (endpointProfile.RequiresWorkspaceId != !string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace identity does not match the endpoint profile.");
        var expected = endpointProfile.BaseAddressTemplate.Replace(
            ProviderEndpointProfile.WorkspacePlaceholder,
            workspaceId ?? string.Empty,
            StringComparison.Ordinal);
        var expectedUri = UnifiedProviderConfiguredInstance.NormalizeBaseUri(
            new Uri(expected, UriKind.Absolute),
            nameof(endpointProfile));
        if (!string.Equals(endpoint.AbsoluteUri, expectedUri.AbsoluteUri, StringComparison.Ordinal))
            throw new ArgumentException("The endpoint does not match its profile.");
    }

    private static UnifiedProviderControlPlaneCanonicalPayload ToCanonicalPayload(
        UnifiedProviderControlPlaneSnapshot snapshot)
        => new()
        {
            Schema = UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .CurrentSchemaVersion,
            StoreGeneration = snapshot.StoreGeneration,
            Instances = snapshot.Instances.Select(value =>
                new UnifiedProviderControlPlaneCanonicalInstance
            {
                InstanceId = value.InstanceId,
                ProviderKind = (int)value.ProviderKind,
                ProfileId = value.ProfileId,
                BaseUri = value.BaseUri.AbsoluteUri,
                EndpointProfileId = value.EndpointProfileId,
                WorkspaceId = value.WorkspaceId,
                RouteKind = (int)value.RouteIntent.Kind,
                RouteExposure = (int)value.RouteIntent.Exposure,
                RouteTrust = (int)value.RouteIntent.Trust,
                RouteTransportProtection =
                    (int)value.RouteIntent.TransportProtection,
                ConfigurationGeneration = value.ConfigurationGeneration,
                SelectedModel = value.SelectedModel,
                CatalogModels = value.CatalogModels.ToArray(),
                CatalogCapabilityEvidence = value.CatalogCapabilityEvidence
                    .Select(evidence =>
                        new UnifiedProviderControlPlaneCanonicalCapabilityEvidence
                    {
                        ModelId = evidence.ModelId,
                        Kind = (int)evidence.Kind,
                        State = (int)evidence.State,
                        Provenance = (int)evidence.Provenance
                    })
                    .ToArray(),
                CatalogObservedAtUnixMilliseconds = value.CatalogObservedAt.ToUnixTimeMilliseconds(),
                CatalogExpiresAtUnixMilliseconds = value.CatalogExpiresAt.ToUnixTimeMilliseconds(),
                CatalogFingerprint = value.CatalogFingerprint,
                CredentialConfigured = value.CredentialConfigured,
                CredentialFile = value.CredentialFile,
                CredentialSha256 = value.CredentialSha256
            }).ToArray(),
            TextAssignment = snapshot.TextAssignment == null
                ? null
                : new UnifiedProviderControlPlaneCanonicalAssignment
                {
                    InstanceId = snapshot.TextAssignment.InstanceId,
                    ModelId = snapshot.TextAssignment.ModelId,
                    ConfigurationGeneration = snapshot.TextAssignment.ConfigurationGeneration,
                    ConfigurationFingerprint = snapshot.TextAssignment.ConfigurationFingerprint
                }
        };

    private static UnifiedProviderConfiguredInstance FromDto(
        UnifiedProviderControlPlaneCanonicalInstance value,
        int schema)
    {
        var provider = (ProviderControlKind)value.ProviderKind;
        var endpoint = new Uri(value.BaseUri, UriKind.Absolute);
        var route = RouteFromDto(value, schema, provider, endpoint);
        var capabilityEvidence = CapabilityEvidenceFromDto(value, schema);
        return new UnifiedProviderConfiguredInstance(
            value.InstanceId,
            provider,
            value.ProfileId,
            endpoint,
            value.EndpointProfileId,
            value.WorkspaceId,
            value.ConfigurationGeneration,
            value.SelectedModel,
            value.CatalogModels ?? Array.Empty<string>(),
            DateTimeOffset.FromUnixTimeMilliseconds(
                value.CatalogObservedAtUnixMilliseconds),
            DateTimeOffset.FromUnixTimeMilliseconds(
                value.CatalogExpiresAtUnixMilliseconds),
            value.CatalogFingerprint,
            value.CredentialConfigured,
            value.CredentialFile,
            value.CredentialSha256,
            route,
            capabilityEvidence);
    }

    private static UnifiedProviderCatalogCapabilityEvidence[]
        CapabilityEvidenceFromDto(
            UnifiedProviderControlPlaneCanonicalInstance value,
            int schema)
    {
        if (schema
            < UnifiedProviderControlPlaneCanonicalSnapshotCodec
                .CurrentSchemaVersion)
            return Array.Empty<UnifiedProviderCatalogCapabilityEvidence>();
        var persisted = value.CatalogCapabilityEvidence
            ?? throw new ArgumentException(
                "Catalog capability evidence is missing.",
                nameof(value));
        if (persisted.Any(evidence => evidence == null))
        {
            throw new ArgumentException(
                "Catalog capability evidence is invalid.",
                nameof(value));
        }
        return persisted.Select(evidence =>
                new UnifiedProviderCatalogCapabilityEvidence(
                    evidence.ModelId,
                    (ProviderModelCapabilityKind)evidence.Kind,
                    (ProviderCapabilityState)evidence.State,
                    (ProviderCapabilityProvenance)evidence.Provenance))
            .ToArray();
    }

    private static ConfiguredEndpointRouteIntent RouteFromDto(
        UnifiedProviderControlPlaneCanonicalInstance value,
        int schema,
        ProviderControlKind provider,
        Uri endpoint)
    {
        var routeFields = new[]
        {
            value.RouteKind,
            value.RouteExposure,
            value.RouteTrust,
            value.RouteTransportProtection
        };
        var allMissing = routeFields.All(field => field == 0);
        var partiallyMissing = routeFields.Any(field => field == 0);
        if (schema
                == UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .LegacySchemaVersion
            && allMissing)
        {
            return ConfiguredEndpointRouteIntent.SafeCompatibilityDefault(
                provider,
                endpoint);
        }
        if (schema is not (
                UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .RouteSchemaVersion
                or UnifiedProviderControlPlaneCanonicalSnapshotCodec
                    .CurrentSchemaVersion)
            || allMissing
            || partiallyMissing)
        {
            throw new ArgumentException(
                "Endpoint route persistence is incomplete.",
                nameof(value));
        }
        return ConfiguredEndpointRouteIntent.FromPersisted(
            value.RouteKind,
            value.RouteExposure,
            value.RouteTrust,
            value.RouteTransportProtection);
    }

    private static string LegacyConfigurationFingerprint(
        UnifiedProviderConfiguredInstance value)
        => UnifiedProviderConfiguredInstance.Hash(
            "D033/CONFIGURED-INSTANCE/1",
            value.InstanceId,
            ((int)value.ProviderKind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            value.ProfileId,
            value.BaseUri.AbsoluteUri,
            value.EndpointProfileId,
            value.WorkspaceId,
            value.ConfigurationGeneration.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            value.SelectedModel,
            value.CatalogFingerprint,
            value.CredentialConfigured ? "1" : "0",
            value.CredentialSha256);

    private static string LegacySnapshotFingerprint(
        long storeGeneration,
        IEnumerable<UnifiedProviderConfiguredInstance> instances,
        UnifiedProviderControlPlaneCanonicalAssignment? assignment)
    {
        var frozen = instances
            .OrderBy(value => value.ProviderKind)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        return UnifiedProviderConfiguredInstance.Hash(
            "D033/CONTROL-PLANE-SNAPSHOT/1",
            storeGeneration.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            string.Join(",", frozen.Select(LegacyConfigurationFingerprint)),
            assignment == null
                ? string.Empty
                : string.Join(
                    ":",
                    assignment.InstanceId,
                    assignment.ModelId,
                    assignment.ConfigurationFingerprint));
    }

    private static byte[] Entropy(ProviderControlKind kind, string instanceId)
        => SHA256.HashData(Encoding.UTF8.GetBytes(
            "LilithAiMod/ProviderControl/V2/CurrentUser/"
            + ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "/"
            + instanceId));

    private static string Below(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(segments.Aggregate(fullRoot, Path.Combine));
        var prefix = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provider path escaped its root.");
        return candidate;
    }

    private static void WriteImmutable(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var existing = ReadBounded(path, Math.Max(bytes.Length, 1) + 1);
            if (!CryptographicOperations.FixedTimeEquals(existing, bytes))
                throw new IOException("Immutable Provider artifact collision.");
            return;
        }
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void AtomicWrite(string path, byte[] bytes, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("Provider file is out of bounds.");
        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0)
                throw new EndOfStreamException();
            offset += count;
        }
        return bytes;
    }

    private static UnifiedProviderStoreResult Reject(
        UnifiedProviderStoreAdmission admission)
        => new(admission);

}

internal sealed class LegacyProviderCredentialAuthorityRetirementReceipt
{
    internal string MarkerPath { get; }
    internal string MetadataArchivePath { get; }
    internal bool LegacyArtifactsRemoved { get; }

    internal LegacyProviderCredentialAuthorityRetirementReceipt(
        string markerPath,
        string metadataArchivePath,
        bool legacyArtifactsRemoved)
    {
        MarkerPath = markerPath;
        MetadataArchivePath = metadataArchivePath;
        LegacyArtifactsRemoved = legacyArtifactsRemoved;
    }
}

/// <summary>
/// Permanently retires the pre-Hermes provider credential stores. The marker is
/// intentionally checked by every legacy read and mutation path, so an upgraded
/// profile cannot recreate a second raw-secret authority after migration.
/// </summary>
internal static class LegacyProviderCredentialAuthorityRetirement
{
    private const string AuthorityDirectoryName =
        "hermes-native-credential-authority-v1";
    private const string MarkerFileName =
        "legacy-provider-credentials-retired-v1.json";
    private const string MetadataArchiveFileName =
        "legacy-provider-metadata-v1.json";
    private const string CoordinationFileName = "retirement.lock";
    private const string WriterCoordinationFileName =
        "retirement-writer.lock";
    private const int MaximumRetirementArtifactBytes = 1024 * 1024;
    private static readonly TimeSpan CoordinationTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly object ProcessEnforcementGate = new();
    private static readonly HashSet<string> ProcessEnforcedMarkers =
        new(StringComparer.OrdinalIgnoreCase);

    internal static LegacyProviderCredentialAuthorityRetirementReceipt
        RetireForCurrentUser()
        => Retire(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData));

    internal static void EnforceForCurrentUserWithoutBlockingStartup()
    {
        try
        {
            _ = RetireForCurrentUser();
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or TimeoutException
            or System.Security.SecurityException)
        {
            // Retire() marks the exact profile fail-closed in-process before
            // touching disk. A later factory call retries physical cleanup.
        }
    }

    internal static string MarkerPath(string localApplicationDataDirectory)
        => Below(
            PlayerRoot(localApplicationDataDirectory),
            "Config",
            AuthorityDirectoryName,
            MarkerFileName);

    internal static string MetadataArchivePath(
        string localApplicationDataDirectory)
        => Below(
            PlayerRoot(localApplicationDataDirectory),
            "Config",
            AuthorityDirectoryName,
            MetadataArchiveFileName);

    internal static bool IsEnforced(string markerPath)
    {
        var exactMarker = Path.GetFullPath(markerPath);
        lock (ProcessEnforcementGate)
        {
            if (ProcessEnforcedMarkers.Contains(exactMarker))
                return true;
        }
        return LegacyLayoutRequiresEnforcement(exactMarker);
    }

    internal static FileStream? TryAcquireLegacyAccess(string markerPath)
    {
        var exactMarker = Path.GetFullPath(markerPath);
        if (IsEnforced(exactMarker))
            return null;
        var directory = Path.GetDirectoryName(exactMarker)
            ?? throw new InvalidOperationException(
                "Credential authority marker requires a directory.");
        var coordinationPath = Path.Combine(
            directory,
            CoordinationFileName);
        var deadline = DateTime.UtcNow + CoordinationTimeout;
        while (true)
        {
            try
            {
                EnsureSafeLegacyLayout(
                    exactMarker,
                    createAuthorityRoot: true);
                EnsureSafeRegularFileOrAbsent(coordinationPath);
                var lease = new FileStream(
                    coordinationPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);
                try
                {
                    EnsureSafeRegularFileOrAbsent(coordinationPath);
                    if (!IsEnforced(exactMarker))
                        return lease;
                    lease.Dispose();
                    return null;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            catch (IOException)
            {
                if (IsEnforced(exactMarker) || DateTime.UtcNow >= deadline)
                    return null;
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }
        }
    }

    internal static LegacyProviderCredentialAuthorityRetirementReceipt Retire(
        string localApplicationDataDirectory)
    {
        var playerRoot = PlayerRoot(localApplicationDataDirectory);
        var authorityRoot = Below(
            playerRoot,
            "Config",
            AuthorityDirectoryName);
        var markerPath = Below(authorityRoot, MarkerFileName);
        lock (ProcessEnforcementGate)
            ProcessEnforcedMarkers.Add(markerPath);
        var retiringPath = RetiringMarkerPath(markerPath);
        var metadataArchivePath = Below(
            authorityRoot,
            MetadataArchiveFileName);
        var coordinationPath = Below(
            authorityRoot,
            CoordinationFileName);
        var writerCoordinationPath = Below(
            authorityRoot,
            WriterCoordinationFileName);
        var legacyDirectories = new[]
        {
            Below(playerRoot, "Config", "provider-control-v1"),
            Below(playerRoot, "Config", "provider-control-v2"),
            Below(playerRoot, "Secrets", "provider-control-v1"),
            Below(playerRoot, "Secrets", "provider-control-v2")
        };

        EnsureSafeLegacyLayout(markerPath, createAuthorityRoot: true);
        foreach (var artifact in new[]
                 {
                     markerPath,
                     retiringPath,
                     metadataArchivePath,
                     coordinationPath,
                     writerCoordinationPath
                 })
        {
            EnsureSafeRegularFileOrAbsent(artifact);
        }
        if (File.Exists(markerPath)
            && !ValidateMarker(
                markerPath,
                "retired",
                metadataArchivePath))
        {
            // An existing final marker already gates every updated reader. Do
            // not create writer coordination or a new .retiring artifact when
            // that pre-existing commit is invalid (including pre-digest v1).
            throw new IOException(
                "The provider credential retirement marker is invalid.");
        }
        // Serialize writers separately, then publish a durable intent gate
        // before waiting for existing shared readers to drain. Updated readers
        // observe .retiring and cannot continuously starve this writer.
        using var writerLease = AcquireExclusive(writerCoordinationPath);
        EnsureSafeRegularFileOrAbsent(markerPath);
        if (File.Exists(markerPath))
        {
            if (!ValidateMarker(
                    markerPath,
                    "retired",
                    metadataArchivePath))
            {
                throw new IOException(
                    "The provider credential retirement marker is invalid.");
            }
        }
        else
        {
            PublishWriterIntentMarkerIfAbsent(
                retiringPath,
                metadataArchivePath);
        }
        using var exclusiveLease = AcquireExclusive(coordinationPath);
        EnsureSafeLegacyLayout(markerPath, createAuthorityRoot: false);
        foreach (var artifact in new[]
                 {
                     markerPath,
                     retiringPath,
                     metadataArchivePath,
                     coordinationPath,
                     writerCoordinationPath
                 })
        {
            EnsureSafeRegularFileOrAbsent(artifact);
        }
        var finalMarkerExists = File.Exists(markerPath);
        var retiringMarkerExists = File.Exists(retiringPath);
        var finalMarkerValid = finalMarkerExists
            && ValidateMarker(
                markerPath,
                "retired",
                metadataArchivePath);
        if (finalMarkerExists && !finalMarkerValid)
        {
            // Atomic publication cannot leave a partial final marker. Treat an
            // invalid marker, including an archive digest mismatch, as tamper
            // or storage damage and never mint a new integrity root for it.
            throw new IOException(
                "The provider credential retirement marker is invalid.");
        }

        var archiveReadyForCleanup = retiringMarkerExists
            && ValidateArchiveReadyMarker(
                retiringPath,
                metadataArchivePath);
        var genericRetiringMarkerValid = retiringMarkerExists
            && ValidateMarker(
                retiringPath,
                "retiring",
                metadataArchivePath: null);
        if (retiringMarkerExists
            && !archiveReadyForCleanup
            && !genericRetiringMarkerValid)
        {
            // In particular, do not overwrite an archive-ready marker whose
            // SHA-256 no longer matches the archive. Once cleanup can have
            // started, the journal is the only proof of the complete archive.
            throw new IOException(
                "The provider credential retirement journal is invalid.");
        }
        if (!finalMarkerValid && !archiveReadyForCleanup)
        {
            // Publish the fail-closed boundary before reading any legacy
            // authority. A strict generic marker denotes the pre-cleanup phase
            // and may be retried; any other pre-existing marker was rejected
            // above rather than silently replaced.
            if (!retiringMarkerExists)
            {
                PublishMarker(
                    retiringPath,
                    "retiring",
                    metadataArchivePath: null);
            }
            AtomicPublishValidated(
                metadataArchivePath,
                BuildMetadataArchivePayload(localApplicationDataDirectory),
                ValidateMetadataArchive);
            if (!ValidateMetadataArchive(metadataArchivePath))
            {
                throw new IOException(
                    "The retired provider metadata archive could not be verified.");
            }

            // This journal state closes the partial-deletion retry gap: once it
            // is durable, cleanup may remove any subset of legacy trees without
            // a later retry replacing or re-signing the complete archive.
            PublishArchiveReadyMarker(retiringPath, metadataArchivePath);
            archiveReadyForCleanup = true;
        }
        // Keep the exact archive path read-only through cleanup and final-marker
        // publication. This prevents a cooperating same-user process from
        // replacing the archive between digest validation and tree deletion.
        using var archiveReadLease = new FileStream(
            metadataArchivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var archiveBindingValid = finalMarkerValid
            ? ValidateMarker(markerPath, "retired", metadataArchivePath)
            : ValidateArchiveReadyMarker(retiringPath, metadataArchivePath);
        if (!ValidateMetadataArchive(metadataArchivePath)
            || !archiveBindingValid)
        {
            throw new IOException(
                "The retired provider metadata archive is not bound to cleanup.");
        }

        foreach (var directory in legacyDirectories)
            DeleteExactDirectory(directory, playerRoot);
        var removed = legacyDirectories.All(directory =>
            !ExactPathEntryExists(directory));
        if (!removed)
        {
            throw new IOException(
                "Legacy provider credential artifacts were not fully removed.");
        }

        // The final marker is the commit boundary. It is published only after
        // the metadata archive is durable and every legacy tree reads absent.
        PublishMarker(
            markerPath,
            "retired",
            metadataArchivePath);
        if (!ValidateMarker(
                markerPath,
                "retired",
                metadataArchivePath))
        {
            throw new IOException(
                "The provider credential retirement marker could not be verified.");
        }

        EnsureSafeRegularFileOrAbsent(retiringPath);
        File.Delete(retiringPath);
        return new LegacyProviderCredentialAuthorityRetirementReceipt(
            markerPath,
            metadataArchivePath,
            legacyArtifactsRemoved: true);
    }

    private static FileStream AcquireExclusive(string coordinationPath)
    {
        var deadline = DateTime.UtcNow + CoordinationTimeout;
        while (true)
        {
            try
            {
                EnsureSafeRegularFileOrAbsent(coordinationPath);
                var lease = new FileStream(
                    coordinationPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
                try
                {
                    EnsureSafeRegularFileOrAbsent(coordinationPath);
                    return lease;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static byte[] BuildMetadataArchivePayload(
        string localApplicationDataDirectory)
    {
        var unified = new UnifiedProviderControlPlaneStore(
                localApplicationDataDirectory)
            .LoadBeforeCredentialAuthorityRetirement();
        var legacy = new DeepSeekControlPlaneStore(
                localApplicationDataDirectory)
            .LoadBeforeCredentialAuthorityRetirement();
        if (!unified.Accepted || unified.Snapshot == null)
        {
            throw new IOException(
                "The unified legacy provider metadata could not be admitted: "
                + unified.Admission + ".");
        }
        if (legacy.Admission is not (
                DeepSeekControlAdmission.Accepted
                or DeepSeekControlAdmission.Missing))
        {
            throw new IOException(
                "The legacy provider metadata could not be admitted: "
                + legacy.Admission + ".");
        }
        var archive = new
        {
            schema = 1,
            raw_secret_authority = "hermes-native",
            unified_store_generation = unified.Snapshot.StoreGeneration,
            configured_instances = unified.Snapshot.Instances.Select(value => new
                {
                    instance_id = value.InstanceId,
                    provider_kind = (int)value.ProviderKind,
                    profile_id = value.ProfileId,
                    base_uri = SanitizeEndpoint(value.BaseUri),
                    endpoint_profile_id = value.EndpointProfileId,
                    workspace_id = value.WorkspaceId,
                    configuration_generation = value.ConfigurationGeneration,
                    selected_model = value.SelectedModel,
                    catalog_models = value.CatalogModels,
                    catalog_observed_at = value.CatalogObservedAt,
                    catalog_expires_at = value.CatalogExpiresAt,
                    route = new
                    {
                        kind = (int)value.RouteIntent.Kind,
                        exposure = (int)value.RouteIntent.Exposure,
                        trust = (int)value.RouteIntent.Trust,
                        transport_protection =
                            (int)value.RouteIntent.TransportProtection
                    },
                    capability_evidence = value.CatalogCapabilityEvidence
                        .Select(evidence => new
                        {
                            model_id = evidence.ModelId,
                            kind = (int)evidence.Kind,
                            state = (int)evidence.State,
                            provenance = (int)evidence.Provenance
                        })
                }).ToArray(),
            text_assignment = unified.Snapshot.TextAssignment == null
                ? null
                : new
                {
                    instance_id = unified.Snapshot.TextAssignment.InstanceId,
                    model_id = unified.Snapshot.TextAssignment.ModelId,
                    configuration_generation = unified.Snapshot.TextAssignment
                        .ConfigurationGeneration
                },
            legacy_v1 = legacy.Snapshot == null
                ? null
                : new
                {
                    configuration_generation = legacy.Snapshot
                        .ConfigurationGeneration,
                    selected_model = legacy.Snapshot.SelectedModel,
                    catalog_models = legacy.Snapshot.CatalogModels,
                    catalog_observed_at = legacy.Snapshot.CatalogObservedAt,
                    catalog_expires_at = legacy.Snapshot.CatalogExpiresAt
                },
            archived_at_utc = DateTimeOffset.UtcNow
        };
        return JsonSerializer.SerializeToUtf8Bytes(archive);
    }

    private static void PublishWriterIntentMarkerIfAbsent(
        string path,
        string metadataArchivePath)
    {
        EnsureSafeRegularFileOrAbsent(path);
        if (File.Exists(path))
        {
            if (ValidateMarker(
                    path,
                    "retiring",
                    metadataArchivePath: null)
                || ValidateArchiveReadyMarker(path, metadataArchivePath))
            {
                return;
            }
            throw new IOException(
                "The existing provider retirement writer gate is invalid.");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1,
            state = "retiring",
            raw_secret_authority = "hermes-native",
            recorded_at_utc = DateTimeOffset.UtcNow
        });
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The retirement writer gate requires a directory.");
        EnsureSafeDirectoryPath(directory, createMissing: true);
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(path) + "."
            + Guid.NewGuid().ToString("N") + ".intent.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }
            if (!ValidateMarker(
                    temporaryPath,
                    "retiring",
                    metadataArchivePath: null))
            {
                throw new IOException(
                    "The staged provider retirement writer gate is invalid.");
            }

            EnsureSafeRegularFileOrAbsent(path);
            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException)
            {
                // Another serialized/recovering writer may already have
                // published the same gate. Never replace or delete its entry.
                if (!File.Exists(path))
                    throw;
            }
            if (!ValidateMarker(
                    path,
                    "retiring",
                    metadataArchivePath: null)
                && !ValidateArchiveReadyMarker(path, metadataArchivePath))
            {
                throw new IOException(
                    "The provider retirement writer gate failed validation.");
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void PublishMarker(
        string path,
        string state,
        string? metadataArchivePath)
    {
        var payload = metadataArchivePath == null
            ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1,
                state,
                raw_secret_authority = "hermes-native",
                recorded_at_utc = DateTimeOffset.UtcNow
            })
            : JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1,
                state,
                raw_secret_authority = "hermes-native",
                metadata_archive = MetadataArchiveFileName,
                metadata_sha256 = FileSha256(metadataArchivePath),
                recorded_at_utc = DateTimeOffset.UtcNow
            });
        AtomicPublishValidated(
            path,
            payload,
            candidate => ValidateMarker(
                candidate,
                state,
                metadataArchivePath));
    }

    private static void PublishArchiveReadyMarker(
        string path,
        string metadataArchivePath)
    {
        if (!ValidateMetadataArchive(metadataArchivePath))
        {
            throw new IOException(
                "The metadata archive is invalid before cleanup journaling.");
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1,
            state = "archive-ready",
            raw_secret_authority = "hermes-native",
            metadata_archive = MetadataArchiveFileName,
            metadata_sha256 = FileSha256(metadataArchivePath),
            recorded_at_utc = DateTimeOffset.UtcNow
        });
        AtomicPublishValidated(
            path,
            payload,
            candidate => ValidateArchiveReadyMarker(
                candidate,
                metadataArchivePath));
        if (!ValidateMetadataArchive(metadataArchivePath)
            || !ValidateArchiveReadyMarker(path, metadataArchivePath))
        {
            throw new IOException(
                "The archive-ready cleanup journal failed readback validation.");
        }
    }

    private static void AtomicPublishValidated(
        string path,
        byte[] payload,
        Func<string, bool> validate)
    {
        if (payload == null || payload.Length == 0
            || payload.Length > MaximumRetirementArtifactBytes)
        {
            throw new IOException(
                "The retirement artifact payload is invalid.");
        }
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The retirement artifact requires a directory.");
        EnsureSafeDirectoryPath(directory, createMissing: true);
        EnsureSafeRegularFileOrAbsent(path);
        var temporaryPath = Path.Combine(
            directory,
            "." + Path.GetFileName(path) + "."
            + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }
            if (!validate(temporaryPath))
                throw new IOException("The staged retirement artifact is invalid.");

            EnsureSafeRegularFileOrAbsent(path);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            else
                File.Move(temporaryPath, path);

            if (!validate(path))
            {
                throw new IOException(
                    "The published retirement artifact failed readback validation.");
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static bool ValidateMarker(
        string path,
        string state,
        string? metadataArchivePath)
    {
        try
        {
            using var document = ReadBoundedJson(path);
            var root = document.RootElement;
            var expected = metadataArchivePath == null
                ? new[]
                {
                    "schema",
                    "state",
                    "raw_secret_authority",
                    "recorded_at_utc"
                }
                : new[]
                {
                    "schema",
                    "state",
                    "raw_secret_authority",
                    "metadata_archive",
                    "metadata_sha256",
                    "recorded_at_utc"
                };
            if (!HasExactProperties(root, expected)
                || !IsInteger(root, "schema", 1)
                || !IsString(root, "state", state)
                || !IsString(root, "raw_secret_authority", "hermes-native")
                || !IsTimestamp(root, "recorded_at_utc"))
            {
                return false;
            }
            if (metadataArchivePath == null)
                return true;
            if (!IsString(
                    root,
                    "metadata_archive",
                    MetadataArchiveFileName))
            {
                return false;
            }
            var expectedSha256 = root.GetProperty("metadata_sha256").GetString();
            return IsSha256(expectedSha256)
                && string.Equals(
                    expectedSha256,
                    FileSha256(metadataArchivePath),
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidateArchiveReadyMarker(
        string path,
        string metadataArchivePath)
    {
        try
        {
            using var document = ReadBoundedJson(path);
            var root = document.RootElement;
            if (!HasExactProperties(
                    root,
                    "schema",
                    "state",
                    "raw_secret_authority",
                    "metadata_archive",
                    "metadata_sha256",
                    "recorded_at_utc")
                || !IsInteger(root, "schema", 1)
                || !IsString(root, "state", "archive-ready")
                || !IsString(
                    root,
                    "raw_secret_authority",
                    "hermes-native")
                || !IsString(
                    root,
                    "metadata_archive",
                    MetadataArchiveFileName)
                || !IsTimestamp(root, "recorded_at_utc"))
            {
                return false;
            }
            var expected = root.GetProperty("metadata_sha256").GetString();
            return IsSha256(expected)
                && string.Equals(
                    expected,
                    FileSha256(metadataArchivePath),
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static string FileSha256(string path)
    {
        EnsureSafeRegularFile(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0
            || info.Length > MaximumRetirementArtifactBytes)
        {
            throw new IOException("The metadata archive size is invalid.");
        }
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }

    private static bool IsSha256(string? value)
        => value?.Length == 64
           && value.All(character => character is >= '0' and <= '9'
               or >= 'A' and <= 'F');

    private static bool ValidateMetadataArchive(string path)
    {
        try
        {
            using var document = ReadBoundedJson(path);
            var root = document.RootElement;
            if (!HasExactProperties(
                    root,
                    "schema",
                    "raw_secret_authority",
                    "unified_store_generation",
                    "configured_instances",
                    "text_assignment",
                    "legacy_v1",
                    "archived_at_utc")
                || !IsInteger(root, "schema", 1)
                || !IsString(root, "raw_secret_authority", "hermes-native")
                || !IsNullablePositiveInteger(root, "unified_store_generation")
                || !IsTimestamp(root, "archived_at_utc"))
            {
                return false;
            }

            var configured = root.GetProperty("configured_instances");
            if (configured.ValueKind != JsonValueKind.Array
                || configured.GetArrayLength()
                    > UnifiedProviderControlPlaneStore.MaximumInstances)
            {
                return false;
            }
            var instances = configured.EnumerateArray().ToArray();
            if (instances.Select(instance =>
                    instance.GetProperty("instance_id").GetString())
                .Distinct(StringComparer.Ordinal).Count() != instances.Length)
            {
                return false;
            }
            foreach (var instance in instances)
            {
                if (!ValidateArchivedInstance(instance))
                    return false;
            }
            var assignment = root.GetProperty("text_assignment");
            if (!ValidateNullableAssignment(assignment)
                || !AssignmentMatchesArchivedInstance(assignment, instances)
                || !ValidateNullableLegacyV1(root.GetProperty("legacy_v1")))
            {
                return false;
            }
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ValidateArchivedInstance(JsonElement value)
    {
        if (!HasExactProperties(
                value,
                "instance_id",
                "provider_kind",
                "profile_id",
                "base_uri",
                "endpoint_profile_id",
                "workspace_id",
                "configuration_generation",
                "selected_model",
                "catalog_models",
                "catalog_observed_at",
                "catalog_expires_at",
                "route",
                "capability_evidence")
            || !IsBoundedString(value, "instance_id", 96, optional: false)
            || !IsDefinedEnum<ProviderControlKind>(value, "provider_kind")
            || !IsBoundedString(value, "profile_id", 64, optional: false)
            || !IsSanitizedEndpoint(value, "base_uri")
            || !IsBoundedString(value, "endpoint_profile_id", 96, optional: true)
            || !IsBoundedString(value, "workspace_id", 128, optional: true)
            || !IsPositiveInteger(value, "configuration_generation")
            || !IsBoundedString(value, "selected_model", 128, optional: false)
            || !IsTimestamp(value, "catalog_observed_at")
            || !IsTimestamp(value, "catalog_expires_at"))
        {
            return false;
        }
        var models = value.GetProperty("catalog_models");
        if (!ValidateStringArray(
                models,
                UnifiedProviderControlPlaneStore.MaximumModels,
                128)
            || !models.EnumerateArray().Any(model => string.Equals(
                model.GetString(),
                value.GetProperty("selected_model").GetString(),
                StringComparison.Ordinal)))
        {
            return false;
        }
        var route = value.GetProperty("route");
        if (!HasExactProperties(
                route,
                "kind",
                "exposure",
                "trust",
                "transport_protection")
            || !IsDefinedEnum<ConfiguredEndpointRouteKind>(route, "kind")
            || !IsDefinedEnum<ConfiguredEndpointExposure>(route, "exposure")
            || !IsDefinedEnum<ConfiguredEndpointTrust>(route, "trust")
            || !IsDefinedEnum<ConfiguredEndpointTransportProtection>(
                route,
                "transport_protection")
            || !IsCanonicalArchivedRoute(route))
        {
            return false;
        }
        var evidence = value.GetProperty("capability_evidence");
        if (evidence.ValueKind != JsonValueKind.Array
            || evidence.GetArrayLength()
                > UnifiedProviderControlPlaneStore.MaximumModels * 8)
        {
            return false;
        }
        foreach (var item in evidence.EnumerateArray())
        {
            if (!HasExactProperties(
                    item,
                    "model_id",
                    "kind",
                    "state",
                    "provenance")
                || !IsBoundedString(item, "model_id", 128, optional: false)
                || !IsDefinedEnum<ProviderModelCapabilityKind>(item, "kind")
                || !IsDefinedEnum<ProviderCapabilityState>(item, "state")
                || !IsDefinedEnum<ProviderCapabilityProvenance>(
                    item,
                    "provenance")
                || item.GetProperty("state").GetInt32()
                    != (int)ProviderCapabilityState.Advertised
                || item.GetProperty("provenance").GetInt32() is not (
                    (int)ProviderCapabilityProvenance.OfficialModelCatalog
                    or (int)ProviderCapabilityProvenance
                        .OfficialModelMetadataEndpoint)
                || !models.EnumerateArray().Any(model => string.Equals(
                    model.GetString(),
                    item.GetProperty("model_id").GetString(),
                    StringComparison.Ordinal)))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidateNullableAssignment(JsonElement value)
        => value.ValueKind == JsonValueKind.Null
           || (HasExactProperties(
                   value,
                   "instance_id",
                   "model_id",
                   "configuration_generation")
               && IsBoundedString(value, "instance_id", 96, optional: false)
               && IsBoundedString(value, "model_id", 128, optional: false)
               && IsPositiveInteger(value, "configuration_generation"));

    private static bool AssignmentMatchesArchivedInstance(
        JsonElement assignment,
        IReadOnlyList<JsonElement> instances)
    {
        if (assignment.ValueKind == JsonValueKind.Null)
            return true;
        var instanceId = assignment.GetProperty("instance_id").GetString();
        var modelId = assignment.GetProperty("model_id").GetString();
        var generation = assignment.GetProperty(
            "configuration_generation").GetInt64();
        return instances.Any(instance =>
            string.Equals(
                instance.GetProperty("instance_id").GetString(),
                instanceId,
                StringComparison.Ordinal)
            && string.Equals(
                instance.GetProperty("selected_model").GetString(),
                modelId,
                StringComparison.Ordinal)
            && instance.GetProperty("configuration_generation").GetInt64()
                == generation);
    }

    private static bool IsCanonicalArchivedRoute(JsonElement route)
    {
        var kind = (ConfiguredEndpointRouteKind)route.GetProperty("kind")
            .GetInt32();
        var exposure = (ConfiguredEndpointExposure)route.GetProperty(
            "exposure").GetInt32();
        var trust = (ConfiguredEndpointTrust)route.GetProperty("trust")
            .GetInt32();
        var transport = (ConfiguredEndpointTransportProtection)route
            .GetProperty("transport_protection").GetInt32();
        return (kind, exposure, trust, transport) switch
        {
            (
                ConfiguredEndpointRouteKind.ManagedCloudHttps,
                ConfiguredEndpointExposure.ManagedCloud,
                ConfiguredEndpointTrust.OfficialCloudHttps,
                ConfiguredEndpointTransportProtection.HttpsRequired) => true,
            (
                ConfiguredEndpointRouteKind.LoopbackOnly,
                ConfiguredEndpointExposure.LoopbackOnly,
                ConfiguredEndpointTrust.LoopbackRouteAdmitted,
                ConfiguredEndpointTransportProtection.LoopbackHttpPermitted) => true,
            (
                ConfiguredEndpointRouteKind.ExplicitLan,
                ConfiguredEndpointExposure.LanExposedDeclared,
                ConfiguredEndpointTrust.ExplicitLan,
                ConfiguredEndpointTransportProtection.HttpsRequired) => true,
            (
                ConfiguredEndpointRouteKind.ExplicitRemoteHttps,
                ConfiguredEndpointExposure.PublicExposedDeclared,
                ConfiguredEndpointTrust.ExplicitRemoteCustom,
                ConfiguredEndpointTransportProtection.HttpsRequired) => true,
            _ => false
        };
    }

    private static bool ValidateNullableLegacyV1(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        return HasExactProperties(
                   value,
                   "configuration_generation",
                   "selected_model",
                   "catalog_models",
                   "catalog_observed_at",
                   "catalog_expires_at")
               && IsPositiveInteger(value, "configuration_generation")
               && IsBoundedString(value, "selected_model", 128, optional: false)
               && ValidateStringArray(
                   value.GetProperty("catalog_models"),
                   DeepSeekControlPlanePolicy.MaximumModels,
                   128)
               && IsTimestamp(value, "catalog_observed_at")
               && IsTimestamp(value, "catalog_expires_at");
    }

    private static JsonDocument ReadBoundedJson(string path)
    {
        EnsureSafeRegularFile(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0
            || info.Length > MaximumRetirementArtifactBytes)
        {
            throw new IOException("The retirement artifact size is invalid.");
        }
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        return JsonDocument.Parse(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
    }

    private static bool HasExactProperties(
        JsonElement value,
        params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var properties = value.EnumerateObject().ToArray();
        return properties.Length == expected.Length
            && properties.All(property => expected.Contains(
                property.Name,
                StringComparer.Ordinal));
    }

    private static bool IsString(
        JsonElement owner,
        string propertyName,
        string expected)
        => owner.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && string.Equals(
               value.GetString(),
               expected,
               StringComparison.Ordinal);

    private static bool IsInteger(
        JsonElement owner,
        string propertyName,
        int expected)
        => owner.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
           && parsed == expected;

    private static bool IsPositiveInteger(
        JsonElement owner,
        string propertyName)
        => owner.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var parsed)
           && parsed > 0;

    private static bool IsNullablePositiveInteger(
        JsonElement owner,
        string propertyName)
        => owner.TryGetProperty(propertyName, out var value)
           && (value.ValueKind == JsonValueKind.Null
               || (value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt64(out var parsed)
                   && parsed > 0));

    private static bool IsBoundedString(
        JsonElement owner,
        string propertyName,
        int maximumLength,
        bool optional)
    {
        if (!owner.TryGetProperty(propertyName, out var value))
            return false;
        if (optional && value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var text = value.GetString();
        return text != null
            && (optional || !string.IsNullOrWhiteSpace(text))
            && text.Length <= maximumLength;
    }

    private static bool IsTimestamp(JsonElement owner, string propertyName)
        => owner.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(
               value.GetString(),
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.RoundtripKind,
               out _);

    private static bool IsSanitizedEndpoint(
        JsonElement owner,
        string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri))
        {
            return false;
        }
        return uri.Scheme is "http" or "https"
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool ValidateStringArray(
        JsonElement value,
        int maximumItems,
        int maximumLength)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximumItems)
        {
            return false;
        }
        var values = value.EnumerateArray().ToArray();
        return values.Length > 0
               && values.All(item =>
                   item.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(item.GetString())
                   && item.GetString()!.Length <= maximumLength)
               && values.Select(item => item.GetString())
                   .Distinct(StringComparer.Ordinal).Count() == values.Length;
    }

    private static bool IsDefinedEnum<TEnum>(
        JsonElement owner,
        string propertyName)
        where TEnum : struct, Enum
        => owner.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var parsed)
           && parsed != 0
           && Enum.IsDefined(typeof(TEnum), parsed);

    private static bool LegacyLayoutRequiresEnforcement(string markerPath)
    {
        try
        {
            EnsureSafeLegacyLayout(markerPath, createAuthorityRoot: false);
            if (TryGetPathAttributes(markerPath, out _)
                || TryGetPathAttributes(
                    RetiringMarkerPath(markerPath),
                    out _))
            {
                return true;
            }
            var authorityRoot = Path.GetDirectoryName(markerPath)
                ?? throw new InvalidOperationException(
                    "Credential authority marker requires a directory.");
            EnsureSafeRegularFileOrAbsent(Path.Combine(
                authorityRoot,
                MetadataArchiveFileName));
            EnsureSafeRegularFileOrAbsent(Path.Combine(
                authorityRoot,
                CoordinationFileName));
            EnsureSafeRegularFileOrAbsent(Path.Combine(
                authorityRoot,
                WriterCoordinationFileName));
            return false;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static void EnsureSafeLegacyLayout(
        string markerPath,
        bool createAuthorityRoot)
    {
        var exactMarker = Path.GetFullPath(markerPath);
        var authorityRoot = Path.GetDirectoryName(exactMarker)
            ?? throw new InvalidOperationException(
                "Credential authority marker requires a directory.");
        var configRoot = Path.GetDirectoryName(authorityRoot)
            ?? throw new InvalidOperationException(
                "Credential authority root requires a Config directory.");
        var playerRoot = Path.GetDirectoryName(configRoot)
            ?? throw new InvalidOperationException(
                "Credential Config root requires a player directory.");
        if (!string.Equals(
                Path.GetFileName(exactMarker),
                MarkerFileName,
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(authorityRoot),
                AuthorityDirectoryName,
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(configRoot),
                "Config",
                StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(playerRoot),
                "LilithAiMod",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Credential authority layout is not canonical.");
        }

        var secretsRoot = Path.Combine(playerRoot, "Secrets");
        EnsureSafeDirectoryPath(playerRoot, createMissing: false);
        EnsureSafeDirectoryPath(configRoot, createMissing: false);
        EnsureSafeDirectoryPath(secretsRoot, createMissing: false);
        foreach (var legacyDirectory in new[]
                 {
                     Path.Combine(configRoot, "provider-control-v1"),
                     Path.Combine(configRoot, "provider-control-v2"),
                     Path.Combine(secretsRoot, "provider-control-v1"),
                     Path.Combine(secretsRoot, "provider-control-v2")
                 })
        {
            EnsureSafeDirectoryPath(legacyDirectory, createMissing: false);
        }
        EnsureSafeDirectoryPath(authorityRoot, createAuthorityRoot);
    }

    private static void EnsureSafeDirectoryPath(
        string path,
        bool createMissing)
    {
        var exactPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(exactPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("A rooted path is required.");
        var exactRoot = Path.GetFullPath(root);
        var rootAttributes = File.GetAttributes(exactRoot);
        if ((rootAttributes & FileAttributes.Directory) == 0
            || (rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The filesystem root is unsafe.");
        }

        var relative = Path.GetRelativePath(exactRoot, exactPath);
        if (string.Equals(relative, ".", StringComparison.Ordinal))
            return;
        var current = exactRoot;
        foreach (var segment in relative.Split(
                     new[]
                     {
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar
                     },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!TryGetPathAttributes(current, out var attributes))
            {
                if (!createMissing)
                    return;
                Directory.CreateDirectory(current);
                if (!TryGetPathAttributes(current, out attributes))
                {
                    throw new IOException(
                        "A credential authority directory was not created.");
                }
            }
            if ((attributes & FileAttributes.Directory) == 0
                || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    "A credential authority path component is unsafe.");
            }
        }
    }

    private static void EnsureSafeRegularFileOrAbsent(string path)
    {
        var exactPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(exactPath)
            ?? throw new InvalidOperationException(
                "A credential authority file requires a directory.");
        EnsureSafeDirectoryPath(parent, createMissing: false);
        if (!TryGetPathAttributes(exactPath, out var attributes))
            return;
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
            != 0)
        {
            throw new IOException(
                "A credential authority artifact is not a regular file.");
        }
    }

    private static void EnsureSafeRegularFile(string path)
    {
        EnsureSafeRegularFileOrAbsent(path);
        if (!TryGetPathAttributes(path, out _))
            throw new FileNotFoundException(
                "The credential authority artifact is missing.",
                path);
    }

    private static void DeleteExactDirectory(string path, string playerRoot)
    {
        var exactPath = Path.GetFullPath(path);
        var exactRoot = Path.GetFullPath(playerRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var prefix = exactRoot + Path.DirectorySeparatorChar;
        if (!exactPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(exactPath, exactRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Legacy retirement path escaped the player root.");
        }
        EnsureSafeDirectoryPath(
            Path.GetDirectoryName(exactPath)
                ?? throw new InvalidOperationException(
                    "A legacy retirement path requires a directory."),
            createMissing: false);
        if (!TryGetPathAttributes(exactPath, out var attributes))
            return;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                "A legacy provider directory is an unsupported reparse point.");
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException(
                "A legacy provider directory was replaced by a file.");
        }
        Directory.Delete(exactPath, recursive: true);
    }

    private static bool ExactPathEntryExists(string path)
    {
        var exactPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(exactPath);
        if (parent == null || !Directory.Exists(parent))
            return false;
        return Directory.EnumerateFileSystemEntries(
                parent,
                "*",
                SearchOption.TopDirectoryOnly)
            .Any(entry => string.Equals(
                Path.GetFullPath(entry),
                exactPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetPathAttributes(
        string path,
        out FileAttributes attributes)
        => TryGetPathAttributes(
            path,
            File.GetAttributes,
            ExactPathEntryExists,
            out attributes);

    // Per-call probes make the missing -> created interleaving testable without
    // a mutable process-wide hook. Production always binds the real filesystem.
    internal static bool TryGetPathAttributes(
        string path,
        Func<string, FileAttributes> getAttributes,
        Func<string, bool> exactPathEntryExists,
        out FileAttributes attributes)
    {
        try
        {
            attributes = getAttributes(path);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // File.Exists/Directory.Exists both return false for a dangling
            // link, but a cooperating writer can also create a normal lock file
            // between our failed lookup and parent enumeration. Re-read once;
            // enumeration alone proves neither a dangling link nor a safe file.
            if (exactPathEntryExists(path))
            {
                try
                {
                    attributes = getAttributes(path);
                    // Preserve all flags for the existing directory/reparse
                    // rejection at the caller. This is not a safety admission.
                    return true;
                }
                catch (Exception recheckException) when (
                    recheckException is FileNotFoundException
                    or DirectoryNotFoundException)
                {
                    throw new IOException(
                        "A dangling or unstable credential authority path entry is unsafe.",
                        recheckException);
                }
            }
            attributes = default;
            return false;
        }
    }

    private static string SanitizeEndpoint(Uri endpoint)
    {
        var builder = new UriBuilder(endpoint)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string PlayerRoot(string localApplicationDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
        {
            throw new ArgumentException(
                "Local application data is required.",
                nameof(localApplicationDataDirectory));
        }
        return Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            "LilithAiMod");
    }

    private static string RetiringMarkerPath(string markerPath)
        => markerPath + ".retiring";

    private static string Below(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            segments.Aggregate(fullRoot, Path.Combine));
        var prefix = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Credential authority path escaped its root.");
        return candidate;
    }
}
