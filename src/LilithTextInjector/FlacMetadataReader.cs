using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace LilithTextInjector;

internal sealed record FlacMetadata(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalSamples,
    string? Title,
    string? Artist)
{
    // FLAC uses zero total samples to mean that the duration is not known.
    public double DurationSeconds => TotalSamples / (double)SampleRate;
}

/// <summary>
/// Reads native FLAC metadata only. Audio frames and their checksums still need
/// independent decoder validation. The caller owns streams passed to Read.
/// Layout and validity rules: RFC 9639, sections 8.1, 8.2 and 8.6.
/// </summary>
internal static class FlacMetadataReader
{
    internal const int MaximumMetadataBlocks = 256;
    internal const long MaximumMetadataBytes = 64L * 1024 * 1024;
    internal const int MaximumCommentBlockBytes = 1024 * 1024;
    internal const int MaximumComments = 4096;
    internal const int MaximumCommentStringBytes = 64 * 1024;
    internal const int MaximumDisplayCharacters = 512;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static FlacMetadata Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, FileOptions.SequentialScan);
        return Read(stream);
    }

    public static FlacMetadata Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("A readable stream is required.", nameof(stream));

        Span<byte> header = stackalloc byte[4];
        ReadExactly(stream, header);
        if (header[0] != 'f' || header[1] != 'L' || header[2] != 'a' || header[3] != 'C')
            throw Invalid("The native FLAC signature is missing.");

        FlacMetadata? metadata = null;
        string? title = null;
        string? artist = null;
        var commentsSeen = false;
        long metadataBytes = 0;
        for (var block = 0; block < MaximumMetadataBlocks; block++)
        {
            ReadExactly(stream, header);
            var isLast = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7f;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            metadataBytes += 4L + length;
            if (metadataBytes > MaximumMetadataBytes)
                throw Invalid("FLAC metadata exceeds the read budget.");
            if (type == 127 || (block == 0 && type != 0))
                throw Invalid("The FLAC metadata block type or order is invalid.");

            if (type == 0)
            {
                if (metadata is not null || length != 34)
                    throw Invalid("FLAC requires one 34-byte initial STREAMINFO block.");
                metadata = ReadStreamInfo(stream);
            }
            else if (type == 4)
            {
                if (commentsSeen || length > MaximumCommentBlockBytes)
                    throw Invalid("The FLAC comment block is duplicated or too large.");
                commentsSeen = true;
                var payload = new byte[length];
                ReadExactly(stream, payload);
                (title, artist) = ReadComments(payload);
            }
            else
            {
                // Pictures, padding and application data never become allocations
                // sized by untrusted payload lengths. Reserved block types can be
                // skipped while retaining the standard length boundary.
                SkipExactly(stream, length);
            }

            if (isLast)
                return metadata! with { Title = title, Artist = artist };
        }

        throw Invalid("FLAC contains too many metadata blocks.");
    }

    private static FlacMetadata ReadStreamInfo(Stream stream)
    {
        Span<byte> info = stackalloc byte[34];
        ReadExactly(stream, info);
        var minimumBlock = BinaryPrimitives.ReadUInt16BigEndian(info);
        var maximumBlock = BinaryPrimitives.ReadUInt16BigEndian(info[2..]);
        var minimumFrame = (info[4] << 16) | (info[5] << 8) | info[6];
        var maximumFrame = (info[7] << 16) | (info[8] << 8) | info[9];
        if (minimumBlock < 16 || maximumBlock < minimumBlock ||
            (minimumFrame != 0 && maximumFrame != 0 && maximumFrame < minimumFrame))
            throw Invalid("The FLAC block or frame size range is invalid.");

        var packed = BinaryPrimitives.ReadUInt64BigEndian(info[10..18]);
        var sampleRate = (int)(packed >> 44);
        var channels = (int)((packed >> 41) & 7) + 1;
        var bitsPerSample = (int)((packed >> 36) & 31) + 1;
        var totalSamples = (long)(packed & 0xfffffffffUL);
        // A zero sample rate can represent non-audio in FLAC, but is not music.
        if (sampleRate == 0 || bitsPerSample < 4)
            throw Invalid("The FLAC music sample rate or bit depth is invalid.");
        return new(sampleRate, channels, bitsPerSample, totalSamples, null, null);
    }

    private static (string? Title, string? Artist) ReadComments(ReadOnlySpan<byte> payload)
    {
        try
        {
            var offset = 0;
            _ = ReadCommentString(payload, ref offset); // Vendor must also be valid UTF-8.
            var count = ReadCommentInteger(payload, ref offset);
            if (count > MaximumComments)
                throw Invalid("FLAC contains too many comments.");

            string? title = null;
            string? artist = null;
            for (uint field = 0; field < count; field++)
            {
                var comment = ReadCommentString(payload, ref offset);
                var separator = comment.IndexOf('=');
                if (separator <= 0)
                    throw Invalid("A FLAC comment has no field name.");
                for (var index = 0; index < separator; index++)
                {
                    if (comment[index] < 0x20 || comment[index] > 0x7e)
                        throw Invalid("A FLAC comment field name is invalid.");
                }

                var name = comment.AsSpan(0, separator);
                if (title is null && name.Equals("TITLE", StringComparison.OrdinalIgnoreCase))
                    title = SanitizeDisplay(comment[(separator + 1)..]);
                else if (artist is null && name.Equals("ARTIST", StringComparison.OrdinalIgnoreCase))
                    artist = SanitizeDisplay(comment[(separator + 1)..]);
            }

            if (offset != payload.Length)
                throw Invalid("The FLAC comment block has unexpected trailing bytes.");
            return (title, artist);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("A FLAC comment is not valid UTF-8.", exception);
        }
    }

    private static uint ReadCommentInteger(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < 4)
            throw Invalid("The FLAC comment block is truncated.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
        offset += 4;
        return value;
    }

    private static string ReadCommentString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = ReadCommentInteger(payload, ref offset);
        if (length > MaximumCommentStringBytes || length > (uint)(payload.Length - offset))
            throw Invalid("A FLAC comment string is truncated or too large.");
        var value = StrictUtf8.GetString(payload.Slice(offset, (int)length));
        offset += (int)length;
        return value;
    }

    private static string? SanitizeDisplay(string value)
    {
        var result = new StringBuilder();
        var pendingSpace = false;
        var characters = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = result.Length > 0;
                continue;
            }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format)
                continue;
            if (characters >= MaximumDisplayCharacters)
                break;
            if (pendingSpace)
            {
                if (characters + 1 >= MaximumDisplayCharacters)
                    break;
                result.Append(' ');
                characters++;
                pendingSpace = false;
            }

            // TMP can interpret raw angle brackets as rich-text commands. Preserve
            // their visible meaning with fullwidth characters, including malformed
            // and nested tags, instead of attempting to parse TMP's tag grammar.
            result.Append(rune.Value switch
            {
                '<' => "＜",
                '>' => "＞",
                _ => rune.ToString()
            });
            characters++;
        }
        return result.Length == 0 ? null : result.ToString();
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            var read = stream.Read(destination);
            if (read <= 0)
                throw Invalid("The FLAC metadata is truncated.");
            destination = destination[read..];
        }
    }

    private static void SkipExactly(Stream stream, int length)
    {
        if (stream.CanSeek)
        {
            if (stream.Position > stream.Length || stream.Length - stream.Position < length)
                throw Invalid("The FLAC metadata payload is truncated.");
            stream.Seek(length, SeekOrigin.Current);
            return;
        }

        Span<byte> scratch = stackalloc byte[4096];
        while (length > 0)
        {
            var count = Math.Min(scratch.Length, length);
            ReadExactly(stream, scratch[..count]);
            length -= count;
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
