using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

// V1–V8 remain frozen packages and compatibility snapshots. V9 adds reviewed speech
// examples and concise conversational guidance, never Canon or the player-memory root.
internal static class LilithIdentityPackageV9Loader
{
    internal const string PackageId = "lilith-identity-package-v9";
    internal const int PackageVersion = 9;
    internal const string ManifestFileName = "lilith-identity-package.v9.json";
    internal const string ManifestSha256 = "19C66E9758D839961C9F32D12EB2430BFEB1424F5C4D0BA55EA96888D81DB66C";
    internal const string IdentityFingerprintSha256 = "CDC1DD4F53596F2E0F214C1C9CE4474BEC97634CF3CBD8A10A026FB62318DBCF";
    internal const string SoulAssetSha256 = "08DF982FA7EAFB9A98CA5AA13B1E97325879ECFF7FAE88B06349655139ED799B";
    internal const string HermesPersonalityContentSha256 = "4F61737587D437325618C85262C70DBF11028BE9D411DBB9772FF601B5AF1135";
    internal const string CompositionMode = "SpeechExamplesWithTulpaOriginalExperiencesAndOneActiveLocaleV9";
    internal const string LocaleActivation =
        "This versioned identity profile explicitly enables ConversationAdaptive for its Hermes Workbench. "
        + "Apply exactly one locale section using the operating contract's display-language precedence. "
        + "The other sections are inactive references, not additional voices. "
        + "Do not blend, recite or translate their instructions. Voice language remains independent.\n";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (string File, string Sha256)[] Assets =
    {
        ("SOUL.lilith.v9.md", "08DF982FA7EAFB9A98CA5AA13B1E97325879ECFF7FAE88B06349655139ED799B"),
        ("personality.lilith.v9.md", "83DF3CC521B8FBF4D4C71918EA0796127AF1CDF6D03BADFB06F6AEA49E6BE32F"),
        ("operating-contract.lilith.v9.md", "58C05303B80A98D7666009CD68DCA7737AA7B840975D901099A5AF183C252A5A"),
        ("worldview.tulpa.lilith.v9.md", "4062823C0F9F163504F3CBF4DBFB6C58B1F17575720F0665FBD78B4E5A8DFA78"),
        ("experiences.original.lilith.v9.md", "849DCE1768FE41FA7F491E258D024B804F4E69EEA66EEEDC1044AEAA0176B3C8"),
        ("speech.original-examples.lilith.v9.md", "0F30E6EDED4F1A2C42CF3A5ABF1E8734A637809324B2DF00282F5E9398E9258F"),
        ("speech.mod-examples.lilith.v9.md", "AA67E0F5BF383000F9DB5127786B4774C266BD43DD5ADC5697C8289BF946DCF6"),
        ("voice.zh-Hant-TW.v9.md", "E00EF31C6A45FE3F6E8D882B3021ED73E36FF89505EF4FB91458AC059C800767"),
        ("voice.zh-Hans-CN.v9.md", "4078776A3B05F49A2932B1D5B206AB71E63FA16BEA8F4A855FA98425B0234FE0"),
        ("voice.ja-JP.v9.md", "AAF7E98FF9237741100736C35A094D63741B27392A701E334CFB78BA6CC16266"),
        ("voice.en.v9.md", "59B581454A9CC78B912D680AA2259FCEE646E831C0D1DA3D7F571490FC74A27A"),
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(root);
        var manifestBytes = ReadPinned(root, ManifestFileName, ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 9
            || manifest.GetProperty("packageId").GetString() != PackageId
            || manifest.GetProperty("packageVersion").GetInt32() != PackageVersion
            || manifest.GetProperty("composition").GetString() != CompositionMode
            || manifest.GetProperty("canonSemanticHash").GetString() != LilithIdentityPackageV1Loader.CanonSemanticHash)
            throw Invalid("The sealed manifest does not describe the accepted v9 projection.");

        var expectedFiles = Assets.Select(asset => asset.File).Append(ManifestFileName)
            .Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal)
            || Directory.GetDirectories(root).Length != 0)
            throw Invalid("The v9 asset set differs from its exact sealed file set.");

        var text = Assets.ToDictionary(asset => asset.File,
            asset => Utf8.GetString(ReadPinned(root, asset.File, asset.Sha256)), StringComparer.Ordinal);
        var personality = Compose(text);
        if (Hash(Utf8.GetBytes(personality)) != HermesPersonalityContentSha256)
            throw Invalid("The composed v9 personality differs from its pinned digest.");

        var material = new List<string>
        {
            $"package:{PackageId}@{PackageVersion}",
            $"baseline-identity:{LilithIdentityPackageV8Loader.IdentityFingerprintSha256}",
            $"canon:{LilithIdentityPackageV1Loader.CanonProfileId}@{LilithIdentityPackageV1Loader.CanonProfileVersion}",
            $"canon-sha256:{LilithIdentityPackageV1Loader.CanonSemanticHash}",
            $"composition:{CompositionMode}"
        };
        material.AddRange(Assets.Select(asset => $"asset:{asset.File}:{asset.Sha256}"));
        material.Add($"personality-composed-sha256:{HermesPersonalityContentSha256}");
        var recordedMaterial = manifest.GetProperty("identityFingerprintMaterial")
            .EnumerateArray().Select(item => item.GetString()).ToArray();
        if (!material.SequenceEqual(recordedMaterial, StringComparer.Ordinal)
            || LilithIdentityPackageV1Loader.ComputeFingerprint(material) != IdentityFingerprintSha256
            || manifest.GetProperty("identityFingerprintSha256").GetString() != IdentityFingerprintSha256)
            throw Invalid("The v9 identity fingerprint material differs from its pinned identity.");

        var voices = new Dictionary<string, LilithVoiceProjectionV1>(StringComparer.Ordinal);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            var file = $"voice.{locale}.v9.md";
            voices.Add(locale, new LilithVoiceProjectionV1(locale,
                $"lilith-voice-{locale}-v9", locale, file,
                Assets.Single(asset => asset.File == file).Sha256,
                Array.AsReadOnly(new[] { new LilithVoiceInstructionV1("voice.expression.v9", text[file]) })));
        }
        return new LilithIdentityPackageV1(PackageId, PackageVersion, IdentityFingerprintSha256,
            LilithIdentityPackageV1Loader.CanonProfileId, LilithIdentityPackageV1Loader.CanonProfileVersion,
            LilithIdentityPackageV1Loader.CanonSemanticHash,
            "lilith-soul-projection-v9", Assets[0].File, SoulAssetSha256, text[Assets[0].File],
            "lilith-voice-contract-v9", 9, new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(voices),
            "lilith-hermes-personality-v9", Assets[1].File, Assets[1].Sha256,
            personality, HermesPersonalityContentSha256, ManifestSha256);
    }

    private static string Compose(IReadOnlyDictionary<string, string> text)
    {
        var builder = new StringBuilder(text["worldview.tulpa.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["experiences.original.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["personality.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["speech.original-examples.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["speech.mod-examples.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["operating-contract.lilith.v9.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n## Locale reference sections\n\n").Append(LocaleActivation);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            builder.Append("\n### Active locale `").Append(locale).Append("`\n\n")
                .Append(text[$"voice.{locale}.v9.md"].TrimEnd('\r', '\n')).Append('\n');
        }
        return builder.ToString();
    }

    private static byte[] ReadPinned(string root, string file, string expectedHash)
    {
        var path = Path.Combine(root, file);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw Invalid("A required v9 identity asset is missing or outside its size bound.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
                || Utf8.GetString(bytes).Contains('\r') || Hash(bytes) != expectedHash)
                throw Invalid("A v9 identity asset differs from its reviewed UTF-8 bytes.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("A v9 identity asset contains invalid UTF-8.");
        }
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException Invalid(string message)
        => new("The immutable Lilith v9 identity package was rejected. " + message);
}
