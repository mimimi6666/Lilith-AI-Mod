using System;
using System.Numerics;

namespace LilithTextInjector;

// Coordinates use bottom-left screen origin. The PNG's measured grid is a
// trapezoid with measured row boundaries. The same map drives sprites/picking;
// artwork is not assumed to be a mathematically perfect camera projection.
internal sealed class ChessLayout
{
    // Measured from the exact admitted board PNG: top-origin row 894 is the
    // last nonzero alpha row, so its bottom 129 rows are completely transparent.
    // Keep full-image coordinates for the calibrated grid; crop only that
    // empty strip from the displayed sprite and its pointer rectangle.
    internal const int BoardTransparentBottom = 129;
    internal float X { get; }
    internal float Y { get; }
    internal float Width { get; }
    internal float Height => Width * 1024 / 1536;
    internal float BoardBottomInset => Height * BoardTransparentBottom / 1024;
    internal float SeatBottom => Y + Height * 0.88f;
    internal float SeatAvailableHeight { get; }
    internal ChessLayout(float screenWidth, float screenHeight, float centerX)
    {
        Width = Math.Min(screenWidth * 0.62f, screenHeight * 0.95f);
        Width = Math.Max(1, Width);
        X = Math.Clamp(centerX - Width * 0.5f, screenWidth * 0.17f, Math.Max(screenWidth * 0.17f, screenWidth - Width - 8));
        // Preserve the established character size, then translate the entire
        // arrangement down. Extra headroom from lowering must not enlarge her.
        var priorBottom = Math.Max(12, screenHeight * 0.025f);
        SeatAvailableHeight = screenHeight - (priorBottom + Height * 0.88f) - 56;
        // Work-area coordinates already include the taskbar-safe inset. Align
        // the painted board edge, not the transparent PNG canvas, to that edge.
        Y = -BoardBottomInset;
    }

    private static readonly float[] Rows = { 258, 350, 442, 529, 613, 694, 774, 848, 924 };
    // Intersections measured on the accepted 1536x1024 board at image y=136
    // and y=720 (top origin). Its painted columns are not perfectly uniform.
    private static readonly float[] FarColumns = { 298, 414.5f, 531.5f, 649, 766.5f, 884, 1001.5f, 1119, 1237 };
    private static readonly float[] NearColumns = { 173.5f, 329, 477, 623, 766.5f, 912, 1057, 1203.5f, 1360 };
    private static float Column(int index, float imageY)
        => FarColumns[index] + (NearColumns[index] - FarColumns[index]) * (888 - imageY) / 584;
    // Piecewise perspective map calibrated to the accepted PNG grid.
    internal Vector2 Grid(float u, float v)
    {
        var row = Math.Clamp((int)(v * 8), 0, 7);
        var imageY = Rows[row] + (Rows[row + 1] - Rows[row]) * (v * 8 - row);
        var column = Math.Clamp((int)(u * 8), 0, 7);
        var imageX = Column(column, imageY) + (Column(column + 1, imageY) - Column(column, imageY)) * (u * 8 - column);
        return new Vector2(X + imageX / 1536 * Width, Y + imageY / 1024 * Height);
    }

    internal Vector2 SquareCenter(int square, bool playerWhite)
    {
        var display = playerWhite ? square : 63 - square;
        return Grid((display % 8 + 0.5f) / 8, (display / 8 + 0.5f) / 8);
    }

    internal int Hit(float x, float y, bool playerWhite)
    {
        var imageY = (y - Y) * 1024 / Height;
        if (!float.IsFinite(x) || !float.IsFinite(imageY) || imageY < Rows[0] || imageY >= Rows[8]) return -1;
        var row = 0;
        while (row < 7 && imageY >= Rows[row + 1]) row++;
        var imageX = (x - X) * 1536 / Width;
        if (imageX < Column(0, imageY) || imageX >= Column(8, imageY)) return -1;
        var column = 0;
        while (column < 7 && imageX >= Column(column + 1, imageY)) column++;
        var index = row * 8 + column;
        return playerWhite ? index : 63 - index;
    }

    internal float CellWidth(int square, bool playerWhite)
    {
        var display = playerWhite ? square : 63 - square;
        var rank = display / 8; var file = display % 8;
        return Grid((file + 1f) / 8, (rank + 0.5f) / 8).X - Grid(file / 8f, (rank + 0.5f) / 8).X;
    }
}
