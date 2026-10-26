using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal readonly record struct HermesCapabilityCatalogNativeRead
{
    private HermesCapabilityCatalogNativeRead(
        JsonElement? payload,
        HermesCapabilitySectionReason failureReason)
    {
        if (payload.HasValue
            == (failureReason != HermesCapabilitySectionReason.None))
        {
            throw new ArgumentException(
                "Hermes capability read state was inconsistent.",
                nameof(failureReason));
        }
        Payload = payload;
        FailureReason = failureReason;
    }

    internal JsonElement? Payload { get; }
    internal HermesCapabilitySectionReason FailureReason { get; }

    internal static HermesCapabilityCatalogNativeRead Available(
        JsonElement payload)
        => new(payload.Clone(), HermesCapabilitySectionReason.None);

    internal static HermesCapabilityCatalogNativeRead Unavailable(
        HermesCapabilitySectionReason reason)
    {
        if (reason == HermesCapabilitySectionReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        return new(null, reason);
    }
}

internal static class HermesCapabilityCatalogAssembler
{
    internal static HermesCommandCatalogSnapshot AssembleCommands(JsonElement nativePayload)
    {
        var commands = ProjectCommands(nativePayload).Value;
        var snapshot = new HermesCommandCatalogSnapshot(new HermesCapabilityCatalogIdentity(
                LilithRuntimePipeServer.RuntimeVersion,
                LilithRuntimePipeServer.PinnedHermesVersion,
                LilithRuntimeOptions.PinnedHermesCommit), commands);
        return HermesCommandCatalogContract.Decode(JsonSerializer.SerializeToElement(snapshot));
    }

    private const int MaximumSkillCategories = 256;
    private const int MaximumNativeWarningUtf8Bytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private sealed record ProjectedSection<T>(
        T Value,
        HermesCapabilitySectionStatus Status);

    private sealed record InstalledToolsetProjection(
        IReadOnlyList<HermesCapabilityInstalledToolset> Rows,
        bool Attested);

    internal static HermesCapabilityCatalog Assemble(
        HermesCapabilityCatalogNativeRead commandsRead,
        HermesCapabilityCatalogNativeRead installedToolsRead,
        HermesCapabilityCatalogNativeRead nativeToolsRead,
        bool nativeToolsNewSessionRequired,
        bool nativeToolsLoadedSnapshotAttested,
        IReadOnlyCollection<string> nativeToolsLoadedTools,
        HermesCapabilityCatalogNativeRead modelOptionsRead,
        HermesCapabilityCatalogNativeRead skillsRead,
        HermesCapabilityCatalogNativeRead pluginsRead,
        HermesCapabilityMcpCatalog mcp)
    {
        ArgumentNullException.ThrowIfNull(mcp);

        var commands = Project(
            HermesCapabilityCatalogContract.CommandsSectionId,
            commandsRead,
            ProjectCommands,
            static () => new HermesCapabilityCommandCatalog(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<HermesCapabilityCommandDetail>(),
                Array.Empty<HermesCapabilityQuickSkillDetail>(),
                Array.Empty<HermesCapabilityCommandCategory>(),
                Array.Empty<HermesCapabilityCommandAlias>(),
                0));
        var installedTools = ProjectInstalledToolsets(installedToolsRead);
        var nativeTools = Project(
            HermesCapabilityCatalogContract.NativeToolsSectionId,
            nativeToolsRead,
            payload => ProjectNativeTools(
                payload,
                nativeToolsNewSessionRequired,
                installedTools.Rows,
                installedTools.Attested,
                nativeToolsLoadedSnapshotAttested,
                nativeToolsLoadedTools),
            () => new HermesCapabilityNativeToolsCatalog(
                HermesNativeApprovalMode.Off,
                Array.Empty<string>(),
                Array.Empty<HermesCapabilityInstalledToolset>(),
                Array.Empty<HermesCapabilityToolset>(),
                nativeToolsNewSessionRequired,
                loadedSnapshotAttested: false,
                Array.Empty<string>()));
        var models = Project(
            HermesCapabilityCatalogContract.ModelsSectionId,
            modelOptionsRead,
            ProjectModels,
            static () => new HermesCapabilityModelCatalog(
                string.Empty,
                string.Empty,
                Array.Empty<HermesCapabilityModelProvider>()));
        var skills = Project(
            HermesCapabilityCatalogContract.SkillsSectionId,
            skillsRead,
            ProjectSkills,
            static () => new HermesCapabilitySkillCatalog(
                Array.Empty<HermesCapabilitySkill>(),
                0,
                0));
        var plugins = Project(
            HermesCapabilityCatalogContract.PluginsSectionId,
            pluginsRead,
            ProjectPlugins,
            static () => new HermesCapabilityPluginCatalog(
                Array.Empty<HermesCapabilityPlugin>(),
                0,
                0));
        var mcpStatus = ProjectMcpStatus(mcp);

        var warnings = new List<string>();
        if (mcp.State == HermesCapabilityMcpSnapshotState.Unavailable)
        {
            warnings.Add(
                HermesCapabilityCatalogContract.McpSnapshotUnavailableWarning);
        }
        if (commands.Status.Coverage.Offered.State
            == HermesCapabilityCoverageState.Partial)
        {
            warnings.Add(
                HermesCapabilityCatalogContract.CommandsDiscoveryPartialWarning);
        }
        if (nativeTools.Status.State == HermesCapabilitySectionState.Available
            && !installedTools.Attested)
        {
            warnings.Add(
                HermesCapabilityCatalogContract
                    .NativeToolsInstalledInventoryUnavailableWarning);
        }
        var catalog = new HermesCapabilityCatalog(
            new HermesCapabilityCatalogIdentity(
                LilithRuntimePipeServer.RuntimeVersion,
                LilithRuntimePipeServer.PinnedHermesVersion,
                LilithRuntimeOptions.PinnedHermesCommit),
            LilithRuntimeRequestRouter.PublishedMethods,
            new[]
            {
                commands.Status,
                nativeTools.Status,
                models.Status,
                skills.Status,
                plugins.Status,
                mcpStatus
            },
            commands.Value,
            nativeTools.Value,
            models.Value,
            skills.Value,
            plugins.Value,
            mcp,
            warnings);

        // Upstream payloads are allowlisted projections, while the public
        // Runtime response remains an exact, closed v3 contract.
        return HermesCapabilityCatalogContract.Decode(
            JsonSerializer.SerializeToElement(catalog));
    }

    private static ProjectedSection<T> Project<T>(
        string sectionId,
        HermesCapabilityCatalogNativeRead read,
        Func<JsonElement, (T Value, HermesCapabilitySectionCoverage Coverage)> projector,
        Func<T> empty)
    {
        if (!read.Payload.HasValue)
        {
            return new ProjectedSection<T>(
                empty(),
                UnavailableSection(sectionId, read.FailureReason));
        }

        try
        {
            var projected = projector(read.Payload.Value);
            return new ProjectedSection<T>(
                projected.Value,
                new HermesCapabilitySectionStatus(
                    sectionId,
                    HermesCapabilitySectionState.Available,
                    HermesCapabilitySectionReason.None,
                    projected.Coverage));
        }
        catch (Exception exception) when (IsRejectedNativePayload(exception))
        {
            return new ProjectedSection<T>(
                empty(),
                UnavailableSection(
                    sectionId,
                    HermesCapabilitySectionReason.InvalidPayload));
        }
    }

    private static bool IsRejectedNativePayload(Exception exception)
        => exception is InvalidDataException
            or ArgumentException
            or InvalidOperationException
            or KeyNotFoundException
            or JsonException
            or OverflowException;

    private static HermesCapabilitySectionStatus UnavailableSection(
        string id,
        HermesCapabilitySectionReason reason)
        => new(
            id,
            HermesCapabilitySectionState.Unavailable,
            reason,
            new HermesCapabilitySectionCoverage(
                Unknown(),
                Unknown(),
                Unknown(),
                Unknown()));

    private static HermesCapabilitySectionStatus ProjectMcpStatus(
        HermesCapabilityMcpCatalog mcp)
    {
        if (mcp.State == HermesCapabilityMcpSnapshotState.Unavailable)
        {
            return UnavailableSection(
                HermesCapabilityCatalogContract.McpSectionId,
                HermesCapabilitySectionReason.NoAttestedSession);
        }
        return new HermesCapabilitySectionStatus(
            HermesCapabilityCatalogContract.McpSectionId,
            HermesCapabilitySectionState.Available,
            HermesCapabilitySectionReason.None,
            new HermesCapabilitySectionCoverage(
                NotApplicable(),
                Attested(mcp.Servers.Count),
                Attested(mcp.Servers.Count),
                Attested(mcp.Servers.Count(server => server.Connected))));
    }

    private static HermesCapabilityCoverage Attested(int count)
        => new(HermesCapabilityCoverageState.Attested, count);

    private static HermesCapabilityCoverage Partial(int count)
        => new(HermesCapabilityCoverageState.Partial, count);

    private static HermesCapabilityCoverage Unknown()
        => new(HermesCapabilityCoverageState.Unknown, null);

    private static HermesCapabilityCoverage NotApplicable()
        => new(HermesCapabilityCoverageState.NotApplicable, null);

    private static (
        HermesCapabilityCommandCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectCommands(
        JsonElement payload)
    {
        RequireObject(payload, "commands");
        var pairs = RequireArray(
            RequiredProperty(payload, "pairs"),
            HermesCapabilityCatalogContract.MaximumCommands,
            allowEmpty: false,
            "command pairs");
        var commandPairs = pairs.EnumerateArray()
            .Select(CommandPair)
            .ToArray();
        var commands = commandPairs.Select(static pair => pair.Name).ToArray();
        if (commands.Distinct(StringComparer.Ordinal).Count() != commands.Length)
            throw new InvalidDataException("Hermes returned duplicate commands.");

        var quickSkillsValue = RequiredProperty(payload, "skills");
        RequireObject(quickSkillsValue, "quick Skills");
        var quickSkills = new List<string>();
        var quickSkillDetails = new List<HermesCapabilityQuickSkillDetail>();
        foreach (var skill in quickSkillsValue.EnumerateObject())
        {
            if (quickSkills.Count >= HermesCapabilityCatalogContract.MaximumSkills)
                throw new InvalidDataException("Hermes returned too many Quick Skills.");
            RequireObject(skill.Value, "Quick Skill metadata");
            var name = NativeText(
                skill.Name,
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: false);
            quickSkills.Add(name);
            quickSkillDetails.Add(new HermesCapabilityQuickSkillDetail(
                name,
                RequiredInteger(
                    skill.Value,
                    "usage",
                    0,
                    HermesCapabilityCatalogContract.MaximumCoverageCount),
                RequiredText(
                    skill.Value,
                    "origin",
                    HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes)));
        }

        var categoriesValue = RequireArray(
            RequiredProperty(payload, "categories"),
            HermesCapabilityCatalogContract.MaximumCommandCategories,
            allowEmpty: true,
            "command categories");
        var userCommands = new HashSet<string>(StringComparer.Ordinal);
        var categories = categoriesValue.EnumerateArray()
            .Select(category =>
            {
                RequireObject(category, "command category");
                var categoryPairs = RequireArray(
                    RequiredProperty(category, "pairs"),
                    HermesCapabilityCatalogContract.MaximumCommands,
                    allowEmpty: true,
                    "category pairs");
                var categoryName = RequiredText(
                    category,
                    "name",
                    HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
                var categoryCommands = categoryPairs.EnumerateArray()
                    .Select(pair => CommandPair(pair).Name)
                    .ToArray();
                if (string.Equals(
                        categoryName,
                        "User commands",
                        StringComparison.Ordinal))
                {
                    userCommands.UnionWith(categoryCommands);
                }
                return new HermesCapabilityCommandCategory(
                    categoryName,
                    categoryCommands);
            })
            .ToArray();

        var canon = RequiredProperty(payload, "canon");
        RequireObject(canon, "command aliases");
        var aliases = new List<HermesCapabilityCommandAlias>();
        foreach (var alias in canon.EnumerateObject())
        {
            if (aliases.Count >= HermesCapabilityCatalogContract.MaximumCommands * 4)
                throw new InvalidDataException("Hermes returned too many aliases.");
            var canonical = RequiredStringValue(
                alias.Value,
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: false);
            if (string.Equals(alias.Name, canonical, StringComparison.OrdinalIgnoreCase))
                continue;
            aliases.Add(new HermesCapabilityCommandAlias(alias.Name, canonical));
        }

        var skillCount = RequiredInteger(
            payload,
            "skill_count",
            0,
            HermesCapabilityCatalogContract.MaximumSkills);
        var commandSet = commands.ToHashSet(StringComparer.Ordinal);
        var quickSkillSet = quickSkills.ToHashSet(StringComparer.Ordinal);
        var subcommands = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.Ordinal);
        var subPayload = RequiredProperty(payload, "sub");
        RequireObject(subPayload, "command subcommands");
        foreach (var sub in subPayload.EnumerateObject())
        {
            if (!commandSet.Contains(sub.Name)
                || quickSkillSet.Contains(sub.Name))
            {
                continue;
            }
            subcommands.Add(
                sub.Name,
                ReadStrings(
                    RequireArray(
                        sub.Value,
                        HermesCapabilityCatalogContract
                            .MaximumSubcommandsPerCommand,
                        allowEmpty: true,
                        "command subcommands"),
                    HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes));
        }
        var details = commandPairs.Select(pair =>
            new HermesCapabilityCommandDetail(
                pair.Name,
                quickSkillSet.Contains(pair.Name)
                    || userCommands.Contains(pair.Name)
                    ? string.Empty
                    : NativeText(
                        pair.Description,
                        HermesCapabilityCatalogContract
                            .MaximumDisplayTextUtf8Bytes,
                        allowEmpty: true),
                subcommands.TryGetValue(pair.Name, out var values)
                    ? values
                    : Array.Empty<string>()))
            .ToArray();
        var discoveryPartial = HasOpaqueWarning(
            RequiredProperty(payload, "warning"));
        var result = new HermesCapabilityCommandCatalog(
            commands,
            quickSkills,
            details,
            quickSkillDetails,
            categories,
            aliases,
            skillCount);
        return (
            result,
            new HermesCapabilitySectionCoverage(
                NotApplicable(),
                discoveryPartial
                    ? Partial(result.Commands.Count)
                    : Attested(result.Commands.Count),
                NotApplicable(),
                Unknown()));
    }

    private sealed record NativeCommandPair(string Name, string Description);

    private static NativeCommandPair CommandPair(JsonElement pair)
    {
        var values = RequireArray(pair, 8, allowEmpty: false, "command pair");
        if (values.GetArrayLength() != 2)
            throw new InvalidDataException("Hermes returned an invalid command pair.");
        var row = values.EnumerateArray().ToArray();
        return new NativeCommandPair(
            RequiredStringValue(
                row[0],
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: false),
            OpaqueNativeText(
                row[1],
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes));
    }

    private static (
        HermesCapabilityNativeToolsCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectNativeTools(
        JsonElement payload,
        bool newSessionRequired,
        IReadOnlyList<HermesCapabilityInstalledToolset> installedToolsets,
        bool installedToolsetsAttested,
        bool loadedSnapshotAttested,
        IReadOnlyCollection<string> loadedTools)
    {
        RequireObject(payload, "native tools");
        if (!string.Equals(
                RequiredText(
                    payload,
                    "platform",
                    HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes),
                "cli",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Hermes returned an unknown tool platform.");
        }
        var approvalMode = RequiredText(
            payload,
            "approval_mode",
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes) switch
        {
            "manual" => HermesNativeApprovalMode.Manual,
            "smart" => HermesNativeApprovalMode.Smart,
            "off" => HermesNativeApprovalMode.Off,
            _ => throw new InvalidDataException(
                "Hermes returned an unknown approval mode.")
        };
        var enabled = ReadStrings(
            RequireArray(
                RequiredProperty(payload, "enabled_toolsets"),
                HermesNativeToolsContract.MaximumToolsets,
                allowEmpty: true,
                "enabled toolsets"),
            HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes);
        var toolsetsPayload = RequireArray(
            RequiredProperty(payload, "toolsets"),
            HermesNativeToolsContract.MaximumToolsets,
            allowEmpty: false,
            "toolsets");
        var toolsets = toolsetsPayload.EnumerateArray()
            .Select(toolset =>
            {
                RequireObject(toolset, "toolset");
                var readiness = RequiredText(
                    toolset,
                    "readiness",
                    HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes) switch
                {
                    "ready" => HermesNativeToolsetReadiness.Ready,
                    "needs_setup" => HermesNativeToolsetReadiness.NeedsSetup,
                    "needs_auth" => HermesNativeToolsetReadiness.NeedsAuth,
                    "needs_keys" => HermesNativeToolsetReadiness.NeedsKeys,
                    _ => throw new InvalidDataException(
                        "Hermes returned unknown tool readiness.")
                };
                return new HermesCapabilityToolset(
                    RequiredText(
                        toolset,
                        "name",
                        HermesNativeToolsContract.MaximumToolsetNameUtf8Bytes),
                    RequiredText(
                        toolset,
                        "label",
                        HermesNativeToolsContract.MaximumToolsetLabelUtf8Bytes),
                    NormalizedNativeDescription(
                        RequiredProperty(toolset, "description"),
                        HermesNativeToolsContract
                            .MaximumToolsetDescriptionUtf8Bytes),
                    RequiredBoolean(toolset, "enabled"),
                    RequiredBoolean(toolset, "configured"),
                    readiness,
                    ReadStrings(
                        RequireArray(
                            RequiredProperty(toolset, "tools"),
                            HermesNativeToolsContract.MaximumToolsPerToolset,
                            allowEmpty: true,
                            "tool names"),
                        HermesNativeToolsContract.MaximumToolNameUtf8Bytes));
            })
            .ToArray();
        if (RequiredBoolean(payload, "new_session_required"))
        {
            throw new InvalidDataException(
                "Hermes tools.catalog unexpectedly asserted a pending mutation.");
        }
        var result = new HermesCapabilityNativeToolsCatalog(
            approvalMode,
            enabled,
            installedToolsets,
            toolsets,
            newSessionRequired,
            loadedSnapshotAttested,
            loadedSnapshotAttested
                ? loadedTools.Intersect(
                    toolsets.SelectMany(toolset => toolset.Tools),
                    StringComparer.Ordinal)
                : Array.Empty<string>());
        var installed = result.InstalledToolsets
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var offered = result.Toolsets
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var configured = result.Toolsets
            .Where(toolset => toolset.Configured)
            .SelectMany(toolset => toolset.Tools)
            .Distinct(StringComparer.Ordinal)
            .Count();
        return (
            result,
            new HermesCapabilitySectionCoverage(
                installedToolsetsAttested
                    ? Attested(installed)
                    : Unknown(),
                Attested(offered),
                Attested(configured),
                result.LoadedSnapshotAttested
                    ? Attested(result.LoadedTools.Count)
                    : Unknown()));
    }

    private static InstalledToolsetProjection ProjectInstalledToolsets(
        HermesCapabilityCatalogNativeRead read)
    {
        if (!read.Payload.HasValue)
        {
            return new InstalledToolsetProjection(
                Array.Empty<HermesCapabilityInstalledToolset>(),
                Attested: false);
        }
        try
        {
            var payload = read.Payload.Value;
            RequireObject(payload, "installed tools");
            var toolsets = RequireArray(
                    RequiredProperty(payload, "toolsets"),
                    HermesNativeToolsContract.MaximumToolsets,
                    allowEmpty: false,
                    "installed toolsets")
                .EnumerateArray()
                .Select(toolset =>
                {
                    RequireObject(toolset, "installed toolset");
                    _ = RequiredBoolean(toolset, "enabled");
                    var tools = ReadStrings(
                        RequireArray(
                            RequiredProperty(toolset, "tools"),
                            HermesNativeToolsContract.MaximumToolsPerToolset,
                            allowEmpty: true,
                            "installed tool names"),
                        HermesNativeToolsContract.MaximumToolNameUtf8Bytes);
                    if (RequiredInteger(
                            toolset,
                            "tool_count",
                            0,
                            HermesNativeToolsContract.MaximumToolsPerToolset)
                        != tools.Count)
                    {
                        throw new InvalidDataException(
                            "Hermes installed tool count disagreed.");
                    }
                    return new HermesCapabilityInstalledToolset(
                        RequiredText(
                            toolset,
                            "name",
                            HermesNativeToolsContract
                                .MaximumToolsetNameUtf8Bytes),
                        NormalizedNativeDescription(
                            RequiredProperty(toolset, "description"),
                            HermesNativeToolsContract
                                .MaximumToolsetDescriptionUtf8Bytes),
                        tools);
                })
                .ToArray();
            return new InstalledToolsetProjection(toolsets, Attested: true);
        }
        catch (Exception exception) when (IsRejectedNativePayload(exception))
        {
            return new InstalledToolsetProjection(
                Array.Empty<HermesCapabilityInstalledToolset>(),
                Attested: false);
        }
    }

    private static (
        HermesCapabilityModelCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectModels(
        JsonElement payload)
    {
        RequireObject(payload, "models");
        var providersPayload = RequireArray(
            RequiredProperty(payload, "providers"),
            HermesCapabilityCatalogContract.MaximumModelProviders,
            allowEmpty: false,
            "model providers");
        var providers = providersPayload.EnumerateArray()
            .Select(ProjectModelProvider)
            .ToArray();
        var result = new HermesCapabilityModelCatalog(
            RequiredText(
                payload,
                "provider",
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: true),
            RequiredText(
                payload,
                "model",
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: true),
            providers);
        return (
            result,
            new HermesCapabilitySectionCoverage(
                NotApplicable(),
                Attested(providers.Sum(provider => provider.Models.Count)),
                Attested(result.DefaultModel.Length == 0 ? 0 : 1),
                Unknown()));
    }

    private static HermesCapabilityModelProvider ProjectModelProvider(
        JsonElement provider)
    {
        RequireObject(provider, "model provider");
        var featured = StringSet(
            RequireArray(
                RequiredProperty(provider, "featured_models"),
                HermesCapabilityCatalogContract.MaximumModelsPerProvider,
                allowEmpty: true,
                "featured models"));
        var unavailable = TryProperty(
                provider,
                "unavailable_models",
                out var unavailableModels)
            ? StringSet(RequireArray(
                unavailableModels,
                HermesCapabilityCatalogContract.MaximumModelsPerProvider,
                allowEmpty: true,
                "unavailable models"))
            : new HashSet<string>(StringComparer.Ordinal);
        var capabilities = CapabilityRows(
            RequiredProperty(provider, "capabilities"));
        var modelsPayload = RequireArray(
            RequiredProperty(provider, "models"),
            HermesCapabilityCatalogContract.MaximumModelsPerProvider,
            allowEmpty: true,
            "models");
        var models = modelsPayload.EnumerateArray()
            .Select(modelValue =>
            {
                var model = RequiredStringValue(
                    modelValue,
                    HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                    allowEmpty: false);
                var known = capabilities.TryGetValue(model, out var capability);
                return new HermesCapabilityModel(
                    model,
                    available: !unavailable.Contains(model),
                    featured: featured.Contains(model),
                    capabilityKnown: known,
                    fast: known && capability.Fast,
                    reasoning: known && capability.Reasoning);
            })
            .ToArray();
        return new HermesCapabilityModelProvider(
            RequiredText(
                provider,
                "slug",
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes),
            RequiredText(
                provider,
                "name",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes),
            RequiredBoolean(provider, "is_current"),
            RequiredBoolean(provider, "is_user_defined"),
            RequiredBoolean(provider, "authenticated"),
            models);
    }

    private static Dictionary<string, (bool Fast, bool Reasoning)>
        CapabilityRows(JsonElement capabilities)
    {
        RequireObject(capabilities, "model capabilities");
        var rows = new Dictionary<string, (bool Fast, bool Reasoning)>(
            StringComparer.Ordinal);
        foreach (var capability in capabilities.EnumerateObject())
        {
            if (rows.Count >= HermesCapabilityCatalogContract.MaximumModelsPerProvider)
                throw new InvalidDataException("Hermes returned too many capabilities.");
            RequireObject(capability.Value, "model capability");
            if (!rows.TryAdd(
                    NativeText(
                        capability.Name,
                        HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                        allowEmpty: false),
                    (
                        RequiredBoolean(capability.Value, "fast"),
                        RequiredBoolean(capability.Value, "reasoning"))))
            {
                throw new InvalidDataException(
                    "Hermes returned duplicate model capability rows.");
            }
        }
        return rows;
    }

    private static HashSet<string> StringSet(JsonElement values)
        => values.EnumerateArray()
            .Select(value => RequiredStringValue(
                value,
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes,
                allowEmpty: false))
            .ToHashSet(StringComparer.Ordinal);

    private static (
        HermesCapabilitySkillCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectSkills(
        JsonElement payload)
    {
        RequireObject(payload, "Skills");
        var offered = ReadOfferedSkills(RequiredProperty(payload, "skills"));
        var hasInstalled = TryProperty(payload, "installed", out var installedValue);
        var hasExcluded = TryProperty(payload, "excluded", out var excludedValue);
        var hasCounts = TryProperty(payload, "counts", out var countsValue);
        if (hasInstalled || hasExcluded || hasCounts)
        {
            if (!(hasInstalled && hasExcluded && hasCounts))
            {
                throw new InvalidDataException(
                    "Hermes returned a partial Skill inventory extension.");
            }
            return ProjectSkillInventory(
                offered,
                installedValue,
                excludedValue,
                countsValue);
        }

        var knownOffered = offered.Select(row => new HermesCapabilitySkill(
                row.Key,
                row.Value,
                offered: true,
                HermesCapabilitySkillExclusionReason.None))
            .ToArray();
        return (
            new HermesCapabilitySkillCatalog(
                knownOffered,
                knownOffered.Length,
                0),
            new HermesCapabilitySectionCoverage(
                Partial(knownOffered.Length),
                Attested(knownOffered.Length),
                Unknown(),
                Unknown()));
    }

    private static (
        HermesCapabilitySkillCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectSkillInventory(
        IReadOnlyDictionary<string, string> offered,
        JsonElement installedPayload,
        JsonElement excludedPayload,
        JsonElement countsPayload)
    {
        var installedRows = RequireArray(
            installedPayload,
            HermesCapabilityCatalogContract.MaximumSkills,
            allowEmpty: true,
            "installed Skills");
        var installed = new List<(string Name, string Category)>();
        var installedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in installedRows.EnumerateArray())
        {
            RequireObject(row, "installed Skill");
            var name = RequiredText(
                row,
                "name",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
            var category = RequiredText(
                row,
                "category",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
            if (!installedNames.Add(name))
                throw new InvalidDataException("Hermes returned duplicate Skills.");
            installed.Add((name, category));
        }

        var excludedRows = RequireArray(
            excludedPayload,
            HermesCapabilityCatalogContract.MaximumSkills,
            allowEmpty: true,
            "excluded Skills");
        var excluded = new Dictionary<
            string,
            (string Category, HermesCapabilitySkillExclusionReason Reason)>(
            StringComparer.Ordinal);
        foreach (var row in excludedRows.EnumerateArray())
        {
            RequireObject(row, "excluded Skill");
            var name = RequiredText(
                row,
                "name",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
            var category = RequiredText(
                row,
                "category",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
            var reason = RequiredText(
                row,
                "reason",
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes) switch
            {
                "platform_incompatible" =>
                    HermesCapabilitySkillExclusionReason.PlatformIncompatible,
                "environment_incompatible" =>
                    HermesCapabilitySkillExclusionReason.EnvironmentIncompatible,
                "disabled" => HermesCapabilitySkillExclusionReason.Disabled,
                _ => HermesCapabilitySkillExclusionReason.Other
            };
            if (!excluded.TryAdd(name, (category, reason)))
                throw new InvalidDataException("Hermes returned duplicate exclusions.");
        }

        RequireObject(countsPayload, "Skill inventory counts");
        var installedCount = RequiredInteger(
            countsPayload,
            "installed",
            0,
            HermesCapabilityCatalogContract.MaximumSkills);
        var offeredCount = RequiredInteger(
            countsPayload,
            "offered",
            0,
            HermesCapabilityCatalogContract.MaximumSkills);
        var excludedCount = RequiredInteger(
            countsPayload,
            "excluded",
            0,
            HermesCapabilityCatalogContract.MaximumSkills);
        if (installedCount != installed.Count
            || offeredCount != offered.Count
            || excludedCount != excluded.Count
            || installedCount != offeredCount + excludedCount
            || offered.Keys.Any(name => !installedNames.Contains(name))
            || excluded.Keys.Any(name => !installedNames.Contains(name)))
        {
            throw new InvalidDataException("Hermes Skill inventory counts disagreed.");
        }

        var rows = new List<HermesCapabilitySkill>(installed.Count);
        foreach (var skill in installed)
        {
            if (offered.TryGetValue(skill.Name, out var offeredCategory))
            {
                if (!string.Equals(
                        skill.Category,
                        offeredCategory,
                        StringComparison.Ordinal)
                    || excluded.ContainsKey(skill.Name))
                {
                    throw new InvalidDataException(
                        "Hermes Skill offer metadata disagreed.");
                }
                rows.Add(new HermesCapabilitySkill(
                    skill.Name,
                    skill.Category,
                    offered: true,
                    HermesCapabilitySkillExclusionReason.None));
                continue;
            }
            if (!excluded.TryGetValue(skill.Name, out var exclusion)
                || !string.Equals(
                    skill.Category,
                    exclusion.Category,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Hermes Skill exclusion metadata disagreed.");
            }
            rows.Add(new HermesCapabilitySkill(
                skill.Name,
                skill.Category,
                offered: false,
                exclusion.Reason));
        }

        var result = new HermesCapabilitySkillCatalog(
            rows,
            offeredCount,
            excludedCount);
        return (
            result,
            new HermesCapabilitySectionCoverage(
                Attested(installedCount),
                Attested(offeredCount),
                Unknown(),
                Unknown()));
    }

    private static IReadOnlyDictionary<string, string> ReadOfferedSkills(
        JsonElement categories)
    {
        RequireObject(categories, "Skill categories");
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var categoryCount = 0;
        foreach (var category in categories.EnumerateObject())
        {
            categoryCount++;
            if (categoryCount > MaximumSkillCategories)
                throw new InvalidDataException("Hermes returned too many Skill categories.");
            var categoryName = NativeText(
                category.Name,
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
                allowEmpty: false);
            var skills = RequireArray(
                category.Value,
                HermesCapabilityCatalogContract.MaximumSkills,
                allowEmpty: true,
                "offered Skills");
            foreach (var skill in skills.EnumerateArray())
            {
                if (rows.Count >= HermesCapabilityCatalogContract.MaximumSkills)
                    throw new InvalidDataException("Hermes returned too many Skills.");
                var name = RequiredStringValue(
                    skill,
                    HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes,
                    allowEmpty: false);
                if (!rows.TryAdd(name, categoryName))
                    throw new InvalidDataException("Hermes returned duplicate Skills.");
            }
        }
        return rows;
    }

    private static (
        HermesCapabilityPluginCatalog Value,
        HermesCapabilitySectionCoverage Coverage) ProjectPlugins(
        JsonElement payload)
    {
        RequireObject(payload, "Plugins");
        var pluginPayload = RequireArray(
            RequiredProperty(payload, "plugins"),
            HermesCapabilityCatalogContract.MaximumPlugins,
            allowEmpty: true,
            "Plugins");
        var plugins = pluginPayload.EnumerateArray()
            .Select(ProjectPlugin)
            .ToArray();
        var result = new HermesCapabilityPluginCatalog(
            plugins,
            RequiredInteger(
                payload,
                "user_count",
                0,
                HermesCapabilityCatalogContract.MaximumPlugins),
            RequiredInteger(
                payload,
                "bundled_count",
                0,
                HermesCapabilityCatalogContract.MaximumPlugins));
        var enabled = result.Plugins.Count(plugin =>
            plugin.Status == HermesCapabilityPluginStatus.Enabled);
        return (
            result,
            new HermesCapabilitySectionCoverage(
                Attested(result.Plugins.Count),
                Attested(result.Plugins.Count),
                Attested(enabled),
                Unknown()));
    }

    private static HermesCapabilityPlugin ProjectPlugin(JsonElement plugin)
    {
        RequireObject(plugin, "Plugin");
        var sourceValue = RequiredText(
            plugin,
            "source",
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes);
        var source = sourceValue is "bundled" or "user" or "git" or "entrypoint"
            ? sourceValue
            : "other";
        var nativeDescription = NormalizedNativeDescription(
            RequiredProperty(plugin, "description"),
            HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes);
        var status = RequiredText(
            plugin,
            "status",
            HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes) switch
        {
            "enabled" => HermesCapabilityPluginStatus.Enabled,
            "disabled" => HermesCapabilityPluginStatus.Disabled,
            "not enabled" => HermesCapabilityPluginStatus.NotEnabled,
            _ => HermesCapabilityPluginStatus.Unknown
        };
        return new HermesCapabilityPlugin(
            RequiredText(
                plugin,
                "name",
                HermesCapabilityCatalogContract.MaximumDisplayTextUtf8Bytes),
            RequiredText(
                plugin,
                "key",
                HermesCapabilityCatalogContract.MaximumTokenUtf8Bytes),
            RequiredText(
                plugin,
                "version",
                HermesCapabilityCatalogContract.MaximumVersionUtf8Bytes,
                allowEmpty: true),
            string.Equals(source, "bundled", StringComparison.Ordinal)
                ? nativeDescription
                : string.Empty,
            source,
            status,
            RequiredBoolean(plugin, "portable"));
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Hermes returned invalid {label}.");
    }

    private static JsonElement RequireArray(
        JsonElement value,
        int maximumCount,
        bool allowEmpty,
        string label)
    {
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximumCount
            || !allowEmpty && value.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"Hermes returned invalid {label}.");
        }
        return value;
    }

    private static JsonElement RequiredProperty(JsonElement value, string name)
    {
        if (!TryProperty(value, name, out var property))
            throw new InvalidDataException("Hermes omitted a catalog property.");
        return property;
    }

    private static bool TryProperty(
        JsonElement value,
        string name,
        out JsonElement property)
    {
        RequireObject(value, "catalog object");
        property = default;
        var found = false;
        foreach (var candidate in value.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                continue;
            if (found)
                throw new InvalidDataException("Hermes duplicated a catalog property.");
            property = candidate.Value;
            found = true;
        }
        return found;
    }

    private static string RequiredText(
        JsonElement value,
        string name,
        int maximumUtf8Bytes,
        bool allowEmpty = false)
        => RequiredStringValue(
            RequiredProperty(value, name),
            maximumUtf8Bytes,
            allowEmpty);

    private static string RequiredStringValue(
        JsonElement value,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes returned invalid catalog text.");
        return NativeText(value.GetString(), maximumUtf8Bytes, allowEmpty);
    }

    private static string NativeText(
        string? value,
        int maximumUtf8Bytes,
        bool allowEmpty)
    {
        if (value is null
            || !allowEmpty && string.IsNullOrWhiteSpace(value)
            || value.Any(char.IsControl)
            || StrictUtf8.GetByteCount(value) > maximumUtf8Bytes)
        {
            throw new InvalidDataException("Hermes returned invalid catalog text.");
        }
        return value;
    }

    private static string OpaqueNativeText(
        JsonElement value,
        int maximumUtf8Bytes)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes returned invalid catalog text.");
        var text = value.GetString();
        if (text is null
            || StrictUtf8.GetByteCount(text) > maximumUtf8Bytes)
        {
            throw new InvalidDataException("Hermes returned invalid catalog text.");
        }
        return text;
    }

    private static string NormalizedNativeDescription(
        JsonElement value,
        int maximumUtf8Bytes)
    {
        var text = OpaqueNativeText(value, maximumUtf8Bytes);
        if (text.Any(character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException(
                "Hermes returned invalid catalog description.");
        }
        var normalized = string.Join(
            " ",
            text.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
        return NativeText(normalized, maximumUtf8Bytes, allowEmpty: true);
    }

    private static bool HasOpaqueWarning(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Hermes returned an invalid warning.");
        var warning = value.GetString();
        if (warning is null
            || StrictUtf8.GetByteCount(warning) > MaximumNativeWarningUtf8Bytes)
        {
            throw new InvalidDataException("Hermes returned an invalid warning.");
        }
        return warning.Length != 0;
    }

    private static bool RequiredBoolean(JsonElement value, string name)
    {
        var property = RequiredProperty(value, name);
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Hermes returned invalid catalog state.");
        return property.GetBoolean();
    }

    private static int RequiredInteger(
        JsonElement value,
        string name,
        int minimum,
        int maximum)
    {
        var property = RequiredProperty(value, name);
        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var result)
            || result < minimum
            || result > maximum)
        {
            throw new InvalidDataException("Hermes returned invalid catalog count.");
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStrings(
        JsonElement values,
        int maximumUtf8Bytes)
        => values.EnumerateArray()
            .Select(value => RequiredStringValue(
                value,
                maximumUtf8Bytes,
                allowEmpty: false))
            .ToArray();
}
