using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithGameBridgeContracts;

public sealed record LilithGameBridgeTrustedFile(
    string RelativePath,
    string Sha256);

public sealed record LilithGameBridgeTrustManifestDocument(
    string GameRoot,
    LilithGameBridgeTrustedFile GameExecutable,
    LilithGameBridgeTrustedFile Plugin);

public static class LilithGameBridgeTrustManifest
{
    public const string Schema = "lilith-game-bridge-peer-trust/v1";
    public const string ManifestFileName = "game-bridge-peer-trust.v1.json";
    public const string DigestFileName = "game-bridge-peer-trust.v1.sha256";
    public const string ManifestPathEnvironmentVariable =
        "LILITH_GAME_BRIDGE_PEER_TRUST_MANIFEST";
    public const string ManifestSha256EnvironmentVariable =
        "LILITH_GAME_BRIDGE_PEER_TRUST_MANIFEST_SHA256";
    public const int MaximumManifestBytes = 32 * 1024;
    public const int MaximumDigestFileBytes = 66;

    public static byte[] CreateCanonicalBytes(
        string gameRoot,
        string gameExecutableRelativePath,
        string gameExecutableSha256,
        string pluginRelativePath,
        string pluginSha256)
    {
        var document = Validate(
            gameRoot,
            new LilithGameBridgeTrustedFile(
                gameExecutableRelativePath,
                gameExecutableSha256),
            new LilithGameBridgeTrustedFile(
                pluginRelativePath,
                pluginSha256));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", Schema);
            writer.WriteString("gameRoot", document.GameRoot);
            WriteFile(writer, "gameExecutable", document.GameExecutable);
            WriteFile(writer, "plugin", document.Plugin);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static LilithGameBridgeTrustManifestDocument Decode(
        ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || bytes.Length > MaximumManifestBytes)
            throw new InvalidDataException("The game bridge trust manifest exceeded its bound.");
        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        var root = document.RootElement;
        RequireExact(
            root,
            "schema",
            "gameRoot",
            "gameExecutable",
            "plugin");
        if (!string.Equals(
                RequiredString(root, "schema", 128),
                Schema,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The game bridge trust schema was rejected.");
        }
        return Validate(
            RequiredString(root, "gameRoot", 32_767),
            ReadFile(root.GetProperty("gameExecutable")),
            ReadFile(root.GetProperty("plugin")));
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    public static byte[] CreateDigestFileBytes(ReadOnlySpan<byte> manifestBytes)
        => Encoding.ASCII.GetBytes(ComputeSha256(manifestBytes) + "\n");

    public static string DecodeDigestFile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 64 or > MaximumDigestFileBytes)
            throw new InvalidDataException("The game bridge trust digest file was rejected.");
        var length = bytes.Length;
        if (length > 0 && bytes[length - 1] == (byte)'\n')
            length--;
        if (length > 0 && bytes[length - 1] == (byte)'\r')
            length--;
        if (length != 64)
            throw new InvalidDataException("The game bridge trust digest file was rejected.");
        Span<char> characters = stackalloc char[64];
        for (var index = 0; index < length; index++)
        {
            var value = (char)bytes[index];
            if (!Uri.IsHexDigit(value))
                throw new InvalidDataException("The game bridge trust digest was rejected.");
            characters[index] = char.ToUpperInvariant(value);
        }
        return new string(characters);
    }

    public static string ResolveTrustedPath(
        LilithGameBridgeTrustManifestDocument manifest,
        LilithGameBridgeTrustedFile file)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(file);
        return ResolveRelativePath(manifest.GameRoot, file.RelativePath);
    }

    private static LilithGameBridgeTrustManifestDocument Validate(
        string gameRoot,
        LilithGameBridgeTrustedFile executable,
        LilithGameBridgeTrustedFile plugin)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)
            || gameRoot.Length > 32_767
            || !Path.IsPathFullyQualified(gameRoot)
            || gameRoot.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The game bridge trust root was rejected.");
        }
        var normalizedRoot = Path.GetFullPath(gameRoot);
        var volumeRoot = Path.GetPathRoot(normalizedRoot)
            ?? throw new InvalidDataException("The game bridge trust root was rejected.");
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(normalizedRoot),
                Path.TrimEndingDirectorySeparator(volumeRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A volume root cannot be a game root.");
        }
        var normalizedExecutable = ValidateFile(normalizedRoot, executable);
        var normalizedPlugin = ValidateFile(normalizedRoot, plugin);
        if (string.Equals(
                ResolveRelativePath(
                    normalizedRoot,
                    normalizedExecutable.RelativePath),
                ResolveRelativePath(
                    normalizedRoot,
                    normalizedPlugin.RelativePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The game executable and plugin identities must differ.");
        }
        return new LilithGameBridgeTrustManifestDocument(
            normalizedRoot,
            normalizedExecutable,
            normalizedPlugin);
    }

    private static LilithGameBridgeTrustedFile ValidateFile(
        string gameRoot,
        LilithGameBridgeTrustedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(file.RelativePath)
            || file.RelativePath.Length > 1_024)
        {
            throw new InvalidDataException("A game bridge trust path was rejected.");
        }
        _ = ResolveRelativePath(gameRoot, file.RelativePath);
        if (!IsSha256(file.Sha256))
            throw new InvalidDataException("A game bridge trust digest was rejected.");
        return new LilithGameBridgeTrustedFile(
            file.RelativePath.Replace('/', Path.DirectorySeparatorChar),
            file.Sha256.ToUpperInvariant());
    }

    private static string ResolveRelativePath(
        string gameRoot,
        string relativePath)
    {
        if (Path.IsPathRooted(relativePath)
            || Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException("A game bridge trust path was rejected.");
        }
        var segments = relativePath.Split(
            new[] { '\\', '/' },
            StringSplitOptions.None);
        if (segments.Length == 0
            || segments.Any(static segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException("A game bridge trust path was rejected.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(gameRoot, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(gameRoot)
            + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A game bridge trust path escaped its root.");
        return fullPath;
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
            return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return true;
    }

    private static LilithGameBridgeTrustedFile ReadFile(JsonElement value)
    {
        RequireExact(value, "relativePath", "sha256");
        return new LilithGameBridgeTrustedFile(
            RequiredString(value, "relativePath", 1_024),
            RequiredString(value, "sha256", 64));
    }

    private static string RequiredString(
        JsonElement value,
        string name,
        int maximumLength)
    {
        var property = value.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A game bridge trust string was rejected.");
        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength)
            throw new InvalidDataException("A game bridge trust string was rejected.");
        return text;
    }

    private static void RequireExact(
        JsonElement value,
        params string[] expectedNames)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A game bridge trust object was rejected.");
        var expected = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("A game bridge trust property was rejected.");
        }
        if (!seen.SetEquals(expected))
            throw new InvalidDataException("A game bridge trust property was missing.");
    }

    private static void WriteFile(
        Utf8JsonWriter writer,
        string propertyName,
        LilithGameBridgeTrustedFile file)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("relativePath", file.RelativePath);
        writer.WriteString("sha256", file.Sha256);
        writer.WriteEndObject();
    }
}
