using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithTextInjector;

/// <summary>
/// Decorative screen-space card motion only. Coordinates are card centers in
/// bottom-left/Y-up pixels; this type has no Unity, input or game-physics authority.
/// </summary>
internal sealed class OldMaidCardDrop
{
    internal const int MaxBodies = 27;
    internal const float SecondCardDelay = 0.13f;
    internal const float MaximumLaunchDelay = 4f;
    internal const float TiltSeconds = 0.65f;
    private const float MaximumDelta = 0.1f;
    private const float MaximumSubstep = 1f / 120f;
    private const float Gravity = 1150f;
    private const float MaximumFlightSeconds = 5f;
    private readonly List<OldMaidDroppedCard> _bodies = new List<OldMaidDroppedCard>(MaxBodies);
    private readonly ReadOnlyCollection<OldMaidDroppedCard> _view;
    private readonly uint _initialSeed;
    private uint _random;

    internal OldMaidCardDrop(int seed = 7)
    {
        _initialSeed = unchecked((uint)seed) == 0 ? 1u : unchecked((uint)seed);
        _random = _initialSeed;
        _view = _bodies.AsReadOnly();
    }

    internal IReadOnlyList<OldMaidDroppedCard> Bodies => _view;

    /// <summary>
    /// Enqueue one already-discarded card without delaying the rules engine.
    /// Owner is 0 (player) or 1 (Lilith); pairIndex is 0 or 1. A pair's second
    /// card remains at its hand origin until its short throw delay expires. An
    /// explicit throwOrdinal keeps a pair on the same outward arc when the
    /// caller stages several opening pairs; -1 derives it from accepted cards.
    /// launchDelay is presentation time only and freezes when Step receives zero.
    /// simultaneousPair omits only the second-card delay after a shared reveal;
    /// pairIndex still identifies each member's throw and forward/backward tilt.
    /// </summary>
    internal bool Emit(int cardId, float x, float y, int owner, int pairIndex,
        float cardWidth = 72f, float cardHeight = 104f, int throwOrdinal = -1, float launchDelay = 0f,
        bool simultaneousPair = false)
    {
        if (cardId < 0 || cardId >= MaxBodies || owner < 0 || owner > 1 || pairIndex < 0 || pairIndex > 1 ||
            !OldMaidCardGeometry.IsFinite(x) || !OldMaidCardGeometry.IsFinite(y) ||
            !OldMaidCardGeometry.PositiveFinite(cardWidth) || !OldMaidCardGeometry.PositiveFinite(cardHeight) ||
            throwOrdinal < -1 || throwOrdinal >= MaxBodies ||
            !OldMaidCardGeometry.IsFinite(launchDelay) || launchDelay < 0f || launchDelay > MaximumLaunchDelay ||
            _bodies.Count >= MaxBodies)
        {
            return false;
        }

        foreach (var existing in _bodies)
        {
            if (existing.CardId == cardId) return false;
        }

        var ordinal = throwOrdinal < 0 ? _bodies.Count / 2 : throwOrdinal;
        // Player pairs fan out to alternating sides. Lilith throws from the
        // sofa toward the open desktop, rather than dropping below her hand.
        // A permuted lane sequence spreads successive pairs without consulting
        // the rules RNG or inventing card-to-card collision authority.
        var direction = owner == 0 && ordinal % 2 == 0 ? 1f : -1f;
        var lane = ordinal * 3 % 7;
        var travelScale = OldMaidCardGeometry.Clamp(cardWidth / 72f, 0.8f, 1.5f);
        var liftScale = (float)Math.Sqrt(travelScale);
        var body = new OldMaidDroppedCard(cardId, owner,
            OldMaidCardGeometry.Clamp(x, 0f, 65536f), OldMaidCardGeometry.Clamp(y, 0f, 65536f),
            Math.Min(cardWidth, 4096f), Math.Min(cardHeight, 4096f),
            launchDelay + (simultaneousPair ? 0f : pairIndex * SecondCardDelay));
        body.VelocityX = direction * (330f + lane * 48f + pairIndex * 28f + NextUnit() * 45f) * travelScale;
        body.VelocityY = (440f + (6 - lane) * 20f + NextUnit() * 55f) * liftScale;
        body.AngularVelocity = direction * (120f + NextUnit() * 190f);
        // Alternate local forward/backward tilt without consuming another random
        // sample or changing the established outward-throw sequence. At rest,
        // 60–66 degrees retains 41–50% of the face height instead of an edge.
        body.TargetPitchDegrees = ((ordinal + pairIndex) % 2 == 0 ? 1f : -1f) * (60f + lane);
        body.StackOffset = (_bodies.Count % 5) * 1.5f;
        _bodies.Add(body);
        return true;
    }

    /// <summary>
    /// A valid zero delta still fits bodies to a changed viewport. Invalid inputs
    /// do nothing. Long stalls advance at most 0.1 seconds, never an unbounded
    /// catch-up loop. Ground is the bottom work-area edge above the taskbar.
    /// </summary>
    internal void Step(float deltaSeconds, float width, float height, float ground)
    {
        if (!OldMaidCardGeometry.IsFinite(deltaSeconds) || deltaSeconds < 0f ||
            !OldMaidCardGeometry.PositiveFinite(width) || !OldMaidCardGeometry.PositiveFinite(height) ||
            !OldMaidCardGeometry.IsFinite(ground))
        {
            return;
        }

        width = Math.Min(width, 65536f);
        height = Math.Min(height, 65536f);
        ground = OldMaidCardGeometry.Clamp(ground, 0f, height * 0.99f);
        foreach (var body in _bodies) Fit(body, width, height, ground);
        var delta = Math.Min(deltaSeconds, MaximumDelta);
        if (delta <= 0f) return;
        var substeps = Math.Min(12, Math.Max(1, (int)Math.Ceiling(delta / MaximumSubstep)));
        var step = delta / substeps;
        for (var i = 0; i < substeps; i++)
        {
            foreach (var body in _bodies)
            {
                Advance(body, step, width, height, ground);
            }
        }
    }

    internal void Clear()
    {
        _bodies.Clear();
        _random = _initialSeed;
    }

    private void Advance(OldMaidDroppedCard body, float delta, float width, float height, float ground)
    {
        if (body.Settled) return;
        if (body.Delay > 0f)
        {
            var consumed = Math.Min(delta, body.Delay);
            body.Delay -= consumed;
            delta -= consumed;
            if (delta <= 0f) return;
        }

        body.Elapsed += delta;
        if (body.MotionStopped)
        {
            AdvanceTilt(body, delta);
            body.Settled = body.TiltProgress >= 1f;
            Fit(body, width, height, ground);
            return;
        }
        body.VelocityY -= Gravity * delta;
        // Tilt starts after the crest, then continues through any small bounce.
        // It is a bounded decorative clock, not a Rigidbody or a rules callback.
        if (body.VelocityY <= 0f || body.TiltProgress > 0f) AdvanceTilt(body, delta);
        body.X += body.VelocityX * delta;
        body.Y += body.VelocityY * delta;
        body.Angle = NormalizeAngle(body.Angle + body.AngularVelocity * delta);
        var halfX = OldMaidCardGeometry.HalfExtentX(body.Width, body.ProjectedHeight, body.Angle);
        var halfY = OldMaidCardGeometry.HalfExtentY(body.Width, body.ProjectedHeight, body.Angle);
        var floor = ground + halfY + AvailableStack(body, height, ground);

        if (body.X < halfX || body.X > width - halfX)
        {
            body.X = OldMaidCardGeometry.Clamp(body.X, halfX, width - halfX);
            body.VelocityX *= -0.3f;
        }
        if (body.Y > height - halfY)
        {
            body.Y = height - halfY;
            body.VelocityY = Math.Min(0f, body.VelocityY);
        }
        if (body.Y <= floor)
        {
            body.Y = floor;
            if (body.VelocityY < -55f && body.Bounces < 3)
            {
                body.VelocityY = -body.VelocityY * 0.24f;
                body.VelocityX *= 0.6f;
                body.AngularVelocity *= 0.48f;
                body.Bounces++;
            }
            else
            {
                body.VelocityY = 0f;
                body.VelocityX *= Math.Max(0f, 1f - 8f * delta);
                body.AngularVelocity *= Math.Max(0f, 1f - 10f * delta);
                if (Math.Abs(body.VelocityX) < 9f && Math.Abs(body.AngularVelocity) < 9f)
                {
                    StopMotion(body, width, height, ground);
                }
            }
        }
        if (body.Elapsed >= MaximumFlightSeconds) StopMotion(body, width, height, ground);
        Fit(body, width, height, ground);
    }

    private static void AdvanceTilt(OldMaidDroppedCard body, float delta)
    {
        body.TiltProgress = Math.Min(1f, body.TiltProgress + delta / TiltSeconds);
        var progress = body.TiltProgress;
        var eased = progress * progress * (3f - 2f * progress);
        body.PitchDegrees = body.TargetPitchDegrees * eased;
    }

    private static void StopMotion(OldMaidDroppedCard body, float width, float height, float ground)
    {
        body.MotionStopped = true;
        body.Settled = body.TiltProgress >= 1f;
        // Sleep in the current orientation. Snapping to a separately sampled
        // rest angle could visibly flip a card on its final animation frame.
        // Even a forced five-second flight timeout finishes the remaining tilt
        // smoothly (at most TiltSeconds), rather than snapping or sleeping upright.
        body.Angle = NormalizeAngle(body.Angle);
        body.VelocityX = body.VelocityY = body.AngularVelocity = 0f;
        Fit(body, width, height, ground);
    }

    private static void Fit(OldMaidDroppedCard body, float width, float height, float ground)
    {
        // Fitting the diagonal guarantees room at every rotation, including a
        // resize while airborne. Restore requested dimensions if space returns.
        var diagonal = (float)Math.Sqrt(body.RequestedWidth * body.RequestedWidth +
            body.RequestedHeight * body.RequestedHeight);
        var scale = Math.Min(1f, Math.Min(width, height - ground) / diagonal * 0.98f);
        body.Width = body.RequestedWidth * scale;
        body.Height = body.RequestedHeight * scale;
        var halfX = OldMaidCardGeometry.HalfExtentX(body.Width, body.ProjectedHeight, body.Angle);
        var halfY = OldMaidCardGeometry.HalfExtentY(body.Width, body.ProjectedHeight, body.Angle);
        body.X = OldMaidCardGeometry.Clamp(body.X, halfX, width - halfX);
        var floor = ground + halfY + AvailableStack(body, height, ground);
        body.Y = body.MotionStopped ? floor : OldMaidCardGeometry.Clamp(body.Y, floor, height - halfY);
    }

    private static float AvailableStack(OldMaidDroppedCard body, float height, float ground)
    {
        var halfY = OldMaidCardGeometry.HalfExtentY(body.Width, body.ProjectedHeight, body.Angle);
        return Math.Min(body.StackOffset, Math.Max(0f, height - ground - halfY * 2f));
    }

    private float NextUnit()
    {
        // Explicit PRNG keeps decorative throws reproducible across runtimes.
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random & 0x00FFFFFFu) / 16777216f;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        return angle < -180f ? angle + 360f : angle > 180f ? angle - 360f : angle;
    }
}

internal sealed class OldMaidDroppedCard
{
    internal OldMaidDroppedCard(int cardId, int owner, float x, float y, float width, float height, float delay)
    {
        CardId = cardId;
        Owner = owner;
        X = x;
        Y = y;
        RequestedWidth = Width = width;
        RequestedHeight = Height = height;
        Delay = delay;
    }

    internal int CardId { get; }
    internal int Owner { get; }
    internal float X { get; set; }
    internal float Y { get; set; }
    internal float Angle { get; set; }
    // Render a local X pitch followed by screen-space Z spin. Width/Height stay
    // unprojected; use ProjectedHeight only for the resulting screen bounds.
    internal float PitchDegrees { get; set; }
    internal float TargetPitchDegrees { get; set; }
    internal float TiltProgress { get; set; }
    internal float ProjectedHeight => Height * (float)Math.Cos(PitchDegrees * Math.PI / 180d);
    internal float Width { get; set; }
    internal float Height { get; set; }
    internal float Delay { get; set; }
    internal bool Settled { get; set; }
    internal bool MotionStopped { get; set; }
    internal float RequestedWidth { get; }
    internal float RequestedHeight { get; }
    internal float VelocityX { get; set; }
    internal float VelocityY { get; set; }
    internal float AngularVelocity { get; set; }
    internal float StackOffset { get; set; }
    internal float Elapsed { get; set; }
    internal int Bounces { get; set; }
}

internal readonly struct OldMaidCardPose
{
    internal OldMaidCardPose(float x, float y, float angle, float width, float height)
    {
        X = x;
        Y = y;
        Angle = angle;
        Width = width;
        Height = height;
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Angle { get; }
    internal float Width { get; }
    internal float Height { get; }
}

/// <summary>Pure bounded fan layout in an already-authorized screen work area.</summary>
internal static class OldMaidFloatingLayout
{
    internal static IReadOnlyList<OldMaidCardPose> GetFan(int count, float left, float bottom,
        float width, float height, float requestedWidth, float requestedHeight, int hoveredIndex = -1)
    {
        if (count < 1 || count > OldMaidCardDrop.MaxBodies ||
            !OldMaidCardGeometry.IsFinite(left) || !OldMaidCardGeometry.IsFinite(bottom) ||
            Math.Abs(left) > 65536f || Math.Abs(bottom) > 65536f ||
            !OldMaidCardGeometry.PositiveFinite(width) || !OldMaidCardGeometry.PositiveFinite(height) ||
            !OldMaidCardGeometry.PositiveFinite(requestedWidth) || !OldMaidCardGeometry.PositiveFinite(requestedHeight))
        {
            return Array.Empty<OldMaidCardPose>();
        }

        width = Math.Min(width, 65536f);
        height = Math.Min(height, 65536f);
        requestedWidth = Math.Min(requestedWidth, 4096f);
        requestedHeight = Math.Min(requestedHeight, 4096f);
        var diagonal = (float)Math.Sqrt(requestedWidth * requestedWidth + requestedHeight * requestedHeight);
        var scale = Math.Min(1f, Math.Min(width, height) / diagonal * 0.98f);
        var cardWidth = requestedWidth * scale;
        var cardHeight = requestedHeight * scale;
        var radius = diagonal * scale * 0.5f;
        var spread = count == 1 ? 0f : Math.Min(cardWidth * 0.42f, Math.Max(0f, width - radius * 2f) / (count - 1));
        var span = spread * (count - 1);
        var middleX = left + width * 0.5f;
        var lift = Math.Min(cardHeight * 0.16f, Math.Max(0f, height - radius * 2f) * 0.5f);
        var poses = new OldMaidCardPose[count];
        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0f : (float)i / (count - 1) * 2f - 1f;
            var angle = -t * 14f;
            var halfX = OldMaidCardGeometry.HalfExtentX(cardWidth, cardHeight, angle);
            var halfY = OldMaidCardGeometry.HalfExtentY(cardWidth, cardHeight, angle);
            var x = middleX - span * 0.5f + i * spread;
            var y = bottom + radius + (1f - t * t) * lift + (i == hoveredIndex ? lift : 0f);
            poses[i] = new OldMaidCardPose(
                OldMaidCardGeometry.Clamp(x, left + halfX, left + width - halfX),
                OldMaidCardGeometry.Clamp(y, bottom + halfY, bottom + height - halfY),
                angle, cardWidth, cardHeight);
        }
        return Array.AsReadOnly(poses);
    }
}

internal static class OldMaidCardGeometry
{
    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool PositiveFinite(float value) => IsFinite(value) && value > 0f;
    internal static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(value, maximum));
    internal static float HalfExtentX(float width, float height, float angle)
    {
        var radians = angle * Math.PI / 180d;
        return (float)(Math.Abs(Math.Cos(radians)) * width + Math.Abs(Math.Sin(radians)) * height) * 0.5f;
    }
    internal static float HalfExtentY(float width, float height, float angle)
    {
        var radians = angle * Math.PI / 180d;
        return (float)(Math.Abs(Math.Sin(radians)) * width + Math.Abs(Math.Cos(radians)) * height) * 0.5f;
    }
}
