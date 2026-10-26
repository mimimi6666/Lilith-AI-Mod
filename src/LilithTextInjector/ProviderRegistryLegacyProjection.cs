using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithTextInjector;

internal enum LegacyProviderRegistryAdmission
{
    Invalid = 0,
    Off = 1,
    Projected = 2,
    UnknownProvider = 3,
    InvalidEndpoint = 4,
    InvalidModel = 5,
    RegistryRejected = 6
}

internal enum LegacyModelMigrationState
{
    Invalid = 0,
    CurrentOrUnverified = 1,
    MigrationRequired = 2
}

internal sealed class LegacyProviderSettingBinding
{
    internal string SemanticId { get; }
    internal string Section { get; }
    internal string Key { get; }
    internal string Fingerprint { get; }

    internal LegacyProviderSettingBinding(
        string semanticId,
        string section,
        string key)
    {
        SemanticId = ProviderRegistryContractGuard.Identifier(
            semanticId,
            nameof(semanticId));
        Section = ProviderRegistryContractGuard.Identifier(
            section,
            nameof(section));
        Key = ProviderRegistryContractGuard.Identifier(key, nameof(key));
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/LEGACY-SETTING-BINDING/1",
            SemanticId,
            Section,
            Key);
    }
}

internal sealed class LegacyProviderRegistryInput
{
    internal bool MigrationGateEnabled { get; }
    internal string ActiveProvider { get; }
    internal string GeminiModel { get; }
    internal string OpenAiModel { get; }
    internal string DeepSeekModel { get; }
    internal string QwenModel { get; }
    internal string QwenBaseUrl { get; }
    internal bool GeminiCredentialConfigured { get; }
    internal bool OpenAiCredentialConfigured { get; }
    internal bool DeepSeekCredentialConfigured { get; }
    internal bool QwenCredentialConfigured { get; }
    internal long ConfigurationGeneration { get; }
    internal LegacyProviderExecutionSignature LegacyExecution { get; }

    internal LegacyProviderRegistryInput(
        bool migrationGateEnabled,
        string activeProvider,
        string geminiModel,
        string openAiModel,
        string deepSeekModel,
        string qwenModel,
        string qwenBaseUrl,
        bool geminiCredentialConfigured,
        bool openAiCredentialConfigured,
        bool deepSeekCredentialConfigured,
        bool qwenCredentialConfigured,
        long configurationGeneration,
        LegacyProviderExecutionSignature legacyExecution)
    {
        MigrationGateEnabled = migrationGateEnabled;
        ActiveProvider = Required(activeProvider, nameof(activeProvider));
        GeminiModel = Required(geminiModel, nameof(geminiModel));
        OpenAiModel = Required(openAiModel, nameof(openAiModel));
        DeepSeekModel = Required(deepSeekModel, nameof(deepSeekModel));
        QwenModel = Required(qwenModel, nameof(qwenModel));
        QwenBaseUrl = Required(qwenBaseUrl, nameof(qwenBaseUrl), 2048);
        if (configurationGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configurationGeneration));
        }
        GeminiCredentialConfigured = geminiCredentialConfigured;
        OpenAiCredentialConfigured = openAiCredentialConfigured;
        DeepSeekCredentialConfigured = deepSeekCredentialConfigured;
        QwenCredentialConfigured = qwenCredentialConfigured;
        ConfigurationGeneration = configurationGeneration;
        LegacyExecution = legacyExecution
            ?? throw new ArgumentNullException(nameof(legacyExecution));
    }

    private static string Required(
        string? value,
        string parameterName,
        int maximumLength = 128)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ArgumentException("A bounded value is required.", parameterName);
        return value.Trim();
    }
}

internal sealed class LegacyProviderExecutionSignature
{
    internal string EndpointBytesHash { get; }
    internal string ModelBytesHash { get; }
    internal string RequestBytesHash { get; }
    internal string CompletionBytesHash { get; }
    internal long ProviderCallCount { get; }
    internal long RngCursor { get; }
    internal long TimerTicks { get; }
    internal long SourceSequence { get; }
    internal string Fingerprint { get; }

    internal LegacyProviderExecutionSignature(
        string endpointBytesHash,
        string modelBytesHash,
        string requestBytesHash,
        string completionBytesHash,
        long providerCallCount,
        long rngCursor,
        long timerTicks,
        long sourceSequence)
    {
        EndpointBytesHash = ProviderRegistryContractGuard.Sha256(
            endpointBytesHash,
            nameof(endpointBytesHash));
        ModelBytesHash = ProviderRegistryContractGuard.Sha256(
            modelBytesHash,
            nameof(modelBytesHash));
        RequestBytesHash = ProviderRegistryContractGuard.Sha256(
            requestBytesHash,
            nameof(requestBytesHash));
        CompletionBytesHash = ProviderRegistryContractGuard.Sha256(
            completionBytesHash,
            nameof(completionBytesHash));
        if (providerCallCount < 0
            || rngCursor < 0
            || timerTicks < 0
            || sourceSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                "Legacy execution counters cannot be negative.");
        }
        ProviderCallCount = providerCallCount;
        RngCursor = rngCursor;
        TimerTicks = timerTicks;
        SourceSequence = sourceSequence;
        Fingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/LEGACY-EXECUTION-SIGNATURE/1",
            EndpointBytesHash,
            ModelBytesHash,
            RequestBytesHash,
            CompletionBytesHash,
            ProviderRegistryCanonicalHash.Number(ProviderCallCount),
            ProviderRegistryCanonicalHash.Number(RngCursor),
            ProviderRegistryCanonicalHash.Number(TimerTicks),
            ProviderRegistryCanonicalHash.Number(SourceSequence));
    }
}

internal sealed class LegacyProviderRegistryProjection
{
    internal ProviderRegistrySnapshot Snapshot { get; }
    internal ProviderEndpointInstanceId ActiveInstanceId { get; }
    internal ProviderModelInstanceId ActiveModelId { get; }
    internal LegacyModelMigrationState ActiveModelMigrationState { get; }
    internal IReadOnlyList<LegacyProviderSettingBinding> SourceBindings {
        get;
    }
    internal IReadOnlyDictionary<string, bool> CredentialPresence { get; }
    internal string SemanticFingerprint { get; }

    internal LegacyProviderRegistryProjection(
        ProviderRegistrySnapshot snapshot,
        ProviderEndpointInstanceId activeInstanceId,
        ProviderModelInstanceId activeModelId,
        LegacyModelMigrationState activeModelMigrationState,
        IReadOnlyList<LegacyProviderSettingBinding> sourceBindings,
        IReadOnlyDictionary<string, bool> credentialPresence)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ActiveInstanceId = activeInstanceId
            ?? throw new ArgumentNullException(nameof(activeInstanceId));
        ActiveModelId = activeModelId
            ?? throw new ArgumentNullException(nameof(activeModelId));
        ActiveModelMigrationState = ProviderRegistryContractGuard.Defined(
            activeModelMigrationState,
            nameof(activeModelMigrationState));
        SourceBindings = sourceBindings
            ?? throw new ArgumentNullException(nameof(sourceBindings));
        CredentialPresence = credentialPresence
            ?? throw new ArgumentNullException(nameof(credentialPresence));
        SemanticFingerprint = ProviderRegistryCanonicalHash.Of(
            "D027/LEGACY-REGISTRY-PROJECTION/1",
            Snapshot.Fingerprint,
            ActiveInstanceId.Value,
            ActiveModelId.Value,
            ProviderRegistryCanonicalHash.Number(
                (int)ActiveModelMigrationState),
            ProviderRegistryCanonicalHash.Of(
                "D027/LEGACY-BINDINGS/1",
                SourceBindings.Select(value => value.Fingerprint).ToArray()),
            ProviderRegistryCanonicalHash.Of(
                "D027/CREDENTIAL-PRESENCE/1",
                CredentialPresence.OrderBy(
                        value => value.Key,
                        StringComparer.Ordinal)
                    .Select(
                        value => value.Key + "="
                            + (value.Value ? "1" : "0"))
                    .ToArray()));
    }
}

internal sealed class LegacyProviderRegistryProjectionResult
{
    internal LegacyProviderRegistryAdmission Admission { get; }
    internal LegacyProviderRegistryProjection? Projection { get; }
    internal LegacyProviderExecutionSignature LegacyExecutionAfter { get; }
    internal ProviderRegistryStageBAuthorityCounters AuthorityCounters =>
        ProviderRegistryStageBAuthorityCounters.Zero;

    internal LegacyProviderRegistryProjectionResult(
        LegacyProviderRegistryAdmission admission,
        LegacyProviderRegistryProjection? projection,
        LegacyProviderExecutionSignature legacyExecutionAfter)
    {
        Admission = ProviderRegistryContractGuard.Defined(
            admission,
            nameof(admission));
        if ((admission == LegacyProviderRegistryAdmission.Projected)
            != (projection != null))
        {
            throw new ArgumentException(
                "Only projected results can carry a projection.",
                nameof(projection));
        }
        Projection = projection;
        LegacyExecutionAfter = legacyExecutionAfter
            ?? throw new ArgumentNullException(nameof(legacyExecutionAfter));
    }
}

internal static class LegacyProviderRegistryProjector
{
    private static readonly ReadOnlyCollection<LegacyProviderSettingBinding>
        Bindings = new(
            new[]
            {
                Binding("provider.active", "AI", "Provider"),
                Binding("gemini.model", "Gemini", "Model"),
                Binding("gemini.credential", "Gemini", "ApiKey"),
                Binding("openai.model", "OpenAI", "Model"),
                Binding("openai.credential", "OpenAI", "ApiKey"),
                Binding("deepseek.model", "DeepSeek", "Model"),
                Binding("deepseek.credential", "DeepSeek", "ApiKey"),
                Binding("qwen.model", "Qwen", "Model"),
                Binding("qwen.endpoint", "Qwen", "BaseUrl"),
                Binding("qwen.credential", "Qwen", "ApiKey")
            });

    internal static LegacyProviderRegistryProjectionResult Project(
        LegacyProviderRegistryInput input,
        DateTimeOffset observedAt)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (!input.MigrationGateEnabled)
        {
            return new LegacyProviderRegistryProjectionResult(
                LegacyProviderRegistryAdmission.Off,
                null,
                input.LegacyExecution);
        }

        if (!TryProvider(input.ActiveProvider, out var activeKind))
        {
            return Reject(
                LegacyProviderRegistryAdmission.UnknownProvider,
                input);
        }
        var providerSpecs = new[]
        {
            new LegacyProviderSpec(
                ProviderKind.Gemini,
                "gemini",
                "https://generativelanguage.googleapis.com/v1beta/",
                ProviderProtocolKind.GeminiGenerateContent,
                ProviderAuthenticationScheme.GoogleApiKey,
                input.GeminiModel,
                input.GeminiCredentialConfigured),
            new LegacyProviderSpec(
                ProviderKind.OpenAI,
                "openai",
                "https://api.openai.com/v1/",
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                input.OpenAiModel,
                input.OpenAiCredentialConfigured),
            new LegacyProviderSpec(
                ProviderKind.DeepSeek,
                "deepseek",
                "https://api.deepseek.com/",
                ProviderProtocolKind.OpenAiChatCompletions,
                ProviderAuthenticationScheme.Bearer,
                input.DeepSeekModel,
                input.DeepSeekCredentialConfigured),
            new LegacyProviderSpec(
                ProviderKind.QwenDashScope,
                "qwen",
                input.QwenBaseUrl,
                ProviderProtocolKind.OpenAiResponses,
                ProviderAuthenticationScheme.Bearer,
                input.QwenModel,
                input.QwenCredentialConfigured)
        };
        if (providerSpecs.Any(spec => spec.BaseUri == null))
        {
            return Reject(
                LegacyProviderRegistryAdmission.InvalidEndpoint,
                input);
        }
        if (providerSpecs.Any(spec => !TryModelToken(spec.Model)))
        {
            return Reject(
                LegacyProviderRegistryAdmission.InvalidModel,
                input);
        }

        var definitions = providerSpecs
            .Select(
                spec => new ProviderDefinition(
                    new ProviderDefinitionId("builtin-" + spec.Token),
                    spec.Kind,
                    new[] { spec.Protocol }))
            .ToArray();
        var instances = providerSpecs
            .Select(spec => Instance(spec, input.ConfigurationGeneration))
            .ToArray();
        var models = providerSpecs
            .Select(spec => Model(spec, observedAt))
            .ToArray();
        var snapshot = ProviderRegistrySnapshot.Create(
            ProviderRegistryContractGuard.RegistrySchemaVersion,
            input.ConfigurationGeneration,
            definitions,
            instances,
            models);
        if (!snapshot.Accepted || snapshot.Snapshot == null)
        {
            return Reject(
                LegacyProviderRegistryAdmission.RegistryRejected,
                input);
        }
        var active = providerSpecs.Single(spec => spec.Kind == activeKind);
        var projection = new LegacyProviderRegistryProjection(
            snapshot.Snapshot,
            new ProviderEndpointInstanceId("legacy-" + active.Token),
            new ProviderModelInstanceId("legacy-model-" + active.Token),
            MigrationState(active),
            Bindings,
            new ReadOnlyDictionary<string, bool>(
                providerSpecs.ToDictionary(
                    spec => spec.Token,
                    spec => spec.CredentialConfigured,
                    StringComparer.Ordinal)));
        return new LegacyProviderRegistryProjectionResult(
            LegacyProviderRegistryAdmission.Projected,
            projection,
            input.LegacyExecution);
    }

    private static ProviderEndpointInstance Instance(
        LegacyProviderSpec spec,
        long configurationGeneration)
    {
        var instanceId = new ProviderEndpointInstanceId(
            "legacy-" + spec.Token);
        var credential = new CredentialReference(
            new CredentialReferenceId(
                "legacy-" + spec.Token + "-credential-reference"),
            instanceId,
            spec.AuthenticationScheme);
        return new ProviderEndpointInstance(
            instanceId,
            new ProviderDefinitionId("builtin-" + spec.Token),
            spec.Kind,
            ProviderUiGroup.Cloud,
            DeploymentOwnership.ManagedCloud,
            ConnectionRouteLocality.PublicRemote,
            EndpointExposureState.PublicExposedDeclared,
            spec.BaseUri!,
            spec.Protocol,
            protocolProfileVersion: 1,
            spec.AuthenticationScheme,
            credential,
            ProviderTrustClass.OfficialCloudHttps,
            enabled: true,
            configurationGeneration);
    }

    private static ProviderModelDescriptor Model(
        LegacyProviderSpec spec,
        DateTimeOffset observedAt)
    {
        var evidence = new ProviderEvidenceStamp(
            observedAt,
            observedAt,
            observedAt.AddHours(1),
            ProviderEvidenceFreshness.Current);
        return new ProviderModelDescriptor(
            new ProviderEndpointInstanceId("legacy-" + spec.Token),
            new ProviderModelInstanceId("legacy-model-" + spec.Token),
            SafeModelAlias(spec.Model),
            ProviderRegistryCanonicalHash.Of(
                "D027/LEGACY-MODEL-CORRELATION/1",
                spec.Token,
                spec.Model),
            ProviderTransportIdentityClass.PublicModelId,
            new OpaqueTransportModelReference(
                "legacy-model-reference-" + spec.Token),
            new[]
            {
                new ProviderCatalogFact(
                    CatalogFactKind.ServerVisible,
                    ProviderCatalogFactProvenance.ExplicitConfiguration,
                    evidence)
            },
            NoAuthorityCapabilities(),
            ExecutionVenue.ManagedRemote,
            ProviderExecutionVenueProvenance.OfficialProvider,
            evidence);
    }

    private static ProviderCapabilityProfile NoAuthorityCapabilities()
        => new(
            Enum.GetValues<ProviderCapability>()
                .Where(value => value != ProviderCapability.Invalid)
                .Select(
                    value => new ProviderCapabilityStatus(
                        value,
                        ProviderCapabilityEvidenceState.Unknown,
                        ProviderCapabilityEvidenceState.Unknown,
                        ProviderCapabilityAuthorizationState.NotAuthorized))
                .ToArray());

    private static LegacyModelMigrationState MigrationState(
        LegacyProviderSpec spec)
        => spec.Kind == ProviderKind.DeepSeek
            && (string.Equals(
                    spec.Model,
                    "deepseek-chat",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    spec.Model,
                    "deepseek-reasoner",
                    StringComparison.OrdinalIgnoreCase))
            ? LegacyModelMigrationState.MigrationRequired
            : LegacyModelMigrationState.CurrentOrUnverified;

    private static bool TryProvider(string value, out ProviderKind kind)
    {
        if (string.Equals(value, "Gemini", StringComparison.OrdinalIgnoreCase))
            kind = ProviderKind.Gemini;
        else if (string.Equals(value, "OpenAI", StringComparison.OrdinalIgnoreCase))
            kind = ProviderKind.OpenAI;
        else if (string.Equals(value, "DeepSeek", StringComparison.OrdinalIgnoreCase))
            kind = ProviderKind.DeepSeek;
        else if (string.Equals(value, "Qwen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Alibaba", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "DashScope", StringComparison.OrdinalIgnoreCase))
            kind = ProviderKind.QwenDashScope;
        else
            kind = ProviderKind.Invalid;
        return kind != ProviderKind.Invalid;
    }

    private static bool TryModelToken(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= ProviderRegistryContractGuard.MaximumIdentifierLength
            && value.All(
                character => char.IsLetterOrDigit(character)
                    || character is '-' or '_' or '.');

    private static string SafeModelAlias(string value)
        => "configured-" + ProviderRegistryCanonicalHash.Of(
            "D027/LEGACY-MODEL-ALIAS/1",
            value)[..12].ToLowerInvariant();

    private static ProviderEndpointUri? NormalizeBaseUrl(string value)
        => ProviderEndpointUri.TryCreate(
            value.EndsWith("/", StringComparison.Ordinal)
                ? value
                : value + "/").Endpoint;

    private static LegacyProviderRegistryProjectionResult Reject(
        LegacyProviderRegistryAdmission admission,
        LegacyProviderRegistryInput input)
        => new(admission, null, input.LegacyExecution);

    private static LegacyProviderSettingBinding Binding(
        string semanticId,
        string section,
        string key)
        => new(semanticId, section, key);

    private sealed class LegacyProviderSpec
    {
        internal ProviderKind Kind { get; }
        internal string Token { get; }
        internal ProviderEndpointUri? BaseUri { get; }
        internal ProviderProtocolKind Protocol { get; }
        internal ProviderAuthenticationScheme AuthenticationScheme { get; }
        internal string Model { get; }
        internal bool CredentialConfigured { get; }

        internal LegacyProviderSpec(
            ProviderKind kind,
            string token,
            string baseUri,
            ProviderProtocolKind protocol,
            ProviderAuthenticationScheme authenticationScheme,
            string model,
            bool credentialConfigured)
        {
            Kind = kind;
            Token = token;
            BaseUri = NormalizeBaseUrl(baseUri);
            Protocol = protocol;
            AuthenticationScheme = authenticationScheme;
            Model = model;
            CredentialConfigured = credentialConfigured;
        }
    }
}

internal sealed class ProviderRegistryStageBAuthorityCounters
{
    internal static ProviderRegistryStageBAuthorityCounters Zero { get; } =
        new();

    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int CredentialReads => 0;
    internal int CredentialCopies => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int MemoryWrites => 0;
    internal int DecisionOutputs => 0;
    internal int PersistenceWrites => 0;
    internal int Total => 0;

    private ProviderRegistryStageBAuthorityCounters()
    {
    }
}
