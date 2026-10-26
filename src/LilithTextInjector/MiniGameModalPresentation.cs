using System;
using System.Numerics;

namespace LilithTextInjector;

// Input ownership is not visibility. Only our F7 field (and its opening key
// frame) may keep the mini-game visible; unrelated modal owners retain priority.
internal readonly record struct MiniGameModalState(bool InputBlocked, bool HidePresentation);

internal static class MiniGameModalPresentation
{
    internal static MiniGameModalState Evaluate(bool textEntryActive, bool textEntryKeyHeld,
        bool otherModal, bool textFocus)
    {
        var hidden = otherModal || (textFocus && !textEntryActive);
        return new MiniGameModalState(hidden || textEntryActive || textEntryKeyHeld, hidden);
    }
}

// Screen coordinates only. Move the owned input field, never the board, native
// bubble, character or camera. Bounds are measured after canvas scaling.
internal static class MiniGameInputBubbleLayout
{
    internal static bool TryPlace(Vector2 workMin, Vector2 workMax, Vector2 boardTop,
        Vector2 pivot, Vector2 bubbleMin, Vector2 bubbleMax, out Vector2 position)
    {
        position = pivot;
        if (!Finite(workMin) || !Finite(workMax) || !Finite(boardTop) || !Finite(pivot)
            || !Finite(bubbleMin) || !Finite(bubbleMax)) return false;
        var size = bubbleMax - bubbleMin;
        var available = workMax - workMin;
        if (size.X <= 0 || size.Y <= 0 || available.X < size.X || available.Y < size.Y) return false;
        var half = size * 0.5f;
        var center = new Vector2(
            Math.Clamp(boardTop.X, workMin.X + half.X, workMax.X - half.X),
            Math.Clamp(boardTop.Y + half.Y + 12, workMin.Y + half.Y, workMax.Y - half.Y));
        position = pivot + center - (bubbleMin + half);
        return Finite(position);
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
