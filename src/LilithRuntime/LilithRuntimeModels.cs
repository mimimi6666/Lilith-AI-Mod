using System.Collections.Frozen;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

public enum LilithRuntimeState
{
    Stopped = 0,
    Starting = 1,
    Ready = 2,
    Stopping = 3,
    Faulted = 4
}

public enum LilithSoulProvisioningDisposition
{
    Seeded = 0,
    VerifiedExisting = 1,
    MigratedKnownVersion = 2
}

public sealed record LilithProfileProvisioningReceipt(
    string HermesHomePath,
    LilithSoulProvisioningDisposition SoulDisposition,
    string SoulSha256,
    string LearningConfigSha256,
    bool BundledSkillsOptOutPresent,
    string IdentityPackageId,
    int IdentityPackageVersion,
    string IdentityFingerprintSha256,
    string IdentityPackageManifestSha256,
    string HermesPersonalitySha256,
    string CanonicalLearningConfigSha256 = "",
    bool NativeConfigMutationReceiptAccepted = false);

public sealed record LilithRuntimeSnapshot(
    Guid GenerationId,
    LilithRuntimeState State,
    int HermesProcessId,
    int LoopbackPort,
    string HermesCommit,
    string HermesHomePath,
    string ProviderId,
    string ModelId,
    string ProviderInstanceId,
    long ProviderConfigurationGeneration,
    bool GatewayReady,
    bool JobObjectBound,
    LilithProfileProvisioningReceipt Profile);

public sealed record HermesNativeSurfaceProbeReceipt(
    Guid GenerationId,
    string SessionId,
    int CommandCount,
    int ModelProviderCount,
    bool SessionStatusReceived,
    bool SessionNativeSurfaceValid,
    int NativeToolCount,
    int NativeToolsetCount,
    int McpServerCount,
    int NativeCatalogToolsetCount,
    string NativeApprovalMode,
    bool NoPromptSubmitted,
    bool CredentialStatusReceived,
    bool ApiKeyProviderConfigured,
    bool CatalogSurfaceValid);

public sealed record HermesStopReceipt(
    Guid GenerationId,
    bool GatewayCloseAttempted,
    bool ProcessExited,
    bool ForcedTermination,
    bool ReadySentinelRemoved);

public sealed record LilithRuntimeSmokeReport(
    LilithRuntimeSnapshot Startup,
    HermesNativeSurfaceProbeReceipt Probe,
    HermesStopReceipt Shutdown);

public sealed record HermesGatewayEvent(
    string Type,
    string SessionId,
    JsonElement Payload,
    JsonElement Parameters);

internal sealed class LilithRuntimeApprovedModelCatalog
{
    private readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _models;

    internal string DefaultProviderId { get; }

    internal string DefaultModelId { get; }

    internal bool HasDefault =>
        DefaultProviderId.Length != 0 && DefaultModelId.Length != 0;

    internal IReadOnlyDictionary<string, IReadOnlySet<string>> Providers => _models;

    internal LilithRuntimeApprovedModelCatalog(
        string defaultProviderId,
        string defaultModelId,
        IEnumerable<(string ProviderId, IEnumerable<string> ModelIds)> providers)
    {
        DefaultProviderId = defaultProviderId ?? string.Empty;
        DefaultModelId = defaultModelId ?? string.Empty;
        var frozen = new Dictionary<string, IReadOnlySet<string>>(
            StringComparer.Ordinal);
        foreach (var provider in providers
            ?? throw new ArgumentNullException(nameof(providers)))
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderId)
                || frozen.ContainsKey(provider.ProviderId))
            {
                throw new ArgumentException(
                    "Approved provider identifiers must be unique.",
                    nameof(providers));
            }
            var models = provider.ModelIds.ToFrozenSet(StringComparer.Ordinal);
            if (models.Count == 0 || models.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Each approved provider requires at least one model.",
                    nameof(providers));
            }
            frozen.Add(provider.ProviderId, models);
        }
        if ((DefaultProviderId.Length == 0) != (DefaultModelId.Length == 0))
        {
            throw new ArgumentException(
                "The default provider and model must both be set or both be empty.",
                nameof(defaultModelId));
        }
        if (HasDefault && !Allows(DefaultProviderId, DefaultModelId, frozen))
        {
            throw new ArgumentException(
                "The default provider and model must be in the approved catalog.",
                nameof(defaultModelId));
        }
        _models = new System.Collections.ObjectModel
            .ReadOnlyDictionary<string, IReadOnlySet<string>>(frozen);
    }

    internal bool Allows(string providerId, string modelId)
        => Allows(providerId, modelId, _models);

    private static bool Allows(
        string providerId,
        string modelId,
        IReadOnlyDictionary<string, IReadOnlySet<string>> models)
        => models.TryGetValue(providerId, out var approved)
            && approved.Contains(modelId);
}

internal enum LilithRuntimeModelAuthorityState
{
    Unavailable = 0,
    Ready = 1,
    Quarantined = 2,
    Retired = 3
}

internal sealed record LilithRuntimeModelAuthoritySnapshot(
    Guid GenerationId,
    LilithRuntimeModelAuthorityState State,
    long Revision,
    long NativeConfigRevision,
    LilithRuntimeApprovedModelCatalog? Catalog,
    string ProviderId,
    string ModelId,
    LilithNativeModelMutationDiagnostic? NativeModelFailure = null)
{
    internal bool HasApprovedDefault =>
        State == LilithRuntimeModelAuthorityState.Ready
        && Catalog is not null
        && Catalog.Allows(ProviderId, ModelId);
}

internal sealed class LilithRuntimeModelAuthorityLease : IDisposable
{
    private LilithRuntimeModelAuthority? _owner;
    private readonly long _leaseId;

    internal LilithRuntimeModelAuthoritySnapshot Snapshot { get; }

    internal LilithRuntimeModelAuthorityLease(
        LilithRuntimeModelAuthority owner,
        long leaseId,
        LilithRuntimeModelAuthoritySnapshot snapshot)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (leaseId <= 0)
            throw new ArgumentOutOfRangeException(nameof(leaseId));
        _leaseId = leaseId;
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    }

    internal bool IsActiveFor(
        LilithRuntimeModelAuthority owner,
        long leaseId)
        => ReferenceEquals(Volatile.Read(ref _owner), owner)
            && _leaseId == leaseId;

    public void Dispose()
        => Interlocked.Exchange(ref _owner, null)?.ReleaseLease(_leaseId);
}

internal sealed class LilithRuntimeModelAuthority
{
    private readonly SemaphoreSlim _linearizationGate = new(1, 1);
    private readonly object _stateGate = new();
    private Guid _generationId;
    private LilithRuntimeModelAuthorityState _state =
        LilithRuntimeModelAuthorityState.Retired;
    private long _revision;
    private long _nativeConfigRevision;
    private LilithRuntimeApprovedModelCatalog? _catalog;
    private string _providerId = string.Empty;
    private string _modelId = string.Empty;
    private long _nextLeaseId;
    private long _activeLeaseId;
    private LilithNativeModelMutationDiagnostic? _nativeModelFailure;

    internal LilithRuntimeModelAuthoritySnapshot Snapshot()
    {
        lock (_stateGate)
        {
            return SnapshotLocked();
        }
    }

    internal async ValueTask<LilithRuntimeModelAuthorityLease> AcquireAsync(
        Guid generationId,
        CancellationToken cancellationToken)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("A model-authority generation is required.", nameof(generationId));
        await _linearizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long leaseId;
            LilithRuntimeModelAuthoritySnapshot snapshot;
            lock (_stateGate)
            {
                leaseId = checked(++_nextLeaseId);
                if (leaseId <= 0 || _activeLeaseId != 0)
                {
                    throw new InvalidOperationException(
                        "The model-authority lease state is invalid.");
                }
                _activeLeaseId = leaseId;
                snapshot = SnapshotLocked();
            }
            return new LilithRuntimeModelAuthorityLease(
                this,
                leaseId,
                snapshot);
        }
        catch
        {
            lock (_stateGate)
                _activeLeaseId = 0;
            _linearizationGate.Release();
            throw;
        }
    }

    internal void BeginGeneration(Guid generationId)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("A model-authority generation is required.", nameof(generationId));
        lock (_stateGate)
        {
            if (_activeLeaseId != 0)
            {
                throw new InvalidOperationException(
                    "A model-authority generation cannot begin while a lease is active.");
            }
            _generationId = generationId;
            _state = LilithRuntimeModelAuthorityState.Unavailable;
            _revision = 1;
            _nativeConfigRevision = 1;
            _catalog = null;
            _providerId = string.Empty;
            _modelId = string.Empty;
            _nativeModelFailure = null;
        }
    }

    internal bool PublishCatalog(
        Guid generationId,
        LilithRuntimeApprovedModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_stateGate)
        {
            if (_activeLeaseId != 0
                || _generationId != generationId
                || _state is LilithRuntimeModelAuthorityState.Quarantined
                    or LilithRuntimeModelAuthorityState.Retired)
            {
                return false;
            }
            _catalog = catalog;
            _providerId = catalog.HasDefault
                ? catalog.DefaultProviderId
                : string.Empty;
            _modelId = catalog.HasDefault
                ? catalog.DefaultModelId
                : string.Empty;
            _state = LilithRuntimeModelAuthorityState.Ready;
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal bool PublishCatalog(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId,
        LilithRuntimeApprovedModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        lock (_stateGate)
        {
            if (!IsActiveLeaseLocked(lease)
                || _generationId != generationId
                || _state is LilithRuntimeModelAuthorityState.Quarantined
                    or LilithRuntimeModelAuthorityState.Retired)
            {
                return false;
            }
            _catalog = catalog;
            _providerId = catalog.HasDefault
                ? catalog.DefaultProviderId
                : string.Empty;
            _modelId = catalog.HasDefault
                ? catalog.DefaultModelId
                : string.Empty;
            _state = LilithRuntimeModelAuthorityState.Ready;
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal bool ApplyAttestedDefault(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId,
        LilithRuntimeApprovedModelCatalog catalog,
        string providerId,
        string modelId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!catalog.HasDefault
            || !string.Equals(
                catalog.DefaultProviderId,
                providerId,
                StringComparison.Ordinal)
            || !string.Equals(
                catalog.DefaultModelId,
                modelId,
                StringComparison.Ordinal)
            || !catalog.Allows(providerId, modelId))
        {
            return false;
        }
        lock (_stateGate)
        {
            if (!IsActiveLeaseLocked(lease)
                || _generationId != generationId
                || _state != LilithRuntimeModelAuthorityState.Ready)
            {
                return false;
            }
            _catalog = catalog;
            _providerId = providerId;
            _modelId = modelId;
            _nativeConfigRevision = checked(_nativeConfigRevision + 1);
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal bool PublishNativeConfigMutation(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId)
    {
        lock (_stateGate)
        {
            if (!IsActiveLeaseLocked(lease)
                || _generationId != generationId
                || _state != LilithRuntimeModelAuthorityState.Ready)
            {
                return false;
            }
            _nativeConfigRevision = checked(_nativeConfigRevision + 1);
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal bool OwnsActiveLease(
        LilithRuntimeModelAuthorityLease? lease,
        Guid generationId)
    {
        lock (_stateGate)
        {
            return _generationId == generationId
                && IsActiveLeaseLocked(lease);
        }
    }

    internal bool Quarantine(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId)
    {
        lock (_stateGate)
        {
            if (!IsActiveLeaseLocked(lease)
                || _generationId != generationId)
            {
                return false;
            }
            if (_state == LilithRuntimeModelAuthorityState.Retired)
                return true;
            if (_state == LilithRuntimeModelAuthorityState.Quarantined)
                return true;
            _state = LilithRuntimeModelAuthorityState.Quarantined;
            _catalog = null;
            _providerId = string.Empty;
            _modelId = string.Empty;
            _revision = checked(_revision + 1);
            return true;
        }
    }

    internal void RecordNativeModelFailure(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId,
        LilithNativeModelMutationDiagnostic diagnostic)
    {
        lock (_stateGate)
        {
            if (IsActiveLeaseLocked(lease) && _generationId == generationId)
                _nativeModelFailure ??= diagnostic;
        }
    }

    internal bool Retire(
        LilithRuntimeModelAuthorityLease lease,
        Guid generationId)
    {
        lock (_stateGate)
        {
            if (!IsActiveLeaseLocked(lease)
                || _generationId != generationId)
                return false;
            if (_state != LilithRuntimeModelAuthorityState.Retired)
                _revision = checked(_revision + 1);
            _state = LilithRuntimeModelAuthorityState.Retired;
            _catalog = null;
            _providerId = string.Empty;
            _modelId = string.Empty;
            return true;
        }
    }

    internal void ReleaseLease(long leaseId)
    {
        var release = false;
        lock (_stateGate)
        {
            if (_activeLeaseId == leaseId)
            {
                _activeLeaseId = 0;
                release = true;
            }
        }
        if (release)
            _linearizationGate.Release();
    }

    private bool IsActiveLeaseLocked(LilithRuntimeModelAuthorityLease? lease)
        => lease is not null
            && _activeLeaseId != 0
            && lease.IsActiveFor(this, _activeLeaseId);

    private LilithRuntimeModelAuthoritySnapshot SnapshotLocked()
        => new(
            _generationId,
            _state,
            _revision,
            _nativeConfigRevision,
            _catalog,
            _providerId,
            _modelId,
            _nativeModelFailure);
}

internal enum HermesNativeSessionInfoDisposition
{
    Invalid = 0,
    Pending = 1,
    Ready = 2
}

internal sealed record HermesNativeSessionInfoInspection(
    HermesNativeSessionInfoDisposition Disposition,
    int ToolsetCount,
    int ToolCount,
    int McpServerCount);

internal static class HermesNativeSessionInfoSurface
{
    private const int MaximumToolsets = 256;
    private const int MaximumTools = 4096;
    private const int MaximumMcpServers = 256;
    private const int MaximumNameCharacters = 256;
    private const int MaximumMcpErrorCharacters = 16 * 1024;
    private static readonly HashSet<string> McpRequiredProperties = new(
        new[] { "name", "transport", "tools", "connected", "disabled", "status" },
        StringComparer.Ordinal);
    private static readonly HashSet<string> McpAllowedProperties = new(
        McpRequiredProperties.Concat(new[] { "error", "sampling" }),
        StringComparer.Ordinal);

    internal static HermesNativeSessionInfoInspection Inspect(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object)
            return Invalid();

        var toolsPropertyCount = 0;
        var mcpPropertyCount = 0;
        var lazyPropertyCount = 0;
        foreach (var property in info.EnumerateObject())
        {
            if (property.NameEquals("tools"))
                toolsPropertyCount++;
            else if (property.NameEquals("mcp_servers"))
                mcpPropertyCount++;
            else if (property.NameEquals("lazy"))
                lazyPropertyCount++;
        }

        if (toolsPropertyCount != 1
            || lazyPropertyCount > 1
            || !info.TryGetProperty("tools", out var tools)
            || tools.ValueKind != JsonValueKind.Object
            || info.TryGetProperty("lazy", out var lazy)
                && lazy.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return Invalid();
        }

        var isLazy = info.TryGetProperty("lazy", out lazy)
            && lazy.ValueKind == JsonValueKind.True;
        if ((!isLazy && mcpPropertyCount != 1)
            || (isLazy && mcpPropertyCount > 1))
        {
            return Invalid();
        }

        var toolsets = 0;
        var toolCount = 0;
        var observedToolsets = new HashSet<string>(StringComparer.Ordinal);
        var observedTools = new HashSet<string>(StringComparer.Ordinal);
        foreach (var toolset in tools.EnumerateObject())
        {
            toolsets++;
            if (toolsets > MaximumToolsets
                || !IsBoundedText(toolset.Name, MaximumNameCharacters)
                || !observedToolsets.Add(toolset.Name)
                || toolset.Value.ValueKind != JsonValueKind.Array)
            {
                return Invalid();
            }

            foreach (var tool in toolset.Value.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.String
                    || !IsBoundedText(
                        tool.GetString(),
                        MaximumNameCharacters)
                    || !observedTools.Add(tool.GetString()!))
                {
                    return Invalid();
                }
                toolCount++;
                if (toolCount > MaximumTools)
                    return Invalid();
            }
        }

        if (isLazy)
        {
            if (toolsets != 0 || toolCount != 0)
                return Invalid();
            var lazyMcpCount = mcpPropertyCount == 0
                ? 0
                : InspectMcpServers(info.GetProperty("mcp_servers"));
            return lazyMcpCount < 0
                ? Invalid()
                : new HermesNativeSessionInfoInspection(
                    HermesNativeSessionInfoDisposition.Pending,
                    0,
                    0,
                    lazyMcpCount);
        }

        var mcpCount = InspectMcpServers(info.GetProperty("mcp_servers"));
        return mcpCount < 0
            ? Invalid()
            : new HermesNativeSessionInfoInspection(
                HermesNativeSessionInfoDisposition.Ready,
                toolsets,
                toolCount,
                mcpCount);
    }

    internal static HermesCapabilityMcpCatalog ProjectAttestedMcpCatalog(
        JsonElement info)
    {
        var inspection = Inspect(info);
        if (inspection.Disposition != HermesNativeSessionInfoDisposition.Ready)
        {
            throw new InvalidDataException(
                "Hermes session.info is not an attested ready snapshot.");
        }

        var servers = info.GetProperty("mcp_servers")
            .EnumerateArray()
            .Select(ProjectMcpServer)
            .ToArray();
        if (servers.Length != inspection.McpServerCount)
        {
            throw new InvalidDataException(
                "Hermes session.info MCP count was contradictory.");
        }
        return new HermesCapabilityMcpCatalog(
            HermesCapabilityMcpSnapshotState.Attested,
            servers);
    }

    internal static IReadOnlyList<string> ProjectAttestedToolNames(
        JsonElement info)
    {
        var inspection = Inspect(info);
        if (inspection.Disposition != HermesNativeSessionInfoDisposition.Ready)
        {
            throw new InvalidDataException(
                "Hermes session.info is not an attested ready snapshot.");
        }
        var tools = info.GetProperty("tools")
            .EnumerateObject()
            .SelectMany(toolset => toolset.Value.EnumerateArray())
            .Select(tool => tool.GetString()!)
            .ToArray();
        if (tools.Length != inspection.ToolCount)
        {
            throw new InvalidDataException(
                "Hermes session.info tool count was contradictory.");
        }
        return tools;
    }

    private static HermesCapabilityMcpServer ProjectMcpServer(
        JsonElement server)
    {
        var status = server.GetProperty("status").GetString() switch
        {
            "connected" => HermesCapabilityMcpServerStatus.Connected,
            "disabled" => HermesCapabilityMcpServerStatus.Disabled,
            "connecting" => HermesCapabilityMcpServerStatus.Connecting,
            "failed" => HermesCapabilityMcpServerStatus.Failed,
            "configured" => HermesCapabilityMcpServerStatus.Configured,
            _ => throw new InvalidDataException(
                "Hermes session.info returned an unknown MCP status.")
        };
        return new HermesCapabilityMcpServer(
            server.GetProperty("name").GetString()!,
            server.GetProperty("transport").GetString()!,
            server.GetProperty("tools").GetInt32(),
            server.GetProperty("connected").GetBoolean(),
            server.GetProperty("disabled").GetBoolean(),
            status);
    }

    internal static bool IsBoundedToolEvent(HermesGatewayEvent gatewayEvent)
        => gatewayEvent.Payload.ValueKind == JsonValueKind.Object
            && TryGetSingleProperty(gatewayEvent.Payload, "name", out var name)
            && name.ValueKind == JsonValueKind.String
            && IsBoundedText(name.GetString(), MaximumNameCharacters);

    private static int InspectMcpServers(JsonElement servers)
    {
        if (servers.ValueKind != JsonValueKind.Array
            || servers.GetArrayLength() > MaximumMcpServers)
        {
            return -1;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in servers.EnumerateArray())
        {
            if (server.ValueKind != JsonValueKind.Object)
                return -1;
            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in server.EnumerateObject())
            {
                if (!McpAllowedProperties.Contains(property.Name)
                    || !observed.Add(property.Name))
                {
                    return -1;
                }
            }
            if (!McpRequiredProperties.IsSubsetOf(observed)
                || !TryGetSingleProperty(server, "name", out var name)
                || name.ValueKind != JsonValueKind.String
                || !IsBoundedText(name.GetString(), MaximumNameCharacters)
                || !names.Add(name.GetString()!)
                || !TryGetSingleProperty(server, "transport", out var transport)
                || transport.ValueKind != JsonValueKind.String
                || !IsBoundedText(transport.GetString(), MaximumNameCharacters)
                || !TryGetSingleProperty(server, "tools", out var tools)
                || tools.ValueKind != JsonValueKind.Number
                || !tools.TryGetInt32(out var toolCount)
                || toolCount < 0
                || toolCount > MaximumTools
                || !BooleanProperty(server, "connected")
                || !BooleanProperty(server, "disabled")
                || !TryGetSingleProperty(server, "status", out var status)
                || status.ValueKind != JsonValueKind.String
                || !IsBoundedText(status.GetString(), MaximumNameCharacters))
            {
                return -1;
            }
            if (TryGetSingleProperty(server, "error", out var error)
                && (error.ValueKind != JsonValueKind.String
                    || !IsBoundedMultilineText(
                        error.GetString(),
                        MaximumMcpErrorCharacters)))
            {
                return -1;
            }
            if (TryGetSingleProperty(server, "sampling", out var sampling)
                && sampling.ValueKind != JsonValueKind.Object)
            {
                return -1;
            }
        }
        return servers.GetArrayLength();
    }

    private static bool BooleanProperty(JsonElement value, string name)
        => TryGetSingleProperty(value, name, out var property)
            && property.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool TryGetSingleProperty(
        JsonElement value,
        string name,
        out JsonElement result)
    {
        result = default;
        var found = false;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.NameEquals(name))
                continue;
            if (found)
                return false;
            found = true;
            result = property.Value;
        }
        return found;
    }

    private static bool IsBoundedText(string? value, int maximumCharacters)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximumCharacters
            && !value.Any(char.IsControl);

    private static bool IsBoundedMultilineText(
        string? value,
        int maximumCharacters)
        => value is not null
            && value.Length <= maximumCharacters
            && !value.Any(character =>
                char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t');

    private static HermesNativeSessionInfoInspection Invalid()
        => new(HermesNativeSessionInfoDisposition.Invalid, 0, 0, 0);
}

public class LilithRuntimeException : Exception
{
    public LilithRuntimeFailureKind FailureKind { get; }

    public LilithRuntimeException(string message,
        LilithRuntimeFailureKind failureKind = LilithRuntimeFailureKind.Unspecified)
        : base(message)
    {
        FailureKind = failureKind;
    }
}

public sealed class HermesRpcException : LilithRuntimeException
{
    public int Code { get; }

    internal HermesRpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }
}

internal sealed class LilithNativeConfigMutationUncertainException :
    LilithRuntimeException
{
    internal LilithNativeConfigMutationUncertainException()
        : base(
            "Hermes native config delivery is uncertain; Runtime restart and profile recovery are required.")
    {
    }
}

internal sealed class LilithSessionActivationEventException :
    LilithRuntimeException
{
    internal LilithSessionActivationEventException(string message)
        : base(message)
    {
    }
}
