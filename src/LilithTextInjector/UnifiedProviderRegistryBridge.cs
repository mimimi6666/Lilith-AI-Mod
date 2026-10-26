using System;
using System.Collections.Generic;
using System.Linq;
using LilithProviderControlPlane;

namespace LilithTextInjector;

internal enum UnifiedProviderRegistryBridgeMode
{
    Invalid = 0,
    Off = 1,
    SourceProjection = 2
}

internal enum UnifiedProviderRegistryBridgeAdmission
{
    Invalid = 0,
    Off = 1,
    AcceptedForSourceProjection = 2,
    TextAssignmentMissing = 3,
    ConfiguredInstanceMissing = 4,
    ConfiguredInstanceMismatch = 5,
    CatalogExpired = 6,
    ControlProfileMismatch = 7,
    RuntimeProfileRejected = 8,
    ProtocolMismatch = 9,
    EndpointRejected = 10,
    RegistryRejected = 11,
    OwnershipRejected = 12
}

internal enum UnifiedProviderRuntimeContractAdmission
{
    Invalid = 0,
    Accepted = 1,
    ControlKindMismatch = 2,
    ProviderIdMismatch = 3,
    ControlProtocolMismatch = 4,
    RuntimeProtocolMismatch = 5,
    GenerationPathMismatch = 6,
    AuthenticationMismatch = 7,
    RuntimeNotTextOnly = 8,
    RetryOrFallbackEnabled = 9,
    PreparedProtocolMismatch = 10,
    CodecUnavailable = 11
}

internal enum UnifiedProviderSourceOwner
{
    Invalid = 0,
    Legacy = 1,
    Registry = 2
}

internal enum UnifiedProviderSourceOwnershipAdmission
{
    Invalid = 0,
    Created = 1,
    Existing = 2,
    OpportunityIdentityConflict = 3,
    OwnerConflict = 4
}

/// <summary>
/// Immutable copy of the currently active runtime identity.  The bridge never
/// consults a global character name and cannot silently follow a later runtime
/// generation.
/// </summary>
internal sealed class UnifiedProviderRuntimeIdentitySnapshot
{
    internal string RuntimeProfileId { get; }
    internal string RuntimeInstanceId { get; }
    internal long Generation { get; }
    internal string OrchestratorSessionId { get; }
    internal string OwnerIdentity { get; }
    internal string Fingerprint { get; }

    internal UnifiedProviderRuntimeIdentitySnapshot(
        CharacterRuntimeIdentity identity)
    {
        if (identity == null)
            throw new ArgumentNullException(nameof(identity));
        RuntimeProfileId = identity.RuntimeProfileId;
        RuntimeInstanceId = identity.RuntimeInstanceId;
        Generation = identity.Generation;
        OrchestratorSessionId = identity.OrchestratorSessionId;
        OwnerIdentity = identity.OwnerIdentity;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-RUNTIME-IDENTITY/1",
            RuntimeProfileId,
            RuntimeInstanceId,
            ProviderRegistryCanonicalHash.Number(Generation),
            OrchestratorSessionId,
            OwnerIdentity);
    }

    internal bool Matches(CharacterRuntimeIdentity? identity)
        => identity != null
            && string.Equals(
                RuntimeProfileId,
                identity.RuntimeProfileId,
                StringComparison.Ordinal)
            && string.Equals(
                RuntimeInstanceId,
                identity.RuntimeInstanceId,
                StringComparison.Ordinal)
            && Generation == identity.Generation
            && string.Equals(
                OrchestratorSessionId,
                identity.OrchestratorSessionId,
                StringComparison.Ordinal)
            && string.Equals(
                OwnerIdentity,
                identity.OwnerIdentity,
                StringComparison.Ordinal);
}

/// <summary>
/// Exact model source selected by the control plane.  This is intentionally a
/// source binding only: it cannot acquire a credential lease, send a request,
/// deliver text, retry, or choose a fallback.
/// </summary>
internal sealed class UnifiedProviderGenericTextRuntimeSourceBinding
{
    internal long ControlStoreGeneration { get; }
    internal string ControlSnapshotFingerprint { get; }
    internal string ConfiguredInstanceId { get; }
    internal ProviderControlKind ControlKind { get; }
    internal string ControlProfileId { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfigurationFingerprint { get; }
    internal string SelectedModel { get; }
    internal ProviderEndpointInstanceId RegistryInstanceId { get; }
    internal ProviderModelInstanceId RegistryModelId { get; }
    internal OpaqueTransportModelReference TransportReference { get; }
    internal ProviderControlRuntimeProfile RuntimeProfile { get; }
    internal UnifiedProviderRuntimeIdentitySnapshot RuntimeIdentity { get; }
    internal UnifiedProviderEndpointAdmission ControlEndpointAdmission { get; }
    internal UnifiedProviderEndpointAdmissionReason ControlEndpointReason { get; }
    internal bool RequiresSendTimeConnectedAddressEvidence { get; }
    internal string Fingerprint { get; }
    internal bool TextGenerationOnly => true;
    internal bool AllowsCredentialLease => false;
    internal bool AllowsNetworkCall => false;
    internal bool AllowsDelivery => false;
    internal bool AllowsQueueWrite => false;
    internal int AutomaticRetryCount => 0;
    internal int ProviderFallbackCount => 0;
    internal int ModelFallbackCount => 0;

    internal UnifiedProviderGenericTextRuntimeSourceBinding(
        long controlStoreGeneration,
        string controlSnapshotFingerprint,
        UnifiedProviderConfiguredInstance configured,
        ProviderEndpointInstanceId registryInstanceId,
        ProviderModelInstanceId registryModelId,
        OpaqueTransportModelReference transportReference,
        ProviderControlRuntimeProfile runtimeProfile,
        UnifiedProviderRuntimeIdentitySnapshot runtimeIdentity,
        UnifiedProviderEndpointAdmissionResult endpointAdmission)
    {
        if (controlStoreGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(controlStoreGeneration));
        ControlStoreGeneration = controlStoreGeneration;
        ControlSnapshotFingerprint = ProviderRegistryContractGuard.Sha256(
            controlSnapshotFingerprint,
            nameof(controlSnapshotFingerprint));
        if (configured == null)
            throw new ArgumentNullException(nameof(configured));
        ConfiguredInstanceId = configured.InstanceId;
        ControlKind = configured.ProviderKind;
        ControlProfileId = configured.ProfileId;
        ConfigurationGeneration = configured.ConfigurationGeneration;
        ConfigurationFingerprint = ProviderRegistryContractGuard.Sha256(
            configured.ConfigurationFingerprint,
            nameof(configured.ConfigurationFingerprint));
        SelectedModel = configured.SelectedModel;
        RegistryInstanceId = registryInstanceId
            ?? throw new ArgumentNullException(nameof(registryInstanceId));
        RegistryModelId = registryModelId
            ?? throw new ArgumentNullException(nameof(registryModelId));
        TransportReference = transportReference
            ?? throw new ArgumentNullException(nameof(transportReference));
        RuntimeProfile = runtimeProfile
            ?? throw new ArgumentNullException(nameof(runtimeProfile));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        if (endpointAdmission == null)
            throw new ArgumentNullException(nameof(endpointAdmission));
        if (endpointAdmission.Admission
            == UnifiedProviderEndpointAdmission.Rejected)
        {
            throw new ArgumentException(
                "A rejected endpoint cannot enter the runtime source binding.",
                nameof(endpointAdmission));
        }
        ControlEndpointAdmission = endpointAdmission.Admission;
        ControlEndpointReason = endpointAdmission.Reason;
        RequiresSendTimeConnectedAddressEvidence =
            endpointAdmission.RequiresSendTimeConnectedAddressEvidence;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-GENERIC-TEXT-SOURCE/1",
            ProviderRegistryCanonicalHash.Number(ControlStoreGeneration),
            ControlSnapshotFingerprint,
            ConfiguredInstanceId,
            ProviderRegistryCanonicalHash.Number((int)ControlKind),
            ControlProfileId,
            ProviderRegistryCanonicalHash.Number(ConfigurationGeneration),
            ConfigurationFingerprint,
            SelectedModel,
            RegistryInstanceId.Value,
            RegistryModelId.Value,
            TransportReference.Value,
            RuntimeProfile.ProfileFingerprint,
            RuntimeIdentity.Fingerprint,
            ProviderRegistryCanonicalHash.Number(
                (int)ControlEndpointAdmission),
            ProviderRegistryCanonicalHash.Number((int)ControlEndpointReason),
            RequiresSendTimeConnectedAddressEvidence ? "1" : "0");
    }
}

internal sealed class UnifiedProviderSourceOpportunity
{
    internal string OpportunityId { get; }
    internal string SourceKind => "UnifiedProviderControlPlaneText";
    internal long StoreGeneration { get; }
    internal long ConfigurationGeneration { get; }
    internal string ConfiguredInstanceId { get; }
    internal string SelectedModel { get; }
    internal string RuntimeIdentityFingerprint { get; }
    internal string Fingerprint { get; }

    internal UnifiedProviderSourceOpportunity(
        string opportunityId,
        UnifiedProviderGenericTextRuntimeSourceBinding source)
    {
        OpportunityId = ProviderRegistryContractGuard.Identifier(
            opportunityId,
            nameof(opportunityId));
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        StoreGeneration = source.ControlStoreGeneration;
        ConfigurationGeneration = source.ConfigurationGeneration;
        ConfiguredInstanceId = source.ConfiguredInstanceId;
        SelectedModel = source.SelectedModel;
        RuntimeIdentityFingerprint = source.RuntimeIdentity.Fingerprint;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-SOURCE-OPPORTUNITY/1",
            OpportunityId,
            SourceKind,
            source.Fingerprint);
    }
}

internal sealed class UnifiedProviderSourceOwnershipClaim
{
    internal UnifiedProviderSourceOpportunity Opportunity { get; }
    internal UnifiedProviderSourceOwner Owner { get; }
    internal string Fingerprint { get; }
    internal bool LegacyMaySubmit => Owner == UnifiedProviderSourceOwner.Legacy;
    internal bool RegistryMayProject => Owner == UnifiedProviderSourceOwner.Registry;
    internal bool SuppressesLegacyDirectSubmission =>
        Owner == UnifiedProviderSourceOwner.Registry;
    internal bool AllowsLegacyFallback => false;
    internal bool AllowsNetworkCall => false;
    internal bool AllowsDelivery => false;

    internal UnifiedProviderSourceOwnershipClaim(
        UnifiedProviderSourceOpportunity opportunity,
        UnifiedProviderSourceOwner owner)
    {
        Opportunity = opportunity
            ?? throw new ArgumentNullException(nameof(opportunity));
        if (!Enum.IsDefined(typeof(UnifiedProviderSourceOwner), owner)
            || owner == UnifiedProviderSourceOwner.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }
        Owner = owner;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-SOURCE-OWNERSHIP/1",
            Opportunity.Fingerprint,
            ProviderRegistryCanonicalHash.Number((int)Owner));
    }
}

/// <summary>
/// Source-only atomic ownership ledger.  Registry ownership suppresses the
/// matching Legacy source and never falls back.  It does not submit work.
/// </summary>
internal sealed class UnifiedProviderSourceOwnershipLedger
{
    private readonly object _sync = new();
    private readonly Dictionary<string, UnifiedProviderSourceOwnershipClaim>
        _claims = new(StringComparer.Ordinal);

    internal int Count
    {
        get
        {
            lock (_sync)
                return _claims.Count;
        }
    }

    internal UnifiedProviderSourceOwnershipAdmission TryAssign(
        UnifiedProviderSourceOpportunity opportunity,
        UnifiedProviderSourceOwner owner,
        out UnifiedProviderSourceOwnershipClaim? claim)
    {
        if (opportunity == null)
            throw new ArgumentNullException(nameof(opportunity));
        if (!Enum.IsDefined(typeof(UnifiedProviderSourceOwner), owner)
            || owner == UnifiedProviderSourceOwner.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }

        lock (_sync)
        {
            if (_claims.TryGetValue(opportunity.OpportunityId, out var existing))
            {
                claim = existing;
                if (!string.Equals(
                        existing.Opportunity.Fingerprint,
                        opportunity.Fingerprint,
                        StringComparison.Ordinal))
                {
                    return UnifiedProviderSourceOwnershipAdmission
                        .OpportunityIdentityConflict;
                }
                return existing.Owner == owner
                    ? UnifiedProviderSourceOwnershipAdmission.Existing
                    : UnifiedProviderSourceOwnershipAdmission.OwnerConflict;
            }

            claim = new UnifiedProviderSourceOwnershipClaim(opportunity, owner);
            _claims.Add(opportunity.OpportunityId, claim);
            return UnifiedProviderSourceOwnershipAdmission.Created;
        }
    }
}

internal sealed class UnifiedProviderRegistrySourceProjection
{
    internal ProviderRegistrySnapshot Registry { get; }
    internal UnifiedProviderGenericTextRuntimeSourceBinding RuntimeSource { get; }
    internal UnifiedProviderSourceOpportunity Opportunity { get; }
    internal UnifiedProviderSourceOwnershipClaim Ownership { get; }
    internal string Fingerprint { get; }
    internal bool AllowsNetworkCall => false;
    internal bool AllowsDelivery => false;
    internal bool AllowsQueueWrite => false;
    internal bool AllowsExecution => false;

    internal UnifiedProviderRegistrySourceProjection(
        ProviderRegistrySnapshot registry,
        UnifiedProviderGenericTextRuntimeSourceBinding runtimeSource,
        UnifiedProviderSourceOpportunity opportunity,
        UnifiedProviderSourceOwnershipClaim ownership)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        RuntimeSource = runtimeSource
            ?? throw new ArgumentNullException(nameof(runtimeSource));
        Opportunity = opportunity
            ?? throw new ArgumentNullException(nameof(opportunity));
        Ownership = ownership
            ?? throw new ArgumentNullException(nameof(ownership));
        if (Ownership.Owner != UnifiedProviderSourceOwner.Registry
            || !string.Equals(
                Ownership.Opportunity.Fingerprint,
                Opportunity.Fingerprint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A source projection requires matching Registry ownership.",
                nameof(ownership));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-REGISTRY-SOURCE-PROJECTION/1",
            Registry.Fingerprint,
            RuntimeSource.Fingerprint,
            Opportunity.Fingerprint,
            Ownership.Fingerprint);
    }
}

internal sealed class UnifiedProviderRegistryBridgeResult
{
    internal UnifiedProviderRegistryBridgeAdmission Admission { get; }
    internal UnifiedProviderRuntimeContractAdmission RuntimeContractAdmission { get; }
    internal ProviderControlRuntimeProfileAdmission RuntimeProfileAdmission { get; }
    internal ProviderEndpointUriAdmission EndpointAdmission { get; }
    internal ProviderRegistryBuildAdmission RegistryAdmission { get; }
    internal UnifiedProviderSourceOwnershipAdmission OwnershipAdmission { get; }
    internal UnifiedProviderRegistrySourceProjection? Projection { get; }
    internal bool Accepted =>
        Admission
        == UnifiedProviderRegistryBridgeAdmission.AcceptedForSourceProjection;
    internal bool AllowsNetworkCall => false;
    internal bool AllowsDelivery => false;
    internal bool AllowsQueueWrite => false;
    internal bool AllowsExecution => false;

    internal UnifiedProviderRegistryBridgeResult(
        UnifiedProviderRegistryBridgeAdmission admission,
        UnifiedProviderRuntimeContractAdmission runtimeContractAdmission =
            UnifiedProviderRuntimeContractAdmission.Invalid,
        ProviderControlRuntimeProfileAdmission runtimeProfileAdmission =
            ProviderControlRuntimeProfileAdmission.UnknownControlKind,
        ProviderEndpointUriAdmission endpointAdmission =
            ProviderEndpointUriAdmission.Invalid,
        ProviderRegistryBuildAdmission registryAdmission =
            ProviderRegistryBuildAdmission.InvalidGeneration,
        UnifiedProviderSourceOwnershipAdmission ownershipAdmission =
            UnifiedProviderSourceOwnershipAdmission.Invalid,
        UnifiedProviderRegistrySourceProjection? projection = null)
    {
        if (!Enum.IsDefined(typeof(UnifiedProviderRegistryBridgeAdmission), admission)
            || admission == UnifiedProviderRegistryBridgeAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        var accepted = admission
            == UnifiedProviderRegistryBridgeAdmission.AcceptedForSourceProjection;
        if (accepted != (projection != null))
        {
            throw new ArgumentException(
                "Only an accepted bridge result may carry a projection.",
                nameof(projection));
        }
        Admission = admission;
        RuntimeContractAdmission = runtimeContractAdmission;
        RuntimeProfileAdmission = runtimeProfileAdmission;
        EndpointAdmission = endpointAdmission;
        RegistryAdmission = registryAdmission;
        OwnershipAdmission = ownershipAdmission;
        Projection = projection;
    }
}

internal static class UnifiedProviderRuntimeContractPolicy
{
    internal static UnifiedProviderRuntimeContractAdmission Evaluate(
        ProviderControlProfile control,
        ProviderControlRuntimeProfile runtime)
    {
        if (control == null)
            throw new ArgumentNullException(nameof(control));
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));
        if (control.Kind != runtime.ControlKind)
            return UnifiedProviderRuntimeContractAdmission.ControlKindMismatch;
        if (!string.Equals(
                control.ProviderId,
                runtime.ControlProviderId,
                StringComparison.Ordinal))
        {
            return UnifiedProviderRuntimeContractAdmission.ProviderIdMismatch;
        }
        if (control.Protocol != runtime.ControlPlaneProtocol)
        {
            return UnifiedProviderRuntimeContractAdmission
                .ControlProtocolMismatch;
        }
        if (ExpectedRuntimeProtocol(control.Protocol) != runtime.RuntimeProtocol)
        {
            return UnifiedProviderRuntimeContractAdmission
                .RuntimeProtocolMismatch;
        }
        if (!string.Equals(
                ExpectedGenerationPath(control.Kind),
                control.GenerationPath,
                StringComparison.Ordinal))
        {
            return UnifiedProviderRuntimeContractAdmission
                .GenerationPathMismatch;
        }
        if (!AuthenticationMatches(control, runtime.AuthenticationScheme))
        {
            return UnifiedProviderRuntimeContractAdmission
                .AuthenticationMismatch;
        }
        if (!runtime.TextGenerationOnly
            || !runtime.AllowsCapability(ProviderCapability.TextGeneration)
            || Enum.GetValues<ProviderCapability>()
                .Where(value => value is not ProviderCapability.Invalid
                    and not ProviderCapability.TextGeneration)
                .Any(runtime.AllowsCapability))
        {
            return UnifiedProviderRuntimeContractAdmission.RuntimeNotTextOnly;
        }
        if (runtime.AutomaticRetryCount != 0
            || runtime.ProviderFallbackCount != 0
            || runtime.ModelFallbackCount != 0)
        {
            return UnifiedProviderRuntimeContractAdmission
                .RetryOrFallbackEnabled;
        }
        if (ExpectedPreparedProtocol(control.Protocol)
            != runtime.PreparedProtocol)
        {
            return UnifiedProviderRuntimeContractAdmission
                .PreparedProtocolMismatch;
        }
        if (!ProviderProtocolCodecResolver.TryResolve(
                runtime.RuntimeProvider,
                runtime.RuntimeProtocol,
                runtime.CodecProfileVersion,
                out var codec)
            || codec == null)
        {
            return UnifiedProviderRuntimeContractAdmission.CodecUnavailable;
        }
        return UnifiedProviderRuntimeContractAdmission.Accepted;
    }

    private static ProviderProtocolKind ExpectedRuntimeProtocol(
        ProviderControlProtocol protocol)
        => protocol switch
        {
            ProviderControlProtocol.OpenAiResponses =>
                ProviderProtocolKind.OpenAiResponses,
            ProviderControlProtocol.OpenAiChatCompletions or
                ProviderControlProtocol.OpenAiCompatibleChat =>
                ProviderProtocolKind.OpenAiChatCompletions,
            ProviderControlProtocol.GeminiGenerateContent =>
                ProviderProtocolKind.GeminiGenerateContent,
            ProviderControlProtocol.AnthropicMessages =>
                ProviderProtocolKind.AnthropicMessages,
            ProviderControlProtocol.OllamaNativeChat =>
                ProviderProtocolKind.OllamaNativeChat,
            _ => ProviderProtocolKind.Invalid
        };

    private static string ExpectedGenerationPath(ProviderControlKind kind)
        => kind switch
        {
            ProviderControlKind.DeepSeek => "chat/completions",
            ProviderControlKind.GoogleGemini =>
                "v1beta/models/{model}:generateContent",
            ProviderControlKind.OpenAI => "v1/chat/completions",
            ProviderControlKind.QwenDashScope => "chat/completions",
            ProviderControlKind.AnthropicClaude => "v1/messages",
            ProviderControlKind.OpenRouter => "chat/completions",
            ProviderControlKind.OllamaCloud or ProviderControlKind.OllamaLocal =>
                "chat",
            ProviderControlKind.LMStudio => "v1/chat/completions",
            ProviderControlKind.LlamaCpp or ProviderControlKind.Vllm or
                ProviderControlKind.LocalAI or
                ProviderControlKind.CustomOpenAICompatible =>
                "chat/completions",
            _ => string.Empty
        };

    private static PreparedProviderProtocol ExpectedPreparedProtocol(
        ProviderControlProtocol protocol)
        => protocol switch
        {
            ProviderControlProtocol.OpenAiResponses =>
                PreparedProviderProtocol.OpenAiResponses,
            ProviderControlProtocol.OpenAiChatCompletions or
                ProviderControlProtocol.OpenAiCompatibleChat =>
                PreparedProviderProtocol.OpenAiChatCompletions,
            ProviderControlProtocol.GeminiGenerateContent =>
                PreparedProviderProtocol.GeminiGenerateContent,
            ProviderControlProtocol.AnthropicMessages =>
                PreparedProviderProtocol.AnthropicMessages,
            ProviderControlProtocol.OllamaNativeChat =>
                PreparedProviderProtocol.OllamaNativeChat,
            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };

    private static bool AuthenticationMatches(
        ProviderControlProfile control,
        ProviderAuthenticationScheme runtime)
        => control.Auth switch
        {
            ProviderControlAuth.Bearer =>
                runtime == ProviderAuthenticationScheme.Bearer,
            ProviderControlAuth.GoogleApiKeyHeader =>
                runtime == ProviderAuthenticationScheme.GoogleApiKey,
            ProviderControlAuth.AnthropicApiKeyHeader =>
                runtime == ProviderAuthenticationScheme.AnthropicApiKey,
            ProviderControlAuth.NoneByDefault =>
                runtime == ProviderAuthenticationScheme.None,
            ProviderControlAuth.OptionalBearer or
                ProviderControlAuth.LocalAiOptional or
                ProviderControlAuth.UserDefined =>
                runtime is ProviderAuthenticationScheme.None
                    or ProviderAuthenticationScheme.Bearer,
            _ => false
        };
}

/// <summary>
/// Independently Default-Off, source-only bridge from the D-033 control plane
/// to the existing Provider Registry and generic text runtime profile.  It has
/// no live caller and creates no request, credential lease, queue entry,
/// delivery, retry, or fallback.
/// </summary>
internal static class UnifiedProviderControlPlaneRegistryBridge
{
    internal static UnifiedProviderRegistryBridgeResult TryProject(
        UnifiedProviderControlPlaneSnapshot control,
        CharacterRuntimeIdentity runtimeIdentity,
        string opportunityId,
        DateTimeOffset observedAt,
        UnifiedProviderRegistryBridgeMode mode,
        UnifiedProviderSourceOwnershipLedger ownership)
    {
        if (control == null)
            throw new ArgumentNullException(nameof(control));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        if (ownership == null)
            throw new ArgumentNullException(nameof(ownership));
        if (!Enum.IsDefined(typeof(UnifiedProviderRegistryBridgeMode), mode)
            || mode == UnifiedProviderRegistryBridgeMode.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (mode == UnifiedProviderRegistryBridgeMode.Off)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.Off);
        }

        var assignment = control.TextAssignment;
        if (assignment == null)
        {
            return Reject(
                UnifiedProviderRegistryBridgeAdmission.TextAssignmentMissing);
        }
        var configured = control.Instances.SingleOrDefault(value =>
            string.Equals(
                value.InstanceId,
                assignment.InstanceId,
                StringComparison.Ordinal));
        if (configured == null)
        {
            return Reject(
                UnifiedProviderRegistryBridgeAdmission.ConfiguredInstanceMissing);
        }
        if (!assignment.Matches(configured))
        {
            return Reject(
                UnifiedProviderRegistryBridgeAdmission.ConfiguredInstanceMismatch);
        }
        if (!configured.CatalogIsCurrent(observedAt))
        {
            return Reject(UnifiedProviderRegistryBridgeAdmission.CatalogExpired);
        }
        if (!UnifiedProviderProfileRegistry.TryGet(
                configured.ProviderKind,
                out var controlProfile)
            || controlProfile == null
            || !string.Equals(
                configured.ProfileId,
                controlProfile.ProviderId,
                StringComparison.Ordinal))
        {
            return Reject(
                UnifiedProviderRegistryBridgeAdmission.ControlProfileMismatch);
        }

        var runtimeProfileAdmission =
            ProviderControlRuntimeProfileResolver.TryResolve(
                configured.ProviderKind,
                configured.CredentialConfigured,
                out var runtimeProfile);
        if (runtimeProfileAdmission
                != ProviderControlRuntimeProfileAdmission.Accepted
            || runtimeProfile == null)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.RuntimeProfileRejected,
                runtimeProfileAdmission: runtimeProfileAdmission);
        }
        var runtimeContractAdmission =
            UnifiedProviderRuntimeContractPolicy.Evaluate(
                controlProfile,
                runtimeProfile);
        if (runtimeContractAdmission
            != UnifiedProviderRuntimeContractAdmission.Accepted)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.ProtocolMismatch,
                runtimeContractAdmission,
                runtimeProfileAdmission);
        }

        var endpointPolicy = UnifiedProviderEndpointAdmissionPolicy.Evaluate(
            configured.ProviderKind,
            configured.BaseUri,
            configured.RouteIntent,
            configured.EndpointProfileId,
            configured.WorkspaceId);
        if (endpointPolicy.Admission
            == UnifiedProviderEndpointAdmission.Rejected)
        {
            return Reject(
                UnifiedProviderRegistryBridgeAdmission.EndpointRejected,
                runtimeContractAdmission,
                runtimeProfileAdmission);
        }
        var endpoint = ProviderEndpointUri.TryCreate(
            endpointPolicy.NormalizedEndpoint!.AbsoluteUri);
        if (!endpoint.Accepted || endpoint.Endpoint == null)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.EndpointRejected,
                runtimeContractAdmission,
                runtimeProfileAdmission,
                endpoint.Admission);
        }

        var ids = Identities(configured, runtimeProfile);
        var definition = new ProviderDefinition(
            ids.DefinitionId,
            runtimeProfile.RuntimeProvider,
            new[] { runtimeProfile.RuntimeProtocol });
        var route = RouteMetadata(configured.RouteIntent);
        CredentialReference? credential = null;
        if (runtimeProfile.AuthenticationScheme
            != ProviderAuthenticationScheme.None)
        {
            credential = new CredentialReference(
                ids.CredentialReferenceId!,
                ids.InstanceId,
                runtimeProfile.AuthenticationScheme);
        }
        var instance = new ProviderEndpointInstance(
            ids.InstanceId,
            ids.DefinitionId,
            runtimeProfile.RuntimeProvider,
            route.UiGroup,
            route.DeploymentOwnership,
            route.RouteLocality,
            route.Exposure,
            endpoint.Endpoint,
            runtimeProfile.RuntimeProtocol,
            runtimeProfile.ProtocolProfileVersion,
            runtimeProfile.AuthenticationScheme,
            credential,
            route.Trust,
            enabled: true,
            configured.ConfigurationGeneration);
        var evidence = new ProviderEvidenceStamp(
            configured.CatalogObservedAt,
            observedAt,
            configured.CatalogExpiresAt,
            ProviderEvidenceFreshness.Current);
        var model = new ProviderModelDescriptor(
            ids.InstanceId,
            ids.ModelId,
            runtimeProfile.ControlProviderId + "-selected-model",
            ProviderRegistryCanonicalHash.Of(
                "D033/UNIFIED-PROVIDER-MODEL-CORRELATION/1",
                configured.InstanceId,
                configured.SelectedModel,
                configured.ConfigurationFingerprint),
            route.IsLocal
                ? ProviderTransportIdentityClass.SensitiveLocalIdentifier
                : ProviderTransportIdentityClass.PublicModelId,
            ids.TransportReference,
            new[]
            {
                new ProviderCatalogFact(
                    CatalogFactKind.ServerVisible,
                    route.IsLocal
                        ? ProviderCatalogFactProvenance.LocalRuntime
                        : ProviderCatalogFactProvenance.ProviderEndpoint,
                    evidence)
            },
            TextOnlyCapabilities(),
            route.Venue,
            route.VenueProvenance,
            evidence);
        var registryResult = ProviderRegistrySnapshot.Create(
            ProviderRegistryContractGuard.RegistrySchemaVersion,
            control.StoreGeneration,
            new[] { definition },
            new[] { instance },
            new[] { model });
        if (!registryResult.Accepted || registryResult.Snapshot == null)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.RegistryRejected,
                runtimeContractAdmission,
                runtimeProfileAdmission,
                endpoint.Admission,
                registryResult.Admission);
        }

        var source = new UnifiedProviderGenericTextRuntimeSourceBinding(
            control.StoreGeneration,
            control.SnapshotFingerprint,
            configured,
            ids.InstanceId,
            ids.ModelId,
            ids.TransportReference,
            runtimeProfile,
            new UnifiedProviderRuntimeIdentitySnapshot(runtimeIdentity),
            endpointPolicy);
        var opportunity = new UnifiedProviderSourceOpportunity(
            opportunityId,
            source);
        var ownershipAdmission = ownership.TryAssign(
            opportunity,
            UnifiedProviderSourceOwner.Registry,
            out var claim);
        if (ownershipAdmission
                is UnifiedProviderSourceOwnershipAdmission
                    .OpportunityIdentityConflict
                    or UnifiedProviderSourceOwnershipAdmission.OwnerConflict
            || claim == null)
        {
            return new UnifiedProviderRegistryBridgeResult(
                UnifiedProviderRegistryBridgeAdmission.OwnershipRejected,
                runtimeContractAdmission,
                runtimeProfileAdmission,
                endpoint.Admission,
                registryResult.Admission,
                ownershipAdmission);
        }
        var projection = new UnifiedProviderRegistrySourceProjection(
            registryResult.Snapshot,
            source,
            opportunity,
            claim);
        return new UnifiedProviderRegistryBridgeResult(
            UnifiedProviderRegistryBridgeAdmission.AcceptedForSourceProjection,
            runtimeContractAdmission,
            runtimeProfileAdmission,
            endpoint.Admission,
            registryResult.Admission,
            ownershipAdmission,
            projection);
    }

    private static UnifiedProviderRegistryBridgeResult Reject(
        UnifiedProviderRegistryBridgeAdmission admission,
        UnifiedProviderRuntimeContractAdmission runtimeContract =
            UnifiedProviderRuntimeContractAdmission.Invalid,
        ProviderControlRuntimeProfileAdmission runtimeProfile =
            ProviderControlRuntimeProfileAdmission.UnknownControlKind)
        => new(admission, runtimeContract, runtimeProfile);

    private static (
        ProviderDefinitionId DefinitionId,
        ProviderEndpointInstanceId InstanceId,
        ProviderModelInstanceId ModelId,
        CredentialReferenceId? CredentialReferenceId,
        OpaqueTransportModelReference TransportReference) Identities(
        UnifiedProviderConfiguredInstance configured,
        ProviderControlRuntimeProfile runtime)
    {
        var instanceHash = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-INSTANCE-ID/1",
            configured.InstanceId,
            configured.ConfigurationFingerprint);
        var modelHash = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-MODEL-ID/1",
            configured.InstanceId,
            configured.SelectedModel,
            configured.ConfigurationFingerprint);
        var credentialHash = ProviderRegistryCanonicalHash.Of(
            "D033/UNIFIED-PROVIDER-CREDENTIAL-ID/1",
            configured.InstanceId,
            configured.CredentialSha256,
            configured.ConfigurationFingerprint);
        return (
            new ProviderDefinitionId("d033-" + runtime.ControlProviderId),
            new ProviderEndpointInstanceId(
                "d033-instance-" + instanceHash[..32]),
            new ProviderModelInstanceId("d033-model-" + modelHash[..32]),
            runtime.AuthenticationScheme == ProviderAuthenticationScheme.None
                ? null
                : new CredentialReferenceId(
                    "d033-credential-" + credentialHash[..32]),
            new OpaqueTransportModelReference(
                "d033-transport-" + modelHash[32..64]));
    }

    private static ProviderCapabilityProfile TextOnlyCapabilities()
        => new(
            Enum.GetValues<ProviderCapability>()
                .Where(value => value != ProviderCapability.Invalid)
                .Select(value => new ProviderCapabilityStatus(
                    value,
                    value == ProviderCapability.TextGeneration
                        ? ProviderCapabilityEvidenceState.Supported
                        : ProviderCapabilityEvidenceState.Unknown,
                    ProviderCapabilityEvidenceState.Unknown,
                    value == ProviderCapability.TextGeneration
                        ? ProviderCapabilityAuthorizationState.Authorized
                        : ProviderCapabilityAuthorizationState.NotAuthorized))
                .ToArray());

    private static (
        ProviderUiGroup UiGroup,
        DeploymentOwnership DeploymentOwnership,
        ConnectionRouteLocality RouteLocality,
        EndpointExposureState Exposure,
        ProviderTrustClass Trust,
        ExecutionVenue Venue,
        ProviderExecutionVenueProvenance VenueProvenance,
        bool IsLocal) RouteMetadata(ConfiguredEndpointRouteIntent routeIntent)
    {
        if (routeIntent == null)
            throw new ArgumentNullException(nameof(routeIntent));
        return routeIntent.Kind switch
        {
            ConfiguredEndpointRouteKind.ManagedCloudHttps => (
                ProviderUiGroup.Cloud,
                DeploymentOwnership.ManagedCloud,
                ConnectionRouteLocality.PublicRemote,
                EndpointExposureState.PublicExposedDeclared,
                ProviderTrustClass.OfficialCloudHttps,
                ExecutionVenue.ManagedRemote,
                ProviderExecutionVenueProvenance.OfficialProvider,
                false),
            ConfiguredEndpointRouteKind.LoopbackOnly => (
                ProviderUiGroup.LocalAndSelfHosted,
                DeploymentOwnership.SelfHosted,
                ConnectionRouteLocality.Loopback,
                EndpointExposureState.LoopbackOnlyVerified,
                ProviderTrustClass.LoopbackRouteAdmitted,
                ExecutionVenue.LocalProcess,
                ProviderExecutionVenueProvenance.VerifiedLocalRuntime,
                true),
            ConfiguredEndpointRouteKind.ExplicitLan => (
                ProviderUiGroup.LocalAndSelfHosted,
                DeploymentOwnership.SelfHosted,
                ConnectionRouteLocality.Lan,
                EndpointExposureState.LanExposedDeclared,
                ProviderTrustClass.ExplicitLan,
                ExecutionVenue.SelfHostedRemote,
                ProviderExecutionVenueProvenance.ExplicitPlayerDeclaration,
                false),
            ConfiguredEndpointRouteKind.ExplicitRemoteHttps => (
                ProviderUiGroup.LocalAndSelfHosted,
                DeploymentOwnership.SelfHosted,
                ConnectionRouteLocality.PublicRemote,
                EndpointExposureState.PublicExposedDeclared,
                ProviderTrustClass.ExplicitRemoteCustom,
                ExecutionVenue.SelfHostedRemote,
                ProviderExecutionVenueProvenance.ExplicitPlayerDeclaration,
                false),
            _ => throw new ArgumentOutOfRangeException(nameof(routeIntent))
        };
    }
}
