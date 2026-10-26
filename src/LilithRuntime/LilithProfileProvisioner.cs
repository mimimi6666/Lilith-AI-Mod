using System.Security.Cryptography;
using System.Text;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal static partial class LilithProfileProvisioner
{
    internal const string MessagingBridgePluginId = "lilith_messaging_bridge";

    internal const string LegacyVersionOneSoulSha256 =
        "ABC419409E7D04F71669D9F555411A761DCBE0C9620F444D85EC780E6E23D854";

    internal const string CurrentOfficialPersonaConfigSha256 =
        "55C6D281991B8B03611EB55855B917E3ADE67225D8D59C66FF32CD75AEC6EDB5";

    private static readonly string[] KnownPreviousLearningConfigSha256 =
    {
        "8821F000C854BA670FB4846497F8AA6E665B9C39602F5D601C3342619877D39B",
        "CE9A747BD73FC63542997589613D05ECD9B162C379232BAB099D71463633AACD",
        "0273187EEC8CB3BBA1BB7878CA23D98D5A62DC6D1802376900F7FF36F15468AE",
        "DFF079B01C4CA20909DA0FB93D483BEDA4E94FB7F1485ED4A56C2F4B31070621",
        "24D51E5F7A1CFDF5F52912ADFCD730F780AF931DE2CB6A16F6C9DE1C12B1B634",
        "C391DE1D209F63F74B83A90FDB6AF45988ED1595D0FD73C9C2A8F5794296065A",
        "2255250BA3233CB411054AB6A24D28824A035B13A847EEB75400F92E0C1001AF",
        "55C6D281991B8B03611EB55855B917E3ADE67225D8D59C66FF32CD75AEC6EDB5"
    };

    internal static string LearningOnlyConfig =>
        BuildLearningOnlyConfig(LilithRuntimeOptions.CreateDefault());

    internal const string Stage8LearningOnlyConfig =
        "_config_version: 35\n"
        + "platform_toolsets:\n"
        + "  cli: [memory,skills,session_search,computer_use,delegation]\n"
        + "tools:\n"
        + "  tool_search:\n"
        + "    enabled: off\n"
        + "computer_use:\n"
        + "  cua_telemetry: false\n"
        + "  no_overlay: true\n"
        + "agent:\n"
        + "  coding_context: off\n"
        + "  disabled_toolsets: []\n"
        + "  max_turns: 8\n"
        + "delegation:\n"
        + "  child_toolsets: [skills,session_search]\n"
        + "  inherit_mcp_toolsets: false\n"
        + "  max_concurrent_children: 2\n"
        + "  max_spawn_depth: 1\n"
        + "  orchestrator_enabled: false\n"
        + "  subagent_auto_approve: false\n"
        + "  worktree_isolation: false\n"
        + "  max_iterations: 6\n"
        + "  max_summary_chars: 4000\n"
        + "  child_timeout_seconds: 180\n"
        + "plugins:\n"
        + "  enabled: []\n"
        + "  disabled: []\n"
        + "mcp_servers: {}\n"
        + "memory:\n"
        + "  memory_enabled: true\n"
        + "  user_profile_enabled: true\n"
        + "  write_approval: false\n"
        + "  memory_char_limit: 2200\n"
        + "  user_char_limit: 1375\n"
        + "  provider: \"\"\n"
        + "  nudge_interval: 1\n"
        + "compression:\n"
        + "  enabled: true\n"
        + "checkpoints:\n"
        + "  enabled: false\n"
        + "skills:\n"
        + "  external_dirs: []\n"
        + "  template_vars: true\n"
        + "  inline_shell: false\n"
        + "  inline_shell_timeout: 10\n"
        + "  guard_agent_created: false\n"
        + "  write_approval: false\n"
        + "  creation_nudge_interval: 1\n"
        + "curator:\n"
        + "  enabled: true\n"
        + "  interval_hours: 168\n"
        + "  min_idle_hours: 2\n"
        + "  stale_after_days: 30\n"
        + "  archive_after_days: 90\n"
        + "  consolidate: false\n"
        + "  prune_builtins: true\n"
        + "  backup:\n"
        + "    enabled: true\n"
        + "    keep: 5\n"
        + "smart_model_routing:\n"
        + "  enabled: false\n"
        + "session_reset:\n"
        + "  mode: none\n"
        + "display:\n"
        + "  tool_progress: verbose\n"
        + "  memory_notifications: verbose\n"
        + "approvals:\n"
        + "  mode: manual\n";

    internal static string PreviousPeriodicNativeToolsLearningConfig =>
        PreNotesNativeToolsLearningConfig
            .Replace(
                "  background_review_max_iterations: 6\n",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "  nudge_interval: 10\n",
                "  nudge_interval: 1\n",
                StringComparison.Ordinal)
            .Replace(
                "  creation_nudge_interval: 15\n",
                "  creation_nudge_interval: 1\n",
                StringComparison.Ordinal);

    internal const string PreNotesNativeToolsLearningConfig =
        "_config_version: 35\n"
        + "platform_toolsets:\n"
        + "  cli: [memory,skills,session_search,computer_use,delegation,web,browser,terminal,spotify]\n"
        + "tools:\n"
        + "  tool_search:\n"
        + "    enabled: off\n"
        + "computer_use:\n"
        + "  cua_telemetry: false\n"
        + "  no_overlay: true\n"
        + "web:\n"
        + "  search_backend: ddgs\n"
        + "browser:\n"
        + "  cloud_provider: local\n"
        + "  backend: off\n"
        + "agent:\n"
        + "  coding_context: off\n"
        + "  disabled_toolsets: []\n"
        + "  max_turns: 8\n"
        + "  background_review_max_iterations: 6\n"
        + "delegation:\n"
        + "  child_toolsets: [skills,session_search]\n"
        + "  inherit_mcp_toolsets: false\n"
        + "  max_concurrent_children: 2\n"
        + "  max_spawn_depth: 1\n"
        + "  orchestrator_enabled: false\n"
        + "  subagent_auto_approve: false\n"
        + "  worktree_isolation: false\n"
        + "  max_iterations: 6\n"
        + "  max_summary_chars: 4000\n"
        + "  child_timeout_seconds: 180\n"
        + "plugins:\n"
        + "  enabled: []\n"
        + "  disabled: []\n"
        + "mcp_servers: {}\n"
        + "memory:\n"
        + "  memory_enabled: true\n"
        + "  user_profile_enabled: true\n"
        + "  write_approval: false\n"
        + "  memory_char_limit: 2200\n"
        + "  user_char_limit: 1375\n"
        + "  provider: \"\"\n"
        + "  nudge_interval: 10\n"
        + "compression:\n"
        + "  enabled: true\n"
        + "checkpoints:\n"
        + "  enabled: false\n"
        + "skills:\n"
        + "  external_dirs: []\n"
        + "  template_vars: true\n"
        + "  inline_shell: false\n"
        + "  inline_shell_timeout: 10\n"
        + "  guard_agent_created: false\n"
        + "  write_approval: false\n"
        + "  creation_nudge_interval: 15\n"
        + "curator:\n"
        + "  enabled: true\n"
        + "  interval_hours: 168\n"
        + "  min_idle_hours: 2\n"
        + "  stale_after_days: 30\n"
        + "  archive_after_days: 90\n"
        + "  consolidate: false\n"
        + "  prune_builtins: true\n"
        + "  backup:\n"
        + "    enabled: true\n"
        + "    keep: 5\n"
        + "smart_model_routing:\n"
        + "  enabled: false\n"
        + "session_reset:\n"
        + "  mode: none\n"
        + "display:\n"
        + "  tool_progress: verbose\n"
        + "  memory_notifications: verbose\n"
        + "approvals:\n"
        + "  mode: manual\n";

    internal static string NotesNativeToolsLearningConfig =>
        PreNotesNativeToolsLearningConfig
            .Replace(
                "  cli: [memory,skills,session_search,computer_use,delegation,web,browser,terminal,spotify]\n",
                "  cli: [memory,skills,session_search,computer_use,delegation,web,browser,terminal,spotify,lilith_notes]\n",
                StringComparison.Ordinal)
            .Replace(
                "plugins:\n  enabled: []\n",
                "plugins:\n  enabled: [lilith_notes]\n",
                StringComparison.Ordinal);

    internal static string PreLeaveNoteNativeToolsLearningConfig =>
        NotesNativeToolsLearningConfig.Replace(
            "plugins:\n  enabled: [lilith_notes]\n",
            "plugins:\n  enabled: [lilith_notes,lilith_messaging_bridge]\n",
            StringComparison.Ordinal);

    internal static string PreviousExecutionBudgetLearningConfig =>
        PreLeaveNoteNativeToolsLearningConfig
            .Replace(
                "spotify,lilith_notes]",
                "spotify,lilith_notes,lilith_leave_note]",
                StringComparison.Ordinal)
            .Replace(
                "enabled: [lilith_notes,lilith_messaging_bridge]",
                "enabled: [lilith_notes,lilith_messaging_bridge,lilith_leave_note]",
                StringComparison.Ordinal);

    internal static string NativeToolsLearningConfig =>
        Encoding.UTF8.GetString(MigrateExecutionBudgetDefaults(
            Encoding.UTF8.GetBytes(PreviousExecutionBudgetLearningConfig)));

    internal const string BundledSkillsOptOut =
        "This profile opted out of bundled-skill seeding "
        + "(`hermes skills opt-out`).\n"
        + "Delete this file to re-enable sync on the next `hermes update`.\n";

    internal static async Task<LilithProfileProvisioningReceipt> ProvisionAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProvisionCoreAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is LilithRuntimeException
            or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw LilithRuntimeStartupFailure.Classify(exception,
                LilithRuntimeFailureKind.ProfileVerificationFailed);
        }
    }

    private static async Task<LilithProfileProvisioningReceipt> ProvisionCoreAsync(
        LilithRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        options.Validate();
        LilithPreviousConfigRecovery.ThrowIfPending(options.HermesHomePath);
        LilithIdentityTransitionGuard.ThrowIfPending(options.HermesHomePath);
        var identityPackage = LilithIdentityPackageV9Loader.Load(
            options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        var expectedSoulBytes = identityPackage.CopySoulUtf8Bytes();
        var expectedConfigText = BuildLearningOnlyConfig(
            options,
            identityPackage,
            NativeToolsLearningConfig);
        var expectedConfig = new UTF8Encoding(false).GetBytes(
            expectedConfigText);
        var previousPeriodicNativeToolsConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousPeriodicNativeToolsConfig(options, identityPackage));
        var previousNotesNativeToolsConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousNotesNativeToolsConfig(options, identityPackage));
        var previousMessagingNativeToolsConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousMessagingNativeToolsConfig(options, identityPackage));
        var previousLeaveNoteNativeToolsConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousLeaveNoteNativeToolsConfig(options, identityPackage));
        var previousExecutionBudgetConfig = Encoding.UTF8.GetBytes(
            BuildLearningOnlyConfig(options, identityPackage, PreviousExecutionBudgetLearningConfig));
        if (!CryptographicOperations.FixedTimeEquals(
                MigrateExecutionBudgetDefaults(MigrateLeaveNoteDefault(MigrateMessagingBridgeDefault(
                    MigrateLilithNotesDefault(
                        MigratePeriodicLearningNudges(
                            previousPeriodicNativeToolsConfig))))),
                expectedConfig))
        {
            throw new LilithRuntimeException(
                "The pinned periodic-learning and Lilith notes profile migration was invalid; startup refused.");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                MigrateExecutionBudgetDefaults(MigrateLeaveNoteDefault(MigrateMessagingBridgeDefault(
                    MigrateLilithNotesDefault(previousNotesNativeToolsConfig)))),
                expectedConfig))
        {
            throw new LilithRuntimeException(
                "The pinned Lilith notes profile migration was invalid; startup refused.");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                MigrateExecutionBudgetDefaults(MigrateLeaveNoteDefault(MigrateMessagingBridgeDefault(previousMessagingNativeToolsConfig))),
                expectedConfig))
        {
            throw new LilithRuntimeException(
                "The pinned Telegram messaging profile migration was invalid; startup refused.");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                MigrateExecutionBudgetDefaults(MigrateLeaveNoteDefault(previousLeaveNoteNativeToolsConfig)), expectedConfig))
        {
            throw new LilithRuntimeException(
                "The pinned image-note profile migration was invalid; startup refused.");
        }
        if (!CryptographicOperations.FixedTimeEquals(
                MigrateExecutionBudgetDefaults(previousExecutionBudgetConfig), expectedConfig))
        {
            throw new LilithRuntimeException(
                "The pinned execution-budget profile migration was invalid; startup refused.");
        }
        var previousCheckpointVoiceConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousCheckpointVoiceConfig(options));
        var previousNativeToolsConfig = new UTF8Encoding(false).GetBytes(
            BuildPreviousNativeToolsConfig(options, identityPackage));
        var voicePathMigrations = BuildSupportedVoicePathMigrations(options,
            [expectedConfig, previousPeriodicNativeToolsConfig, previousNotesNativeToolsConfig,
                previousMessagingNativeToolsConfig, previousLeaveNoteNativeToolsConfig, previousExecutionBudgetConfig]);
        var knownPreviousConfigHashes = KnownPreviousLearningConfigSha256
            .Append(Sha256(previousPeriodicNativeToolsConfig))
            .Append(Sha256(previousNotesNativeToolsConfig))
            .Append(Sha256(previousMessagingNativeToolsConfig))
            .Append(Sha256(previousLeaveNoteNativeToolsConfig))
            .Append(Sha256(previousExecutionBudgetConfig))
            .Append(Sha256(previousCheckpointVoiceConfig))
            .Append(Sha256(previousNativeToolsConfig))
            .Concat(voicePathMigrations.Select(static migration => migration.PreviousCanonicalSha256))
            .Concat(BuildSupportedVoicePathMigrations(options,
                [previousCheckpointVoiceConfig, previousNativeToolsConfig])
                .Select(static migration => migration.PreviousCanonicalSha256))
            .ToArray();
        var soulPath = Path.Combine(options.HermesHomePath, "SOUL.md");
        await PreflightSoulAsync(
            soulPath,
            expectedSoulBytes,
            cancellationToken).ConfigureAwait(false);
        var workspacePath = Path.Combine(options.HermesHomePath, "workspace");
        LilithRuntimeWorkspacePathGuard.EnsureSafe(workspacePath);

        Directory.CreateDirectory(options.HermesHomePath);
        Directory.CreateDirectory(Path.Combine(options.HermesHomePath, "runtime"));
        Directory.CreateDirectory(workspacePath);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(workspacePath);

        var markerPath = Path.Combine(
            options.HermesHomePath,
            ".no-bundled-skills");
        var markerBytes = new UTF8Encoding(false).GetBytes(BundledSkillsOptOut);
        var markerPresent = await VerifyOptionalExactAsync(
            markerPath,
            markerBytes,
            "The bundled-skill opt-out marker drifted; startup refused.",
            cancellationToken).ConfigureAwait(false);

        var configPath = Path.Combine(options.HermesHomePath, "config.yaml");
        var canonicalConfigSha256 = Sha256(expectedConfig);
        var expectedSoulSha256 = Sha256(expectedSoulBytes);
        var nativeMutationReceiptAccepted = false;
        var activeConfigSha256 = canonicalConfigSha256;
        var hasNativeMutationReceipt =
            LilithNativeConfigMutationReceiptStore.HasReceiptEntry(
                options.HermesHomePath);
        if (hasNativeMutationReceipt && !File.Exists(configPath))
        {
            throw new LilithRuntimeException(
                "The Hermes native config was missing beside its durable receipt; startup refused.");
        }
        if (File.Exists(configPath))
        {
            var existingConfig = await File.ReadAllBytesAsync(
                configPath,
                cancellationToken).ConfigureAwait(false);
            var existingConfigSha256 = Sha256(existingConfig);
            if (hasNativeMutationReceipt)
            {
                var previousPeriodicCanonicalSha256 = Sha256(
                    previousPeriodicNativeToolsConfig);
                var previousNotesCanonicalSha256 = Sha256(
                    previousNotesNativeToolsConfig);
                var previousMessagingCanonicalSha256 = Sha256(
                    previousMessagingNativeToolsConfig);
                var previousLeaveNoteCanonicalSha256 = Sha256(
                    previousLeaveNoteNativeToolsConfig);
                var previousExecutionBudgetCanonicalSha256 = Sha256(previousExecutionBudgetConfig);
                var nativeState = await LilithNativeConfigMutationReceiptStore
                    .VerifyExistingForCanonicalMigrationAsync(
                    options.HermesHomePath,
                    new[]
                    {
                        canonicalConfigSha256,
                        previousPeriodicCanonicalSha256,
                        previousNotesCanonicalSha256,
                        previousMessagingCanonicalSha256,
                        previousLeaveNoteCanonicalSha256,
                        previousExecutionBudgetCanonicalSha256
                    }.Concat(voicePathMigrations.Select(static migration => migration.PreviousCanonicalSha256)).ToArray(),
                    existingConfigSha256,
                    identityPackage.IdentityFingerprintSha256,
                    expectedSoulSha256,
                    identityPackage.HermesPersonalityContentSha256,
                    cancellationToken).ConfigureAwait(false);
                var voicePathMigration = voicePathMigrations.SingleOrDefault(migration => string.Equals(
                    migration.PreviousCanonicalSha256, nativeState.CanonicalBaseSha256,
                    StringComparison.OrdinalIgnoreCase));
                var effectiveCanonicalBase = voicePathMigration?.CurrentPathCanonicalSha256
                    ?? nativeState.CanonicalBaseSha256;
                var requiresPeriodicMigration = string.Equals(
                    effectiveCanonicalBase,
                    previousPeriodicCanonicalSha256,
                    StringComparison.OrdinalIgnoreCase);
                var requiresNotesMigration = requiresPeriodicMigration
                    || string.Equals(
                        effectiveCanonicalBase,
                        previousNotesCanonicalSha256,
                        StringComparison.OrdinalIgnoreCase);
                var requiresMessagingMigration = requiresNotesMigration
                    || string.Equals(
                        effectiveCanonicalBase,
                        previousMessagingCanonicalSha256,
                        StringComparison.OrdinalIgnoreCase);
                var requiresLeaveNoteMigration = requiresMessagingMigration
                    || string.Equals(effectiveCanonicalBase,
                        previousLeaveNoteCanonicalSha256, StringComparison.OrdinalIgnoreCase);
                var requiresExecutionBudgetMigration = requiresLeaveNoteMigration
                    || string.Equals(effectiveCanonicalBase,
                        previousExecutionBudgetCanonicalSha256, StringComparison.OrdinalIgnoreCase);
                if (requiresExecutionBudgetMigration || voicePathMigration is not null)
                {
                    var rebasedConfig = requiresPeriodicMigration
                        ? MigratePeriodicLearningNudges(existingConfig)
                        : existingConfig;
                    if (requiresNotesMigration)
                        rebasedConfig = MigrateLilithNotesDefault(rebasedConfig);
                    if (requiresMessagingMigration)
                        rebasedConfig = MigrateMessagingBridgeDefault(rebasedConfig);
                    if (requiresLeaveNoteMigration)
                        rebasedConfig = MigrateLeaveNoteDefault(rebasedConfig);
                    if (requiresExecutionBudgetMigration)
                        rebasedConfig = MigrateExecutionBudgetDefaults(rebasedConfig);
                    if (voicePathMigration is not null)
                        rebasedConfig = MigratePackagedVoiceModelDefault(rebasedConfig,
                            voicePathMigration.PreviousModelPath, options.VoiceModelPath);
                    var rebasedEnabledToolsets = (requiresNotesMigration
                            ? nativeState.EnabledToolsets.Append(
                                LilithNotesPluginProvisioner.PluginId)
                            : nativeState.EnabledToolsets)
                        .Concat(requiresLeaveNoteMigration
                            ? new[] { LilithLeaveNotePluginProvisioner.PluginId }
                            : Array.Empty<string>())
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(static value => value, StringComparer.Ordinal)
                        .ToArray();
                    nativeState = await LilithNativeConfigMutationReceiptStore
                        .CommitCanonicalRebaseAsync(
                            options.HermesHomePath,
                            nativeState,
                            canonicalConfigSha256,
                            rebasedConfig,
                            rebasedEnabledToolsets,
                            cancellationToken,
                            allowUnchangedConfig: !requiresLeaveNoteMigration).ConfigureAwait(false);
                    existingConfigSha256 = nativeState.ConfigSha256
                        .ToUpperInvariant();
                }
                nativeMutationReceiptAccepted = true;
                activeConfigSha256 = existingConfigSha256;
            }
        }
        if (!nativeMutationReceiptAccepted)
        {
            await CreateMigrateOrVerifyExactAsync(
                configPath,
                expectedConfig,
                knownPreviousConfigHashes,
                "The dedicated Hermes learning config drifted without a durable native-config receipt; startup refused.",
                cancellationToken).ConfigureAwait(false);
        }

        if (markerPresent)
        {
            await RemoveIfExactAsync(
                markerPath,
                markerBytes,
                "The bundled-skill opt-out marker drifted; startup refused.",
                cancellationToken).ConfigureAwait(false);
        }

        var soulDisposition = await CreateMigrateOrVerifySoulAsync(
            soulPath,
            expectedSoulBytes,
            cancellationToken).ConfigureAwait(false);

        var soulBytes = await File.ReadAllBytesAsync(
            soulPath,
            cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(
                soulBytes,
                expectedSoulBytes))
        {
            throw new LilithRuntimeException(
                "The immutable Lilith SOUL drifted; startup refused.");
        }

        if (File.Exists(markerPath))
        {
            throw new LilithRuntimeException(
                "The bundled-skill opt-out marker is still present; startup refused.");
        }

        var finalConfigSha256 = await LilithNativeConfigMutationReceiptStore
            .ReadConfigSha256Async(options.HermesHomePath, cancellationToken)
            .ConfigureAwait(false);
        if (nativeMutationReceiptAccepted)
        {
            await LilithNativeConfigMutationReceiptStore.VerifyExistingAsync(
                options.HermesHomePath,
                canonicalConfigSha256,
                finalConfigSha256,
                identityPackage.IdentityFingerprintSha256,
                expectedSoulSha256,
                identityPackage.HermesPersonalityContentSha256,
                cancellationToken).ConfigureAwait(false);
        }
        else if (!string.Equals(
                     finalConfigSha256,
                     canonicalConfigSha256,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new LilithRuntimeException(
                "The dedicated Hermes learning config changed after provisioning; startup refused.");
        }
        activeConfigSha256 = finalConfigSha256.ToUpperInvariant();

        await HermesPackagedLocalSceneProvisioner.TryProvisionAsync(options, cancellationToken).ConfigureAwait(false);

        return new LilithProfileProvisioningReceipt(
            options.HermesHomePath,
            soulDisposition,
            Sha256(soulBytes),
            activeConfigSha256,
            BundledSkillsOptOutPresent: false,
            identityPackage.PackageId,
            identityPackage.PackageVersion,
            identityPackage.IdentityFingerprintSha256,
            identityPackage.ManifestSha256,
            identityPackage.HermesPersonalityContentSha256,
            canonicalConfigSha256,
            nativeMutationReceiptAccepted);
    }

    private static void RequirePinnedIdentityPackage(
        LilithIdentityPackageV1 identityPackage)
    {
        if (!string.Equals(
                identityPackage.IdentityFingerprintSha256,
                LilithRuntimeOptions.PinnedIdentityFingerprintSha256,
                StringComparison.Ordinal)
            || !string.Equals(
                identityPackage.ManifestSha256,
                LilithRuntimeOptions.PinnedIdentityPackageManifestSha256,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The Lilith identity package does not match the Runtime pin.");
        }
    }

    private static async Task<LilithSoulProvisioningDisposition>
        CreateMigrateOrVerifySoulAsync(
            string path,
            byte[] expected,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            try
            {
                await WriteNewAsync(path, expected, cancellationToken)
                    .ConfigureAwait(false);
                return LilithSoulProvisioningDisposition.Seeded;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return LilithSoulProvisioningDisposition.VerifiedExisting;
        }
        if (!string.Equals(
                Sha256(actual),
                LegacyVersionOneSoulSha256,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The immutable Lilith SOUL drifted; startup refused without overwriting it.");
        }

        var temporaryPath = path + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteNewAsync(temporaryPath, expected, cancellationToken)
                .ConfigureAwait(false);
            var current = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    Sha256(current),
                    LegacyVersionOneSoulSha256,
                    StringComparison.Ordinal))
            {
                throw new LilithRuntimeException(
                    "The immutable Lilith SOUL changed during migration; startup refused.");
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var migrated = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(migrated, expected))
        {
            throw new LilithRuntimeException(
                "The immutable Lilith SOUL migration could not be verified.");
        }
        return LilithSoulProvisioningDisposition.MigratedKnownVersion;
    }

    private static async Task PreflightSoulAsync(
        string path,
        byte[] expected,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (CryptographicOperations.FixedTimeEquals(actual, expected)
            || string.Equals(
                Sha256(actual),
                LegacyVersionOneSoulSha256,
                StringComparison.Ordinal))
        {
            return;
        }

        throw new LilithRuntimeException(
            "The immutable Lilith SOUL drifted; startup refused without changing the profile.");
    }

    private static async Task<bool> VerifyOptionalExactAsync(
        string path,
        byte[] expected,
        string driftMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new LilithRuntimeException(driftMessage);
        }

        return true;
    }

    private static async Task RemoveIfExactAsync(
        string path,
        byte[] expected,
        string driftMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new LilithRuntimeException(driftMessage);
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The known bundled-skill opt-out marker could not be removed; startup refused.");
        }

        if (File.Exists(path))
        {
            throw new LilithRuntimeException(
                "The known bundled-skill opt-out marker remained after removal; startup refused.");
        }
    }

    private static async Task CreateOrVerifyExactAsync(
        string path,
        byte[] expected,
        string driftMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            try
            {
                await WriteNewAsync(path, expected, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            throw new LilithRuntimeException(driftMessage);
        }
    }

    private static async Task CreateMigrateOrVerifyExactAsync(
        string path,
        byte[] expected,
        IReadOnlyList<string> knownPreviousSha256,
        string driftMessage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            await CreateOrVerifyExactAsync(
                path,
                expected,
                driftMessage,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return;
        }

        var actualSha256 = Sha256(actual);
        if (!knownPreviousSha256.Contains(actualSha256, StringComparer.Ordinal))
        {
            throw new LilithRuntimeException(driftMessage);
        }

        var temporaryPath = path + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            await WriteNewAsync(temporaryPath, expected, cancellationToken)
                .ConfigureAwait(false);
            var current = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(current, actual))
            {
                throw new LilithRuntimeException(
                    "The dedicated Hermes learning config changed during migration; startup refused.");
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var migrated = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(migrated, expected))
        {
            throw new LilithRuntimeException(
                "The dedicated Hermes learning config migration could not be verified.");
        }
    }

    private static async Task WriteNewAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    internal static string BuildLearningOnlyConfig(LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(
            options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildLearningOnlyConfig(
            options,
            identityPackage,
            NativeToolsLearningConfig);
    }

    // Offline deployment preparation only. This builds canonical bytes; it does
    // not read or overwrite the active profile and is not a startup migration.
    internal static string BuildCanonicalForIdentityTransition(
        LilithRuntimeOptions options, LilithIdentityPackageV1 identityPackage)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identityPackage);
        options.Validate();
        var isV1 = identityPackage.PackageId == LilithIdentityPackageV1Loader.PackageId
            && identityPackage.PackageVersion == 1
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV1Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV1Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV1Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == "A30EEEF433358200B2EBD7CE6D6C5106CC41D6F0C5D4A9B884DCC07846461A97";
        var isV2 = identityPackage.PackageId == LilithIdentityPackageV2Loader.PackageId
            && identityPackage.PackageVersion == 2
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV2Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV2Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV2Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV2Loader.ManifestSha256;
        var isV3 = identityPackage.PackageId == LilithIdentityPackageV3Loader.PackageId
            && identityPackage.PackageVersion == 3
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV3Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV3Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV3Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV3Loader.ManifestSha256;
        var isV4 = identityPackage.PackageId == LilithIdentityPackageV4Loader.PackageId
            && identityPackage.PackageVersion == 4
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV4Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV4Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV4Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV4Loader.ManifestSha256;
        var isV5 = identityPackage.PackageId == LilithIdentityPackageV5Loader.PackageId
            && identityPackage.PackageVersion == 5
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV5Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV5Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV5Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV5Loader.ManifestSha256;
        var isV6 = identityPackage.PackageId == LilithIdentityPackageV6Loader.PackageId
            && identityPackage.PackageVersion == 6
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV6Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV6Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV6Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV6Loader.ManifestSha256;
        var isV7 = identityPackage.PackageId == LilithIdentityPackageV7Loader.PackageId
            && identityPackage.PackageVersion == 7
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV7Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV7Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV7Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV7Loader.ManifestSha256;
        var isV8 = identityPackage.PackageId == LilithIdentityPackageV8Loader.PackageId
            && identityPackage.PackageVersion == 8
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV8Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV8Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV8Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV8Loader.ManifestSha256;
        var isV9 = identityPackage.PackageId == LilithIdentityPackageV9Loader.PackageId
            && identityPackage.PackageVersion == 9
            && identityPackage.IdentityFingerprintSha256 == LilithIdentityPackageV9Loader.IdentityFingerprintSha256
            && identityPackage.SoulAssetSha256 == LilithIdentityPackageV9Loader.SoulAssetSha256
            && identityPackage.HermesPersonalityContentSha256 == LilithIdentityPackageV9Loader.HermesPersonalityContentSha256
            && identityPackage.ManifestSha256 == LilithIdentityPackageV9Loader.ManifestSha256;
        if ((!isV1 && !isV2 && !isV3 && !isV4 && !isV5 && !isV6 && !isV7 && !isV8 && !isV9)
            || Sha256(identityPackage.CopySoulUtf8Bytes()) != identityPackage.SoulAssetSha256
            || Sha256(identityPackage.CopyHermesPersonalityUtf8Bytes()) != identityPackage.HermesPersonalityContentSha256)
            throw new LilithRuntimeException("Identity transition requires an exact reviewed package.");
        return BuildLearningOnlyConfig(options, identityPackage, NativeToolsLearningConfig);
    }

    private static string BuildLearningOnlyConfig(
        LilithRuntimeOptions options,
        LilithIdentityPackageV1 identityPackage,
        string baseConfig)
    {
        var personalityBlock = IndentYamlLiteralBlock(
            identityPackage.HermesPersonalityContent,
            spaces: 8);
        return baseConfig.Replace(
                "checkpoints:\n  enabled: false\n",
                "checkpoints:\n"
                + "  enabled: true\n"
                + "  max_snapshots: 20\n"
                + "  max_total_size_mb: 500\n"
                + "  max_file_size_mb: 10\n"
                + "  auto_prune: false\n",
                StringComparison.Ordinal)
            .Replace(
                "  max_turns: 8\n",
                "  max_turns: 8\n"
                + "  personalities:\n"
                + "    lilith:\n"
                + "      system_prompt: |\n"
                + personalityBlock,
                StringComparison.Ordinal)
            .Replace(
                "  max_turns: 32\n",
                "  max_turns: 32\n"
                + "  personalities:\n"
                + "    lilith:\n"
                + "      system_prompt: |\n"
                + personalityBlock,
                StringComparison.Ordinal)
            .Replace(
                "  memory_notifications: verbose\n",
                "  memory_notifications: verbose\n"
                + "  personality: lilith\n",
                StringComparison.Ordinal)
            + "identity:\n"
            + "  require_soul: true\n"
            + "  suppress_kernel_help: true\n"
            + "  personality_allowlist: [lilith]\n"
            + "  locked_personality: lilith\n"
            + "  force_soul_for_children: true\n"
            + "  fingerprint: "
            + identityPackage.IdentityFingerprintSha256.ToLowerInvariant()
            + "\n"
            + "voice:\n"
            + "  submit_mode: draft\n"
            + "  auto_tts: false\n"
            + "  barge_in: false\n"
            + "  stop_phrases: []\n"
            + "stt:\n"
            + "  enabled: true\n"
            + "  provider: local\n"
            + "  local:\n"
            + "    model: " + YamlSingleQuoted(options.VoiceModelPath) + "\n"
            + "    language: zh\n"
            + "    device: cpu\n"
            + "    compute_type: int8\n"
            + "wake_word:\n"
            + "  enabled: false\n"
            + "security:\n"
            + "  allow_lazy_installs: false\n";
    }

    private static string BuildPreviousNativeToolsConfig(
        LilithRuntimeOptions options,
        LilithIdentityPackageV1 identityPackage)
        => BuildLearningOnlyConfig(
            options,
            identityPackage,
            Stage8LearningOnlyConfig);

    internal static string BuildPreviousNotesNativeToolsConfig(
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(
            options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildPreviousNotesNativeToolsConfig(options, identityPackage);
    }

    private static string BuildPreviousNotesNativeToolsConfig(
        LilithRuntimeOptions options,
        LilithIdentityPackageV1 identityPackage)
        => BuildLearningOnlyConfig(
            options,
            identityPackage,
            PreNotesNativeToolsLearningConfig);

    internal static string BuildPreviousMessagingNativeToolsConfig(
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(
            options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildPreviousMessagingNativeToolsConfig(options, identityPackage);
    }

    private static string BuildPreviousMessagingNativeToolsConfig(
        LilithRuntimeOptions options,
        LilithIdentityPackageV1 identityPackage)
        => BuildLearningOnlyConfig(
            options,
            identityPackage,
            NotesNativeToolsLearningConfig);

    internal static string BuildPreviousLeaveNoteNativeToolsConfig(
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildPreviousLeaveNoteNativeToolsConfig(options, identityPackage);
    }

    private static string BuildPreviousLeaveNoteNativeToolsConfig(
        LilithRuntimeOptions options, LilithIdentityPackageV1 identityPackage)
        => BuildLearningOnlyConfig(options, identityPackage, PreLeaveNoteNativeToolsLearningConfig);

    internal static string BuildPreviousPeriodicNativeToolsConfig(
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(
            options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildPreviousPeriodicNativeToolsConfig(options, identityPackage);
    }

    private static string BuildPreviousPeriodicNativeToolsConfig(
        LilithRuntimeOptions options,
        LilithIdentityPackageV1 identityPackage)
        => BuildLearningOnlyConfig(
            options,
            identityPackage,
            PreviousPeriodicNativeToolsLearningConfig);

    internal static string BuildPreviousExecutionBudgetConfig(LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var identityPackage = LilithIdentityPackageV9Loader.Load(options.IdentityPackageRootPath);
        RequirePinnedIdentityPackage(identityPackage);
        return BuildLearningOnlyConfig(options, identityPackage, PreviousExecutionBudgetLearningConfig);
    }

    internal static byte[] MigrateExecutionBudgetDefaults(byte[] config)
    {
        ArgumentNullException.ThrowIfNull(config);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            throw new LilithRuntimeException("The execution-budget config was not strict UTF-8; migration refused.");
        }
        var newline = RequireConsistentLineEnding(text);
        var lines = text.Split(newline, StringSplitOptions.None);
        UpgradeDefault("agent", "max_turns", 8, 32);
        UpgradeDefault("delegation", "max_iterations", 6, 16);
        return Encoding.UTF8.GetBytes(string.Join(newline, lines));

        void UpgradeDefault(string section, string key, int oldDefault, int newDefault)
        {
            // Deliberately narrow: the native writer uses root mappings and two-space
            // scalar keys. Ambiguous/unsupported YAML must not be silently rebased.
            var headers = Enumerable.Range(0, lines.Length)
                .Where(index => lines[index].Length > 0 && !char.IsWhiteSpace(lines[index][0])
                    && lines[index].Split(':', 2)[0].Trim().Trim('\'', '"') == section)
                .ToArray();
            if (headers.Length != 1
                || lines[headers[0]].Split('#', 2)[0].TrimEnd() != section + ":")
                throw new LilithRuntimeException("The execution-budget section was ambiguous; migration refused.");
            var start = headers[0] + 1;
            var end = start;
            while (end < lines.Length && (lines[end].Length == 0
                || char.IsWhiteSpace(lines[end][0]) || lines[end][0] == '#'))
                end++;
            var prefix = "  " + key + ":";
            var keys = Enumerable.Range(start, end - start)
                .Where(index => lines[index].TakeWhile(char.IsWhiteSpace).Count() == 2
                    && lines[index].TrimStart().Split(':', 2)[0].Trim().Trim('\'', '"') == key)
                .ToArray();
            if (keys.Length != 1 || !lines[keys[0]].StartsWith(prefix, StringComparison.Ordinal))
                throw new LilithRuntimeException("The execution-budget key was ambiguous; migration refused.");
            var index = keys[0];
            var suffix = lines[index][prefix.Length..];
            var comment = suffix.IndexOf('#');
            var scalar = (comment < 0 ? suffix : suffix[..comment]).Trim();
            if (!int.TryParse(scalar, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 1)
                throw new LilithRuntimeException("The execution-budget scalar was unsupported; migration refused.");
            if (value == oldDefault)
            {
                var offset = prefix.Length + suffix.TakeWhile(char.IsWhiteSpace).Count();
                lines[index] = lines[index][..offset]
                    + newDefault.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + lines[index][(offset + scalar.Length)..];
            }
        }
    }

    private static byte[] MigratePeriodicLearningNudges(byte[] config)
    {
        ArgumentNullException.ThrowIfNull(config);
        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            throw new LilithRuntimeException(
                "The Hermes native config was not strict UTF-8; canonical migration refused.");
        }

        var lineEnding = RequireConsistentLineEnding(text);
        text = ReplaceExactlyOnce(
            text,
            "delegation:" + lineEnding,
            "  background_review_max_iterations: 6" + lineEnding
            + "delegation:" + lineEnding);
        text = ReplaceExactlyOnce(
            text,
            "  nudge_interval: 1" + lineEnding,
            "  nudge_interval: 10" + lineEnding);
        text = ReplaceExactlyOnce(
            text,
            "  creation_nudge_interval: 1" + lineEnding,
            "  creation_nudge_interval: 15" + lineEnding);
        return new UTF8Encoding(false).GetBytes(text);
    }

    private static byte[] MigrateLilithNotesDefault(byte[] config)
    {
        ArgumentNullException.ThrowIfNull(config);
        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            throw new LilithRuntimeException(
                "The Hermes native config was not strict UTF-8; Lilith notes migration refused.");
        }

        var lineEnding = RequireConsistentLineEnding(text);
        var lines = text.Split(lineEnding, StringSplitOptions.None).ToList();
        AddYamlListItem(
            lines,
            "platform_toolsets",
            "cli",
            LilithNotesPluginProvisioner.PluginId);
        AddYamlListItem(
            lines,
            "plugins",
            "enabled",
            LilithNotesPluginProvisioner.PluginId);
        return new UTF8Encoding(false).GetBytes(
            string.Join(lineEnding, lines));
    }

    private static byte[] MigrateMessagingBridgeDefault(byte[] config)
    {
        ArgumentNullException.ThrowIfNull(config);
        string text;
        try
        {
            text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            throw new LilithRuntimeException(
                "The Hermes native config was not strict UTF-8; messaging migration refused.");
        }

        var lineEnding = RequireConsistentLineEnding(text);
        var lines = text.Split(lineEnding, StringSplitOptions.None).ToList();
        AddYamlListItem(
            lines,
            "plugins",
            "enabled",
            MessagingBridgePluginId);
        return new UTF8Encoding(false).GetBytes(
            string.Join(lineEnding, lines));
    }

    private static byte[] MigrateLeaveNoteDefault(byte[] config)
    {
        ArgumentNullException.ThrowIfNull(config);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(config);
        }
        catch (DecoderFallbackException)
        {
            throw new LilithRuntimeException(
                "The Hermes native config was not strict UTF-8; image-note migration refused.");
        }
        var lineEnding = RequireConsistentLineEnding(text);
        var lines = text.Split(lineEnding, StringSplitOptions.None).ToList();
        AddYamlListItem(lines, "platform_toolsets", "cli", LilithLeaveNotePluginProvisioner.PluginId);
        AddYamlListItem(lines, "plugins", "enabled", LilithLeaveNotePluginProvisioner.PluginId);
        return new UTF8Encoding(false).GetBytes(string.Join(lineEnding, lines));
    }

    private static void AddYamlListItem(
        IList<string> lines,
        string section,
        string key,
        string item)
    {
        var sectionHeader = section + ":";
        var sectionIndexes = lines
            .Select((line, index) => (line, index))
            .Where(pair => string.Equals(
                pair.line,
                sectionHeader,
                StringComparison.Ordinal))
            .Select(static pair => pair.index)
            .ToArray();
        if (sectionIndexes.Length != 1)
        {
            throw new LilithRuntimeException(
                "The Hermes native config did not contain exactly one pinned "
                + section
                + " section; Lilith notes migration refused.");
        }

        var sectionStart = sectionIndexes[0];
        var sectionEnd = lines.Count;
        for (var index = sectionStart + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length > 0
                && !char.IsWhiteSpace(line[0])
                && !line.StartsWith("#", StringComparison.Ordinal))
            {
                sectionEnd = index;
                break;
            }
        }

        var keyPrefix = "  " + key + ":";
        var keyIndexes = Enumerable.Range(
                sectionStart + 1,
                sectionEnd - sectionStart - 1)
            .Where(index => lines[index].StartsWith(
                keyPrefix,
                StringComparison.Ordinal))
            .ToArray();
        if (keyIndexes.Length != 1)
        {
            throw new LilithRuntimeException(
                "The Hermes native config did not contain exactly one pinned "
                + section
                + "."
                + key
                + " list; Lilith notes migration refused.");
        }

        var keyIndex = keyIndexes[0];
        var suffix = lines[keyIndex][keyPrefix.Length..].Trim();
        if (suffix.Length > 0)
        {
            if (suffix.Length < 2
                || suffix[0] != '['
                || suffix[^1] != ']')
            {
                throw new LilithRuntimeException(
                    "The Hermes native config used an unsupported inline list; Lilith notes migration refused.");
            }
            var values = suffix[1..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .ToList();
            if (values.Any(static value => !IsYamlIdentifier(value))
                || values.Distinct(StringComparer.Ordinal).Count()
                    != values.Count)
            {
                throw new LilithRuntimeException(
                    "The Hermes native config inline list was invalid; Lilith notes migration refused.");
            }
            if (!values.Contains(item, StringComparer.Ordinal))
                values.Add(item);
            lines[keyIndex] = keyPrefix + " [" + string.Join(',', values) + "]";
            return;
        }

        var insertionIndex = keyIndex + 1;
        var found = false;
        while (insertionIndex < lines.Count)
        {
            var line = lines[insertionIndex];
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                break;
            var indentation = line.TakeWhile(char.IsWhiteSpace).Count();
            if (indentation <= 2)
                break;
            if (!line.StartsWith("    - ", StringComparison.Ordinal)
                || !IsYamlIdentifier(line[6..].Trim()))
            {
                throw new LilithRuntimeException(
                    "The Hermes native config block list was invalid; Lilith notes migration refused.");
            }
            found |= string.Equals(
                line[6..].Trim(),
                item,
                StringComparison.Ordinal);
            insertionIndex++;
        }
        if (!found)
            lines.Insert(insertionIndex, "    - " + item);
    }

    private static bool IsYamlIdentifier(string value)
        => value.Length is > 0 and <= 64
           && value.All(static character =>
               character is >= 'a' and <= 'z'
               or >= '0' and <= '9'
               or '_' or '-');

    private static string RequireConsistentLineEnding(string value)
    {
        var hasCrLf = value.Contains("\r\n", StringComparison.Ordinal);
        var withoutCrLf = value.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        if (withoutCrLf.Contains('\r') || (hasCrLf && withoutCrLf.Contains('\n')))
        {
            throw new LilithRuntimeException(
                "The Hermes native config used mixed line endings; canonical migration refused.");
        }
        return hasCrLf ? "\r\n" : "\n";
    }

    private static string ReplaceExactlyOnce(
        string value,
        string previous,
        string current)
    {
        var first = value.IndexOf(previous, StringComparison.Ordinal);
        if (first < 0
            || value.IndexOf(
                previous,
                first + previous.Length,
                StringComparison.Ordinal) >= 0)
        {
            throw new LilithRuntimeException(
                "The Hermes native config did not contain exactly one pinned learning setting; canonical migration refused.");
        }
        return string.Concat(
            value.AsSpan(0, first),
            current,
            value.AsSpan(first + previous.Length));
    }

    internal static string BuildPreviousCheckpointVoiceConfig(
        LilithRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return Stage8LearningOnlyConfig.Replace(
                "checkpoints:\n  enabled: false\n",
                "checkpoints:\n"
                + "  enabled: true\n"
                + "  max_snapshots: 20\n"
                + "  max_total_size_mb: 500\n"
                + "  max_file_size_mb: 10\n"
                + "  auto_prune: false\n",
                StringComparison.Ordinal)
            + "voice:\n"
            + "  submit_mode: draft\n"
            + "  auto_tts: false\n"
            + "  barge_in: false\n"
            + "  stop_phrases: []\n"
            + "stt:\n"
            + "  enabled: true\n"
            + "  provider: local\n"
            + "  local:\n"
            + "    model: " + YamlSingleQuoted(options.VoiceModelPath) + "\n"
            + "    language: zh\n"
            + "    device: cpu\n"
            + "    compute_type: int8\n"
            + "wake_word:\n"
            + "  enabled: false\n"
            + "security:\n"
            + "  allow_lazy_installs: false\n";
    }

    private static string IndentYamlLiteralBlock(string value, int spaces)
    {
        if (string.IsNullOrWhiteSpace(value)
            || spaces <= 0
            || value.Any(character =>
                char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            throw new ArgumentException(
                "A non-empty text value is required for the YAML literal block.",
                nameof(value));
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var prefix = new string(' ', spaces);
        return string.Join(
                "\n",
                normalized.Split('\n').Select(line => prefix + line))
            + "\n";
    }

    private static string YamlSingleQuoted(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "A non-empty single-line YAML value is required.",
                nameof(value));
        }
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
