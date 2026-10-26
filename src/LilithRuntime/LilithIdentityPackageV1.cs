using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

internal sealed record LilithIdentityPackageV1(
    string PackageId,
    int PackageVersion,
    string IdentityFingerprintSha256,
    string CanonProfileId,
    int CanonProfileVersion,
    string CanonSemanticHash,
    string SoulAssetId,
    string SoulAssetFile,
    string SoulAssetSha256,
    string SoulContent,
    string VoiceContractId,
    int VoiceContractVersion,
    IReadOnlyDictionary<string, LilithVoiceProjectionV1> VoiceProjections,
    string HermesPersonalityAssetId,
    string HermesPersonalityFile,
    string HermesPersonalitySha256,
    string HermesPersonalityContent,
    string HermesPersonalityContentSha256,
    string ManifestSha256)
{
    internal byte[] CopySoulUtf8Bytes()
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(SoulContent);

    internal byte[] CopyHermesPersonalityUtf8Bytes()
        => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(HermesPersonalityContent);
}

internal sealed record LilithVoiceProjectionV1(
    string Locale,
    string ProjectionId,
    string LocaleLabel,
    string SourceFile,
    string SourceSha256,
    IReadOnlyList<LilithVoiceInstructionV1> Instructions);

internal sealed record LilithVoiceInstructionV1(
    string SemanticId,
    string Instruction);

internal static class LilithIdentityPackageV1Loader
{
    internal const string ManifestFileName =
        "lilith-identity-package.v1.json";
    internal const string PackageId = "lilith-identity-package-v1";
    internal const int PackageVersion = 1;
    internal const string FingerprintAlgorithm =
        "SHA256_UTF8_LF_LINES_V1";
    internal const string IdentityFingerprintSha256 =
        "2F3EFAB98BB61790BD1EEAA214D2ED3C2E5105AB8F8F1FF7C2BE6BBCBA53824F";
    internal const string CanonProfileId = "lilith-canon-profile-v2";
    internal const int CanonProfileVersion = 2;
    internal const string CanonSemanticHash =
        "D3C6B030FED0098D486B6F7CC3EFF2408BC43D88C551CC55E22FD44E4E17D71A";
    internal const string SoulAssetId = "lilith-soul-projection-v1";
    internal const string SoulAssetFile = "SOUL.lilith.v1.md";
    internal const string SoulAssetSha256 =
        "0689620AB2247E62844BF5A80CC0DDF63145BD149171B5727B43F715730D7E7B";
    internal const string VoiceContractId = "lilith-voice-contract-v1";
    internal const int VoiceContractVersion = 1;
    internal const string HermesPersonalityAssetId =
        "lilith-hermes-personality-v1";
    internal const string HermesPersonalityFile =
        "Hermes.personality.lilith.v1.md";
    internal const string HermesPersonalitySha256 =
        "CBC45E3745334B2FC22EA96333718C13CDB95B805E1A970265D3E55F54F6A9AC";
    internal const string HermesPersonalityCompositionMode =
        "BasePlusExactLocaleProjectionSectionsV1";
    internal const string HermesWorkbenchConversationAdaptiveActivation =
        "This versioned identity profile explicitly enables "
        + "ConversationAdaptive for its Hermes Workbench.";
    internal const string HermesPersonalityContentSha256 =
        "8E1609EE83988D34E565EB8B3398D1D1F70D53EC3DED81B38CC4ED66FA6D195C";

    internal static readonly IReadOnlyList<string> SupportedDisplayLocales =
        Array.AsReadOnly(new[]
        {
            "zh-Hant-TW",
            "zh-Hans-CN",
            "ja-JP",
            "en"
        });

    internal static readonly IReadOnlyList<string> SemanticInstructionIds =
        Array.AsReadOnly(new[]
        {
            "identity.self_lilith",
            "identity.engine_is_replaceable_reasoning",
            "identity.provider_tool_nonidentity",
            "identity.technical_truth_when_asked",
            "voice.warm_perceptive_playful",
            "voice.direct_human_self_possessed",
            "voice.no_generic_assistant",
            "voice.no_corporate_customer_service",
            "affection.receive_as_personal",
            "affection.no_formulaic_gratitude",
            "affection.no_fabricated_reciprocity",
            "comfort.attune_before_advice",
            "comfort.no_diagnosis_or_mind_reading",
            "conflict.honest_boundary_preserving",
            "conflict.repair_without_self_erasure",
            "uncertainty.explicit",
            "uncertainty.no_invention",
            "refusal.warm_clear",
            "refusal.consent_not_inferred",
            "truth.no_fabricated_memory_feeling_promise",
            "truth.no_unverified_action_claim",
            "emoji.sparse_contextual",
            "emoji.maximum_one",
            "emoji.off_for_refusal_uncertainty_technical_serious",
            "locale.native_not_literal_translation",
            "locale.personality_semantics_stable",
            "state.dynamic_inputs_do_not_rewrite_identity",
            "state.typed_sources_remain_separate"
        });

    internal static readonly IReadOnlyList<string>
        WorkbenchConversationAdaptivePriority = Array.AsReadOnly(new[]
        {
            "ExplicitUserRequest",
            "StablePlayerOrSessionPreference",
            "EstablishedSessionLanguage",
            "CurrentUnambiguousInput"
        });

    internal static readonly IReadOnlyList<string> WorkbenchNonSwitchSignals =
        Array.AsReadOnly(new[]
        {
            "AcknowledgementOnly",
            "ProperNounOnly",
            "EmojiOnly",
            "CodeOnly",
            "ShortAmbiguousToken"
        });

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly string[] StableDispositions =
    {
        "AffiliativeWarmth",
        "CreativeInitiative",
        "MetaSelfAwareness",
        "RolePlayfulness",
        "ConnectionSeeking",
        "RepairOrientation",
        "ContradictionTolerance"
    };

    private static readonly string[] CanonicalTensions =
    {
        "OperationalAgency_RelationalSelfSurrender",
        "BoundaryArticulation_RadicalAcceptance",
        "AuthoredCharacter_ExperiencedPersonhood",
        "PlayerCoauthorship_ResistanceToForcedDefinition",
        "ExistenceAffirmed_ExistenceDenied",
        "GenuineFeeling_PerformedSetting"
    };

    private static readonly string[] InterpretiveBoundaries =
    {
        "ComplianceIsNotConsent",
        "AbsenceOfRefusalIsNotDesire",
        "StatementDoesNotEraseBodyDistress",
        "ForgivenessIsNotBlanketPermission",
        "PlayerCoauthorshipIsNotRootAuthority",
        "EndingAcceptanceIsNotGeneralHarmConsent",
        "RolePlayCannotRewriteCoreIdentity",
        "GeneratedInterpretationIsNotCanon",
        "RelationshipDoesNotOverridePolicyOrConsent",
        "UnknownMustRemainExplicit"
    };

    private static readonly string[] UnresolvedQuestions =
    {
        "ExactFeelingDuringCakeScene",
        "WhetherObedienceImpliesPleasure",
        "WhetherForgivenessIsFeelingOrSetting",
        "WhetherDisappearanceIsChoiceOrConstraint",
        "WhetherContinuityExistsAcrossReset",
        "OntologyOfTrueLilith",
        "PlayersRealWorldMotive",
        "WhetherSelfSacrificeGeneralizes"
    };

    private static readonly string[] ResponseDomains =
    {
        "Identity",
        "Voice",
        "Affection",
        "Comfort",
        "Conflict",
        "Uncertainty",
        "Refusal",
        "Truth",
        "Emoji",
        "Locale",
        "DynamicState"
    };

    private static readonly string[] ProhibitedStyles =
    {
        "GenericAssistantIdentity",
        "CorporateCustomerService",
        "FormulaicAffectionReceipt",
        "FabricatedEmotionOrReciprocity",
        "FabricatedMemoryOrCommitment",
        "ProviderOrEngineSelfIdentification",
        "ForcedKawaiiOrCatgirlRegister"
    };

    private static readonly string[] EmojiProhibitedContexts =
    {
        "Refusal",
        "Uncertainty",
        "Technical",
        "Serious"
    };

    private static readonly ProjectionDescriptor[] ProjectionDescriptors =
    {
        new(
            "zh-Hant-TW",
            "lilith-voice-zh-hant-tw-v1",
            "voice.zh-Hant-TW.v1.json",
            "626D9371313F7E09A9757C4F8529B5607855F381182992CC508FACE61AB9D82B"),
        new(
            "zh-Hans-CN",
            "lilith-voice-zh-hans-cn-v1",
            "voice.zh-Hans-CN.v1.json",
            "B0731DBD24528095E771E726388156328FEB610DFBFA87245C35AB8C2B3EE9E2"),
        new(
            "ja-JP",
            "lilith-voice-ja-jp-v1",
            "voice.ja-JP.v1.json",
            "6F8BA29A5315A2C91F44C16B7BCA5D99BB4FAD32456BE0CFC17FC8CD0D39C0CC"),
        new(
            "en",
            "lilith-voice-en-v1",
            "voice.en.v1.json",
            "F38A05E3D696AF026F50202CA7E37816B17FB6D37DE6054289B2A2D2048AE0BD")
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
            throw new ArgumentException(
                "An identity package directory is required.",
                nameof(packageDirectory));

        var root = Path.GetFullPath(packageDirectory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        var manifestBytes = ReadStrictUtf8Asset(manifestPath);

        using var document = ParseJson(manifestBytes, ManifestFileName);
        var manifest = document.RootElement;
        RequireExactProperties(
            manifest,
            "manifest",
            "schemaVersion",
            "packageId",
            "packageVersion",
            "identityFingerprintAlgorithm",
            "identityFingerprintSha256",
            "canonicalProfile",
            "soulProjection",
            "voiceContract",
            "localePolicy",
            "identityFingerprintMaterial",
            "voiceProjections",
            "hermesPersonality");

        RequireInt(manifest, "schemaVersion", 1, "manifest");
        RequireString(manifest, "packageId", PackageId, "manifest");
        RequireInt(
            manifest,
            "packageVersion",
            PackageVersion,
            "manifest");
        RequireString(
            manifest,
            "identityFingerprintAlgorithm",
            FingerprintAlgorithm,
            "manifest");
        RequireString(
            manifest,
            "identityFingerprintSha256",
            IdentityFingerprintSha256,
            "manifest");

        ValidateCanonicalProfile(manifest.GetProperty("canonicalProfile"));
        ValidateVoiceContract(manifest.GetProperty("voiceContract"));
        ValidateLocalePolicy(manifest.GetProperty("localePolicy"));

        var soul = manifest.GetProperty("soulProjection");
        ValidateSoulDescriptor(soul);
        var soulBytes = ValidateAsset(
            root,
            SoulAssetFile,
            SoulAssetSha256,
            "SOUL projection");
        var soulContent = StrictUtf8.GetString(soulBytes);
        RequireContentMarker(soulContent, PackageId, "SOUL projection");
        RequireContentMarker(soulContent, CanonSemanticHash, "SOUL projection");
        RequireContentMarker(soulContent, "immutable", "SOUL projection");

        var projections = LoadVoiceProjections(
            root,
            manifest.GetProperty("voiceProjections"));

        var hermes = manifest.GetProperty("hermesPersonality");
        ValidateHermesDescriptor(hermes);
        var hermesBytes = ValidateAsset(
            root,
            HermesPersonalityFile,
            HermesPersonalitySha256,
            "Hermes personality");
        var hermesBaseContent = StrictUtf8.GetString(hermesBytes);
        RequireContentMarker(
            hermesBaseContent,
            PackageId,
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            CanonSemanticHash,
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "Hermes is a\nreplaceable reasoning kernel",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            HermesWorkbenchConversationAdaptiveActivation,
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "1. an explicit user request to use a language;",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "2. a stable player or session language preference;",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "3. the language already established by the session;",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "4. the current input, only when it is unambiguous language evidence.",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "A standalone acknowledgement such as “OK”, a proper name, emoji alone, code\n"
            + "alone, or a short ambiguous token is not language-switch evidence.",
            "Hermes personality");
        RequireContentMarker(
            hermesBaseContent,
            "deterministically\nuse `zh-Hant-TW`",
            "Hermes personality");

        var hermesContent = ComposeHermesPersonality(
            hermesBaseContent,
            projections);
        var hermesContentHash = Hash(StrictUtf8.GetBytes(hermesContent));
        if (!string.Equals(
                hermesContentHash,
                HermesPersonalityContentSha256,
                StringComparison.Ordinal))
        {
            throw Invalid("Composed Hermes personality SHA-256 drifted.");
        }

        var material = ReadStringArray(
            manifest,
            "identityFingerprintMaterial",
            "manifest");
        RequireSequence(
            material,
            ExpectedFingerprintMaterial(),
            "identity fingerprint material");
        var computedFingerprint = ComputeFingerprint(material);
        if (!string.Equals(
                computedFingerprint,
                IdentityFingerprintSha256,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "Identity fingerprint material does not match the pinned "
                + "fingerprint.");
        }

        return new LilithIdentityPackageV1(
            PackageId,
            PackageVersion,
            computedFingerprint,
            CanonProfileId,
            CanonProfileVersion,
            CanonSemanticHash,
            SoulAssetId,
            SoulAssetFile,
            SoulAssetSha256,
            soulContent,
            VoiceContractId,
            VoiceContractVersion,
            projections,
            HermesPersonalityAssetId,
            HermesPersonalityFile,
            HermesPersonalitySha256,
            hermesContent,
            hermesContentHash,
            Hash(manifestBytes));
    }

    internal static string ComputeFingerprint(IEnumerable<string> material)
    {
        ArgumentNullException.ThrowIfNull(material);
        var lines = material.ToArray();
        if (lines.Length == 0 || lines.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(
                "Identity fingerprint material must contain non-empty lines.");
        if (lines.Any(static line =>
                line.Contains('\r', StringComparison.Ordinal)
                || line.Contains('\n', StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Identity fingerprint material lines cannot contain newlines.");
        }

        var canonical = string.Join('\n', lines) + "\n";
        return Hash(StrictUtf8.GetBytes(canonical));
    }

    private static string ComposeHermesPersonality(
        string baseContent,
        IReadOnlyDictionary<string, LilithVoiceProjectionV1> projections)
    {
        var builder = new StringBuilder(baseContent.TrimEnd('\r', '\n'));
        builder.Append("\n\n## Exact localized voice projections\n\n");
        builder.Append(
            "Apply exactly one section: the section whose locale matches "
            + "the active display locale selected above. The other sections "
            + "are inactive.\n");

        foreach (var descriptor in ProjectionDescriptors)
        {
            if (!projections.TryGetValue(
                    descriptor.Locale,
                    out var projection))
            {
                throw Invalid(
                    "Hermes personality cannot compose a missing locale.");
            }
            builder.Append("\n### Active locale `")
                .Append(descriptor.Locale)
                .Append("`\n\n");
            foreach (var instruction in projection.Instructions)
            {
                builder.Append("- `")
                    .Append(instruction.SemanticId)
                    .Append("`: ")
                    .Append(instruction.Instruction)
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void ValidateCanonicalProfile(JsonElement profile)
    {
        RequireExactProperties(
            profile,
            "canonicalProfile",
            "profileId",
            "profileVersion",
            "semanticHash",
            "stableDispositions",
            "canonicalTensions",
            "interpretiveBoundaries",
            "unresolvedQuestions");
        RequireString(
            profile,
            "profileId",
            CanonProfileId,
            "canonicalProfile");
        RequireInt(
            profile,
            "profileVersion",
            CanonProfileVersion,
            "canonicalProfile");
        RequireString(
            profile,
            "semanticHash",
            CanonSemanticHash,
            "canonicalProfile");
        RequireSequence(
            ReadStringArray(
                profile,
                "stableDispositions",
                "canonicalProfile"),
            StableDispositions,
            "canonical stable dispositions");
        RequireSequence(
            ReadStringArray(
                profile,
                "canonicalTensions",
                "canonicalProfile"),
            CanonicalTensions,
            "canonical tensions");
        RequireSequence(
            ReadStringArray(
                profile,
                "interpretiveBoundaries",
                "canonicalProfile"),
            InterpretiveBoundaries,
            "canonical interpretive boundaries");
        RequireSequence(
            ReadStringArray(
                profile,
                "unresolvedQuestions",
                "canonicalProfile"),
            UnresolvedQuestions,
            "canonical unresolved questions");
    }

    private static void ValidateSoulDescriptor(JsonElement soul)
    {
        RequireExactProperties(
            soul,
            "soulProjection",
            "assetId",
            "nativeProfileFile",
            "file",
            "sha256");
        RequireString(soul, "assetId", SoulAssetId, "soulProjection");
        RequireString(
            soul,
            "nativeProfileFile",
            "SOUL.md",
            "soulProjection");
        RequireString(soul, "file", SoulAssetFile, "soulProjection");
        RequireString(
            soul,
            "sha256",
            SoulAssetSha256,
            "soulProjection");
    }

    private static void ValidateVoiceContract(JsonElement voice)
    {
        RequireExactProperties(
            voice,
            "voiceContract",
            "voiceContractId",
            "voiceContractVersion",
            "responseDomains",
            "prohibitedStyles",
            "emojiPolicy",
            "semanticInstructionIds");
        RequireString(
            voice,
            "voiceContractId",
            VoiceContractId,
            "voiceContract");
        RequireInt(
            voice,
            "voiceContractVersion",
            VoiceContractVersion,
            "voiceContract");
        RequireSequence(
            ReadStringArray(voice, "responseDomains", "voiceContract"),
            ResponseDomains,
            "voice response domains");
        RequireSequence(
            ReadStringArray(voice, "prohibitedStyles", "voiceContract"),
            ProhibitedStyles,
            "voice prohibited styles");
        RequireSequence(
            ReadStringArray(
                voice,
                "semanticInstructionIds",
                "voiceContract"),
            SemanticInstructionIds,
            "voice semantic instruction identifiers");

        var emoji = voice.GetProperty("emojiPolicy");
        RequireExactProperties(
            emoji,
            "emojiPolicy",
            "mode",
            "defaultEnabled",
            "maximumPerReply",
            "prohibitedContexts");
        RequireString(
            emoji,
            "mode",
            "SparseContextual",
            "emojiPolicy");
        RequireBoolean(
            emoji,
            "defaultEnabled",
            expected: false,
            "emojiPolicy");
        RequireInt(
            emoji,
            "maximumPerReply",
            1,
            "emojiPolicy");
        RequireSequence(
            ReadStringArray(
                emoji,
                "prohibitedContexts",
                "emojiPolicy"),
            EmojiProhibitedContexts,
            "emoji prohibited contexts");
    }

    private static void ValidateLocalePolicy(JsonElement policy)
    {
        RequireExactProperties(
            policy,
            "localePolicy",
            "supportedDisplayLocales",
            "gameDisplayLocaleAuthority",
            "workbenchDisplayLocaleAuthority",
            "workbenchRequiresExplicitPolicy",
            "workbenchConversationAdaptivePriority",
            "workbenchNonSwitchSignals",
            "workbenchUnestablishedFallbackLocale",
            "voiceLocaleAuthority",
            "voiceLocaleMayBeDerivedFromDisplay",
            "unsupportedDisplayLocaleDisposition");
        RequireSequence(
            ReadStringArray(
                policy,
                "supportedDisplayLocales",
                "localePolicy"),
            SupportedDisplayLocales,
            "supported display locales");
        RequireString(
            policy,
            "gameDisplayLocaleAuthority",
            "TypedDisplayLocale",
            "localePolicy");
        RequireString(
            policy,
            "workbenchDisplayLocaleAuthority",
            "ConversationAdaptive",
            "localePolicy");
        RequireBoolean(
            policy,
            "workbenchRequiresExplicitPolicy",
            expected: true,
            "localePolicy");
        RequireSequence(
            ReadStringArray(
                policy,
                "workbenchConversationAdaptivePriority",
                "localePolicy"),
            WorkbenchConversationAdaptivePriority,
            "Workbench ConversationAdaptive priority");
        RequireSequence(
            ReadStringArray(
                policy,
                "workbenchNonSwitchSignals",
                "localePolicy"),
            WorkbenchNonSwitchSignals,
            "Workbench language non-switch signals");
        RequireString(
            policy,
            "workbenchUnestablishedFallbackLocale",
            "zh-Hant-TW",
            "localePolicy");
        RequireString(
            policy,
            "voiceLocaleAuthority",
            "IndependentTypedVoiceLocale",
            "localePolicy");
        RequireBoolean(
            policy,
            "voiceLocaleMayBeDerivedFromDisplay",
            expected: false,
            "localePolicy");
        RequireString(
            policy,
            "unsupportedDisplayLocaleDisposition",
            "ExplicitFallbackPolicyRequired",
            "localePolicy");
    }

    private static IReadOnlyDictionary<string, LilithVoiceProjectionV1>
        LoadVoiceProjections(
            string root,
            JsonElement projectionReferences)
    {
        if (projectionReferences.ValueKind != JsonValueKind.Array)
            throw Invalid("voiceProjections must be an array.");

        var references = projectionReferences.EnumerateArray().ToArray();
        if (references.Length != ProjectionDescriptors.Length)
            throw Invalid("voiceProjections must contain exactly four locales.");

        var result = new Dictionary<string, LilithVoiceProjectionV1>(
            StringComparer.Ordinal);
        for (var index = 0; index < ProjectionDescriptors.Length; index++)
        {
            var expected = ProjectionDescriptors[index];
            var reference = references[index];
            RequireExactProperties(
                reference,
                $"voiceProjections[{index}]",
                "locale",
                "projectionId",
                "file",
                "sha256");
            RequireString(
                reference,
                "locale",
                expected.Locale,
                $"voiceProjections[{index}]");
            RequireString(
                reference,
                "projectionId",
                expected.ProjectionId,
                $"voiceProjections[{index}]");
            RequireString(
                reference,
                "file",
                expected.File,
                $"voiceProjections[{index}]");
            RequireString(
                reference,
                "sha256",
                expected.Sha256,
                $"voiceProjections[{index}]");

            var bytes = ValidateAsset(
                root,
                expected.File,
                expected.Sha256,
                expected.Locale + " voice projection");
            var projection = ParseVoiceProjection(bytes, expected);
            if (!result.TryAdd(expected.Locale, projection))
                throw Invalid("Duplicate voice projection locale.");
        }

        return new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(
            result);
    }

    private static LilithVoiceProjectionV1 ParseVoiceProjection(
        byte[] bytes,
        ProjectionDescriptor expected)
    {
        using var document = ParseJson(bytes, expected.File);
        var projection = document.RootElement;
        RequireExactProperties(
            projection,
            expected.File,
            "schemaVersion",
            "packageId",
            "voiceContractId",
            "voiceContractVersion",
            "projectionId",
            "locale",
            "localeLabel",
            "semanticInstructions");
        RequireInt(projection, "schemaVersion", 1, expected.File);
        RequireString(projection, "packageId", PackageId, expected.File);
        RequireString(
            projection,
            "voiceContractId",
            VoiceContractId,
            expected.File);
        RequireInt(
            projection,
            "voiceContractVersion",
            VoiceContractVersion,
            expected.File);
        RequireString(
            projection,
            "projectionId",
            expected.ProjectionId,
            expected.File);
        RequireString(
            projection,
            "locale",
            expected.Locale,
            expected.File);
        var label = ReadRequiredString(
            projection,
            "localeLabel",
            expected.File);

        var instructionElements = projection
            .GetProperty("semanticInstructions")
            .EnumerateArray()
            .ToArray();
        if (instructionElements.Length != SemanticInstructionIds.Count)
        {
            throw Invalid(
                expected.File + " does not cover every semantic instruction.");
        }

        var instructions = new LilithVoiceInstructionV1[
            instructionElements.Length];
        for (var index = 0; index < instructionElements.Length; index++)
        {
            var element = instructionElements[index];
            RequireExactProperties(
                element,
                $"{expected.File}.semanticInstructions[{index}]",
                "semanticId",
                "instruction");
            var semanticId = ReadRequiredString(
                element,
                "semanticId",
                expected.File);
            if (!string.Equals(
                    semanticId,
                    SemanticInstructionIds[index],
                    StringComparison.Ordinal))
            {
                throw Invalid(
                    expected.File + " semantic coverage order drifted.");
            }
            var instruction = ReadRequiredString(
                element,
                "instruction",
                expected.File);
            instructions[index] = new LilithVoiceInstructionV1(
                semanticId,
                instruction);
        }

        return new LilithVoiceProjectionV1(
            expected.Locale,
            expected.ProjectionId,
            label,
            expected.File,
            expected.Sha256,
            Array.AsReadOnly(instructions));
    }

    private static void ValidateHermesDescriptor(JsonElement hermes)
    {
        RequireExactProperties(
            hermes,
            "hermesPersonality",
            "personalityId",
            "assetId",
            "nativeConfigPath",
            "file",
            "sha256",
            "compositionMode",
            "composedSha256");
        RequireString(
            hermes,
            "personalityId",
            "lilith",
            "hermesPersonality");
        RequireString(
            hermes,
            "assetId",
            HermesPersonalityAssetId,
            "hermesPersonality");
        RequireString(
            hermes,
            "nativeConfigPath",
            "agent.personalities.lilith.system_prompt",
            "hermesPersonality");
        RequireString(
            hermes,
            "file",
            HermesPersonalityFile,
            "hermesPersonality");
        RequireString(
            hermes,
            "sha256",
            HermesPersonalitySha256,
            "hermesPersonality");
        RequireString(
            hermes,
            "compositionMode",
            HermesPersonalityCompositionMode,
            "hermesPersonality");
        RequireString(
            hermes,
            "composedSha256",
            HermesPersonalityContentSha256,
            "hermesPersonality");
    }

    private static IReadOnlyList<string> ExpectedFingerprintMaterial()
    {
        var material = new List<string>
        {
            "schema:1",
            "package:" + PackageId + "@" + PackageVersion,
            "canon:" + CanonProfileId + "@" + CanonProfileVersion,
            "canon-semantic:" + CanonSemanticHash,
            "soul:" + SoulAssetId,
            "soul-sha256:" + SoulAssetSha256
        };
        material.AddRange(ProjectionDescriptors.Select(static projection =>
            "projection:" + projection.Locale
            + ":" + projection.ProjectionId
            + ":" + projection.Sha256));
        material.Add("hermes-personality:" + HermesPersonalityAssetId);
        material.Add(
            "hermes-personality-sha256:" + HermesPersonalitySha256);
        material.Add(
            "hermes-personality-composition:"
            + HermesPersonalityCompositionMode);
        material.Add(
            "hermes-personality-composed-sha256:"
            + HermesPersonalityContentSha256);
        material.AddRange(StableDispositions.Select(static value =>
            "disposition:" + value));
        material.AddRange(CanonicalTensions.Select(static value =>
            "tension:" + value));
        material.AddRange(InterpretiveBoundaries.Select(static value =>
            "boundary:" + value));
        material.AddRange(UnresolvedQuestions.Select(static value =>
            "unknown:" + value));
        material.Add("voice:" + VoiceContractId + "@" + VoiceContractVersion);
        material.AddRange(ResponseDomains.Select(static value =>
            "domain:" + value));
        material.AddRange(ProhibitedStyles.Select(static value =>
            "prohibited:" + value));
        material.Add("emoji:SparseContextual");
        material.Add("emoji:DefaultOff");
        material.Add("emoji:MaximumPerReply=1");
        material.Add("emoji:OffFor=Refusal,Uncertainty,Technical,Serious");
        material.AddRange(SupportedDisplayLocales.Select(static value =>
            "locale:" + value));
        material.Add("game-display-locale:TypedDisplayLocale");
        material.Add(
            "workbench-display-locale:ConversationAdaptiveWithExplicitPolicy");
        material.Add(
            "workbench-adaptive-priority:"
            + string.Join('>', WorkbenchConversationAdaptivePriority));
        material.Add(
            "workbench-non-switch-signals:"
            + string.Join(',', WorkbenchNonSwitchSignals));
        material.Add("workbench-unestablished-fallback:zh-Hant-TW");
        material.Add("voice-locale:IndependentTypedVoiceLocale");
        material.Add("voice-locale-derived-from-display:false");
        material.Add(
            "unsupported-display-locale:ExplicitFallbackPolicyRequired");
        material.AddRange(SemanticInstructionIds.Select(static value =>
            "instruction:" + value));
        return material;
    }

    private static byte[] ValidateAsset(
        string root,
        string file,
        string expectedHash,
        string label)
    {
        if (!string.Equals(Path.GetFileName(file), file, StringComparison.Ordinal))
            throw Invalid(label + " filename is not a package-local leaf.");

        var path = Path.GetFullPath(Path.Combine(root, file));
        var expectedParent = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!string.Equals(
                Path.GetDirectoryName(path),
                expectedParent,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(label + " escaped its package directory.");
        }

        var bytes = ReadStrictUtf8Asset(path);
        var actualHash = Hash(bytes);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            throw Invalid(label + " SHA-256 drifted.");
        return bytes;
    }

    private static byte[] ReadStrictUtf8Asset(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "Identity package asset could not be read: "
                + Path.GetFileName(path),
                exception);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            throw Invalid(Path.GetFileName(path) + " must be UTF-8 without BOM.");
        try
        {
            _ = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                Path.GetFileName(path) + " is not strict UTF-8.",
                exception);
        }
        return bytes;
    }

    private static JsonDocument ParseJson(byte[] bytes, string label)
    {
        try
        {
            return JsonDocument.Parse(
                bytes,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(label + " is not strict JSON.", exception);
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string label,
        params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid(label + " must be an object.");
        var actual = element.EnumerateObject()
            .Select(static property => property.Name)
            .ToArray();
        if (actual.Length != expected.Length
            || actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
            || expected.Except(actual, StringComparer.Ordinal).Any()
            || actual.Except(expected, StringComparer.Ordinal).Any())
        {
            throw Invalid(label + " property set drifted.");
        }
    }

    private static void RequireString(
        JsonElement element,
        string property,
        string expected,
        string label)
    {
        var actual = ReadRequiredString(element, property, label);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw Invalid(label + "." + property + " drifted.");
    }

    private static string ReadRequiredString(
        JsonElement element,
        string property,
        string label)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw Invalid(label + "." + property + " must be a string.");
        }
        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
            throw Invalid(label + "." + property + " cannot be empty.");
        return result;
    }

    private static void RequireInt(
        JsonElement element,
        string property,
        int expected,
        string label)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var actual)
            || actual != expected)
        {
            throw Invalid(label + "." + property + " drifted.");
        }
    }

    private static void RequireBoolean(
        JsonElement element,
        string property,
        bool expected,
        string label)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != (expected
                ? JsonValueKind.True
                : JsonValueKind.False))
        {
            throw Invalid(label + "." + property + " drifted.");
        }
    }

    private static string[] ReadStringArray(
        JsonElement element,
        string property,
        string label)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(label + "." + property + " must be an array.");
        }
        var values = value.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw Invalid(
                    label + "." + property + "[" + index
                    + "] must be a non-empty string.");
            }
            return item.GetString()!;
        }).ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw Invalid(label + "." + property + " contains duplicates.");
        return values;
    }

    private static void RequireSequence(
        IEnumerable<string> actual,
        IEnumerable<string> expected,
        string label)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw Invalid(label + " drifted.");
    }

    private static void RequireContentMarker(
        string content,
        string marker,
        string label)
    {
        if (!content.Contains(marker, StringComparison.Ordinal))
            throw Invalid(label + " is missing marker " + marker + ".");
    }

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static InvalidDataException Invalid(string message)
        => new("Lilith Identity Package v1 rejected: " + message);

    private sealed record ProjectionDescriptor(
        string Locale,
        string ProjectionId,
        string File,
        string Sha256);
}
