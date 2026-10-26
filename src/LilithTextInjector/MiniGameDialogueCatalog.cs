using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace LilithTextInjector;

// Original MOD dialogue only. This catalog never probes, replaces or supplies
// an official recorded dialogue clip. Event admission belongs to the caller.
internal sealed class MiniGameDialogueLine
{
    internal string Id { get; }
    internal string Zh { get; }
    internal string Ja { get; }
    internal string Direction { get; }
    internal string ProposedTrigger { get; }
    private readonly MiniGameDialogueAudio chinese;
    private readonly MiniGameDialogueAudio japanese;

    internal MiniGameDialogueLine(string id, string zh, string ja, string direction,
        string proposedTrigger, MiniGameDialogueAudio chinese, MiniGameDialogueAudio japanese)
    {
        Id = id; Zh = zh; Ja = ja; Direction = direction; ProposedTrigger = proposedTrigger;
        this.chinese = chinese; this.japanese = japanese;
    }

    internal MiniGameDialogueAudio Audio(bool japanese) => japanese ? this.japanese : chinese;
}

internal sealed class MiniGameDialogueAudio
{
    internal const int MaximumEncodedBytes = 44 + 32000 * 2 * 30;
    internal string RelativePath { get; }
    internal string Sha256 { get; }
    internal int ByteLength { get; }
    internal double DurationSeconds { get; }
    internal int SampleRate { get; }
    internal int Channels { get; }
    internal int BitsPerSample { get; }
    internal string Emotion { get; }
    internal bool HumanListeningAccepted { get; }
    internal bool ReusedAcceptedAudition { get; }

    internal MiniGameDialogueAudio(string relativePath, string sha256, int byteLength,
        double durationSeconds, int sampleRate, int channels, int bitsPerSample,
        string emotion, bool humanListeningAccepted, bool reusedAcceptedAudition)
    {
        RelativePath = relativePath; Sha256 = sha256; ByteLength = byteLength;
        DurationSeconds = durationSeconds; SampleRate = sampleRate; Channels = channels;
        BitsPerSample = bitsPerSample; Emotion = emotion;
        HumanListeningAccepted = humanListeningAccepted; ReusedAcceptedAudition = reusedAcceptedAudition;
    }

    // Only the reviewed, canonical 44-byte PCM WAV layout is admitted. No codec,
    // resampler, float WAV, ancillary chunk, redirected file or provider fallback.
    // Allocation is bounded by catalog identity and an independent hard ceiling.
    internal float[] ReadPcm16(string packDirectory)
    {
        if (!Path.IsPathFullyQualified(packDirectory))
            throw new InvalidDataException("Mini-game voice directory must be absolute.");
        var directory = Path.GetFullPath(packDirectory);
        var path = Path.GetFullPath(Path.Combine(directory, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mini-game voice path rejected.");
        for (string? probe = path; !string.IsNullOrEmpty(probe); probe = Path.GetDirectoryName(probe))
            if ((File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Redirected mini-game voice path rejected.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (ByteLength is < 44 or > MaximumEncodedBytes || stream.Length != ByteLength)
            throw new InvalidDataException("Mini-game voice encoded size differs.");
        var bytes = new byte[ByteLength];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
        return DecodePcm16(bytes);
    }

    internal float[] DecodePcm16(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength || ByteLength is < 44 or > MaximumEncodedBytes
            || (ByteLength - 44) % 2 != 0 || SampleRate != 32000 || Channels != 1 || BitsPerSample != 16
            || !double.IsFinite(DurationSeconds) || DurationSeconds is < 0.3 or > 30
            || Math.Abs((ByteLength - 44) / 64000d - DurationSeconds) > 0.000001)
            throw new InvalidDataException("Mini-game voice shape differs.");
        if (U32(bytes, 0) != 0x46464952 || U32(bytes, 4) != bytes.Length - 8
            || U32(bytes, 8) != 0x45564157 || U32(bytes, 12) != 0x20746D66
            || U32(bytes, 16) != 16 || U16(bytes, 20) != 1 || U16(bytes, 22) != 1
            || U32(bytes, 24) != 32000 || U32(bytes, 28) != 64000
            || U16(bytes, 32) != 2 || U16(bytes, 34) != 16
            || U32(bytes, 36) != 0x61746164 || U32(bytes, 40) != bytes.Length - 44)
            throw new InvalidDataException("Mini-game voice PCM header differs.");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Mini-game voice identity differs.");
        var samples = new float[(bytes.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = unchecked((short)U16(bytes, 44 + i * 2)) / 32768f;
        return samples;
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)(bytes[offset] | bytes[offset + 1] << 8);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
        (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
}

internal static class MiniGameDialogueCatalog
{
    internal const string ResourceName = "LilithTextInjector.MiniGameDialogue.catalog.json";
    internal const int MaximumCatalogBytes = 512 * 1024;
    private static readonly Lazy<IReadOnlyList<MiniGameDialogueLine>> lines = new(Load);
    internal static IReadOnlyList<MiniGameDialogueLine> All => lines.Value;
    internal static MiniGameDialogueLine? Get(string id) =>
        All.FirstOrDefault(line => string.Equals(line.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<MiniGameDialogueLine> Load()
    {
        using var stream = typeof(MiniGameDialogueCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("Mini-game dialogue catalog resource missing.");
        if (stream.Length is <= 0 or > MaximumCatalogBytes)
            throw new InvalidDataException("Mini-game dialogue catalog size rejected.");
        var bytes = new byte[checked((int)stream.Length)];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
        return Parse(bytes);
    }

    internal static IReadOnlyList<MiniGameDialogueLine> Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is <= 0 or > MaximumCatalogBytes)
            throw new InvalidDataException("Mini-game dialogue catalog size rejected.");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = json.RootElement;
        UniqueProperties(root);
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || root.GetProperty("numericEmotionIntensityApplied").GetBoolean())
            throw new InvalidDataException("Unsupported mini-game dialogue catalog.");
        foreach (var name in new[] { "reviewDocumentSha256", "sourceManifestSha256", "sourceCatalogSha256" })
            RequireHash(Text(root, name, 64));
        // The original 180-pair approval is not evidence for new recordings.
        // This separately approved extension is closed to exactly two IDs.
        if (!root.TryGetProperty("extensions", out var extensions)
            || extensions.ValueKind != JsonValueKind.Array || extensions.GetArrayLength() != 1)
            throw new InvalidDataException("Mini-game dialogue extension provenance missing.");
        var extension = extensions[0];
        UniqueProperties(extension);
        foreach (var name in new[] { "reviewDocumentSha256", "sourceManifestSha256", "sourceCatalogSha256" })
            RequireHash(Text(extension, name, 64));
        if (!extension.TryGetProperty("ids", out var extensionIds) || extensionIds.ValueKind != JsonValueKind.Array
            || extensionIds.GetArrayLength() != 2 || extensionIds[0].GetString() != "GD-OM073"
            || extensionIds[1].GetString() != "GD-OM074")
            throw new InvalidDataException("Mini-game dialogue extension scope rejected.");
        var source = root.GetProperty("lines");
        if (source.ValueKind != JsonValueKind.Array || source.GetArrayLength() != 182)
            throw new InvalidDataException("Mini-game dialogue catalog incomplete.");
        var result = new List<MiniGameDialogueLine>(182);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in source.EnumerateArray())
        {
            UniqueProperties(row);
            var id = Text(row, "id", 8);
            if (!ValidId(id) || !ids.Add(id)) throw new InvalidDataException("Invalid mini-game dialogue identity.");
            var audio = row.GetProperty("audio");
            UniqueProperties(audio);
            if (audio.EnumerateObject().Count() != 2)
                throw new InvalidDataException("Invalid mini-game dialogue languages.");
            result.Add(new MiniGameDialogueLine(id, Text(row, "zh", 512), Text(row, "ja", 512),
                Text(row, "direction", 512), Text(row, "proposedTrigger", 512),
                ParseAudio(id, "zh", audio.GetProperty("zh")), ParseAudio(id, "ja", audio.GetProperty("ja"))));
        }
        return new ReadOnlyCollection<MiniGameDialogueLine>(result);
    }

    private static MiniGameDialogueAudio ParseAudio(string id, string language, JsonElement value)
    {
        UniqueProperties(value);
        var path = Text(value, "relativePath", 32);
        if (path != language + "/" + id + ".wav") throw new InvalidDataException("Mini-game voice path rejected.");
        var hash = Text(value, "sha256", 64); RequireHash(hash);
        var size = value.GetProperty("byteLength").GetInt32();
        var seconds = value.GetProperty("durationSeconds").GetDouble();
        var rate = value.GetProperty("sampleRate").GetInt32();
        var channels = value.GetProperty("channels").GetInt32();
        var bits = value.GetProperty("bitsPerSample").GetInt32();
        var emotion = Text(value, "emotion", 16);
        if (size is < 44 or > MiniGameDialogueAudio.MaximumEncodedBytes || (size - 44) % 2 != 0
            || !double.IsFinite(seconds) || seconds is < 0.3 or > 30
            || Math.Abs((size - 44) / 64000d - seconds) > 0.000001
            || rate != 32000 || channels != 1 || bits != 16
            || emotion is not ("calm" or "excited" or "wronged" or "gentle" or "playful" or "relieved" or "surprised"))
            throw new InvalidDataException("Mini-game voice metadata rejected.");
        var accepted = value.GetProperty("humanListeningAccepted").GetBoolean();
        var reused = value.GetProperty("reusedAcceptedAudition").GetBoolean();
        if (reused && !accepted) throw new InvalidDataException("Mini-game audition provenance differs.");
        return new MiniGameDialogueAudio(path, hash, size, seconds, rate, channels, bits, emotion, accepted, reused);
    }

    private static bool ValidId(string id)
    {
        var prefix = id.StartsWith("GD-S", StringComparison.Ordinal) ? "GD-S"
            : id.StartsWith("GD-OM", StringComparison.Ordinal) ? "GD-OM"
            : id.StartsWith("GD-CH", StringComparison.Ordinal) ? "GD-CH" : string.Empty;
        if (prefix.Length == 0 || id.Length != prefix.Length + 3) return false;
        var digits = id.AsSpan(prefix.Length);
        foreach (var character in digits) if (character is < '0' or > '9') return false;
        return int.TryParse(digits, out var number) && number >= 1
            && number <= (prefix == "GD-S" ? 24 : prefix == "GD-OM" ? 74 : 84);
    }

    private static string Text(JsonElement element, string name, int maximum)
    {
        var text = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl))
            throw new InvalidDataException("Invalid mini-game dialogue text.");
        return text;
    }

    private static void RequireHash(string hash)
    {
        if (hash.Length != 64 || hash.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("Invalid mini-game voice hash.");
    }

    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid mini-game catalog object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate mini-game catalog property.");
    }
}
