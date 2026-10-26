using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LilithTextInjector;

internal enum LegacyControlCenterProjectionAdmission
{
    Invalid = 0,
    Accepted = 1,
    CoalescedTimeRefresh = 2,
    InvalidProvider = 3,
    InvalidMotionMode = 4,
    SchemaRejected = 5,
    SnapshotRejected = 6,
    CrossRuntimeRejected = 7,
    StaleConfigurationRejected = 8,
    ConflictingConfigurationRejected = 9,
    DisabledAndCleared = 10
}

internal enum LegacyProviderSelection
{
    Invalid = 0,
    Gemini = 1,
    Qwen = 2,
    OpenAI = 3,
    DeepSeek = 4
}

internal enum LegacyMotionDirectorSelection
{
    Invalid = 0,
    Off = 1,
    Observe = 2,
    LocalRules = 3
}

internal sealed class LegacySettingProjectionBinding
{
    internal ControlCenterSettingId SettingId { get; }
    internal string SourceSection { get; }
    internal string SourceKey { get; }
    internal string Fingerprint { get; }

    internal LegacySettingProjectionBinding(
        string settingId,
        string sourceSection,
        string sourceKey)
    {
        SettingId = new ControlCenterSettingId(settingId);
        SourceSection = ControlCenterStageAContractGuard.Identifier(
            sourceSection,
            nameof(sourceSection));
        SourceKey = ControlCenterStageAContractGuard.Identifier(
            sourceKey,
            nameof(sourceKey));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/LEGACY-BINDING/1",
            SettingId.Value,
            SourceSection,
            SourceKey);
    }
}

internal sealed class LegacyControlCenterProjectionInput
{
    internal ControlCenterRuntimeIdentity RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal string ProviderSelection { get; }
    internal bool ActiveCredentialConfigured { get; }
    internal bool VoiceEnabled { get; }
    internal bool ConversationHistoryEnabled { get; }
    internal bool MotionDirectorEnabled { get; }
    internal string MotionDirectorMode { get; }
    internal bool GlobalMotionPhysicsEnabled { get; }
    internal bool CharacterSoleWriterMigrationEnabled { get; }
    internal bool UnifiedPromptOwnershipEnabled { get; }
    internal bool PlayerReplyEnabled { get; }
    internal bool PlayerReplyMemoryEnabled { get; }
    internal bool ProactiveNoteEnabled { get; }
    internal bool OrchestratorObserveEnabled { get; }
    internal bool InitiativeShadowEnabled { get; }

    internal LegacyControlCenterProjectionInput(
        ControlCenterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        DateTimeOffset observedAt,
        string providerSelection,
        bool activeCredentialConfigured,
        bool voiceEnabled,
        bool conversationHistoryEnabled,
        bool motionDirectorEnabled,
        string motionDirectorMode,
        bool globalMotionPhysicsEnabled,
        bool characterSoleWriterMigrationEnabled,
        bool unifiedPromptOwnershipEnabled,
        bool playerReplyEnabled,
        bool playerReplyMemoryEnabled,
        bool proactiveNoteEnabled,
        bool orchestratorObserveEnabled,
        bool initiativeShadowEnabled)
    {
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ConfigurationGeneration = ControlCenterStageAContractGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        ObservedAt = ControlCenterStageAContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        ProviderSelection = BoundedLegacyToken(
            providerSelection,
            nameof(providerSelection));
        ActiveCredentialConfigured = activeCredentialConfigured;
        VoiceEnabled = voiceEnabled;
        ConversationHistoryEnabled = conversationHistoryEnabled;
        MotionDirectorEnabled = motionDirectorEnabled;
        MotionDirectorMode = BoundedLegacyToken(
            motionDirectorMode,
            nameof(motionDirectorMode));
        GlobalMotionPhysicsEnabled = globalMotionPhysicsEnabled;
        // Keep the positional input compatible; D-058 retired its authority.
        CharacterSoleWriterMigrationEnabled = false;
        UnifiedPromptOwnershipEnabled = unifiedPromptOwnershipEnabled;
        PlayerReplyEnabled = playerReplyEnabled;
        PlayerReplyMemoryEnabled = playerReplyMemoryEnabled;
        ProactiveNoteEnabled = proactiveNoteEnabled;
        // The retired passive observer has no configuration or authority.
        OrchestratorObserveEnabled = false;
        InitiativeShadowEnabled = initiativeShadowEnabled;
    }

    private static string BoundedLegacyToken(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || value.Any(char.IsControl)
            || value.IndexOf('/') >= 0
            || value.IndexOf('\\') >= 0
            || value.IndexOf(':') >= 0)
        {
            throw new ArgumentException(
                "Legacy projection tokens must be bounded and path-free.",
                parameterName);
        }
        return value.Trim();
    }
}

internal sealed class LegacyControlCenterProjectionResult
{
    internal LegacyControlCenterProjectionAdmission Admission { get; }
    internal LegacyControlCenterProjection? Projection { get; }

    internal LegacyControlCenterProjectionResult(
        LegacyControlCenterProjectionAdmission admission,
        LegacyControlCenterProjection? projection)
    {
        Admission = admission;
        Projection = projection;
    }
}

internal sealed class LegacyControlCenterProjection
{
    internal ControlCenterSchema Schema { get; }
    internal EffectiveRuntimeSnapshot EffectiveSnapshot { get; }
    internal LegacyProviderSelection ProviderSelection { get; }
    internal LegacyMotionDirectorSelection MotionDirectorMode { get; }
    internal IReadOnlyList<LegacySettingProjectionBinding> SourceBindings {
        get;
    }
    internal ControlCenterRuntimeTelemetry? RuntimeTelemetry { get; }
    internal string SemanticFingerprint { get; }
    internal string ObservationFingerprint { get; }

    internal LegacyControlCenterProjection(
        ControlCenterSchema schema,
        EffectiveRuntimeSnapshot effectiveSnapshot,
        LegacyProviderSelection providerSelection,
        LegacyMotionDirectorSelection motionDirectorMode,
        IReadOnlyList<LegacySettingProjectionBinding> sourceBindings,
        ControlCenterRuntimeTelemetry? runtimeTelemetry = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        EffectiveSnapshot = effectiveSnapshot
            ?? throw new ArgumentNullException(nameof(effectiveSnapshot));
        ProviderSelection = providerSelection;
        MotionDirectorMode = motionDirectorMode;
        SourceBindings = sourceBindings
            ?? throw new ArgumentNullException(nameof(sourceBindings));
        RuntimeTelemetry = runtimeTelemetry;
        SemanticFingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/LEGACY-PROJECTION-SEMANTIC/1",
            Schema.Fingerprint,
            EffectiveSnapshot.RuntimeIdentity.Fingerprint,
            ControlCenterStageACanonicalHash.Number(
                EffectiveSnapshot.ConfigurationGeneration),
            ControlCenterStageACanonicalHash.Number((int)ProviderSelection),
            ControlCenterStageACanonicalHash.Number((int)MotionDirectorMode),
            string.Join(
                ",",
                EffectiveSnapshot.EffectiveValues.Select(
                    value => value.Fingerprint)),
            string.Join(
                ",",
                EffectiveSnapshot.FeatureStates.Select(
                    value => value.Fingerprint)),
            string.Join(",", sourceBindings.Select(value => value.Fingerprint)));
        ObservationFingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/LEGACY-PROJECTION-OBSERVATION/1",
            SemanticFingerprint,
            runtimeTelemetry?.Fingerprint ?? "telemetry-unavailable",
            ControlCenterStageACanonicalHash.Timestamp(
                EffectiveSnapshot.ObservedAt));
    }

    internal LegacyControlCenterProjection WithRuntimeTelemetry(
        ControlCenterRuntimeTelemetry telemetry)
        => new(
            Schema,
            EffectiveSnapshot,
            ProviderSelection,
            MotionDirectorMode,
            SourceBindings,
            telemetry ?? throw new ArgumentNullException(nameof(telemetry)));
}

internal static class ControlCenterStageBLegacyProjectionAdapter
{
    internal const int DescriptorSetVersion = 4;

    private static readonly IReadOnlyList<LegacySettingProjectionBinding>
        CommonBindings = new ReadOnlyCollection<LegacySettingProjectionBinding>(
            new[]
            {
                Binding("provider.active", "AI", "Provider"),
                Binding("voice.enabled", "Voice", "Enabled"),
                Binding(
                    "memory.conversation-history-enabled",
                    "ConversationHistory",
                    "Enabled"),
                Binding(
                    "motion.director-enabled",
                    "MotionDirector",
                    "Enabled"),
                Binding(
                    "motion.director-mode",
                    "MotionDirector",
                    "Mode"),
                Binding(
                    "motion.global-physics-enabled",
                    "MotionAuthority",
                    "GlobalPhysicsEnabled"),
                Binding(
                    "prompt.unified-ownership-enabled",
                    "LivingContinuity",
                    "UnifiedPromptOwnershipEnabled"),
                Binding(
                    "reply.player-triggered-enabled",
                    "LivingContinuity",
                    "PlayerReplyEnabled"),
                Binding(
                    "reply.player-memory-enabled",
                    "LivingContinuity",
                    "PlayerReplyLongTermMemoryPromptEnabled"),
                Binding(
                    "initiative.proactive-note-enabled",
                    "LivingContinuity",
                    "ProactiveNoteEnabled"),
                Binding(
                    "initiative.shadow-enabled",
                    "InitiativeShadow",
                    "Enabled")
            });

    internal static LegacyControlCenterProjectionResult Project(
        LegacyControlCenterProjectionInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        var provider = ParseProvider(input.ProviderSelection);
        if (provider == LegacyProviderSelection.Invalid)
        {
            return Reject(
                LegacyControlCenterProjectionAdmission.InvalidProvider);
        }
        var motionMode = ParseMotionMode(input.MotionDirectorMode);
        if (motionMode == LegacyMotionDirectorSelection.Invalid)
        {
            return Reject(
                LegacyControlCenterProjectionAdmission.InvalidMotionMode);
        }
        var schemaResult = BuildSchema();
        if (schemaResult.Admission
                != ControlCenterSchemaBuildAdmission
                    .AcceptedForContractReplay
            || schemaResult.Schema == null)
        {
            return Reject(
                LegacyControlCenterProjectionAdmission.SchemaRejected);
        }
        var schema = schemaResult.Schema;
        var bindings = CommonBindings
            .Concat(new[] { ActiveCredentialBinding(provider) })
            .OrderBy(value => value.SettingId.Value, StringComparer.Ordinal)
            .ToArray();
        var effective = EffectiveRuntimeSnapshot.Create(
            schema,
            input.RuntimeIdentity,
            input.ConfigurationGeneration,
            input.ObservedAt,
            EffectiveValues(input, provider, motionMode),
            FeatureStates(input, motionMode),
            new ControlCenterDiagnosticsSummary(
                activeRecords: 0,
                retiredRecords: 0,
                faultCount: 0,
                capacityUsed: 0,
                capacityLimit: 128));
        if (effective.Admission
                != ControlCenterSnapshotBuildAdmission
                    .AcceptedForContractReplay
            || effective.Snapshot == null)
        {
            return Reject(
                LegacyControlCenterProjectionAdmission.SnapshotRejected);
        }
        return new LegacyControlCenterProjectionResult(
            LegacyControlCenterProjectionAdmission.Accepted,
            new LegacyControlCenterProjection(
                schema,
                effective.Snapshot,
                provider,
                motionMode,
                new ReadOnlyCollection<LegacySettingProjectionBinding>(
                    bindings)));
    }

    internal static ControlCenterSchemaBuildResult BuildSchema()
        => ControlCenterSchema.Create(
            ControlCenterStageAContractGuard.SchemaVersion,
            DescriptorSetVersion,
            new[]
            {
                OptionDescriptor(
                    "provider.active",
                    ControlCenterSettingCategory.Provider,
                    ControlCenterAuthorityDomain.ProviderRegistry,
                    new[] { "gemini", "qwen", "openai", "deepseek" },
                    ControlCenterApplyMode.ProcessRestart,
                    ControlCenterRestartRequirement.GameProcess),
                BooleanDescriptor(
                    "provider.active-credential-configured",
                    ControlCenterSettingCategory.Provider,
                    ControlCenterAuthorityDomain.ProviderRegistry),
                BooleanDescriptor(
                    "voice.enabled",
                    ControlCenterSettingCategory.Voice,
                    ControlCenterAuthorityDomain.RuntimeConfiguration),
                BooleanDescriptor(
                    "memory.conversation-history-enabled",
                    ControlCenterSettingCategory.Memory,
                    ControlCenterAuthorityDomain.Memory),
                BooleanDescriptor(
                    "motion.director-enabled",
                    ControlCenterSettingCategory.Motion,
                    ControlCenterAuthorityDomain.Motion),
                OptionDescriptor(
                    "motion.director-mode",
                    ControlCenterSettingCategory.Motion,
                    ControlCenterAuthorityDomain.Motion,
                    new[] { "off", "observe", "localrules" },
                    ControlCenterApplyMode.RuntimeRequest,
                    ControlCenterRestartRequirement.None),
                BooleanDescriptor(
                    "motion.global-physics-enabled",
                    ControlCenterSettingCategory.Motion,
                    ControlCenterAuthorityDomain.Motion),
                BooleanDescriptor(
                    "prompt.unified-ownership-enabled",
                    ControlCenterSettingCategory.Character,
                    ControlCenterAuthorityDomain.CharacterRuntime),
                BooleanDescriptor(
                    "reply.player-triggered-enabled",
                    ControlCenterSettingCategory.General,
                    ControlCenterAuthorityDomain.RuntimeConfiguration),
                BooleanDescriptor(
                    "reply.player-memory-enabled",
                    ControlCenterSettingCategory.Memory,
                    ControlCenterAuthorityDomain.Memory),
                BooleanDescriptor(
                    "initiative.proactive-note-enabled",
                    ControlCenterSettingCategory.Initiative,
                    ControlCenterAuthorityDomain.Initiative),
                BooleanDescriptor(
                    "initiative.shadow-enabled",
                    ControlCenterSettingCategory.Initiative,
                    ControlCenterAuthorityDomain.Initiative)
            });

    private static IEnumerable<ControlCenterEffectiveSetting> EffectiveValues(
        LegacyControlCenterProjectionInput input,
        LegacyProviderSelection provider,
        LegacyMotionDirectorSelection motionMode)
    {
        yield return Effective(
            "provider.active",
            ControlCenterSettingValue.Option(ProviderToken(provider)));
        yield return Effective(
            "provider.active-credential-configured",
            ControlCenterSettingValue.Boolean(
                input.ActiveCredentialConfigured));
        yield return Effective(
            "voice.enabled",
            ControlCenterSettingValue.Boolean(input.VoiceEnabled));
        yield return Effective(
            "memory.conversation-history-enabled",
            ControlCenterSettingValue.Boolean(
                input.ConversationHistoryEnabled));
        yield return Effective(
            "motion.director-enabled",
            ControlCenterSettingValue.Boolean(input.MotionDirectorEnabled));
        yield return Effective(
            "motion.director-mode",
            ControlCenterSettingValue.Option(MotionToken(motionMode)));
        yield return Effective(
            "motion.global-physics-enabled",
            ControlCenterSettingValue.Boolean(
                input.GlobalMotionPhysicsEnabled));
        yield return Effective(
            "prompt.unified-ownership-enabled",
            ControlCenterSettingValue.Boolean(
                input.UnifiedPromptOwnershipEnabled));
        yield return Effective(
            "reply.player-triggered-enabled",
            ControlCenterSettingValue.Boolean(input.PlayerReplyEnabled));
        yield return Effective(
            "reply.player-memory-enabled",
            ControlCenterSettingValue.Boolean(
                input.PlayerReplyMemoryEnabled));
        yield return Effective(
            "initiative.proactive-note-enabled",
            ControlCenterSettingValue.Boolean(input.ProactiveNoteEnabled));
        yield return Effective(
            "initiative.shadow-enabled",
            ControlCenterSettingValue.Boolean(input.InitiativeShadowEnabled));
    }

    private static IEnumerable<ControlCenterFeatureState> FeatureStates(
        LegacyControlCenterProjectionInput input,
        LegacyMotionDirectorSelection motionMode)
    {
        yield return Feature(
            "prompt.unified-owner",
            input.UnifiedPromptOwnershipEnabled
                ? ControlCenterFeatureStateKind.Assist
                : ControlCenterFeatureStateKind.Off);
        yield return Feature(
            "reply.player-triggered",
            input.PlayerReplyEnabled
                ? ControlCenterFeatureStateKind.Assist
                : ControlCenterFeatureStateKind.Off);
        yield return Feature(
            "initiative.proactive-note",
            input.ProactiveNoteEnabled
                ? ControlCenterFeatureStateKind.Assist
                : ControlCenterFeatureStateKind.Off);
        yield return Feature(
            "motion.director",
            !input.MotionDirectorEnabled
                || motionMode == LegacyMotionDirectorSelection.Off
                    ? ControlCenterFeatureStateKind.Off
                    : motionMode == LegacyMotionDirectorSelection.Observe
                        ? ControlCenterFeatureStateKind.Observe
                        : ControlCenterFeatureStateKind.Assist);
        yield return Feature(
            "initiative.shadow",
            input.InitiativeShadowEnabled
                ? ControlCenterFeatureStateKind.Observe
                : ControlCenterFeatureStateKind.Off);
    }

    private static LegacyProviderSelection ParseProvider(string value)
    {
        if (string.Equals(value, "Gemini", StringComparison.OrdinalIgnoreCase))
            return LegacyProviderSelection.Gemini;
        if (string.Equals(value, "Qwen", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                value,
                "Tongyi",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                value,
                "DashScope",
                StringComparison.OrdinalIgnoreCase))
        {
            return LegacyProviderSelection.Qwen;
        }
        if (string.Equals(value, "OpenAI", StringComparison.OrdinalIgnoreCase))
            return LegacyProviderSelection.OpenAI;
        if (string.Equals(
                value,
                "DeepSeek",
                StringComparison.OrdinalIgnoreCase))
        {
            return LegacyProviderSelection.DeepSeek;
        }
        return LegacyProviderSelection.Invalid;
    }

    private static LegacyMotionDirectorSelection ParseMotionMode(string value)
    {
        if (string.Equals(value, "Off", StringComparison.OrdinalIgnoreCase))
            return LegacyMotionDirectorSelection.Off;
        if (string.Equals(value, "Observe", StringComparison.OrdinalIgnoreCase))
            return LegacyMotionDirectorSelection.Observe;
        if (string.Equals(
                value,
                "LocalRules",
                StringComparison.OrdinalIgnoreCase))
        {
            return LegacyMotionDirectorSelection.LocalRules;
        }
        return LegacyMotionDirectorSelection.Invalid;
    }

    private static string ProviderToken(LegacyProviderSelection provider)
        => provider switch
        {
            LegacyProviderSelection.Gemini => "gemini",
            LegacyProviderSelection.Qwen => "qwen",
            LegacyProviderSelection.OpenAI => "openai",
            LegacyProviderSelection.DeepSeek => "deepseek",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    private static string MotionToken(LegacyMotionDirectorSelection mode)
        => mode switch
        {
            LegacyMotionDirectorSelection.Off => "off",
            LegacyMotionDirectorSelection.Observe => "observe",
            LegacyMotionDirectorSelection.LocalRules => "localrules",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static LegacySettingProjectionBinding ActiveCredentialBinding(
        LegacyProviderSelection provider)
        => provider switch
        {
            LegacyProviderSelection.Gemini => Binding(
                "provider.active-credential-configured",
                "Gemini",
                "ApiKey"),
            LegacyProviderSelection.Qwen => Binding(
                "provider.active-credential-configured",
                "Qwen",
                "ApiKey"),
            LegacyProviderSelection.OpenAI => Binding(
                "provider.active-credential-configured",
                "OpenAI",
                "ApiKey"),
            LegacyProviderSelection.DeepSeek => Binding(
                "provider.active-credential-configured",
                "DeepSeek",
                "ApiKey"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    private static ControlCenterSettingDescriptor BooleanDescriptor(
        string id,
        ControlCenterSettingCategory category,
        ControlCenterAuthorityDomain authority)
        => new(
            new ControlCenterSettingId(id),
            category,
            ControlCenterSettingValueKind.Boolean,
            defaultValue: null,
            ControlCenterValidationRule.Boolean(),
            ControlCenterSensitivityClass.PrivateConfiguration,
            ControlCenterApplyMode.RuntimeRequest,
            ControlCenterRestartRequirement.None,
            authority,
            ControlCenterVisibilityPolicy.Basic);

    private static ControlCenterSettingDescriptor OptionDescriptor(
        string id,
        ControlCenterSettingCategory category,
        ControlCenterAuthorityDomain authority,
        IEnumerable<string> options,
        ControlCenterApplyMode apply,
        ControlCenterRestartRequirement restart)
        => new(
            new ControlCenterSettingId(id),
            category,
            ControlCenterSettingValueKind.Option,
            defaultValue: null,
            ControlCenterValidationRule.ClosedOptions(options),
            ControlCenterSensitivityClass.PrivateConfiguration,
            apply,
            restart,
            authority,
            ControlCenterVisibilityPolicy.Basic);

    private static ControlCenterEffectiveSetting Effective(
        string id,
        ControlCenterSettingValue value)
        => new(
            new ControlCenterSettingId(id),
            ControlCenterEffectiveValueState.Effective,
            value);

    private static ControlCenterFeatureState Feature(
        string id,
        ControlCenterFeatureStateKind state)
        => new(new ControlCenterFeatureId(id), state);

    private static LegacySettingProjectionBinding Binding(
        string id,
        string section,
        string key)
        => new(id, section, key);

    private static LegacyControlCenterProjectionResult Reject(
        LegacyControlCenterProjectionAdmission admission)
        => new(admission, null);
}

internal readonly struct ControlCenterStageBAuthorityCounters
{
    internal int PluginWrites { get; }
    internal int ConfigurationWrites { get; }
    internal int ProviderCalls { get; }
    internal int PromptWrites { get; }
    internal int MemoryWrites { get; }
    internal int DecisionOutputs { get; }
    internal int QueueWrites { get; }
    internal int MotionOutputs { get; }
    internal int ExecutionOutputs { get; }
    internal int PlayerDataWrites { get; }
    internal int IpcOutputs { get; }
    internal int DesktopOutputs { get; }

    internal static ControlCenterStageBAuthorityCounters Zero => default;
}
