using System;
using System.Collections.Generic;
using System.Numerics;

namespace LilithTextInjector;

/// <summary>
/// Bounded decorative mesh math only. The unit card uses top-left coordinates;
/// texture UVs explicitly use bottom-left coordinates. All samples are mapped
/// by one projective homography, never bilinear quadrilateral interpolation.
/// A native triangle renderer still approximates perspective between samples.
/// </summary>
internal static class OldMaidSpatialMeshGeometry
{
    internal const int DefaultColumns = 12;
    internal const int DefaultRows = 18;
    internal const int MaxGridColumns = 16;
    internal const int MaxGridRows = 24;
    internal const int MaxVertices = 2048;
    internal const int MaxTriangleIndices = 12288;
    internal const int CornerSegments = 8;
    private const float MaximumCoordinate = 1000000f;
    private const double MinimumProjectedArea = 0.02d;

    /// <summary>
    /// Corners correspond to UV TL/TR/BR/BL, including an already-corrected
    /// back face. Either winding is valid; concave, crossing, edge-on and
    /// nonfinite quadrilaterals fail closed. No caller array is retained.
    /// </summary>
    internal static bool TryCreateMap(Vector2[]? corners, out OldMaidProjectiveMap map)
    {
        map = default;
        if (corners == null || corners.Length != 4) return false;
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        foreach (var corner in corners)
        {
            if (!Finite(corner.X) || !Finite(corner.Y) ||
                Math.Abs(corner.X) > MaximumCoordinate || Math.Abs(corner.Y) > MaximumCoordinate) return false;
            minimum = Vector2.Min(minimum, corner);
            maximum = Vector2.Max(maximum, corner);
        }
        var boundsArea = (double)(maximum.X - minimum.X) * (maximum.Y - minimum.Y);
        var tolerance = Math.Max(1e-7d, boundsArea * 1e-8d);
        var sign = 0;
        var twiceArea = 0d;
        for (var index = 0; index < 4; index++)
        {
            var a = corners[index];
            var b = corners[(index + 1) % 4];
            var c = corners[(index + 2) % 4];
            var cross = Cross(b - a, c - b);
            if (Math.Abs(cross) <= tolerance) return false;
            var currentSign = cross > 0d ? 1 : -1;
            if (sign != 0 && sign != currentSign) return false;
            sign = currentSign;
            twiceArea += (double)a.X * b.Y - (double)a.Y * b.X;
        }
        if (Math.Abs(twiceArea) * 0.5d <= MinimumProjectedArea) return false;

        var x0 = (double)corners[0].X; var y0 = (double)corners[0].Y;
        var x1 = (double)corners[1].X; var y1 = (double)corners[1].Y;
        var x2 = (double)corners[2].X; var y2 = (double)corners[2].Y;
        var x3 = (double)corners[3].X; var y3 = (double)corners[3].Y;
        var dx1 = x1 - x2; var dx2 = x3 - x2; var dx3 = x0 - x1 + x2 - x3;
        var dy1 = y1 - y2; var dy2 = y3 - y2; var dy3 = y0 - y1 + y2 - y3;
        var determinant = dx1 * dy2 - dx2 * dy1;
        if (Math.Abs(determinant) <= tolerance) return false;
        var g = (dx3 * dy2 - dx2 * dy3) / determinant;
        var h = (dx1 * dy3 - dx3 * dy1) / determinant;
        // The denominator is affine over the unit square. Checking all corners
        // proves there is no pole or sign change anywhere inside a card.
        if (1d + g <= 1e-6d || 1d + h <= 1e-6d || 1d + g + h <= 1e-6d) return false;
        map = new OldMaidProjectiveMap(x1 - x0 + g * x1, x3 - x0 + h * x3, x0,
            y1 - y0 + g * y1, y3 - y0 + h * y3, y0, g, h);
        return true;
    }

    /// <summary>
    /// Returns a cacheable grid clipped against a convex rounded perimeter.
    /// radiusWidthFraction and insetWidthFraction are fractions of the FULL
    /// card width. heightToWidth is 1.5 for the normal card, not its reciprocal.
    /// Points retain full-card coordinates; Uvs span the whole inset rectangle,
    /// so layering an inset art face does not crop the image. Each result owns
    /// its arrays; callers should cache them and treat them as immutable.
    /// </summary>
    internal static bool TryCreateRoundedTopology(int columns, int rows, float radiusWidthFraction,
        float heightToWidth, float insetWidthFraction, out OldMaidSpatialMeshTopology topology)
    {
        topology = OldMaidSpatialMeshTopology.Empty;
        if (columns < 1 || columns > MaxGridColumns || rows < 1 || rows > MaxGridRows ||
            !Finite(radiusWidthFraction) || !Finite(heightToWidth) || !Finite(insetWidthFraction) ||
            heightToWidth < 0.25f || heightToWidth > 4f || radiusWidthFraction < 0f ||
            radiusWidthFraction > Math.Min(0.5f, heightToWidth * 0.5f) ||
            insetWidthFraction < 0f || insetWidthFraction > 0.2f || insetWidthFraction * 2f >= Math.Min(1f, heightToWidth))
            return false;

        var left = insetWidthFraction;
        var top = insetWidthFraction / heightToWidth;
        var right = 1f - left;
        var bottom = 1f - top;
        var radiusX = Math.Max(0f, radiusWidthFraction - insetWidthFraction);
        var radiusY = radiusX / heightToWidth;
        var boundary = RoundedBoundary(left, top, right, bottom, radiusX, radiusY);
        var points = new List<Vector2>(Math.Min(MaxVertices, columns * rows * 5));
        var uvs = new List<Vector2>(points.Capacity);
        var indices = new List<int>(Math.Min(MaxTriangleIndices, columns * rows * 9));
        for (var row = 0; row < rows; row++)
        {
            var y0 = row == 0 ? top : top + (bottom - top) * row / rows;
            var y1 = row == rows - 1 ? bottom : top + (bottom - top) * (row + 1) / rows;
            for (var column = 0; column < columns; column++)
            {
                var x0 = column == 0 ? left : left + (right - left) * column / columns;
                var x1 = column == columns - 1 ? right : left + (right - left) * (column + 1) / columns;
                var polygon = Clip(boundary, axis: 0, x0, keepGreater: true);
                polygon = Clip(polygon, axis: 0, x1, keepGreater: false);
                polygon = Clip(polygon, axis: 1, y0, keepGreater: true);
                polygon = Clip(polygon, axis: 1, y1, keepGreater: false);
                RemoveAdjacentDuplicates(polygon);
                if (polygon.Count < 3) continue;
                if (points.Count + polygon.Count > MaxVertices) return false;
                var offset = points.Count;
                foreach (var point in polygon)
                {
                    points.Add(point);
                    // Polygon clipping already bounds the geometry. Clamp only
                    // normalized float roundoff at the texture's exact edges.
                    uvs.Add(new Vector2(Math.Clamp((point.X - left) / (right - left), 0f, 1f),
                        Math.Clamp(1f - (point.Y - top) / (bottom - top), 0f, 1f)));
                }
                for (var vertex = 1; vertex < polygon.Count - 1; vertex++)
                {
                    // Collinear clipping vertices are harmless but must not
                    // produce zero-area triangles or inconsistent winding.
                    if (Cross(polygon[vertex] - polygon[0], polygon[vertex + 1] - polygon[0]) <= 1e-12d) continue;
                    if (indices.Count + 3 > MaxTriangleIndices) return false;
                    indices.Add(offset); indices.Add(offset + vertex); indices.Add(offset + vertex + 1);
                }
            }
        }
        if (indices.Count == 0) return false;
        topology = new OldMaidSpatialMeshTopology(points.ToArray(), uvs.ToArray(), indices.ToArray());
        return true;
    }

    private static List<Vector2> RoundedBoundary(float left, float top, float right, float bottom, float radiusX, float radiusY)
    {
        if (radiusX == 0f)
            return new List<Vector2> { new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom), new Vector2(left, bottom) };
        var points = new List<Vector2>(4 * (CornerSegments + 1));
        var centers = new[]
        {
            new Vector2(left + radiusX, top + radiusY), new Vector2(right - radiusX, top + radiusY),
            new Vector2(right - radiusX, bottom - radiusY), new Vector2(left + radiusX, bottom - radiusY)
        };
        for (var corner = 0; corner < 4; corner++)
        {
            var start = Math.PI + corner * Math.PI * 0.5d;
            for (var sample = 0; sample <= CornerSegments; sample++)
            {
                var angle = start + sample * Math.PI * 0.5d / CornerSegments;
                var point = new Vector2(
                    Math.Clamp(centers[corner].X + radiusX * (float)Math.Cos(angle), left, right),
                    Math.Clamp(centers[corner].Y + radiusY * (float)Math.Sin(angle), top, bottom));
                // Cardinal endpoints lie exactly on an inset boundary. Avoid
                // cancellation such as (left + radius) - radius retaining a
                // tiny offset that would prevent the UVs reaching 0 or 1.
                if (sample == 0)
                {
                    if (corner == 0) point.X = left;
                    else if (corner == 1) point.Y = top;
                    else if (corner == 2) point.X = right;
                    else point.Y = bottom;
                }
                else if (sample == CornerSegments)
                {
                    if (corner == 0) point.Y = top;
                    else if (corner == 1) point.X = right;
                    else if (corner == 2) point.Y = bottom;
                    else point.X = left;
                }
                points.Add(point);
            }
        }
        return points;
    }

    private static List<Vector2> Clip(List<Vector2> input, int axis, float threshold, bool keepGreater)
    {
        var output = new List<Vector2>(input.Count + 2);
        if (input.Count == 0) return output;
        var previous = input[input.Count - 1];
        var previousDistance = Component(previous, axis) - threshold;
        var previousInside = keepGreater ? previousDistance >= 0f : previousDistance <= 0f;
        foreach (var current in input)
        {
            var currentDistance = Component(current, axis) - threshold;
            var currentInside = keepGreater ? currentDistance >= 0f : currentDistance <= 0f;
            if (currentInside != previousInside)
            {
                var fraction = (double)previousDistance / (previousDistance - currentDistance);
                var intersection = previous + (current - previous) * (float)fraction;
                // Shared cells must use the exact same clipping coordinate;
                // correcting this axis prevents float cracks along grid lines.
                if (axis == 0) intersection.X = threshold; else intersection.Y = threshold;
                output.Add(intersection);
            }
            if (currentInside) output.Add(current);
            previous = current; previousDistance = currentDistance; previousInside = currentInside;
        }
        return output;
    }

    private static void RemoveAdjacentDuplicates(List<Vector2> points)
    {
        for (var index = points.Count - 1; index >= 0 && points.Count > 1; index--)
        {
            var previous = (index + points.Count - 1) % points.Count;
            if (Vector2.DistanceSquared(points[index], points[previous]) < 1e-14f) points.RemoveAt(index);
        }
    }

    private static float Component(Vector2 point, int axis) => axis == 0 ? point.X : point.Y;
    private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal readonly struct OldMaidProjectiveMap
{
    private readonly double _a, _b, _c, _d, _e, _f, _g, _h;
    private readonly bool _valid;

    internal OldMaidProjectiveMap(double a, double b, double c, double d, double e, double f, double g, double h)
    { _a = a; _b = b; _c = c; _d = d; _e = e; _f = f; _g = g; _h = h; _valid = true; }

    internal bool TryMap(float u, float v, out Vector2 point)
    {
        point = Vector2.Zero;
        if (!_valid || float.IsNaN(u) || float.IsNaN(v) || u < 0f || u > 1f || v < 0f || v > 1f) return false;
        var denominator = _g * u + _h * v + 1d;
        if (double.IsNaN(denominator) || double.IsInfinity(denominator) || denominator <= 1e-6d) return false;
        var x = (_a * u + _b * v + _c) / denominator;
        var y = (_d * u + _e * v + _f) / denominator;
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) ||
            Math.Abs(x) > 1000000d || Math.Abs(y) > 1000000d) return false;
        point = new Vector2((float)x, (float)y);
        return true;
    }
}

internal sealed class OldMaidSpatialMeshTopology
{
    internal static readonly OldMaidSpatialMeshTopology Empty =
        new OldMaidSpatialMeshTopology(Array.Empty<Vector2>(), Array.Empty<Vector2>(), Array.Empty<int>());

    internal OldMaidSpatialMeshTopology(Vector2[] points, Vector2[] uvs, int[] triangles)
    { Points = points; Uvs = uvs; Triangles = triangles; }

    internal Vector2[] Points { get; }
    internal Vector2[] Uvs { get; }
    internal int[] Triangles { get; }
}
