using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithGameBridgeContracts;
using LilithRuntime.Configuration;

namespace LilithRuntime;

// Child-start voice assets with a read-only selector pointing to the one existing
// Control Center store. Each reply reads that selector; no second preference
// store, voice service, language change, TTS call or Unity entry is created here.
internal static class LilithTelegramVoiceProfileProjection
{
    internal const string EnvironmentVariable = "LILITH_TELEGRAM_VOICE_PROFILE";
    internal const int MaximumConfigurationBytes = 256 * 1024;
    internal const string ConfigurationFileName = "community.lilith.textinjector.cfg";
    private const int MaximumLineCharacters = 4096;
    private const long MaximumReferenceBytes = 32L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ApplyToChildEnvironment(
        ProcessStartInfo startInfo,
        Func<string, string?>? readEnvironment = null,
        Func<D046GameControlPreferencesStore>? createPreferencesStore = null)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        // Never inherit a profile supplied by a parent shell or prior generation.
        startInfo.Environment.Remove(EnvironmentVariable);
        try
        {
            var read = readEnvironment ?? Environment.GetEnvironmentVariable;
            var manifestPath = read(LilithGameBridgeTrustManifest.ManifestPathEnvironmentVariable);
            var manifestSha256 = read(LilithGameBridgeTrustManifest.ManifestSha256EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(manifestSha256))
                return;

            var store = (createPreferencesStore ?? D046GameControlPreferencesStore.ForCurrentUser)();
            RequireSafeLocalPath(store.StorePath);
            using var preferencesLease = new FileStream(store.StorePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (preferencesLease.Length is <= 0 or > D046GameControlPreferencesStore.MaximumStoreBytes)
                return;
            var controls = store.Load();
            RequireSafeLocalPath(store.StorePath);
            if (TryCreateCatalog(manifestPath, manifestSha256, store.StorePath, controls, out var encodedProfile))
                startInfo.Environment[EnvironmentVariable] = encodedProfile;
        }
        catch (Exception)
        {
            // Optional delivery must not prevent ordinary Runtime startup. Do not
            // log configuration text, reference paths or incidental player data.
            startInfo.Environment.Remove(EnvironmentVariable);
        }
    }

    internal static bool TryCreateCatalog(
        string manifestPath,
        string expectedManifestSha256,
        string preferencesPath,
        D046GameControlLoadResult controls,
        out string encodedCatalog,
        Func<string, FileAttributes?>? readAttributes = null)
    {
        encodedCatalog = string.Empty;
        try
        {
            RequireSafeLocalPath(preferencesPath, readAttributes);
            if (!string.Equals(Path.GetFileName(preferencesPath),
                    D046GameControlPreferencesStore.StoreFileName, StringComparison.Ordinal)
                || controls.Disposition is not (D046GameControlLoadDisposition.Ready
                    or D046GameControlLoadDisposition.PreviousSchemaMigrated)
                || !Enum.IsDefined(controls.Snapshot.VoiceLanguage))
                return false;

            JsonElement? Profile(D046GameVoiceLanguage language)
            {
                var selected = controls.Snapshot.Copy();
                selected.VoiceLanguage = language;
                if (!TryCreate(manifestPath, expectedManifestSha256,
                        new D046GameControlLoadResult(selected, controls.Disposition),
                        out var encoded, readAttributes))
                    return null;
                using var decoded = JsonDocument.Parse(Convert.FromBase64String(encoded));
                return decoded.RootElement.Clone();
            }

            var chinese = Profile(D046GameVoiceLanguage.TraditionalChinese);
            var japanese = Profile(D046GameVoiceLanguage.Japanese);
            if (chinese is null && japanese is null)
                return false;
            RequireSafeLocalPath(preferencesPath, readAttributes);
            encodedCatalog = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = 2,
                preferences_path = Path.GetFullPath(preferencesPath),
                profiles = new { zh = chinese, ja = japanese }
            }));
            return true;
        }
        catch (Exception)
        {
            encodedCatalog = string.Empty;
            return false;
        }
    }

    internal static bool TryCreate(
        string manifestPath,
        string expectedManifestSha256,
        D046GameControlLoadResult controls,
        out string encodedProfile,
        Func<string, FileAttributes?>? readAttributes = null)
    {
        encodedProfile = string.Empty;
        try
        {
            if (controls.Disposition is not (D046GameControlLoadDisposition.Ready
                or D046GameControlLoadDisposition.PreviousSchemaMigrated)
                || !Enum.IsDefined(controls.Snapshot.VoiceLanguage)
                || expectedManifestSha256.Length != 64
                || expectedManifestSha256.Any(character => !Uri.IsHexDigit(character)))
                return false;

            var manifestBytes = ReadBoundedFile(manifestPath, LilithGameBridgeTrustManifest.MaximumManifestBytes, readAttributes);
            if (!CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(manifestBytes), Convert.FromHexString(expectedManifestSha256)))
                return false;
            var manifest = LilithGameBridgeTrustManifest.Decode(manifestBytes);
            RequireSafeLocalPath(manifest.GameRoot, readAttributes);
            if (!Directory.Exists(manifest.GameRoot))
                return false;

            var configurationPath = Path.Combine(manifest.GameRoot, "BepInEx", "config", ConfigurationFileName);
            var configurationBytes = ReadBoundedFile(configurationPath, MaximumConfigurationBytes, readAttributes);
            try
            {
                var configuration = ParseVoiceConfiguration(configurationBytes);
                var japanese = controls.Snapshot.VoiceLanguage == D046GameVoiceLanguage.Japanese;
                var section = japanese ? "JapaneseVoice" : "Voice";
                var endpoint = Required(configuration, section, "Endpoint");
                var expectedEndpoint = japanese ? "http://127.0.0.1:9881/tts" : "http://127.0.0.1:9880/tts";
                if (!string.Equals(endpoint, expectedEndpoint, StringComparison.Ordinal))
                    return false;

                var referencePath = ResolveReference(manifest.GameRoot,
                    Required(configuration, section, japanese ? "CalmReferencePath" : "ReferencePath"), readAttributes);
                var auxiliary = Array.Empty<string>();
                if (japanese)
                {
                    var auxiliaryPath = Required(configuration, section, "CalmAuxReferencePath", allowEmpty: true);
                    if (auxiliaryPath.Length != 0)
                        auxiliary = new[] { ResolveReference(manifest.GameRoot, auxiliaryPath, readAttributes) };
                }
                var locale = japanese ? "ja" : "zh";
                var prompt = japanese
                    ? "これは儀式でもあるの。君に私の存在を感じてもらうための儀式ね。"
                    : "你的選擇創造了我，所以我的存在本身就是你的善意。";
                var snapshot = new
                {
                    version = 1,
                    locale,
                    endpoint,
                    reference_path = referencePath,
                    aux_reference_paths = auxiliary,
                    prompt_text = prompt
                };
                // Hash only the selected voice fields. Unrelated configuration
                // values never enter the child payload or its fingerprint.
                var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));
                encodedProfile = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    snapshot.version,
                    snapshot.locale,
                    snapshot.endpoint,
                    snapshot.reference_path,
                    snapshot.aux_reference_paths,
                    snapshot.prompt_text,
                    config_fingerprint = fingerprint
                }));
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(configurationBytes);
            }
        }
        catch (Exception)
        {
            encodedProfile = string.Empty;
            return false;
        }
    }

    private static Dictionary<string, string> ParseVoiceConfiguration(byte[] bytes)
    {
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        using var reader = new StringReader(StrictUtf8.GetString(bytes, offset, bytes.Length - offset));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        while (reader.ReadLine() is { } rawLine)
        {
            if (rawLine.Contains('\0'))
                throw new InvalidDataException("Voice configuration line was rejected.");
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']'))
                    throw new InvalidDataException("Voice configuration section was rejected.");
                section = line[1..^1].Trim();
                if (string.Equals(section, "Voice", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(section, "JapaneseVoice", StringComparison.OrdinalIgnoreCase))
                {
                    if (section is not ("Voice" or "JapaneseVoice") || !seenSections.Add(section))
                        throw new InvalidDataException("Voice configuration section was ambiguous.");
                }
                continue;
            }
            if (section is not ("Voice" or "JapaneseVoice"))
                continue;
            // Other game sections can contain long persona/configuration lines.
            // They are not voice authority and must not disable this projection.
            // The entire file is already bounded; constrain voice entries here.
            if (rawLine.Length > MaximumLineCharacters)
                throw new InvalidDataException("Voice configuration line was rejected.");
            var separator = line.IndexOf('=');
            if (separator <= 0)
                throw new InvalidDataException("Voice configuration entry was rejected.");
            var key = line[..separator].Trim();
            var qualified = section + "/" + key;
            if (!seenKeys.Add(qualified))
                throw new InvalidDataException("Voice configuration entry was ambiguous.");
            if (key is "Endpoint" or "ReferencePath" or "CalmReferencePath" or "CalmAuxReferencePath")
                values.Add(qualified, line[(separator + 1)..].Trim());
        }
        return values;
    }

    private static string Required(Dictionary<string, string> values, string section, string key, bool allowEmpty = false)
    {
        if (!values.TryGetValue(section + "/" + key, out var value)
            || (!allowEmpty && value.Length == 0))
            throw new InvalidDataException("A required voice configuration entry was unavailable.");
        return value;
    }

    private static string ResolveReference(string gameRoot, string configuredPath,
        Func<string, FileAttributes?>? readAttributes)
    {
        if (configuredPath.Length > 1024 || configuredPath.Any(char.IsControl))
            throw new InvalidDataException("Voice reference path was rejected.");
        var path = Path.IsPathFullyQualified(configuredPath)
            ? configuredPath
            : Path.Combine(gameRoot, configuredPath);
        path = Path.GetFullPath(path);
        var rootPrefix = Path.TrimEndingDirectorySeparator(gameRoot) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Voice reference escaped the installed game assets.");
        RequireSafeLocalPath(path, readAttributes);
        using var reference = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (reference.Length is <= 0 or > MaximumReferenceBytes)
            throw new InvalidDataException("Voice reference exceeded its bound.");
        RequireSafeLocalPath(path, readAttributes);
        return path;
    }

    private static byte[] ReadBoundedFile(string path, int maximumBytes,
        Func<string, FileAttributes?>? readAttributes)
    {
        RequireSafeLocalPath(path, readAttributes);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("Voice profile input exceeded its bound.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("Voice profile input changed during its read.");
        RequireSafeLocalPath(path, readAttributes);
        return bytes;
    }

    private static void RequireSafeLocalPath(string path,
        Func<string, FileAttributes?>? readAttributes = null)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal) || path.Any(char.IsControl))
            throw new InvalidDataException("Voice profile path was not local and absolute.");
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (fullPath[root.Length..].Contains(':'))
            throw new InvalidDataException("Voice profile path contained an alternate stream.");
        LilithRuntimeWorkspacePathGuard.EnsureSafe(fullPath, readAttributes);
    }
}
