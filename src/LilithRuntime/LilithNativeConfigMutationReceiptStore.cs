using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record LilithNativeConfigMutationState(
    long Sequence,
    string Source,
    string CanonicalBaseSha256,
    string PreviousConfigSha256,
    string ConfigSha256,
    string IdentityFingerprintSha256,
    string SoulSha256,
    string PersonalitySha256,
    HermesNativeApprovalMode ApprovalMode,
    IReadOnlyList<string> EnabledToolsets,
    string? OperationNonce)
{
    internal bool IsCanonicalBase => Sequence == 0;
}

internal sealed record LilithNativeConfigMutationCommit(
    LilithNativeConfigMutationState State,
    string ReceiptSha256);

internal static class LilithNativeConfigMutationReceiptStore
{
    internal const string ReceiptFileName = "native-config-receipt.v1.json";
    internal const string ToolsSetSource = "hermes.tools.set";
    internal const string ApprovalAlwaysSource =
        HermesNativeToolsContract.ApprovalPersistenceSource;
    internal const string ModelDefaultSetSource = "hermes.model.default.set";
    internal const string ProviderSetupSource = "hermes.provider.setup";
    internal const string PreviousStateRecoverySource =
        "runtime.profile.previous_state_recovery";
    internal const string CanonicalProfileMigrationSource =
        "canonical.profile_migration";
    private const string CanonicalBaseSource = "canonical.base";

    private const int MaximumReceiptBytes = 64 * 1024;
    private static readonly string[] ToolsSetExactProperties =
    {
        "schema_version",
        "source",
        "sequence",
        "canonical_base_sha256",
        "previous_config_sha256",
        "config_sha256",
        "identity_fingerprint_sha256",
        "soul_sha256",
        "personality_sha256",
        "approval_mode",
        "enabled_toolsets"
    };
    private static readonly string[] ApprovalAlwaysExactProperties =
    {
        "schema_version",
        "source",
        "sequence",
        "canonical_base_sha256",
        "previous_config_sha256",
        "config_sha256",
        "operation_nonce",
        "identity_fingerprint_sha256",
        "soul_sha256",
        "personality_sha256",
        "approval_mode",
        "enabled_toolsets"
    };

    internal static string ReceiptPath(string hermesHomePath)
        => Path.Combine(
            RequireHermesHome(hermesHomePath),
            "runtime",
            ReceiptFileName);

    internal static bool HasReceiptEntry(string hermesHomePath)
    {
        var path = ReceiptPath(hermesHomePath);
        EnsureReceiptPathSafe(path, requireFile: false);
        return ReceiptEntryExists(path);
    }

    internal static async Task<LilithNativeConfigMutationState>
        CaptureCurrentAsync(
            string hermesHomePath,
            string canonicalBaseSha256,
            string identityFingerprintSha256,
            string soulSha256,
            string personalitySha256,
            CancellationToken cancellationToken)
    {
        var configSha256 = await ReadConfigSha256Async(
            hermesHomePath,
            cancellationToken).ConfigureAwait(false);
        canonicalBaseSha256 = Digest(canonicalBaseSha256);
        var receiptPath = ReceiptPath(hermesHomePath);
        EnsureReceiptPathSafe(receiptPath, requireFile: false);
        if (ReceiptEntryExists(receiptPath))
        {
            return await VerifyExistingAsync(
                hermesHomePath,
                canonicalBaseSha256,
                configSha256,
                identityFingerprintSha256,
                soulSha256,
                personalitySha256,
                cancellationToken).ConfigureAwait(false);
        }
        if (string.Equals(
                configSha256,
                canonicalBaseSha256,
                StringComparison.Ordinal))
        {
            return new LilithNativeConfigMutationState(
                0,
                CanonicalBaseSource,
                canonicalBaseSha256,
                canonicalBaseSha256,
                canonicalBaseSha256,
                Digest(identityFingerprintSha256),
                Digest(soulSha256),
                Digest(personalitySha256),
                HermesNativeApprovalMode.Manual,
                Array.Empty<string>(),
                null);
        }

        return await VerifyExistingAsync(
            hermesHomePath,
            canonicalBaseSha256,
            configSha256,
            identityFingerprintSha256,
            soulSha256,
            personalitySha256,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<LilithNativeConfigMutationState>
        VerifyExistingAsync(
            string hermesHomePath,
            string canonicalBaseSha256,
            string currentConfigSha256,
            string identityFingerprintSha256,
            string soulSha256,
            string personalitySha256,
            CancellationToken cancellationToken)
        => await VerifyExistingCoreAsync(
            hermesHomePath,
            new[] { canonicalBaseSha256 },
            currentConfigSha256,
            identityFingerprintSha256,
            soulSha256,
            personalitySha256,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<LilithNativeConfigMutationState>
        VerifyExistingForCanonicalMigrationAsync(
            string hermesHomePath,
            IReadOnlyList<string> acceptedCanonicalBaseSha256,
            string currentConfigSha256,
            string identityFingerprintSha256,
            string soulSha256,
            string personalitySha256,
            CancellationToken cancellationToken)
        => await VerifyExistingCoreAsync(
            hermesHomePath,
            acceptedCanonicalBaseSha256,
            currentConfigSha256,
            identityFingerprintSha256,
            soulSha256,
            personalitySha256,
            cancellationToken).ConfigureAwait(false);

    private static async Task<LilithNativeConfigMutationState>
        VerifyExistingCoreAsync(
            string hermesHomePath,
            IReadOnlyList<string> acceptedCanonicalBaseSha256,
            string currentConfigSha256,
            string identityFingerprintSha256,
            string soulSha256,
            string personalitySha256,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(acceptedCanonicalBaseSha256);
        var acceptedCanonicalBases = acceptedCanonicalBaseSha256
            .Select(Digest)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (acceptedCanonicalBases.Length == 0)
        {
            throw new ArgumentException(
                "At least one canonical base digest is required.",
                nameof(acceptedCanonicalBaseSha256));
        }
        var path = ReceiptPath(hermesHomePath);
        try
        {
            EnsureReceiptPathSafe(path, requireFile: true);
        }
        catch (Exception exception) when (exception is LilithRuntimeException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt path could not be verified; startup refused.",
                LilithRuntimeFailureKind.ConfigVerificationFailed);
        }
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumReceiptBytes)
            {
                throw new LilithRuntimeException(
                    "The Hermes native-config receipt was missing or invalid; startup refused.",
                    LilithRuntimeFailureKind.ConfigVerificationFailed);
            }
            bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LilithRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt could not be verified; startup refused.",
                LilithRuntimeFailureKind.ConfigVerificationFailed);
        }

        LilithNativeConfigMutationState state;
        try
        {
            state = ReadExact(bytes);
        }
        catch (Exception exception) when (exception is JsonException
                                          or InvalidDataException
                                          or ArgumentException
                                          or OverflowException)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt was malformed; startup refused.",
                LilithRuntimeFailureKind.ConfigVerificationFailed);
        }

        if (!acceptedCanonicalBases.Contains(
                state.CanonicalBaseSha256,
                StringComparer.Ordinal)
            || !string.Equals(
                state.ConfigSha256,
                Digest(currentConfigSha256),
                StringComparison.Ordinal)
            || !string.Equals(
                state.IdentityFingerprintSha256,
                Digest(identityFingerprintSha256),
                StringComparison.Ordinal)
            || !string.Equals(
                state.SoulSha256,
                Digest(soulSha256),
                StringComparison.Ordinal)
            || !string.Equals(
                state.PersonalitySha256,
                Digest(personalitySha256),
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt did not match the pinned profile; startup refused.",
                LilithRuntimeFailureKind.ConfigVerificationFailed);
        }

        return state;
    }

    internal static async Task<LilithNativeConfigMutationState> CommitAsync(
        string hermesHomePath,
        LilithNativeConfigMutationState previous,
        HermesNativeToolsSetResult nativeResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(nativeResult);
        return await CommitCoreAsync(
            hermesHomePath,
            previous,
            ToolsSetSource,
            nativeResult.ConfigSha256,
            nativeResult.ApprovalMode,
            nativeResult.EnabledToolsets,
            operationNonce: null,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<LilithNativeConfigMutationCommit>
        CommitModelDefaultAsync(
            string hermesHomePath,
            LilithNativeConfigMutationState previous,
            string configSha256,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var state = await CommitCoreAsync(
            hermesHomePath,
            previous,
            ModelDefaultSetSource,
            configSha256,
            previous.ApprovalMode,
            previous.EnabledToolsets,
            operationNonce: null,
            cancellationToken).ConfigureAwait(false);
        var receiptSha256 = await ReadReceiptSha256Async(
            hermesHomePath,
            cancellationToken).ConfigureAwait(false);
        return new LilithNativeConfigMutationCommit(state, receiptSha256);
    }

    internal static async Task<LilithNativeConfigMutationCommit>
        CommitApprovalAlwaysAsync(
            string hermesHomePath,
            LilithNativeConfigMutationState previous,
            HermesNativeApprovalPersistenceReceipt nativeReceipt,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(nativeReceipt);
        if (!string.Equals(
                previous.ConfigSha256,
                nativeReceipt.PreviousConfigSha256,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "Hermes permanent approval did not bind the prior native config digest.");
        }
        if (!previous.IsCanonicalBase
            && (previous.ApprovalMode != nativeReceipt.ApprovalMode
                || !previous.EnabledToolsets.SequenceEqual(
                    nativeReceipt.EnabledToolsets,
                    StringComparer.Ordinal)))
        {
            throw new LilithRuntimeException(
                "Hermes permanent approval changed the native tool policy.");
        }
        var state = await CommitCoreAsync(
            hermesHomePath,
            previous,
            ApprovalAlwaysSource,
            nativeReceipt.ConfigSha256,
            nativeReceipt.ApprovalMode,
            nativeReceipt.EnabledToolsets,
            nativeReceipt.CommitNonce,
            cancellationToken).ConfigureAwait(false);
        var receiptSha256 = await ReadReceiptSha256Async(
            hermesHomePath,
            cancellationToken).ConfigureAwait(false);
        return new LilithNativeConfigMutationCommit(state, receiptSha256);
    }

    internal static async Task<LilithNativeConfigMutationCommit> CommitProviderSetupAsync(
        string hermesHomePath,
        LilithNativeConfigMutationState previous,
        string configSha256,
        HermesNativeToolsCatalog? canonicalPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous.IsCanonicalBase != (canonicalPolicy is not null))
            throw new InvalidDataException("Provider setup requires the exact initial native tool policy.");
        // Provider setup does not grant tools, change approval mode, or select
        // a global model. Its source must not masquerade as one of those edits.
        var state = await CommitCoreAsync(hermesHomePath, previous,
            ProviderSetupSource, configSha256, canonicalPolicy?.ApprovalMode ?? previous.ApprovalMode,
            canonicalPolicy?.EnabledToolsets ?? previous.EnabledToolsets, operationNonce: null, cancellationToken)
            .ConfigureAwait(false);
        return new LilithNativeConfigMutationCommit(state,
            await ReadReceiptSha256Async(hermesHomePath, cancellationToken).ConfigureAwait(false));
    }

    internal static async Task<LilithNativeConfigMutationState>
        CommitCanonicalRebaseAsync(
            string hermesHomePath,
            LilithNativeConfigMutationState previous,
            string newCanonicalBaseSha256,
            byte[] rebasedConfig,
            IReadOnlyList<string>? rebasedEnabledToolsets,
            CancellationToken cancellationToken,
            bool allowUnchangedConfig = false)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(rebasedConfig);
        if (previous.IsCanonicalBase)
        {
            throw new LilithRuntimeException(
                "A canonical profile rebase requires an existing durable native-config receipt.");
        }
        if (previous.Sequence == long.MaxValue)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt sequence was exhausted.");
        }

        var enabledToolsets = rebasedEnabledToolsets is null
            ? previous.EnabledToolsets
            : new HermesNativeToolsSetRequest(
                rebasedEnabledToolsets,
                previous.ApprovalMode).EnabledToolsets;

        var newCanonical = Digest(newCanonicalBaseSha256);
        if (string.Equals(
                previous.CanonicalBaseSha256,
                newCanonical,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt already uses the requested canonical base.");
        }

        var home = RequireHermesHome(hermesHomePath);
        var configPath = Path.Combine(home, "config.yaml");
        var receiptPath = ReceiptPath(home);
        EnsureReceiptPathSafe(receiptPath, requireFile: true);
        var previousConfig = await File.ReadAllBytesAsync(
            configPath,
            cancellationToken).ConfigureAwait(false);
        var previousConfigSha256 = Convert.ToHexString(
            SHA256.HashData(previousConfig)).ToLowerInvariant();
        if (!string.Equals(
                previousConfigSha256,
                previous.ConfigSha256,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "The Hermes native config changed before canonical profile migration; startup refused.");
        }

        var verifiedPrevious = await VerifyExistingAsync(
            home,
            previous.CanonicalBaseSha256,
            previousConfigSha256,
            previous.IdentityFingerprintSha256,
            previous.SoulSha256,
            previous.PersonalitySha256,
            cancellationToken).ConfigureAwait(false);
        if (!StatesMatch(previous, verifiedPrevious))
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt changed before canonical profile migration; startup refused.");
        }

        var rebasedConfigSha256 = Convert.ToHexString(
            SHA256.HashData(rebasedConfig)).ToLowerInvariant();
        if (string.Equals(
                previousConfigSha256,
                rebasedConfigSha256,
                StringComparison.Ordinal)
            && !allowUnchangedConfig)
        {
            throw new LilithRuntimeException(
                "Canonical profile migration did not change the Hermes config.");
        }

        var state = new LilithNativeConfigMutationState(
            previous.Sequence + 1,
            CanonicalProfileMigrationSource,
            newCanonical,
            previous.ConfigSha256,
            rebasedConfigSha256,
            previous.IdentityFingerprintSha256,
            previous.SoulSha256,
            previous.PersonalitySha256,
            previous.ApprovalMode,
            Array.AsReadOnly(enabledToolsets.ToArray()),
            null);
        var receipt = EncodeExact(state);
        var previousReceipt = await File.ReadAllBytesAsync(
            receiptPath,
            cancellationToken).ConfigureAwait(false);
        var transaction = Guid.NewGuid().ToString("N");
        var configTemporaryPath = configPath + ".rebase-new-" + transaction;
        var configBackupPath = configPath + ".rebase-old-" + transaction;
        var receiptTemporaryPath = receiptPath + ".rebase-new-" + transaction;
        var receiptBackupPath = receiptPath + ".rebase-old-" + transaction;
        var configReplaced = false;
        var receiptReplaced = false;
        var preserveRecoveryArtifacts = false;
        try
        {
            await WriteNewDurablyAsync(
                configTemporaryPath,
                rebasedConfig,
                cancellationToken).ConfigureAwait(false);
            await WriteNewDurablyAsync(
                receiptTemporaryPath,
                receipt,
                cancellationToken).ConfigureAwait(false);

            var currentConfig = await File.ReadAllBytesAsync(
                configPath,
                cancellationToken).ConfigureAwait(false);
            var currentReceipt = await File.ReadAllBytesAsync(
                receiptPath,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    currentConfig,
                    previousConfig)
                || !CryptographicOperations.FixedTimeEquals(
                    currentReceipt,
                    previousReceipt))
            {
                throw new LilithRuntimeException(
                    "The Hermes native profile changed during canonical migration; startup refused.");
            }

            File.Replace(
                configTemporaryPath,
                configPath,
                configBackupPath);
            configReplaced = true;
            File.Replace(
                receiptTemporaryPath,
                receiptPath,
                receiptBackupPath);
            receiptReplaced = true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            var restored = false;
            if (configReplaced && !receiptReplaced && File.Exists(configBackupPath))
            {
                try
                {
                    var currentReceipt = await File.ReadAllBytesAsync(
                        receiptPath,
                        CancellationToken.None).ConfigureAwait(false);
                    if (CryptographicOperations.FixedTimeEquals(
                            currentReceipt,
                            previousReceipt))
                    {
                        File.Replace(
                            configBackupPath,
                            configPath,
                            destinationBackupFileName: null);
                        restored = true;
                    }
                }
                catch (Exception restoreException) when (
                    restoreException is IOException or UnauthorizedAccessException)
                {
                }
            }

            preserveRecoveryArtifacts =
                (configReplaced || receiptReplaced) && !restored;

            throw new LilithRuntimeException(
                restored
                    ? "Canonical profile migration could not commit; the verified native config was restored."
                    : "Canonical profile migration became uncertain; startup refused.");
        }
        finally
        {
            if (!preserveRecoveryArtifacts)
            {
                foreach (var path in new[]
                         {
                             configTemporaryPath,
                             receiptTemporaryPath,
                             configBackupPath,
                             receiptBackupPath
                         })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }

        var verified = await VerifyExistingAsync(
            home,
            state.CanonicalBaseSha256,
            state.ConfigSha256,
            state.IdentityFingerprintSha256,
            state.SoulSha256,
            state.PersonalitySha256,
            CancellationToken.None).ConfigureAwait(false);
        if (!StatesMatch(state, verified))
        {
            throw new LilithRuntimeException(
                "The rebased Hermes native-config receipt could not be reverified.");
        }
        return verified;
    }

    private static async Task<LilithNativeConfigMutationState> CommitCoreAsync(
        string hermesHomePath,
        LilithNativeConfigMutationState previous,
        string source,
        string expectedConfigSha256,
        HermesNativeApprovalMode approvalMode,
        IReadOnlyList<string> enabledToolsets,
        string? operationNonce,
        CancellationToken cancellationToken)
    {
        if (previous.Sequence < 0 || previous.Sequence == long.MaxValue)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt sequence was exhausted.");
        }

        var actualSha256 = await ReadConfigSha256Async(
            hermesHomePath,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                actualSha256,
                expectedConfigSha256,
                StringComparison.Ordinal))
        {
            throw new LilithRuntimeException(
                "Hermes native tools changed config without an exact digest receipt.");
        }

        var state = new LilithNativeConfigMutationState(
            previous.Sequence + 1,
            source,
            previous.CanonicalBaseSha256,
            previous.ConfigSha256,
            actualSha256,
            previous.IdentityFingerprintSha256,
            previous.SoulSha256,
            previous.PersonalitySha256,
            approvalMode,
            Array.AsReadOnly(enabledToolsets.ToArray()),
            operationNonce);
        var bytes = EncodeExact(state);
        var path = ReceiptPath(hermesHomePath);
        var directory = Path.GetDirectoryName(path)
            ?? throw new LilithRuntimeException(
                "The Hermes native-config receipt path was invalid.");
        Directory.CreateDirectory(directory);
        EnsureReceiptPathSafe(path, requireFile: false);
        var receiptExists = ReceiptEntryExists(path);
        if (receiptExists == previous.IsCanonicalBase)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt chain changed before commit.");
        }

        var temporaryPath = path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (previous.IsCanonicalBase)
                File.Move(temporaryPath, path);
            else
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        var verified = await VerifyExistingAsync(
            hermesHomePath,
            state.CanonicalBaseSha256,
            state.ConfigSha256,
            state.IdentityFingerprintSha256,
            state.SoulSha256,
            state.PersonalitySha256,
            cancellationToken).ConfigureAwait(false);
        if (verified.Sequence != state.Sequence
            || !string.Equals(verified.Source, state.Source, StringComparison.Ordinal)
            || !string.Equals(
                verified.OperationNonce,
                state.OperationNonce,
                StringComparison.Ordinal)
            || !string.Equals(
                verified.PreviousConfigSha256,
                state.PreviousConfigSha256,
                StringComparison.Ordinal)
            || verified.ApprovalMode != state.ApprovalMode
            || !verified.EnabledToolsets.SequenceEqual(
                state.EnabledToolsets,
                StringComparer.Ordinal))
        {
            throw new LilithRuntimeException(
                "The durable Hermes native-config receipt could not be reverified.");
        }
        return verified;
    }

    internal static async Task<string> ReadReceiptSha256Async(
        string hermesHomePath,
        CancellationToken cancellationToken)
    {
        var path = ReceiptPath(hermesHomePath);
        EnsureReceiptPathSafe(path, requireFile: true);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumReceiptBytes)
            {
                throw new LilithRuntimeException(
                    "The durable Hermes native-config receipt could not be hashed.");
            }
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (LilithRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The durable Hermes native-config receipt could not be hashed.");
        }
    }

    internal static async Task<string> ReadConfigSha256Async(
        string hermesHomePath,
        CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(
            RequireHermesHome(hermesHomePath),
            "config.yaml");
        try
        {
            var bytes = await File.ReadAllBytesAsync(configPath, cancellationToken)
                .ConfigureAwait(false);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The Hermes native config could not be hashed exactly.");
        }
    }

    private static async Task WriteNewDurablyAsync(
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
        stream.Flush(flushToDisk: true);
    }

    private static bool StatesMatch(
        LilithNativeConfigMutationState expected,
        LilithNativeConfigMutationState actual)
        => expected.Sequence == actual.Sequence
           && string.Equals(expected.Source, actual.Source, StringComparison.Ordinal)
           && string.Equals(
               expected.CanonicalBaseSha256,
               actual.CanonicalBaseSha256,
               StringComparison.Ordinal)
           && string.Equals(
               expected.PreviousConfigSha256,
               actual.PreviousConfigSha256,
               StringComparison.Ordinal)
           && string.Equals(
               expected.ConfigSha256,
               actual.ConfigSha256,
               StringComparison.Ordinal)
           && string.Equals(
               expected.IdentityFingerprintSha256,
               actual.IdentityFingerprintSha256,
               StringComparison.Ordinal)
           && string.Equals(
               expected.SoulSha256,
               actual.SoulSha256,
               StringComparison.Ordinal)
           && string.Equals(
               expected.PersonalitySha256,
               actual.PersonalitySha256,
               StringComparison.Ordinal)
           && expected.ApprovalMode == actual.ApprovalMode
           && expected.EnabledToolsets.SequenceEqual(
               actual.EnabledToolsets,
               StringComparer.Ordinal)
           && string.Equals(
               expected.OperationNonce,
               actual.OperationNonce,
               StringComparison.Ordinal);

    private static byte[] EncodeExact(LilithNativeConfigMutationState state)
    {
        var payload = new JsonObject
        {
            ["schema_version"] = 1,
            ["source"] = state.Source,
            ["sequence"] = state.Sequence,
            ["canonical_base_sha256"] = state.CanonicalBaseSha256,
            ["previous_config_sha256"] = state.PreviousConfigSha256,
            ["config_sha256"] = state.ConfigSha256
        };
        if (state.OperationNonce is not null)
            payload["operation_nonce"] = state.OperationNonce;
        payload["identity_fingerprint_sha256"] = state.IdentityFingerprintSha256;
        payload["soul_sha256"] = state.SoulSha256;
        payload["personality_sha256"] = state.PersonalitySha256;
        payload["approval_mode"] = HermesNativeToolsContract.ApprovalModeToken(
            state.ApprovalMode);
        payload["enabled_toolsets"] = JsonSerializer.SerializeToNode(
            state.EnabledToolsets);
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    // Shared strict reader for startup and side-effect-free offline preparation.
    // Reading a receipt does not establish that its config or identity is current.
    internal static LilithNativeConfigMutationState ReadExact(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is <= 0 or > MaximumReceiptBytes)
            throw new InvalidDataException("Receipt size was rejected.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        return DecodeExact(document.RootElement);
    }

    private static LilithNativeConfigMutationState DecodeExact(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Receipt root must be an object.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var propertyNames = new List<string>();
        var allowed = new HashSet<string>(
            ApprovalAlwaysExactProperties,
            StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !properties.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException("Receipt properties were rejected.");
            }
            propertyNames.Add(property.Name);
        }
        if (!properties.TryGetValue("source", out var sourceProperty)
            || sourceProperty.ValueKind != JsonValueKind.String
            || sourceProperty.GetString() is not (
                ToolsSetSource
                or ApprovalAlwaysSource
                or ModelDefaultSetSource
                or ProviderSetupSource
                or PreviousStateRecoverySource
                or CanonicalProfileMigrationSource))
        {
            throw new InvalidDataException("Receipt metadata was rejected.");
        }
        var source = sourceProperty.GetString()!;
        var hasOperationNonce = properties.ContainsKey("operation_nonce");
        var expectedProperties = string.Equals(
                source,
                ApprovalAlwaysSource,
                StringComparison.Ordinal)
            ? ApprovalAlwaysExactProperties
            : ToolsSetExactProperties;
        if (!propertyNames.SequenceEqual(
                expectedProperties,
                StringComparer.Ordinal)
            || hasOperationNonce != string.Equals(
                source,
                ApprovalAlwaysSource,
                StringComparison.Ordinal)
            || properties["schema_version"].ValueKind != JsonValueKind.Number
            || !properties["schema_version"].TryGetInt32(out var version)
            || version != 1
            || properties["sequence"].ValueKind != JsonValueKind.Number
            || !properties["sequence"].TryGetInt64(out var sequence)
            || sequence <= 0)
        {
            throw new InvalidDataException("Receipt metadata was rejected.");
        }

        var approval = RequiredString(properties, "approval_mode") switch
        {
            "manual" => HermesNativeApprovalMode.Manual,
            "smart" => HermesNativeApprovalMode.Smart,
            "off" => HermesNativeApprovalMode.Off,
            _ => throw new InvalidDataException("Receipt approval mode was rejected.")
        };
        if (properties["enabled_toolsets"].ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Receipt toolsets were rejected.");
        var toolsets = properties["enabled_toolsets"]
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new InvalidDataException("Receipt toolsets were rejected."))
            .ToArray();
        if (!toolsets.SequenceEqual(
                toolsets.OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Receipt toolsets were not in canonical ordinal order.");
        }
        var validated = new HermesNativeToolsSetRequest(toolsets, approval);

        return new LilithNativeConfigMutationState(
            sequence,
            source,
            ReceiptDigest(RequiredString(properties, "canonical_base_sha256")),
            ReceiptDigest(RequiredString(properties, "previous_config_sha256")),
            ReceiptDigest(RequiredString(properties, "config_sha256")),
            ReceiptDigest(RequiredString(properties, "identity_fingerprint_sha256")),
            ReceiptDigest(RequiredString(properties, "soul_sha256")),
            ReceiptDigest(RequiredString(properties, "personality_sha256")),
            approval,
            Array.AsReadOnly(validated.EnabledToolsets.ToArray()),
            hasOperationNonce
                ? ReceiptDigest(RequiredString(properties, "operation_nonce"))
                : null);
    }

    private static string RequiredString(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name)
    {
        var value = properties[name];
        if (value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException("Receipt text was rejected.");
        }
        return value.GetString()!;
    }

    private static string Digest(string value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        var normalized = value.ToLowerInvariant();
        if (normalized.Length != 64
            || normalized.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("A SHA-256 digest was invalid.", nameof(value));
        }
        return normalized;
    }

    private static string ReceiptDigest(string value)
    {
        var digest = Digest(value);
        if (!string.Equals(value, digest, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Receipt digests must be lowercase SHA-256 values.");
        }
        return digest;
    }

    private static string RequireHermesHome(string hermesHomePath)
    {
        if (string.IsNullOrWhiteSpace(hermesHomePath)
            || !Path.IsPathFullyQualified(hermesHomePath))
        {
            throw new ArgumentException(
                "An absolute Hermes home is required.",
                nameof(hermesHomePath));
        }
        return Path.GetFullPath(hermesHomePath);
    }

    private static bool ReceiptEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt path could not be verified.");
        }
    }

    private static void EnsureReceiptPathSafe(string path, bool requireFile)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new LilithRuntimeException(
                "The Hermes native-config receipt path was invalid.");
        if (!Directory.Exists(directory))
        {
            if (requireFile)
            {
                throw new LilithRuntimeException(
                    "The Hermes native-config receipt was missing; startup refused.");
            }
            return;
        }
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt directory was a reparse point.");
        }
        if (File.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt was a reparse point.");
        }
        if (requireFile && !File.Exists(path))
        {
            throw new LilithRuntimeException(
                "The Hermes native-config receipt was missing; startup refused.");
        }
    }
}
