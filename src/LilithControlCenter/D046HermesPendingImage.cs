using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace LilithControlCenter;

internal sealed class D046HermesPendingImage : IDisposable
{
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private const int MaximumFileNameUtf8Bytes = 255;

    private byte[]? _bytes;

    private D046HermesPendingImage(
        string runtimeGeneration,
        string taskId,
        string sessionId,
        string fileName,
        string mediaType,
        byte[] bytes,
        BitmapImage? preview)
    {
        RuntimeGeneration = runtimeGeneration;
        TaskId = taskId;
        SessionId = sessionId;
        FileName = fileName;
        MediaType = mediaType;
        ByteLength = bytes.LongLength;
        Preview = preview;
        _bytes = bytes;
    }

    internal string RuntimeGeneration { get; }
    internal string TaskId { get; }
    internal string SessionId { get; }
    public string FileName { get; }
    public string MediaType { get; }
    public long ByteLength { get; }
    public BitmapImage? Preview { get; }
    public string Detail => FormatBytes(ByteLength);
    public string PreviewLabel => Preview is null ? "WEBP" : string.Empty;

    internal static D046HermesPendingImage Load(
        string sourcePath,
        string runtimeGeneration,
        string taskId,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException(ControlCenterLocalization.Current.Get("chat.image.pick.required"), nameof(sourcePath));
        DemandOwner(runtimeGeneration, taskId, sessionId);

        var fullPath = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName)
            || StrictUtf8ByteCount(fileName) > MaximumFileNameUtf8Bytes)
        {
            throw new InvalidDataException(ControlCenterLocalization.Current.Get("chat.image.name.too.long"));
        }

        var mediaType = MediaTypeFor(fileName);
        byte[]? bytes = null;
        try
        {
            using (var stream = new FileStream(
                       fullPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                if (stream.Length <= 0 || stream.Length > MaximumBytes)
                    throw new InvalidDataException(ControlCenterLocalization.Current.Get("chat.image.byte.limit"));

                bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
            }

            DemandSignature(mediaType, bytes);
            var preview = CreatePreview(bytes, mediaType);
            return new D046HermesPendingImage(
                runtimeGeneration,
                taskId,
                sessionId,
                fileName,
                mediaType,
                bytes,
                preview);
        }
        catch
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    internal bool IsOwnedBy(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(RuntimeGeneration, runtimeGeneration, StringComparison.Ordinal)
            && string.Equals(TaskId, taskId, StringComparison.Ordinal)
            && string.Equals(SessionId, sessionId, StringComparison.Ordinal);

    internal D046HermesImageUpload CreateUpload()
    {
        var bytes = _bytes ?? throw new ObjectDisposedException(nameof(D046HermesPendingImage));
        return new D046HermesImageUpload(FileName, MediaType, bytes);
    }

    internal BitmapImage? CreateExpandedPreview()
    {
        var bytes = _bytes ?? throw new ObjectDisposedException(nameof(D046HermesPendingImage));
        return CreatePreview(bytes, MediaType, 2048);
    }

    public void Dispose()
    {
        var bytes = Interlocked.Exchange(ref _bytes, null);
        if (bytes is not null)
            CryptographicOperations.ZeroMemory(bytes);
    }

    internal static BitmapImage? CreatePreview(byte[] bytes, string mediaType, int maximumEdge = 256)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            // Compressed byte bounds do not bound the decoded bitmap. Read the
            // dimensions before allocating a downsampled, detached WPF image.
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0
                || frame.PixelWidth > 16384 || frame.PixelHeight > 16384
                || (long)frame.PixelWidth * frame.PixelHeight > 32L * 1024 * 1024)
                throw new InvalidDataException(ControlCenterLocalization.Current.Get("chat.image.too.large"));
            stream.Position = 0;
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            if (frame.PixelWidth >= frame.PixelHeight)
                preview.DecodePixelWidth = Math.Min(frame.PixelWidth, maximumEdge);
            else
                preview.DecodePixelHeight = Math.Min(frame.PixelHeight, maximumEdge);
            preview.StreamSource = stream;
            preview.EndInit();
            preview.Freeze();
            return preview;
        }
        catch (Exception exception) when (string.Equals(
                mediaType,
                "image/webp",
                StringComparison.Ordinal)
            && exception is NotSupportedException
                or FileFormatException
                or InvalidOperationException)
        {
            // WebP decoding depends on the machine's WIC codec. The validated
            // bytes remain uploadable even when this machine cannot thumbnail it.
            return null;
        }
    }

    private static void DemandOwner(
        string runtimeGeneration,
        string taskId,
        string sessionId)
    {
        if (string.IsNullOrWhiteSpace(runtimeGeneration)
            || string.IsNullOrWhiteSpace(taskId)
            || string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException(ControlCenterLocalization.Current.Get("state.this.session.is.not.ready.to.receive.images.97"));
        }
    }

    private static string MediaTypeFor(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new InvalidDataException(ControlCenterLocalization.Current.Get("chat.image.supported"))
        };

    internal static void DemandSignature(string mediaType, byte[] bytes)
    {
        var valid = mediaType switch
        {
            "image/png" => bytes.Length >= 8
                && bytes.AsSpan(0, 8).SequenceEqual(
                    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.Length >= 4
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[^2] == 0xFF
                && bytes[^1] == 0xD9,
            "image/webp" => bytes.Length >= 12
                && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid)
            throw new InvalidDataException(ControlCenterLocalization.Current.Get("chat.image.invalid"));
    }

    private static int StrictUtf8ByteCount(string value)
        => new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetByteCount(value);

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? FormattableString.Invariant($"{bytes / (1024d * 1024d):0.##} MiB")
            : FormattableString.Invariant($"{Math.Max(1, bytes / 1024d):0.##} KiB");
}
