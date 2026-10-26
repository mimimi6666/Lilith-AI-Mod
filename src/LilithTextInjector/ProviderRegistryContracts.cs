using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum ProviderUiGroup
{
    Invalid = 0,
    Cloud = 1,
    LocalAndSelfHosted = 2
}

internal enum DeploymentOwnership
{
    Invalid = 0,
    ManagedCloud = 1,
    SelfHosted = 2
}

internal enum ConnectionRouteLocality
{
    Invalid = 0,
    Loopback = 1,
    Lan = 2,
    PublicRemote = 3
}

internal enum EndpointExposureState
{
    Invalid = 0,
    LoopbackOnlyVerified = 1,
    LanExposedDeclared = 2,
    PublicExposedDeclared = 3,
    Unknown = 4
}

internal enum ExecutionVenue
{
    Invalid = 0,
    LocalProcess = 1,
    SelfHostedRemote = 2,
    ManagedRemote = 3,
    Unknown = 4
}

internal enum ProcessingLocalityRequirement
{
    Invalid = 0,
    LocalProcessRequired = 1,
    SelfHostedAllowed = 2,
    ExternalProcessingAllowed = 3
}

internal enum ProviderKind
{
    Invalid = 0,
    Gemini = 1,
    OpenAI = 2,
    AnthropicClaude = 3,
    DeepSeek = 4,
    QwenDashScope = 5,
    Ollama = 6,
    LlamaCpp = 7,
    LMStudio = 8,
    Vllm = 9,
    LocalAI = 10,
    CustomOpenAICompatible = 11,
    OpenRouter = 12,
    OllamaCloud = 13,
    OllamaLocal = 14
}

internal enum ProviderProtocolKind
{
    Invalid = 0,
    GeminiGenerateContent = 1,
    GeminiInteractions = 2,
    OpenAiChatCompletions = 3,
    OpenAiResponses = 4,
    AnthropicMessages = 5,
    OllamaNativeChat = 6,
    DashScopeNativeTextGeneration = 7
}

internal enum ProviderTrustClass
{
    Invalid = 0,
    OfficialCloudHttps = 1,
    LoopbackRouteAdmitted = 2,
    ExplicitLan = 3,
    ExplicitRemoteCustom = 4
}

internal enum CatalogFactKind
{
    Invalid = 0,
    Installed = 1,
    Loaded = 2,
    Served = 3,
    ServerVisible = 4,
    Deployable = 5,
    GalleryAvailable = 6
}

internal enum ProviderCapability
{
    Invalid = 0,
    TextGeneration = 1,
    SystemInstruction = 2,
    OrderedHistory = 3,
    ClientRequestAbort = 4,
    LateResultSuppression = 5,
    UsageMetadata = 6,
    StructuredOutput = 7,
    Tools = 8,
    Reasoning = 9,
    Vision = 10,
    Audio = 11,
    Embeddings = 12,
    ProviderCancelOperation = 13
}

internal enum ProviderAuthenticationScheme
{
    Invalid = 0,
    None = 1,
    GoogleApiKey = 2,
    Bearer = 3,
    AnthropicApiKey = 4,
    CustomHeaderReference = 5
}

internal enum ProviderTransportIdentityClass
{
    Invalid = 0,
    PublicModelId = 1,
    SensitiveLocalIdentifier = 2
}

internal enum ProviderCatalogFactProvenance
{
    Invalid = 0,
    OfficialCatalog = 1,
    ProviderEndpoint = 2,
    ExplicitConfiguration = 3,
    LocalRuntime = 4
}

internal enum ProviderExecutionVenueProvenance
{
    Invalid = 0,
    VerifiedLocalRuntime = 1,
    ExplicitPlayerDeclaration = 2,
    OfficialProvider = 3,
    Unknown = 4
}

internal enum ProviderEvidenceFreshness
{
    Invalid = 0,
    Current = 1,
    Stale = 2,
    Unknown = 3
}

internal enum ProviderCapabilityEvidenceState
{
    Invalid = 0,
    Unknown = 1,
    Unsupported = 2,
    Supported = 3
}

internal enum ProviderCapabilityAuthorizationState
{
    Invalid = 0,
    NotAuthorized = 1,
    Authorized = 2
}

internal enum ProviderRegistryBuildAdmission
{
    Accepted = 0,
    UnsupportedRegistryVersion = 1,
    InvalidGeneration = 2,
    CapacityExceeded = 3,
    DuplicateDefinitionId = 4,
    DuplicateInstanceId = 5,
    UnknownDefinitionId = 6,
    DefinitionKindMismatch = 7,
    ProtocolNotDeclared = 8,
    DuplicateModelIdentity = 9,
    UnknownModelInstance = 10,
    CredentialBindingMismatch = 11,
    StageACapabilityAuthorizationExceeded = 12
}

internal enum ProviderIdentityEvidenceKind
{
    Invalid = 0,
    OfficialWellKnownEndpoint = 1,
    ProviderSpecificSchema = 2,
    ExplicitPlayerConfirmation = 3,
    GenericOpenAiModelsEndpoint = 4
}

internal enum ProviderIdentityAdmission
{
    Invalid = 0,
    AcceptedOfficialWellKnown = 1,
    AcceptedProviderSpecificSchema = 2,
    AcceptedExplicitPlayerConfirmation = 3,
    InsufficientGenericCompatibilityEvidence = 4,
    ConflictingEvidence = 5
}

internal enum ProviderDiscoveryBudgetAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    WireBytesExceeded = 2,
    DecompressedBytesExceeded = 3,
    JsonDepthExceeded = 4,
    ModelCountExceeded = 5,
    PageCountExceeded = 6,
    StringLengthExceeded = 7,
    ConnectionBudgetExceeded = 8
}

internal static class ProviderRegistryContractGuard
{
    internal const int MaximumIdentifierLength = 128;
    internal const int MaximumDefinitions = 32;
    internal const int MaximumInstances = 128;
    internal const int MaximumModels = 4096;
    internal const int MaximumProtocolsPerDefinition = 8;
    internal const int MaximumFactsPerModel = 64;
    internal const int RegistrySchemaVersion = 1;

    internal static string Identifier(
        string? value,
        string parameterName)
    {
        var result = DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);
        if (result.IndexOf('/') >= 0
            || result.IndexOf('\\') >= 0
            || result.IndexOf(':') >= 0)
        {
            throw new ArgumentException(
                "Opaque identifiers cannot contain path or URI separators.",
                parameterName);
        }
        return result;
    }

    internal static string SafeDisplayAlias(
        string? value,
        string parameterName)
    {
        var result = DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);
        if (result.StartsWith("/", StringComparison.Ordinal)
            || result.StartsWith("\\", StringComparison.Ordinal)
            || result.Contains(":\\", StringComparison.Ordinal)
            || result.Contains(":/", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Display aliases cannot expose a local or URI path.",
                parameterName);
        }
        return result;
    }

    internal static string Sha256(
        string? value,
        string parameterName)
        => LivingContinuityContractGuard.Sha256(value, parameterName)
            .ToUpperInvariant();

    internal static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)
            || Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }
}

internal abstract class ProviderOrdinalIdentity
{
    internal string Value { get; }

    protected ProviderOrdinalIdentity(string value, string parameterName)
    {
        Value = ProviderRegistryContractGuard.Identifier(
            value,
            parameterName);
    }

    public sealed override string ToString() => Value;

    public sealed override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public sealed override bool Equals(object? obj)
        => obj != null
            && obj.GetType() == GetType()
            && string.Equals(
                Value,
                ((ProviderOrdinalIdentity)obj).Value,
                StringComparison.Ordinal);
}

internal sealed class ProviderDefinitionId :
    ProviderOrdinalIdentity,
    IEquatable<ProviderDefinitionId>
{
    internal ProviderDefinitionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderDefinitionId? other)
        => Equals((object?)other);
}

internal sealed class ProviderEndpointInstanceId :
    ProviderOrdinalIdentity,
    IEquatable<ProviderEndpointInstanceId>
{
    internal ProviderEndpointInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderEndpointInstanceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderModelInstanceId :
    ProviderOrdinalIdentity,
    IEquatable<ProviderModelInstanceId>
{
    internal ProviderModelInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderModelInstanceId? other)
        => Equals((object?)other);
}

internal sealed class ProviderLoadedInstanceId :
    ProviderOrdinalIdentity,
    IEquatable<ProviderLoadedInstanceId>
{
    internal ProviderLoadedInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ProviderLoadedInstanceId? other)
        => Equals((object?)other);
}

internal sealed class CredentialReferenceId :
    ProviderOrdinalIdentity,
    IEquatable<CredentialReferenceId>
{
    internal CredentialReferenceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(CredentialReferenceId? other)
        => Equals((object?)other);
}

internal sealed class OpaqueTransportModelReference :
    ProviderOrdinalIdentity,
    IEquatable<OpaqueTransportModelReference>
{
    internal OpaqueTransportModelReference(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(OpaqueTransportModelReference? other)
        => Equals((object?)other);
}

internal static class ProviderRegistryCanonicalHash
{
    internal static string Of(
        string domain,
        params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Hash domain is required.", nameof(domain));
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var material = new StringBuilder();
        Append(material, domain);
        for (var index = 0; index < values.Length; index++)
            Append(material, values[index] ?? string.Empty);

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(
            Encoding.UTF8.GetBytes(material.ToString()));
        return Convert.ToHexString(bytes);
    }

    internal static string Number(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    internal static string Number(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    internal static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().Ticks.ToString(
            CultureInfo.InvariantCulture);

    private static void Append(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
    }
}

internal sealed class ProviderIdentityEvidence
{
    internal ProviderIdentityEvidenceKind Kind { get; }
    internal ProviderKind ClaimedProvider { get; }
    internal string EvidenceFingerprint { get; }

    internal ProviderIdentityEvidence(
        ProviderIdentityEvidenceKind kind,
        ProviderKind claimedProvider,
        string evidenceFingerprint)
    {
        Kind = ProviderRegistryContractGuard.Defined(kind, nameof(kind));
        ClaimedProvider = ProviderRegistryContractGuard.Defined(
            claimedProvider,
            nameof(claimedProvider));
        EvidenceFingerprint = ProviderRegistryContractGuard.Sha256(
            evidenceFingerprint,
            nameof(evidenceFingerprint));
    }
}

internal sealed class ProviderIdentityAssessment
{
    internal ProviderIdentityAdmission Admission { get; }
    internal ProviderKind? AdmittedProvider { get; }
    internal bool Accepted =>
        Admission
            is ProviderIdentityAdmission.AcceptedOfficialWellKnown
                or ProviderIdentityAdmission.AcceptedProviderSpecificSchema
                or ProviderIdentityAdmission
                    .AcceptedExplicitPlayerConfirmation;
    internal bool GrantsTransmissionAuthority => false;

    private ProviderIdentityAssessment(
        ProviderIdentityAdmission admission,
        ProviderKind? admittedProvider)
    {
        if (!Enum.IsDefined(typeof(ProviderIdentityAdmission), admission)
            || admission == ProviderIdentityAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        var accepted =
            admission
                is ProviderIdentityAdmission.AcceptedOfficialWellKnown
                    or ProviderIdentityAdmission
                        .AcceptedProviderSpecificSchema
                    or ProviderIdentityAdmission
                        .AcceptedExplicitPlayerConfirmation;
        if (accepted != admittedProvider.HasValue)
        {
            throw new ArgumentException(
                "Only accepted identity evidence may carry a Provider.");
        }
        Admission = admission;
        AdmittedProvider = admittedProvider;
    }

    internal static ProviderIdentityAssessment Evaluate(
        IReadOnlyList<ProviderIdentityEvidence> evidence)
    {
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));
        if (evidence.Count == 0 || evidence.Any(item => item == null))
        {
            throw new ArgumentException(
                "At least one identity evidence item is required.",
                nameof(evidence));
        }
        var authoritative = evidence
            .Where(
                item => item.Kind
                    != ProviderIdentityEvidenceKind
                        .GenericOpenAiModelsEndpoint)
            .ToArray();
        if (authoritative.Length == 0)
        {
            return new ProviderIdentityAssessment(
                ProviderIdentityAdmission
                    .InsufficientGenericCompatibilityEvidence,
                null);
        }
        var providers = authoritative
            .Select(item => item.ClaimedProvider)
            .Distinct()
            .ToArray();
        if (providers.Length != 1)
        {
            return new ProviderIdentityAssessment(
                ProviderIdentityAdmission.ConflictingEvidence,
                null);
        }
        var strongest = authoritative.Min(item => (int)item.Kind);
        var admission = (ProviderIdentityEvidenceKind)strongest switch
        {
            ProviderIdentityEvidenceKind.OfficialWellKnownEndpoint =>
                ProviderIdentityAdmission.AcceptedOfficialWellKnown,
            ProviderIdentityEvidenceKind.ProviderSpecificSchema =>
                ProviderIdentityAdmission.AcceptedProviderSpecificSchema,
            ProviderIdentityEvidenceKind.ExplicitPlayerConfirmation =>
                ProviderIdentityAdmission
                    .AcceptedExplicitPlayerConfirmation,
            _ => throw new InvalidOperationException(
                "Unsupported identity evidence precedence.")
        };
        return new ProviderIdentityAssessment(admission, providers[0]);
    }
}

internal sealed class ProviderDiscoveryResourceBudget
{
    internal const int MaximumWireBytes = 1_048_576;
    internal const int MaximumDecompressedBytes = 4_194_304;
    internal const int MaximumJsonDepth = 64;
    internal const int MaximumModelCount = 4096;
    internal const int MaximumPageCount = 32;
    internal const int MaximumStringLength = 2048;
    internal const int MaximumConnectionCount = 8;

    internal static ProviderDiscoveryResourceBudget StageAContract { get; } =
        new();

    private ProviderDiscoveryResourceBudget()
    {
    }

    internal ProviderDiscoveryBudgetAdmission Evaluate(
        long wireBytes,
        long decompressedBytes,
        int jsonDepth,
        int modelCount,
        int pageCount,
        int maximumObservedStringLength,
        int connectionCount)
    {
        if (wireBytes < 0
            || decompressedBytes < 0
            || jsonDepth < 0
            || modelCount < 0
            || pageCount < 0
            || maximumObservedStringLength < 0
            || connectionCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                "Discovery observations cannot be negative.");
        }
        if (wireBytes > MaximumWireBytes)
            return ProviderDiscoveryBudgetAdmission.WireBytesExceeded;
        if (decompressedBytes > MaximumDecompressedBytes)
        {
            return ProviderDiscoveryBudgetAdmission
                .DecompressedBytesExceeded;
        }
        if (jsonDepth > MaximumJsonDepth)
            return ProviderDiscoveryBudgetAdmission.JsonDepthExceeded;
        if (modelCount > MaximumModelCount)
            return ProviderDiscoveryBudgetAdmission.ModelCountExceeded;
        if (pageCount > MaximumPageCount)
            return ProviderDiscoveryBudgetAdmission.PageCountExceeded;
        if (maximumObservedStringLength > MaximumStringLength)
            return ProviderDiscoveryBudgetAdmission.StringLengthExceeded;
        if (connectionCount > MaximumConnectionCount)
        {
            return ProviderDiscoveryBudgetAdmission
                .ConnectionBudgetExceeded;
        }
        return ProviderDiscoveryBudgetAdmission.AcceptedForContractReplay;
    }
}

internal sealed class ProviderDefinition
{
    internal ProviderDefinitionId DefinitionId { get; }
    internal ProviderKind Kind { get; }
    internal IReadOnlyList<ProviderProtocolKind> DeclaredProtocols { get; }
    internal string Fingerprint { get; }

    internal ProviderDefinition(
        ProviderDefinitionId definitionId,
        ProviderKind kind,
        IReadOnlyList<ProviderProtocolKind> declaredProtocols)
    {
        DefinitionId = definitionId
            ?? throw new ArgumentNullException(nameof(definitionId));
        Kind = ProviderRegistryContractGuard.Defined(kind, nameof(kind));
        if (declaredProtocols == null)
            throw new ArgumentNullException(nameof(declaredProtocols));
        if (declaredProtocols.Count == 0
            || declaredProtocols.Count
                > ProviderRegistryContractGuard.MaximumProtocolsPerDefinition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(declaredProtocols));
        }

        var protocols = declaredProtocols
            .Select(
                value => ProviderRegistryContractGuard.Defined(
                    value,
                    nameof(declaredProtocols)))
            .OrderBy(value => (int)value)
            .ToArray();
        if (protocols.Distinct().Count() != protocols.Length)
        {
            throw new ArgumentException(
                "Provider protocols must be unique.",
                nameof(declaredProtocols));
        }
        DeclaredProtocols =
            new ReadOnlyCollection<ProviderProtocolKind>(protocols);
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-DEFINITION/1",
            DefinitionId.Value,
            ProviderRegistryCanonicalHash.Number((int)Kind),
            string.Join(
                ",",
                protocols.Select(
                    value => ProviderRegistryCanonicalHash.Number(
                        (int)value))));
    }

    internal bool Declares(ProviderProtocolKind protocol)
        => DeclaredProtocols.Contains(protocol);
}

internal sealed class CredentialReference
{
    internal CredentialReferenceId ReferenceId { get; }
    internal ProviderEndpointInstanceId BoundInstanceId { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal string Fingerprint { get; }

    internal CredentialReference(
        CredentialReferenceId referenceId,
        ProviderEndpointInstanceId boundInstanceId,
        ProviderAuthenticationScheme authenticationScheme)
    {
        ReferenceId = referenceId
            ?? throw new ArgumentNullException(nameof(referenceId));
        BoundInstanceId = boundInstanceId
            ?? throw new ArgumentNullException(nameof(boundInstanceId));
        AuthenticationScheme = ProviderRegistryContractGuard.Defined(
            authenticationScheme,
            nameof(authenticationScheme));
        if (AuthenticationScheme == ProviderAuthenticationScheme.None)
        {
            throw new ArgumentException(
                "A credential reference cannot use the None scheme.",
                nameof(authenticationScheme));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CREDENTIAL-REFERENCE/1",
            ReferenceId.Value,
            BoundInstanceId.Value,
            ProviderRegistryCanonicalHash.Number(
                (int)AuthenticationScheme));
    }
}

internal sealed class ProviderEndpointInstance
{
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderDefinitionId DefinitionId { get; }
    internal ProviderKind Kind { get; }
    internal ProviderUiGroup UiGroup { get; }
    internal DeploymentOwnership DeploymentOwnership { get; }
    internal ConnectionRouteLocality ConnectionRouteLocality { get; }
    internal EndpointExposureState ExposureState { get; }
    internal ProviderEndpointUri BaseUri { get; }
    internal ProviderProtocolKind Protocol { get; }
    internal int ProtocolProfileVersion { get; }
    internal ProviderAuthenticationScheme AuthenticationScheme { get; }
    internal CredentialReference? CredentialReference { get; }
    internal ProviderTrustClass TrustClass { get; }
    internal bool Enabled { get; }
    internal long ConfigurationGeneration { get; }
    internal string EndpointFingerprint => BaseUri.Fingerprint;
    internal string ConfigurationFingerprint { get; }
    internal string Fingerprint { get; }

    internal ProviderEndpointInstance(
        ProviderEndpointInstanceId instanceId,
        ProviderDefinitionId definitionId,
        ProviderKind kind,
        ProviderUiGroup uiGroup,
        DeploymentOwnership deploymentOwnership,
        ConnectionRouteLocality connectionRouteLocality,
        EndpointExposureState exposureState,
        ProviderEndpointUri baseUri,
        ProviderProtocolKind protocol,
        int protocolProfileVersion,
        ProviderAuthenticationScheme authenticationScheme,
        CredentialReference? credentialReference,
        ProviderTrustClass trustClass,
        bool enabled,
        long configurationGeneration)
    {
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        DefinitionId = definitionId
            ?? throw new ArgumentNullException(nameof(definitionId));
        Kind = ProviderRegistryContractGuard.Defined(kind, nameof(kind));
        UiGroup = ProviderRegistryContractGuard.Defined(
            uiGroup,
            nameof(uiGroup));
        DeploymentOwnership = ProviderRegistryContractGuard.Defined(
            deploymentOwnership,
            nameof(deploymentOwnership));
        ConnectionRouteLocality = ProviderRegistryContractGuard.Defined(
            connectionRouteLocality,
            nameof(connectionRouteLocality));
        ExposureState = ProviderRegistryContractGuard.Defined(
            exposureState,
            nameof(exposureState));
        BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        Protocol = ProviderRegistryContractGuard.Defined(
            protocol,
            nameof(protocol));
        if (protocolProfileVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolProfileVersion));
        }
        ProtocolProfileVersion = protocolProfileVersion;
        AuthenticationScheme = ProviderRegistryContractGuard.Defined(
            authenticationScheme,
            nameof(authenticationScheme));
        TrustClass = ProviderRegistryContractGuard.Defined(
            trustClass,
            nameof(trustClass));
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        if (AuthenticationScheme == ProviderAuthenticationScheme.None)
        {
            if (credentialReference != null)
            {
                throw new ArgumentException(
                    "Unauthenticated instances cannot carry credentials.",
                    nameof(credentialReference));
            }
        }
        else
        {
            if (credentialReference == null
                || !credentialReference.BoundInstanceId.Equals(InstanceId)
                || credentialReference.AuthenticationScheme
                    != AuthenticationScheme)
            {
                throw new ArgumentException(
                    "Credential reference must match the instance and scheme.",
                    nameof(credentialReference));
            }
        }

        CredentialReference = credentialReference;
        Enabled = enabled;
        ConfigurationGeneration = configurationGeneration;
        ConfigurationFingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-INSTANCE-CONFIGURATION/1",
            InstanceId.Value,
            DefinitionId.Value,
            ProviderRegistryCanonicalHash.Number((int)Kind),
            ProviderRegistryCanonicalHash.Number((int)UiGroup),
            ProviderRegistryCanonicalHash.Number((int)DeploymentOwnership),
            ProviderRegistryCanonicalHash.Number((int)ConnectionRouteLocality),
            ProviderRegistryCanonicalHash.Number((int)ExposureState),
            EndpointFingerprint,
            ProviderRegistryCanonicalHash.Number((int)Protocol),
            ProviderRegistryCanonicalHash.Number(protocolProfileVersion),
            ProviderRegistryCanonicalHash.Number((int)AuthenticationScheme),
            CredentialReference?.Fingerprint ?? string.Empty,
            ProviderRegistryCanonicalHash.Number((int)TrustClass),
            Enabled ? "1" : "0");
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/PROVIDER-INSTANCE/1",
            ConfigurationFingerprint,
            ProviderRegistryCanonicalHash.Number(ConfigurationGeneration));
    }
}

internal sealed class ProviderEvidenceStamp
{
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset AssessedAt { get; }
    internal DateTimeOffset? ValidUntil { get; }
    internal ProviderEvidenceFreshness Freshness { get; }
    internal string Fingerprint { get; }

    internal ProviderEvidenceStamp(
        DateTimeOffset observedAt,
        DateTimeOffset assessedAt,
        DateTimeOffset? validUntil,
        ProviderEvidenceFreshness freshness)
    {
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        AssessedAt = DecisionCoreContractGuard.Timestamp(
            assessedAt,
            nameof(assessedAt));
        if (AssessedAt < ObservedAt)
            throw new ArgumentOutOfRangeException(nameof(assessedAt));
        if (validUntil.HasValue)
        {
            ValidUntil = DecisionCoreContractGuard.Timestamp(
                validUntil.Value,
                nameof(validUntil));
            if (ValidUntil <= AssessedAt)
                throw new ArgumentOutOfRangeException(nameof(validUntil));
        }
        Freshness = ProviderRegistryContractGuard.Defined(
            freshness,
            nameof(freshness));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/EVIDENCE-STAMP/1",
            ProviderRegistryCanonicalHash.Timestamp(ObservedAt),
            ProviderRegistryCanonicalHash.Timestamp(AssessedAt),
            ValidUntil.HasValue
                ? ProviderRegistryCanonicalHash.Timestamp(ValidUntil.Value)
                : string.Empty,
            ProviderRegistryCanonicalHash.Number((int)Freshness));
    }
}

internal sealed class ProviderCatalogFact
{
    internal CatalogFactKind Kind { get; }
    internal ProviderCatalogFactProvenance Provenance { get; }
    internal ProviderEvidenceStamp Evidence { get; }
    internal ProviderLoadedInstanceId? LoadedInstanceIdentity { get; }
    internal string Fingerprint { get; }

    internal ProviderCatalogFact(
        CatalogFactKind kind,
        ProviderCatalogFactProvenance provenance,
        ProviderEvidenceStamp evidence,
        ProviderLoadedInstanceId? loadedInstanceIdentity = null)
    {
        Kind = ProviderRegistryContractGuard.Defined(kind, nameof(kind));
        Provenance = ProviderRegistryContractGuard.Defined(
            provenance,
            nameof(provenance));
        Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        if (Kind == CatalogFactKind.Loaded)
        {
            LoadedInstanceIdentity = loadedInstanceIdentity
                ?? throw new ArgumentNullException(
                    nameof(loadedInstanceIdentity));
        }
        else if (loadedInstanceIdentity != null)
        {
            throw new ArgumentException(
                "Only Loaded facts may carry a loaded-instance identity.",
                nameof(loadedInstanceIdentity));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CATALOG-FACT/1",
            ProviderRegistryCanonicalHash.Number((int)Kind),
            ProviderRegistryCanonicalHash.Number((int)Provenance),
            Evidence.Fingerprint,
            LoadedInstanceIdentity?.Value ?? string.Empty);
    }
}

internal sealed class ProviderCapabilityStatus
{
    internal ProviderCapability Capability { get; }
    internal ProviderCapabilityEvidenceState Advertised { get; }
    internal ProviderCapabilityEvidenceState Observed { get; }
    internal ProviderCapabilityAuthorizationState Authorized { get; }
    internal string Fingerprint { get; }

    internal ProviderCapabilityStatus(
        ProviderCapability capability,
        ProviderCapabilityEvidenceState advertised,
        ProviderCapabilityEvidenceState observed,
        ProviderCapabilityAuthorizationState authorized)
    {
        Capability = ProviderRegistryContractGuard.Defined(
            capability,
            nameof(capability));
        Advertised = ProviderRegistryContractGuard.Defined(
            advertised,
            nameof(advertised));
        Observed = ProviderRegistryContractGuard.Defined(
            observed,
            nameof(observed));
        Authorized = ProviderRegistryContractGuard.Defined(
            authorized,
            nameof(authorized));
        if (Authorized
                == ProviderCapabilityAuthorizationState.Authorized
            && (Advertised
                    == ProviderCapabilityEvidenceState.Unsupported
                || Observed
                    == ProviderCapabilityEvidenceState.Unsupported))
        {
            throw new ArgumentException(
                "An unsupported capability cannot be authorized.",
                nameof(authorized));
        }
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CAPABILITY-STATUS/1",
            ProviderRegistryCanonicalHash.Number((int)Capability),
            ProviderRegistryCanonicalHash.Number((int)Advertised),
            ProviderRegistryCanonicalHash.Number((int)Observed),
            ProviderRegistryCanonicalHash.Number((int)Authorized));
    }
}

internal sealed class ProviderCapabilityProfile
{
    private static readonly ProviderCapability[] AllCapabilities =
        Enum.GetValues<ProviderCapability>()
            .Where(value => value != ProviderCapability.Invalid)
            .OrderBy(value => (int)value)
            .ToArray();

    internal IReadOnlyList<ProviderCapabilityStatus> Statuses { get; }
    internal string Fingerprint { get; }
    internal bool StageAAuthorityCompliant { get; }

    internal ProviderCapabilityProfile(
        IReadOnlyList<ProviderCapabilityStatus> statuses)
    {
        if (statuses == null)
            throw new ArgumentNullException(nameof(statuses));
        if (statuses.Any(item => item == null))
            throw new ArgumentException("Capability status cannot be null.", nameof(statuses));

        var copied = statuses
            .OrderBy(item => (int)item.Capability)
            .ToArray();
        if (copied.Length != AllCapabilities.Length
            || copied.Select(item => item.Capability).Distinct().Count()
                != copied.Length
            || !copied.Select(item => item.Capability)
                .SequenceEqual(AllCapabilities))
        {
            throw new ArgumentException(
                "Capability profiles must contain exactly one status for every capability.",
                nameof(statuses));
        }
        Statuses =
            new ReadOnlyCollection<ProviderCapabilityStatus>(copied);
        StageAAuthorityCompliant = copied
            .Where(
                item => item.Authorized
                    == ProviderCapabilityAuthorizationState.Authorized)
            .All(
                item => item.Capability
                    is ProviderCapability.TextGeneration
                        or ProviderCapability.SystemInstruction
                        or ProviderCapability.OrderedHistory
                        or ProviderCapability.ClientRequestAbort
                        or ProviderCapability.LateResultSuppression
                        or ProviderCapability.UsageMetadata);
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/CAPABILITY-PROFILE/1",
            copied.Select(item => item.Fingerprint).ToArray());
    }

    internal bool IsAuthorized(ProviderCapability capability)
    {
        ProviderRegistryContractGuard.Defined(capability, nameof(capability));
        return Statuses[(int)capability - 1].Authorized
            == ProviderCapabilityAuthorizationState.Authorized;
    }
}

internal sealed class ProviderModelDescriptor
{
    internal ProviderEndpointInstanceId InstanceId { get; }
    internal ProviderModelInstanceId ModelInstanceId { get; }
    internal string SafeDisplayAlias { get; }
    internal string CorrelationHash { get; }
    internal ProviderTransportIdentityClass TransportIdentityClass { get; }
    internal OpaqueTransportModelReference TransportReference { get; }
    internal IReadOnlyList<ProviderCatalogFact> CatalogFacts { get; }
    internal ProviderCapabilityProfile CapabilityProfile { get; }
    internal ExecutionVenue ExecutionVenue { get; }
    internal ProviderExecutionVenueProvenance ExecutionVenueProvenance { get; }
    internal ProviderEvidenceStamp ExecutionVenueEvidence { get; }
    internal string Fingerprint { get; }

    internal ProviderModelDescriptor(
        ProviderEndpointInstanceId instanceId,
        ProviderModelInstanceId modelInstanceId,
        string safeDisplayAlias,
        string correlationHash,
        ProviderTransportIdentityClass transportIdentityClass,
        OpaqueTransportModelReference transportReference,
        IReadOnlyList<ProviderCatalogFact> catalogFacts,
        ProviderCapabilityProfile capabilityProfile,
        ExecutionVenue executionVenue,
        ProviderExecutionVenueProvenance executionVenueProvenance,
        ProviderEvidenceStamp executionVenueEvidence)
    {
        InstanceId = instanceId
            ?? throw new ArgumentNullException(nameof(instanceId));
        ModelInstanceId = modelInstanceId
            ?? throw new ArgumentNullException(nameof(modelInstanceId));
        SafeDisplayAlias = ProviderRegistryContractGuard.SafeDisplayAlias(
            safeDisplayAlias,
            nameof(safeDisplayAlias));
        CorrelationHash = ProviderRegistryContractGuard.Sha256(
            correlationHash,
            nameof(correlationHash));
        TransportIdentityClass = ProviderRegistryContractGuard.Defined(
            transportIdentityClass,
            nameof(transportIdentityClass));
        TransportReference = transportReference
            ?? throw new ArgumentNullException(nameof(transportReference));
        if (catalogFacts == null)
            throw new ArgumentNullException(nameof(catalogFacts));
        if (catalogFacts.Count > ProviderRegistryContractGuard.MaximumFactsPerModel
            || catalogFacts.Any(item => item == null))
        {
            throw new ArgumentOutOfRangeException(nameof(catalogFacts));
        }
        var facts = catalogFacts
            .OrderBy(item => (int)item.Kind)
            .ThenBy(
                item => item.LoadedInstanceIdentity?.Value ?? string.Empty,
                StringComparer.Ordinal)
            .ThenBy(item => item.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        if (facts.Select(item => item.Fingerprint).Distinct(
                StringComparer.Ordinal).Count() != facts.Length)
        {
            throw new ArgumentException(
                "Catalog facts must be unique.",
                nameof(catalogFacts));
        }
        CatalogFacts = new ReadOnlyCollection<ProviderCatalogFact>(facts);
        CapabilityProfile = capabilityProfile
            ?? throw new ArgumentNullException(nameof(capabilityProfile));
        ExecutionVenue = ProviderRegistryContractGuard.Defined(
            executionVenue,
            nameof(executionVenue));
        ExecutionVenueProvenance = ProviderRegistryContractGuard.Defined(
            executionVenueProvenance,
            nameof(executionVenueProvenance));
        ExecutionVenueEvidence = executionVenueEvidence
            ?? throw new ArgumentNullException(nameof(executionVenueEvidence));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/MODEL-DESCRIPTOR/1",
            InstanceId.Value,
            ModelInstanceId.Value,
            SafeDisplayAlias,
            CorrelationHash,
            ProviderRegistryCanonicalHash.Number(
                (int)TransportIdentityClass),
            TransportReference.Value,
            ProviderRegistryCanonicalHash.Of(
                "D027/MODEL-CATALOG/1",
                facts.Select(item => item.Fingerprint).ToArray()),
            CapabilityProfile.Fingerprint,
            ProviderRegistryCanonicalHash.Number((int)ExecutionVenue),
            ProviderRegistryCanonicalHash.Number(
                (int)ExecutionVenueProvenance),
            ExecutionVenueEvidence.Fingerprint);
    }
}

internal sealed class ProviderRegistryBuildResult
{
    internal ProviderRegistryBuildAdmission Admission { get; }
    internal ProviderRegistrySnapshot? Snapshot { get; }
    internal bool Accepted =>
        Admission == ProviderRegistryBuildAdmission.Accepted;

    private ProviderRegistryBuildResult(
        ProviderRegistryBuildAdmission admission,
        ProviderRegistrySnapshot? snapshot)
    {
        if (!Enum.IsDefined(
                typeof(ProviderRegistryBuildAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == ProviderRegistryBuildAdmission.Accepted)
            != (snapshot != null))
        {
            throw new ArgumentException(
                "Only accepted build results may carry a snapshot.",
                nameof(snapshot));
        }
        Admission = admission;
        Snapshot = snapshot;
    }

    internal static ProviderRegistryBuildResult Accept(
        ProviderRegistrySnapshot snapshot)
        => new(
            ProviderRegistryBuildAdmission.Accepted,
            snapshot ?? throw new ArgumentNullException(nameof(snapshot)));

    internal static ProviderRegistryBuildResult Reject(
        ProviderRegistryBuildAdmission admission)
    {
        if (admission == ProviderRegistryBuildAdmission.Accepted)
            throw new ArgumentOutOfRangeException(nameof(admission));
        return new ProviderRegistryBuildResult(admission, null);
    }
}

internal sealed class ProviderRegistrySnapshot
{
    internal int RegistryVersion { get; }
    internal long RegistryGeneration { get; }
    internal IReadOnlyList<ProviderDefinition> Definitions { get; }
    internal IReadOnlyList<ProviderEndpointInstance> Instances { get; }
    internal IReadOnlyList<ProviderModelDescriptor> Models { get; }
    internal string Fingerprint { get; }

    private ProviderRegistrySnapshot(
        int registryVersion,
        long registryGeneration,
        IReadOnlyList<ProviderDefinition> definitions,
        IReadOnlyList<ProviderEndpointInstance> instances,
        IReadOnlyList<ProviderModelDescriptor> models)
    {
        RegistryVersion = registryVersion;
        RegistryGeneration = registryGeneration;
        Definitions = definitions;
        Instances = instances;
        Models = models;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/REGISTRY-SNAPSHOT/1",
            ProviderRegistryCanonicalHash.Number(registryVersion),
            ProviderRegistryCanonicalHash.Number(registryGeneration),
            ProviderRegistryCanonicalHash.Of(
                "D027/DEFINITIONS/1",
                definitions.Select(item => item.Fingerprint).ToArray()),
            ProviderRegistryCanonicalHash.Of(
                "D027/INSTANCES/1",
                instances.Select(item => item.Fingerprint).ToArray()),
            ProviderRegistryCanonicalHash.Of(
                "D027/MODELS/1",
                models.Select(item => item.Fingerprint).ToArray()));
    }

    internal static ProviderRegistryBuildResult Create(
        int registryVersion,
        long generation,
        IReadOnlyList<ProviderDefinition> definitions,
        IReadOnlyList<ProviderEndpointInstance> instances,
        IReadOnlyList<ProviderModelDescriptor> models)
    {
        if (definitions == null)
            throw new ArgumentNullException(nameof(definitions));
        if (instances == null)
            throw new ArgumentNullException(nameof(instances));
        if (models == null)
            throw new ArgumentNullException(nameof(models));
        if (registryVersion
            != ProviderRegistryContractGuard.RegistrySchemaVersion)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.UnsupportedRegistryVersion);
        }
        if (generation <= 0)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.InvalidGeneration);
        }
        if (definitions.Count > ProviderRegistryContractGuard.MaximumDefinitions
            || instances.Count > ProviderRegistryContractGuard.MaximumInstances
            || models.Count > ProviderRegistryContractGuard.MaximumModels)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.CapacityExceeded);
        }
        if (definitions.Any(item => item == null)
            || instances.Any(item => item == null)
            || models.Any(item => item == null))
        {
            throw new ArgumentException(
                "Registry collections cannot contain null items.");
        }

        var orderedDefinitions = definitions
            .OrderBy(item => item.DefinitionId.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedDefinitions.Select(item => item.DefinitionId.Value)
            .Distinct(StringComparer.Ordinal).Count()
            != orderedDefinitions.Length)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.DuplicateDefinitionId);
        }
        var orderedInstances = instances
            .OrderBy(item => item.InstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedInstances.Select(item => item.InstanceId.Value)
            .Distinct(StringComparer.Ordinal).Count()
            != orderedInstances.Length)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.DuplicateInstanceId);
        }

        var definitionsById = orderedDefinitions.ToDictionary(
            item => item.DefinitionId.Value,
            StringComparer.Ordinal);
        foreach (var instance in orderedInstances)
        {
            if (!definitionsById.TryGetValue(
                    instance.DefinitionId.Value,
                    out var definition))
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission.UnknownDefinitionId);
            }
            if (definition.Kind != instance.Kind)
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission.DefinitionKindMismatch);
            }
            if (!definition.Declares(instance.Protocol))
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission.ProtocolNotDeclared);
            }
            if (instance.CredentialReference != null
                && (!instance.CredentialReference.BoundInstanceId.Equals(
                        instance.InstanceId)
                    || instance.CredentialReference.AuthenticationScheme
                        != instance.AuthenticationScheme))
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission.CredentialBindingMismatch);
            }
        }

        var instanceIds = orderedInstances
            .Select(item => item.InstanceId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var orderedModels = models
            .OrderBy(item => item.InstanceId.Value, StringComparer.Ordinal)
            .ThenBy(item => item.ModelInstanceId.Value, StringComparer.Ordinal)
            .ToArray();
        if (orderedModels
            .Select(
                item => string.Concat(
                    item.InstanceId.Value,
                    "\u001F",
                    item.ModelInstanceId.Value))
            .Distinct(StringComparer.Ordinal).Count()
            != orderedModels.Length)
        {
            return ProviderRegistryBuildResult.Reject(
                ProviderRegistryBuildAdmission.DuplicateModelIdentity);
        }
        foreach (var model in orderedModels)
        {
            if (!instanceIds.Contains(model.InstanceId.Value))
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission.UnknownModelInstance);
            }
            if (!model.CapabilityProfile.StageAAuthorityCompliant)
            {
                return ProviderRegistryBuildResult.Reject(
                    ProviderRegistryBuildAdmission
                        .StageACapabilityAuthorizationExceeded);
            }
        }

        return ProviderRegistryBuildResult.Accept(
            new ProviderRegistrySnapshot(
                registryVersion,
                generation,
                new ReadOnlyCollection<ProviderDefinition>(
                    orderedDefinitions),
                new ReadOnlyCollection<ProviderEndpointInstance>(
                    orderedInstances),
                new ReadOnlyCollection<ProviderModelDescriptor>(
                    orderedModels)));
    }

    internal bool TryGetDefinition(
        ProviderDefinitionId definitionId,
        out ProviderDefinition? definition)
    {
        if (definitionId == null)
            throw new ArgumentNullException(nameof(definitionId));
        definition = Definitions.FirstOrDefault(
            item => item.DefinitionId.Equals(definitionId));
        return definition != null;
    }

    internal bool TryGetInstance(
        ProviderEndpointInstanceId instanceId,
        out ProviderEndpointInstance? instance)
    {
        if (instanceId == null)
            throw new ArgumentNullException(nameof(instanceId));
        instance = Instances.FirstOrDefault(
            item => item.InstanceId.Equals(instanceId));
        return instance != null;
    }

    internal bool TryGetModel(
        ProviderEndpointInstanceId instanceId,
        ProviderModelInstanceId modelId,
        out ProviderModelDescriptor? model)
    {
        if (instanceId == null)
            throw new ArgumentNullException(nameof(instanceId));
        if (modelId == null)
            throw new ArgumentNullException(nameof(modelId));
        model = Models.FirstOrDefault(
            item => item.InstanceId.Equals(instanceId)
                && item.ModelInstanceId.Equals(modelId));
        return model != null;
    }
}
