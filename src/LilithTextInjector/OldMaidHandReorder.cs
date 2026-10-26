using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal readonly struct OldMaidHandMove
{
    internal OldMaidHandMove(int cardId, int sourceSlot, int targetSlot, int expectedCount, int expectedRevision)
    {
        CardId = cardId;
        SourceSlot = sourceSlot;
        TargetSlot = targetSlot;
        ExpectedCount = expectedCount;
        ExpectedRevision = expectedRevision;
    }

    internal int CardId { get; }
    internal int SourceSlot { get; }
    internal int TargetSlot { get; }
    internal int ExpectedCount { get; }
    internal int ExpectedRevision { get; }
}

// A pointer capture, not a second authority for cards. All preview coordinates
// come from the frozen, unhovered fan. No frame-dependent lifted edge can change
// its own hit region. A release produces a request; only the rules can commit it.
internal sealed class OldMaidHandReorder
{
    internal const float DragThreshold = 8f;
    private const float MaxCoordinate = 131072f;
    private int[] _ids = Array.Empty<int>();
    private OldMaidCardPose[] _poses = Array.Empty<OldMaidCardPose>();
    private float _startX;
    private float _startY;

    internal bool IsActive { get; private set; }
    internal bool IsDragging { get; private set; }
    internal int CardId { get; private set; } = -1;
    internal int SourceSlot { get; private set; } = -1;
    internal int TargetSlot { get; private set; } = -1;
    internal int ExpectedRevision { get; private set; } = -1;
    internal int ExpectedCount => _ids.Length;
    internal float DeltaX { get; private set; }
    internal float DeltaY { get; private set; }

    internal bool TryBegin(IReadOnlyList<OldMaidCard> hand, int revision, int cardId,
        IReadOnlyList<OldMaidCardPose> basePoses, float pointerX, float pointerY)
    {
        // A second press cannot replace the existing capture silently.
        if (IsActive || hand == null || basePoses == null || hand.Count < 2 ||
            hand.Count > OldMaidGame.DeckSize || hand.Count != basePoses.Count ||
            !Coordinate(pointerX) || !Coordinate(pointerY)) return false;
        var source = -1;
        for (var i = 0; i < hand.Count; i++)
        {
            var pose = basePoses[i];
            if (!ValidPose(pose) || (i > 0 && pose.X <= basePoses[i - 1].X)) return false;
            if (hand[i].Id == cardId) source = i;
            for (var prior = 0; prior < i; prior++)
                if (hand[prior].Id == hand[i].Id) return false;
        }
        if (source < 0) return false;

        _ids = new int[hand.Count];
        _poses = new OldMaidCardPose[hand.Count];
        for (var i = 0; i < hand.Count; i++) { _ids[i] = hand[i].Id; _poses[i] = basePoses[i]; }
        CardId = cardId;
        SourceSlot = source;
        TargetSlot = source;
        ExpectedRevision = revision;
        _startX = pointerX;
        _startY = pointerY;
        DeltaX = DeltaY = 0f;
        IsDragging = false;
        IsActive = true;
        return true;
    }

    internal bool UpdatePointer(IReadOnlyList<OldMaidCard> hand, int revision, float pointerX, float pointerY)
    {
        if (!MatchesHand(hand, revision) || !Coordinate(pointerX) || !Coordinate(pointerY))
        {
            Cancel();
            return false;
        }
        DeltaX = pointerX - _startX;
        DeltaY = pointerY - _startY;
        if (!IsDragging && (double)DeltaX * DeltaX + (double)DeltaY * DeltaY >= DragThreshold * DragThreshold)
            IsDragging = true;
        if (!IsDragging) return true;

        // Use the captured card centre, preserving where within it was grabbed.
        // Small midpoint hysteresis prevents slot flicker without moving anchors.
        var centreX = _poses[SourceSlot].X + DeltaX;
        while (TargetSlot < _poses.Length - 1)
        {
            var left = _poses[TargetSlot].X;
            var right = _poses[TargetSlot + 1].X;
            if (centreX <= left + (right - left) * 0.5f + Hysteresis(right - left)) break;
            TargetSlot++;
        }
        while (TargetSlot > 0)
        {
            var left = _poses[TargetSlot - 1].X;
            var right = _poses[TargetSlot].X;
            if (centreX >= left + (right - left) * 0.5f - Hysteresis(right - left)) break;
            TargetSlot--;
        }
        return true;
    }

    internal bool TryRelease(IReadOnlyList<OldMaidCard> hand, int revision,
        float pointerX, float pointerY, out OldMaidHandMove move)
    {
        move = default;
        if (!UpdatePointer(hand, revision, pointerX, pointerY)) return false;
        var changed = IsDragging && TargetSlot != SourceSlot;
        if (changed) move = new OldMaidHandMove(CardId, SourceSlot, TargetSlot, ExpectedCount, ExpectedRevision);
        Cancel();
        return changed;
    }

    // Caller cancels on viewport/fan movement, focus loss, modal UI or busy turn.
    // Exact equality is intentional: the frozen capture must not survive a new
    // layout while its slot anchors continue describing the old layout.
    internal bool MatchesLayout(IReadOnlyList<OldMaidCardPose> basePoses)
    {
        if (!IsActive || basePoses == null || basePoses.Count != _poses.Length) return false;
        for (var i = 0; i < _poses.Length; i++)
        {
            var left = _poses[i];
            var right = basePoses[i];
            if (left.X != right.X || left.Y != right.Y || left.Angle != right.Angle ||
                left.Width != right.Width || left.Height != right.Height) return false;
        }
        return true;
    }

    internal void Cancel()
    {
        IsActive = IsDragging = false;
        CardId = SourceSlot = TargetSlot = ExpectedRevision = -1;
        DeltaX = DeltaY = 0f;
        _ids = Array.Empty<int>();
        _poses = Array.Empty<OldMaidCardPose>();
    }

    internal static int SlotAfterMove(int originalIndex, int sourceSlot, int targetSlot)
    {
        if (originalIndex == sourceSlot) return targetSlot;
        if (sourceSlot < targetSlot && originalIndex > sourceSlot && originalIndex <= targetSlot)
            return originalIndex - 1;
        if (targetSlot < sourceSlot && originalIndex >= targetSlot && originalIndex < sourceSlot)
            return originalIndex + 1;
        return originalIndex;
    }

    // A held pointer remains inside one card-sized owned hit surface even if
    // the visual lifts or is fitted against a screen edge. Not a desktop mask.
    internal static OldMaidCardPose CapturePose(float pointerX, float pointerY, float width, float height) =>
        new(pointerX, pointerY, 0, width, height);

    private bool MatchesHand(IReadOnlyList<OldMaidCard> hand, int revision)
    {
        if (!IsActive || hand == null || revision != ExpectedRevision || hand.Count != _ids.Length) return false;
        for (var i = 0; i < _ids.Length; i++) if (hand[i].Id != _ids[i]) return false;
        return true;
    }

    private static float Hysteresis(float spacing) => Math.Min(2f, spacing * 0.15f);
    private static bool Coordinate(float value) => OldMaidCardGeometry.IsFinite(value) && Math.Abs(value) <= MaxCoordinate;
    private static bool ValidPose(OldMaidCardPose pose) => Coordinate(pose.X) && Coordinate(pose.Y) &&
        OldMaidCardGeometry.IsFinite(pose.Angle) && OldMaidCardGeometry.PositiveFinite(pose.Width) &&
        OldMaidCardGeometry.PositiveFinite(pose.Height) && pose.Width <= 4096f && pose.Height <= 4096f;
}

// Decorative scheduling has no card identities/faces and cannot consume the
// game's private fair-draw Random. The caller supplies a separate cosmetic RNG,
// waits for idle PlayerTurn, captures its own IDs and animates before committing.
internal sealed class OldMaidOpponentReorderPlanner
{
    private int _lastDrawCount = -1;

    internal bool TryPlan(int drawCount, int handCount, Random decorativeRandom,
        out int sourceSlot, out int targetSlot)
    {
        sourceSlot = targetSlot = -1;
        if (decorativeRandom == null || drawCount < 0 || drawCount <= _lastDrawCount ||
            handCount < 1 || handCount > OldMaidGame.DeckSize) return false;
        _lastDrawCount = drawCount;
        if (drawCount == 0 || drawCount % 3 != 0 || handCount < 2 || decorativeRandom.Next(2) != 0)
            return false;
        sourceSlot = decorativeRandom.Next(handCount);
        targetSlot = decorativeRandom.Next(handCount - 1);
        if (targetSlot >= sourceSlot) targetSlot++;
        return true;
    }

    internal void Reset() => _lastDrawCount = -1;
}

// This receives only an already-arrived card, never candidates for a draw.
// A cancelled visual keeps the obligation until a real own-hand move commits.
internal sealed class OldMaidJokerReorder
{
    private int _drawCount = -1;
    private int _completedDrawCount = -1;
    internal bool IsPending => _drawCount >= 0;

    internal void ObserveArrival(OldMaidOwner receiver, int cardId, int drawCount)
    {
        if (receiver != OldMaidOwner.Opponent || cardId != OldMaidGame.DeckSize - 1
            || drawCount <= 0 || drawCount <= _completedDrawCount) return;
        _drawCount = drawCount;
    }

    internal bool TryPlan(IReadOnlyList<OldMaidCard> ownHand, Random decorativeRandom,
        out int source, out int target)
    {
        source = target = -1;
        if (!IsPending || ownHand == null || decorativeRandom == null) return false;
        if (ownHand.Count < 2 || ownHand.Count > OldMaidGame.DeckSize)
        { Complete(); return false; } // A lone Joker has no different order.
        for (var index = 0; index < ownHand.Count; index++)
            if (ownHand[index].IsJoker) { source = index; break; }
        if (source < 0) { Complete(); return false; }
        target = decorativeRandom.Next(ownHand.Count - 1);
        if (target >= source) target++;
        return true;
    }

    internal void Complete()
    {
        _completedDrawCount = Math.Max(_completedDrawCount, _drawCount);
        _drawCount = -1;
    }

    internal void Reset() => _drawCount = _completedDrawCount = -1;
}
