using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

// V1/V2 remain frozen packages and compatibility snapshots. V3 changes only
// the reviewed open-core projection, never Canon or the player-memory root.
internal static class LilithIdentityPackageV3Loader
{
    internal const string PackageId = "lilith-identity-package-v3";
    internal const int PackageVersion = 3;
    internal const string ManifestFileName = "lilith-identity-package.v3.json";
    internal const string ManifestSha256 = "A735CDD69CCF43B721082EC813A82DD83347EC5A2E61943ABEA033123859C7C3";
    internal const string IdentityFingerprintSha256 = "B71FC3D722F53E60F5675988BBFBED3797A5267EF6D44466E9C1AC76F6993D0B";
    internal const string SoulAssetSha256 = "0374A402D6057497287662B60536159197144315A989FAF0A5C00F23FE14A83F";
    internal const string HermesPersonalityContentSha256 = "F80032E372389B2DABBE0B6BEC7905954F291CFF1ACC177342822F8CD0113411";
    internal const string CompositionMode = "OpenCoreAndOperatingContractWithOneActiveLocaleV3";
    internal const string LocaleActivation =
        "This versioned identity profile explicitly enables ConversationAdaptive for its Hermes Workbench. "
        + "Apply exactly one locale section using the operating contract's display-language precedence. "
        + "The other sections are inactive references, not additional voices. "
        + "Do not blend, recite or translate their instructions. Voice language remains independent.\n";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (string File, string Sha256)[] Assets =
    {
        ("SOUL.lilith.v3.md", "0374A402D6057497287662B60536159197144315A989FAF0A5C00F23FE14A83F"),
        ("personality.lilith.v3.md", "9F9261B43BC88D17D3F0901614C104F485CDC185432B174C930A66B945909526"),
        ("operating-contract.lilith.v3.md", "2ACCA3ABFCD77907FCA8A12E33E654212E8C1CE58619855314437B19386FB1CF"),
        ("voice.zh-Hant-TW.v3.md", "E00EF31C6A45FE3F6E8D882B3021ED73E36FF89505EF4FB91458AC059C800767"),
        ("voice.zh-Hans-CN.v3.md", "4078776A3B05F49A2932B1D5B206AB71E63FA16BEA8F4A855FA98425B0234FE0"),
        ("voice.ja-JP.v3.md", "AAF7E98FF9237741100736C35A094D63741B27392A701E334CFB78BA6CC16266"),
        ("voice.en.v3.md", "59B581454A9CC78B912D680AA2259FCEE646E831C0D1DA3D7F571490FC74A27A"),
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(root);
        var manifestBytes = ReadPinned(root, ManifestFileName, ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 3
            || manifest.GetProperty("packageId").GetString() != PackageId
            || manifest.GetProperty("packageVersion").GetInt32() != PackageVersion
            || manifest.GetProperty("composition").GetString() != CompositionMode
            || manifest.GetProperty("canonSemanticHash").GetString() != LilithIdentityPackageV1Loader.CanonSemanticHash)
            throw Invalid("The sealed manifest does not describe the accepted v3 projection.");

        var expectedFiles = Assets.Select(asset => asset.File).Append(ManifestFileName)
            .Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal)
            || Directory.GetDirectories(root).Length != 0)
            throw Invalid("The v3 asset set differs from its exact sealed file set.");

        var text = Assets.ToDictionary(asset => asset.File,
            asset => Utf8.GetString(ReadPinned(root, asset.File, asset.Sha256)), StringComparer.Ordinal);
        var personality = Compose(text);
        if (Hash(Utf8.GetBytes(personality)) != HermesPersonalityContentSha256)
            throw Invalid("The composed v3 personality differs from its pinned digest.");

        var material = new List<string>
        {
            $"package:{PackageId}@{PackageVersion}",
            $"baseline-identity:{LilithIdentityPackageV2Loader.IdentityFingerprintSha256}",
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
            throw Invalid("The v3 identity fingerprint material differs from its pinned identity.");

        var voices = new Dictionary<string, LilithVoiceProjectionV1>(StringComparer.Ordinal);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            var file = $"voice.{locale}.v3.md";
            voices.Add(locale, new LilithVoiceProjectionV1(locale,
                $"lilith-voice-{locale}-v3", locale, file,
                Assets.Single(asset => asset.File == file).Sha256,
                Array.AsReadOnly(new[] { new LilithVoiceInstructionV1("voice.expression.v3", text[file]) })));
        }
        return new LilithIdentityPackageV1(PackageId, PackageVersion, IdentityFingerprintSha256,
            LilithIdentityPackageV1Loader.CanonProfileId, LilithIdentityPackageV1Loader.CanonProfileVersion,
            LilithIdentityPackageV1Loader.CanonSemanticHash,
            "lilith-soul-projection-v3", Assets[0].File, SoulAssetSha256, text[Assets[0].File],
            "lilith-voice-contract-v3", 3, new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(voices),
            "lilith-hermes-personality-v3", Assets[1].File, Assets[1].Sha256,
            personality, HermesPersonalityContentSha256, ManifestSha256);
    }

    private static string Compose(IReadOnlyDictionary<string, string> text)
    {
        var builder = new StringBuilder(text["personality.lilith.v3.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["operating-contract.lilith.v3.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n## Locale reference sections\n\n").Append(LocaleActivation);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            builder.Append("\n### Active locale `").Append(locale).Append("`\n\n")
                .Append(text[$"voice.{locale}.v3.md"].TrimEnd('\r', '\n')).Append('\n');
        }
        return builder.ToString();
    }

    private static byte[] ReadPinned(string root, string file, string expectedHash)
    {
        var path = Path.Combine(root, file);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw Invalid("A required v3 identity asset is missing or outside its size bound.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
                || Utf8.GetString(bytes).Contains('\r') || Hash(bytes) != expectedHash)
                throw Invalid("A v3 identity asset differs from its reviewed UTF-8 bytes.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("A v3 identity asset contains invalid UTF-8.");
        }
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException Invalid(string message)
        => new("The immutable Lilith v3 identity package was rejected. " + message);
}
