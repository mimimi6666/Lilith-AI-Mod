using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilithRuntimeContracts;

namespace LilithControlCenter;

internal enum D046HermesCapabilityCatalogStatusTone
{
    Muted = 1,
    Accent = 2,
    Good = 3,
    Warning = 4,
    Danger = 5
}

internal sealed record D046HermesCapabilityCatalogEntryViewModel(
    string Name,
    string IdentityText,
    string DetailText,
    string StatusText,
    string ReadinessText,
    D046HermesCapabilityCatalogStatusTone StatusTone)
{
    // A string keeps this projection independent from WPF while still letting
    // the Foreground binding perform its ordinary Brush type conversion.
    public string StatusBrush => StatusTone switch
    {
        D046HermesCapabilityCatalogStatusTone.Good => "#1D795A",
        D046HermesCapabilityCatalogStatusTone.Accent => "#B93343",
        D046HermesCapabilityCatalogStatusTone.Warning => "#936110",
        D046HermesCapabilityCatalogStatusTone.Danger => "#BD354B",
        _ => "#52627A"
    };
}

internal sealed class D046HermesCapabilityCatalogSectionViewModel
    : INotifyPropertyChanged
{
    private readonly IReadOnlyList<D046HermesCapabilityCatalogEntryViewModel>
        _allEntries;
    private bool _isExpanded;

    internal D046HermesCapabilityCatalogSectionViewModel(
        string title,
        string summary,
        string countText,
        IEnumerable<D046HermesCapabilityCatalogEntryViewModel> entries,
        bool initiallyExpanded = false,
        D046HermesCapabilityCatalogStatusTone summaryTone =
            D046HermesCapabilityCatalogStatusTone.Muted)
    {
        Title = title;
        Summary = summary;
        CountText = countText;
        _allEntries = Array.AsReadOnly(entries.ToArray());
        _isExpanded = initiallyExpanded && _allEntries.Count != 0;
        SummaryBrush = summaryTone switch
        {
            D046HermesCapabilityCatalogStatusTone.Warning => "#936110",
            D046HermesCapabilityCatalogStatusTone.Danger => "#BD354B",
            D046HermesCapabilityCatalogStatusTone.Good => "#1D795A",
            D046HermesCapabilityCatalogStatusTone.Accent => "#B93343",
            _ => "#52627A"
        };
        if (_isExpanded)
            MaterializeEntries();
    }

    public string Title { get; }
    public string Summary { get; }
    public string SummaryBrush { get; }
    public string CountText { get; }
    public bool HasEntries => _allEntries.Count != 0;
    public ObservableCollection<D046HermesCapabilityCatalogEntryViewModel>
        Entries { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            var next = value && HasEntries;
            if (_isExpanded == next)
                return;
            _isExpanded = next;
            if (_isExpanded)
                MaterializeEntries();
            else
                Entries.Clear();
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void MaterializeEntries()
    {
        if (Entries.Count != 0)
            return;
        foreach (var entry in _allEntries)
            Entries.Add(entry);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed record D046HermesCapabilityCatalogDisplayProjection(
    IReadOnlyList<D046HermesCapabilityCatalogSectionViewModel> Sections);

internal static class D046HermesCapabilityCatalogDisplayProjector
{
    internal static D046HermesCapabilityCatalogDisplayProjection Project(
        HermesCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var sections = new List<D046HermesCapabilityCatalogSectionViewModel>
        {
            IdentitySection(catalog),
            RuntimeMethodsSection(catalog),
            CommandsSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.CommandsSectionId)),
            NativeToolsSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.NativeToolsSectionId)),
            ModelsSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.ModelsSectionId)),
            SkillsSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.SkillsSectionId)),
            PluginsSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.PluginsSectionId)),
            McpSection(
                catalog,
                catalog.Section(HermesCapabilityCatalogContract.McpSectionId)),
            WarningsSection(catalog)
        };
        return new D046HermesCapabilityCatalogDisplayProjection(
            Array.AsReadOnly(sections.ToArray()));
    }

    private static D046HermesCapabilityCatalogSectionViewModel IdentitySection(
        HermesCapabilityCatalog catalog)
    {
        var identity = catalog.Identity;
        var entries = new[]
        {
            Entry(
                ControlCenterLocalization.Current.Get("capability.runtime"),
                ControlCenterLocalization.Current.Format("capability.versionFormat", identity.RuntimeVersion),
                ControlCenterLocalization.Current.Get("capability.providesCatalog"),
                ControlCenterLocalization.Current.Get("capability.attested"),
                ControlCenterLocalization.Current.Get("capability.versionNotPermission"),
                D046HermesCapabilityCatalogStatusTone.Good),
            Entry(
                ControlCenterLocalization.Current.Get("capability.engine"),
                ControlCenterLocalization.Current.Format("capability.versionFormat", identity.HermesVersion),
                ControlCenterLocalization.Current.Get("capability.engineDetail"),
                ControlCenterLocalization.Current.Get("capability.attested"),
                ControlCenterLocalization.Current.Get("capability.versionNotPermission"),
                D046HermesCapabilityCatalogStatusTone.Good),
            Entry(
                ControlCenterLocalization.Current.Get("capability.revision"),
                identity.HermesCommit,
                ControlCenterLocalization.Current.Get("capability.revisionDetail"),
                ControlCenterLocalization.Current.Get("capability.attested"),
                ControlCenterLocalization.Current.Get("capability.readOnlyVersion"),
                D046HermesCapabilityCatalogStatusTone.Good)
        };
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.versions"),
            ControlCenterLocalization.Current.Get("capability.versionsDetail"),
            ControlCenterLocalization.Current.Format("capability.items", 3),
            entries,
            initiallyExpanded: true);
    }

    private static D046HermesCapabilityCatalogSectionViewModel
        RuntimeMethodsSection(HermesCapabilityCatalog catalog)
    {
        var entries = catalog.RuntimeMethods.Select(method => Entry(
            method,
            ControlCenterLocalization.Current.Get("capability.interfaces"),
            ControlCenterLocalization.Current.Get("capability.interfaceDetail"),
            ControlCenterLocalization.Current.Get("capability.published"),
            ControlCenterLocalization.Current.Get("capability.authorizationRequired"),
            D046HermesCapabilityCatalogStatusTone.Accent));
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.interfaces"),
            ControlCenterLocalization.Current.Get("capability.interfacesDetail"),
            ControlCenterLocalization.Current.Format("capability.items", catalog.RuntimeMethods.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel CommandsSection(
        HermesCapabilityCatalog catalog,
        HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection(ControlCenterLocalization.Current.Get("capability.commands"), ControlCenterLocalization.Current.Get("capability.commandsUnknown"), section);

        var categoryByCommand = new Dictionary<string, List<string>>(
            StringComparer.Ordinal);
        foreach (var category in catalog.Commands.Categories)
        {
            foreach (var command in category.Commands)
            {
                if (!categoryByCommand.TryGetValue(command, out var categories))
                {
                    categories = new List<string>();
                    categoryByCommand.Add(command, categories);
                }
                categories.Add(category.Name);
            }
        }

        var aliasesByCommand = new Dictionary<string, List<string>>(
            StringComparer.Ordinal);
        foreach (var alias in catalog.Commands.Aliases)
        {
            if (!aliasesByCommand.TryGetValue(alias.Canonical, out var aliases))
            {
                aliases = new List<string>();
                aliasesByCommand.Add(alias.Canonical, aliases);
            }
            aliases.Add(alias.Alias);
        }

        var quickSkills = catalog.Commands.QuickSkills.ToHashSet(
            StringComparer.Ordinal);
        foreach (var skill in catalog.Skills.Installed.Where(skill =>
                     skill.Offered))
        {
            var command = "/" + skill.Name;
            if (!quickSkills.Contains(command)
                || categoryByCommand.ContainsKey(command))
            {
                continue;
            }
            categoryByCommand.Add(command, new List<string> { skill.Category });
        }
        var detailsByCommand = catalog.Commands.Details.ToDictionary(
            detail => detail.Name,
            StringComparer.Ordinal);
        var quickDetailsByCommand =
            catalog.Commands.QuickSkillDetails.ToDictionary(
                detail => detail.Name,
                StringComparer.Ordinal);
        var entries = catalog.Commands.Commands.Select(command =>
        {
            var categories = categoryByCommand.TryGetValue(command, out var known)
                ? string.Join(", ", known)
                : ControlCenterLocalization.Current.Get("capability.uncategorized");
            var metadata = detailsByCommand[command];
            var detailParts = new List<string>();
            if (!string.IsNullOrEmpty(metadata.Description))
                detailParts.Add(metadata.Description);
            if (metadata.Subcommands.Count != 0)
            {
                detailParts.Add(
                    ControlCenterLocalization.Current.Get("capability.subcommands") + string.Join(", ", metadata.Subcommands));
            }
            if (aliasesByCommand.TryGetValue(command, out var aliases))
                detailParts.Add(ControlCenterLocalization.Current.Get("capability.aliases") + string.Join(", ", aliases));
            if (detailParts.Count == 0)
                detailParts.Add(ControlCenterLocalization.Current.Get("capability.noDescription"));
            var readiness = ControlCenterLocalization.Current.Get("capability.listedNotRunnable");
            if (quickDetailsByCommand.TryGetValue(command, out var quickDetail))
            {
                readiness = ControlCenterLocalization.Current.Format("capability.origin", QuickSkillOriginText(quickDetail.Origin))
                    + ControlCenterLocalization.Current.Format("capability.usageCount", quickDetail.Usage) + readiness;
            }
            return Entry(
                command,
                (quickSkills.Contains(command) ? ControlCenterLocalization.Current.Get("capability.quickPrefix") : ControlCenterLocalization.Current.Get("capability.commandPrefix"))
                    + ControlCenterLocalization.Current.Get("capability.categoryPrefix") + categories,
                string.Join(" ", detailParts),
                quickSkills.Contains(command) ? ControlCenterLocalization.Current.Get("capability.quick") : ControlCenterLocalization.Current.Get("capability.command"),
                readiness,
                D046HermesCapabilityCatalogStatusTone.Accent);
        });
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.commands"),
            ControlCenterLocalization.Current.Format("capability.categoriesOffered", catalog.Commands.Categories.Count)
                + ControlCenterLocalization.Current.Format("capability.operableSuffix", CoverageText(section.Coverage.Offered))
                + $"{CoverageText(section.Coverage.Operable)}。",
            ControlCenterLocalization.Current.Format("capability.regularCount", catalog.Commands.NonSkillCommandCount)
                + ControlCenterLocalization.Current.Format("capability.quickCount", catalog.Commands.QuickSkillCount)
                + ControlCenterLocalization.Current.Format("capability.totalCount", catalog.Commands.Commands.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel
        NativeToolsSection(
            HermesCapabilityCatalog catalog,
            HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection(ControlCenterLocalization.Current.Get("capability.tools"), ControlCenterLocalization.Current.Get("capability.toolsUnknown"), section);

        var entries = new List<D046HermesCapabilityCatalogEntryViewModel>();
        var membershipCount = 0;
        var distinctTools = new HashSet<string>(StringComparer.Ordinal);
        var loadedTools = catalog.NativeTools.LoadedTools.ToHashSet(
            StringComparer.Ordinal);
        var offeredToolsets = catalog.NativeTools.Toolsets
            .Select(toolset => toolset.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (catalog.NativeTools.NewSessionRequired)
        {
            entries.Add(Entry(
                ControlCenterLocalization.Current.Get("capability.toolSettings"),
                ControlCenterLocalization.Current.Get("capability.settingsUpdated"),
                ControlCenterLocalization.Current.Get("capability.settingsDetail"),
                ControlCenterLocalization.Current.Get("capability.newSession"),
                ControlCenterLocalization.Current.Get("capability.newSessionDetail"),
                D046HermesCapabilityCatalogStatusTone.Warning));
        }
        foreach (var toolset in catalog.NativeTools.Toolsets)
        {
            var (status, readiness, tone) = ToolsetStatus(toolset);
            var loadedCount = toolset.Tools.Count(loadedTools.Contains);
            readiness += catalog.NativeTools.LoadedSnapshotAttested
                ? ControlCenterLocalization.Current.Format("capability.loadedCount", loadedCount, toolset.Tools.Count)
                : ControlCenterLocalization.Current.Get("capability.loadedUnknown");
            entries.Add(Entry(
                ControlCenterLocalization.Current.Get("tool." + toolset.Name + ".label", toolset.Label),
                ControlCenterLocalization.Current.Format("capability.toolsetCount", toolset.Name, toolset.Tools.Count),
                string.IsNullOrEmpty(toolset.Description)
                    ? ControlCenterLocalization.Current.Get("capability.toolsetNoDescription")
                    : ControlCenterLocalization.Current.Get("tool." + toolset.Name + ".description", toolset.Description),
                status,
                readiness,
                tone));
            foreach (var tool in toolset.Tools)
            {
                membershipCount++;
                distinctTools.Add(tool);
                entries.Add(Entry(
                    tool,
                    ControlCenterLocalization.Current.Format("capability.toolsetIdentity", ControlCenterLocalization.Current.Get("tool." + toolset.Name + ".label", toolset.Label), toolset.Name),
                    ControlCenterLocalization.Current.Get("capability.toolIdentifier"),
                    status,
                    readiness
                        + (catalog.NativeTools.LoadedSnapshotAttested
                            ? loadedTools.Contains(tool)
                                ? ControlCenterLocalization.Current.Get("capability.toolLoaded")
                                : ControlCenterLocalization.Current.Get("capability.toolNotLoaded")
                            : string.Empty),
                    tone));
            }
        }
        foreach (var toolset in catalog.NativeTools.InstalledToolsets.Where(
                     toolset => !offeredToolsets.Contains(toolset.Name)))
        {
            entries.Add(Entry(
                toolset.Name,
                ControlCenterLocalization.Current.Format("capability.installedTools", toolset.Tools.Count),
                string.IsNullOrEmpty(toolset.Description)
                    ? ControlCenterLocalization.Current.Get("capability.toolsetNoDescription")
                    : ControlCenterLocalization.Current.Get("tool." + toolset.Name + ".description", toolset.Description),
                ControlCenterLocalization.Current.Get("capability.notOffered"),
                ControlCenterLocalization.Current.Get("capability.installedNotLoaded"),
                D046HermesCapabilityCatalogStatusTone.Muted));
        }

        var installedToolsetCount = section.Coverage.Installed.State
            == HermesCapabilityCoverageState.Attested
                ? ControlCenterLocalization.Current.Format("capability.installedSets", catalog.NativeTools.InstalledToolsets.Count)
                : ControlCenterLocalization.Current.Get("capability.installedSetsUnknown");
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.tools"),
            ControlCenterLocalization.Current.Format("capability.installedCoverage", CoverageText(section.Coverage.Installed))
                + ControlCenterLocalization.Current.Format("capability.approvalMode", catalog.NativeTools.ApprovalModeToken)
                + (catalog.NativeTools.NewSessionRequired
                    ? ControlCenterLocalization.Current.Get("capability.changedNewSession")
                    : string.Empty)
                + ControlCenterLocalization.Current.Format("capability.offeredCoverage", CoverageText(section.Coverage.Offered))
                + ControlCenterLocalization.Current.Format("capability.operableCoverage", CoverageText(section.Coverage.Operable)),
            installedToolsetCount + " / "
                + ControlCenterLocalization.Current.Format("capability.offeredSets", catalog.NativeTools.Toolsets.Count)
                + ControlCenterLocalization.Current.Format("capability.toolMembership", membershipCount, distinctTools.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel ModelsSection(
        HermesCapabilityCatalog catalog,
        HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection(ControlCenterLocalization.Current.Get("capability.models"), ControlCenterLocalization.Current.Get("capability.modelsUnknown"), section);

        var entries = new List<D046HermesCapabilityCatalogEntryViewModel>();
        var modelCount = 0;
        foreach (var provider in catalog.Models.Providers)
        {
            var providerTone = provider.Authenticated
                ? D046HermesCapabilityCatalogStatusTone.Good
                : D046HermesCapabilityCatalogStatusTone.Warning;
            entries.Add(Entry(
                provider.Name,
                provider.Id,
                ProviderDetail(provider, catalog.Models),
                provider.Authenticated ? ControlCenterLocalization.Current.Get("capability.credentialVerified") : ControlCenterLocalization.Current.Get("capability.credentialUnknown"),
                provider.Authenticated
                    ? ControlCenterLocalization.Current.Get("capability.credentialVerifiedUntried")
                    : ControlCenterLocalization.Current.Get("capability.credentialUnknownUntried"),
                providerTone));

            foreach (var model in provider.Models)
            {
                modelCount++;
                var tone = model.Available
                    ? D046HermesCapabilityCatalogStatusTone.Accent
                    : D046HermesCapabilityCatalogStatusTone.Muted;
                entries.Add(Entry(
                    model.Id,
                    ControlCenterLocalization.Current.Format("capability.providerIdentity", provider.Name, provider.Id),
                    ModelDetail(model, provider, catalog.Models),
                    model.Available
                        ? ControlCenterLocalization.Current.Get("capability.listedAvailable")
                        : ControlCenterLocalization.Current.Get("capability.listedUnavailable"),
                    ControlCenterLocalization.Current.Get("capability.untriedPrefix")
                        + (provider.Authenticated ? ControlCenterLocalization.Current.Get("capability.credentialVerified") : ControlCenterLocalization.Current.Get("capability.credentialUnknown")),
                    tone));
            }
        }

        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.models"),
            ControlCenterLocalization.Current.Get("capability.modelsDetail")
                + $"{CoverageText(section.Coverage.Operable)}。",
            ControlCenterLocalization.Current.Format("capability.providerModelsCount", catalog.Models.Providers.Count, modelCount),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel SkillsSection(
        HermesCapabilityCatalog catalog,
        HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection(ControlCenterLocalization.Current.Get("capability.skills"), ControlCenterLocalization.Current.Get("capability.skillsUnknown"), section);

        // Preserve the raw native inventory for diagnostics, but do not offer
        // OS-incompatible skills in the player's day-to-day catalog. Missing
        // setup, disabled and environment-relevance rows remain visible.
        var entries = catalog.Skills.Installed
            .Where(skill => skill.ExclusionReason
                != HermesCapabilitySkillExclusionReason.PlatformIncompatible)
            .Select(skill => skill.Offered
            ? Entry(
                skill.Name,
                ControlCenterLocalization.Current.Get("capability.categoryPrefix") + skill.Category,
                ControlCenterLocalization.Current.Get("capability.skillOfferedDetail"),
                ControlCenterLocalization.Current.Get("capability.offered"),
                ControlCenterLocalization.Current.Get("capability.executionUnconfirmed"),
                D046HermesCapabilityCatalogStatusTone.Accent)
            : Entry(
                skill.Name,
                ControlCenterLocalization.Current.Get("capability.categoryPrefix") + skill.Category,
                ControlCenterLocalization.Current.Get("capability.skillNotOfferedDetail"),
                ControlCenterLocalization.Current.Get("capability.notOffered"),
                ControlCenterLocalization.Current.Get("capability.reasonPrefix") + SkillExclusionReasonText(skill.ExclusionReason),
                D046HermesCapabilityCatalogStatusTone.Muted));
        var platformExcluded = catalog.Skills.Installed.Count(skill =>
            skill.ExclusionReason
                == HermesCapabilitySkillExclusionReason.PlatformIncompatible);
        var environmentExcluded = catalog.Skills.Installed.Count(skill =>
            skill.ExclusionReason
                == HermesCapabilitySkillExclusionReason.EnvironmentIncompatible);
        var disabledExcluded = catalog.Skills.Installed.Count(skill =>
            skill.ExclusionReason
                == HermesCapabilitySkillExclusionReason.Disabled);
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.skills"),
            ControlCenterLocalization.Current.Get("capability.skillsDetail")
                + ControlCenterLocalization.Current.Format("capability.configuredCoverage", CoverageText(section.Coverage.Configured))
                + ControlCenterLocalization.Current.Format("capability.operableCoverage", CoverageText(section.Coverage.Operable)),
            ControlCenterLocalization.Current.Format("capability.installedCount", CoverageCountText(section.Coverage.Installed))
                + ControlCenterLocalization.Current.Format("capability.offeredCount", CoverageCountText(section.Coverage.Offered))
                + ControlCenterLocalization.Current.Format("capability.excludedCount", SkillExcludedCountText(catalog.Skills, section))
                + ControlCenterLocalization.Current.Format("capability.excludedPlatformEnvironment", platformExcluded, environmentExcluded)
                + ControlCenterLocalization.Current.Format("capability.excludedDisabled", disabledExcluded),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel PluginsSection(
        HermesCapabilityCatalog catalog,
        HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection(ControlCenterLocalization.Current.Get("capability.plugins"), ControlCenterLocalization.Current.Get("capability.pluginsUnknown"), section);

        var entries = catalog.Plugins.Plugins.Select(plugin =>
        {
            var (status, tone) = plugin.Status switch
            {
                HermesCapabilityPluginStatus.Enabled =>
                    (ControlCenterLocalization.Current.Get("capability.enabled"), D046HermesCapabilityCatalogStatusTone.Good),
                HermesCapabilityPluginStatus.Disabled =>
                    (ControlCenterLocalization.Current.Get("capability.disabled"), D046HermesCapabilityCatalogStatusTone.Muted),
                HermesCapabilityPluginStatus.NotEnabled =>
                    (ControlCenterLocalization.Current.Get("capability.notEnabled"), D046HermesCapabilityCatalogStatusTone.Muted),
                _ => (ControlCenterLocalization.Current.Get("capability.stateUnknown"), D046HermesCapabilityCatalogStatusTone.Warning)
            };
            var version = string.IsNullOrEmpty(plugin.Version)
                ? ControlCenterLocalization.Current.Get("capability.noVersion")
                : ControlCenterLocalization.Current.Get("capability.versionPrefix") + plugin.Version;
            return Entry(
                plugin.Name,
                plugin.Key,
                (string.IsNullOrEmpty(plugin.Description)
                    ? string.Equals(plugin.Source, "bundled", StringComparison.Ordinal)
                        ? ControlCenterLocalization.Current.Get("capability.bundledNoDescription")
                        : ControlCenterLocalization.Current.Get("capability.externalNoDescription")
                    : plugin.Description + "　")
                    + ControlCenterLocalization.Current.Format("capability.pluginSource", version, plugin.Source)
                    + (plugin.Portable ? ControlCenterLocalization.Current.Get("capability.portable") : ControlCenterLocalization.Current.Get("capability.notPortable")),
                status,
                ControlCenterLocalization.Current.Get("capability.listedNotRunnable"),
                tone);
        });
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.plugins"),
            ControlCenterLocalization.Current.Format("capability.userPlugins", catalog.Plugins.UserCount)
                + ControlCenterLocalization.Current.Format("capability.bundledPlugins", catalog.Plugins.BundledCount)
                + $"{CoverageText(section.Coverage.Operable)}。",
            ControlCenterLocalization.Current.Format("capability.items", catalog.Plugins.Plugins.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel McpSection(
        HermesCapabilityCatalog catalog,
        HermesCapabilitySectionStatus section)
    {
        if (section.State == HermesCapabilitySectionState.Unavailable)
            return UnavailableSection("MCP", ControlCenterLocalization.Current.Get("capability.serversUnknown"), section);

        var entries = new List<D046HermesCapabilityCatalogEntryViewModel>();
        entries.Add(Entry(
            ControlCenterLocalization.Current.Get("capability.mcpConnection"),
            ControlCenterLocalization.Current.Get("capability.dataVerified"),
            ControlCenterLocalization.Current.Get("capability.mcpDetail"),
            ControlCenterLocalization.Current.Get("capability.attested"),
            ControlCenterLocalization.Current.Get("capability.seeServers"),
            D046HermesCapabilityCatalogStatusTone.Good));

        foreach (var server in catalog.Mcp.Servers)
        {
            var (status, readiness, tone) = McpStatus(server.Status);
            entries.Add(Entry(
                server.Name,
                ControlCenterLocalization.Current.Format("capability.transport", server.Transport),
                ControlCenterLocalization.Current.Format("capability.mcpToolsCount", server.ToolCount),
                status,
                readiness,
                tone));
        }
        return new D046HermesCapabilityCatalogSectionViewModel(
            "MCP",
            ControlCenterLocalization.Current.Get("capability.mcpConnections")
                + $"{CoverageText(section.Coverage.Operable)}。",
            ControlCenterLocalization.Current.Format("capability.serversCount", catalog.Mcp.Servers.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel WarningsSection(
        HermesCapabilityCatalog catalog)
    {
        var entries = catalog.Warnings.Select(warning => Entry(
            WarningName(warning),
            ControlCenterLocalization.Current.Get("capability.warningCode") + warning,
            WarningDetail(warning),
            ControlCenterLocalization.Current.Get("capability.attention"),
            ControlCenterLocalization.Current.Get("capability.statusOnly"),
            D046HermesCapabilityCatalogStatusTone.Warning));
        return new D046HermesCapabilityCatalogSectionViewModel(
            ControlCenterLocalization.Current.Get("capability.warnings"),
            catalog.Warnings.Count == 0
                ? ControlCenterLocalization.Current.Get("capability.noWarnings")
                : ControlCenterLocalization.Current.Get("capability.someMissing"),
            ControlCenterLocalization.Current.Format("capability.items", catalog.Warnings.Count),
            entries);
    }

    private static D046HermesCapabilityCatalogSectionViewModel
        UnavailableSection(
            string title,
            string unknownCountText,
            HermesCapabilitySectionStatus section)
    {
        var noSession = section.Reason
            == HermesCapabilitySectionReason.NoAttestedSession;
        var reason = SectionReasonText(section.Reason);
        var summary = noSession
            ? ControlCenterLocalization.Current.Get("capability.noSession")
            : ControlCenterLocalization.Current.Format("capability.cannotVerify", reason);
        var entries = new[]
        {
            Entry(
                title + ControlCenterLocalization.Current.Get("capability.stateSuffix"),
                $"state={section.StateToken} · reason={section.ReasonToken}",
                noSession
                    ? ControlCenterLocalization.Current.Get("capability.profileNoSession")
                    : ControlCenterLocalization.Current.Get("capability.noVerifiedData"),
                noSession ? ControlCenterLocalization.Current.Get("capability.unknown") : ControlCenterLocalization.Current.Get("capability.unreadable"),
                reason,
                D046HermesCapabilityCatalogStatusTone.Warning)
        };
        return new D046HermesCapabilityCatalogSectionViewModel(
            title,
            summary,
            unknownCountText,
            entries,
            initiallyExpanded: true,
            summaryTone: D046HermesCapabilityCatalogStatusTone.Warning);
    }

    private static string CoverageText(HermesCapabilityCoverage coverage)
        => coverage.State switch
        {
            HermesCapabilityCoverageState.Attested =>
                ControlCenterLocalization.Current.Format("capability.coverageAttested", coverage.Count?.ToString() ?? string.Empty),
            HermesCapabilityCoverageState.Partial =>
                ControlCenterLocalization.Current.Format("capability.coveragePartial", coverage.Count?.ToString() ?? string.Empty),
            HermesCapabilityCoverageState.NotApplicable => ControlCenterLocalization.Current.Get("capability.notApplicable"),
            _ => ControlCenterLocalization.Current.Get("capability.unknown")
        };

    private static string CoverageCountText(HermesCapabilityCoverage coverage)
        => coverage.State switch
        {
            HermesCapabilityCoverageState.Attested => coverage.Count!.Value.ToString(),
            HermesCapabilityCoverageState.Partial => ControlCenterLocalization.Current.Format("capability.atLeast", coverage.Count?.ToString() ?? string.Empty),
            HermesCapabilityCoverageState.NotApplicable => ControlCenterLocalization.Current.Get("capability.notApplicable"),
            _ => ControlCenterLocalization.Current.Get("capability.unknown")
        };

    private static string SkillExcludedCountText(
        HermesCapabilitySkillCatalog skills,
        HermesCapabilitySectionStatus section)
    {
        if (section.Coverage.Installed.State
                == HermesCapabilityCoverageState.Attested
            && section.Coverage.Offered.State
                == HermesCapabilityCoverageState.Attested)
        {
            return skills.ExcludedCount.ToString();
        }
        return skills.ExcludedCount == 0
            ? ControlCenterLocalization.Current.Get("capability.unknown")
            : ControlCenterLocalization.Current.Format("capability.atLeast", skills.ExcludedCount);
    }

    private static string SectionReasonText(HermesCapabilitySectionReason reason)
        => reason switch
        {
            HermesCapabilitySectionReason.RequestFailed =>
                ControlCenterLocalization.Current.Get("capability.requestFailed"),
            HermesCapabilitySectionReason.InvalidPayload =>
                ControlCenterLocalization.Current.Get("capability.invalidPayload"),
            HermesCapabilitySectionReason.Unsupported =>
                ControlCenterLocalization.Current.Get("capability.unsupported"),
            HermesCapabilitySectionReason.NoAttestedSession =>
                ControlCenterLocalization.Current.Get("capability.unattestedSession"),
            _ => ControlCenterLocalization.Current.Get("capability.reasonUnknown")
        };

    private static string SkillExclusionReasonText(
        HermesCapabilitySkillExclusionReason reason)
        => reason switch
        {
            HermesCapabilitySkillExclusionReason.PlatformIncompatible =>
                ControlCenterLocalization.Current.Get("capability.platformIncompatible"),
            HermesCapabilitySkillExclusionReason.EnvironmentIncompatible =>
                ControlCenterLocalization.Current.Get("capability.environmentIncompatible"),
            HermesCapabilitySkillExclusionReason.Disabled => ControlCenterLocalization.Current.Get("capability.disabled"),
            HermesCapabilitySkillExclusionReason.Other => ControlCenterLocalization.Current.Get("capability.otherExcluded"),
            _ => ControlCenterLocalization.Current.Get("capability.notExcluded")
        };

    private static string QuickSkillOriginText(string origin)
        => origin switch
        {
            "hub" => "Hub",
            "bundled" => ControlCenterLocalization.Current.Get("capability.bundled"),
            "local" => ControlCenterLocalization.Current.Get("capability.local"),
            _ => ControlCenterLocalization.Current.Get("capability.unknown")
        };

    private static string WarningName(string warning)
        => warning switch
        {
            HermesCapabilityCatalogContract.McpSnapshotUnavailableWarning =>
                ControlCenterLocalization.Current.Get("capability.mcpWarning"),
            HermesCapabilityCatalogContract.CommandsDiscoveryPartialWarning =>
                ControlCenterLocalization.Current.Get("capability.commandsPartial"),
            HermesCapabilityCatalogContract
                .NativeToolsInstalledInventoryUnavailableWarning =>
                ControlCenterLocalization.Current.Get("capability.installedInventory"),
            _ => ControlCenterLocalization.Current.Get("capability.serviceNotice")
        };

    private static string WarningDetail(string warning)
        => warning switch
        {
            HermesCapabilityCatalogContract.McpSnapshotUnavailableWarning =>
                ControlCenterLocalization.Current.Get("capability.mcpNoSession"),
            HermesCapabilityCatalogContract.CommandsDiscoveryPartialWarning =>
                ControlCenterLocalization.Current.Get("capability.commandsPartialDetail"),
            HermesCapabilityCatalogContract
                .NativeToolsInstalledInventoryUnavailableWarning =>
                ControlCenterLocalization.Current.Get("capability.installedInventoryDetail"),
            _ => ControlCenterLocalization.Current.Get("capability.serviceNoticeDetail")
        };

    private static (string Status, string Readiness,
        D046HermesCapabilityCatalogStatusTone Tone) ToolsetStatus(
            HermesCapabilityToolset toolset)
    {
        var readiness = "readiness=" + toolset.ReadinessToken
            + " · configured="
            + toolset.Configured.ToString().ToLowerInvariant();
        if (!toolset.Enabled)
        {
            return (
                ControlCenterLocalization.Current.Get("capability.notEnabled"),
                readiness,
                D046HermesCapabilityCatalogStatusTone.Muted);
        }
        if (toolset.Configured
            && toolset.Readiness == HermesNativeToolsetReadiness.Ready)
        {
            return (
                ControlCenterLocalization.Current.Get("capability.enabledReady"),
                readiness,
                D046HermesCapabilityCatalogStatusTone.Good);
        }
        return (
            ControlCenterLocalization.Current.Get("capability.enabledNotReady"),
            readiness,
            D046HermesCapabilityCatalogStatusTone.Warning);
    }

    private static (string Status, string Readiness,
        D046HermesCapabilityCatalogStatusTone Tone) McpStatus(
            HermesCapabilityMcpServerStatus status)
        => status switch
        {
            HermesCapabilityMcpServerStatus.Connected => (
                ControlCenterLocalization.Current.Get("capability.connected"),
                ControlCenterLocalization.Current.Get("capability.connectionVerified"),
                D046HermesCapabilityCatalogStatusTone.Good),
            HermesCapabilityMcpServerStatus.Disabled => (
                ControlCenterLocalization.Current.Get("capability.disabled"),
                ControlCenterLocalization.Current.Get("capability.currentlyDisabled"),
                D046HermesCapabilityCatalogStatusTone.Muted),
            HermesCapabilityMcpServerStatus.Connecting => (
                ControlCenterLocalization.Current.Get("capability.connecting"),
                ControlCenterLocalization.Current.Get("capability.notReady"),
                D046HermesCapabilityCatalogStatusTone.Accent),
            HermesCapabilityMcpServerStatus.Failed => (
                ControlCenterLocalization.Current.Get("capability.failed"),
                ControlCenterLocalization.Current.Get("capability.notReady"),
                D046HermesCapabilityCatalogStatusTone.Danger),
            _ => (
                ControlCenterLocalization.Current.Get("capability.configured"),
                ControlCenterLocalization.Current.Get("capability.connectionUnconfirmed"),
                D046HermesCapabilityCatalogStatusTone.Muted)
        };

    private static string ProviderDetail(
        HermesCapabilityModelProvider provider,
        HermesCapabilityModelCatalog catalog)
    {
        var labels = new List<string>();
        if (provider.Current)
            labels.Add(ControlCenterLocalization.Current.Get("capability.currentProvider"));
        if (provider.UserDefined)
            labels.Add(ControlCenterLocalization.Current.Get("capability.userDefined"));
        if (string.Equals(
                catalog.DefaultProvider,
                provider.Id,
                StringComparison.Ordinal))
        {
            labels.Add(ControlCenterLocalization.Current.Get("capability.hasDefault"));
        }
        labels.Add(ControlCenterLocalization.Current.Format("capability.modelCount", provider.Models.Count));
        return string.Join(" · ", labels);
    }

    private static string ModelDetail(
        HermesCapabilityModel model,
        HermesCapabilityModelProvider provider,
        HermesCapabilityModelCatalog catalog)
    {
        var labels = new List<string>();
        if (model.Featured)
            labels.Add(ControlCenterLocalization.Current.Get("capability.featured"));
        if (!model.CapabilityKnown)
        {
            labels.Add(ControlCenterLocalization.Current.Get("capability.capabilityUnknown"));
        }
        else
        {
            if (model.Fast)
                labels.Add(ControlCenterLocalization.Current.Get("capability.fast"));
            if (model.Reasoning)
                labels.Add(ControlCenterLocalization.Current.Get("capability.reasoning"));
        }
        if (string.Equals(catalog.DefaultProvider, provider.Id, StringComparison.Ordinal)
            && string.Equals(catalog.DefaultModel, model.Id, StringComparison.Ordinal))
        {
            labels.Add(ControlCenterLocalization.Current.Get("capability.currentDefault"));
        }
        return labels.Count == 0 ? ControlCenterLocalization.Current.Get("capability.noModelFlags") : string.Join(" · ", labels);
    }

    private static D046HermesCapabilityCatalogEntryViewModel Entry(
        string name,
        string identity,
        string detail,
        string status,
        string readiness,
        D046HermesCapabilityCatalogStatusTone tone)
        => new(name, identity, detail, status, readiness, tone);
}
