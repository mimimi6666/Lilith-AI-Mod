using System;
using System.Globalization;

namespace LilithTextInjector;

// Fixed, local presentation assets only. Neither file names nor decode budgets
// come from model text, saved preferences, card artwork or a manifest.
internal static class OldMaidCardArtPolicy
{
    internal const int MaximumEncodedBytes = 8 * 1024 * 1024;
    internal const int MaximumWidth = 1024;
    internal const int MaximumHeight = 1536;
    internal const float HeightToWidth = 1.5f;
    internal const float CornerRadiusFraction = 0.055f;

    internal static string Rank(OldMaidCard card) => card.IsJoker ? "JOKER"
        : card.Rank == 1 ? "A" : card.Rank == 11 ? "J" : card.Rank == 12 ? "Q"
        : card.Rank == 13 ? "K" : card.Rank.ToString(CultureInfo.InvariantCulture);

    internal static string FileName(OldMaidCard card, bool back) =>
        back ? "card-back-v1.png" : Rank(card) + ".png";

    // Keep the established two-copies-per-rank deck: one heart and one spade.
    // Four-suit preview selection never changes the actual 27-card rules.
    internal static string Index(OldMaidCard card) => card.IsJoker ? "J\nO\nK\nE\nR"
        : Rank(card) + "\n" + (card.Id % 2 == 0 ? "♥" : "♠");

    internal static bool TryReadPngDimensions(ReadOnlySpan<byte> header, out int width, out int height)
    {
        width = height = 0;
        if (header.Length < 33 || header[0] != 137 || header[1] != 80 || header[2] != 78
            || header[3] != 71 || header[4] != 13 || header[5] != 10 || header[6] != 26
            || header[7] != 10 || ReadBigEndian(header, 8) != 13
            || header[12] != 73 || header[13] != 72 || header[14] != 68 || header[15] != 82)
            return false;
        var w = ReadBigEndian(header, 16);
        var h = ReadBigEndian(header, 20);
        if (w < 16 || h < 24 || w > MaximumWidth || h > MaximumHeight || w * 3 != h * 2
            || header[24] != 8 || (header[25] != 2 && header[25] != 6)
            || header[26] != 0 || header[27] != 0 || header[28] > 1)
            return false;
        width = (int)w;
        height = (int)h;
        return true;
    }

    private static uint ReadBigEndian(ReadOnlySpan<byte> data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
        | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
