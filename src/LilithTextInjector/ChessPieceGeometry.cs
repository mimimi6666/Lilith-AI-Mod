using System;
using System.Numerics;

namespace LilithTextInjector;

internal readonly record struct ChessPieceAnchor(Vector2 Pivot, float BaseWidth);

// Measures the opaque bottom band, not the tile centre or translucent haze.
// Arrays use bottom-left origin, the same convention as Unity texture pixels.
internal static class ChessPieceGeometry
{
    internal const int TileWidth = 256, TileHeight = 512, HaloPad = 8;
    internal const int HaloWidth = 128 + HaloPad * 2, HaloHeight = 256 + HaloPad * 2;

    internal static ChessPieceAnchor Measure(byte[] alpha)
    {
        if (alpha.Length != TileWidth * TileHeight) throw new ArgumentException("Unexpected chess tile.");
        var bottom = -1;
        for (var y = 0; y < TileHeight && bottom < 0; y++)
        {
            var count = 0;
            for (var x = 0; x < TileWidth; x++) if (alpha[y * TileWidth + x] >= 220) count++;
            if (count > 16) bottom = y;
        }
        if (bottom < 0 || bottom + 35 >= TileHeight) throw new ArgumentException("Missing chess base.");
        var left = TileWidth; var right = -1;
        for (var y = bottom; y <= bottom + 35; y++)
        for (var x = 0; x < TileWidth; x++)
            if (alpha[y * TileWidth + x] >= 220) { left = Math.Min(left, x); right = Math.Max(right, x); }
        // Contact centre is just inside the bottom ellipse. All sprite types
        // then share this same board/shadow anchor, irrespective of padding.
        return new ChessPieceAnchor(new Vector2((left + right + 1) / 512f, (bottom + 8f) / TileHeight),
            (right - left + 1) / (float)TileWidth);
    }

    internal static byte[] Halo(byte[] alpha)
    {
        if (alpha.Length != TileWidth * TileHeight) throw new ArgumentException("Unexpected chess tile.");
        var mask = new float[HaloWidth * HaloHeight];
        for (var y = 0; y < 256; y++) for (var x = 0; x < 128; x++)
        {
            var a = alpha[(y * 2) * TileWidth + x * 2];
            mask[(y + HaloPad) * HaloWidth + x + HaloPad] = a >= 180 ? 1 : 0;
        }
        var expanded = new float[mask.Length];
        for (var y = 0; y < HaloHeight; y++) for (var x = 0; x < HaloWidth; x++)
            if (mask[y * HaloWidth + x] > 0)
                for (var dy = -3; dy <= 3; dy++) for (var dx = -3; dx <= 3; dx++)
                    if (x + dx >= 0 && x + dx < HaloWidth && y + dy >= 0 && y + dy < HaloHeight)
                        expanded[(y + dy) * HaloWidth + x + dx] = 1;
        var blur = new float[mask.Length];
        for (var y = 0; y < HaloHeight; y++) for (var x = 0; x < HaloWidth; x++)
        {
            float sum = 0;
            for (var dx = -3; dx <= 3; dx++) if (x + dx >= 0 && x + dx < HaloWidth) sum += expanded[y * HaloWidth + x + dx];
            blur[y * HaloWidth + x] = sum / 7;
        }
        var result = new byte[mask.Length];
        for (var y = 0; y < HaloHeight; y++) for (var x = 0; x < HaloWidth; x++)
        {
            float sum = 0;
            for (var dy = -3; dy <= 3; dy++) if (y + dy >= 0 && y + dy < HaloHeight) sum += blur[(y + dy) * HaloWidth + x];
            result[y * HaloWidth + x] = (byte)(Math.Clamp(sum / 7 * (1 - mask[y * HaloWidth + x]), 0, 1) * 235);
        }
        return result;
    }
}
