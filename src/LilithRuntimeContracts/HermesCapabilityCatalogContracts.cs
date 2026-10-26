using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

public enum HermesCapabilityPluginStatus
{
    Enabled = 1,
    Disabled = 2,
    NotEnabled = 3,
    Unknown = 4
}

public enum HermesCapabilityMcpSnapshotState
{
    Unavailable = 1,
    Attested = 2
}

public enum HermesCapabilityMcpServerStatus
{
    Connected = 1,
    Disabled = 2,
    Connecting = 3,
    Failed = 4,
    Configured = 5
}

public enum HermesCapabilitySectionState
{
    Available = 1,
    Unavailable = 2
}

public enum HermesCapabilitySectionReason
{
    None = 1,
    RequestFailed = 2,
    InvalidPayload = 3,
    Unsupported = 4,
    NoAttestedSession = 5
}

public enum HermesCapabilityCoverageState
{
    Attested = 1,
    Partial = 2,
    Unknown = 3,
    NotApplicable = 4
}

public enum HermesCapabilitySkillExclusionReason
{
    None = 1,
    PlatformIncompatible = 2,
    EnvironmentIncompatible = 3,
    Disabled = 4,
    Other = 5
}

public sealed class HermesCapabilityCoverage
{
    [JsonIgnore]
    public HermesCapabilityCoverageState State { get; }

    [JsonPropertyName("state")]
    public string StateToken => HermesCapabilityCatalogContract.CoverageStateToken(State);

    [JsonPropertyName("count")]
    public int? Count { get; }

    public HermesCapabilityCoverage(
        HermesCapabilityCoverageState state,
        int? count)
    {
        if (!Enum.IsDefined(typeof(HermesCapabilityCoverageState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (state is HermesCapabilityCoverageState.Attested
                or HermesCapabilityCoverageState.Partial)
        {
            if (!count.HasValue
                || count.Value < 0
                || count.Value > HermesCapabilityCatalogContract.MaximumCoverageCount)
                throw new ArgumentOutOfRangeException(nameof(count));
        }
        else if (count.HasValue)
        {
            throw new ArgumentException(
                "Unknown or inapplicable coverage cannot assert a count.",
                nameof(count));
        }
        State = state;
        Count = count;
    }
}

public sealed class HermesCapabilitySectionCoverage
{
    [JsonPropertyName("installed")]
    public HermesCapabilityCoverage Installed { get; }

    [JsonPropertyName("offered")]
    public HermesCapabilityCoverage Offered { get; }

    [JsonPropertyName("configured")]
    public HermesCapabilityCoverage Configured { get; }

    [JsonPropertyName("operable")]
    public HermesCapabilityCoverage Operable { get; }

    public HermesCapabilitySectionCoverage(
        HermesCapabilityCoverage installed,
        HermesCapabilityCoverage offered,
        HermesCapabilityCoverage configured,
        HermesCapabilityCoverage operable)
    {
        Installed = installed ?? throw new ArgumentNullException(nameof(installed));
        Offered = offered ?? throw new ArgumentNullException(nameof(offered));
        Configured = configured ?? throw new ArgumentNullException(nameof(configured));
        Operable = operable ?? throw new ArgumentNullException(nameof(operable));
    }
}

public sealed class HermesCapabilitySectionStatus
{
    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonIgnore]
    public HermesCapabilitySectionState State { get; }

    [JsonPropertyName("state")]
    public string StateToken => HermesCapabilityCatalogContract.SectionStateToken(State);

    [JsonIgnore]
    public HermesCapabilitySectionReason Reason { get; }

    [JsonPropertyName("reason")]
    public string ReasonToken => HermesCapabilityCatalogContract.SectionReasonToken(Reason);

    [JsonPropertyName("coverage")]
    public HermesCapabilitySectionCoverage Coverage { get; }

    public HermesCapabilitySectionStatus(
        string id,
        HermesCapabilitySectionState state,
        HermesCapabilitySectionReason reason,
        HermesCapabilitySectionCoverage coverage)
    {
        Id = HermesCapabilityCatalogContract.SectionId(id, nameof(id));
        if (!Enum.IsDefined(typeof(HermesCapabilitySectionState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (!Enum.IsDefined(typeof(HermesCapabilitySectionReason), reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        if ((state == HermesCapabilitySectionState.Available)
            != (reason == HermesCapabilitySectionReason.None))
        {
            throw new ArgumentException(
                "Hermes capability section state and reason were inconsistent.",
                nameof(reason));
        }
        Coverage = coverage ?? throw new ArgumentNullException(nameof(coverage));
        if (state == HermesCapabilitySectionState.Unavailable
            && new[]
            {
                Coverage.Installed,
                Coverage.Offered,
                Coverage.Configured,
                Coverage.Operable
            }.Any(value => value.State is HermesCapabilityCoverageState.Attested
                or HermesCapabilityCoverageState.Partial))
        {
            throw new ArgumentException(
                "An unavailable Hermes section cannot assert coverage.",
                nameof(coverage));
        }
        State = state;
        Reason = reason;
    }
}

public sealed class HermesCapabilityCatalogIdentity
{
    [JsonPropertyName("runtime_version")]
    public string RuntimeVersion { get; }

    [JsonPropertyName("hermes_version")]
    public string HermesVersion { get; }

    [JsonPropertyName("hermes_commit")]
    public string HermesCommit { get; }

    public HermesCapabilityCatalogIdentity(
        string runtimeVersion,
        string hermesVersion,
        string hermesCommit)
    {
        RuntimeVersion = HermesCapabilityCatalogContract.Text(
            runtimeVersion,
            HermesCapabilityCatalogContract.MaximumVersionUtf8Bytes,
            nameof(runtimeVersion));
        HermesVersion = HermesCapabilityCatalogContract.Text(
            hermesVersion,
            HermesCapabilityCatalogContract.MaximumVersionUtf8Bytes,
            nameof(hermesVersion));
        HermesCommit = HermesCapabilityCatalogContract.LowerHex(
            hermesCommit,
            40,
            nameof(hermesCommit));
    }
}

public sealed class HermesCapabilityCommandCategory
{
    private readonly IReadOnlyList<string> _commands;

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("commands")]
    public IReadOnlyList<string> Commands => _commands;

    public HermesCapabilityCommandCategory(
        string name,
        IEnumerable<string> commands)
    {
        Name = HermesCapabilityCatalogContract.Text(
            name,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(name));
        _commands = HermesCapabilityCatalogContract.UniqueTokens(
            commands,
            HermesCapabilityCatalogContract.MaximumCommands,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(commands),
            requireSlashPrefix: true);
    }
}

public sealed class HermesCapabilityCommandAlias
{
    [JsonPropertyName("alias")]
    public string Alias { get; }

    [JsonPropertyName("canonical")]
    public string Canonical { get; }

    public HermesCapabilityCommandAlias(string alias, string canonical)
    {
        Alias = HermesCapabilityCatalogContract.Token(
            alias,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(alias),
            requireSlashPrefix: true);
        Canonical = HermesCapabilityCatalogContract.Token(
            canonical,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(canonical),
            requireSlashPrefix: true);
    }
}

public sealed class HermesCapabilityCommandDetail
{
    private readonly IReadOnlyList<string> _subcommands;

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("description")]
    public string Description { get; }

    [JsonPropertyName("subcommands")]
    public IReadOnlyList<string> Subcommands => _subcommands;

    public HermesCapabilityCommandDetail(
        string name,
        string description,
        IEnumerable<string> subcommands)
    {
        Name = HermesCapabilityCatalogContract.Token(
            name,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(name),
            requireSlashPrefix: true);
        Description = HermesCapabilityCatalogContract.Text(
            description,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(description),
            allowEmpty: true);
        _subcommands = HermesCapabilityCatalogContract.UniqueTokens(
            subcommands,
            HermesCapabilityCatalogContract.MaximumSubcommandsPerCommand,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(subcommands));
    }
}

public sealed class HermesCapabilityQuickSkillDetail
{
    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("usage")]
    public int Usage { get; }

    [JsonPropertyName("origin")]
    public string Origin { get; }

    public HermesCapabilityQuickSkillDetail(
        string name,
        int usage,
        string origin)
    {
        Name = HermesCapabilityCatalogContract.Token(
            name,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(name),
            requireSlashPrefix: true);
        if (usage < 0
            || usage > HermesCapabilityCatalogContract.MaximumCoverageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }
        Origin = HermesCapabilityCatalogContract.QuickSkillOrigin(
            origin,
            nameof(origin));
        Usage = usage;
    }
}

public sealed class HermesCapabilityCommandCatalog
{
    private readonly IReadOnlyList<string> _commands;
    private readonly IReadOnlyList<string> _quickSkills;
    private readonly IReadOnlyList<HermesCapabilityCommandDetail> _details;
    private readonly IReadOnlyList<HermesCapabilityQuickSkillDetail>
        _quickSkillDetails;
    private readonly IReadOnlyList<HermesCapabilityCommandCategory> _categories;
    private readonly IReadOnlyList<HermesCapabilityCommandAlias> _aliases;

    [JsonPropertyName("commands")]
    public IReadOnlyList<string> Commands => _commands;

    [JsonPropertyName("quick_skills")]
    public IReadOnlyList<string> QuickSkills => _quickSkills;

    [JsonPropertyName("details")]
    public IReadOnlyList<HermesCapabilityCommandDetail> Details => _details;

    [JsonPropertyName("quick_skill_details")]
    public IReadOnlyList<HermesCapabilityQuickSkillDetail> QuickSkillDetails =>
        _quickSkillDetails;

    [JsonPropertyName("categories")]
    public IReadOnlyList<HermesCapabilityCommandCategory> Categories => _categories;

    [JsonPropertyName("aliases")]
    public IReadOnlyList<HermesCapabilityCommandAlias> Aliases => _aliases;

    [JsonPropertyName("quick_skill_count")]
    public int QuickSkillCount { get; }

    [JsonPropertyName("non_skill_count")]
    public int NonSkillCommandCount => _commands.Count - QuickSkillCount;

    public HermesCapabilityCommandCatalog(
        IEnumerable<string> commands,
        IEnumerable<string> quickSkills,
        IEnumerable<HermesCapabilityCommandDetail> details,
        IEnumerable<HermesCapabilityQuickSkillDetail> quickSkillDetails,
        IEnumerable<HermesCapabilityCommandCategory> categories,
        IEnumerable<HermesCapabilityCommandAlias> aliases,
        int quickSkillCount)
    {
        _commands = HermesCapabilityCatalogContract.UniqueTokens(
            commands,
            HermesCapabilityCatalogContract.MaximumCommands,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(commands),
            requireSlashPrefix: true);
        _quickSkills = HermesCapabilityCatalogContract.UniqueTokens(
            quickSkills,
            HermesCapabilityCatalogContract.MaximumSkills,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(quickSkills),
            requireSlashPrefix: true);
        _details = HermesCapabilityCatalogContract.UniqueRows(
            details,
            HermesCapabilityCatalogContract.MaximumCommands,
            static row => row.Name,
            nameof(details));
        _quickSkillDetails = HermesCapabilityCatalogContract.UniqueRows(
            quickSkillDetails,
            HermesCapabilityCatalogContract.MaximumSkills,
            static row => row.Name,
            nameof(quickSkillDetails));
        _categories = HermesCapabilityCatalogContract.UniqueRows(
            categories,
            HermesCapabilityCatalogContract.MaximumCommandCategories,
            static row => row.Name,
            nameof(categories));
        _aliases = HermesCapabilityCatalogContract.UniqueRows(
            aliases,
            HermesCapabilityCatalogContract.MaximumCommands * 4,
            static row => row.Alias,
            nameof(aliases));
        var commandSet = _commands.ToHashSet(StringComparer.Ordinal);
        if (_categories.Any(category =>
                category.Commands.Any(command => !commandSet.Contains(command)))
            || _aliases.Any(alias => !commandSet.Contains(alias.Canonical)
                || string.Equals(
                    alias.Alias,
                    alias.Canonical,
                    StringComparison.OrdinalIgnoreCase))
            || _quickSkills.Any(command => !commandSet.Contains(command))
            || _details.Count != _commands.Count
            || _details.Any(detail => !commandSet.Contains(detail.Name))
            || _quickSkillDetails.Count != _quickSkills.Count
            || _quickSkillDetails.Any(detail =>
                !_quickSkills.Contains(detail.Name, StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "Hermes command catalog references an unknown command.",
                nameof(commands));
        }
        if (quickSkillCount < 0
            || quickSkillCount > HermesCapabilityCatalogContract.MaximumSkills
            || quickSkillCount > _commands.Count
            || quickSkillCount != _quickSkills.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(quickSkillCount));
        }
        QuickSkillCount = quickSkillCount;
    }
}

public sealed class HermesCapabilityInstalledToolset
{
    private readonly IReadOnlyList<string> _tools;

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("description")]
    public string Description { get; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools => _tools;

    public HermesCapabilityInstalledToolset(
        string name,
        string description,
        IEnumerable<string> tools)
    {
        Name = HermesCapabilityCatalogContract.Token(
            name,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(name));
        Description = HermesCapabilityCatalogContract.Text(
            description,
            HermesNativeToolsContract.MaximumToolsetDescriptionUtf8Bytes,
            nameof(description),
            allowEmpty: true);
        _tools = HermesCapabilityCatalogContract.UniqueTokens(
            tools,
            HermesNativeToolsContract.MaximumToolsPerToolset,
            HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
            nameof(tools));
    }
}

public sealed class HermesCapabilityToolset
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
    public string ReadinessToken => HermesNativeToolsContract.ReadinessToken(Readiness);

    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools => _tools;

    public HermesCapabilityToolset(
        string name,
        string label,
        string description,
        bool enabled,
        bool configured,
        HermesNativeToolsetReadiness readiness,
        IEnumerable<string> tools)
    {
        Name = HermesCapabilityCatalogContract.Token(
            name,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(name));
        Label = HermesCapabilityCatalogContract.Text(
            label,
            HermesNativeToolsContract.MaximumToolsetLabelUtf8Bytes,
            nameof(label));
        Description = HermesCapabilityCatalogContract.Text(
            description,
            HermesNativeToolsContract.MaximumToolsetDescriptionUtf8Bytes,
            nameof(description),
            allowEmpty: true);
        if (!Enum.IsDefined(typeof(HermesNativeToolsetReadiness), readiness))
            throw new ArgumentOutOfRangeException(nameof(readiness));
        if (configured != (readiness == HermesNativeToolsetReadiness.Ready))
        {
            throw new ArgumentException(
                "Hermes toolset configured state must match readiness.",
                nameof(configured));
        }
        Enabled = enabled;
        Configured = configured;
        Readiness = readiness;
        _tools = HermesCapabilityCatalogContract.UniqueTokens(
            tools,
            HermesNativeToolsContract.MaximumToolsPerToolset,
            HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
            nameof(tools));
    }
}

public sealed class HermesCapabilityNativeToolsCatalog
{
    private readonly IReadOnlyList<string> _enabledToolsets;
    private readonly IReadOnlyList<HermesCapabilityInstalledToolset>
        _installedToolsets;
    private readonly IReadOnlyList<HermesCapabilityToolset> _toolsets;
    private readonly IReadOnlyList<string> _loadedTools;

    [JsonPropertyName("platform")]
    public string Platform => "cli";

    [JsonIgnore]
    public HermesNativeApprovalMode ApprovalMode { get; }

    [JsonPropertyName("approval_mode")]
    public string ApprovalModeToken =>
        HermesNativeToolsContract.ApprovalModeToken(ApprovalMode);

    [JsonPropertyName("enabled_toolsets")]
    public IReadOnlyList<string> EnabledToolsets => _enabledToolsets;

    [JsonPropertyName("installed_toolsets")]
    public IReadOnlyList<HermesCapabilityInstalledToolset> InstalledToolsets =>
        _installedToolsets;

    [JsonPropertyName("toolsets")]
    public IReadOnlyList<HermesCapabilityToolset> Toolsets => _toolsets;

    [JsonPropertyName("new_session_required")]
    public bool NewSessionRequired { get; }

    [JsonPropertyName("loaded_snapshot_attested")]
    public bool LoadedSnapshotAttested { get; }

    [JsonPropertyName("loaded_tools")]
    public IReadOnlyList<string> LoadedTools => _loadedTools;

    public HermesCapabilityNativeToolsCatalog(
        HermesNativeApprovalMode approvalMode,
        IEnumerable<string> enabledToolsets,
        IEnumerable<HermesCapabilityInstalledToolset> installedToolsets,
        IEnumerable<HermesCapabilityToolset> toolsets,
        bool newSessionRequired,
        bool loadedSnapshotAttested,
        IEnumerable<string> loadedTools)
    {
        if (!Enum.IsDefined(typeof(HermesNativeApprovalMode), approvalMode))
            throw new ArgumentOutOfRangeException(nameof(approvalMode));
        ApprovalMode = approvalMode;
        _enabledToolsets = HermesCapabilityCatalogContract.UniqueTokens(
            enabledToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
            nameof(enabledToolsets));
        _installedToolsets = HermesCapabilityCatalogContract.UniqueRows(
            installedToolsets,
            HermesNativeToolsContract.MaximumToolsets,
            static row => row.Name,
            nameof(installedToolsets));
        _toolsets = HermesCapabilityCatalogContract.UniqueRows(
            toolsets,
            HermesNativeToolsContract.MaximumToolsets,
            static row => row.Name,
            nameof(toolsets));
        var enabled = _enabledToolsets.ToHashSet(StringComparer.Ordinal);
        if (_toolsets.Any(row => row.Enabled != enabled.Contains(row.Name))
            || enabled.Any(name => _toolsets.All(row => row.Name != name)))
        {
            throw new ArgumentException(
                "Hermes enabled toolsets must exactly match catalog rows.",
                nameof(enabledToolsets));
        }
        _loadedTools = HermesCapabilityCatalogContract.UniqueTokens(
            loadedTools,
            HermesCapabilityCatalogContract.MaximumMcpTools,
            HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
            nameof(loadedTools));
        var offeredTools = _toolsets
            .SelectMany(row => row.Tools)
            .ToHashSet(StringComparer.Ordinal);
        if (!loadedSnapshotAttested && _loadedTools.Count != 0
            || _loadedTools.Any(tool => !offeredTools.Contains(tool)))
        {
            throw new ArgumentException(
                "Hermes loaded-tool snapshot was inconsistent.",
                nameof(loadedTools));
        }
        NewSessionRequired = newSessionRequired;
        LoadedSnapshotAttested = loadedSnapshotAttested;
    }
}

public sealed class HermesCapabilityModel
{
    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("available")]
    public bool Available { get; }

    [JsonPropertyName("featured")]
    public bool Featured { get; }

    [JsonPropertyName("capability_known")]
    public bool CapabilityKnown { get; }

    [JsonPropertyName("fast")]
    public bool Fast { get; }

    [JsonPropertyName("reasoning")]
    public bool Reasoning { get; }

    public HermesCapabilityModel(
        string id,
        bool available,
        bool featured,
        bool capabilityKnown,
        bool fast,
        bool reasoning)
    {
        Id = HermesCapabilityCatalogContract.ModelToken(id, nameof(id));
        Available = available;
        Featured = featured;
        if (!capabilityKnown && (fast || reasoning))
        {
            throw new ArgumentException(
                "Unknown Hermes model capabilities cannot assert support.",
                nameof(capabilityKnown));
        }
        CapabilityKnown = capabilityKnown;
        Fast = fast;
        Reasoning = reasoning;
    }
}

public sealed class HermesCapabilityModelProvider
{
    private readonly IReadOnlyList<HermesCapabilityModel> _models;

    [JsonPropertyName("id")]
    public string Id { get; }

    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("current")]
    public bool Current { get; }

    [JsonPropertyName("user_defined")]
    public bool UserDefined { get; }

    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; }

    [JsonPropertyName("models")]
    public IReadOnlyList<HermesCapabilityModel> Models => _models;

    public HermesCapabilityModelProvider(
        string id,
        string name,
        bool current,
        bool userDefined,
        bool authenticated,
        IEnumerable<HermesCapabilityModel> models)
    {
        Id = HermesCapabilityCatalogContract.ProviderToken(id, nameof(id));
        Name = HermesCapabilityCatalogContract.Text(
            name,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(name));
        Current = current;
        UserDefined = userDefined;
        Authenticated = authenticated;
        _models = HermesCapabilityCatalogContract.UniqueRows(
            models,
            HermesCapabilityCatalogContract.MaximumModelsPerProvider,
            static row => row.Id,
            nameof(models));
    }
}

public sealed class HermesCapabilityModelCatalog
{
    private readonly IReadOnlyList<HermesCapabilityModelProvider> _providers;

    [JsonPropertyName("default_provider")]
    public string DefaultProvider { get; }

    [JsonPropertyName("default_model")]
    public string DefaultModel { get; }

    [JsonPropertyName("providers")]
    public IReadOnlyList<HermesCapabilityModelProvider> Providers => _providers;

    public HermesCapabilityModelCatalog(
        string defaultProvider,
        string defaultModel,
        IEnumerable<HermesCapabilityModelProvider> providers)
    {
        DefaultProvider = string.IsNullOrEmpty(defaultProvider)
            ? string.Empty
            : HermesCapabilityCatalogContract.ProviderToken(
                defaultProvider,
                nameof(defaultProvider));
        DefaultModel = string.IsNullOrEmpty(defaultModel)
            ? string.Empty
            : HermesCapabilityCatalogContract.ModelToken(
                defaultModel,
                nameof(defaultModel));
        if ((DefaultProvider.Length == 0) != (DefaultModel.Length == 0))
        {
            throw new ArgumentException(
                "Hermes default model identity must be both present or both absent.");
        }
        _providers = HermesCapabilityCatalogContract.UniqueRows(
            providers,
            HermesCapabilityCatalogContract.MaximumModelProviders,
            static row => row.Id,
            nameof(providers));
        if (_providers.Count(provider => provider.Current) > 1)
        {
            throw new ArgumentException(
                "Hermes model catalog cannot identify multiple current providers.",
                nameof(providers));
        }
        if (DefaultProvider.Length != 0
            && !_providers.Any(provider =>
                string.Equals(provider.Id, DefaultProvider, StringComparison.Ordinal)
                && provider.Models.Any(model => string.Equals(
                    model.Id,
                    DefaultModel,
                    StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Hermes default model is outside the display catalog.",
                nameof(defaultModel));
        }
    }
}

public sealed class HermesCapabilitySkill
{
    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("category")]
    public string Category { get; }

    [JsonPropertyName("offered")]
    public bool Offered { get; }

    [JsonIgnore]
    public HermesCapabilitySkillExclusionReason ExclusionReason { get; }

    [JsonPropertyName("exclusion_reason")]
    public string ExclusionReasonToken =>
        HermesCapabilityCatalogContract.SkillExclusionReasonToken(ExclusionReason);

    public HermesCapabilitySkill(
        string name,
        string category,
        bool offered,
        HermesCapabilitySkillExclusionReason exclusionReason)
    {
        Name = HermesCapabilityCatalogContract.Text(
            name,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(name));
        Category = HermesCapabilityCatalogContract.Text(
            category,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(category));
        if (!Enum.IsDefined(
                typeof(HermesCapabilitySkillExclusionReason),
                exclusionReason))
        {
            throw new ArgumentOutOfRangeException(nameof(exclusionReason));
        }
        if (offered
            != (exclusionReason == HermesCapabilitySkillExclusionReason.None))
        {
            throw new ArgumentException(
                "Hermes Skill offer state and exclusion reason were inconsistent.",
                nameof(exclusionReason));
        }
        Offered = offered;
        ExclusionReason = exclusionReason;
    }
}

public sealed class HermesCapabilitySkillCatalog
{
    private readonly IReadOnlyList<HermesCapabilitySkill> _installed;

    [JsonPropertyName("installed")]
    public IReadOnlyList<HermesCapabilitySkill> Installed => _installed;

    [JsonPropertyName("offered_count")]
    public int OfferedCount { get; }

    [JsonPropertyName("excluded_count")]
    public int ExcludedCount { get; }

    public HermesCapabilitySkillCatalog(
        IEnumerable<HermesCapabilitySkill> installed,
        int offeredCount,
        int excludedCount)
    {
        _installed = HermesCapabilityCatalogContract.UniqueRows(
            installed,
            HermesCapabilityCatalogContract.MaximumSkills,
            static row => row.Name,
            nameof(installed));
        if (offeredCount < 0
            || excludedCount < 0
            || offeredCount + excludedCount != _installed.Count
            || offeredCount != _installed.Count(skill => skill.Offered)
            || excludedCount != _installed.Count(skill => !skill.Offered))
        {
            throw new ArgumentException("Hermes Skill counts were inconsistent.");
        }
        OfferedCount = offeredCount;
        ExcludedCount = excludedCount;
    }
}

public sealed class HermesCapabilityPlugin
{
    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("key")]
    public string Key { get; }

    [JsonPropertyName("version")]
    public string Version { get; }

    [JsonPropertyName("description")]
    public string Description { get; }

    [JsonPropertyName("source")]
    public string Source { get; }

    [JsonIgnore]
    public HermesCapabilityPluginStatus Status { get; }

    [JsonPropertyName("status")]
    public string StatusToken => HermesCapabilityCatalogContract.PluginStatusToken(Status);

    [JsonPropertyName("portable")]
    public bool Portable { get; }

    public HermesCapabilityPlugin(
        string name,
        string key,
        string version,
        string description,
        string source,
        HermesCapabilityPluginStatus status,
        bool portable)
    {
        Name = HermesCapabilityCatalogContract.Text(
            name,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(name));
        Key = HermesCapabilityCatalogContract.Token(
            key,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(key));
        Version = HermesCapabilityCatalogContract.Text(
            version,
            HermesCapabilityCatalogContract.MaximumVersionUtf8Bytes,
            nameof(version),
            allowEmpty: true);
        Description = HermesCapabilityCatalogContract.Text(
            description,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(description),
            allowEmpty: true);
        var normalizedSource = HermesCapabilityCatalogContract.Token(
            source,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(source));
        if (normalizedSource is not ("bundled" or "user" or "git" or "entrypoint" or "other"))
            throw new ArgumentOutOfRangeException(nameof(source));
        Source = normalizedSource;
        if (!Enum.IsDefined(typeof(HermesCapabilityPluginStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
        Portable = portable;
    }
}

public sealed class HermesCapabilityPluginCatalog
{
    private readonly IReadOnlyList<HermesCapabilityPlugin> _plugins;

    [JsonPropertyName("plugins")]
    public IReadOnlyList<HermesCapabilityPlugin> Plugins => _plugins;

    [JsonPropertyName("user_count")]
    public int UserCount { get; }

    [JsonPropertyName("bundled_count")]
    public int BundledCount { get; }

    public HermesCapabilityPluginCatalog(
        IEnumerable<HermesCapabilityPlugin> plugins,
        int userCount,
        int bundledCount)
    {
        _plugins = HermesCapabilityCatalogContract.UniqueRows(
            plugins,
            HermesCapabilityCatalogContract.MaximumPlugins,
            static row => row.Key,
            nameof(plugins));
        if (userCount < 0
            || bundledCount < 0
            || userCount + bundledCount != _plugins.Count
            || bundledCount != _plugins.Count(plugin => string.Equals(
                plugin.Source,
                "bundled",
                StringComparison.Ordinal))
            || userCount != _plugins.Count(plugin => !string.Equals(
                plugin.Source,
                "bundled",
                StringComparison.Ordinal)))
        {
            throw new ArgumentException("Hermes plugin counts were inconsistent.");
        }
        UserCount = userCount;
        BundledCount = bundledCount;
    }
}

public sealed class HermesCapabilityMcpServer
{
    [JsonPropertyName("name")]
    public string Name { get; }

    [JsonPropertyName("transport")]
    public string Transport { get; }

    [JsonPropertyName("tool_count")]
    public int ToolCount { get; }

    [JsonPropertyName("connected")]
    public bool Connected { get; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; }

    [JsonIgnore]
    public HermesCapabilityMcpServerStatus Status { get; }

    [JsonPropertyName("status")]
    public string StatusToken => HermesCapabilityCatalogContract.McpStatusToken(Status);

    public HermesCapabilityMcpServer(
        string name,
        string transport,
        int toolCount,
        bool connected,
        bool disabled,
        HermesCapabilityMcpServerStatus status)
    {
        Name = HermesCapabilityCatalogContract.Text(
            name,
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
            nameof(name));
        Transport = HermesCapabilityCatalogContract.Token(
            transport,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(transport));
        if (toolCount < 0
            || toolCount > HermesCapabilityCatalogContract.MaximumMcpTools)
        {
            throw new ArgumentOutOfRangeException(nameof(toolCount));
        }
        if (!Enum.IsDefined(typeof(HermesCapabilityMcpServerStatus), status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (connected != (status == HermesCapabilityMcpServerStatus.Connected)
            || disabled != (status == HermesCapabilityMcpServerStatus.Disabled))
        {
            throw new ArgumentException(
                "Hermes MCP flags did not match the attested status.");
        }
        ToolCount = toolCount;
        Connected = connected;
        Disabled = disabled;
        Status = status;
    }
}

public sealed class HermesCapabilityMcpCatalog
{
    private readonly IReadOnlyList<HermesCapabilityMcpServer> _servers;

    [JsonIgnore]
    public HermesCapabilityMcpSnapshotState State { get; }

    [JsonPropertyName("state")]
    public string StateToken => HermesCapabilityCatalogContract.McpStateToken(State);

    [JsonPropertyName("servers")]
    public IReadOnlyList<HermesCapabilityMcpServer> Servers => _servers;

    public HermesCapabilityMcpCatalog(
        HermesCapabilityMcpSnapshotState state,
        IEnumerable<HermesCapabilityMcpServer> servers)
    {
        if (!Enum.IsDefined(typeof(HermesCapabilityMcpSnapshotState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        State = state;
        _servers = HermesCapabilityCatalogContract.UniqueRows(
            servers,
            HermesCapabilityCatalogContract.MaximumMcpServers,
            static row => row.Name,
            nameof(servers));
        if (state == HermesCapabilityMcpSnapshotState.Unavailable
            && _servers.Count != 0)
        {
            throw new ArgumentException(
                "An unavailable MCP snapshot cannot contain servers.",
                nameof(servers));
        }
    }
}

public sealed class HermesCapabilityCatalog
{
    private readonly IReadOnlyList<string> _runtimeMethods;
    private readonly IReadOnlyList<HermesCapabilitySectionStatus> _sections;
    private readonly IReadOnlyList<string> _warnings;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion => HermesCapabilityCatalogContract.SchemaVersion;

    [JsonPropertyName("read_only")]
    public bool ReadOnly => true;

    [JsonPropertyName("identity")]
    public HermesCapabilityCatalogIdentity Identity { get; }

    [JsonPropertyName("runtime_methods")]
    public IReadOnlyList<string> RuntimeMethods => _runtimeMethods;

    [JsonPropertyName("sections")]
    public IReadOnlyList<HermesCapabilitySectionStatus> Sections => _sections;

    [JsonPropertyName("commands")]
    public HermesCapabilityCommandCatalog Commands { get; }

    [JsonPropertyName("native_tools")]
    public HermesCapabilityNativeToolsCatalog NativeTools { get; }

    [JsonPropertyName("models")]
    public HermesCapabilityModelCatalog Models { get; }

    [JsonPropertyName("skills")]
    public HermesCapabilitySkillCatalog Skills { get; }

    [JsonPropertyName("plugins")]
    public HermesCapabilityPluginCatalog Plugins { get; }

    [JsonPropertyName("mcp")]
    public HermesCapabilityMcpCatalog Mcp { get; }

    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings => _warnings;

    public HermesCapabilityCatalog(
        HermesCapabilityCatalogIdentity identity,
        IEnumerable<string> runtimeMethods,
        IEnumerable<HermesCapabilitySectionStatus> sections,
        HermesCapabilityCommandCatalog commands,
        HermesCapabilityNativeToolsCatalog nativeTools,
        HermesCapabilityModelCatalog models,
        HermesCapabilitySkillCatalog skills,
        HermesCapabilityPluginCatalog plugins,
        HermesCapabilityMcpCatalog mcp,
        IEnumerable<string> warnings)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _runtimeMethods = HermesCapabilityCatalogContract.UniqueTokens(
            runtimeMethods,
            HermesCapabilityCatalogContract.MaximumRuntimeMethods,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(runtimeMethods));
        if (!_runtimeMethods.Contains(
                HermesCapabilityCatalogContract.Method,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "The Runtime capability catalog must advertise its read route.",
                nameof(runtimeMethods));
        }
        _sections = HermesCapabilityCatalogContract.UniqueRows(
            sections,
            HermesCapabilityCatalogContract.SectionIds.Count,
            static row => row.Id,
            nameof(sections));
        if (_sections.Count != HermesCapabilityCatalogContract.SectionIds.Count
            || HermesCapabilityCatalogContract.SectionIds.Any(id =>
                _sections.All(section => !string.Equals(
                    section.Id,
                    id,
                    StringComparison.Ordinal))))
        {
            throw new ArgumentException(
                "Hermes capability section coverage was incomplete.",
                nameof(sections));
        }
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
        NativeTools = nativeTools ?? throw new ArgumentNullException(nameof(nativeTools));
        Models = models ?? throw new ArgumentNullException(nameof(models));
        Skills = skills ?? throw new ArgumentNullException(nameof(skills));
        Plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        Mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));

        ValidateSectionPayloads();
        _warnings = HermesCapabilityCatalogContract.UniqueTokens(
            warnings,
            HermesCapabilityCatalogContract.MaximumWarnings,
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
            nameof(warnings));
        var allowedWarnings = new[]
        {
            HermesCapabilityCatalogContract.McpSnapshotUnavailableWarning,
            HermesCapabilityCatalogContract.CommandsDiscoveryPartialWarning,
            HermesCapabilityCatalogContract
                .NativeToolsInstalledInventoryUnavailableWarning
        };
        var commandsPartial = Section(
                HermesCapabilityCatalogContract.CommandsSectionId)
            .Coverage.Offered.State == HermesCapabilityCoverageState.Partial;
        var nativeToolsInstalledUnknown = Section(
                HermesCapabilityCatalogContract.NativeToolsSectionId)
            .State == HermesCapabilitySectionState.Available
            && Section(HermesCapabilityCatalogContract.NativeToolsSectionId)
                .Coverage.Installed.State
                == HermesCapabilityCoverageState.Unknown;
        if (_warnings.Any(warning => !allowedWarnings.Contains(
                warning,
                StringComparer.Ordinal))
            || (Mcp.State == HermesCapabilityMcpSnapshotState.Unavailable)
                != _warnings.Contains(
                    HermesCapabilityCatalogContract.McpSnapshotUnavailableWarning,
                    StringComparer.Ordinal)
            || commandsPartial != _warnings.Contains(
                HermesCapabilityCatalogContract.CommandsDiscoveryPartialWarning,
                StringComparer.Ordinal)
            || nativeToolsInstalledUnknown != _warnings.Contains(
                HermesCapabilityCatalogContract
                    .NativeToolsInstalledInventoryUnavailableWarning,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Hermes capability catalog warnings were inconsistent.",
                nameof(warnings));
        }
    }

    public HermesCapabilitySectionStatus Section(string id)
    {
        var normalized = HermesCapabilityCatalogContract.SectionId(id, nameof(id));
        return _sections.Single(section => string.Equals(
            section.Id,
            normalized,
            StringComparison.Ordinal));
    }

    private void ValidateSectionPayloads()
    {
        var commands = Section(HermesCapabilityCatalogContract.CommandsSectionId);
        var nativeTools = Section(HermesCapabilityCatalogContract.NativeToolsSectionId);
        var models = Section(HermesCapabilityCatalogContract.ModelsSectionId);
        var skills = Section(HermesCapabilityCatalogContract.SkillsSectionId);
        var plugins = Section(HermesCapabilityCatalogContract.PluginsSectionId);
        var mcp = Section(HermesCapabilityCatalogContract.McpSectionId);

        ValidateUnavailable(
            commands,
            Commands.Commands.Count == 0
                && Commands.QuickSkills.Count == 0
                && Commands.Details.Count == 0
                && Commands.QuickSkillDetails.Count == 0
                && Commands.Categories.Count == 0
                && Commands.Aliases.Count == 0);
        ValidateUnavailable(
            nativeTools,
            NativeTools.Toolsets.Count == 0
                && NativeTools.InstalledToolsets.Count == 0);
        ValidateUnavailable(models, Models.Providers.Count == 0);
        ValidateUnavailable(skills, Skills.Installed.Count == 0);
        ValidateUnavailable(plugins, Plugins.Plugins.Count == 0);
        ValidateUnavailable(mcp, Mcp.State == HermesCapabilityMcpSnapshotState.Unavailable);
        if ((mcp.State == HermesCapabilitySectionState.Unavailable)
            != (Mcp.State == HermesCapabilityMcpSnapshotState.Unavailable))
        {
            throw new ArgumentException(
                "Hermes MCP section and snapshot availability were inconsistent.");
        }
        if (nativeTools.Coverage.Installed.State
                is not HermesCapabilityCoverageState.Attested
                and not HermesCapabilityCoverageState.Unknown
            || nativeTools.Coverage.Installed.State
                == HermesCapabilityCoverageState.Unknown
                && NativeTools.InstalledToolsets.Count != 0)
        {
            throw new ArgumentException(
                "Hermes installed-tool inventory coverage was inconsistent.");
        }
        if (NativeTools.LoadedSnapshotAttested
                ? nativeTools.Coverage.Operable.State
                    != HermesCapabilityCoverageState.Attested
                : nativeTools.Coverage.Operable.State
                    != HermesCapabilityCoverageState.Unknown)
        {
            throw new ArgumentException(
                "Hermes loaded-tool snapshot coverage was inconsistent.");
        }

        ValidateAttestedCount(commands.Coverage.Offered, Commands.Commands.Count);
        var installedTools = NativeTools.InstalledToolsets
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var offeredTools = NativeTools.Toolsets
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var configuredTools = NativeTools.Toolsets
            .Where(toolset => toolset.Configured)
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        ValidateAttestedCount(nativeTools.Coverage.Installed, installedTools);
        ValidateAttestedCount(nativeTools.Coverage.Offered, offeredTools);
        ValidateAttestedCount(nativeTools.Coverage.Configured, configuredTools);
        ValidateAttestedCount(
            nativeTools.Coverage.Operable,
            NativeTools.LoadedTools.Count);
        ValidateAttestedCount(
            models.Coverage.Offered,
            Models.Providers.Sum(provider => provider.Models.Count));
        ValidateAttestedCount(
            models.Coverage.Configured,
            Models.DefaultModel.Length == 0 ? 0 : 1);
        ValidateAttestedCount(skills.Coverage.Installed, Skills.Installed.Count);
        ValidateAttestedCount(skills.Coverage.Offered, Skills.OfferedCount);
        ValidateAttestedCount(plugins.Coverage.Installed, Plugins.Plugins.Count);
        ValidateAttestedCount(plugins.Coverage.Offered, Plugins.Plugins.Count);
        ValidateAttestedCount(
            plugins.Coverage.Configured,
            Plugins.Plugins.Count(plugin =>
                plugin.Status == HermesCapabilityPluginStatus.Enabled));
        ValidateAttestedCount(mcp.Coverage.Offered, Mcp.Servers.Count);
        ValidateAttestedCount(mcp.Coverage.Configured, Mcp.Servers.Count);
        ValidateAttestedCount(
            mcp.Coverage.Operable,
            Mcp.Servers.Count(server => server.Connected));
    }

    private static void ValidateUnavailable(
        HermesCapabilitySectionStatus section,
        bool payloadIsEmpty)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable
            && !payloadIsEmpty)
        {
            throw new ArgumentException(
                "An unavailable Hermes capability section contained data.");
        }
    }

    private static void ValidateAttestedCount(
        HermesCapabilityCoverage coverage,
        int expected)
    {
        if (coverage.State is HermesCapabilityCoverageState.Attested
                or HermesCapabilityCoverageState.Partial
            && coverage.Count != expected)
        {
            throw new ArgumentException(
                "Hermes capability coverage count was inconsistent.");
        }
    }
}

public static partial class HermesCapabilityCatalogContract
{
    public const string Method = "capabilities.catalog";
    public const string McpSnapshotUnavailableWarning =
        "mcp_session_info_unavailable";
    public const string CommandsDiscoveryPartialWarning =
        "commands_discovery_partial";
    public const string NativeToolsInstalledInventoryUnavailableWarning =
        "native_tools_installed_inventory_unavailable";
    public const string CommandsSectionId = "commands";
    public const string NativeToolsSectionId = "native_tools";
    public const string ModelsSectionId = "models";
    public const string SkillsSectionId = "skills";
    public const string PluginsSectionId = "plugins";
    public const string McpSectionId = "mcp";
    public const int SchemaVersion = 3;
    public const int MaximumRuntimeMethods = 256;
    public const int MaximumCommands = 4096;
    public const int MaximumCommandCategories = 256;
    public const int MaximumSubcommandsPerCommand = 128;
    public const int MaximumModelProviders = 256;
    public const int MaximumModelsPerProvider = 4096;
    public const int MaximumSkills = 4096;
    public const int MaximumPlugins = 4096;
    public const int MaximumMcpServers = 256;
    public const int MaximumMcpTools = 4096;
    public const int MaximumWarnings = 32;
    public const int MaximumCoverageCount = 1_000_000;
    public const int MaximumTokenUtf8Bytes = 1024;
    public const int MaximumDisplayTextUtf8Bytes = 4096;
    public const int MaximumVersionUtf8Bytes = 256;

    public static IReadOnlyList<string> SectionIds { get; } =
        Array.AsReadOnly(new[]
        {
            CommandsSectionId,
            NativeToolsSectionId,
            ModelsSectionId,
            SkillsSectionId,
            PluginsSectionId,
            McpSectionId
        });

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static HermesCapabilityCatalog Decode(JsonElement payload)
    {
        try
        {
            var root = ExactObject(
                payload,
                "schema_version",
                "read_only",
                "identity",
                "runtime_methods",
                "sections",
                "commands",
                "native_tools",
                "models",
                "skills",
                "plugins",
                "mcp",
                "warnings");
            if (RequiredInteger(root, "schema_version", 1, SchemaVersion)
                    != SchemaVersion
                || !RequiredBoolean(root, "read_only"))
            {
                throw new InvalidDataException(
                    "Hermes capability catalog schema was rejected.");
            }
            var identity = ExactObject(
                root["identity"],
                "runtime_version",
                "hermes_version",
                "hermes_commit");
            return new HermesCapabilityCatalog(
                new HermesCapabilityCatalogIdentity(
                    RequiredString(
                        identity,
                        "runtime_version",
                        MaximumVersionUtf8Bytes),
                    RequiredString(
                        identity,
                        "hermes_version",
                        MaximumVersionUtf8Bytes),
                    RequiredString(identity, "hermes_commit", 40)),
                StringArray(
                    root["runtime_methods"],
                    MaximumRuntimeMethods,
                    MaximumTokenUtf8Bytes,
                    allowEmpty: false),
                DecodeSections(root["sections"]),
                DecodeCommands(root["commands"]),
                DecodeNativeTools(root["native_tools"]),
                DecodeModels(root["models"]),
                DecodeSkills(root["skills"]),
                DecodePlugins(root["plugins"]),
                DecodeMcp(root["mcp"]),
                StringArray(
                    root["warnings"],
                    MaximumWarnings,
                    MaximumTokenUtf8Bytes,
                    allowEmpty: true));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or EncoderFallbackException)
        {
            throw new InvalidDataException(
                "Hermes capability catalog was rejected.",
                exception);
        }
    }

    private static IReadOnlyList<HermesCapabilitySectionStatus> DecodeSections(
        JsonElement payload)
        => BoundedArray(payload, SectionIds.Count, allowEmpty: false)
            .EnumerateArray()
            .Select(section =>
            {
                var row = ExactObject(
                    section,
                    "id",
                    "state",
                    "reason",
                    "coverage");
                var coverage = ExactObject(
                    row["coverage"],
                    "installed",
                    "offered",
                    "configured",
                    "operable");
                return new HermesCapabilitySectionStatus(
                    RequiredString(row, "id", MaximumTokenUtf8Bytes),
                    ParseSectionState(RequiredString(
                        row,
                        "state",
                        MaximumTokenUtf8Bytes)),
                    ParseSectionReason(RequiredString(
                        row,
                        "reason",
                        MaximumTokenUtf8Bytes)),
                    new HermesCapabilitySectionCoverage(
                        DecodeCoverage(coverage["installed"]),
                        DecodeCoverage(coverage["offered"]),
                        DecodeCoverage(coverage["configured"]),
                        DecodeCoverage(coverage["operable"])));
            })
            .ToArray();

    private static HermesCapabilityCoverage DecodeCoverage(JsonElement payload)
    {
        var root = ExactObject(payload, "state", "count");
        var state = ParseCoverageState(RequiredString(
            root,
            "state",
            MaximumTokenUtf8Bytes));
        int? count = root["count"].ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number => RequiredInteger(
                root,
                "count",
                0,
                MaximumCoverageCount),
            _ => throw new InvalidDataException(
                "Hermes capability coverage count was rejected.")
        };
        return new HermesCapabilityCoverage(state, count);
    }

    private static HermesCapabilityCommandCatalog DecodeCommands(JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "commands",
            "quick_skills",
            "details",
            "quick_skill_details",
            "categories",
            "aliases",
            "quick_skill_count",
            "non_skill_count");
        var categories = BoundedArray(
                root["categories"],
                MaximumCommandCategories,
                allowEmpty: true)
            .EnumerateArray()
            .Select(category =>
            {
                var row = ExactObject(category, "name", "commands");
                return new HermesCapabilityCommandCategory(
                    RequiredString(
                        row,
                        "name",
                        MaximumDisplayTextUtf8Bytes),
                    StringArray(
                        row["commands"],
                        MaximumCommands,
                        MaximumTokenUtf8Bytes,
                        allowEmpty: true));
            })
            .ToArray();
        var aliases = BoundedArray(
                root["aliases"],
                MaximumCommands * 4,
                allowEmpty: true)
            .EnumerateArray()
            .Select(alias =>
            {
                var row = ExactObject(alias, "alias", "canonical");
                return new HermesCapabilityCommandAlias(
                    RequiredString(row, "alias", MaximumTokenUtf8Bytes),
                    RequiredString(row, "canonical", MaximumTokenUtf8Bytes));
            })
            .ToArray();
        var details = BoundedArray(
                root["details"],
                MaximumCommands,
                allowEmpty: true)
            .EnumerateArray()
            .Select(detail =>
            {
                var row = ExactObject(
                    detail,
                    "name",
                    "description",
                    "subcommands");
                return new HermesCapabilityCommandDetail(
                    RequiredString(row, "name", MaximumTokenUtf8Bytes),
                    RequiredString(
                        row,
                        "description",
                        MaximumDisplayTextUtf8Bytes,
                        allowEmpty: true),
                    StringArray(
                        row["subcommands"],
                        MaximumSubcommandsPerCommand,
                        MaximumTokenUtf8Bytes,
                        allowEmpty: true));
            })
            .ToArray();
        var quickSkillDetails = BoundedArray(
                root["quick_skill_details"],
                MaximumSkills,
                allowEmpty: true)
            .EnumerateArray()
            .Select(detail =>
            {
                var row = ExactObject(detail, "name", "usage", "origin");
                return new HermesCapabilityQuickSkillDetail(
                    RequiredString(row, "name", MaximumTokenUtf8Bytes),
                    RequiredInteger(
                        row,
                        "usage",
                        0,
                        MaximumCoverageCount),
                    RequiredString(row, "origin", MaximumTokenUtf8Bytes));
            })
            .ToArray();
        var result = new HermesCapabilityCommandCatalog(
            StringArray(
                root["commands"],
                MaximumCommands,
                MaximumTokenUtf8Bytes,
                allowEmpty: true),
            StringArray(
                root["quick_skills"],
                MaximumSkills,
                MaximumTokenUtf8Bytes,
                allowEmpty: true),
            details,
            quickSkillDetails,
            categories,
            aliases,
            RequiredInteger(root, "quick_skill_count", 0, MaximumSkills));
        if (RequiredInteger(root, "non_skill_count", 0, MaximumCommands)
            != result.NonSkillCommandCount)
        {
            throw new InvalidDataException(
                "Hermes non-Skill command count was inconsistent.");
        }
        return result;
    }

    private static HermesCapabilityNativeToolsCatalog DecodeNativeTools(
        JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "platform",
            "approval_mode",
            "enabled_toolsets",
            "installed_toolsets",
            "toolsets",
            "new_session_required",
            "loaded_snapshot_attested",
            "loaded_tools");
        if (!string.Equals(
                RequiredString(root, "platform", MaximumTokenUtf8Bytes),
                "cli",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes capability tool platform was rejected.");
        }
        var installedToolsets = BoundedArray(
                root["installed_toolsets"],
                HermesNativeToolsContract.MaximumToolsets,
                allowEmpty: true)
            .EnumerateArray()
            .Select(toolset =>
            {
                var row = ExactObject(
                    toolset,
                    "name",
                    "description",
                    "tools");
                return new HermesCapabilityInstalledToolset(
                    RequiredString(
                        row,
                        "name",
                        HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes),
                    RequiredString(
                        row,
                        "description",
                        HermesNativeToolsContract
                            .MaximumToolsetDescriptionUtf8Bytes,
                        allowEmpty: true),
                    StringArray(
                        row["tools"],
                        HermesNativeToolsContract.MaximumToolsPerToolset,
                        HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
                        allowEmpty: true));
            })
            .ToArray();
        var toolsets = BoundedArray(
                root["toolsets"],
                HermesNativeToolsContract.MaximumToolsets,
                allowEmpty: true)
            .EnumerateArray()
            .Select(toolset =>
            {
                var row = ExactObject(
                    toolset,
                    "name",
                    "label",
                    "description",
                    "enabled",
                    "configured",
                    "readiness",
                    "tools");
                return new HermesCapabilityToolset(
                    RequiredString(
                        row,
                        "name",
                        HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes),
                    RequiredString(
                        row,
                        "label",
                        HermesNativeToolsContract.MaximumToolsetLabelUtf8Bytes),
                    RequiredString(
                        row,
                        "description",
                        HermesNativeToolsContract
                            .MaximumToolsetDescriptionUtf8Bytes,
                        allowEmpty: true),
                    RequiredBoolean(row, "enabled"),
                    RequiredBoolean(row, "configured"),
                    ParseReadiness(RequiredString(
                        row,
                        "readiness",
                        MaximumTokenUtf8Bytes)),
                    StringArray(
                        row["tools"],
                        HermesNativeToolsContract.MaximumToolsPerToolset,
                        HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
                        allowEmpty: true));
            })
            .ToArray();
        return new HermesCapabilityNativeToolsCatalog(
            ParseApprovalMode(RequiredString(
                root,
                "approval_mode",
                MaximumTokenUtf8Bytes)),
            StringArray(
                root["enabled_toolsets"],
                HermesNativeToolsContract.MaximumToolsets,
                HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes,
                allowEmpty: true),
            installedToolsets,
            toolsets,
            RequiredBoolean(root, "new_session_required"),
            RequiredBoolean(root, "loaded_snapshot_attested"),
            StringArray(
                root["loaded_tools"],
                MaximumMcpTools,
                HermesNativeToolsContract.MaximumToolNameUtf8Bytes,
                allowEmpty: true));
    }

    private static HermesCapabilityModelCatalog DecodeModels(JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "default_provider",
            "default_model",
            "providers");
        var providers = BoundedArray(
                root["providers"],
                MaximumModelProviders,
                allowEmpty: true)
            .EnumerateArray()
            .Select(provider =>
            {
                var row = ExactObject(
                    provider,
                    "id",
                    "name",
                    "current",
                    "user_defined",
                    "authenticated",
                    "models");
                var models = BoundedArray(
                        row["models"],
                        MaximumModelsPerProvider,
                        allowEmpty: true)
                    .EnumerateArray()
                    .Select(model =>
                    {
                        var modelRow = ExactObject(
                            model,
                            "id",
                            "available",
                            "featured",
                            "capability_known",
                            "fast",
                            "reasoning");
                        return new HermesCapabilityModel(
                            RequiredString(
                                modelRow,
                                "id",
                                MaximumTokenUtf8Bytes),
                            RequiredBoolean(modelRow, "available"),
                            RequiredBoolean(modelRow, "featured"),
                            RequiredBoolean(modelRow, "capability_known"),
                            RequiredBoolean(modelRow, "fast"),
                            RequiredBoolean(modelRow, "reasoning"));
                    })
                    .ToArray();
                return new HermesCapabilityModelProvider(
                    RequiredString(row, "id", MaximumTokenUtf8Bytes),
                    RequiredString(
                        row,
                        "name",
                        MaximumDisplayTextUtf8Bytes),
                    RequiredBoolean(row, "current"),
                    RequiredBoolean(row, "user_defined"),
                    RequiredBoolean(row, "authenticated"),
                    models);
            })
            .ToArray();
        return new HermesCapabilityModelCatalog(
            RequiredString(
                root,
                "default_provider",
                MaximumTokenUtf8Bytes,
                allowEmpty: true),
            RequiredString(
                root,
                "default_model",
                MaximumTokenUtf8Bytes,
                allowEmpty: true),
            providers);
    }

    private static HermesCapabilitySkillCatalog DecodeSkills(
        JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "installed",
            "offered_count",
            "excluded_count");
        var installed = BoundedArray(
                root["installed"],
                MaximumSkills,
                allowEmpty: true)
            .EnumerateArray()
            .Select(skill =>
            {
                var row = ExactObject(
                    skill,
                    "name",
                    "category",
                    "offered",
                    "exclusion_reason");
                return new HermesCapabilitySkill(
                    RequiredString(
                        row,
                        "name",
                        MaximumDisplayTextUtf8Bytes),
                    RequiredString(
                        row,
                        "category",
                        MaximumDisplayTextUtf8Bytes),
                    RequiredBoolean(row, "offered"),
                    ParseSkillExclusionReason(RequiredString(
                        row,
                        "exclusion_reason",
                        MaximumTokenUtf8Bytes)));
            })
            .ToArray();
        return new HermesCapabilitySkillCatalog(
            installed,
            RequiredInteger(root, "offered_count", 0, MaximumSkills),
            RequiredInteger(root, "excluded_count", 0, MaximumSkills));
    }

    private static HermesCapabilityPluginCatalog DecodePlugins(JsonElement payload)
    {
        var root = ExactObject(
            payload,
            "plugins",
            "user_count",
            "bundled_count");
        var plugins = BoundedArray(
                root["plugins"],
                MaximumPlugins,
                allowEmpty: true)
            .EnumerateArray()
            .Select(plugin =>
            {
                var row = ExactObject(
                    plugin,
                    "name",
                    "key",
                    "version",
                    "description",
                    "source",
                    "status",
                    "portable");
                return new HermesCapabilityPlugin(
                    RequiredString(
                        row,
                        "name",
                        MaximumDisplayTextUtf8Bytes),
                    RequiredString(row, "key", MaximumTokenUtf8Bytes),
                    RequiredString(
                        row,
                        "version",
                        MaximumVersionUtf8Bytes,
                        allowEmpty: true),
                    RequiredString(
                        row,
                        "description",
                        MaximumDisplayTextUtf8Bytes,
                        allowEmpty: true),
                    RequiredString(row, "source", MaximumTokenUtf8Bytes),
                    ParsePluginStatus(RequiredString(
                        row,
                        "status",
                        MaximumTokenUtf8Bytes)),
                    RequiredBoolean(row, "portable"));
            })
            .ToArray();
        return new HermesCapabilityPluginCatalog(
            plugins,
            RequiredInteger(root, "user_count", 0, MaximumPlugins),
            RequiredInteger(root, "bundled_count", 0, MaximumPlugins));
    }

    private static HermesCapabilityMcpCatalog DecodeMcp(JsonElement payload)
    {
        var root = ExactObject(payload, "state", "servers");
        var servers = BoundedArray(
                root["servers"],
                MaximumMcpServers,
                allowEmpty: true)
            .EnumerateArray()
            .Select(server =>
            {
                var row = ExactObject(
                    server,
                    "name",
                    "transport",
                    "tool_count",
                    "connected",
                    "disabled",
                    "status");
                return new HermesCapabilityMcpServer(
                    RequiredString(
                        row,
                        "name",
                        MaximumDisplayTextUtf8Bytes),
                    RequiredString(row, "transport", MaximumTokenUtf8Bytes),
                    RequiredInteger(row, "tool_count", 0, MaximumMcpTools),
                    RequiredBoolean(row, "connected"),
                    RequiredBoolean(row, "disabled"),
                    ParseMcpStatus(RequiredString(
                        row,
                        "status",
                        MaximumTokenUtf8Bytes)));
            })
            .ToArray();
        return new HermesCapabilityMcpCatalog(
            ParseMcpState(RequiredString(root, "state", MaximumTokenUtf8Bytes)),
            servers);
    }

    private static IReadOnlyDictionary<string, JsonElement> ExactObject(
        JsonElement payload,
        params string[] expectedProperties)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes capability object was rejected.");
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!expected.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException(
                    "Hermes capability properties were rejected.");
            }
        }
        if (values.Count != expected.Count)
            throw new InvalidDataException("Hermes capability object was incomplete.");
        return new ReadOnlyDictionary<string, JsonElement>(values);
    }

    private static JsonElement BoundedArray(
        JsonElement value,
        int maximumCount,
        bool allowEmpty)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximumCount
            || !allowEmpty && value.GetArrayLength() == 0)
        {
            throw new InvalidDataException("Hermes capability array was rejected.");
        }
        return value;
    }

    private static IReadOnlyList<string> StringArray(
        JsonElement value,
        int maximumCount,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        var array = BoundedArray(value, maximumCount, allowEmpty);
        var result = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Hermes capability token was rejected.");
            result.Add(Text(
                item.GetString(),
                maximumUtf8Bytes,
                "catalog_item"));
        }
        return result;
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumUtf8Bytes,
        bool allowEmpty = false)
    {
        if (values[name].ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes capability text was rejected.");
        return Text(
            values[name].GetString(),
            maximumUtf8Bytes,
            name,
            allowEmpty);
    }

    private static bool RequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Hermes capability state was rejected.");
        return value.GetBoolean();
    }

    private static int RequiredInteger(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int minimum,
        int maximum)
    {
        var value = values[name];
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < minimum
            || result > maximum)
        {
            throw new InvalidDataException("Hermes capability count was rejected.");
        }
        return result;
    }

    private static HermesCapabilitySectionState ParseSectionState(string value)
        => value switch
        {
            "available" => HermesCapabilitySectionState.Available,
            "unavailable" => HermesCapabilitySectionState.Unavailable,
            _ => throw new InvalidDataException(
                "Hermes capability section state was rejected.")
        };

    private static HermesCapabilitySectionReason ParseSectionReason(string value)
        => value switch
        {
            "none" => HermesCapabilitySectionReason.None,
            "request_failed" => HermesCapabilitySectionReason.RequestFailed,
            "invalid_payload" => HermesCapabilitySectionReason.InvalidPayload,
            "unsupported" => HermesCapabilitySectionReason.Unsupported,
            "no_attested_session" =>
                HermesCapabilitySectionReason.NoAttestedSession,
            _ => throw new InvalidDataException(
                "Hermes capability section reason was rejected.")
        };

    private static HermesCapabilityCoverageState ParseCoverageState(string value)
        => value switch
        {
            "attested" => HermesCapabilityCoverageState.Attested,
            "partial" => HermesCapabilityCoverageState.Partial,
            "unknown" => HermesCapabilityCoverageState.Unknown,
            "not_applicable" => HermesCapabilityCoverageState.NotApplicable,
            _ => throw new InvalidDataException(
                "Hermes capability coverage state was rejected.")
        };

    private static HermesCapabilitySkillExclusionReason
        ParseSkillExclusionReason(string value)
        => value switch
        {
            "none" => HermesCapabilitySkillExclusionReason.None,
            "platform_incompatible" =>
                HermesCapabilitySkillExclusionReason.PlatformIncompatible,
            "environment_incompatible" =>
                HermesCapabilitySkillExclusionReason.EnvironmentIncompatible,
            "disabled" => HermesCapabilitySkillExclusionReason.Disabled,
            "other" => HermesCapabilitySkillExclusionReason.Other,
            _ => throw new InvalidDataException(
                "Hermes Skill exclusion reason was rejected.")
        };

    private static HermesNativeApprovalMode ParseApprovalMode(string value)
        => value switch
        {
            "manual" => HermesNativeApprovalMode.Manual,
            "smart" => HermesNativeApprovalMode.Smart,
            "off" => HermesNativeApprovalMode.Off,
            _ => throw new InvalidDataException(
                "Hermes capability approval mode was rejected.")
        };

    private static HermesNativeToolsetReadiness ParseReadiness(string value)
        => value switch
        {
            "ready" => HermesNativeToolsetReadiness.Ready,
            "needs_setup" => HermesNativeToolsetReadiness.NeedsSetup,
            "needs_auth" => HermesNativeToolsetReadiness.NeedsAuth,
            "needs_keys" => HermesNativeToolsetReadiness.NeedsKeys,
            _ => throw new InvalidDataException(
                "Hermes capability readiness was rejected.")
        };

    private static HermesCapabilityPluginStatus ParsePluginStatus(string value)
        => value switch
        {
            "enabled" => HermesCapabilityPluginStatus.Enabled,
            "disabled" => HermesCapabilityPluginStatus.Disabled,
            "not_enabled" => HermesCapabilityPluginStatus.NotEnabled,
            "unknown" => HermesCapabilityPluginStatus.Unknown,
            _ => throw new InvalidDataException(
                "Hermes capability plugin status was rejected.")
        };

    private static HermesCapabilityMcpSnapshotState ParseMcpState(string value)
        => value switch
        {
            "unavailable" => HermesCapabilityMcpSnapshotState.Unavailable,
            "attested" => HermesCapabilityMcpSnapshotState.Attested,
            _ => throw new InvalidDataException(
                "Hermes capability MCP state was rejected.")
        };

    private static HermesCapabilityMcpServerStatus ParseMcpStatus(string value)
        => value switch
        {
            "connected" => HermesCapabilityMcpServerStatus.Connected,
            "disabled" => HermesCapabilityMcpServerStatus.Disabled,
            "connecting" => HermesCapabilityMcpServerStatus.Connecting,
            "failed" => HermesCapabilityMcpServerStatus.Failed,
            "configured" => HermesCapabilityMcpServerStatus.Configured,
            _ => throw new InvalidDataException(
                "Hermes capability MCP server status was rejected.")
        };

    public static string PluginStatusToken(HermesCapabilityPluginStatus status)
        => status switch
        {
            HermesCapabilityPluginStatus.Enabled => "enabled",
            HermesCapabilityPluginStatus.Disabled => "disabled",
            HermesCapabilityPluginStatus.NotEnabled => "not_enabled",
            HermesCapabilityPluginStatus.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

    public static string SectionStateToken(HermesCapabilitySectionState state)
        => state switch
        {
            HermesCapabilitySectionState.Available => "available",
            HermesCapabilitySectionState.Unavailable => "unavailable",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    public static string SectionReasonToken(HermesCapabilitySectionReason reason)
        => reason switch
        {
            HermesCapabilitySectionReason.None => "none",
            HermesCapabilitySectionReason.RequestFailed => "request_failed",
            HermesCapabilitySectionReason.InvalidPayload => "invalid_payload",
            HermesCapabilitySectionReason.Unsupported => "unsupported",
            HermesCapabilitySectionReason.NoAttestedSession =>
                "no_attested_session",
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };

    public static string CoverageStateToken(HermesCapabilityCoverageState state)
        => state switch
        {
            HermesCapabilityCoverageState.Attested => "attested",
            HermesCapabilityCoverageState.Partial => "partial",
            HermesCapabilityCoverageState.Unknown => "unknown",
            HermesCapabilityCoverageState.NotApplicable => "not_applicable",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    public static string SkillExclusionReasonToken(
        HermesCapabilitySkillExclusionReason reason)
        => reason switch
        {
            HermesCapabilitySkillExclusionReason.None => "none",
            HermesCapabilitySkillExclusionReason.PlatformIncompatible =>
                "platform_incompatible",
            HermesCapabilitySkillExclusionReason.EnvironmentIncompatible =>
                "environment_incompatible",
            HermesCapabilitySkillExclusionReason.Disabled => "disabled",
            HermesCapabilitySkillExclusionReason.Other => "other",
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };

    public static string McpStateToken(HermesCapabilityMcpSnapshotState state)
        => state switch
        {
            HermesCapabilityMcpSnapshotState.Unavailable => "unavailable",
            HermesCapabilityMcpSnapshotState.Attested => "attested",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    public static string McpStatusToken(HermesCapabilityMcpServerStatus status)
        => status switch
        {
            HermesCapabilityMcpServerStatus.Connected => "connected",
            HermesCapabilityMcpServerStatus.Disabled => "disabled",
            HermesCapabilityMcpServerStatus.Connecting => "connecting",
            HermesCapabilityMcpServerStatus.Failed => "failed",
            HermesCapabilityMcpServerStatus.Configured => "configured",
            _ => throw new ArgumentOutOfRangeException(nameof(status))
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
            throw new ArgumentException(
                "Hermes capability catalog text was rejected.",
                parameterName);
        }
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Hermes capability catalog text was not valid Unicode.",
                parameterName,
                exception);
        }
        if (byteCount > maximumUtf8Bytes)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static string SectionId(string? value, string parameterName)
    {
        value = Token(value, MaximumTokenUtf8Bytes, parameterName);
        if (!SectionIds.Contains(value, StringComparer.Ordinal))
            throw new ArgumentException("Unknown Hermes capability section.", parameterName);
        return value;
    }

    internal static string Token(
        string? value,
        int maximumUtf8Bytes,
        string parameterName,
        bool requireSlashPrefix = false)
    {
        value = Text(value, maximumUtf8Bytes, parameterName);
        if (requireSlashPrefix && !value.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("A slash command token was required.", parameterName);
        if (value.Any(character =>
                !IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or '-' or '/' or ':' or '@' or '+')))
        {
            throw new ArgumentException(
                "Hermes capability catalog token was rejected.",
                parameterName);
        }
        return value;
    }

    private static bool IsAsciiLetterOrDigit(char value)
        => value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9';

    internal static string ProviderToken(string? value, string parameterName)
    {
        value = Token(value, MaximumTokenUtf8Bytes, parameterName);
        if (value.StartsWith("custom:", StringComparison.Ordinal))
        {
            var name = value[7..];
            if (name.Length == 0 || name[0] == '-'
                || name.Any(character => !IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
                throw new ArgumentException("Hermes custom provider token was rejected.", parameterName);
            return value;
        }
        if (value[0] is not (>= 'a' and <= 'z' or >= '0' and <= '9')
            || value.Skip(1).Any(character =>
                character is not (>= 'a' and <= 'z' or >= '0' and <= '9'
                    or '-' or '_')))
        {
            throw new ArgumentException(
                "Hermes provider token was rejected.",
                parameterName);
        }
        return value;
    }

    internal static string QuickSkillOrigin(
        string? value,
        string parameterName)
    {
        value = Token(value, MaximumTokenUtf8Bytes, parameterName);
        if (value is not ("hub" or "bundled" or "local"))
        {
            throw new ArgumentException(
                "Hermes Quick Skill origin was rejected.",
                parameterName);
        }
        return value;
    }

    internal static string ModelToken(string? value, string parameterName)
    {
        value = Token(value, MaximumTokenUtf8Bytes, parameterName);
        if (value[0] == '-')
            throw new ArgumentException("Hermes model token was rejected.", parameterName);
        return value;
    }

    internal static string LowerHex(
        string? value,
        int expectedCharacters,
        string parameterName)
    {
        value = Text(value, expectedCharacters, parameterName);
        if (value.Length != expectedCharacters
            || value.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Hermes capability identity digest was rejected.",
                parameterName);
        }
        return value;
    }

    internal static IReadOnlyList<string> UniqueTokens(
        IEnumerable<string> values,
        int maximumCount,
        int maximumUtf8Bytes,
        string parameterName,
        bool requireSlashPrefix = false)
    {
        if (values is null)
            throw new ArgumentNullException(parameterName);
        var rows = values.ToArray();
        if (rows.Length > maximumCount)
            throw new ArgumentOutOfRangeException(parameterName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index] = Token(
                rows[index],
                maximumUtf8Bytes,
                $"{parameterName}[{index}]",
                requireSlashPrefix);
            if (!seen.Add(rows[index]))
            {
                throw new ArgumentException(
                    "Hermes capability catalog tokens must be unique.",
                    parameterName);
            }
        }
        return Array.AsReadOnly(rows);
    }

    internal static IReadOnlyList<T> UniqueRows<T>(
        IEnumerable<T> values,
        int maximumCount,
        Func<T, string> identity,
        string parameterName)
        where T : class
    {
        if (values is null)
            throw new ArgumentNullException(parameterName);
        var rows = values.ToArray();
        if (rows.Length > maximumCount || rows.Any(static row => row is null))
            throw new ArgumentOutOfRangeException(parameterName);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!seen.Add(identity(row)))
            {
                throw new ArgumentException(
                    "Hermes capability catalog row identities must be unique.",
                    parameterName);
            }
        }
        return new ReadOnlyCollection<T>(rows);
    }
}
