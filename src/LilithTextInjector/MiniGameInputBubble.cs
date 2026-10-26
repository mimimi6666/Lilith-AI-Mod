using System;
using UnityEngine;
using Point = System.Numerics.Vector2;

namespace LilithTextInjector;

internal static partial class ChessPresentation
{
    internal static bool TryGetInputBubbleAnchor(out Camera? camera, out Rect work, out Vector2 boardTop)
    {
        camera = character?._mainCamera;
        work = default;
        boardTop = default;
        if (session == null || root == null || layout == null || closing || camera == null
            || !camera.orthographic || !OldMaidNativeBridge.IsSnackActive) return false;
        work = OldMaidWorkArea.Read();
        boardTop = new Vector2(workOffset.x + layout.X + layout.Width * 0.5f,
            workOffset.y + layout.Y + layout.Height);
        return true;
    }
}

internal static partial class DialogueManagerUpdatePatch
{
    private static void PositionMiniGameInputBubble()
    {
        if (_inputBubble == null || !ChessPresentation.TryGetInputBubbleAnchor(out var camera, out var work, out var anchor)) return;
        try
        {
            var rect = _inputBubble.GetComponent<RectTransform>();
            if (rect == null || camera == null) return;
            var pivot = camera.WorldToScreenPoint(rect.position);
            if (!float.IsFinite(pivot.z) || pivot.z <= camera.nearClipPlane || pivot.z >= camera.farClipPlane) return;
            var bounds = rect.rect;
            var low = new Point(float.PositiveInfinity, float.PositiveInfinity);
            var high = new Point(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < 4; i++)
            {
                var corner = rect.TransformPoint(new Vector3((i & 1) == 0 ? bounds.xMin : bounds.xMax,
                    (i & 2) == 0 ? bounds.yMin : bounds.yMax, 0));
                var pixel = camera.WorldToScreenPoint(corner);
                low = Point.Min(low, new Point(pixel.x, pixel.y));
                high = Point.Max(high, new Point(pixel.x, pixel.y));
            }
            if (!MiniGameInputBubbleLayout.TryPlace(new(work.xMin, work.yMin), new(work.xMax, work.yMax),
                new(anchor.x, anchor.y), new(pivot.x, pivot.y), low, high, out var positioned)) return;
            // The native bubble's lifted position is deliberately NOT copied a
            // second time. Only this disposable F7 field receives the mapping.
            rect.position = camera.ScreenToWorldPoint(new Vector3(positioned.X, positioned.Y, pivot.z));
        }
        catch (Exception) { /* Optional positioning must never consume F7 input. */ }
    }
}
