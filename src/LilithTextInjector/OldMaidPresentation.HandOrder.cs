using System;
using System.Collections.Generic;
using UnityEngine;

namespace LilithTextInjector;

internal static partial class OldMaidPresentation
{
    private static readonly OldMaidHandReorder HandDrag = new();
    private static readonly OldMaidOpponentReorderPlanner OpponentReorderPlanner = new();
    private static readonly OldMaidJokerReorder JokerReorder = new();
    private static readonly System.Random DecorativeRandom = new();
    private const float OpponentOrderSeconds = 0.65f;
    private static OldMaidHandMove? _opponentOrder;
    private static float _opponentOrderElapsed;
    private static OldMaidCardPose _dragOrigin;
    private static int _ownedPressFrame = -1;
    private static bool _mandatoryOrder;
    private static bool HandOrderingBusy => HandDrag.IsActive || _opponentOrder.HasValue || JokerReorder.IsPending;

    private static IReadOnlyList<OldMaidCardPose> HandBasePoses(bool opponent)
    {
        var area = opponent ? _opponentArea : _playerArea;
        var hand = opponent ? _game!.OpponentHand : _game!.PlayerHand;
        var width = (opponent ? 68 : 108) * Math.Clamp(_work.height / 900f, 0.6f, 1.4f);
        return OldMaidFloatingLayout.GetFan(hand.Count, area.x, area.y, area.width, area.height, width, width * 1.5f);
    }

    private static bool OrderingAvailable => _game != null && !DrawMotion.IsBusy && !PairReveal.IsBusy && _discardDelay <= 0
        && !_game.IsPaused && !Activity.IsSuspended && !Scale.IsAnimating
        && !_game.HasReservedOpponentDraw && _game.Phase != OldMaidPhase.Finished
        && _character != null && !_character.IsDrag;
    private static bool CanBeginOrdering => OrderingAvailable && !HandOrderingBusy;

    private static void TryBeginHandDrag(int slot)
    {
        if (!CanBeginOrdering || _game == null || _game.Revision != _renderedRevision
            || slot < 0 || slot >= _game.PlayerHand.Count) return;
        var poses = HandBasePoses(false);
        if (HandDrag.TryBegin(_game.PlayerHand, _renderedRevision, _game.PlayerHand[slot].Id,
            poses, Input.mousePosition.x, Input.mousePosition.y))
        {
            _dragOrigin = VisiblePose(_game.PlayerHand[slot].Id);
            _hover = _playerHover = -1;
        }
    }

    private static bool HandleHandDrag(bool foreground)
    {
        if (!HandDrag.IsActive) return false;
        // Always consume this capture's release, including cancellation. It
        // must never fall through to an opponent-card draw in the same frame.
        if (_game == null || _root == null || !_root.activeInHierarchy || !foreground
            || _game.IsPaused || Activity.IsSuspended || Scale.IsAnimating
            || _game.HasReservedOpponentDraw || DrawMotion.IsBusy || PairReveal.IsBusy || _discardDelay > 0
            || _character == null || _character.IsDrag || TransparentWindowNew.IsForceClickThrough
            || !_work.Contains(new Vector2(Input.mousePosition.x, Input.mousePosition.y))
            || !HandDrag.MatchesLayout(HandBasePoses(false)))
        { CancelHandOrdering(); return true; }
        var pointer = Input.mousePosition;
        if (Input.GetMouseButtonUp(0))
        {
            if (HandDrag.TryRelease(_game.PlayerHand, _game.Revision, pointer.x, pointer.y, out var move)
                && _game.TryReorderHand(OldMaidOwner.Player, move.CardId, move.TargetSlot, move.ExpectedCount, move.ExpectedRevision))
                MiniGameConversationEvents.Shared.RecordOldMaidReorder(_game, OldMaidOwner.Player, move);
            _opponentDelay = 0.9f;
        }
        else if (!Input.GetMouseButton(0)) CancelHandOrdering();
        else HandDrag.UpdatePointer(_game.PlayerHand, _game.Revision, pointer.x, pointer.y);
        return true;
    }

    // True holds the draw clock, not the already visible background discards.
    private static bool AdvanceHandOrdering(float delta)
    {
        if (HandDrag.IsActive) return true;
        if (_opponentOrder.HasValue)
        {
            var move = _opponentOrder.Value;
            if (_game == null || _game.Revision != move.ExpectedRevision || _game.IsPaused
                || _game.OpponentHand.Count != move.ExpectedCount || _game.HasReservedOpponentDraw)
            { CancelHandOrdering(); return true; }
            _opponentOrderElapsed = Math.Min(OpponentOrderSeconds, _opponentOrderElapsed + delta);
            if (_opponentOrderElapsed >= OpponentOrderSeconds)
            {
                if (_game.TryReorderHand(OldMaidOwner.Opponent, move.CardId, move.TargetSlot, move.ExpectedCount, move.ExpectedRevision))
                {
                    MiniGameConversationEvents.Shared.RecordOldMaidReorder(_game, OldMaidOwner.Opponent, move);
                    if (_mandatoryOrder) JokerReorder.Complete();
                }
                _opponentOrder = null;
                _mandatoryOrder = false;
                _hover = _playerHover = -1;
            }
            return true;
        }
        if (JokerReorder.IsPending && _game != null)
        {
            if (_game.Phase == OldMaidPhase.Finished) JokerReorder.Complete();
            else if (OrderingAvailable && !Input.GetMouseButton(0)
                && JokerReorder.TryPlan(_game.OpponentHand, DecorativeRandom, out var from, out var to))
            {
                _opponentOrder = new OldMaidHandMove(_game.OpponentHand[from].Id, from, to,
                    _game.OpponentHand.Count, _game.Revision);
                _opponentOrderElapsed = 0;
                _mandatoryOrder = true;
                _hover = _playerHover = -1;
                return true;
            }
            if (JokerReorder.IsPending) return true;
        }
        if (!CanBeginOrdering || _game!.Phase != OldMaidPhase.PlayerTurn
            || Input.GetMouseButton(0) || _hover >= 0 || _playerHover >= 0) return false;
        if (!OpponentReorderPlanner.TryPlan(_game.DrawCount, _game.OpponentHand.Count, DecorativeRandom,
            out var source, out var target)) return false;
        _opponentOrder = new OldMaidHandMove(_game.OpponentHand[source].Id, source, target,
            _game.OpponentHand.Count, _game.Revision);
        _opponentOrderElapsed = 0;
        return true;
    }

    private static OldMaidCardPose OrderedPose(int id, int slot, bool back, IReadOnlyList<OldMaidCardPose> poses)
    {
        if (!back && HandDrag.IsActive)
        {
            if (id == HandDrag.CardId)
                return Fit(new OldMaidCardPose(_dragOrigin.X + HandDrag.DeltaX,
                    _dragOrigin.Y + HandDrag.DeltaY + (HandDrag.IsDragging ? 12 : 0),
                    _dragOrigin.Angle, _dragOrigin.Width, _dragOrigin.Height));
            return poses[OldMaidHandReorder.SlotAfterMove(slot, HandDrag.SourceSlot, HandDrag.TargetSlot)];
        }
        if (back && _opponentOrder.HasValue)
        {
            var move = _opponentOrder.Value;
            var from = poses[slot];
            var to = poses[OldMaidHandReorder.SlotAfterMove(slot, move.SourceSlot, move.TargetSlot)];
            var t = _opponentOrderElapsed / OpponentOrderSeconds;
            var smooth = t * t * (3 - 2 * t);
            var lift = id == move.CardId ? MathF.Sin(t * MathF.PI) * from.Height * 0.28f : 0;
            return new OldMaidCardPose(from.X + (to.X - from.X) * smooth,
                from.Y + (to.Y - from.Y) * smooth + lift, from.Angle + (to.Angle - from.Angle) * smooth,
                from.Width, from.Height);
        }
        return poses[slot];
    }

    private static void RaiseOrderingCard(bool back)
    {
        var id = back ? _opponentOrder?.CardId ?? -1 : HandDrag.CardId;
        if (id < 0) return;
        var face = Card(id);
        face.Visual.SetGlow(true);
        face.Rect.SetAsLastSibling();
        if (!back && HandDrag.IsActive)
        {
            // Keep only the owned card-sized capture under the held pointer.
            // No desktop-wide hit box or click-through/window-state writes.
            PlaceInput(face.Input.rectTransform, OldMaidHandReorder.CapturePose(
                Input.mousePosition.x, Input.mousePosition.y, _dragOrigin.Width, _dragOrigin.Height));
            face.Input.gameObject.SetActive(true);
            face.Input.transform.SetAsLastSibling();
        }
    }

    private static void CancelHandOrdering()
    {
        HandDrag.Cancel();
        _opponentOrder = null;
        _opponentOrderElapsed = 0;
        _mandatoryOrder = false;
    }

    // Native input may run BEFORE DialogueManager.Update. This read-only test
    // uses last rendered stable footprints and consumes only an owned press.
    internal static bool OwnsNativePointerPress()
    {
        try
        {
            if (_game == null || _root == null || !_root.activeInHierarchy || _menuOpen
                || !Input.GetMouseButtonDown(0) || DialogueManagerUpdatePatch.ReadOldMaidModal()) return false;
            if (_foregroundLayer?.NativeUiOwnsPointer() == true) return false;
            if (_ownedPressFrame == Time.frameCount) return true;
            if (HandDrag.IsActive) return true;
            if (HitHand(_game.PlayerHand, _playerHover) < 0 && HitHand(_game.OpponentHand, _hover) < 0) return false;
            _ownedPressFrame = Time.frameCount;
            return true;
        }
        catch { return false; }
    }
}
