using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

public enum HermesNativeApprovalMode
{
    Manual = 1,
    Smart = 2,
    Off = 3
}

public enum HermesNativeToolsetReadiness
{
    Ready = 1,
    NeedsSetup = 2,
    NeedsAuth = 3,
    NeedsKeys = 4
}

public sealed class HermesNativeToolsetCatalogEntry
{
    private readonly IReadOnlyList<string> _tools;

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("label")]
    public string Label { get; }

    [JsonPropertyName("description")]
    public string Description { get; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; }

    [JsonPropertyName("configured")]
    public bool Configured { get; }

    [JsonIgnore]
    public HermesNativeToolsetReadiness Readiness { get; }

    [JsonPropertyName("readiness")]
    public string ReadinessToken =>
        HermesNativeToolsContract.ReadinessToken(Readiness);

    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools => _tools;

    [JsonPropertyName("setup_providers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HermesNativeSetupProvider>? SetupProviders { get; }

    public HermesNativeToolsetCatalogEntry(
        string name,
        string label,
        string description,
        bool enabled,
        bool configured,
        HermesNativeToolsetReadiness readiness,
        IEnumerable<string> tools,
        IReadOnlyList<HermesNativeSetupProvider>? setupProviders = null)
    {
        Name = HermesNativeToolsContract.Token(
            name,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(name));
        Label = HermesNativeToolsContract.Text(
            label,
            HermesNativeToolsContract.MaximumToolsetLabelUtf8Bytes,
            nameof(label));
        Description = HermesNativeToolsContract.Text(
            description,
            HermesNativeToolsContract.MaximumToolsetDescriptionUtf8Bytes,
            nameof(description),
            allowEmpty: true);
        if (!Enum.IsDefined(typeof(HermesNativeToolsetReadiness), readiness))
            throw new ArgumentOutOfRangeException(nameof(readiness));
        if (configured != (readiness == HermesNativeToolsetReadiness.Ready))
        {
            throw new ArgumentException(
                "Hermes toolset configured state must match native readiness.",
                nameof(configured));
        }
        Enabled = enabled;
        Configured = configured;
        Readiness = readiness;
        SetupProviders = HermesNativeToolsContract.ValidateSetupProviders(setupProviders);
        _tools = HermesNativeToolsContract.UniqueTokens(
            tools,
            HermesNativeToolsContract.MaximumToolsPerToolset,
            HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
            nameof(tools));
    }
}

public sealed class HermesNativeToolsCatalog
{
    private readonly IReadOnlyList<string> _enabledToolsets;
    private readonly IReadOnlyList<HermesNativeToolsetCatalogEntry> _toolsets;

    [JsonPropertyName("platform")]
    public string Platform => "cli";

    [JsonIgnore]
    public HermesNativeApprovalMode ApprovalMode { get; }

    [JsonPropertyName("approval_mode")]
    public string ApprovalModeToken =>
        HermesNativeToolsContract.ApprovalModeToken(ApprovalMode);

    [JsonPropertyName("enabled_toolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonPropertyName("toolsets")]
    public IReadOnlyList<HermesNativeToolsetCatalogEntry> Toolsets => _toolsets;

    [JsonPropertyName("mcp_catalog")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HermesNativeMcpCatalogEntry>? McpCatalog { get; }

    [JsonPropertyName("presets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HermesNativeToolPreset>? Presets { get; }

    [JsonPropertyName("tool_discovery_enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ToolDiscoveryEnabled { get; }

    [JsonPropertyName("new_session_required")]
    public bool NewSessionRequired => false;

    // Preserve the constructor referenced by already-deployed consumers.
    public HermesNativeToolsCatalog(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        IEnumerable<HermesNativeToolsetCatalogEntry> toolsets,
        IReadOnlyList<HermesNativeMcpCatalogEntry>? mcpCatalog)
        : this(approvalMode, enabledToolsets, toolsets, mcpCatalog, null, null) { }

    public HermesNativeToolsCatalog(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        IEnumerable<HermesNativeToolsetCatalogEntry> toolsets,
        IReadOnlyList<HermesNativeMcpCatalogEntry>? mcpCatalog = null,
        IReadOnlyList<HermesNativeToolPreset>? presets = null,
        bool? toolDiscoveryEnabled = null)
    {
        if (!Enum.IsDefined(typeof(HermesNativeApprovalMode), approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        ApprovalMode = approvalMode;
        _enabledToolsets = HermesNativeToolsContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        if (toolsets is null)
            throw new ArgumentNullException(nameof(toolsets));
        var rows = toolsets.ToArray();
        if (rows.Length > HermesNativeToolsContract.MaximumToolsets
            || rows.Any(static row => row is null))
        {
            throw new ArgumentOutOfRangeException(nameof(toolsets));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!names.Add(row.Name))
                throw new ArgumentException(
                    "Hermes toolset catalog names must be unique.",
                    nameof(toolsets));
        }
        var enabled = _enabledToolsets.ToHashSet(StringComparer.Ordinal);
        if (rows.Any(row => row.Enabled != enabled.Contains(row.Name))
            || enabled.Any(name => !names.Contains(name)))
        {
            throw new ArgumentException(
                "Hermes enabled toolsets must exactly match catalog row state.",
                nameof(enabledToolsets));
        }
        _toolsets = Array.AsReadOnly(rows);
        McpCatalog = HermesNativeToolsContract.ValidateMcpCatalog(mcpCatalog);
        Presets = HermesNativeToolsContract.ValidatePresets(presets, names);
        ToolDiscoveryEnabled = toolDiscoveryEnabled;
    }
}

public sealed class HermesNativeToolsSetRequest
{
    private readonly IReadOnlyList<string> _enabledToolsets;

    [JsonPropertyName("enabled_toolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonIgnore]
    public HermesNativeApprovalMode ApprovalMode { get; }

    [JsonPropertyName("approval_mode")]
    public string ApprovalModeToken =>
        HermesNativeToolsContract.ApprovalModeToken(ApprovalMode);

    [JsonPropertyName("setup_selection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HermesNativeSetupSelection? SetupSelection { get; }

    [JsonPropertyName("mcp_setup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HermesNativeMcpSetupSelection? McpSetup { get; }

    [JsonPropertyName("tool_discovery_enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ToolDiscoveryEnabled { get; }

    // Optional arguments are compiled into call sites; retain the prior signature.
    public HermesNativeToolsSetRequest(
        IEnumerable<string> enabledToolsets,
        HermesNativeApprovalMode approvalMode,
        HermesNativeSetupSelection? setupSelection,
        HermesNativeMcpSetupSelection? mcpSetup)
        : this(enabledToolsets, approvalMode, setupSelection, mcpSetup, null) { }

    public HermesNativeToolsSetRequest(
        IEnumerable<string> enabledToolsets,
        HermesNativeApprovalMode approvalMode,
        HermesNativeSetupSelection? setupSelection = null,
        HermesNativeMcpSetupSelection? mcpSetup = null,
        bool? toolDiscoveryEnabled = null)
    {
        _enabledToolsets = HermesNativeToolsContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        if (!Enum.IsDefined(typeof(HermesNativeApprovalMode), approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        ApprovalMode = approvalMode;
        SetupSelection = setupSelection;
        if (setupSelection is not null && mcpSetup is not null)
            throw new InvalidDataException("Native setup transactions cannot be combined.");
        McpSetup = mcpSetup;
        ToolDiscoveryEnabled = toolDiscoveryEnabled;
    }
}

public sealed class HermesNativeToolsSetResult
{
    private readonly IReadOnlyList<string> _enabledToolsets;

    [JsonPropertyName("platform")]
    public string Platform => "cli";

    [JsonIgnore]
    public HermesNativeApprovalMode ApprovalMode { get; }

    [JsonPropertyName("approval_mode")]
    public string ApprovalModeToken =>
        HermesNativeToolsContract.ApprovalModeToken(ApprovalMode);

    [JsonPropertyName("enabled_toolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonPropertyName("config_sha256")]
    public string ConfigSha256 { get; }

    [JsonPropertyName("setup_selection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HermesNativeSetupSelection? SetupSelection { get; }

    [JsonPropertyName("mcp_setup")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HermesNativeMcpSetupSelection? McpSetup { get; }

    [JsonPropertyName("tool_discovery_enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ToolDiscoveryEnabled { get; }

    [JsonPropertyName("new_session_required")]
    public bool NewSessionRequired => true;

    public HermesNativeToolsSetResult(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        string configSha256,
        HermesNativeSetupSelection? setupSelection,
        HermesNativeMcpSetupSelection? mcpSetup)
        : this(approvalMode, enabledToolsets, configSha256, setupSelection, mcpSetup, null) { }

    public HermesNativeToolsSetResult(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        string configSha256,
        HermesNativeSetupSelection? setupSelection = null,
        HermesNativeMcpSetupSelection? mcpSetup = null,
        bool? toolDiscoveryEnabled = null)
    {
        if (!Enum.IsDefined(typeof(HermesNativeApprovalMode), approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        ApprovalMode = approvalMode;
        _enabledToolsets = HermesNativeToolsContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        HermesNativeToolsContract.RequireCanonicalToolsetOrder(
            _enabledToolsets,
            nameof(enabledToolsets));
        ConfigSha256 = HermesNativeToolsContract.Sha256(
            configSha256,
            nameof(configSha256));
        SetupSelection = setupSelection;
        if (setupSelection is not null && mcpSetup is not null)
            throw new InvalidDataException("Native setup transactions cannot be combined.");
        McpSetup = mcpSetup;
        ToolDiscoveryEnabled = toolDiscoveryEnabled;
    }
}

public sealed class HermesNativeApprovalPersistenceReceipt
{
    private readonly IReadOnlyList<string> _enabledToolsets;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion => 1;

    [JsonPropertyName("source")]
    public string Source => HermesNativeToolsContract.ApprovalPersistenceSource;

    [JsonPropertyName("previous_config_sha256")]
    public string PreviousConfigSha256 { get; }

    [JsonPropertyName("config_sha256")]
    public string ConfigSha256 { get; }

    [JsonPropertyName("commit_nonce")]
    public string CommitNonce { get; }

    [JsonIgnore]
    public HermesNativeApprovalMode ApprovalMode { get; }

    [JsonPropertyName("approval_mode")]
    public string ApprovalModeToken =>
        HermesNativeToolsContract.ApprovalModeToken(ApprovalMode);

    [JsonPropertyName("enabled_toolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonPropertyName("new_session_required")]
    public bool NewSessionRequired => true;

    public HermesNativeApprovalPersistenceReceipt(
        string previousConfigSha256,
        string configSha256,
        string commitNonce,
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets)
    {
        PreviousConfigSha256 = HermesNativeToolsContract.Sha256(
            previousConfigSha256,
            nameof(previousConfigSha256));
        ConfigSha256 = HermesNativeToolsContract.Sha256(
            configSha256,
            nameof(configSha256));
        CommitNonce = HermesNativeToolsContract.Sha256(
            commitNonce,
            nameof(commitNonce));
        if (!Enum.IsDefined(typeof(HermesNativeApprovalMode), approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        ApprovalMode = approvalMode;
        _enabledToolsets = HermesNativeToolsContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        HermesNativeToolsContract.RequireCanonicalToolsetOrder(
            _enabledToolsets,
            nameof(enabledToolsets));
    }
}

public sealed class HermesNativeApprovalResponseResult
{
    internal HermesNativeApprovalResponseResult(int resolved)
    {
        Resolved = resolved;
    }

    [JsonPropertyName("resolved")]
    public int Resolved { get; }
}

public sealed class HermesNativeApprovalPrepareResult
{
    [JsonPropertyName("prepared")]
    public int Prepared => 1;

    [JsonPropertyName("persistence")]
    public HermesNativeApprovalPersistenceReceipt Persistence { get; }

    public HermesNativeApprovalPrepareResult(
        HermesNativeApprovalPersistenceReceipt persistence)
    {
        Persistence = persistence
            ?? throw new ArgumentNullException(nameof(persistence));
    }
}

public sealed class HermesNativeEffectPolicy
{
    private readonly IReadOnlyList<string> _enabledToolsets;

    [JsonPropertyName("mode")]
    public string Mode => "hermes_native_config";

    [JsonPropertyName("approvalMode")]
    public string ApprovalMode { get; }

    [JsonPropertyName("enabledToolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonPropertyName("newSessionRequired")]
    public bool NewSessionRequired { get; }

    public HermesNativeEffectPolicy(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        bool newSessionRequired)
    {
        ApprovalMode = HermesNativeToolsContract.ApprovalModeToken(approvalMode);
        _enabledToolsets = HermesNativeToolsContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        NewSessionRequired = newSessionRequired;
    }
}

public static partial class HermesNativeToolsContract
{
    public const string ApprovalPersistenceSource =
        "hermes.approval.respond.always";
    public const string NativeConfigCommitAuthority =
        "lilith-runtime-native-config.v1";
    public const string ApprovalCommitAuthority = NativeConfigCommitAuthority;
    public const int MaximumToolsets = 128;
    public const int MaximumToolsPerToolset = 1024;
    public const int MaximumToolsetNameUtf8Bytes = 256;
    public const int MaximumToolNameUtf8Bytes = 256;
    public const int MaximumToolsetLabelUtf8Bytes = 1024;
    public const int MaximumToolsetDescriptionUtf8Bytes = 16 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static HermesNativeToolsCatalog DecodeCatalog(JsonElement payload)
    {
        var optional = new[] { "mcp_catalog", "presets", "tool_discovery_enabled" }.Where(name =>
            payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out _));
        var values = ExactObject(payload, new[] { "platform", "approval_mode", "enabled_toolsets",
            "toolsets", "new_session_required" }.Concat(optional).ToArray());
        RequireLiteral(values, "platform", "cli");
        var approvalMode = ParseApprovalMode(RequiredString(values, "approval_mode"));
        var enabled = StringArray(
            values["enabled_toolsets"],
            MaximumToolsets,
            MaximumToolsetNameUtf8Bytes,
            "enabled_toolsets");
        if (values["toolsets"].ValueKind != JsonValueKind.Array
            || values["toolsets"].GetArrayLength() > MaximumToolsets)
        {
            throw new InvalidDataException("Hermes toolset catalog was rejected.");
        }
        var rows = new List<HermesNativeToolsetCatalogEntry>();
        foreach (var item in values["toolsets"].EnumerateArray())
        {
            var row = ExactObjectWithOptionalSetup(
                item,
                "setup_providers",
                "name",
                "label",
                "description",
                "enabled",
                "configured",
                "readiness",
                "tools");
            rows.Add(new HermesNativeToolsetCatalogEntry(
                RequiredString(row, "name"),
                RequiredString(row, "label"),
                RequiredString(row, "description", allowEmpty: true),
                RequiredBoolean(row, "enabled"),
                RequiredBoolean(row, "configured"),
                ParseReadiness(RequiredString(row, "readiness")),
                StringArray(
                    row["tools"],
                    MaximumToolsPerToolset,
                    MaximumToolNameUtf8Bytes,
                    "tools"),
                DecodeSetupProviders(item)));
        }
        if (RequiredBoolean(values, "new_session_required"))
        {
            throw new InvalidDataException(
                "Hermes tools.catalog unexpectedly required a new session.");
        }
        return new HermesNativeToolsCatalog(approvalMode, enabled, rows, DecodeMcpCatalog(payload),
            DecodePresets(payload, rows.Select(row => row.Name)), DecodeToolDiscoveryEnabled(payload));
    }

    public static HermesNativeToolsSetRequest DecodeSetRequest(JsonElement payload)
    {
        var values = ExactSetupMutationObject(payload, "enabled_toolsets", "approval_mode");
        return new HermesNativeToolsSetRequest(
            StringArray(
                values["enabled_toolsets"],
                MaximumToolsets,
                MaximumToolsetNameUtf8Bytes,
                "enabled_toolsets"),
            ParseApprovalMode(RequiredString(values, "approval_mode")),
            DecodeSetupSelection(payload), DecodeMcpSetup(payload), DecodeToolDiscoveryEnabled(payload));
    }

    public static JsonElement EncodeSetRequest(HermesNativeToolsSetRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        return JsonSerializer.SerializeToElement(request);
    }

    public static HermesNativeToolsSetResult DecodeSetResult(JsonElement payload)
    {
        var values = ExactSetupMutationObject(
            payload,
            "platform",
            "approval_mode",
            "enabled_toolsets",
            "config_sha256",
            "new_session_required");
        RequireLiteral(values, "platform", "cli");
        if (!RequiredBoolean(values, "new_session_required"))
        {
            throw new InvalidDataException(
                "Hermes tools.set did not require a new session.");
        }
        return new HermesNativeToolsSetResult(
            ParseApprovalMode(RequiredString(values, "approval_mode")),
            StringArray(
                values["enabled_toolsets"],
                MaximumToolsets,
                MaximumToolsetNameUtf8Bytes,
                "enabled_toolsets"),
            RequiredString(values, "config_sha256"),
            DecodeSetupSelection(payload), DecodeMcpSetup(payload), DecodeToolDiscoveryEnabled(payload));
    }

    public static HermesNativeApprovalResponseResult DecodeApprovalResolvedResult(
        JsonElement payload)
    {
        var result = DecodeApprovalResolutionResult(payload);
        if (result.Resolved != 1)
        {
            throw new InvalidDataException(
                "Hermes did not resolve exactly one native approval.");
        }
        return result;
    }

    public static HermesNativeApprovalResponseResult DecodeApprovalResolutionResult(
        JsonElement payload)
    {
        var values = ExactObject(payload, "resolved");
        var resolved = RequiredInteger(values, "resolved");
        if (resolved is not (0 or 1))
        {
            throw new InvalidDataException(
                "Hermes returned an invalid native approval resolution count.");
        }
        return new HermesNativeApprovalResponseResult(resolved);
    }

    public static HermesNativeApprovalPrepareResult
        DecodeApprovalAlwaysPrepareResult(JsonElement payload)
    {
        var values = ExactObject(payload, "prepared", "persistence");
        if (RequiredInteger(values, "prepared") != 1)
        {
            throw new InvalidDataException(
                "Hermes did not prepare exactly one permanent native approval.");
        }
        var receipt = ExactObject(
            values["persistence"],
            "schema_version",
            "source",
            "previous_config_sha256",
            "config_sha256",
            "commit_nonce",
            "approval_mode",
            "enabled_toolsets",
            "new_session_required");
        if (RequiredInteger(receipt, "schema_version") != 1)
        {
            throw new InvalidDataException(
                "Hermes approval persistence schema was rejected.");
        }
        RequireLiteral(receipt, "source", ApprovalPersistenceSource);
        if (!RequiredBoolean(receipt, "new_session_required"))
        {
            throw new InvalidDataException(
                "Hermes permanent approval did not require a new session.");
        }
        return new HermesNativeApprovalPrepareResult(
            new HermesNativeApprovalPersistenceReceipt(
                RequiredString(receipt, "previous_config_sha256"),
                RequiredString(receipt, "config_sha256"),
                RequiredString(receipt, "commit_nonce"),
                ParseApprovalMode(RequiredString(receipt, "approval_mode")),
                StringArray(
                    receipt["enabled_toolsets"],
                    MaximumToolsets,
                    MaximumToolsetNameUtf8Bytes,
                    "enabled_toolsets")));
    }

    public static string ApprovalModeToken(HermesNativeApprovalMode mode)
        => mode switch
        {
            HermesNativeApprovalMode.Manual => "manual",
            HermesNativeApprovalMode.Smart => "smart",
            HermesNativeApprovalMode.Off => "off",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    public static string ReadinessToken(HermesNativeToolsetReadiness readiness)
        => readiness switch
        {
            HermesNativeToolsetReadiness.Ready => "ready",
            HermesNativeToolsetReadiness.NeedsSetup => "needs_setup",
            HermesNativeToolsetReadiness.NeedsAuth => "needs_auth",
            HermesNativeToolsetReadiness.NeedsKeys => "needs_keys",
            _ => throw new ArgumentOutOfRangeException(nameof(readiness))
        };

    internal static string Text(
        string? value,
        int maximumUtf8Bytes,
        string parameterName,
        bool allowEmpty = false)
    {
        if (value is null
            || !allowEmpty && string.IsNullOrWhiteSpace(value)
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("Hermes native tool text was rejected.", parameterName);
        }
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Hermes native tool text was not valid Unicode.",
                parameterName,
                exception);
        }
        if (byteCount > maximumUtf8Bytes)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static string Token(
        string? value,
        int maximumUtf8Bytes,
        string parameterName)
    {
        value = Text(value, maximumUtf8Bytes, parameterName);
        if (value.Any(character =>
                !((character >= 'a' && character <= 'z')
                  || (character >= 'A' && character <= 'Z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-' or '/' or ':')))
        {
            throw new ArgumentException("Hermes native tool token was rejected.", parameterName);
        }
        return value;
    }

    internal static IReadOnlyList<string> UniqueTokens(
        IEnumerable<string> values,
        int maximumCount,
        int maximumUtf8Bytes,
        string parameterName)
    {
        if (values is null)
            throw new ArgumentNullException(parameterName);
        var items = values.ToArray();
        if (items.Length > maximumCount)
            throw new ArgumentOutOfRangeException(parameterName);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = Token(
                items[index],
                maximumUtf8Bytes,
                $"{parameterName}[{index}]");
            if (!unique.Add(items[index]))
            {
                throw new ArgumentException(
                    "Hermes native tool tokens must be unique.",
                    parameterName);
            }
        }
        return Array.AsReadOnly(items);
    }

    internal static void RequireCanonicalToolsetOrder(
        IReadOnlyList<string> values,
        string parameterName)
    {
        if (!values.SequenceEqual(
                values.OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Hermes native toolsets must use canonical ordinal order.",
                parameterName);
        }
    }

    internal static string Sha256(string? value, string parameterName)
    {
        value = Text(value, 64, parameterName);
        if (value.Length != 64
            || value.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Hermes native config digest was rejected.",
                parameterName);
        }
        return value;
    }

    private static HermesNativeApprovalMode ParseApprovalMode(string value)
        => value switch
        {
            "manual" => HermesNativeApprovalMode.Manual,
            "smart" => HermesNativeApprovalMode.Smart,
            "off" => HermesNativeApprovalMode.Off,
            _ => throw new InvalidDataException(
                "Hermes native approval mode was rejected.")
        };

    private static HermesNativeToolsetReadiness ParseReadiness(string value)
        => value switch
        {
            "ready" => HermesNativeToolsetReadiness.Ready,
            "needs_setup" => HermesNativeToolsetReadiness.NeedsSetup,
            "needs_auth" => HermesNativeToolsetReadiness.NeedsAuth,
            "needs_keys" => HermesNativeToolsetReadiness.NeedsKeys,
            _ => throw new InvalidDataException(
                "Hermes native toolset readiness was rejected.")
        };

    private static IReadOnlyDictionary<string, JsonElement> ExactObject(
        JsonElement payload,
        params string[] expectedProperties)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes native tools payload was rejected.");
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!expected.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException(
                    "Hermes native tools payload properties were rejected.");
            }
        }
        if (values.Count != expected.Count)
            throw new InvalidDataException("Hermes native tools payload was incomplete.");
        return new ReadOnlyDictionary<string, JsonElement>(values);
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        bool allowEmpty = false)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes native tools text was rejected.");
        return Text(
            value.GetString(),
            name == "description"
                ? MaximumToolsetDescriptionUtf8Bytes
                : name == "label"
                    ? MaximumToolsetLabelUtf8Bytes
                    : name == "config_sha256"
                        ? 64
                        : MaximumToolsetNameUtf8Bytes,
            name,
            allowEmpty);
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Hermes native tools boolean was rejected.");
        return value.GetBoolean();
    }

    private static int RequiredInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException(
                "Hermes native tools integer was rejected.");
        }
        return result;
    }

    private static IReadOnlyList<string> StringArray(
        JsonElement value,
        int maximumCount,
        int maximumUtf8Bytes,
        string name)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximumCount)
        {
            throw new InvalidDataException("Hermes native tools array was rejected.");
        }
        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Hermes native tool token was rejected.");
            items.Add(item.GetString()!);
        }
        try
        {
            return UniqueTokens(items, maximumCount, maximumUtf8Bytes, name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Hermes native tools array was rejected.",
                exception);
        }
    }

    private static void RequireLiteral(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        string expected)
    {
        if (!string.Equals(
                RequiredString(values, name),
                expected,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Hermes native tools literal was rejected.");
        }
    }
}
