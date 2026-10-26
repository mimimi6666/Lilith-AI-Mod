using System;
using System.IO;
using System.Security.Cryptography;

namespace LilithTextInjector;

internal enum ChessArtwork { Board, Clock, Pieces }
internal enum ChessArtFailure { None, RedirectedPath, EncodedSize, Header, Dimensions, Encoding, Identity, Decode, DecodedDimensions }
internal readonly record struct ChessArtSpec(string FileName, int Width, int Height, string Sha256);

internal sealed class ChessArtworkException : IOException
{
    internal ChessArtwork Artwork { get; }
    internal ChessArtFailure Failure { get; }
    internal ChessArtworkException(ChessArtwork artwork, ChessArtFailure failure)
        : base("Chess artwork admission failed.") { Artwork = artwork; Failure = failure; }
}

// These three fixed assets are landscape/square, not portrait playing cards.
// Keep their identities, dimensions and allocation budget independent of Old Maid.
internal static class ChessArtPolicy
{
    internal const int MaximumEncodedBytes = 16000000;

    internal static ChessArtSpec Describe(ChessArtwork artwork) => artwork switch
    {
        ChessArtwork.Board => new("chessboard-sofa-pink-8x8.png", 1536, 1024,
            "D632B7BADCD9FAF793FC97EAAE6BE5B3FA25ED0CDDAA586FF5079561C12FFA56"),
        ChessArtwork.Clock => new("chess-clock-left-shell-v1.png", 1254, 1254,
            "0FCD710F1AE39FF8034AC76097040284A5D94A7FAD33982BAEF885B859581E35"),
        ChessArtwork.Pieces => new("chess-pieces-white-pink-flat-v2.png", 1536, 1024,
            "2E7B1270854127C2F02E61527E8022E5CB0EB8A26D4CF17D3A7A22F438ED9879"),
        _ => throw new ArgumentOutOfRangeException(nameof(artwork))
    };

    internal static ChessArtFailure ValidateHeader(ChessArtwork artwork, ReadOnlySpan<byte> header)
    {
        var spec = Describe(artwork);
        if (header.Length < 33 || header[0] != 137 || header[1] != 80 || header[2] != 78
            || header[3] != 71 || header[4] != 13 || header[5] != 10 || header[6] != 26
            || header[7] != 10 || ReadBigEndian(header, 8) != 13
            || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
            return ChessArtFailure.Header;
        if (ReadBigEndian(header, 16) != spec.Width || ReadBigEndian(header, 20) != spec.Height)
            return ChessArtFailure.Dimensions;
        if (header[24] != 8 || (header[25] != 2 && header[25] != 6)
            || header[26] != 0 || header[27] != 0 || header[28] > 1)
            return ChessArtFailure.Encoding;
        return ChessArtFailure.None;
    }

    internal static ChessArtFailure Validate(ChessArtwork artwork, ReadOnlySpan<byte> bytes)
    {
        var spec = Describe(artwork);
        if (bytes.Length is < 33 or > MaximumEncodedBytes) return ChessArtFailure.EncodedSize;
        var header = ValidateHeader(artwork, bytes);
        if (header != ChessArtFailure.None) return header;
        return Convert.ToHexString(SHA256.HashData(bytes)) == spec.Sha256
            ? ChessArtFailure.None : ChessArtFailure.Identity;
    }

    // The renderer and offline verification use this exact read/admission path.
    // A single read-only handle holds length and bytes together; never allocate
    // from an unchecked file length, and never accept a header in lieu of a hash.
    internal static byte[] ReadFile(string directory, ChessArtwork artwork)
    {
        var spec = Describe(artwork);
        var path = Path.Combine(directory, spec.FileName);
        foreach (var target in new[] { directory, path })
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new ChessArtworkException(artwork, ChessArtFailure.RedirectedPath);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 33 or > MaximumEncodedBytes)
            throw new ChessArtworkException(artwork, ChessArtFailure.EncodedSize);
        var bytes = new byte[checked((int)stream.Length)];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
        var failure = Validate(artwork, bytes);
        if (failure != ChessArtFailure.None) throw new ChessArtworkException(artwork, failure);
        return bytes;
    }

    private static uint ReadBigEndian(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
}
