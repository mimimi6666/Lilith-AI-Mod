using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithRuntime;

// V1 remains a frozen package and a compatibility data shape. V2 changes the
// expression projection, not the accepted original Canon or player-memory root.
internal static class LilithIdentityPackageV2Loader
{
    internal const string PackageId = "lilith-identity-package-v2";
    internal const int PackageVersion = 2;
    internal const string ManifestFileName = "lilith-identity-package.v2.json";
    internal const string ManifestSha256 = "A08EEADAF77DE3FEB7449198FA8367AF4C346594DD881230203904666E40C180";
    internal const string IdentityFingerprintSha256 = "61EF26409508F44EF30F6B56F0C28A0510AF4E6A091E454C67936D4A8BA3A3D8";
    internal const string SoulAssetSha256 = "C0269CAEF2A546ABC4F4689ED435C78039FE7FF5E1DF2E6690DFE039AC89F474";
    internal const string HermesPersonalityContentSha256 = "30D180AFA96EAD344CBB03C78D96E37E251276FD852EFF43B79C2BA6128BEBEF";
    internal const string CompositionMode = "ExpressionAndOperatingContractWithOneActiveLocaleV2";
    internal const string LocaleActivation =
        "This versioned identity profile explicitly enables ConversationAdaptive for its Hermes Workbench. "
        + "Apply exactly one locale section using the operating contract's display-language precedence. "
        + "The other sections are inactive references, not additional voices. "
        + "Do not blend, recite or translate their instructions. Voice language remains independent.\n";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (string File, string Sha256)[] Assets =
    {
        ("SOUL.lilith.v2.md", "C0269CAEF2A546ABC4F4689ED435C78039FE7FF5E1DF2E6690DFE039AC89F474"),
        ("personality.lilith.v2.md", "C2E44FD63B6D67D4D8A3172D3F2D82047C8D5AD1044150DFED687CA2E6EB8523"),
        ("operating-contract.lilith.v2.md", "49E62C003E942D57ADF1C6A970BF0806647786100AB6DA203FC94E0509FA3558"),
        ("voice.zh-Hant-TW.v2.md", "FFDE01C6452DDD357B71CF3DA609AC897F70132CA4D3F48725048F9EA19CC0A6"),
        ("voice.zh-Hans-CN.v2.md", "67638755687406E0CBECECEA6FD3574628EDD6A1D7DCCFE2B568702973B27323"),
        ("voice.ja-JP.v2.md", "05C951C245F63EB8D296DA16C7A5F113F2E3516026AEC2C3A782096A07A8D0E3"),
        ("voice.en.v2.md", "65572A344BB5845FC44822EBC4F47D37DA25123538DC1D2671CAAD772EF04543"),
    };

    internal static LilithIdentityPackageV1 Load(string packageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var root = Path.GetFullPath(packageDirectory);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(root);
        var manifestBytes = ReadPinned(root, ManifestFileName, ManifestSha256);
        using var document = JsonDocument.Parse(manifestBytes);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schemaVersion").GetInt32() != 2
            || manifest.GetProperty("packageId").GetString() != PackageId
            || manifest.GetProperty("packageVersion").GetInt32() != PackageVersion
            || manifest.GetProperty("composition").GetString() != CompositionMode
            || manifest.GetProperty("canonSemanticHash").GetString() != LilithIdentityPackageV1Loader.CanonSemanticHash)
            throw Invalid("The sealed manifest does not describe the accepted v2 projection.");

        var expectedFiles = Assets.Select(asset => asset.File).Append(ManifestFileName)
            .Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal)
            || Directory.GetDirectories(root).Length != 0)
            throw Invalid("The v2 asset set differs from its exact sealed file set.");

        var text = Assets.ToDictionary(asset => asset.File,
            asset => Utf8.GetString(ReadPinned(root, asset.File, asset.Sha256)), StringComparer.Ordinal);
        var personality = Compose(text);
        if (Hash(Utf8.GetBytes(personality)) != HermesPersonalityContentSha256)
            throw Invalid("The composed v2 personality differs from its pinned digest.");

        var material = new List<string>
        {
            $"package:{PackageId}@{PackageVersion}",
            $"baseline-identity:{LilithIdentityPackageV1Loader.IdentityFingerprintSha256}",
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
            throw Invalid("The v2 identity fingerprint material differs from its pinned identity.");

        var voices = new Dictionary<string, LilithVoiceProjectionV1>(StringComparer.Ordinal);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            var file = $"voice.{locale}.v2.md";
            voices.Add(locale, new LilithVoiceProjectionV1(locale,
                $"lilith-voice-{locale}-v2", locale, file,
                Assets.Single(asset => asset.File == file).Sha256,
                Array.AsReadOnly(new[] { new LilithVoiceInstructionV1("voice.expression.v2", text[file]) })));
        }
        return new LilithIdentityPackageV1(PackageId, PackageVersion, IdentityFingerprintSha256,
            LilithIdentityPackageV1Loader.CanonProfileId, LilithIdentityPackageV1Loader.CanonProfileVersion,
            LilithIdentityPackageV1Loader.CanonSemanticHash,
            "lilith-soul-projection-v2", Assets[0].File, SoulAssetSha256, text[Assets[0].File],
            "lilith-voice-contract-v2", 2, new ReadOnlyDictionary<string, LilithVoiceProjectionV1>(voices),
            "lilith-hermes-personality-v2", Assets[1].File, Assets[1].Sha256,
            personality, HermesPersonalityContentSha256, ManifestSha256);
    }

    private static string Compose(IReadOnlyDictionary<string, string> text)
    {
        var builder = new StringBuilder(text["personality.lilith.v2.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n").Append(text["operating-contract.lilith.v2.md"].TrimEnd('\r', '\n'));
        builder.Append("\n\n## Locale reference sections\n\n").Append(LocaleActivation);
        foreach (var locale in LilithIdentityPackageV1Loader.SupportedDisplayLocales)
        {
            builder.Append("\n### Active locale `").Append(locale).Append("`\n\n")
                .Append(text[$"voice.{locale}.v2.md"].TrimEnd('\r', '\n')).Append('\n');
        }
        return builder.ToString();
    }

    private static byte[] ReadPinned(string root, string file, string expectedHash)
    {
        var path = Path.Combine(root, file);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 65536)
            throw Invalid("A required v2 identity asset is missing or outside its size bound.");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })
                || Utf8.GetString(bytes).Contains('\r') || Hash(bytes) != expectedHash)
                throw Invalid("A v2 identity asset differs from its reviewed UTF-8 bytes.");
        }
        catch (DecoderFallbackException)
        {
            throw Invalid("A v2 identity asset contains invalid UTF-8.");
        }
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static InvalidDataException Invalid(string message)
        => new("The immutable Lilith v2 identity package was rejected. " + message);
}

