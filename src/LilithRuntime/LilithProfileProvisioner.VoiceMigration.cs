using System.Text;

namespace LilithRuntime;

internal static partial class LilithProfileProvisioner
{
    private sealed record VoicePathCanonicalMigration(
        string PreviousCanonicalSha256, string CurrentPathCanonicalSha256, string PreviousModelPath);

    private static IReadOnlyList<VoicePathCanonicalMigration> BuildSupportedVoicePathMigrations(
        LilithRuntimeOptions options, IReadOnlyList<byte[]> canonicalConfigs)
    {
        // Only known version siblings of this installation are candidates. Do not
        // derive an accepted canonical hash from receipt data or an arbitrary old
        // config/model path. No old component needs to remain on disk after update.
        var root = Path.TrimEndingDirectorySeparator(options.VoiceComponentRootPath);
        var parent = Path.GetDirectoryName(root);
        if (Path.GetFileName(root) != LilithRuntimeOptions.PinnedVoiceComponentVersion
            || parent is null || Path.GetFileName(parent) != "voice"
            || Path.GetFileName(Path.GetDirectoryName(parent)) != "Components")
            return [];
        var migrations = new List<VoicePathCanonicalMigration>();
        foreach (var version in new[] { PinnedHermesVoiceLayout.LegacyVersion, PinnedHermesVoiceLayout.PreviousVersion })
        {
            var previousOptions = options with { VoiceComponentRootPath = Path.Combine(parent, version) };
            foreach (var current in canonicalConfigs)
            {
                var previous = MigratePackagedVoiceModelDefault(current, options.VoiceModelPath, previousOptions.VoiceModelPath);
                if (current.AsSpan().SequenceEqual(previous))
                    throw new LilithRuntimeException("The pinned voice-path canonical candidate was invalid.");
                migrations.Add(new(Sha256(previous), Sha256(current), previousOptions.VoiceModelPath));
            }
        }
        return migrations;
    }

    internal static byte[] MigratePackagedVoiceModelDefault(byte[] config, string previousModelPath, string currentModelPath)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(config); }
        catch (DecoderFallbackException)
        { throw new LilithRuntimeException("The voice-path config was not strict UTF-8; migration refused."); }
        var newline = RequireConsistentLineEnding(text);
        var lines = text.Split(newline, StringSplitOptions.None);
        var stt = FindUniqueKey(0, lines.Length, 0, "stt");
        if (stt < 0) return config; // A verified native mutation may remove STT.
        RequireMapping(stt, "stt");
        var local = FindUniqueKey(stt + 1, SectionEnd(stt, 0), 2, "local");
        if (local < 0) return config;
        RequireMapping(local, "local");
        var model = FindUniqueKey(local + 1, SectionEnd(local, 2), 4, "model");
        if (model < 0) return config;
        var prefix = "    model:";
        if (!lines[model].StartsWith(prefix, StringComparison.Ordinal))
            throw new LilithRuntimeException("The voice-path model key was ambiguous; migration refused.");
        var suffix = lines[model][prefix.Length..];
        var leading = suffix.Length - suffix.TrimStart().Length;
        var scalar = suffix[leading..];
        // Match only a complete, exact old default scalar. Preserve comments,
        // line endings and every other byte, including custom model selections.
        // The native YAML writer emits plain or single-quoted Windows paths.
        foreach (var oldScalar in new[] { YamlSingleQuoted(previousModelPath), previousModelPath,
            System.Text.Json.JsonSerializer.Serialize(previousModelPath) })
        {
            if (!scalar.StartsWith(oldScalar, StringComparison.Ordinal)) continue;
            var tail = scalar[oldScalar.Length..];
            if (tail.Length != 0 && (!char.IsWhiteSpace(tail[0])
                || (tail.TrimStart().Length != 0 && !tail.TrimStart().StartsWith('#')))) continue;
            lines[model] = prefix + suffix[..leading] + YamlSingleQuoted(currentModelPath) + tail;
            return Encoding.UTF8.GetBytes(string.Join(newline, lines));
        }
        return config;

        int FindUniqueKey(int start, int end, int indent, string key)
        {
            var matches = Enumerable.Range(start, end - start).Where(index =>
                lines[index].TakeWhile(char.IsWhiteSpace).Count() == indent
                && lines[index].TrimStart().Split(':', 2)[0].Trim().Trim('\'', '"') == key).ToArray();
            if (matches.Length > 1)
                throw new LilithRuntimeException("The voice-path config key was ambiguous; migration refused.");
            return matches.Length == 0 ? -1 : matches[0];
        }
        int SectionEnd(int start, int indent)
        {
            var end = start + 1;
            while (end < lines.Length && (string.IsNullOrWhiteSpace(lines[end]) || lines[end].TrimStart().StartsWith('#')
                || lines[end].TakeWhile(char.IsWhiteSpace).Count() > indent)) end++;
            return end;
        }
        void RequireMapping(int index, string key)
        {
            if (lines[index].Split('#', 2)[0].Trim() != key + ":")
                throw new LilithRuntimeException("The voice-path config mapping was unsupported; migration refused.");
        }
    }
}
