using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

// V1/V2/V3/V4/V5 remain frozen packages and compatibility snapshots. V6 changes only
// the reviewed open-core projection, never Canon or the player-memory root.
internal static class LilithIdentityPackageV6Loader
{
    internal const string PackageId = "lilith-identity-package-v6";
    internal const int PackageVersion = 6;
    internal const string ManifestFileName = "lilith-identity-package.v6.json";
    internal const string ManifestSha256 = "C67F85AE34F1541B7FE34CD27A98657178E852C0263E347332800761487722F0";
    internal const string IdentityFingerprintSha256 = "4FD401627287EEEDCF60AEEF1C02A38E8A2561E469450C64147B3B8AED60DE76";
    internal const string SoulAssetSha256 = "D8A6DE70851AE2B573D8720934EA47FC278E05872DA817C64C2666BE161679FF";
    internal const string HermesPersonalityContentSha256 = "5209E3A822914394B58ECFC49976A22AE69B5BF32AAA88D79D17163A63D49E5B";
    internal const string CompositionMode = "OpenCoreAndOperatingContractWithOneActiveLocaleV6";
    internal const string LocaleActivation =
        "This versioned identity profile explicitly enables ConversationAdaptive for its Hermes Workbench. "
        + "Apply exactly one locale section using the operating contract's display-language precedence. "
        + "The other sections are inactive references, not additional voices. "
        + "Do not blend, recite or translate their instructions. Voice language remains independent.\n";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (string File, string Sha256)[] Assets =
    {
        ("SOUL.lilith.v6.md", "D8A6DE70851AE2B573D8720934EA47FC278E05872DA817C64C2666BE161679FF"),
        ("personality.lilith.v6.md", "FB369D1F82DB9EBEE07D0871B0DB42C326553A191EFC6EB293348E5FF1DDC53E"),
        ("operating-contract.lilith.v6.md", "BC01959010B26182F8D393F6AAA4541B3FDDAC94B1FB83A0C1253826B6D130D9"),
        ("voice.zh-Hant-TW.v6.md", "E00EF31C6A45FE3F6E8D882B3021ED73E36FF89505EF4FB91458AC059C800767"),
        ("voice.zh-Hans-CN.v6.md", "4078776A3B05F49A2932B1D5B206AB71E63FA16BEA8F4A855FA98425B0234FE0"),
        ("voice.ja-JP.v6.md", "AAF7E98FF9237741100736C35A094D63741B27392A701E334CFB78BA6CC16266"),
        ("voice.en.v6.md", "59B581454A9CC78B912D680AA2259FCEE646E831C0D1DA3D7F571490FC74A27A"),
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(root);
        var manifestBytes = ReadPinned(root, ManifestFileName, ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 6
            || manifest.GetProperty("packageId").GetString() != PackageId
            || manifest.GetProperty("packageVersion").GetInt32() != PackageVersion
            || manifest.GetProperty("composition").GetString() != CompositionMode
            || manifest.GetProperty("canonSemanticHash").GetString() != LilithIdentityPackageV1Loader.CanonSemanticHash)
            throw Invalid("The sealed manifest does not describe the accepted v6 projection.");

        var expectedFiles = Assets.Select(asset => asset.File).Append(ManifestFileName)
            .Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal)
            || Directory.GetDirectories(root).Length != 0)
            throw Invalid("The v6 asset set differs from its exact sealed file set.");

        var text = Assets.ToDictionary(asset => asset.File,
            asset => Utf8.GetString(ReadPinned(root, asset.File, asset.Sha256)), StringComparer.Ordinal);
        var personality = Compose(text);
        if (Hash(Utf8.GetBytes(personality)) != HermesPersonalityContentSha256)
            throw Invalid("The composed v6 personality differs from its pinned digest.");

        var material = new List<string>
        {
            $"package:{PackageId}@{PackageVersion}",
            $"baseline-identity:{LilithIdentityPackageV5Loader.IdentityFingerprintSha256}",
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
            throw Invalid("The v6 identity fingerprint material differs from its pinned identity.");

        var voices = new Dictionary<string, LilithVoiceProjectionV1>(StringComparer.Ordinal);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            var file = $"voice.{locale}.v6.md";
            voices.Add(locale, new LilithVoiceProjectionV1(locale,
                $"lilith-voice-{locale}-v6", locale, file,
                Assets.Single(asset => asset.File == file).Sha256,
                Array.AsReadOnly(new[] { new LilithVoiceInstructionV1("voice.expression.v6", text[file]) })));
        }
        return new LilithIdentityPackageV1(PackageId, PackageVersion, IdentityFingerprintSha256,
            LilithIdentityPackageV1Loader.CanonProfileId, LilithIdentityPackageV1Loader.CanonProfileVersion,
            LilithIdentityPackageV1Loader.CanonSemanticHash,
            "lilith-soul-projection-v6", Assets[0].File, SoulAssetSha256, text[Assets[0].File],
            "lilith-voice-contract-v6", 6, new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(voices),
            "lilith-hermes-personality-v6", Assets[1].File, Assets[1].Sha256,
            personality, HermesPersonalityContentSha256, ManifestSha256);
    }

    private static string Compose(IReadOnlyDictionary<string, string> text)
    {
        var builder = new StringBuilder(text["personality.lilith.v6.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["operating-contract.lilith.v6.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n## Locale reference sections\n\n").Append(LocaleActivation);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            builder.Append("\n### Active locale `").Append(locale).Append("`\n\n")
                .Append(text[$"voice.{locale}.v6.md"].TrimEnd('\r', '\n')).Append('\n');
        }
        return builder.ToString();
    }

    private static byte[] ReadPinned(string root, string file, string expectedHash)
    {
        var path = Path.Combine(root, file);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw Invalid("A required v6 identity asset is missing or outside its size bound.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
                || Utf8.GetString(bytes).Contains('\r') || Hash(bytes) != expectedHash)
                throw Invalid("A v6 identity asset differs from its reviewed UTF-8 bytes.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("A v6 identity asset contains invalid UTF-8.");
        }
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException Invalid(string message)
        => new("The immutable Lilith v6 identity package was rejected. " + message);
}
