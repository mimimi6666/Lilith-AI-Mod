using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace LilithControlCenter;

// This is a display receipt, not a filesystem or model authority. Only the
// native image_generate decoder or the narrowly scoped completed-assistant
// delivery policy may nominate one. Actual pixels still require confined read
// and bounded decode; a receipt/nomination alone is never delivery success.
public sealed record D046HermesGeneratedImageAttachment(string FilePath, string FileName, string MediaType);

internal static class D046HermesGeneratedImagePresentation
{
    internal const int MaximumImageBytes = 25 * 1024 * 1024;
    private const int MaximumReceiptCharacters = 128 * 1024;
    private static readonly Regex NativeName = new(
        @"^google_image_[0-9]{8}_[0-9]{6}_[0-9a-f]{8}\.(png|jpg|jpeg|webp)$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    internal static string CurrentImagesRoot => Path.Combine(
        Path.GetDirectoryName(D046HermesImagePresentation.CurrentImagesRoot)!, "cache", "images");

    internal static D046HermesGeneratedImageAttachment? TryRead(
        string certifiedToolName, JsonElement result, string? imagesRoot = null)
    {
        if (!string.Equals(certifiedToolName, "image_generate", StringComparison.Ordinal)) return null;
        JsonDocument? parsed = null;
        try
        {
            if (result.ValueKind == JsonValueKind.String)
            {
                var encoded = result.GetString();
                if (string.IsNullOrEmpty(encoded) || encoded.Length > MaximumReceiptCharacters) return null;
                parsed = JsonDocument.Parse(encoded, new JsonDocumentOptions { MaxDepth = 8 });
                result = parsed.RootElement;
            }
            if (result.ValueKind != JsonValueKind.Object) return null;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in result.EnumerateObject())
                if (names.Count >= 16 || !names.Add(property.Name)) return null;
            if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !HasString(result, "provider", "google")
                || !(HasString(result, "modality", "text") || HasString(result, "modality", "image"))
                || !result.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.String
                || !TryResolveOwnedPath(image.GetString(), imagesRoot ?? CurrentImagesRoot, out var path)) return null;
            // Never accept an ambiguous second host path as a fallback.
            if (result.TryGetProperty("host_image", out var hostImage)
                && (hostImage.ValueKind != JsonValueKind.String
                    || !string.Equals(hostImage.GetString(), path, StringComparison.OrdinalIgnoreCase))) return null;
            return new(path, Path.GetFileName(path), MediaType(path));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        { return null; }
        finally { parsed?.Dispose(); }
    }

    private static bool HasString(JsonElement value, string name, string expected)
        => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            && string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    internal static bool TryResolveOwnedPath(string? proposed, string imagesRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(proposed) || proposed.Length > 1024 || proposed.Any(char.IsControl)
            || !Path.IsPathFullyQualified(proposed) || proposed.StartsWith("\\\\", StringComparison.Ordinal)
            || proposed.StartsWith("//", StringComparison.Ordinal)) return false;
        try
        {
            var full = Path.GetFullPath(proposed);
            var root = Path.GetFullPath(imagesRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(full.Replace('/', '\\'), proposed.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase)
                || !NativeName.IsMatch(Path.GetFileName(full))
                || (OperatingSystem.IsWindows() && (Path.GetPathRoot(full)?.Length != 3 || full[2..].Contains(':')))) return false;
            path = full;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        { return false; }
    }

    internal static byte[]? ReadVerified(D046HermesGeneratedImageAttachment image, string imagesRoot)
    {
        if (!TryResolveOwnedPath(image.FilePath, imagesRoot, out var path)
            || !string.Equals(image.MediaType, MediaType(path), StringComparison.Ordinal)
            || !string.Equals(image.FileName, Path.GetFileName(path), StringComparison.Ordinal)) return null;
        byte[]? bytes = null;
        try
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
                 directory is not null; directory = directory.Parent)
                if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            if (OperatingSystem.IsWindows() && !HandleStillNamesExactPath(stream.SafeFileHandle, path)) return null;
            if (stream.Length <= 0 || stream.Length > MaximumImageBytes) return null;
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            // A bounded WPF decode must still follow this transport/container check.
            if (!HasCompleteContainer(bytes, image.MediaType))
            { CryptographicOperations.ZeroMemory(bytes); return null; }
            return bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            return null;
        }
    }

    internal static bool HasCompleteContainer(ReadOnlySpan<byte> bytes, string mediaType)
    {
        if (mediaType == "image/jpeg")
            return bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8
                && bytes[^2] == 0xff && bytes[^1] == 0xd9;
        if (mediaType == "image/webp")
            return bytes.Length >= 20 && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes[8..12].SequenceEqual("WEBP"u8)
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) == bytes.Length - 8;
        if (mediaType != "image/png" || bytes.Length < 45
            || !bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var offset = 8;
        var first = true;
        while (bytes.Length - offset >= 12)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > (uint)(bytes.Length - offset - 12)) return false;
            var type = bytes.Slice(offset + 4, 4);
            if (first && (!type.SequenceEqual("IHDR"u8) || length != 13)) return false;
            first = false;
            offset += checked((int)length + 12);
            if (type.SequenceEqual("IEND"u8)) return length == 0 && offset == bytes.Length;
        }
        return false;
    }

    private static string MediaType(string path) => Path.GetExtension(path) switch
    { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => string.Empty };

    private static bool HandleStillNamesExactPath(SafeFileHandle handle, string expected)
    {
        var buffer = new StringBuilder(2048);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) return false;
        var resolved = buffer.ToString();
        if (resolved.StartsWith("\\\\?\\", StringComparison.Ordinal)) resolved = resolved[4..];
        return string.Equals(resolved, expected, StringComparison.OrdinalIgnoreCase);
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true,
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}

internal sealed partial class D052HermesTurnPresentationCollection
{
    internal D052HermesPresentationUpsertResult UpsertGeneratedImage(
        string turnId, string toolCallId, D046HermesGeneratedImageAttachment image)
    {
        if (!IsLowercaseHex32(turnId) || string.IsNullOrWhiteSpace(toolCallId)
            || !D046HermesGeneratedImagePresentation.TryResolveOwnedPath(image.FilePath,
                D046HermesGeneratedImagePresentation.CurrentImagesRoot, out _))
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        return GetOrCreateTurn(turnId).TryUpsertGeneratedImage("image:" + toolCallId, image)
            ? D052HermesPresentationUpsertResult.Applied : D052HermesPresentationUpsertResult.IdentityConflict;
    }
}
