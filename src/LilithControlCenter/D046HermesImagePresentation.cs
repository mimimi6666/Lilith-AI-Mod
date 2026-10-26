using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using LilithGameBridgeContracts;
using Microsoft.Win32.SafeHandles;

namespace LilithControlCenter;

// Hermes' documented persisted @image directive is a display format, not a
// filesystem capability. Only exact native-generated files in the dedicated
// profile's images directory can become display descriptors. No HTTP/UNC, other
// profile, arbitrary message path, or model-generated command is dereferenced.
internal static class D046HermesImagePresentation
{
    internal const int MaximumImageBytes = 8 * 1024 * 1024;
    private const int MaximumImages = 8;
    private static readonly Regex NativeName = new(
        @"^(upload|paste|clipboard)_[0-9]{8}_[0-9]{6}_[1-9][0-9]{0,8}\.(png|jpg|jpeg|webp)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50));

    internal static string CurrentImagesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LilithAiMod", "Runtime", "hermes", LilithPlayerDisplayName.ProfileId, "images");

    internal static (string Text, IReadOnlyList<D046HermesImageAttachment> Images) Parse(
        string text, string imagesRoot)
    {
        var images = new List<D046HermesImageAttachment>();
        var body = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var directive = line.TrimEnd('\r');
            var reference = directive.StartsWith("@image:", StringComparison.Ordinal)
                ? directive[7..] : string.Empty;
            if (reference.Length >= 2 && reference[0] is '`' or '\'' or '"'
                && reference[^1] == reference[0])
                reference = reference[1..^1];
            if (images.Count < MaximumImages && TryResolveOwnedPath(reference, imagesRoot, out var path))
            {
                if (!images.Any(image => string.Equals(image.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    images.Add(new(path, Path.GetFileName(path), MediaType(path), 0, string.Empty));
            }
            else
                body.Add(line);
        }
        return (images.Count == 0 ? text : string.Join("\n", body).TrimEnd('\r', '\n'), images);
    }

    internal static bool TryResolveOwnedPath(string? proposedPath, string imagesRoot, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(proposedPath) || proposedPath.Length > 1024
            || proposedPath.Any(char.IsControl) || !Path.IsPathFullyQualified(proposedPath)
            || proposedPath.StartsWith("\\\\", StringComparison.Ordinal)
            || proposedPath.StartsWith("//", StringComparison.Ordinal))
            return false;
        try
        {
            var full = Path.GetFullPath(proposedPath);
            var root = Path.GetFullPath(imagesRoot).TrimEnd(Path.DirectorySeparatorChar);
            // GetFullPath canonicalization is not admission of traversal or an ADS.
            if (!string.Equals(full.Replace('/', '\\'), proposedPath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase)
                || !NativeName.IsMatch(Path.GetFileName(full))
                || (OperatingSystem.IsWindows() && (Path.GetPathRoot(full)?.Length != 3 || full[2..].Contains(':'))))
                return false;
            path = full;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    internal static byte[]? ReadVerified(D046HermesImageAttachment image, string imagesRoot)
    {
        if (!TryResolveOwnedPath(image.FilePath, imagesRoot, out var path)
            || !string.Equals(MediaType(path), image.MediaType, StringComparison.Ordinal)
            || image.ByteLength < 0 || image.ByteLength > MaximumImageBytes
            || (image.Sha256.Length != 0 && (image.Sha256.Length != 64 || !image.Sha256.All(Uri.IsHexDigit))))
            return null;
        byte[]? bytes = null;
        try
        {
            // Every directory is checked; a junction inside or above the cache
            // cannot transform a harmless history row into an unrelated read.
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
                 directory is not null; directory = directory.Parent)
                if (!directory.Exists || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return null;
            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.SequentialScan);
            if (OperatingSystem.IsWindows() && !HandleStillNamesExactPath(stream.SafeFileHandle, path))
                return null;
            if (stream.Length <= 0 || stream.Length > MaximumImageBytes
                || (image.ByteLength != 0 && image.ByteLength != stream.Length))
                return null;
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (image.Sha256.Length != 0 && !string.Equals(image.Sha256,
                    Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            {
                CryptographicOperations.ZeroMemory(bytes);
                return null;
            }
            return bytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            return null;
        }
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => string.Empty
    };

    private static bool HandleStillNamesExactPath(SafeFileHandle handle, string expected)
    {
        var buffer = new StringBuilder(2048);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) return false;
        var resolved = buffer.ToString();
        if (resolved.StartsWith("\\\\?\\", StringComparison.Ordinal)) resolved = resolved[4..];
        return string.Equals(resolved, expected, StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}

internal sealed partial class D046HermesTimelineItemViewModel
{
    private IReadOnlyList<D046HermesImageAttachment> _acceptedImages = Array.Empty<D046HermesImageAttachment>();
    private IReadOnlyList<D046HermesImageAttachment> _displayImages = Array.Empty<D046HermesImageAttachment>();
    private string? _imageTextVersion;
    private string _imageDisplayText = string.Empty;
    public IReadOnlyList<D046HermesImageAttachment> Images { get { EnsureImagePresentation(); return _displayImages; } }
    public bool HasImages => Images.Count > 0;
    public string ImageDisplayText { get { EnsureImagePresentation(); return _imageDisplayText; } }

    internal void SetImages(IReadOnlyList<D046HermesImageAttachment>? images)
    {
        if (MessageRole != D046HermesMessageRole.User || images is null || images.Count == 0)
            return;
        _acceptedImages = images.Take(8).Where(image => D046HermesImagePresentation.TryResolveOwnedPath(
            image.FilePath, D046HermesImagePresentation.CurrentImagesRoot, out _)).ToArray();
        RefreshImagePresentation();
    }

    private void EnsureImagePresentation()
    {
        if (string.Equals(_imageTextVersion, Text, StringComparison.Ordinal)) return;
        _imageTextVersion = Text;
        var parsed = MessageRole == D046HermesMessageRole.User
            ? D046HermesImagePresentation.Parse(Text, D046HermesImagePresentation.CurrentImagesRoot)
            : (Text, (IReadOnlyList<D046HermesImageAttachment>)Array.Empty<D046HermesImageAttachment>());
        _imageDisplayText = parsed.Item1;
        _displayImages = _acceptedImages.Count != 0 ? _acceptedImages : parsed.Item2;
    }

    private void RefreshImagePresentation()
    {
        _imageTextVersion = null;
        OnPropertyChanged(nameof(Images));
        OnPropertyChanged(nameof(HasImages));
        OnPropertyChanged(nameof(ImageDisplayText));
    }
}

internal sealed partial class D052HermesTurnPresentationCollection
{
    internal void SetMessageImages(string turnId, string messageId, IReadOnlyList<D046HermesImageAttachment>? images)
    {
        if (TryMessageIdentity(turnId, messageId, out var identity)
            && _messagesByIdentity.TryGetValue(identity, out var item))
            item.SetImages(images);
    }
}
