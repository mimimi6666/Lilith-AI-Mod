using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace LilithRuntimeProviderBridge;

internal static class LilithHermesEnvironmentKeys
{
    internal const string HermesHome = "HERMES_HOME";

    // These values define Lilith's fixed identity/workspace boundary. Provider
    // credentials, provider/model choices and provider-specific settings are
    // intentionally not enumerated here: Hermes owns those native settings.
    private static readonly HashSet<string> Reserved = new(
        new[]
        {
            HermesHome,
            "HERMES_EPHEMERAL_SYSTEM_PROMPT",
            "HERMES_ENVIRONMENT_HINT",
            "HERMES_ISO_CERTIFY_SYNTH_TURN",
            "TERMINAL_CWD",
            "HERMES_IDENTITY_REQUIRED",
            "HERMES_IDENTITY_EXPECTED_FINGERPRINT",
            "HERMES_IDENTITY_EXPECTED_CONFIG_SHA256",
            "HERMES_IDENTITY_EXPECTED_SOUL_SHA256",
            "HERMES_IDENTITY_EXPECTED_PERSONALITY_SHA256",
            "HERMES_MANAGED_DIR",
            "HERMES_MANAGED_CONFIG_SHA256"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static bool IsReserved(string value) => Reserved.Contains(value);
}

internal static class LilithHermesEnvironmentFileGuard
{
    private const int MaximumEnvironmentFileBytes = 256 * 1024;
    private const int MaximumKeyPrefixBytes = 512;

    internal static LilithRuntimeProviderBridgeAdmission InspectNativeProfile(
        string? productHermesHome)
    {
        string home;
        try
        {
            if (string.IsNullOrWhiteSpace(productHermesHome)
                || !Path.IsPathFullyQualified(productHermesHome))
            {
                return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
            }
            home = Path.GetFullPath(productHermesHome);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
        }

        var environmentPath = Path.Combine(home, ".env");
        if (!File.Exists(environmentPath))
            return LilithRuntimeProviderBridgeAdmission.Accepted;

        byte[]? buffer = null;
        try
        {
            var information = new FileInfo(environmentPath);
            if (information.Length is < 0 or > MaximumEnvironmentFileBytes
                || information.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
            }

            using var stream = new FileStream(
                environmentPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            buffer = new byte[4096];
            var prefix = new List<byte>();
            var skipLine = false;
            var firstBytes = true;
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                var index = 0;
                if (firstBytes)
                {
                    firstBytes = false;
                    if (read >= 3
                        && buffer[0] == 0xEF
                        && buffer[1] == 0xBB
                        && buffer[2] == 0xBF)
                    {
                        index = 3;
                    }
                }
                for (; index < read; index++)
                {
                    var value = buffer[index];
                    if (value is (byte)'\r' or (byte)'\n')
                    {
                        var admission = InspectPrefix(prefix, skipLine);
                        if (admission != LilithRuntimeProviderBridgeAdmission.Accepted)
                            return admission;
                        prefix.Clear();
                        skipLine = false;
                        continue;
                    }
                    if (skipLine)
                        continue;
                    if (value == (byte)'=')
                    {
                        var admission = InspectPrefix(prefix, skipLine: false);
                        if (admission != LilithRuntimeProviderBridgeAdmission.Accepted)
                            return admission;
                        prefix.Clear();
                        skipLine = true;
                        continue;
                    }
                    if (prefix.Count >= MaximumKeyPrefixBytes)
                        return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
                    prefix.Add(value);
                    if (FirstNonWhitespaceIsComment(prefix))
                    {
                        prefix.Clear();
                        skipLine = true;
                    }
                }
            }
            return InspectPrefix(prefix, skipLine);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or SecurityException)
        {
            return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
        }
        finally
        {
            if (buffer is not null)
                CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static LilithRuntimeProviderBridgeAdmission InspectPrefix(
        IReadOnlyCollection<byte> prefix,
        bool skipLine)
    {
        if (skipLine || prefix.Count == 0)
            return LilithRuntimeProviderBridgeAdmission.Accepted;
        var bytes = prefix.ToArray();
        try
        {
            if (bytes.Any(value => value > 0x7F || value == 0))
                return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
            var text = Encoding.ASCII.GetString(bytes).Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                return LilithRuntimeProviderBridgeAdmission.Accepted;
            if (text.StartsWith("export ", StringComparison.Ordinal))
                text = text[7..].Trim();
            if (text.Length == 0 || !IsEnvironmentName(text))
                return LilithRuntimeProviderBridgeAdmission.HermesHomeRejected;
            return LilithHermesEnvironmentKeys.IsReserved(text)
                ? LilithRuntimeProviderBridgeAdmission
                    .ReservedEnvironmentCollision
                : LilithRuntimeProviderBridgeAdmission.Accepted;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool FirstNonWhitespaceIsComment(IReadOnlyList<byte> prefix)
    {
        foreach (var value in prefix)
        {
            if (value is (byte)' ' or (byte)'\t')
                continue;
            return value == (byte)'#';
        }
        return false;
    }

    private static bool IsEnvironmentName(string value)
    {
        if (value.Length == 0
            || !(value[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_'))
        {
            return false;
        }
        return value.Skip(1).All(character =>
            character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_');
    }
}
