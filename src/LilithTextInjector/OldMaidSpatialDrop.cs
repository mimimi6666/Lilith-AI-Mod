using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

namespace LilithTextInjector;

/// <summary>
/// Isolated decorative 3-D card experiment, not a rigid-body/contact solver and
/// not connected to Unity, native character physics, input, or game rules.
/// X is horizontal, Y is height above a shared floor, Z is depth. Free flight is
/// ballistic; landing is a bounded, support-point-anchored kinematic hinge.
/// Cards do not collide with one another. Whole-card depth order is approximate
/// for crossing/intersecting cards; projection itself is genuinely perspective.
/// </summary>
internal sealed class OldMaidSpatialDrop
{
    internal const int MaxBodies = 27;
    internal const float FixedStep = 1f / 120f;
    internal const float MaximumDelta = 0.1f;
    internal const float MaximumLaunchDelay = 4f;
    internal const float FloorClearance = 0.012f;
    internal const float Gravity = 11f;
    internal const float NearPlane = 0.5f;
    internal static readonly Vector3 CameraPosition = new Vector3(0f, 8f, -14f);
    internal static readonly Vector3 CameraForward = Vector3.Normalize(new Vector3(0f, -8f, 19f));
    internal static readonly Vector3 CameraRight = Vector3.UnitX;
    internal static readonly Vector3 CameraUp = Vector3.Cross(CameraForward, CameraRight);
    private readonly List<OldMaidSpatialBody> _bodies = new List<OldMaidSpatialBody>(MaxBodies);
    private readonly ReadOnlyCollection<OldMaidSpatialBody> _view;
    private readonly uint _initialSeed;
    private uint _random;
    private double _accumulator;

    internal OldMaidSpatialDrop(int seed = 7)
    {
        _initialSeed = unchecked((uint)seed) == 0 ? 1u : unchecked((uint)seed);
        _random = _initialSeed;
        _view = _bodies.AsReadOnly();
    }

    internal IReadOnlyList<OldMaidSpatialBody> Bodies => _view;

    internal bool Emit(int cardId, int owner, int pairIndex, float launchDelay = 0f)
    {
        var origin = owner == 0 ? new Vector3(-2.2f, 3.3f, 0.7f) : new Vector3(3.1f, 3.7f, 4.2f);
        origin.X += pairIndex * 0.2f;
        return Emit(cardId, origin, owner, pairIndex, launchDelay: launchDelay);
    }

    /// <summary>
    /// Enqueues a unique decorative card. Inputs outside the finite experiment
    /// envelope fail without consuming RNG. Fixed world dimensions are retained
    /// when the presentation viewport changes. Position is the card's center.
    /// </summary>
    internal bool Emit(int cardId, Vector3 position, int owner, int pairIndex,
        float width = 1.6f, float height = 2.4f, float launchDelay = 0f, int throwOrdinal = -1)
    {
        if (cardId < 0 || cardId >= MaxBodies || owner < 0 || owner > 1 || pairIndex < 0 || pairIndex > 1 ||
            _bodies.Count >= MaxBodies || !Finite(position) || Math.Abs(position.X) > 5f ||
            position.Y < 2.5f || position.Y > 5f || position.Z < 0f || position.Z > 8f ||
            !Finite(width) || width < 0.5f || width > 2f || !Finite(height) || height < 0.8f || height > 2.8f ||
            !Finite(launchDelay) || launchDelay < 0f || launchDelay > MaximumLaunchDelay ||
            throwOrdinal < -1 || throwOrdinal >= MaxBodies) return false;
        foreach (var existing in _bodies) if (existing.CardId == cardId) return false;

        var ordinal = throwOrdinal < 0 ? _bodies.Count : throwOrdinal * 2 + pairIndex;
        // Permuted columns and depth rows spread the landing field in two axes.
        // This is deterministic decoration, independent of any game/deck RNG.
        var targetX = -5.5f + (ordinal * 5 % 11) * 1.1f + Range(-0.18f, 0.18f);
        var targetZ = 1.3f + (ordinal * 3 % 7) * 1.1f + Range(-0.15f, 0.15f);
        var up = Range(4.2f, 5.8f);
        var flight = (up + (float)Math.Sqrt(up * up + 2f * Gravity * (position.Y - 0.7f))) / Gravity;
        var orientation = Quaternion.CreateFromYawPitchRoll(Range(-0.6f, 0.6f),
            Range(-0.45f, 0.45f), Range(-0.4f, 0.4f));
        var body = new OldMaidSpatialBody(cardId, owner, width, height)
        {
            Position = position,
            Orientation = orientation,
            Velocity = new Vector3((targetX - position.X) / flight, up, (targetZ - position.Z) / flight),
            AngularVelocity = new Vector3((pairIndex == 0 ? 1f : -1f) * Range(2.2f, 4f),
                Range(-1.5f, 1.5f), Range(-1.7f, 1.7f)),
            Delay = launchDelay,
            Phase = launchDelay > 0f ? OldMaidSpatialPhase.Waiting : OldMaidSpatialPhase.Airborne,
            TipDuration = Range(0.48f, 0.67f),
            BounceDuration = Range(0.36f, 0.46f)
        };
        _bodies.Add(body);
        return true;
    }

    /// <summary>Zero/invalid time pauses everything, including launch delays.</summary>
    internal void Step(float delta)
    {
        if (!Finite(delta) || delta <= 0f) return;
        _accumulator += Math.Min(delta, MaximumDelta);
        var ticks = 0;
        while (_accumulator + 1e-9 >= FixedStep && ticks++ < 13)
        {
            foreach (var body in _bodies) Tick(body);
            _accumulator -= FixedStep;
            if (_accumulator < 0d) _accumulator = 0d;
        }
    }

    internal void Clear()
    {
        _bodies.Clear();
        _random = _initialSeed;
        _accumulator = 0d;
    }

    /// <summary>
    /// Top-left pixel coordinates. Physical corners retain front UV order
    /// TL/TR/BR/BL; TextureCorners reverse the back side's winding so the back
    /// image is not mirrored. Both surfaces are rendered, including front-down
    /// landings. No world state changes here. Invalid viewports return no cards.
    /// </summary>
    internal IReadOnlyList<OldMaidSpatialProjection> Project(float viewportWidth, float viewportHeight)
    {
        var result = new List<OldMaidSpatialProjection>(_bodies.Count);
        if (!ValidViewport(viewportWidth, viewportHeight)) return result;
        foreach (var body in _bodies)
        {
            if (body.Phase == OldMaidSpatialPhase.Waiting) continue;
            var corners = new Vector2[4];
            var shadow = new Vector2[4];
            var world = body.WorldCorners();
            var valid = TryProjectPoint(body.Position, viewportWidth, viewportHeight, out var center, out var depth);
            for (var index = 0; index < 4; index++)
            {
                valid &= TryProjectPoint(world[index], viewportWidth, viewportHeight, out corners[index], out _);
                // Directional light projection onto the same Y=0 world floor.
                var floor = new Vector3(world[index].X + world[index].Y * 0.18f, 0f,
                    world[index].Z + world[index].Y * 0.1f);
                valid &= TryProjectPoint(floor, viewportWidth, viewportHeight, out shadow[index], out _);
            }
            if (!valid) continue;
            var front = Vector3.Dot(body.Normal, CameraPosition - body.Position) >= 0f;
            var texture = front ? (Vector2[])corners.Clone() : new[] { corners[1], corners[0], corners[3], corners[2] };
            result.Add(new OldMaidSpatialProjection(body, center, depth, front, corners, texture, shadow,
                0.11f + 0.17f / (1f + body.Position.Y), Math.Min(10f, 0.6f + body.Position.Y * 1.7f)));
        }
        result.Sort((left, right) =>
        {
            var depthOrder = right.Depth.CompareTo(left.Depth);
            return depthOrder != 0 ? depthOrder : left.CardId.CompareTo(right.CardId);
        });
        return result;
    }

    internal static bool TryProjectPoint(Vector3 world, float viewportWidth, float viewportHeight,
        out Vector2 screen, out float depth)
    {
        screen = Vector2.Zero;
        depth = 0f;
        if (!Finite(world) || !ValidViewport(viewportWidth, viewportHeight)) return false;
        var relative = world - CameraPosition;
        depth = Vector3.Dot(relative, CameraForward);
        if (!Finite(depth) || depth <= NearPlane) return false;
        var focal = Math.Min(viewportHeight * 1.6f, viewportWidth * 0.98f);
        screen = new Vector2(viewportWidth * 0.5f + focal * Vector3.Dot(relative, CameraRight) / depth,
            viewportHeight * 0.68f - focal * Vector3.Dot(relative, CameraUp) / depth);
        return Finite(screen.X) && Finite(screen.Y);
    }

    private static void Tick(OldMaidSpatialBody body)
    {
        if (body.Phase == OldMaidSpatialPhase.Settled) return;
        if (body.Phase == OldMaidSpatialPhase.Waiting)
        {
            body.Delay = Math.Max(0f, body.Delay - FixedStep);
            if (body.Delay > 1e-6f) return;
            body.Delay = 0f;
            body.Phase = OldMaidSpatialPhase.Airborne;
        }
        body.Age += FixedStep;
        body.PhaseElapsed += FixedStep;
        switch (body.Phase)
        {
            case OldMaidSpatialPhase.Airborne:
                Fly(body);
                break;
            case OldMaidSpatialPhase.Contact:
                if (body.PhaseElapsed >= 0.055f) Transition(body, OldMaidSpatialPhase.Tipping);
                break;
            case OldMaidSpatialPhase.Tipping:
                var tip = Math.Min(1f, body.PhaseElapsed / body.TipDuration);
                Hinge(body, body.TipAngle * tip * tip);
                if (tip >= 1f) Transition(body, OldMaidSpatialPhase.Bouncing);
                break;
            case OldMaidSpatialPhase.Bouncing:
                var bounce = Math.Min(1f, body.PhaseElapsed / body.BounceDuration);
                var rebound = Math.Min(body.TipAngle * 0.22f, 0.12f) *
                    (float)Math.Sin(Math.PI * bounce) * (float)Math.Exp(-2.8f * bounce);
                Hinge(body, body.TipAngle - rebound);
                if (bounce >= 1f)
                {
                    Hinge(body, body.TipAngle);
                    Transition(body, OldMaidSpatialPhase.Settled);
                }
                break;
        }
    }

    private static void Fly(OldMaidSpatialBody body)
    {
        var previousPosition = body.Position;
        var previousOrientation = body.Orientation;
        var gravity = new Vector3(0f, -Gravity, 0f);
        body.Position += body.Velocity * FixedStep + gravity * (0.5f * FixedStep * FixedStep);
        body.Velocity += gravity * FixedStep;
        var angularSpeed = body.AngularVelocity.Length();
        body.Orientation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(body.AngularVelocity / angularSpeed,
            angularSpeed * FixedStep) * body.Orientation);
        if (MinimumHeight(body, body.Position, body.Orientation) > FloorClearance) return;

        // Resolve the first contact inside this bounded timestep, instead of
        // sinking a rotated corner below the floor and snapping its center up.
        var endPosition = body.Position;
        var endOrientation = body.Orientation;
        var low = 0f;
        var high = 1f;
        for (var iteration = 0; iteration < 14; iteration++)
        {
            var fraction = (low + high) * 0.5f;
            var position = Vector3.Lerp(previousPosition, endPosition, fraction);
            var orientation = Quaternion.Slerp(previousOrientation, endOrientation, fraction);
            if (MinimumHeight(body, position, orientation) > FloorClearance) low = fraction;
            else high = fraction;
        }
        body.Position = Vector3.Lerp(previousPosition, endPosition, high);
        body.Orientation = Quaternion.Normalize(Quaternion.Slerp(previousOrientation, endOrientation, high));
        body.Position += Vector3.UnitY * (FloorClearance - MinimumHeight(body, body.Position, body.Orientation));
        var corners = body.WorldCorners();
        var pivot = Vector3.Zero;
        var local = Vector3.Zero;
        var contacts = 0;
        for (var index = 0; index < 4; index++)
        {
            if (corners[index].Y - FloorClearance > 1e-5f) continue;
            pivot += corners[index];
            local += body.LocalCorner(index);
            contacts++;
        }
        // Exact minimum always contributes; near-equal corners give an edge
        // midpoint rather than an arbitrary endpoint of that contacting edge.
        body.ContactPoint = pivot / contacts;
        body.ContactLocalPoint = local / contacts;
        body.ContactOrientation = body.Orientation;
        var normal = body.Normal;
        var destination = normal.Y >= 0f ? Vector3.UnitY : -Vector3.UnitY;
        body.TipAngle = (float)Math.Acos(Math.Clamp(Vector3.Dot(normal, destination), -1f, 1f));
        var axis = Vector3.Cross(normal, destination);
        body.TipAxis = axis.LengthSquared() > 1e-10f ? Vector3.Normalize(axis) : Vector3.UnitX;
        body.Velocity = Vector3.Zero;
        body.AngularVelocity = Vector3.Zero;
        Transition(body, OldMaidSpatialPhase.Contact);
    }

    private static void Hinge(OldMaidSpatialBody body, float angle)
    {
        body.Orientation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(body.TipAxis, angle) * body.ContactOrientation);
        body.Position = body.ContactPoint!.Value - Vector3.Transform(body.ContactLocalPoint, body.Orientation);
    }

    private static void Transition(OldMaidSpatialBody body, OldMaidSpatialPhase phase)
    {
        body.Phase = phase;
        body.PhaseElapsed = 0f;
    }

    private static float MinimumHeight(OldMaidSpatialBody body, Vector3 position, Quaternion orientation)
    {
        var minimum = float.MaxValue;
        for (var index = 0; index < 4; index++)
            minimum = Math.Min(minimum, position.Y + Vector3.Transform(body.LocalCorner(index), orientation).Y);
        return minimum;
    }

    private float Range(float minimum, float maximum)
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return minimum + (_random & 0x00ffffffu) / 16777216f * (maximum - minimum);
    }

    private static bool ValidViewport(float width, float height) =>
        Finite(width) && Finite(height) && width >= 1f && height >= 1f && width <= 65536f && height <= 65536f;
    private static bool Finite(Vector3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

internal enum OldMaidSpatialPhase { Waiting, Airborne, Contact, Tipping, Bouncing, Settled }

internal sealed class OldMaidSpatialBody
{
    internal OldMaidSpatialBody(int cardId, int owner, float width, float height)
    { CardId = cardId; Owner = owner; Width = width; Height = height; }
    internal int CardId { get; }
    internal int Owner { get; }
    internal float Width { get; }
    internal float Height { get; }
    internal float Thickness => OldMaidSpatialDrop.FloorClearance * 2f;
    internal Vector3 Position { get; set; }
    internal Quaternion Orientation { get; set; }
    // TL/TR/BR/BL winds toward local -Z. The negative-Z camera therefore sees
    // identity as an ordinary, unmirrored front, not as an already-flipped back.
    internal Vector3 Normal => Vector3.Transform(-Vector3.UnitZ, Orientation);
    internal Vector3 Velocity { get; set; }
    internal Vector3 AngularVelocity { get; set; }
    internal OldMaidSpatialPhase Phase { get; set; }
    internal bool Settled => Phase == OldMaidSpatialPhase.Settled;
    internal float Delay { get; set; }
    internal float Age { get; set; }
    internal Vector3? ContactPoint { get; set; }
    internal Vector3 ContactLocalPoint { get; set; }
    internal Quaternion ContactOrientation { get; set; }
    internal Vector3 TipAxis { get; set; }
    internal float TipAngle { get; set; }
    internal float TipDuration { get; set; }
    internal float BounceDuration { get; set; }
    internal float PhaseElapsed { get; set; }
    internal Vector3 LocalCorner(int index) => index switch
    {
        0 => new Vector3(-Width * 0.5f, Height * 0.5f, 0f),
        1 => new Vector3(Width * 0.5f, Height * 0.5f, 0f),
        2 => new Vector3(Width * 0.5f, -Height * 0.5f, 0f),
        3 => new Vector3(-Width * 0.5f, -Height * 0.5f, 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    internal Vector3[] WorldCorners()
    {
        var corners = new Vector3[4];
        for (var index = 0; index < 4; index++) corners[index] = Position + Vector3.Transform(LocalCorner(index), Orientation);
        return corners;
    }
}

internal sealed class OldMaidSpatialProjection
{
    internal OldMaidSpatialProjection(OldMaidSpatialBody body, Vector2 center, float depth, bool frontFacing,
        Vector2[] corners, Vector2[] textureCorners, Vector2[] shadowCorners, float shadowOpacity, float shadowBlur)
    {
        CardId = body.CardId; Owner = body.Owner; Phase = body.Phase; Position = body.Position;
        Orientation = body.Orientation; Normal = body.Normal; ContactPoint = body.ContactPoint;
        Center = center; Depth = depth; FrontFacing = frontFacing; Corners = corners;
        TextureCorners = textureCorners; ShadowCorners = shadowCorners;
        ShadowOpacity = shadowOpacity; ShadowBlur = shadowBlur;
    }
    internal int CardId { get; }
    internal int Owner { get; }
    internal OldMaidSpatialPhase Phase { get; }
    internal Vector3 Position { get; }
    internal Quaternion Orientation { get; }
    internal Vector3 Normal { get; }
    internal Vector3? ContactPoint { get; }
    internal Vector2 Center { get; }
    internal float Depth { get; }
    internal bool FrontFacing { get; }
    internal Vector2[] Corners { get; }
    internal Vector2[] TextureCorners { get; }
    internal Vector2[] ShadowCorners { get; }
    internal float ShadowOpacity { get; }
    internal float ShadowBlur { get; }
}
