using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record HermesNativeStartupCatalogAttestation(
    bool ApiKeyProviderConfigured,
    int CommandCount,
    int ModelProviderCount,
    HermesNativeToolsCatalog NativeTools,
    LilithRuntimeApprovedModelCatalog ApprovedModelCatalog);

internal sealed record HermesNativeModelCatalogAttestation(
    int ProviderCount,
    LilithRuntimeApprovedModelCatalog ApprovedModelCatalog);

internal static class HermesNativeStartupAttestation
{
    private const int MaximumCredentialProviders = 256;
    private const int MaximumCommands = 4096;
    private const int MaximumCommandCategories = 256;
    private const int MaximumCommandSubsets = 1024;
    private const int MaximumSkills = 4096;
    private const int MaximumModelProviders = 256;
    private const int MaximumModelsPerProvider = 4096;
    private const int MaximumTextUtf8Bytes = 16 * 1024;
    private const int MaximumTokenUtf8Bytes = 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly IReadOnlySet<string> ModelProviderProperties =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "slug",
            "name",
            "is_current",
            "is_user_defined",
            "models",
            "total_models",
            "source",
            "authenticated",
            "auth_type",
            "key_env",
            "warning",
            "api_url",
            "pricing",
            "free_tier",
            "unavailable_models",
            "capabilities",
            "featured_models"
        };

    internal static HermesNativeStartupCatalogAttestation Inspect(
        JsonElement credentialStatus,
        JsonElement commands,
        JsonElement models,
        JsonElement nativeTools)
    {
        var apiKeyProviderConfigured = InspectCredentialStatus(credentialStatus);
        var commandCount = InspectCommands(commands);
        var modelCatalog = InspectModelCatalog(models);
        HermesNativeToolsCatalog decodedTools;
        try
        {
            decodedTools = HermesNativeToolsContract.DecodeCatalog(nativeTools);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid native tool catalog.",
                exception);
        }

        if (decodedTools.Toolsets.Count == 0)
        {
            throw new InvalidDataException(
                "Hermes returned an empty native tool catalog.");
        }

        return new HermesNativeStartupCatalogAttestation(
            apiKeyProviderConfigured,
            commandCount,
            modelCatalog.ProviderCount,
            decodedTools,
            modelCatalog.ApprovedModelCatalog);
    }

    internal static HermesNativeModelCatalogAttestation InspectModelCatalog(
        JsonElement payload)
        => InspectModels(payload);

    internal static int InspectCommandCatalog(JsonElement payload)
        => InspectCommands(payload);

    private static bool InspectCredentialStatus(JsonElement payload)
    {
        var root = ExactObject(payload, "providers");
        var providers = RequiredArray(
            root,
            "providers",
            MaximumCredentialProviders,
            allowEmpty: false);
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var anyConfigured = false;
        foreach (var provider in providers.EnumerateArray())
        {
            _ = HermesCredentialRegionMetadata.Parse(provider);
            var values = ExactObject(provider, HermesCredentialRegionMetadata.RowKeys(provider));
            var slug = RequiredToken(values, "slug", MaximumTokenUtf8Bytes);
            if (!IsCredentialProviderSlug(slug) || !slugs.Add(slug))
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid credential provider catalog.");
            }
            _ = RequiredText(values, "name", MaximumTokenUtf8Bytes);
            anyConfigured |= RequiredBoolean(values, "configured");
            _ = RequiredBoolean(values, "legacy_sources_present");
        }
        return anyConfigured;
    }

    private static int InspectCommands(JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "pairs",
            "sub",
            "canon",
            "categories",
            "skills",
            "skill_count",
            "warning");
        var pairs = RequiredArray(
            root,
            "pairs",
            MaximumCommands,
            allowEmpty: false);
        foreach (var pair in pairs.EnumerateArray())
            InspectCommandPair(pair);

        var subsets = RequiredObject(root, "sub", MaximumCommandSubsets);
        foreach (var subset in subsets.EnumerateObject())
        {
            RequireCommandToken(subset.Name);
            if (subset.Value.ValueKind != JsonValueKind.Array
                || subset.Value.GetArrayLength() > MaximumCommands)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid command subset catalog.");
            }
            foreach (var item in subset.Value.EnumerateArray())
                _ = RequiredStringValue(item, MaximumTokenUtf8Bytes, allowEmpty: false);
        }

        var canonical = RequiredObject(root, "canon", MaximumCommands * 4);
        foreach (var alias in canonical.EnumerateObject())
        {
            RequireCommandToken(alias.Name);
            RequireCommandToken(RequiredStringValue(
                alias.Value,
                MaximumTokenUtf8Bytes,
                allowEmpty: false));
        }

        var categories = RequiredArray(
            root,
            "categories",
            MaximumCommandCategories,
            allowEmpty: false);
        foreach (var category in categories.EnumerateArray())
        {
            var values = ExactObject(category, "name", "pairs");
            _ = RequiredText(values, "name", MaximumTokenUtf8Bytes);
            var categoryPairs = RequiredArray(
                values,
                "pairs",
                MaximumCommands,
                allowEmpty: false);
            foreach (var pair in categoryPairs.EnumerateArray())
                InspectCommandPair(pair);
        }

        var skills = RequiredObject(root, "skills", MaximumSkills);
        foreach (var skill in skills.EnumerateObject())
        {
            RequireCommandToken(skill.Name);
            var values = ExactObject(skill.Value, "usage", "origin");
            _ = RequiredInteger(values, "usage", 0, int.MaxValue);
            _ = RequiredText(values, "origin", MaximumTokenUtf8Bytes);
        }
        var skillCount = RequiredInteger(
            root,
            "skill_count",
            0,
            MaximumSkills);
        if (skillCount != PropertyCount(skills))
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory skill command catalog.");
        }
        _ = RequiredText(
            root,
            "warning",
            MaximumTextUtf8Bytes,
            allowEmpty: true);
        return pairs.GetArrayLength();
    }

    private static HermesNativeModelCatalogAttestation InspectModels(
        JsonElement payload)
    {
        var root = ExactObject(payload, "providers", "model", "provider");
        var defaultModel = RequiredText(
            root,
            "model",
            MaximumTokenUtf8Bytes,
            allowEmpty: true);
        var defaultProvider = RequiredText(
            root,
            "provider",
            MaximumTokenUtf8Bytes,
            allowEmpty: true);
        var providers = RequiredArray(
            root,
            "providers",
            MaximumModelProviders,
            allowEmpty: false);
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var approvedProviders = new List<(
            string ProviderId,
            IEnumerable<string> ModelIds)>();
        foreach (var provider in providers.EnumerateArray())
        {
            var inspected = InspectModelProvider(provider, slugs);
            if (inspected.Models.Count != 0)
            {
                approvedProviders.Add((
                    inspected.Slug,
                    inspected.Models));
            }
        }

        if ((defaultProvider.Length == 0) != (defaultModel.Length == 0))
        {
            throw new InvalidDataException(
                "Hermes returned a half-empty default model selection.");
        }
        if (defaultProvider.Length != 0
            && !approvedProviders.Any(provider =>
                string.Equals(
                    provider.ProviderId,
                    defaultProvider,
                    StringComparison.Ordinal)
                && provider.ModelIds.Contains(
                    defaultModel,
                    StringComparer.Ordinal)))
        {
            // The root picker projection is display state, not executable
            // authority, unless the same exact pair is currently authenticated
            // and available. Keep recovery possible without inventing a
            // substitute from catalog order.
            defaultProvider = string.Empty;
            defaultModel = string.Empty;
        }

        LilithRuntimeApprovedModelCatalog approvedCatalog;
        try
        {
            approvedCatalog = new LilithRuntimeApprovedModelCatalog(
                defaultProvider,
                defaultModel,
                approvedProviders);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory default model selection.",
                exception);
        }

        return new HermesNativeModelCatalogAttestation(
            providers.GetArrayLength(),
            approvedCatalog);
    }

    private static (string Slug, IReadOnlyList<string> Models)
        InspectModelProvider(
        JsonElement provider,
        ISet<string> slugs)
    {
        var values = AllowedObject(
            provider,
            ModelProviderProperties,
            "slug",
            "name",
            "is_current",
            "is_user_defined",
            "models",
            "total_models",
            "source",
            "authenticated",
            "capabilities",
            "featured_models");
        var slug = RequiredToken(values, "slug", MaximumTokenUtf8Bytes);
        if (!IsModelProviderSlug(slug) || !slugs.Add(slug))
            throw new InvalidDataException("Hermes returned a duplicate model provider.");
        _ = RequiredText(values, "name", MaximumTokenUtf8Bytes);
        _ = RequiredBoolean(values, "is_current");
        _ = RequiredBoolean(values, "is_user_defined");
        var authenticated = RequiredBoolean(values, "authenticated");
        _ = RequiredText(values, "source", MaximumTokenUtf8Bytes);

        var models = RequiredStringArray(
            values,
            "models",
            MaximumModelsPerProvider,
            MaximumTokenUtf8Bytes);
        if (models.Any(static model => !IsSafeModelToken(model)))
        {
            throw new InvalidDataException(
                "Hermes returned an invalid model identifier.");
        }
        var modelIds = models.ToHashSet(StringComparer.Ordinal);
        if (modelIds.Count != models.Count
            || RequiredInteger(
                values,
                "total_models",
                0,
                MaximumModelsPerProvider) != models.Count)
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory model provider catalog.");
        }

        InspectOptionalText(values, "auth_type", MaximumTokenUtf8Bytes);
        InspectOptionalText(values, "key_env", MaximumTokenUtf8Bytes, allowEmpty: true);
        InspectOptionalText(values, "warning", MaximumTextUtf8Bytes, allowEmpty: true);
        InspectOptionalText(values, "api_url", MaximumTextUtf8Bytes, allowEmpty: true);
        InspectOptionalBoolean(values, "free_tier");
        _ = InspectOptionalModelSubset(values, "featured_models", modelIds);
        var unavailable = InspectOptionalModelSubset(
            values,
            "unavailable_models",
            modelIds);
        InspectCapabilities(values, modelIds);
        InspectPricing(values, modelIds);
        var executableModels = authenticated
            ? models.Where(model => !unavailable.Contains(model)).ToArray()
            : Array.Empty<string>();
        return (slug, executableModels);
    }

    private static void InspectCapabilities(
        IReadOnlyDictionary<string, JsonElement> values,
        IReadOnlySet<string> modelIds)
    {
        if (!values.TryGetValue("capabilities", out var capabilities)
            || capabilities.ValueKind != JsonValueKind.Object
            || PropertyCount(capabilities) > MaximumModelsPerProvider)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid model capability catalog.");
        }
        foreach (var capability in capabilities.EnumerateObject())
        {
            if (!modelIds.Contains(capability.Name))
            {
                throw new InvalidDataException(
                    "Hermes returned capability data for an unknown model.");
            }
            var row = ExactObject(capability.Value, "fast", "reasoning");
            _ = RequiredBoolean(row, "fast");
            _ = RequiredBoolean(row, "reasoning");
        }
    }

    private static void InspectPricing(
        IReadOnlyDictionary<string, JsonElement> values,
        IReadOnlySet<string> modelIds)
    {
        if (!values.TryGetValue("pricing", out var pricing))
            return;
        if (pricing.ValueKind != JsonValueKind.Object
            || PropertyCount(pricing) > MaximumModelsPerProvider)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid model pricing catalog.");
        }
        foreach (var price in pricing.EnumerateObject())
        {
            if (!modelIds.Contains(price.Name))
                throw new InvalidDataException("Hermes priced an unknown model.");
            var row = AllowedObject(
                price.Value,
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "input",
                    "output",
                    "cache",
                    "free",
                    "discount_percent",
                    "was_input",
                    "was_output"
                },
                "input",
                "output",
                "cache",
                "free");
            _ = RequiredText(row, "input", MaximumTokenUtf8Bytes, allowEmpty: true);
            _ = RequiredText(row, "output", MaximumTokenUtf8Bytes, allowEmpty: true);
            var cache = row["cache"];
            if (cache.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                throw new InvalidDataException("Hermes returned invalid cache pricing.");
            if (cache.ValueKind == JsonValueKind.String)
                _ = RequiredStringValue(cache, MaximumTokenUtf8Bytes, allowEmpty: true);
            _ = RequiredBoolean(row, "free");
            if (row.ContainsKey("discount_percent"))
                _ = RequiredInteger(row, "discount_percent", 0, 100);
            InspectOptionalText(row, "was_input", MaximumTokenUtf8Bytes, allowEmpty: true);
            InspectOptionalText(row, "was_output", MaximumTokenUtf8Bytes, allowEmpty: true);
        }
    }

    private static IReadOnlySet<string> InspectOptionalModelSubset(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        IReadOnlySet<string> modelIds)
    {
        if (!values.ContainsKey(property))
            return new HashSet<string>(StringComparer.Ordinal);
        var subset = RequiredStringArray(
            values,
            property,
            MaximumModelsPerProvider,
            MaximumTokenUtf8Bytes);
        if (subset.Distinct(StringComparer.Ordinal).Count() != subset.Count
            || subset.Any(model => !modelIds.Contains(model)))
        {
            throw new InvalidDataException(
                "Hermes returned an invalid model subset catalog.");
        }
        return subset.ToHashSet(StringComparer.Ordinal);
    }

    private static void InspectCommandPair(JsonElement pair)
    {
        if (pair.ValueKind != JsonValueKind.Array
            || pair.GetArrayLength() != 2)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid command catalog row.");
        }
        var values = pair.EnumerateArray().ToArray();
        RequireCommandToken(RequiredStringValue(
            values[0],
            MaximumTokenUtf8Bytes,
            allowEmpty: false));
        _ = RequiredStringValue(
            values[1],
            MaximumTextUtf8Bytes,
            allowEmpty: true);
    }

    private static Dictionary<string, JsonElement> ExactObject(
        JsonElement value,
        params string[] properties)
        => AllowedObject(
            value,
            properties.ToHashSet(StringComparer.Ordinal),
            properties);

    private static Dictionary<string, JsonElement> AllowedObject(
        JsonElement value,
        IReadOnlySet<string> allowed,
        params string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes returned a non-object catalog.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException(
                    "Hermes returned an unexpected catalog property.");
            }
        }
        if (required.Any(property => !values.ContainsKey(property)))
            throw new InvalidDataException("Hermes omitted a required catalog property.");
        return values;
    }

    private static JsonElement RequiredArray(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximum,
        bool allowEmpty)
    {
        var value = values[property];
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximum
            || (!allowEmpty && value.GetArrayLength() == 0))
        {
            throw new InvalidDataException("Hermes returned an invalid catalog array.");
        }
        return value;
    }

    private static JsonElement RequiredObject(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximumProperties)
    {
        var value = values[property];
        if (value.ValueKind != JsonValueKind.Object
            || PropertyCount(value) > maximumProperties)
        {
            throw new InvalidDataException("Hermes returned an invalid catalog object.");
        }
        return value;
    }

    private static List<string> RequiredStringArray(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximumItems,
        int maximumUtf8Bytes)
    {
        var array = RequiredArray(
            values,
            property,
            maximumItems,
            allowEmpty: true);
        return array.EnumerateArray()
            .Select(value => RequiredStringValue(
                value,
                maximumUtf8Bytes,
                allowEmpty: false))
            .ToList();
    }

    private static string RequiredToken(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximumUtf8Bytes)
        => RequiredStringValue(values[property], maximumUtf8Bytes, allowEmpty: false);

    private static string RequiredText(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximumUtf8Bytes,
        bool allowEmpty = false)
        => RequiredStringValue(values[property], maximumUtf8Bytes, allowEmpty);

    private static string RequiredStringValue(
        JsonElement value,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        if (value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || (!allowEmpty && string.IsNullOrWhiteSpace(text))
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException("Hermes returned invalid catalog text.");
        }
        return text;
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string property)
    {
        var value = values[property];
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Hermes returned invalid catalog state.");
        return value.GetBoolean();
    }

    private static int RequiredInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int minimum,
        int maximum)
    {
        var value = values[property];
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number)
            || number < minimum
            || number > maximum)
        {
            throw new InvalidDataException("Hermes returned an invalid catalog count.");
        }
        return number;
    }

    private static void InspectOptionalText(
        IReadOnlyDictionary<string, JsonElement> values,
        string property,
        int maximumUtf8Bytes,
        bool allowEmpty = false)
    {
        if (values.TryGetValue(property, out var value))
            _ = RequiredStringValue(value, maximumUtf8Bytes, allowEmpty);
    }

    private static void InspectOptionalBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string property)
    {
        if (values.ContainsKey(property))
            _ = RequiredBoolean(values, property);
    }

    private static void RequireCommandToken(string value)
    {
        if (!value.StartsWith("/", StringComparison.Ordinal)
            || value.Any(char.IsWhiteSpace))
        {
            throw new InvalidDataException("Hermes returned an invalid command token.");
        }
    }

    private static bool IsCredentialProviderSlug(string value)
        => value.Length > 0
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.Skip(1).All(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9'
                or '-' or '_');

    private static bool IsModelProviderSlug(string value)
        => IsCredentialProviderSlug(value)
            || value.StartsWith("custom:", StringComparison.Ordinal)
                && IsCredentialProviderSlug(value[7..]);

    private static bool IsSafeModelToken(string value)
        => value.Length > 0
            && value[0] != '-'
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.' or '/' or ':' or '@' or '+');

    private static int PropertyCount(JsonElement value)
        => value.EnumerateObject().Count();
}
