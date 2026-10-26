using System;
using System.Numerics;

namespace LilithTextInjector;

/// <summary>
/// Presentation-only continuity from a visible UI card into the accepted
/// spatial trajectory. For at most 0.22 seconds, a decaying corner residual
/// joins the exact visible origin to the core projection. This brief bridge
/// is not physical geometry. It never changes world state, depth, the floor
/// shadow, or any settled projection, and owns no independent clock.
/// </summary>
internal sealed class OldMaidSpatialLaunch
{
    internal const float Duration = 0.22f;
    private readonly int _cardId;
    private readonly int _owner;
    private readonly OldMaidCardPose _source;
    private readonly float _captureWidth;
    private readonly float _captureHeight;
    private readonly Vector3[] _initialWorldCorners;
    private readonly bool _initialFrontFacing;

    private OldMaidSpatialLaunch(OldMaidSpatialBody body, OldMaidCardPose source,
        float width, float height, Vector3[] initialWorldCorners)
    {
        _cardId = body.CardId;
        _owner = body.Owner;
        _source = source;
        _captureWidth = width;
        _captureHeight = height;
        _initialWorldCorners = initialWorldCorners;
        _initialFrontFacing = Vector3.Dot(body.Normal, OldMaidSpatialDrop.CameraPosition - body.Position) >= 0f;
    }

    /// <summary>
    /// Capture immediately after Emit, before the first Step. Waiting bodies
    /// are supported without making them visible or changing their delay.
    /// Source uses work-area-relative bottom-left/Y-up UI coordinates and
    /// counter-clockwise degrees. Only this conversion knows about UI poses.
    /// </summary>
    internal static bool TryCapture(OldMaidSpatialBody body, OldMaidCardPose source,
        float viewportWidth, float viewportHeight, out OldMaidSpatialLaunch? launch)
    {
        launch = null;
        if (body == null || !ValidViewport(viewportWidth, viewportHeight) || !ValidSource(source, viewportWidth, viewportHeight) ||
            body.CardId < 0 || body.CardId >= OldMaidSpatialDrop.MaxBodies || body.Owner < 0 || body.Owner > 1 ||
            body.Age != 0f || (body.Phase != OldMaidSpatialPhase.Waiting && body.Phase != OldMaidSpatialPhase.Airborne) ||
            !Finite(body.Normal.X) || !Finite(body.Normal.Y) || !Finite(body.Normal.Z)) return false;
        var corners = body.WorldCorners();
        foreach (var corner in corners)
            if (Math.Abs(corner.X) > 12f || corner.Y < 0f || corner.Y > 10f || corner.Z < -2f || corner.Z > 12f ||
                !OldMaidSpatialDrop.TryProjectPoint(corner, viewportWidth, viewportHeight, out _, out _)) return false;
        launch = new OldMaidSpatialLaunch(body, source, viewportWidth, viewportHeight, corners);
        return true;
    }

    /// <summary>
    /// Output is viewport-relative TOP-LEFT/Y-down pixels. The renderer adds
    /// its work-area offset and converts Y once. TextureCorners already carry
    /// the core's back-face UV reversal. False means do not draw this card.
    /// The caller supplies the core body's Age, so pause and pair timing remain
    /// shared with that model. Never apply this residual to ShadowCorners.
    /// </summary>
    internal bool TryApply(OldMaidSpatialProjection projection, float bodyAge,
        float viewportWidth, float viewportHeight, out Vector2[] physicalCorners, out Vector2[] textureCorners)
    {
        physicalCorners = Array.Empty<Vector2>();
        textureCorners = Array.Empty<Vector2>();
        if (projection == null || projection.CardId != _cardId || projection.Owner != _owner ||
            projection.Phase < OldMaidSpatialPhase.Airborne || projection.Phase > OldMaidSpatialPhase.Settled ||
            !ValidViewport(viewportWidth, viewportHeight) ||
            !Finite(bodyAge) || bodyAge < 0f || !ValidQuad(projection.Corners)) return false;

        physicalCorners = (Vector2[])projection.Corners.Clone();
        // Never compensate an already-contacting or resting card, even if a
        // caller supplies a stale age. The approved support geometry wins.
        if (projection.Phase == OldMaidSpatialPhase.Airborne && bodyAge < Duration)
        {
            var progress = bodyAge / Duration;
            var eased = progress * progress * progress * (progress * (progress * 6f - 15f) + 10f);
            var weight = 1f - Math.Clamp(eased, 0f, 1f);
            var source = SourceCorners(viewportWidth, viewportHeight);
            if (!_initialFrontFacing) ReverseSurface(source);
            for (var index = 0; index < 4; index++)
            {
                if (!OldMaidSpatialDrop.TryProjectPoint(_initialWorldCorners[index], viewportWidth,
                    viewportHeight, out var initial, out _))
                {
                    physicalCorners = Array.Empty<Vector2>();
                    return false;
                }
                physicalCorners[index] += (source[index] - initial) * weight;
            }
        }
        if (!ValidQuad(physicalCorners))
        {
            physicalCorners = Array.Empty<Vector2>();
            return false;
        }
        textureCorners = (Vector2[])physicalCorners.Clone();
        if (!projection.FrontFacing) ReverseSurface(textureCorners);
        return true;
    }

    private Vector2[] SourceCorners(float width, float height)
    {
        // Resize only the saved visual anchor: center follows normalized work
        // coordinates, while a uniform fit ratio retains the card's aspect and
        // rotation. Reprojecting the saved world corners separately is crucial
        // when the viewport aspect changes and the camera's focal limit changes.
        var scale = Math.Min(width / _captureWidth, height / _captureHeight);
        var center = new Vector2(_source.X / _captureWidth * width, height - _source.Y / _captureHeight * height);
        var halfWidth = _source.Width * scale * 0.5f;
        var halfHeight = _source.Height * scale * 0.5f;
        var radians = _source.Angle * Math.PI / 180d;
        var cosine = (float)Math.Cos(radians);
        var sine = (float)Math.Sin(radians);
        var local = new[] { new Vector2(-halfWidth, halfHeight), new Vector2(halfWidth, halfHeight),
            new Vector2(halfWidth, -halfHeight), new Vector2(-halfWidth, -halfHeight) };
        for (var index = 0; index < 4; index++)
        {
            var point = local[index];
            local[index] = center + new Vector2(point.X * cosine - point.Y * sine, -point.X * sine - point.Y * cosine);
        }
        return local;
    }

    private static void ReverseSurface(Vector2[] quad)
    {
        (quad[0], quad[1]) = (quad[1], quad[0]);
        (quad[2], quad[3]) = (quad[3], quad[2]);
    }

    private static bool ValidSource(OldMaidCardPose source, float width, float height) =>
        Finite(source.X) && Finite(source.Y) && Finite(source.Angle) && Math.Abs(source.Angle) <= 3600f &&
        Finite(source.Width) && source.Width >= 1f && source.Width <= Math.Min(4096f, width * 2f) &&
        Finite(source.Height) && source.Height >= 1f && source.Height <= Math.Min(4096f, height * 2f) &&
        source.X >= -source.Width && source.X <= width + source.Width &&
        source.Y >= -source.Height && source.Y <= height + source.Height;
    private static bool ValidViewport(float width, float height) =>
        Finite(width) && Finite(height) && width >= 1f && height >= 1f && width <= 65536f && height <= 65536f;
    private static bool ValidQuad(Vector2[] quad)
    {
        if (quad == null || quad.Length != 4) return false;
        foreach (var corner in quad)
            if (!Finite(corner.X) || !Finite(corner.Y) || Math.Abs(corner.X) > 1048576f || Math.Abs(corner.Y) > 1048576f) return false;
        return true;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
