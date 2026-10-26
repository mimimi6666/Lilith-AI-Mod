using System;
using System.Collections.Generic;
using System.Numerics;

namespace LilithTextInjector;

internal readonly record struct ChessMoveGesture(int From, int To, int Revision);

// Local input state only. A gesture proposes a move; the current rules/clock
// still decide whether it may commit. No Unity capture or native input writes.
internal sealed class ChessInteraction
{
    internal int Selected { get; private set; } = -1;
    internal bool Pressed { get; private set; }
    internal bool Dragging { get; private set; }
    internal Vector2 DragFoot { get; private set; }
    private Vector2 down, grab;
    private int pressSquare = -1, revision = -1;
    private bool ownPress;

    internal void Press(int square, bool own, int currentRevision, Vector2 point, Vector2 foot)
    {
        if (!Finite(point) || !Finite(foot) || square is < 0 or >= 64) { Cancel(); return; }
        if (revision != currentRevision) Selected = -1;
        revision = currentRevision;
        ownPress = own;
        if (own) Selected = square;
        Pressed = Selected >= 0;
        Dragging = false;
        down = point; grab = point - foot; DragFoot = foot;
        pressSquare = square;
    }

    internal void Move(Vector2 point, float threshold)
    {
        if (!Pressed || !ownPress || !Finite(point) || !float.IsFinite(threshold)) return;
        if (Vector2.DistanceSquared(down, point) >= MathF.Pow(Math.Max(3, threshold), 2)) Dragging = true;
        if (Dragging) DragFoot = point - grab;
    }

    internal ChessMoveGesture? Release(int square, int currentRevision)
    {
        if (!Pressed || revision != currentRevision) { Cancel(); return null; }
        var wasDragging = Dragging;
        Pressed = Dragging = false;
        if (square is < 0 or >= 64 || square == Selected) return null;
        if (!wasDragging && (ownPress || square != pressSquare)) return null;
        return new ChessMoveGesture(Selected, square, revision);
    }

    internal void Cancel() { Selected = pressSquare = revision = -1; Pressed = Dragging = false; }

    internal static bool[] Targets(int selected, IReadOnlyList<string> legal)
    {
        var result = new bool[64];
        if (selected is < 0 or >= 64) return result;
        var prefix = ChessRules.Square(selected);
        foreach (var move in legal)
            if (move.Length is 4 or 5 && move.StartsWith(prefix, StringComparison.Ordinal))
            {
                var target = ChessRules.Square(move.Substring(2, 2));
                if (target is >= 0 and < 64) result[target] = true;
            }
        return result;
    }
    private static bool Finite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
}

internal sealed class ChessDoubleClick
{
    private double last = double.NegativeInfinity;
    private Vector2 position;
    internal bool Press(double now, Vector2 point, float tolerance)
    {
        if (!double.IsFinite(now) || !float.IsFinite(point.X) || !float.IsFinite(point.Y)
            || !float.IsFinite(tolerance) || tolerance < 0) { Reset(); return false; }
        var match = now >= last && now - last <= 0.42 && Vector2.DistanceSquared(point, position) <= tolerance * tolerance;
        last = match ? double.NegativeInfinity : now; position = point;
        return match;
    }
    internal void Reset() => last = double.NegativeInfinity;
}
