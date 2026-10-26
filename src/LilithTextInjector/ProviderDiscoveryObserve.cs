using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LilithTextInjector;

internal enum ProviderDiscoveryMode
{
    Invalid = 0,
    Off = 1,
    Observe = 2
}

internal enum ProviderDiscoveryProbeKind
{
    Invalid = 0,
    OfficialConfiguredEndpoint = 1,
    OllamaLoopback11434 = 2,
    VllmLoopback8000 = 3,
    AmbiguousOpenAiLoopback8080 = 4,
    ExplicitLmStudioLoopback = 5
}

internal enum ProviderReachabilityEvidence
{
    Invalid = 0,
    Unknown = 1,
    Reachable = 2,
    Unreachable = 3,
    Timeout = 4,
    Cancelled = 5
}

internal enum ProviderAuthenticationEvidence
{
    Invalid = 0,
    NotRequired = 1,
    AuthenticationRequired = 2,
    Authenticated = 3,
    Rejected = 4,
    Unknown = 5
}

internal enum ProviderCatalogAvailability
{
    Invalid = 0,
    Unknown = 1,
    Available = 2,
    AuthenticationRequired = 3,
    Unsupported = 4,
    Malformed = 5,
    ResourceLimitExceeded = 6,
    Stale = 7
}

internal enum ProviderDiscoveryAdmission
{
    Invalid = 0,
    PlannedForObserve = 1,
    Parsed = 2,
    ModeOff = 3,
    ProbeNotAllowlisted = 4,
    EndpointRejected = 5,
    AddressRejected = 6,
    AuthenticationRequired = 7,
    MalformedCatalog = 8,
    ResourceLimitExceeded = 9,
    IdentityEvidenceInsufficient = 10,
    Stale = 11,
    Cancelled = 12
}

internal enum ProviderCatalogAdapterKind
{
    Invalid = 0,
    OpenAiModels = 1,
    OllamaTags = 2,
    GeminiModels = 3
}

internal sealed class ProviderDiscoveryRuntimeBudget
{
    internal const int MaximumWireBytesCeiling = 256 * 1024;
    internal const int MaximumDecompressedBytesCeiling = 512 * 1024;
    internal const int MaximumJsonDepthCeiling = 32;
    internal const int MaximumModelCountCeiling = 256;
    internal const int MaximumPageCountCeiling = 2;
    internal const int MaximumStringLengthCeiling = 256;
    internal const int MaximumConnectionsCeiling = 4;

    internal int MaximumWireBytes { get; }
    internal int MaximumDecompressedBytes { get; }
    internal int MaximumJsonDepth { get; }
    internal int MaximumModelCount { get; }
    internal int MaximumPageCount { get; }
    internal int MaximumStringLength { get; }
    internal int MaximumConnections { get; }
    internal TimeSpan Timeout { get; }

    internal ProviderDiscoveryRuntimeBudget(
        int maximumWireBytes = 64 * 1024,
        int maximumDecompressedBytes = 128 * 1024,
        int maximumJsonDepth = 16,
        int maximumModelCount = 128,
        int maximumPageCount = 1,
        int maximumStringLength = 128,
        int maximumConnections = 1,
        TimeSpan? timeout = null)
    {
        MaximumWireBytes = Bounded(
            maximumWireBytes,
            MaximumWireBytesCeiling,
            nameof(maximumWireBytes));
        MaximumDecompressedBytes = Bounded(
            maximumDecompressedBytes,
            MaximumDecompressedBytesCeiling,
            nameof(maximumDecompressedBytes));
        MaximumJsonDepth = Bounded(
            maximumJsonDepth,
            MaximumJsonDepthCeiling,
            nameof(maximumJsonDepth));
        MaximumModelCount = Bounded(
            maximumModelCount,
            MaximumModelCountCeiling,
            nameof(maximumModelCount));
        MaximumPageCount = Bounded(
            maximumPageCount,
            MaximumPageCountCeiling,
            nameof(maximumPageCount));
        MaximumStringLength = Bounded(
            maximumStringLength,
            MaximumStringLengthCeiling,
            nameof(maximumStringLength));
        MaximumConnections = Bounded(
            maximumConnections,
            MaximumConnectionsCeiling,
            nameof(maximumConnections));
        Timeout = timeout ?? TimeSpan.FromSeconds(2);
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    private static int Bounded(int value, int ceiling, string name)
    {
        if (value < 1 || value > ceiling)
            throw new ArgumentOutOfRangeException(name);
        return value;
    }
}

internal sealed class ProviderDiscoveryProbePlan
{
    internal ProviderDiscoveryProbeKind Kind { get; }
    internal ProviderKind? ClaimedProvider { get; }
    internal ProviderEndpointUri Endpoint { get; }
    internal ProviderCatalogAdapterKind AdapterKind { get; }
    internal string RelativeCatalogPath { get; }
    internal ProviderIdentityEvidenceKind IdentityEvidence { get; }
    internal ProviderIdentityAdmission IdentityAdmission { get; }
    internal bool SendsCredential => false;
    internal bool AllowsGeneration => false;
    internal bool AllowsRedirect => false;
    internal bool AllowsLanScan => false;
    internal bool AllowsMdns => false;
    internal bool AllowsFilesystemScan => false;
    internal int ConnectionBudget => 1;
    internal string Fingerprint { get; }

    private ProviderDiscoveryProbePlan(
        ProviderDiscoveryProbeKind kind,
        ProviderKind? claimedProvider,
        ProviderEndpointUri endpoint,
        ProviderCatalogAdapterKind adapterKind,
        string relativeCatalogPath,
        ProviderIdentityEvidenceKind identityEvidence,
        ProviderIdentityAdmission identityAdmission)
    {
        Kind = ProviderRegistryContractGuard.Defined(kind, nameof(kind));
        if (claimedProvider.HasValue)
        {
            ClaimedProvider = ProviderRegistryContractGuard.Defined(
                claimedProvider.Value,
                nameof(claimedProvider));
        }
        Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        AdapterKind = ProviderRegistryContractGuard.Defined(
            adapterKind,
            nameof(adapterKind));
        RelativeCatalogPath = ValidateRelativePath(relativeCatalogPath);
        IdentityEvidence = ProviderRegistryContractGuard.Defined(
            identityEvidence,
            nameof(identityEvidence));
        IdentityAdmission = ProviderRegistryContractGuard.Defined(
            identityAdmission,
            nameof(identityAdmission));
        if (!ClaimedProvider.HasValue
            && IdentityAdmission
                != ProviderIdentityAdmission
                    .InsufficientGenericCompatibilityEvidence)
        {
            throw new ArgumentException(
                "An unclaimed generic endpoint cannot establish identity.",
                nameof(identityAdmission));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-DISCOVERY-PROBE/1",
            ProviderRegistryCanonicalHash.Number((int)Kind),
            ClaimedProvider.HasValue
                ? ProviderRegistryCanonicalHash.Number(
                    (int)ClaimedProvider.Value)
                : "-",
            Endpoint.Fingerprint,
            ProviderRegistryCanonicalHash.Number((int)AdapterKind),
            RelativeCatalogPath,
            ProviderRegistryCanonicalHash.Number((int)IdentityEvidence),
            ProviderRegistryCanonicalHash.Number((int)IdentityAdmission));
    }

    internal static ProviderDiscoveryProbePlan Ollama()
        => CreateLoopback(
            ProviderDiscoveryProbeKind.OllamaLoopback11434,
            ProviderKind.Ollama,
            11434,
            ProviderCatalogAdapterKind.OllamaTags,
            "/api/tags",
            ProviderIdentityEvidenceKind.ProviderSpecificSchema,
            ProviderIdentityAdmission.AcceptedProviderSpecificSchema);

    internal static ProviderDiscoveryProbePlan Vllm()
        => CreateLoopback(
            ProviderDiscoveryProbeKind.VllmLoopback8000,
            ProviderKind.Vllm,
            8000,
            ProviderCatalogAdapterKind.OpenAiModels,
            "/v1/models",
            ProviderIdentityEvidenceKind.ExplicitPlayerConfirmation,
            ProviderIdentityAdmission.AcceptedExplicitPlayerConfirmation);

    internal static ProviderDiscoveryProbePlan Ambiguous8080()
        => CreateLoopback(
            ProviderDiscoveryProbeKind.AmbiguousOpenAiLoopback8080,
            null,
            8080,
            ProviderCatalogAdapterKind.OpenAiModels,
            "/v1/models",
            ProviderIdentityEvidenceKind.GenericOpenAiModelsEndpoint,
            ProviderIdentityAdmission
                .InsufficientGenericCompatibilityEvidence);

    internal static ProviderDiscoveryProbePlan LmStudio(int explicitPort)
    {
        if (explicitPort < 1 || explicitPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(explicitPort));
        return CreateLoopback(
            ProviderDiscoveryProbeKind.ExplicitLmStudioLoopback,
            ProviderKind.LMStudio,
            explicitPort,
            ProviderCatalogAdapterKind.OpenAiModels,
            "/v1/models",
            ProviderIdentityEvidenceKind.ExplicitPlayerConfirmation,
            ProviderIdentityAdmission.AcceptedExplicitPlayerConfirmation);
    }

    internal static ProviderDiscoveryProbePlan ConfiguredOfficial(
        ProviderKind provider,
        ProviderEndpointUri endpoint,
        string relativeCatalogPath)
    {
        if (provider is ProviderKind.Ollama
            or ProviderKind.LlamaCpp
            or ProviderKind.LMStudio
            or ProviderKind.Vllm
            or ProviderKind.LocalAI
            or ProviderKind.CustomOpenAICompatible)
        {
            throw new ArgumentOutOfRangeException(nameof(provider));
        }
        return new ProviderDiscoveryProbePlan(
            ProviderDiscoveryProbeKind.OfficialConfiguredEndpoint,
            provider,
            endpoint,
            provider == ProviderKind.Gemini
                ? ProviderCatalogAdapterKind.GeminiModels
                : ProviderCatalogAdapterKind.OpenAiModels,
            relativeCatalogPath,
            ProviderIdentityEvidenceKind.OfficialWellKnownEndpoint,
            ProviderIdentityAdmission.AcceptedOfficialWellKnown);
    }

    private static ProviderDiscoveryProbePlan CreateLoopback(
        ProviderDiscoveryProbeKind kind,
        ProviderKind? provider,
        int port,
        ProviderCatalogAdapterKind adapterKind,
        string path,
        ProviderIdentityEvidenceKind evidence,
        ProviderIdentityAdmission admission)
    {
        var result = ProviderEndpointUri.TryCreate(
            "http://127.0.0.1:"
            + port.ToString(CultureInfo.InvariantCulture)
            + "/");
        return new ProviderDiscoveryProbePlan(
            kind,
            provider,
            result.Endpoint!,
            adapterKind,
            path,
            evidence,
            admission);
    }

    private static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.Contains("..", StringComparison.Ordinal)
            || path.Contains("?", StringComparison.Ordinal)
            || path.Contains("#", StringComparison.Ordinal)
            || Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            throw new ArgumentException(
                "Catalog path must be a bounded same-origin relative path.",
                nameof(path));
        }
        return path;
    }
}

internal sealed class ProviderDiscoveryPlanResult
{
    internal ProviderDiscoveryAdmission Admission { get; }
    internal IReadOnlyList<ProviderDiscoveryProbePlan> Probes { get; }
    internal bool Accepted =>
        Admission == ProviderDiscoveryAdmission.PlannedForObserve;
    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int CredentialResolutions => 0;

    internal ProviderDiscoveryPlanResult(
        ProviderDiscoveryAdmission admission,
        IEnumerable<ProviderDiscoveryProbePlan>? probes = null)
    {
        Admission = ProviderRegistryContractGuard.Defined(
            admission,
            nameof(admission));
        Probes = new ReadOnlyCollection<ProviderDiscoveryProbePlan>(
            (probes ?? Array.Empty<ProviderDiscoveryProbePlan>()).ToArray());
        if ((Admission == ProviderDiscoveryAdmission.PlannedForObserve)
            != (Probes.Count > 0))
        {
            throw new ArgumentException(
                "Only an accepted Observe plan may contain probes.",
                nameof(probes));
        }
    }
}

internal static class ProviderLoopbackDiscoveryPlanner
{
    internal static ProviderDiscoveryPlanResult Plan(
        ProviderDiscoveryMode mode,
        bool probeOllama,
        bool probeVllm,
        bool probeAmbiguous8080,
        int? explicitLmStudioPort)
    {
        if (mode == ProviderDiscoveryMode.Off)
        {
            return new ProviderDiscoveryPlanResult(
                ProviderDiscoveryAdmission.ModeOff);
        }
        if (mode != ProviderDiscoveryMode.Observe)
            throw new ArgumentOutOfRangeException(nameof(mode));
        var probes = new List<ProviderDiscoveryProbePlan>();
        if (probeOllama)
            probes.Add(ProviderDiscoveryProbePlan.Ollama());
        if (probeVllm)
            probes.Add(ProviderDiscoveryProbePlan.Vllm());
        if (probeAmbiguous8080)
            probes.Add(ProviderDiscoveryProbePlan.Ambiguous8080());
        if (explicitLmStudioPort.HasValue)
        {
            probes.Add(
                ProviderDiscoveryProbePlan.LmStudio(
                    explicitLmStudioPort.Value));
        }
        if (probes.Count == 0)
        {
            return new ProviderDiscoveryPlanResult(
                ProviderDiscoveryAdmission.ProbeNotAllowlisted);
        }
        return new ProviderDiscoveryPlanResult(
            ProviderDiscoveryAdmission.PlannedForObserve,
            probes);
    }
}

internal sealed class DiscoveredProviderModelFact
{
    internal string ModelAlias { get; }
    internal string ModelIdentityHash { get; }
    internal CatalogFactKind FactKind { get; }
    internal ProviderCatalogFactProvenance Provenance { get; }
    internal ProviderEvidenceFreshness Freshness { get; }

    internal DiscoveredProviderModelFact(
        string modelAlias,
        CatalogFactKind factKind,
        ProviderCatalogFactProvenance provenance,
        ProviderEvidenceFreshness freshness)
    {
        ModelAlias = ProviderRegistryContractGuard.SafeDisplayAlias(
            modelAlias,
            nameof(modelAlias));
        FactKind = ProviderRegistryContractGuard.Defined(
            factKind,
            nameof(factKind));
        Provenance = ProviderRegistryContractGuard.Defined(
            provenance,
            nameof(provenance));
        Freshness = ProviderRegistryContractGuard.Defined(
            freshness,
            nameof(freshness));
        ModelIdentityHash = ProviderRegistryCanonicalHash.Of(
            "D027/DISCOVERED-MODEL/1",
            LivingPromptHash.Of(ModelAlias),
            ProviderRegistryCanonicalHash.Number((int)FactKind),
            ProviderRegistryCanonicalHash.Number((int)Provenance),
            ProviderRegistryCanonicalHash.Number((int)Freshness));
    }
}

internal sealed class ProviderCatalogObservation
{
    internal ProviderDiscoveryAdmission Admission { get; }
    internal ProviderReachabilityEvidence Reachability { get; }
    internal ProviderAuthenticationEvidence Authentication { get; }
    internal ProviderCatalogAvailability CatalogAvailability { get; }
    internal IReadOnlyList<DiscoveredProviderModelFact> Models { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string? RawPayload => null;
    internal int CredentialResolutions => 0;
    internal int ProviderGenerationCalls => 0;
    internal string Fingerprint { get; }

    internal ProviderCatalogObservation(
        ProviderDiscoveryAdmission admission,
        ProviderReachabilityEvidence reachability,
        ProviderAuthenticationEvidence authentication,
        ProviderCatalogAvailability catalogAvailability,
        IEnumerable<DiscoveredProviderModelFact>? models,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        Admission = ProviderRegistryContractGuard.Defined(
            admission,
            nameof(admission));
        Reachability = ProviderRegistryContractGuard.Defined(
            reachability,
            nameof(reachability));
        Authentication = ProviderRegistryContractGuard.Defined(
            authentication,
            nameof(authentication));
        CatalogAvailability = ProviderRegistryContractGuard.Defined(
            catalogAvailability,
            nameof(catalogAvailability));
        Models = new ReadOnlyCollection<DiscoveredProviderModelFact>(
            (models ?? Array.Empty<DiscoveredProviderModelFact>()).ToArray());
        ObservedAt = LivingContinuityContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ExpiresAt = LivingContinuityContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= ObservedAt)
            throw new ArgumentException("Expiry must follow observation.", nameof(expiresAt));
        if (CatalogAvailability == ProviderCatalogAvailability.Available
            && Models.Count == 0)
        {
            throw new ArgumentException(
                "Available catalogs must contain at least one model.",
                nameof(models));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-CATALOG-OBSERVATION/1",
            ProviderRegistryCanonicalHash.Number((int)Admission),
            ProviderRegistryCanonicalHash.Number((int)Reachability),
            ProviderRegistryCanonicalHash.Number((int)Authentication),
            ProviderRegistryCanonicalHash.Number((int)CatalogAvailability),
            string.Join(",", Models.Select(model => model.ModelIdentityHash)),
            ProviderRegistryCanonicalHash.Timestamp(ObservedAt),
            ProviderRegistryCanonicalHash.Timestamp(ExpiresAt));
    }

    internal static ProviderCatalogObservation AuthenticationRequired(
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
        => new(
            ProviderDiscoveryAdmission.AuthenticationRequired,
            ProviderReachabilityEvidence.Reachable,
            ProviderAuthenticationEvidence.AuthenticationRequired,
            ProviderCatalogAvailability.AuthenticationRequired,
            null,
            observedAt,
            expiresAt);
}

internal interface IProviderModelCatalogAdapter
{
    ProviderCatalogAdapterKind Kind { get; }

    ProviderCatalogObservation Parse(
        byte[] wirePayload,
        byte[] decompressedPayload,
        int pageCount,
        ProviderDiscoveryRuntimeBudget budget,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt);
}

internal static class ProviderModelCatalogAdapterResolver
{
    internal static IProviderModelCatalogAdapter Resolve(
        ProviderCatalogAdapterKind kind)
        => kind switch
        {
            ProviderCatalogAdapterKind.OpenAiModels =>
                new OpenAiModelsCatalogAdapter(),
            ProviderCatalogAdapterKind.OllamaTags =>
                new OllamaTagsCatalogAdapter(),
            ProviderCatalogAdapterKind.GeminiModels =>
                new GeminiModelsCatalogAdapter(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

internal abstract class ProviderModelCatalogAdapterBase
    : IProviderModelCatalogAdapter
{
    public abstract ProviderCatalogAdapterKind Kind { get; }

    public ProviderCatalogObservation Parse(
        byte[] wirePayload,
        byte[] decompressedPayload,
        int pageCount,
        ProviderDiscoveryRuntimeBudget budget,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
    {
        if (wirePayload == null)
            throw new ArgumentNullException(nameof(wirePayload));
        if (decompressedPayload == null)
            throw new ArgumentNullException(nameof(decompressedPayload));
        if (budget == null)
            throw new ArgumentNullException(nameof(budget));
        if (wirePayload.Length > budget.MaximumWireBytes
            || decompressedPayload.Length > budget.MaximumDecompressedBytes
            || pageCount < 1
            || pageCount > budget.MaximumPageCount)
        {
            return ResourceRejected(observedAt, expiresAt);
        }
        try
        {
            using var document = JsonDocument.Parse(
                decompressedPayload,
                new JsonDocumentOptions
                {
                    MaxDepth = budget.MaximumJsonDepth
                });
            var aliases = ParseAliases(document.RootElement);
            if (aliases == null)
                return Malformed(observedAt, expiresAt);
            var materialized = aliases.ToArray();
            if (materialized.Length == 0)
                return Malformed(observedAt, expiresAt);
            if (materialized.Length > budget.MaximumModelCount
                || materialized.Any(
                    alias => alias.Length > budget.MaximumStringLength))
            {
                return ResourceRejected(observedAt, expiresAt);
            }
            var facts = materialized
                .Distinct(StringComparer.Ordinal)
                .OrderBy(alias => alias, StringComparer.Ordinal)
                .Select(
                    alias => new DiscoveredProviderModelFact(
                        alias,
                        FactKind(),
                        ProviderCatalogFactProvenance.ProviderEndpoint,
                        ProviderEvidenceFreshness.Current))
                .ToArray();
            return new ProviderCatalogObservation(
                ProviderDiscoveryAdmission.Parsed,
                ProviderReachabilityEvidence.Reachable,
                ProviderAuthenticationEvidence.NotRequired,
                ProviderCatalogAvailability.Available,
                facts,
                observedAt,
                expiresAt);
        }
        catch (JsonException)
        {
            return Malformed(observedAt, expiresAt);
        }
        catch (ArgumentException)
        {
            return Malformed(observedAt, expiresAt);
        }
    }

    protected abstract IEnumerable<string>? ParseAliases(JsonElement root);
    protected abstract CatalogFactKind FactKind();

    private static ProviderCatalogObservation Malformed(
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
        => new(
            ProviderDiscoveryAdmission.MalformedCatalog,
            ProviderReachabilityEvidence.Reachable,
            ProviderAuthenticationEvidence.NotRequired,
            ProviderCatalogAvailability.Malformed,
            null,
            observedAt,
            expiresAt);

    private static ProviderCatalogObservation ResourceRejected(
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt)
        => new(
            ProviderDiscoveryAdmission.ResourceLimitExceeded,
            ProviderReachabilityEvidence.Reachable,
            ProviderAuthenticationEvidence.NotRequired,
            ProviderCatalogAvailability.ResourceLimitExceeded,
            null,
            observedAt,
            expiresAt);
}

internal sealed class OpenAiModelsCatalogAdapter
    : ProviderModelCatalogAdapterBase
{
    public override ProviderCatalogAdapterKind Kind =>
        ProviderCatalogAdapterKind.OpenAiModels;

    protected override IEnumerable<string>? ParseAliases(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var result = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id)
                || id.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            result.Add(id.GetString() ?? string.Empty);
        }
        return result;
    }

    protected override CatalogFactKind FactKind() =>
        CatalogFactKind.ServerVisible;
}

internal sealed class OllamaTagsCatalogAdapter
    : ProviderModelCatalogAdapterBase
{
    public override ProviderCatalogAdapterKind Kind =>
        ProviderCatalogAdapterKind.OllamaTags;

    protected override IEnumerable<string>? ParseAliases(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var result = new List<string>();
        foreach (var item in models.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            result.Add(name.GetString() ?? string.Empty);
        }
        return result;
    }

    protected override CatalogFactKind FactKind() =>
        CatalogFactKind.Installed;
}

internal sealed class GeminiModelsCatalogAdapter
    : ProviderModelCatalogAdapterBase
{
    public override ProviderCatalogAdapterKind Kind =>
        ProviderCatalogAdapterKind.GeminiModels;

    protected override IEnumerable<string>? ParseAliases(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var result = new List<string>();
        foreach (var item in models.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var alias = name.GetString() ?? string.Empty;
            const string resourcePrefix = "models/";
            if (alias.StartsWith(resourcePrefix, StringComparison.Ordinal))
                alias = alias.Substring(resourcePrefix.Length);
            result.Add(alias);
        }
        return result;
    }

    protected override CatalogFactKind FactKind() =>
        CatalogFactKind.ServerVisible;
}

internal static class ProviderDiscoverySameOriginPolicy
{
    internal static ProviderDiscoveryAdmission Validate(
        ProviderEndpointUri endpoint,
        string relativePath,
        out Uri? resolved)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));
        resolved = null;
        if (string.IsNullOrWhiteSpace(relativePath)
            || !relativePath.StartsWith("/", StringComparison.Ordinal)
            || relativePath.StartsWith("//", StringComparison.Ordinal)
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains("?", StringComparison.Ordinal)
            || relativePath.Contains("#", StringComparison.Ordinal)
            || Uri.TryCreate(relativePath, UriKind.Absolute, out _))
        {
            return ProviderDiscoveryAdmission.EndpointRejected;
        }
        var candidate = new Uri(
            endpoint.Origin + relativePath,
            UriKind.Absolute);
        var admitted = ProviderEndpointUri.TryCreate(candidate.AbsoluteUri);
        if (!admitted.Accepted
            || admitted.Endpoint == null
            || !endpoint.HasSameOrigin(admitted.Endpoint))
        {
            return ProviderDiscoveryAdmission.EndpointRejected;
        }
        resolved = candidate;
        return ProviderDiscoveryAdmission.PlannedForObserve;
    }
}
