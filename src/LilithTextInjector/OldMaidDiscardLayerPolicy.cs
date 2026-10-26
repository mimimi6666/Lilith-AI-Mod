using System;
using System.Globalization;
using System.Numerics;

namespace LilithTextInjector;

// Pure validation only. These numbers authorize updates to an owned discard
// canvas, never writes to a native camera, renderer, material or character.
internal static class OldMaidDiscardLayerPolicy
{
    // Spine repeats material references for ordered draw slots: the native
    // animated character already uses 65-85 slots, not 65-85 unique assets.
    // This is only a corruption/work-budget ceiling, never an asset-count cap.
    // Every admitted slot still requires shader, tag and queue validation.
    internal const int MaximumMaterials = 4096;
    internal const int MinimumOwnedQueue = 2501, MaximumOwnedQueue = 4999;

    internal static bool KnownCharacterShader(string? shader, string? renderType) =>
        KnownCharacterShaderName(shader) && KnownCharacterRenderType(renderType);

    internal static bool KnownCharacterShaderName(string? shader) =>
        string.Equals(shader, "Spine/Skeleton", StringComparison.Ordinal);

    internal static bool KnownCharacterRenderType(string? renderType) =>
        string.Equals(renderType, "Transparent", StringComparison.Ordinal);

    internal static bool ValidOwnedQueue(int queue) => queue >= MinimumOwnedQueue && queue <= MaximumOwnedQueue;

    internal static bool TryOrdering(int layer, int nativeOrder, int[] queues,
        out int order, out int queue, out string reason)
    {
        queue = 0;
        if (queues == null) { order = 0; reason = "material_count_empty"; return false; }
        if (!TryBeginOrdering(layer, nativeOrder, queues.Length, out order, out var minimum, out reason)) return false;
        foreach (var value in queues)
        {
            if (!TryIncludeMaterialQueue(value, ref minimum)) { order = 0; reason = "material_queue"; return false; }
        }
        return TryFinishOrdering(minimum, out queue, out reason);
    }

    // The native adapter visits EVERY current material slot and accumulates
    // only numeric queue values. No per-frame queues[] allocation or cached
    // previous-frame permission is required.
    internal static bool TryBeginOrdering(int layer, int nativeOrder, int materialCount,
        out int order, out int minimumQueue, out string reason)
    {
        order = 0; minimumQueue = MaximumOwnedQueue + 1;
        reason = "sorting";
        if (layer < 0 || layer > 31 || nativeOrder <= short.MinValue || nativeOrder > short.MaxValue) return false;
        reason = "material_count_empty";
        if (materialCount < 1) return false;
        reason = "material_count_limit";
        if (materialCount > MaximumMaterials) return false;
        order = nativeOrder - 1;
        reason = "ready";
        return true;
    }

    internal static bool TryIncludeMaterialQueue(int nativeQueue, ref int minimumQueue)
    {
        if (nativeQueue < MinimumOwnedQueue + 1 || nativeQueue > MaximumOwnedQueue + 1 ||
            minimumQueue < MinimumOwnedQueue + 1 || minimumQueue > MaximumOwnedQueue + 1) return false;
        minimumQueue = Math.Min(minimumQueue, nativeQueue);
        return true;
    }

    internal static bool TryFinishOrdering(int minimumQueue, out int queue, out string reason)
    {
        queue = 0; reason = "material_queue";
        if (minimumQueue < MinimumOwnedQueue + 1 || minimumQueue > MaximumOwnedQueue + 1) return false;
        queue = minimumQueue - 1;
        reason = "ready";
        return true;
    }

    // Metadata only. Shader/tag classes are 0=unread, 1=known, 2=other;
    // no native asset name, hierarchy, arbitrary tag or exception text leaks.
    // Formatting is lazy: callers request it only for a bounded state log.
    internal static string MaterialDiagnostic(int count, int slot, int queue, int shaderClass, int tagClass) =>
        string.Format(CultureInfo.InvariantCulture, "count={0};slot={1};queue={2};shader={3};tag={4}",
            Math.Clamp(count, -1, 65535), Math.Clamp(slot, -1, MaximumMaterials - 1),
            Math.Clamp(queue, -1, 65535), Math.Clamp(shaderClass, 0, 2), Math.Clamp(tagClass, 0, 2));

    internal static bool TryPlaneDepth(float near, float far, float nearest, float farthest, out float depth)
    {
        depth = 0;
        if (!Finite(near) || !Finite(far) || !Finite(nearest) || !Finite(farthest) ||
            near <= 0 || far > 1000000f || far <= near || nearest <= near || farthest < nearest || farthest >= far)
            return false;
        var clearance = Math.Clamp((farthest - nearest) * 0.05f, 0.05f, 1f);
        depth = farthest + clearance;
        if (!Finite(depth) || depth <= farthest || depth + clearance >= far) { depth = 0; return false; }
        return true;
    }

    internal static bool FullViewport(int width, int height, float x, float y, float w, float h) =>
        width >= 1 && height >= 1 && width <= 65536 && height <= 65536 &&
        Finite(x) && Finite(y) && Finite(w) && Finite(h) &&
        Math.Abs(x) <= 0.25f && Math.Abs(y) <= 0.25f &&
        Math.Abs(w - width) <= 0.25f && Math.Abs(h - height) <= 0.25f;

    internal static bool FullNormalizedViewport(float x, float y, float width, float height) =>
        Finite(x) && Finite(y) && Finite(width) && Finite(height) &&
        Math.Abs(x) <= 0.00001f && Math.Abs(y) <= 0.00001f &&
        Math.Abs(width - 1) <= 0.00001f && Math.Abs(height - 1) <= 0.00001f;

    internal static bool TryBasis(int width, int height, Vector3 bottomLeft, Vector3 bottomRight,
        Vector3 topLeft, Vector3 topRight, out OldMaidDiscardBasis basis)
    {
        basis = default;
        if (!FullViewport(width, height, 0, 0, width, height) ||
            !Finite(bottomLeft) || !Finite(bottomRight) || !Finite(topLeft) || !Finite(topRight)) return false;
        var right = bottomRight - bottomLeft;
        var up = topLeft - bottomLeft;
        var horizontal = right.Length();
        var vertical = up.Length();
        if (!Finite(horizontal) || !Finite(vertical) || horizontal < 0.00001f || vertical < 0.00001f ||
            horizontal > 1000000f || vertical > 1000000f) return false;
        right /= horizontal;
        up /= vertical;
        if (Math.Abs(Vector3.Dot(right, up)) > 0.0001f ||
            Vector3.Distance(topRight, bottomLeft + right * horizontal + up * vertical) >
                Math.Max(horizontal, vertical) * 0.0001f) return false;
        basis = new OldMaidDiscardBasis(bottomLeft, right, up, horizontal / width, vertical / height);
        return true;
    }

    internal static bool RoundTrips(Vector3 screen, float x, float y, float depth) =>
        Finite(screen) && Finite(depth) && depth > 0 &&
        Math.Abs(screen.X - x) <= 0.25f && Math.Abs(screen.Y - y) <= 0.25f &&
        Math.Abs(screen.Z - depth) <= Math.Max(0.001f, depth * 0.0001f);

    internal static bool Finite(Vector3 point) => Finite(point.X) && Finite(point.Y) && Finite(point.Z) &&
        Math.Abs(point.X) <= 1000000f && Math.Abs(point.Y) <= 1000000f && Math.Abs(point.Z) <= 1000000f;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal readonly struct OldMaidDiscardBasis
{
    internal OldMaidDiscardBasis(Vector3 origin, Vector3 right, Vector3 up, float scaleX, float scaleY)
    { Origin = origin; Right = right; Up = up; ScaleX = scaleX; ScaleY = scaleY; }
    internal Vector3 Origin { get; }
    internal Vector3 Right { get; }
    internal Vector3 Up { get; }
    internal float ScaleX { get; }
    internal float ScaleY { get; }
}
