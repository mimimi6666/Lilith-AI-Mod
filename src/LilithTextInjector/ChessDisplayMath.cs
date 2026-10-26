using System;

namespace LilithTextInjector;

internal static class ChessDisplayMath
{
    internal static float Scale(float originalHeight, float availableHeight)
    {
        if (!float.IsFinite(originalHeight) || !float.IsFinite(availableHeight) || originalHeight <= 0 || availableHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalHeight));
        return Math.Min(1, availableHeight / originalHeight);
    }
}
