using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithTextInjector;

internal enum ProviderUserStoreMode
{
    Invalid = 0,
    Off = 1,
    Armed = 2,
    Fault = 3,
    Capacity = 4
}

internal enum ProviderUserStoreAdmission
{
    Invalid = 0,
    Accepted = 1,
    Missing = 2,
    RecoveredPrevious = 3,
    Off = 4,
    ConcurrentMutation = 5,
    ProcessCapacityReached = 6,
    DuplicateTransaction = 7,
    TicketExpired = 8,
    TicketConsumed = 9,
    TicketMismatch = 10,
    GenerationConflict = 11,
    Corrupt = 12,
    BindingMismatch = 13,
    UnsupportedPlatform = 14,
    ProtectionFailed = 15,
    IoFailure = 16
}

internal enum ProviderCredentialHealth
{
    Invalid = 0,
    Missing = 1,
    ProtectedHealthy = 2,
    Corrupt = 3,
    StaleBinding = 4,
    UnsupportedPlatform = 5
}

internal sealed class ProviderUserStorePaths
{
    internal string RootDirectory { get; }
    internal string ConfigurationDirectory { get; }
    internal string ConfigurationGenerationsDirectory { get; }
    internal string ConfigurationCurrentManifestPath { get; }
    internal string CredentialDirectory { get; }
    internal string CredentialBlobDirectory { get; }
    internal string CredentialReferenceDirectory { get; }

    internal ProviderUserStorePaths(string localApplicationDataDirectory)
    {
        RootDirectory = PlayerDataStorage.BuildRootDirectory(
            localApplicationDataDirectory);
        ConfigurationDirectory = BelowRoot(
            RootDirectory,
            "Config",
            "providers-v1");
        ConfigurationGenerationsDirectory = BelowRoot(
            RootDirectory,
            "Config",
            "providers-v1",
            "generations");
        ConfigurationCurrentManifestPath = BelowRoot(
            RootDirectory,
            "Config",
            "providers-v1",
            "current-v1.json");
        CredentialDirectory = BelowRoot(
            RootDirectory,
            "Secrets",
            "provider-credentials-v1");
        CredentialBlobDirectory = BelowRoot(
            RootDirectory,
            "Secrets",
            "provider-credentials-v1",
            "blobs");
        CredentialReferenceDirectory = BelowRoot(
            RootDirectory,
            "Secrets",
            "provider-credentials-v1",
            "refs");
    }

    internal string CredentialManifestPath(CredentialReferenceId referenceId)
    {
        if (referenceId == null)
            throw new ArgumentNullException(nameof(referenceId));
        return BelowRoot(
            RootDirectory,
            "Secrets",
            "provider-credentials-v1",
            "refs",
            ReferenceFileStem(referenceId) + ".json");
    }

    internal static string ReferenceFileStem(CredentialReferenceId referenceId)
        => ProviderRegistryCanonicalHash.Of(
                "D031/STAGE-C/CREDENTIAL-PATH/1",
                referenceId.Value)
            .Substring(0, 32)
            .ToLowerInvariant();

    private static string BelowRoot(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(
            segments.Aggregate(fullRoot, Path.Combine));
        var prefix = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provider storage path escaped its update-safe root.");
        return candidate;
    }
}

internal sealed class ProviderStoredInstance
{
    internal string InstanceId { get; }
    internal string DefinitionId { get; }
    internal ProviderKind Kind { get; }
    internal ProviderUiGroup UiGroup { get; }
    internal DeploymentOwnership DeploymentOwnership { get; }
    internal ConnectionRouteLocality ConnectionRouteLocality { get; }
    internal EndpointExposureState ExposureState { get; }
    internal string BaseUri { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal string? CredentialReferenceId { get; }
    internal ProviderTrustClass TrustClass { get; }
    internal bool Enabled { get; }
    internal long ConfigurationGeneration { get; }
    internal string Fingerprint { get; }

    internal ProviderStoredInstance(ProviderEndpointInstance source)
        : this(
            source?.InstanceId.Value ?? throw new ArgumentNullException(nameof(source)),
            source.DefinitionId.Value,
            source.Kind,
            source.UiGroup,
            source.DeploymentOwnership,
            source.ConnectionRouteLocality,
            source.ExposureState,
            source.BaseUri.CanonicalValue,
            source.Protocol,
            source.ProtocolProfileVersion,
            source.AuthenticationScheme,
            source.CredentialReference?.ReferenceId.Value,
            source.TrustClass,
            source.Enabled,
            source.ConfigurationGeneration)
    {
    }

    internal ProviderStoredInstance(
        string instanceId,
        string definitionId,
        ProviderKind kind,
        ProviderUiGroup uiGroup,
        DeploymentOwnership deploymentOwnership,
        ConnectionRouteLocality connectionRouteLocality,
        EndpointExposureState exposureState,
        string baseUri,
        ProviderProtocolKind protocol,
        int protocolProfileVersion,
        ProviderAuthenticationScheme authenticationScheme,
        string? credentialReferenceId,
        ProviderTrustClass trustClass,
        bool enabled,
        long configurationGeneration)
    {
        InstanceId = Required(instanceId, nameof(instanceId));
        DefinitionId = Required(definitionId, nameof(definitionId));
        Kind = Defined(kind, nameof(kind));
        UiGroup = Defined(uiGroup, nameof(uiGroup));
        DeploymentOwnership = Defined(deploymentOwnership, nameof(deploymentOwnership));
        ConnectionRouteLocality = Defined(connectionRouteLocality, nameof(connectionRouteLocality));
        ExposureState = Defined(exposureState, nameof(exposureState));
        var endpoint = ProviderEndpointUri.TryCreate(baseUri);
        if (!endpoint.Accepted || endpoint.Endpoint == null)
            throw new ArgumentException("Stored endpoint is not admitted.", nameof(baseUri));
        BaseUri = endpoint.Endpoint.CanonicalValue;
        Protocol = Defined(protocol, nameof(protocol));
        if (protocolProfileVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(protocolProfileVersion));
        ProtocolProfileVersion = protocolProfileVersion;
        AuthenticationScheme = Defined(authenticationScheme, nameof(authenticationScheme));
        CredentialReferenceId = string.IsNullOrWhiteSpace(credentialReferenceId)
            ? null
            : Required(credentialReferenceId, nameof(credentialReferenceId));
        if ((AuthenticationScheme == ProviderAuthenticationScheme.None)
            != (CredentialReferenceId == null))
        {
            throw new ArgumentException("Credential reference presence must match authentication.");
        }
        TrustClass = Defined(trustClass, nameof(trustClass));
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        Enabled = enabled;
        ConfigurationGeneration = configurationGeneration;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/STORED-INSTANCE/1",
            InstanceId,
            DefinitionId,
            ProviderRegistryCanonicalHash.Number((int)Kind),
            ProviderRegistryCanonicalHash.Number((int)UiGroup),
            ProviderRegistryCanonicalHash.Number((int)DeploymentOwnership),
            ProviderRegistryCanonicalHash.Number((int)ConnectionRouteLocality),
            ProviderRegistryCanonicalHash.Number((int)ExposureState),
            BaseUri,
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            ProviderRegistryCanonicalHash.Number(ProtocolProfileVersion),
            ProviderRegistryCanonicalHash.Number((int)AuthenticationScheme),
            CredentialReferenceId ?? string.Empty,
            ProviderRegistryCanonicalHash.Number((int)TrustClass),
            enabled ? "1" : "0",
            ProviderRegistryCanonicalHash.Number(ConfigurationGeneration));
    }

    internal ProviderEndpointInstance ToRuntimeInstance()
    {
        var instanceId = new ProviderEndpointInstanceId(InstanceId);
        var endpoint = ProviderEndpointUri.TryCreate(BaseUri);
        if (!endpoint.Accepted || endpoint.Endpoint == null)
            throw new InvalidDataException("Stored endpoint no longer satisfies admission.");
        CredentialReference? credential = CredentialReferenceId == null
            ? null
            : new CredentialReference(
                new CredentialReferenceId(CredentialReferenceId),
                instanceId,
                AuthenticationScheme);
        return new ProviderEndpointInstance(
            instanceId,
            new ProviderDefinitionId(DefinitionId),
            Kind,
            UiGroup,
            DeploymentOwnership,
            ConnectionRouteLocality,
            ExposureState,
            endpoint.Endpoint,
            Protocol,
            ProtocolProfileVersion,
            AuthenticationScheme,
            credential,
            TrustClass,
            Enabled,
            ConfigurationGeneration);
    }

    private static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            throw new ArgumentException("A bounded value is required.", name);
        return value.Trim();
    }

    private static T Defined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value) || Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

internal sealed class ProviderConfiguredInstanceSnapshot
{
    internal const int MaximumInstanceCount = 64;
    internal const int SchemaVersion = 1;

    internal long StoreGeneration { get; }
    internal IReadOnlyList<ProviderStoredInstance> Instances { get; }
    internal string Fingerprint { get; }

    internal ProviderConfiguredInstanceSnapshot(
        long storeGeneration,
        IEnumerable<ProviderStoredInstance> instances)
    {
        if (storeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(storeGeneration));
        if (instances == null)
            throw new ArgumentNullException(nameof(instances));
        var ordered = instances.OrderBy(value => value.InstanceId, StringComparer.Ordinal).ToArray();
        if (ordered.Length > MaximumInstanceCount)
            throw new ArgumentOutOfRangeException(nameof(instances));
        if (ordered.Select(value => value.InstanceId).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Configured Provider instance IDs must be unique.", nameof(instances));
        StoreGeneration = storeGeneration;
        Instances = new ReadOnlyCollection<ProviderStoredInstance>(ordered);
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/CONFIGURED-SNAPSHOT/1",
            ProviderRegistryCanonicalHash.Number(storeGeneration),
            string.Join("|", ordered.Select(value => value.Fingerprint)));
    }
}

internal sealed class ProviderUserStoreAuthorization
{
    private readonly ProviderUserStoreHost _owner;
    internal string AuthorizationId { get; }
    internal string OperationFingerprint { get; }
    internal DateTimeOffset ExpiresAt { get; }

    internal ProviderUserStoreAuthorization(
        ProviderUserStoreHost owner,
        string authorizationId,
        string operationFingerprint,
        DateTimeOffset expiresAt)
    {
        _owner = owner;
        AuthorizationId = authorizationId;
        OperationFingerprint = operationFingerprint;
        ExpiresAt = expiresAt;
    }

    internal ProviderUserStoreAdmission TryConsume(string operationFingerprint, DateTimeOffset now)
        => _owner.TryConsume(this, operationFingerprint, now);
}

internal sealed class ProviderUserStoreHost
{
    internal const int MaximumAuthorizationsPerProcess = 4;
    internal static TimeSpan AuthorizationLifetime { get; } = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly HashSet<string> _transactions = new(StringComparer.Ordinal);
    private string? _activeAuthorizationId;
    private bool _activeConsumed;

    internal ProviderUserStoreMode Mode { get; private set; } = ProviderUserStoreMode.Off;
    internal int AuthorizationCount { get; private set; }

    internal void ArmForProcess()
    {
        lock (_gate)
        {
            if (Mode == ProviderUserStoreMode.Off)
                Mode = ProviderUserStoreMode.Armed;
        }
    }

    internal void Disable()
    {
        lock (_gate)
        {
            Mode = ProviderUserStoreMode.Off;
            _activeAuthorizationId = null;
            _activeConsumed = false;
        }
    }

    internal (ProviderUserStoreAdmission Admission, ProviderUserStoreAuthorization? Authorization)
        TryAuthorize(string operationFingerprint, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(operationFingerprint))
            throw new ArgumentException("Operation fingerprint is required.", nameof(operationFingerprint));
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (Mode == ProviderUserStoreMode.Off)
                return (ProviderUserStoreAdmission.Off, null);
            if (Mode == ProviderUserStoreMode.Fault || Mode == ProviderUserStoreMode.Capacity)
                return (ProviderUserStoreAdmission.ProcessCapacityReached, null);
            if (_transactions.Contains(operationFingerprint))
                return (ProviderUserStoreAdmission.DuplicateTransaction, null);
            if (_activeAuthorizationId != null && !_activeConsumed)
                return (ProviderUserStoreAdmission.ConcurrentMutation, null);
            if (AuthorizationCount >= MaximumAuthorizationsPerProcess)
            {
                Mode = ProviderUserStoreMode.Capacity;
                return (ProviderUserStoreAdmission.ProcessCapacityReached, null);
            }

            var id = Guid.NewGuid().ToString("N");
            _activeAuthorizationId = id;
            _activeConsumed = false;
            _transactions.Add(operationFingerprint);
            AuthorizationCount++;
            return (
                ProviderUserStoreAdmission.Accepted,
                new ProviderUserStoreAuthorization(
                    this,
                    id,
                    operationFingerprint,
                    now + AuthorizationLifetime));
        }
    }

    internal ProviderUserStoreAdmission TryConsume(
        ProviderUserStoreAuthorization authorization,
        string operationFingerprint,
        DateTimeOffset now)
    {
        now = DecisionCoreContractGuard.Timestamp(now, nameof(now));
        lock (_gate)
        {
            if (Mode != ProviderUserStoreMode.Armed)
                return ProviderUserStoreAdmission.Off;
            if (!string.Equals(_activeAuthorizationId, authorization.AuthorizationId, StringComparison.Ordinal)
                || !string.Equals(authorization.OperationFingerprint, operationFingerprint, StringComparison.Ordinal))
            {
                return ProviderUserStoreAdmission.TicketMismatch;
            }
            if (_activeConsumed)
                return ProviderUserStoreAdmission.TicketConsumed;
            _activeConsumed = true;
            if (now >= authorization.ExpiresAt)
                return ProviderUserStoreAdmission.TicketExpired;
            return ProviderUserStoreAdmission.Accepted;
        }
    }
}

internal sealed class ProviderConfiguredInstanceStoreResult
{
    internal ProviderUserStoreAdmission Admission { get; }
    internal ProviderConfiguredInstanceSnapshot? Snapshot { get; }
    internal string? TransactionId { get; }

    internal ProviderConfiguredInstanceStoreResult(
        ProviderUserStoreAdmission admission,
        ProviderConfiguredInstanceSnapshot? snapshot = null,
        string? transactionId = null)
    {
        Admission = admission;
        Snapshot = snapshot;
        TransactionId = transactionId;
    }
}

internal sealed class ProviderConfiguredInstanceStore
{
    private const int MaximumPayloadBytes = 131072;
    private readonly ProviderUserStorePaths _paths;

    internal ProviderConfiguredInstanceStore(ProviderUserStorePaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    internal ProviderConfiguredInstanceStoreResult Load()
    {
        var current = ReadFromManifest(_paths.ConfigurationCurrentManifestPath);
        if (current.Admission == ProviderUserStoreAdmission.Accepted
            || current.Admission == ProviderUserStoreAdmission.Missing)
        {
            return current;
        }
        var previous = ReadFromManifest(_paths.ConfigurationCurrentManifestPath + ".previous");
        return previous.Admission == ProviderUserStoreAdmission.Accepted
            ? new ProviderConfiguredInstanceStoreResult(
                ProviderUserStoreAdmission.RecoveredPrevious,
                previous.Snapshot,
                previous.TransactionId)
            : current;
    }

    internal ProviderConfiguredInstanceStoreResult Save(
        ProviderConfiguredInstanceSnapshot snapshot,
        long expectedCurrentGeneration,
        ProviderUserStoreAuthorization authorization,
        DateTimeOffset now)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        var operation = SaveOperationFingerprint(snapshot, expectedCurrentGeneration);
        var ticket = authorization.TryConsume(operation, now);
        if (ticket != ProviderUserStoreAdmission.Accepted)
            return new ProviderConfiguredInstanceStoreResult(ticket);

        var current = Load();
        var effectiveGeneration = current.Snapshot?.StoreGeneration ?? 0;
        if (effectiveGeneration != expectedCurrentGeneration
            || snapshot.StoreGeneration != expectedCurrentGeneration + 1)
        {
            return new ProviderConfiguredInstanceStoreResult(
                ProviderUserStoreAdmission.GenerationConflict);
        }

        return Persist(snapshot, now);
    }

    internal ProviderConfiguredInstanceStoreResult Rollback(
        string expectedCurrentTransactionId,
        ProviderUserStoreAuthorization authorization,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(expectedCurrentTransactionId))
            throw new ArgumentException("Current transaction identity is required.", nameof(expectedCurrentTransactionId));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        var current = ReadFromManifest(_paths.ConfigurationCurrentManifestPath);
        var previous = ReadFromManifest(_paths.ConfigurationCurrentManifestPath + ".previous");
        if (current.Admission != ProviderUserStoreAdmission.Accepted
            || current.Snapshot == null
            || previous.Admission != ProviderUserStoreAdmission.Accepted
            || previous.Snapshot == null)
        {
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
        }
        var operation = RollbackOperationFingerprint(
            expectedCurrentTransactionId,
            current.Snapshot,
            previous.Snapshot);
        var ticket = authorization.TryConsume(operation, now);
        if (ticket != ProviderUserStoreAdmission.Accepted)
            return new ProviderConfiguredInstanceStoreResult(ticket);
        if (!string.Equals(current.TransactionId, expectedCurrentTransactionId, StringComparison.Ordinal))
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.TicketMismatch);
        var restored = new ProviderConfiguredInstanceSnapshot(
            current.Snapshot.StoreGeneration + 1,
            previous.Snapshot.Instances);
        return Persist(restored, now);
    }

    internal static string RollbackOperationFingerprint(
        string expectedCurrentTransactionId,
        ProviderConfiguredInstanceSnapshot current,
        ProviderConfiguredInstanceSnapshot previous)
        => ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/ROLLBACK-CONFIG/1",
            expectedCurrentTransactionId,
            current.Fingerprint,
            previous.Fingerprint);

    private ProviderConfiguredInstanceStoreResult Persist(
        ProviderConfiguredInstanceSnapshot snapshot,
        DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(_paths.ConfigurationGenerationsDirectory);
            var payload = SerializeSnapshot(snapshot);
            if (payload.Length > MaximumPayloadBytes)
                return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
            var payloadHash = Sha256(payload);
            var payloadFile = "provider-instances-g"
                + snapshot.StoreGeneration.ToString(CultureInfo.InvariantCulture)
                + "-"
                + payloadHash.Substring(0, 16).ToLowerInvariant()
                + ".json";
            var payloadPath = Path.Combine(_paths.ConfigurationGenerationsDirectory, payloadFile);
            WriteImmutable(payloadPath, payload);

            var transactionId = Guid.NewGuid().ToString("N");
            var manifest = new ConfigurationManifestDto
            {
                Schema = 1,
                TransactionId = transactionId,
                StoreGeneration = snapshot.StoreGeneration,
                PayloadFile = payloadFile,
                PayloadSha256 = payloadHash,
                SnapshotFingerprint = snapshot.Fingerprint,
                CreatedUtcTicks = now.ToUniversalTime().Ticks
            };
            AtomicWrite(
                _paths.ConfigurationCurrentManifestPath,
                JsonSerializer.SerializeToUtf8Bytes(manifest));
            return new ProviderConfiguredInstanceStoreResult(
                ProviderUserStoreAdmission.Accepted,
                snapshot,
                transactionId);
        }
        catch (IOException)
        {
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.IoFailure);
        }
    }

    internal static string SaveOperationFingerprint(
        ProviderConfiguredInstanceSnapshot snapshot,
        long expectedCurrentGeneration)
        => ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/SAVE-CONFIG/1",
            snapshot.Fingerprint,
            ProviderRegistryCanonicalHash.Number(expectedCurrentGeneration));

    private ProviderConfiguredInstanceStoreResult ReadFromManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Missing);
        try
        {
            var manifestBytes = ReadBounded(manifestPath, 16384);
            var manifest = JsonSerializer.Deserialize<ConfigurationManifestDto>(manifestBytes);
            if (manifest == null
                || manifest.Schema != 1
                || manifest.StoreGeneration <= 0
                || string.IsNullOrWhiteSpace(manifest.PayloadFile)
                || Path.GetFileName(manifest.PayloadFile) != manifest.PayloadFile)
            {
                return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
            }
            var payloadPath = Path.Combine(
                _paths.ConfigurationGenerationsDirectory,
                manifest.PayloadFile);
            var payload = ReadBounded(payloadPath, MaximumPayloadBytes);
            if (!string.Equals(Sha256(payload), manifest.PayloadSha256, StringComparison.Ordinal))
                return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
            var snapshot = DeserializeSnapshot(payload);
            if (snapshot.StoreGeneration != manifest.StoreGeneration
                || !string.Equals(snapshot.Fingerprint, manifest.SnapshotFingerprint, StringComparison.Ordinal))
            {
                return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
            }
            return new ProviderConfiguredInstanceStoreResult(
                ProviderUserStoreAdmission.Accepted,
                snapshot,
                manifest.TransactionId);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is JsonException
            || exception is ArgumentException
            || exception is InvalidDataException)
        {
            return new ProviderConfiguredInstanceStoreResult(ProviderUserStoreAdmission.Corrupt);
        }
    }

    private static byte[] SerializeSnapshot(ProviderConfiguredInstanceSnapshot snapshot)
        => JsonSerializer.SerializeToUtf8Bytes(new ConfigurationPayloadDto
        {
            Schema = ProviderConfiguredInstanceSnapshot.SchemaVersion,
            StoreGeneration = snapshot.StoreGeneration,
            SnapshotFingerprint = snapshot.Fingerprint,
            Instances = snapshot.Instances.Select(value => new StoredInstanceDto
            {
                InstanceId = value.InstanceId,
                DefinitionId = value.DefinitionId,
                Kind = (int)value.Kind,
                UiGroup = (int)value.UiGroup,
                DeploymentOwnership = (int)value.DeploymentOwnership,
                ConnectionRouteLocality = (int)value.ConnectionRouteLocality,
                ExposureState = (int)value.ExposureState,
                BaseUri = value.BaseUri,
                Protocol = (int)value.Protocol,
                ProtocolProfileVersion = value.ProtocolProfileVersion,
                AuthenticationScheme = (int)value.AuthenticationScheme,
                CredentialReferenceId = value.CredentialReferenceId,
                TrustClass = (int)value.TrustClass,
                Enabled = value.Enabled,
                ConfigurationGeneration = value.ConfigurationGeneration
            }).ToArray()
        });

    private static ProviderConfiguredInstanceSnapshot DeserializeSnapshot(byte[] payload)
    {
        var dto = JsonSerializer.Deserialize<ConfigurationPayloadDto>(payload)
            ?? throw new InvalidDataException("Configuration payload is empty.");
        if (dto.Schema != ProviderConfiguredInstanceSnapshot.SchemaVersion
            || dto.Instances == null
            || dto.Instances.Length > ProviderConfiguredInstanceSnapshot.MaximumInstanceCount)
        {
            throw new InvalidDataException("Configuration payload schema or capacity is invalid.");
        }
        var instances = dto.Instances.Select(value => new ProviderStoredInstance(
            value.InstanceId ?? string.Empty,
            value.DefinitionId ?? string.Empty,
            (ProviderKind)value.Kind,
            (ProviderUiGroup)value.UiGroup,
            (DeploymentOwnership)value.DeploymentOwnership,
            (ConnectionRouteLocality)value.ConnectionRouteLocality,
            (EndpointExposureState)value.ExposureState,
            value.BaseUri ?? string.Empty,
            (ProviderProtocolKind)value.Protocol,
            value.ProtocolProfileVersion,
            (ProviderAuthenticationScheme)value.AuthenticationScheme,
            value.CredentialReferenceId,
            (ProviderTrustClass)value.TrustClass,
            value.Enabled,
            value.ConfigurationGeneration));
        var snapshot = new ProviderConfiguredInstanceSnapshot(dto.StoreGeneration, instances);
        if (!string.Equals(snapshot.Fingerprint, dto.SnapshotFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Configuration snapshot fingerprint is invalid.");
        return snapshot;
    }

    internal static byte[] ReadBounded(string path, int maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 1 || info.Length > maximumBytes)
            throw new InvalidDataException("Provider store file is missing or outside its bound.");
        return File.ReadAllBytes(path);
    }

    internal static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    internal static void WriteImmutable(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
        if (File.Exists(path))
        {
            if (!CryptographicOperations.FixedTimeEquals(File.ReadAllBytes(path), bytes))
                throw new IOException("Immutable Provider store payload collision.");
            return;
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    internal static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path))
            File.Replace(temporary, path, path + ".previous", ignoreMetadataErrors: true);
        else
            File.Move(temporary, path);
    }

    private sealed class ConfigurationManifestDto
    {
        public int Schema { get; set; }
        public string? TransactionId { get; set; }
        public long StoreGeneration { get; set; }
        public string? PayloadFile { get; set; }
        public string? PayloadSha256 { get; set; }
        public string? SnapshotFingerprint { get; set; }
        public long CreatedUtcTicks { get; set; }
    }

    private sealed class ConfigurationPayloadDto
    {
        public int Schema { get; set; }
        public long StoreGeneration { get; set; }
        public string? SnapshotFingerprint { get; set; }
        public StoredInstanceDto[]? Instances { get; set; }
    }

    private sealed class StoredInstanceDto
    {
        public string? InstanceId { get; set; }
        public string? DefinitionId { get; set; }
        public int Kind { get; set; }
        public int UiGroup { get; set; }
        public int DeploymentOwnership { get; set; }
        public int ConnectionRouteLocality { get; set; }
        public int ExposureState { get; set; }
        public string? BaseUri { get; set; }
        public int Protocol { get; set; }
        public int ProtocolProfileVersion { get; set; }
        public int AuthenticationScheme { get; set; }
        public string? CredentialReferenceId { get; set; }
        public int TrustClass { get; set; }
        public bool Enabled { get; set; }
        public long ConfigurationGeneration { get; set; }
    }
}

internal sealed class ProviderCredentialStoreResult
{
    internal ProviderUserStoreAdmission Admission { get; }
    internal ProviderCredentialHealth Health { get; }
    internal bool SourceDeleted { get; }
    internal string? TransactionId { get; }

    internal ProviderCredentialStoreResult(
        ProviderUserStoreAdmission admission,
        ProviderCredentialHealth health,
        bool sourceDeleted = false,
        string? transactionId = null)
    {
        Admission = admission;
        Health = health;
        SourceDeleted = sourceDeleted;
        TransactionId = transactionId;
    }
}

internal sealed class ProviderProtectedCredentialStore
{
    private const int MaximumCipherBytes = 32768;
    private readonly ProviderUserStorePaths _paths;

    internal ProviderProtectedCredentialStore(ProviderUserStorePaths paths)
        => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    internal ProviderCredentialStoreResult GetHealth(ProviderEndpointInstance instance)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (instance.CredentialReference == null)
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.Missing, ProviderCredentialHealth.Missing);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.UnsupportedPlatform, ProviderCredentialHealth.UnsupportedPlatform);
        var read = TryReadManifest(instance.CredentialReference.ReferenceId);
        if (read.Admission == ProviderUserStoreAdmission.Missing)
            return new ProviderCredentialStoreResult(read.Admission, ProviderCredentialHealth.Missing);
        if (read.Manifest == null)
            return new ProviderCredentialStoreResult(read.Admission, ProviderCredentialHealth.Corrupt);
        if (!ManifestMatches(read.Manifest, instance))
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.BindingMismatch, ProviderCredentialHealth.StaleBinding);
        byte[]? cipher = null;
        byte[]? plain = null;
        try
        {
            cipher = ProviderConfiguredInstanceStore.ReadBounded(
                Path.Combine(_paths.CredentialBlobDirectory, read.Manifest.BlobFile ?? string.Empty),
                MaximumCipherBytes);
            var entropy = Entropy(instance);
            try
            {
                plain = WindowsCurrentUserDataProtection.Unprotect(cipher, entropy);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
            return new ProviderCredentialStoreResult(read.Admission, ProviderCredentialHealth.ProtectedHealthy);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is CryptographicException)
        {
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.Corrupt, ProviderCredentialHealth.Corrupt);
        }
        finally
        {
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
            if (plain != null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    internal ProviderCredentialStoreResult ImportCopyAndVerify(
        ProviderEndpointInstance instance,
        string importTransactionId,
        char[] legacySecret,
        ProviderUserStoreAuthorization authorization,
        DateTimeOffset now)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(importTransactionId)
            || importTransactionId.Length > 128)
        {
            throw new ArgumentException(
                "A bounded import transaction identity is required.",
                nameof(importTransactionId));
        }
        if (legacySecret == null)
            throw new ArgumentNullException(nameof(legacySecret));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (instance.CredentialReference == null)
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.BindingMismatch, ProviderCredentialHealth.StaleBinding);
        var operation = ImportOperationFingerprint(instance, importTransactionId);
        var ticket = authorization.TryConsume(operation, now);
        if (ticket != ProviderUserStoreAdmission.Accepted)
            return new ProviderCredentialStoreResult(ticket, ProviderCredentialHealth.Missing);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.UnsupportedPlatform, ProviderCredentialHealth.UnsupportedPlatform);

        var validationRequest = new ProviderCredentialResolutionRequest(
            instance,
            now,
            now + TimeSpan.FromSeconds(1));
        var validation = CredentialLease.TryCreateForContractReplay(
            validationRequest,
            instance,
            legacySecret);
        if (validation.Admission
            != ProviderCredentialLeaseAdmission.AcceptedForContractReplay)
        {
            return new ProviderCredentialStoreResult(
                ProviderUserStoreAdmission.ProtectionFailed,
                ProviderCredentialHealth.Corrupt);
        }
        validation.Lease!.Dispose();

        byte[]? plain = null;
        byte[]? cipher = null;
        byte[]? verified = null;
        try
        {
            plain = Encoding.UTF8.GetBytes(legacySecret);
            if (plain.Length == 0 || legacySecret.Length > CredentialLease.MaximumSecretLength)
                return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.ProtectionFailed, ProviderCredentialHealth.Corrupt);
            var entropy = Entropy(instance);
            try
            {
                cipher = WindowsCurrentUserDataProtection.Protect(plain, entropy);
                verified = WindowsCurrentUserDataProtection.Unprotect(cipher, entropy);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
            if (!CryptographicOperations.FixedTimeEquals(plain, verified))
                return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.ProtectionFailed, ProviderCredentialHealth.Corrupt);

            Directory.CreateDirectory(_paths.CredentialBlobDirectory);
            Directory.CreateDirectory(_paths.CredentialReferenceDirectory);
            var cipherHash = ProviderConfiguredInstanceStore.Sha256(cipher);
            var referenceStem = ProviderUserStorePaths.ReferenceFileStem(instance.CredentialReference.ReferenceId);
            var blobFile = referenceStem + "-" + cipherHash.Substring(0, 16).ToLowerInvariant() + ".dpapi";
            var blobPath = Path.Combine(_paths.CredentialBlobDirectory, blobFile);
            ProviderConfiguredInstanceStore.WriteImmutable(blobPath, cipher);
            var transactionId = Guid.NewGuid().ToString("N");
            var manifest = new CredentialManifestDto
            {
                Schema = 1,
                TransactionId = transactionId,
                InstanceId = instance.InstanceId.Value,
                ReferenceId = instance.CredentialReference.ReferenceId.Value,
                AuthenticationScheme = (int)instance.AuthenticationScheme,
                EndpointFingerprint = instance.EndpointFingerprint,
                ConfigurationGeneration = instance.ConfigurationGeneration,
                BlobFile = blobFile,
                CipherSha256 = cipherHash,
                CreatedUtcTicks = now.ToUniversalTime().Ticks
            };
            ProviderConfiguredInstanceStore.AtomicWrite(
                _paths.CredentialManifestPath(instance.CredentialReference.ReferenceId),
                JsonSerializer.SerializeToUtf8Bytes(manifest));
            return new ProviderCredentialStoreResult(
                ProviderUserStoreAdmission.Accepted,
                ProviderCredentialHealth.ProtectedHealthy,
                sourceDeleted: false,
                transactionId);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is CryptographicException)
        {
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.ProtectionFailed, ProviderCredentialHealth.Corrupt);
        }
        finally
        {
            if (plain != null)
                CryptographicOperations.ZeroMemory(plain);
            if (verified != null)
                CryptographicOperations.ZeroMemory(verified);
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
        }
    }

    internal ProviderCredentialStoreResult Rollback(
        ProviderEndpointInstance currentInstance,
        string expectedCurrentTransactionId,
        ProviderUserStoreAuthorization authorization,
        DateTimeOffset now)
    {
        if (currentInstance == null)
            throw new ArgumentNullException(nameof(currentInstance));
        if (currentInstance.CredentialReference == null)
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.BindingMismatch, ProviderCredentialHealth.StaleBinding);
        if (string.IsNullOrWhiteSpace(expectedCurrentTransactionId))
            throw new ArgumentException("Current credential transaction identity is required.", nameof(expectedCurrentTransactionId));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        var currentPath = _paths.CredentialManifestPath(currentInstance.CredentialReference.ReferenceId);
        var current = TryReadManifestAt(currentPath);
        var previous = TryReadManifestAt(currentPath + ".previous");
        if (current.Manifest == null || previous.Manifest == null)
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.Corrupt, ProviderCredentialHealth.Corrupt);
        var operation = RollbackOperationFingerprint(
            currentInstance,
            expectedCurrentTransactionId,
            current.Manifest.CipherSha256 ?? string.Empty,
            previous.Manifest.CipherSha256 ?? string.Empty);
        var ticket = authorization.TryConsume(operation, now);
        if (ticket != ProviderUserStoreAdmission.Accepted)
            return new ProviderCredentialStoreResult(ticket, ProviderCredentialHealth.Missing);
        if (!string.Equals(current.Manifest.TransactionId, expectedCurrentTransactionId, StringComparison.Ordinal)
            || !ManifestMatches(previous.Manifest, currentInstance))
        {
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.BindingMismatch, ProviderCredentialHealth.StaleBinding);
        }
        try
        {
            previous.Manifest.TransactionId = Guid.NewGuid().ToString("N");
            previous.Manifest.CreatedUtcTicks = now.ToUniversalTime().Ticks;
            ProviderConfiguredInstanceStore.AtomicWrite(
                currentPath,
                JsonSerializer.SerializeToUtf8Bytes(previous.Manifest));
            return new ProviderCredentialStoreResult(
                ProviderUserStoreAdmission.Accepted,
                ProviderCredentialHealth.ProtectedHealthy,
                sourceDeleted: false,
                previous.Manifest.TransactionId);
        }
        catch (Exception exception) when (
            exception is IOException || exception is UnauthorizedAccessException)
        {
            return new ProviderCredentialStoreResult(ProviderUserStoreAdmission.IoFailure, ProviderCredentialHealth.Corrupt);
        }
    }

    internal ProviderCredentialLeaseResult Resolve(
        ProviderCredentialResolutionRequest request,
        ProviderEndpointInstance currentInstance,
        ProviderCredentialAccessHost accessHost)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (currentInstance == null)
            throw new ArgumentNullException(nameof(currentInstance));
        if (accessHost == null)
            throw new ArgumentNullException(nameof(accessHost));
        if (!accessHost.TryConsumeResolution())
            return ProviderCredentialLeaseResult.Reject(ProviderCredentialLeaseAdmission.ResolverOff);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || currentInstance.CredentialReference == null)
        {
            return ProviderCredentialLeaseResult.Reject(ProviderCredentialLeaseAdmission.SecretMaterialUnavailable);
        }
        var read = TryReadManifest(currentInstance.CredentialReference.ReferenceId);
        if (read.Manifest == null || !ManifestMatches(read.Manifest, currentInstance))
            return ProviderCredentialLeaseResult.Reject(ProviderCredentialLeaseAdmission.ReferenceMismatch);

        byte[]? cipher = null;
        byte[]? plain = null;
        char[]? characters = null;
        try
        {
            var blobPath = Path.Combine(_paths.CredentialBlobDirectory, read.Manifest.BlobFile ?? string.Empty);
            cipher = ProviderConfiguredInstanceStore.ReadBounded(blobPath, MaximumCipherBytes);
            if (!string.Equals(ProviderConfiguredInstanceStore.Sha256(cipher), read.Manifest.CipherSha256, StringComparison.Ordinal))
                return ProviderCredentialLeaseResult.Reject(ProviderCredentialLeaseAdmission.SecretMaterialUnavailable);
            var entropy = Entropy(currentInstance);
            try
            {
                plain = WindowsCurrentUserDataProtection.Unprotect(cipher, entropy);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entropy);
            }
            characters = Encoding.UTF8.GetChars(plain);
            return CredentialLease.TryCreateForContractReplay(request, currentInstance, characters);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is CryptographicException
            || exception is DecoderFallbackException)
        {
            return ProviderCredentialLeaseResult.Reject(ProviderCredentialLeaseAdmission.SecretMaterialUnavailable);
        }
        finally
        {
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
            if (plain != null)
                CryptographicOperations.ZeroMemory(plain);
            if (characters != null)
                Array.Clear(characters, 0, characters.Length);
        }
    }

    internal static string ImportOperationFingerprint(
        ProviderEndpointInstance instance,
        string importTransactionId)
        => ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/IMPORT-CREDENTIAL/1",
            instance.InstanceId.Value,
            instance.CredentialReference?.ReferenceId.Value ?? string.Empty,
            instance.EndpointFingerprint,
            ProviderRegistryCanonicalHash.Number(instance.ConfigurationGeneration),
            importTransactionId);

    internal static string RollbackOperationFingerprint(
        ProviderEndpointInstance instance,
        string expectedCurrentTransactionId,
        string currentCipherHash,
        string previousCipherHash)
        => ProviderRegistryCanonicalHash.Of(
            "D031/STAGE-C/ROLLBACK-CREDENTIAL/1",
            instance.InstanceId.Value,
            instance.CredentialReference?.ReferenceId.Value ?? string.Empty,
            expectedCurrentTransactionId,
            currentCipherHash,
            previousCipherHash);

    private (ProviderUserStoreAdmission Admission, CredentialManifestDto? Manifest)
        TryReadManifest(CredentialReferenceId referenceId)
    {
        var path = _paths.CredentialManifestPath(referenceId);
        var current = TryReadManifestAt(path);
        if (current.Admission == ProviderUserStoreAdmission.Accepted
            || current.Admission == ProviderUserStoreAdmission.Missing)
        {
            return current;
        }
        var previous = TryReadManifestAt(path + ".previous");
        return previous.Admission == ProviderUserStoreAdmission.Accepted
            ? (ProviderUserStoreAdmission.RecoveredPrevious, previous.Manifest)
            : current;
    }

    private (ProviderUserStoreAdmission Admission, CredentialManifestDto? Manifest)
        TryReadManifestAt(string path)
    {
        if (!File.Exists(path))
            return (ProviderUserStoreAdmission.Missing, null);
        try
        {
            var bytes = ProviderConfiguredInstanceStore.ReadBounded(path, 16384);
            var manifest = JsonSerializer.Deserialize<CredentialManifestDto>(bytes);
            if (manifest == null
                || manifest.Schema != 1
                || string.IsNullOrWhiteSpace(manifest.BlobFile)
                || Path.GetFileName(manifest.BlobFile) != manifest.BlobFile)
            {
                return (ProviderUserStoreAdmission.Corrupt, null);
            }
            var blobPath = Path.Combine(_paths.CredentialBlobDirectory, manifest.BlobFile);
            var cipher = ProviderConfiguredInstanceStore.ReadBounded(blobPath, MaximumCipherBytes);
            return string.Equals(ProviderConfiguredInstanceStore.Sha256(cipher), manifest.CipherSha256, StringComparison.Ordinal)
                ? (ProviderUserStoreAdmission.Accepted, manifest)
                : (ProviderUserStoreAdmission.Corrupt, null);
        }
        catch (Exception exception) when (
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is JsonException
            || exception is InvalidDataException)
        {
            return (ProviderUserStoreAdmission.Corrupt, null);
        }
    }

    private static bool ManifestMatches(CredentialManifestDto manifest, ProviderEndpointInstance instance)
        => instance.CredentialReference != null
            && string.Equals(manifest.InstanceId, instance.InstanceId.Value, StringComparison.Ordinal)
            && string.Equals(manifest.ReferenceId, instance.CredentialReference.ReferenceId.Value, StringComparison.Ordinal)
            && manifest.AuthenticationScheme == (int)instance.AuthenticationScheme
            && string.Equals(manifest.EndpointFingerprint, instance.EndpointFingerprint, StringComparison.Ordinal)
            && manifest.ConfigurationGeneration == instance.ConfigurationGeneration;

    private static byte[] Entropy(ProviderEndpointInstance instance)
        => Encoding.UTF8.GetBytes(
            "LilithAiMod/D031/ProviderCredential/v1\n"
            + instance.InstanceId.Value
            + "\n"
            + (instance.CredentialReference?.ReferenceId.Value ?? string.Empty));

    private sealed class CredentialManifestDto
    {
        public int Schema { get; set; }
        public string? TransactionId { get; set; }
        public string? InstanceId { get; set; }
        public string? ReferenceId { get; set; }
        public int AuthenticationScheme { get; set; }
        public string? EndpointFingerprint { get; set; }
        public long ConfigurationGeneration { get; set; }
        public string? BlobFile { get; set; }
        public string? CipherSha256 { get; set; }
        public long CreatedUtcTicks { get; set; }
    }
}

internal sealed class ProviderCredentialAccessHost
{
    internal const int MaximumResolutionsPerProcess = 4;
    private readonly object _gate = new();
    private bool _armed;
    private int _consumed;

    internal bool IsArmed
    {
        get { lock (_gate) return _armed; }
    }

    internal void ArmForProcess()
    {
        lock (_gate)
            _armed = true;
    }

    internal void Disable()
    {
        lock (_gate)
            _armed = false;
    }

    internal bool TryConsumeResolution()
    {
        lock (_gate)
        {
            if (!_armed || _consumed >= MaximumResolutionsPerProcess)
                return false;
            _consumed++;
            return true;
        }
    }
}

internal sealed class ProtectedProviderCredentialResolver :
    IProviderCredentialResolver
{
    private readonly ProviderProtectedCredentialStore _store;
    private readonly ProviderCredentialAccessHost _accessHost;

    internal ProtectedProviderCredentialResolver(
        ProviderProtectedCredentialStore store,
        ProviderCredentialAccessHost accessHost)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accessHost = accessHost
            ?? throw new ArgumentNullException(nameof(accessHost));
    }

    public ProviderCredentialLeaseResult Resolve(
        ProviderCredentialResolutionRequest request,
        ProviderEndpointInstance currentInstance)
        => _store.Resolve(request, currentInstance, _accessHost);
}

internal static class WindowsCurrentUserDataProtection
{
    private const int CryptProtectUiForbidden = 0x1;

    internal static byte[] Protect(byte[] plain, byte[] entropy)
        => Transform(plain, entropy, protect: true);

    internal static byte[] Unprotect(byte[] cipher, byte[] entropy)
        => Transform(cipher, entropy, protect: false);

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Windows current-user protection is required.");
        if (input == null || input.Length == 0)
            throw new CryptographicException("Protected payload is empty.");
        var inputBlob = Allocate(input);
        var entropyBlob = Allocate(entropy);
        DataBlob output = default;
        try
        {
            var accepted = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out output);
            if (!accepted || output.Data == IntPtr.Zero || output.Length <= 0)
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, output.Length);
            return result;
        }
        finally
        {
            ClearAndFreeHGlobal(ref inputBlob);
            ClearAndFreeHGlobal(ref entropyBlob);
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void ClearAndFreeHGlobal(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Length;
        internal IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
