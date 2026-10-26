using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed class LilithPreviousConfigRecoveryPreparation
{
    private readonly byte[] _configBytes;
    private readonly byte[] _receiptBytes;

    internal LilithNativeConfigMutationState State { get; }
    internal string CurrentConfigSha256 { get; }
    internal string ParentReceiptSha256 { get; }
    internal byte[] CopyConfigBytes() => _configBytes.ToArray();
    internal byte[] CopyReceiptBytes() => _receiptBytes.ToArray();

    internal LilithPreviousConfigRecoveryPreparation(byte[] configBytes,
        byte[] receiptBytes, LilithNativeConfigMutationState state,
        string currentConfigSha256, string parentReceiptSha256)
    {
        _configBytes = configBytes.ToArray();
        _receiptBytes = receiptBytes.ToArray();
        State = state;
        CurrentConfigSha256 = currentConfigSha256;
        ParentReceiptSha256 = parentReceiptSha256;
    }
}

// A narrowly reviewed offline recipe, not a startup repair or a general signer.
// It only prepares bytes. The authorized stopped transaction owns its lease,
// exact beforeimages, pending marker, durable replacement and final verification.
internal static class LilithPreviousConfigRecovery
{
    internal const string PendingFileName = "native-config-recovery.pending.json";

    // The caller must independently parse the candidate with the real YAML/native
    // policy rules and pass the resulting policy, never copy it from the receipt.
    // This assembly has no YAML parser; a line search cannot prove YAML semantics.
    internal static LilithPreviousConfigRecoveryPreparation Prepare(
        byte[] currentConfigBytes,
        byte[] parentReceiptBytes,
        byte[] candidateConfigBytes,
        string expectedCanonicalBaseSha256,
        string expectedIdentityFingerprintSha256,
        string expectedSoulSha256,
        string expectedPersonalitySha256,
        HermesNativeApprovalMode independentlyVerifiedApprovalMode,
        IReadOnlyList<string> independentlyVerifiedEnabledToolsets)
    {
        ArgumentNullException.ThrowIfNull(currentConfigBytes);
        ArgumentNullException.ThrowIfNull(parentReceiptBytes);
        ArgumentNullException.ThrowIfNull(candidateConfigBytes);
        ArgumentNullException.ThrowIfNull(independentlyVerifiedEnabledToolsets);
        // Freeze caller-owned buffers before deriving or returning any evidence.
        var current = currentConfigBytes.ToArray();
        var receipt = parentReceiptBytes.ToArray();
        var candidate = candidateConfigBytes.ToArray();
        var parent = LilithNativeConfigMutationReceiptStore.ReadExact(receipt);
        if (parent.Source != LilithNativeConfigMutationReceiptStore.ProviderSetupSource
            || parent.Sequence == long.MaxValue
            || parent.CanonicalBaseSha256 != Digest(expectedCanonicalBaseSha256)
            || parent.IdentityFingerprintSha256 != Digest(expectedIdentityFingerprintSha256)
            || parent.SoulSha256 != Digest(expectedSoulSha256)
            || parent.PersonalitySha256 != Digest(expectedPersonalitySha256))
            throw Rejected("The parent receipt did not match the recovery bindings.");

        var policy = new HermesNativeToolsSetRequest(
            independentlyVerifiedEnabledToolsets, independentlyVerifiedApprovalMode);
        if (parent.ApprovalMode != policy.ApprovalMode
            || !parent.EnabledToolsets.SequenceEqual(policy.EnabledToolsets, StringComparer.Ordinal))
            throw Rejected("The independently verified target policy did not match the parent receipt.");

        var targetSha256 = Hash(candidate);
        var currentSha256 = Hash(current);
        if (targetSha256 != parent.PreviousConfigSha256
            || parent.ConfigSha256 == targetSha256
            || parent.ConfigSha256 == currentSha256
            || currentSha256 == targetSha256)
            throw Rejected("The recovery requires the exact previous config and distinct interrupted state.");

        RequireExactEmptyProvidersAppend(current, candidate);

        // The new predecessor is the last committed receipt, never the unreceipted
        // current bytes. No operation nonce or invented provider success is added.
        var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            source = LilithNativeConfigMutationReceiptStore.PreviousStateRecoverySource,
            sequence = checked(parent.Sequence + 1),
            canonical_base_sha256 = parent.CanonicalBaseSha256,
            previous_config_sha256 = parent.ConfigSha256,
            config_sha256 = targetSha256,
            identity_fingerprint_sha256 = parent.IdentityFingerprintSha256,
            soul_sha256 = parent.SoulSha256,
            personality_sha256 = parent.PersonalitySha256,
            approval_mode = HermesNativeToolsContract.ApprovalModeToken(parent.ApprovalMode),
            enabled_toolsets = parent.EnabledToolsets
        });
        return new LilithPreviousConfigRecoveryPreparation(candidate, receiptBytes,
            LilithNativeConfigMutationReceiptStore.ReadExact(receiptBytes),
            currentSha256, Hash(receipt));
    }

    internal static void ThrowIfPending(string hermesHomePath)
    {
        try
        {
            var path = Path.Combine(hermesHomePath, "runtime", PendingFileName);
            LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
            // Attribute lookup sees directories and dangling links as entries and
            // fails closed on access errors; File.Exists alone would hide them.
            if (LilithRuntimeWorkspacePathGuard.ReadExistingAttributes(path).HasValue)
                throw Rejected("A native-config recovery is pending; startup refused before profile mutation.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException)
        {
            throw Rejected("The native-config recovery marker path could not be verified; startup refused.");
        }
    }

    private static void RequireExactEmptyProvidersAppend(byte[] current, byte[] candidate)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(candidate); }
        catch (DecoderFallbackException)
        {
            throw Rejected("The previous config was not strict UTF-8.");
        }
        var crlf = text.Contains("\r\n", StringComparison.Ordinal);
        var other = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        var newline = crlf ? "\r\n" : "\n";
        if (other.Contains('\r') || (crlf && other.Contains('\n'))
            || !text.EndsWith(newline, StringComparison.Ordinal))
            throw Rejected("The previous config did not have consistent terminal line endings.");
        var suffix = Encoding.UTF8.GetBytes("providers: {}" + newline);
        if (current.Length != candidate.Length + suffix.Length
            || !current.AsSpan(0, candidate.Length).SequenceEqual(candidate)
            || !current.AsSpan(candidate.Length).SequenceEqual(suffix))
            throw Rejected("The interrupted config was not the exact terminal empty-provider append.");
    }

    private static string Digest(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var digest = value.ToLowerInvariant();
        if (digest.Length != 64 || digest.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Rejected("An independently expected binding was not a SHA-256 digest.");
        return digest;
    }

    private static string Hash(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static LilithRuntimeException Rejected(string message)
        => new(message, LilithRuntimeFailureKind.ConfigVerificationFailed);
}
