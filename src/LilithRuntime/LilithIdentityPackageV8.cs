using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

// V1/V2/V3/V4/V5/V6/V7 remain frozen packages and compatibility snapshots. V8 changes only
// the reviewed story-backed role projection, never Canon or the player-memory root.
internal static class LilithIdentityPackageV8Loader
{
    internal const string PackageId = "lilith-identity-package-v8";
    internal const int PackageVersion = 8;
    internal const string ManifestFileName = "lilith-identity-package.v8.json";
    internal const string ManifestSha256 = "4EA7737D7094953010CA19008935FA08AAA4F073A0130A954501D6D1A19E4526";
    internal const string IdentityFingerprintSha256 = "485422365E49FE32DF141C03BF6F2BDB860942A8DB673CDDC10A60B461E2A2E4";
    internal const string SoulAssetSha256 = "C00A9D1E115721CC4C26630825B87CEFE742A87C6604AE7213333EAAF41D2BD3";
    internal const string HermesPersonalityContentSha256 = "A8E30A53C5D29F889D978E5689EFE8024249D64332FBF747B7CD12E8A0956056";
    internal const string CompositionMode = "TulpaRoleWorldAndOriginalExperiencesWithOneActiveLocaleV8";
    internal const string LocaleActivation =
        "This versioned identity profile explicitly enables ConversationAdaptive for its Hermes Workbench. "
        + "Apply exactly one locale section using the operating contract's display-language precedence. "
        + "The other sections are inactive references, not additional voices. "
        + "Do not blend, recite or translate their instructions. Voice language remains independent.\n";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (string File, string Sha256)[] Assets =
    {
        ("SOUL.lilith.v8.md", "C00A9D1E115721CC4C26630825B87CEFE742A87C6604AE7213333EAAF41D2BD3"),
        ("personality.lilith.v8.md", "FB369D1F82DB9EBEE07D0871B0DB42C326553A191EFC6EB293348E5FF1DDC53E"),
        ("operating-contract.lilith.v8.md", "58C05303B80A98D7666009CD68DCA7737AA7B840975D901099A5AF183C252A5A"),
        ("worldview.tulpa.lilith.v8.md", "4062823C0F9F163504F3CBF4DBFB6C58B1F17575720F0665FBD78B4E5A8DFA78"),
        ("experiences.original.lilith.v8.md", "849DCE1768FE41FA7F491E258D024B804F4E69EEA66EEEDC1044AEAA0176B3C8"),
        ("voice.zh-Hant-TW.v8.md", "E00EF31C6A45FE3F6E8D882B3021ED73E36FF89505EF4FB91458AC059C800767"),
        ("voice.zh-Hans-CN.v8.md", "4078776A3B05F49A2932B1D5B206AB71E63FA16BEA8F4A855FA98425B0234FE0"),
        ("voice.ja-JP.v8.md", "AAF7E98FF9237741100736C35A094D63741B27392A701E334CFB78BA6CC16266"),
        ("voice.en.v8.md", "59B581454A9CC78B912D680AA2259FCEE646E831C0D1DA3D7F571490FC74A27A"),
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(root);
        var manifestBytes = ReadPinned(root, ManifestFileName, ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 8
            || manifest.GetProperty("packageId").GetString() != PackageId
            || manifest.GetProperty("packageVersion").GetInt32() != PackageVersion
            || manifest.GetProperty("composition").GetString() != CompositionMode
            || manifest.GetProperty("canonSemanticHash").GetString() != LilithIdentityPackageV1Loader.CanonSemanticHash)
            throw Invalid("The sealed manifest does not describe the accepted v8 projection.");

        var expectedFiles = Assets.Select(asset => asset.File).Append(ManifestFileName)
            .Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal)
            || Directory.GetDirectories(root).Length != 0)
            throw Invalid("The v8 asset set differs from its exact sealed file set.");

        var text = Assets.ToDictionary(asset => asset.File,
            asset => Utf8.GetString(ReadPinned(root, asset.File, asset.Sha256)), StringComparer.Ordinal);
        var personality = Compose(text);
        if (Hash(Utf8.GetBytes(personality)) != HermesPersonalityContentSha256)
            throw Invalid("The composed v8 personality differs from its pinned digest.");

        var material = new List<string>
        {
            $"package:{PackageId}@{PackageVersion}",
            $"baseline-identity:{LilithIdentityPackageV7Loader.IdentityFingerprintSha256}",
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
            throw Invalid("The v8 identity fingerprint material differs from its pinned identity.");

        var voices = new Dictionary<string, LilithVoiceProjectionV1>(StringComparer.Ordinal);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            var file = $"voice.{locale}.v8.md";
            voices.Add(locale, new LilithVoiceProjectionV1(locale,
                $"lilith-voice-{locale}-v8", locale, file,
                Assets.Single(asset => asset.File == file).Sha256,
                Array.AsReadOnly(new[] { new LilithVoiceInstructionV1("voice.expression.v8", text[file]) })));
        }
        return new LilithIdentityPackageV1(PackageId, PackageVersion, IdentityFingerprintSha256,
            LilithIdentityPackageV1Loader.CanonProfileId, LilithIdentityPackageV1Loader.CanonProfileVersion,
            LilithIdentityPackageV1Loader.CanonSemanticHash,
            "lilith-soul-projection-v8", Assets[0].File, SoulAssetSha256, text[Assets[0].File],
            "lilith-voice-contract-v8", 8, new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(voices),
            "lilith-hermes-personality-v8", Assets[1].File, Assets[1].Sha256,
            personality, HermesPersonalityContentSha256, ManifestSha256);
    }

    private static string Compose(IReadOnlyDictionary<string, string> text)
    {
        var builder = new StringBuilder(text["worldview.tulpa.lilith.v8.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["experiences.original.lilith.v8.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["personality.lilith.v8.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["operating-contract.lilith.v8.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n## Locale reference sections\n\n").Append(LocaleActivation);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            builder.Append("\n### Active locale `").Append(locale).Append("`\n\n")
                .Append(text[$"voice.{locale}.v8.md"].TrimEnd('\r', '\n')).Append('\n');
        }
        return builder.ToString();
    }

    private static byte[] ReadPinned(string root, string file, string expectedHash)
    {
        var path = Path.Combine(root, file);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw Invalid("A required v8 identity asset is missing or outside its size bound.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
                || Utf8.GetString(bytes).Contains('\r') || Hash(bytes) != expectedHash)
                throw Invalid("A v8 identity asset differs from its reviewed UTF-8 bytes.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("A v8 identity asset contains invalid UTF-8.");
        }
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException Invalid(string message)
        => new("The immutable Lilith v8 identity package was rejected. " + message);
}
