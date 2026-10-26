using System;
using System.Linq;

namespace LilithProviderControlPlane;

internal enum D045AgentToolModelCompatibility
{
    Invalid = 0,
    NotApplicable = 1,
    Ineligible = 2,
    CatalogAdvertised = 3,
    RuntimeAdapterCatalogUnverified = 4
}

internal static class D045WorkbenchModelCatalogRules
{
    internal static bool IsSelectableLiveTextModel(
        UnifiedProviderConfiguredInstance instance,
        string modelId)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (string.IsNullOrWhiteSpace(modelId)
            || !instance.CatalogModels.Contains(modelId, StringComparer.Ordinal)
            || UnifiedProviderProfileRegistry.IsHardRejectedModel(
                instance.ProviderKind,
                modelId))
        {
            return false;
        }

        var profile = UnifiedProviderProfileRegistry.Get(instance.ProviderKind);
        if (profile.CredentialRequirement == ProviderCredentialRequirement.Required
            && !instance.CredentialConfigured)
        {
            return false;
        }

        var exactEvidence = instance.CatalogCapabilityEvidence
            .Where(value => string.Equals(
                value.ModelId,
                modelId,
                StringComparison.Ordinal))
            .ToArray();
        if (exactEvidence.Length > 0)
        {
            return UnifiedProviderCatalogCapabilityRules
                .AdvertisesTextConversation(exactEvidence, modelId);
        }
        return IsTextGenerationModel(instance.ProviderKind, modelId);
    }

    internal static D045AgentToolModelCompatibility
        GetAgentToolModelCompatibility(
            UnifiedProviderConfiguredInstance instance,
            string modelId,
            DateTimeOffset now)
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance));
        if (!IsSelectableLiveTextModel(instance, modelId))
            return D045AgentToolModelCompatibility.Ineligible;

        var exactEvidence = instance.CatalogCapabilityEvidence
            .Where(value => string.Equals(
                value.ModelId,
                modelId,
                StringComparison.Ordinal))
            .ToArray();
        var catalogAdvertisesTools = instance.CatalogIsCurrent(now)
            && exactEvidence.Any(value =>
                value.Kind == ProviderModelCapabilityKind.ToolCalling
                && value.State == ProviderCapabilityState.Advertised
                && value.Provenance is
                    ProviderCapabilityProvenance.OfficialModelCatalog
                    or ProviderCapabilityProvenance
                        .OfficialModelMetadataEndpoint);
        if (catalogAdvertisesTools)
            return D045AgentToolModelCompatibility.CatalogAdvertised;

        var protocol = UnifiedProviderProfileRegistry
            .Get(instance.ProviderKind)
            .Protocol;
        return HasAgentToolRuntimeAdapter(protocol)
            ? D045AgentToolModelCompatibility
                .RuntimeAdapterCatalogUnverified
            : D045AgentToolModelCompatibility.Ineligible;
    }

    internal static bool IsSelectableHigherReasoningModel(
        UnifiedProviderConfiguredInstance instance,
        string modelId,
        DateTimeOffset now)
        => GetAgentToolModelCompatibility(instance, modelId, now) is
            D045AgentToolModelCompatibility.CatalogAdvertised
            or D045AgentToolModelCompatibility
                .RuntimeAdapterCatalogUnverified;

    internal static bool HasAgentToolRuntimeAdapter(
        ProviderControlProtocol protocol)
        => protocol is ProviderControlProtocol.GeminiGenerateContent
            or ProviderControlProtocol.OpenAiChatCompletions
            or ProviderControlProtocol.OpenAiCompatibleChat
            or ProviderControlProtocol.AnthropicMessages
            or ProviderControlProtocol.OllamaNativeChat;

    internal static bool IsTextGenerationModel(
        ProviderControlKind providerKind,
        string modelId)
    {
        if (!Enum.IsDefined(typeof(ProviderControlKind), providerKind)
            || providerKind == ProviderControlKind.Invalid
            || string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var value = modelId.Trim().ToLowerInvariant();
        if (value.Contains("embedding", StringComparison.Ordinal)
            || value.Contains("text-embedding", StringComparison.Ordinal)
            || value.Contains("imagen-", StringComparison.Ordinal)
            || value.Contains("veo-", StringComparison.Ordinal)
            || value.Contains("lyria-", StringComparison.Ordinal)
            || value.Contains("robotics", StringComparison.Ordinal)
            || value.Contains("image-edit", StringComparison.Ordinal)
            || value.Contains("qwen-image", StringComparison.Ordinal)
            || value.Contains("wan2", StringComparison.Ordinal)
            || value.Contains("z-image", StringComparison.Ordinal)
            || value.Contains("speech-", StringComparison.Ordinal)
            || value.Contains("-tts", StringComparison.Ordinal)
            || value.Contains("tts-", StringComparison.Ordinal)
            || value.Contains("-asr", StringComparison.Ordinal)
            || value.Contains("asr-", StringComparison.Ordinal)
            || value.Contains("realtime", StringComparison.Ordinal)
            || value.Contains("sre-gpu", StringComparison.Ordinal))
        {
            return false;
        }

        return UnifiedProviderProfileRegistry.TryGet(providerKind, out var profile)
            && profile?.Protocol is ProviderControlProtocol.GeminiGenerateContent
                or ProviderControlProtocol.OpenAiChatCompletions
                or ProviderControlProtocol.OpenAiCompatibleChat
                or ProviderControlProtocol.AnthropicMessages
                or ProviderControlProtocol.OllamaNativeChat;
    }
}
