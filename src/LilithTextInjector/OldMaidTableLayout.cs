using System;

namespace LilithTextInjector;

// The player's seat belongs to the desktop work area, never the character.
internal static class OldMaidTableLayout
{
    internal static OldMaidCardPose PlayerArea(float left, float bottom, float width, float height)
    {
        if (!OldMaidCardGeometry.IsFinite(left) || !OldMaidCardGeometry.IsFinite(bottom)
            || Math.Abs(left) > 65536 || Math.Abs(bottom) > 65536
            || !OldMaidCardGeometry.PositiveFinite(width) || width > 65536
            || !OldMaidCardGeometry.PositiveFinite(height) || height > 65536)
            throw new ArgumentOutOfRangeException(nameof(width));
        var ui = Math.Clamp(height / 900f, 0.6f, 1.4f);
        var handWidth = Math.Min(680f * ui, width * 0.72f);
        var handHeight = Math.Min(230f * ui, height * 0.36f);
        var floorGap = Math.Min(48f * ui, height * 0.12f);
        return new OldMaidCardPose(left + width * 0.5f,
            bottom + floorGap + handHeight * 0.5f, 0, handWidth, handHeight);
    }

    // Screen-space character/sofa bounds are read-only. The fan sits over the
    // left part of the backrest, rather than outside the character's left edge.
    internal static OldMaidCardPose OpponentArea(float left, float bottom, float width, float height,
        OldMaidCardPose characterBounds)
    {
        _ = PlayerArea(left, bottom, width, height);
        if (!OldMaidCardGeometry.IsFinite(characterBounds.X) || Math.Abs(characterBounds.X) > 131072
            || !OldMaidCardGeometry.IsFinite(characterBounds.Y) || Math.Abs(characterBounds.Y) > 131072
            || !OldMaidCardGeometry.PositiveFinite(characterBounds.Width) || characterBounds.Width > 65536
            || !OldMaidCardGeometry.PositiveFinite(characterBounds.Height) || characterBounds.Height > 65536)
            throw new ArgumentOutOfRangeException(nameof(characterBounds));
        var ui = Math.Clamp(height / 900f, 0.6f, 1.4f);
        var handWidth = Math.Min(430f * ui, width * 0.52f);
        var handHeight = Math.Min(150f * ui, height * 0.3f);
        var x = characterBounds.X - characterBounds.Width * 0.16f;
        var y = characterBounds.Y + characterBounds.Height * 0.46f;
        return new OldMaidCardPose(
            Math.Clamp(x, left + handWidth * 0.5f, left + width - handWidth * 0.5f),
            Math.Clamp(y, bottom + height * 0.46f + handHeight * 0.5f, bottom + height - handHeight * 0.5f),
            0, handWidth, handHeight);
    }

    // Project a frozen flight into the CURRENT viewport without advancing its
    // clock or altering the rules. Rotation is included in edge containment.
    internal static OldMaidCardPose FitFlight(OldMaidCardPose pose, float left, float bottom, float width, float height)
    {
        _ = PlayerArea(left, bottom, width, height); // Same bounded work-area contract.
        if (!OldMaidCardGeometry.IsFinite(pose.X) || !OldMaidCardGeometry.IsFinite(pose.Y)
            || !OldMaidCardGeometry.IsFinite(pose.Angle) || Math.Abs(pose.Angle) > 360000
            || !OldMaidCardGeometry.PositiveFinite(pose.Width) || pose.Width > 4096
            || !OldMaidCardGeometry.PositiveFinite(pose.Height) || pose.Height > 4096)
            throw new ArgumentOutOfRangeException(nameof(pose));
        var diagonal = (float)Math.Sqrt(pose.Width * pose.Width + pose.Height * pose.Height);
        var scale = Math.Min(1f, Math.Min(width, height) * 0.98f / diagonal);
        var cardWidth = pose.Width * scale;
        var cardHeight = pose.Height * scale;
        var halfX = OldMaidCardGeometry.HalfExtentX(cardWidth, cardHeight, pose.Angle);
        var halfY = OldMaidCardGeometry.HalfExtentY(cardWidth, cardHeight, pose.Angle);
        return new OldMaidCardPose(Math.Clamp(pose.X, left + halfX, left + width - halfX),
            Math.Clamp(pose.Y, bottom + halfY, bottom + height - halfY), pose.Angle, cardWidth, cardHeight);
    }
}
