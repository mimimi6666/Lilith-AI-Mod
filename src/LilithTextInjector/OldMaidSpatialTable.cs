using System;
using System.Numerics;

namespace LilithTextInjector;

/// <summary>
/// Pure presentation calibration for the existing decorative spatial model.
/// The source hand determines a persistent card scale; a low camera places
/// its floor near the bottom of the work area. A fixed uniform transform
/// preserves the model's support-point hinge; airborne-only rigid fitting
/// may keep an earlier displayed trajectory inside the viewport.
/// This does not add physical collisions or character/card occlusion.
/// </summary>
internal sealed class OldMaidSpatialTable
{
    internal const float Duration = 0.45f;
    internal const float SourceScale = 0.82f;
    internal const float FloorScreenAnchor = 0.88f;
    internal static readonly Vector3 CameraPosition = new Vector3(0f, 8f, -18f);
    internal static readonly Vector3 CameraForward = Vector3.Normalize(new Vector3(0f, -8f, 22f));
    internal static readonly Vector3 CameraRight = Vector3.UnitX;
    internal static readonly Vector3 CameraUp = Vector3.Cross(CameraForward, CameraRight);
    private readonly OldMaidSpatialBody _body;
    private readonly OldMaidCardPose _source;
    private readonly float _captureWidth, _captureHeight;
    private readonly Vector3 _initialPosition, _floorAnchor, _landing;
    private readonly bool _initialFront;
    internal float ScatterFraction { get; }

    private OldMaidSpatialTable(OldMaidSpatialBody body, OldMaidCardPose source,
        float width, float height, int ordinal, Vector3 landing)
    {
        _body = body;
        _source = source;
        _captureWidth = width;
        _captureHeight = height;
        _initialPosition = body.Position;
        _floorAnchor = new Vector3(body.Position.X, 0f, body.Position.Z);
        _landing = landing;
        _initialFront = Vector3.Dot(body.Normal, CameraPosition - body.Position) >= 0f;
        ScatterFraction = CaptureScatter(body, ordinal);
    }

    /// <summary>
    /// Capture immediately after Emit, including delayed/Waiting cards. Source
    /// is work-relative bottom-left/Y-up, with counter-clockwise degrees.
    /// Ordinal is the pair ordinal, shared by its two cards, not a card ID.
    /// </summary>
    internal static bool TryCapture(OldMaidSpatialBody body, OldMaidCardPose source,
        float width, float height, int ordinal, out OldMaidSpatialTable? table)
    {
        table = null;
        if (body == null || !ValidViewport(width, height) || !ValidSource(source, width, height) ||
            ordinal < 0 || ordinal >= OldMaidSpatialDrop.MaxBodies || !ValidBody(body) || body.Age != 0f ||
            (body.Phase != OldMaidSpatialPhase.Waiting && body.Phase != OldMaidSpatialPhase.Airborne) ||
            Math.Abs(body.Position.X) > 5f || body.Position.Y < 2.5f || body.Position.Y > 5f ||
            body.Position.Z < 0f || body.Position.Z > 8f || !Finite(body.Velocity) ||
            Math.Abs(body.Velocity.X) > 20f || body.Velocity.Y < 0f || body.Velocity.Y > 10f ||
            Math.Abs(body.Velocity.Z) > 20f) return false;
        var flight = (body.Velocity.Y + (float)Math.Sqrt(body.Velocity.Y * body.Velocity.Y +
            2f * OldMaidSpatialDrop.Gravity * (body.Position.Y - 0.7f))) / OldMaidSpatialDrop.Gravity;
        var landing = body.Position + body.Velocity * flight;
        landing.Y = 0f;
        if (!Finite(landing) || Math.Abs(landing.X) > 40f || Math.Abs(landing.Z) > 40f) return false;
        table = new OldMaidSpatialTable(body, source, width, height, ordinal, landing);
        return true;
    }

    /// <summary>
    /// A fresh snapshot in viewport-relative top-left/Y-down pixels. Waiting
    /// cards are invisible. Body.Age is the only clock, so zero-delta pauses
    /// and pair delays remain owned by the original model. No input is mutated.
    /// </summary>
    internal bool TryProject(OldMaidSpatialBody body, float width, float height,
        out OldMaidSpatialProjection? projection)
    {
        projection = null;
        // Reference identity also rejects a stale capture after Clear/redeal.
        if (!ReferenceEquals(body, _body) || !ValidViewport(width, height) || !ValidBody(body) ||
            body.Phase == OldMaidSpatialPhase.Waiting || !TryCalibration(width, height, out var scale, out var shift)) return false;

        var center = Transform(body.Position, scale, shift);
        var orientation = body.Orientation;
        var cardWidth = body.Width * scale;
        var cardHeight = body.Height * scale;
        if (body.Phase == OldMaidSpatialPhase.Airborne && body.Age < Duration)
        {
            // A rigid source plane, not a four-corner screen-space residual:
            // even during a flip, the quad remains a projective planar card.
            if (!TrySourcePlane(width, height, scale, shift, out var sourceCenter,
                out var sourceOrientation, out var sourceWidth, out var sourceHeight)) return false;
            var t = body.Age / Duration;
            var eased = t * t * t * (t * (t * 6f - 15f) + 10f);
            center = Vector3.Lerp(sourceCenter, center, eased);
            orientation = Quaternion.Normalize(Quaternion.Slerp(sourceOrientation, orientation, eased));
            cardWidth = sourceWidth + (cardWidth - sourceWidth) * eased;
            cardHeight = sourceHeight + (cardHeight - sourceHeight) * eased;
        }

        var view = new OldMaidSpatialBody(body.CardId, body.Owner, cardWidth, cardHeight)
        {
            Position = center, Orientation = orientation, Phase = body.Phase,
            ContactPoint = body.ContactPoint.HasValue ? Transform(body.ContactPoint.Value, scale, shift) : null
        };
        // Earlier airborne travel need not constrain the final floor target.
        // Fit a whole rigid plane, never individual corners, and use that same
        // translated plane for the shadow. Contacted support is never fitted.
        if (body.Phase == OldMaidSpatialPhase.Airborne && body.Age > 0f && !TryFitAirborne(view, width, height)) return false;
        center = view.Position;
        var world = view.WorldCorners();
        var corners = new Vector2[4];
        var shadow = new Vector2[4];
        if (!TryProjectPoint(center, width, height, out var screen, out var depth)) return false;
        for (var index = 0; index < 4; index++)
        {
            if (!TryProjectPoint(world[index], width, height, out corners[index], out _)) return false;
            // Same directional light and Y=0 floor for the displayed geometry,
            // including the release bridge. Contact and shadow cannot use
            // different coordinate systems or a separately clamped support.
            var floor = new Vector3(world[index].X + world[index].Y * 0.18f, 0f,
                world[index].Z + world[index].Y * 0.1f);
            if (!TryProjectPoint(floor, width, height, out shadow[index], out _)) return false;
        }
        var front = Vector3.Dot(view.Normal, CameraPosition - center) >= 0f;
        var texture = front ? (Vector2[])corners.Clone() : new[] { corners[1], corners[0], corners[3], corners[2] };
        var altitude = Math.Max(0f, center.Y);
        projection = new OldMaidSpatialProjection(view, screen, depth, front, corners, texture, shadow,
            0.11f + 0.17f / (1f + altitude), Math.Min(10f, 0.6f + altitude * 1.7f));
        return true;
    }

    internal static bool TryProjectPoint(Vector3 world, float width, float height, out Vector2 screen, out float depth)
    {
        screen = Vector2.Zero;
        depth = 0f;
        if (!Finite(world) || !ValidViewport(width, height)) return false;
        var relative = world - CameraPosition;
        depth = Vector3.Dot(relative, CameraForward);
        if (!Finite(depth) || depth <= OldMaidSpatialDrop.NearPlane) return false;
        var focal = Focal(width, height);
        screen = new Vector2(width * 0.5f + focal * Vector3.Dot(relative, CameraRight) / depth,
            height * FloorScreenAnchor - focal * Vector3.Dot(relative, CameraUp) / depth);
        return Finite(screen.X) && Finite(screen.Y);
    }

    private bool TryCalibration(float width, float height, out float scale, out float shift)
    {
        var fit = Math.Min(width / _captureWidth, height / _captureHeight);
        // One captured sample covers the full floor, including its centre.
        // It is independent of the source's screen position and never rerolls
        // while projecting, pausing or resizing. Original depth is retained.
        var target = ScatterFraction;
        // The nominal source-hand scale is further compensated for off-axis
        // obliquity: a rotating edge has more screen magnification far from
        // the optical axis, especially on ultrawide displays. This factor is
        // captured-geometry/viewport-only, never resized per animation frame.
        var pixels = Math.Min(_source.Width * fit * SourceScale, Math.Min(width * 0.065f, height * 0.13f));
        var targetRayX = (target - 0.5f) * width / Focal(width, height);
        pixels /= (float)Math.Sqrt(1f + targetRayX * targetRayX);
        var anchorDepth = Vector3.Dot(_floorAnchor - CameraPosition, CameraForward);
        scale = pixels * anchorDepth /
            (Focal(width, height) * _body.Width - pixels * CameraForward.Y * _initialPosition.Y);
        shift = 0f;
        if (!Finite(scale) || scale <= 0f) return false;

        var landing = Transform(_landing, scale, 0f);
        var landingDepth = Vector3.Dot(landing - CameraPosition, CameraForward);
        shift = (width * target - width * 0.5f) * landingDepth / Focal(width, height) - landing.X;
        return Finite(shift);
    }

    private static float CaptureScatter(OldMaidSpatialBody body, int ordinal)
    {
        // Explicit unsigned mixing is reproducible, unlike process-seeded
        // GetHashCode/Random.Shared. The core seed already determines these
        // initial kinematics; no clock, global state or character position is
        // consumed. This is decorative variation, not gameplay randomness.
        var hash = Mix(2166136261u, (uint)body.CardId);
        hash = Mix(hash, (uint)ordinal);
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Position.X)));
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Position.Y)));
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Position.Z)));
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Velocity.X)));
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Velocity.Y)));
        hash = Mix(hash, unchecked((uint)BitConverter.SingleToInt32Bits(body.Velocity.Z)));
        hash = unchecked((hash ^ (hash >> 16)) * 0x7FEB352Du);
        hash = unchecked((hash ^ (hash >> 15)) * 0x846CA68Bu);
        hash ^= hash >> 16;
        return 0.12f + 0.76f * ((hash >> 8) / 16777216f);
    }

    private static uint Mix(uint hash, uint value) => unchecked((hash ^ value) * 16777619u);

    private static bool TryFitAirborne(OldMaidSpatialBody view, float width, float height)
    {
        var lowX = float.NegativeInfinity;
        var highX = float.PositiveInfinity;
        var lowY = float.NegativeInfinity;
        var highY = float.PositiveInfinity;
        var focal = Focal(width, height);
        foreach (var world in view.WorldCorners())
        {
            var relative = world - CameraPosition;
            var depth = Vector3.Dot(relative, CameraForward);
            if (depth <= OldMaidSpatialDrop.NearPlane) return false;
            var up = Vector3.Dot(relative, CameraUp);
            lowX = Math.Max(lowX, -width * 0.5f * depth / focal - relative.X);
            highX = Math.Min(highX, width * 0.5f * depth / focal - relative.X);
            lowY = Math.Max(lowY, -height * (1f - FloorScreenAnchor) * depth / focal - up);
            highY = Math.Min(highY, height * FloorScreenAnchor * depth / focal - up);
            // Raising the displayed plane, if necessary during interpolation,
            // also keeps the card and its shadow above their shared floor.
            lowY = Math.Max(lowY, -world.Y / CameraUp.Y);
        }
        if (lowX > highX || lowY > highY) return false;
        view.Position += CameraRight * Math.Clamp(0f, lowX, highX) + CameraUp * Math.Clamp(0f, lowY, highY);
        return true;
    }

    private bool TrySourcePlane(float width, float height, float scale, float shift, out Vector3 center,
        out Quaternion orientation, out float cardWidth, out float cardHeight)
    {
        var fit = Math.Min(width / _captureWidth, height / _captureHeight);
        var x = _source.X / _captureWidth * width;
        var y = height - _source.Y / _captureHeight * height;
        var focal = Focal(width, height);
        var depth = Vector3.Dot(Transform(_initialPosition, scale, shift) - CameraPosition, CameraForward);
        var angle = (float)(_source.Angle * Math.PI / 180d);
        var plane = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.Atan2(CameraUp.Z, CameraUp.Y));
        orientation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(CameraForward, angle) * plane);
        // Keep the source plane above the shared floor without changing its
        // visible quad: moving along the view ray scales its world dimensions.
        var cosine = (float)Math.Cos(angle);
        var sine = (float)Math.Sin(angle);
        for (var index = 0; index < 4; index++)
        {
            var localX = (index == 0 || index == 3 ? -0.5f : 0.5f) * _source.Width * fit;
            var localY = (index < 2 ? 0.5f : -0.5f) * _source.Height * fit;
            var screenY = y - localX * sine - localY * cosine;
            var rayY = CameraForward.Y + (height * FloorScreenAnchor - screenY) / focal * CameraUp.Y;
            if (rayY < 0f) depth = Math.Min(depth, (CameraPosition.Y - OldMaidSpatialDrop.FloorClearance) / -rayY);
        }
        center = CameraPosition + depth * (CameraForward + CameraRight * ((x - width * 0.5f) / focal) +
            CameraUp * ((height * FloorScreenAnchor - y) / focal));
        cardWidth = _source.Width * fit * depth / focal;
        cardHeight = _source.Height * fit * depth / focal;
        if (!_initialFront) orientation = Quaternion.Normalize(orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.PI));
        return Finite(center) && Finite(depth) && depth > OldMaidSpatialDrop.NearPlane;
    }

    private Vector3 Transform(Vector3 point, float scale, float shift) =>
        _floorAnchor + (point - _floorAnchor) * scale + Vector3.UnitX * shift;
    private static float Focal(float width, float height) => Math.Min(height * 1.2f, width * 0.9f);
    private static bool ValidSource(OldMaidCardPose source, float width, float height) =>
        Finite(source.X) && Finite(source.Y) && source.X >= 0f && source.X <= width && source.Y >= 0f && source.Y <= height &&
        Finite(source.Angle) && Math.Abs(source.Angle) <= 3600f &&
        Finite(source.Width) && source.Width >= 1f && source.Width <= Math.Min(4096f, width) &&
        Finite(source.Height) && source.Height >= 1f && source.Height <= Math.Min(4096f, height);
    private static bool ValidBody(OldMaidSpatialBody body) =>
        body.CardId >= 0 && body.CardId < OldMaidSpatialDrop.MaxBodies && body.Owner >= 0 && body.Owner <= 1 &&
        Finite(body.Width) && body.Width >= 0.5f && body.Width <= 2f && Finite(body.Height) && body.Height >= 0.8f && body.Height <= 2.8f &&
        Finite(body.Position) && body.Position.LengthSquared() <= 10000f && Finite(body.Age) && body.Age >= 0f &&
        body.Phase >= OldMaidSpatialPhase.Waiting && body.Phase <= OldMaidSpatialPhase.Settled &&
        Finite(body.Orientation.X) && Finite(body.Orientation.Y) && Finite(body.Orientation.Z) && Finite(body.Orientation.W) &&
        Math.Abs(body.Orientation.LengthSquared() - 1f) <= 0.001f && (!body.ContactPoint.HasValue || Finite(body.ContactPoint.Value));
    private static bool ValidViewport(float width, float height) =>
        Finite(width) && Finite(height) && width >= 1f && height >= 1f && width <= 65536f && height <= 65536f;
    private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
