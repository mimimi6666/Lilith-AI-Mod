using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LilithTextInjector;

// Local player-driven projection: rules commit once before decorative motion.
// No custom toolbar. Native snack choices retain exit and replay ownership.
internal static partial class OldMaidPresentation
{
    private sealed class Face
    {
        internal OldMaidCardVisuals Visual = null!;
        internal RectTransform Rect => Visual.Rect;
        internal Image Input = null!;
        internal OldMaidCardPose HitPose;
        internal OldMaidSpatialCardVisual? Spatial;
    }

    private static readonly OldMaidScaleLease Scale = new();
    private static readonly OldMaidSpatialDrop Drops = new(Environment.TickCount);
    private static readonly System.Random Random = new();
    private static readonly OldMaidDrawAnimation DrawMotion = new();
    private static readonly OldMaidPairReveal PairReveal = new();
    private static readonly OldMaidActivityGate Activity = new();
    private static readonly OldMaidIdleMotion IdleMotion = new();
    private static readonly OldMaidRoundEndFlow RoundEnd = new();
    private static readonly OldMaidThrowSoundGate ThrowSounds = new();
    private static readonly Face?[] Cards = new Face?[OldMaidGame.DeckSize];
    private static readonly OldMaidSpatialTable?[] Launches = new OldMaidSpatialTable?[OldMaidGame.DeckSize];
    private static readonly OldMaidSpatialBody?[] DropBodies = new OldMaidSpatialBody?[OldMaidGame.DeckSize];
    private static readonly List<OldMaidSpatialProjection> ProjectedDrops = new(OldMaidGame.DeckSize);
    private static readonly bool[] RenderedDrops = new bool[OldMaidGame.DeckSize];
    private static OldMaidGame? _game;
    private static OldMaidPair? _revealPair;
    private static GameObject? _root;
    private static OldMaidForegroundLayer? _foregroundLayer;
    private static OldMaidDiscardLayer? _discardLayer;
    private static RectTransform? _dropShadows, _dropCards;
    private static TextMeshProUGUI? _font;
    private static global::CharacterController? _character;
    private static int _layer, _hover = -1, _playerHover = -1, _renderedRevision = -1;
    private static float _opponentDelay, _discardDelay;
    private static int _throwOrdinal, _movingCard = -1;
    private static IReadOnlyList<OldMaidCard>? _flightPlayerHand, _flightOpponentHand;
    private static bool _menuOpen, _faultReported, _introAnnounced, _movingToPlayer;
    private static Rect _work, _playerArea, _opponentArea;

    internal static bool IsOpen => _game != null || Scale.IsActive;
    internal static bool CanReplay => _game?.Phase == OldMaidPhase.Finished && !TurnVisualBusy;
    internal static int DialogueRevision => _game?.Revision ?? -1;
    internal static bool DialogueFinished => _game?.Phase == OldMaidPhase.Finished;
    internal static bool DialogueStable => _game != null && !_game.IsPaused && !_menuOpen
        && !TurnVisualBusy && !Scale.IsAnimating && !Activity.IsSuspended;

    internal static void StartFromSnackMenu()
    {
        try
        {
            if (!OldMaidNativeBridge.IsReady) return;
            // One bounded diagnostic per explicit start attempt, including
            // repeated failures before a surface could be created.
            _faultReported = false;
            if (_game == null)
            {
                if (!OldMaidNativeBridge.TryBeginScale(out var current, out var maximum)
                    || !Scale.TryBegin(current, maximum)) return;
                CreateSurface();
                _character = UnityEngine.Object.FindObjectOfType<global::CharacterController>();
                _game = new OldMaidGame();
                StartRound();
            }
            else if (CanReplay) StartRound();
            _menuOpen = false;
            // The native choice's click must finish before card input resumes.
            Pause();
            _root!.SetActive(true);
            _faultReported = false;
        }
        catch (Exception error) { Fail(error); }
    }

    internal static void Pause()
    {
        _game?.Pause();
        Activity.Suspend();
        VisibilityClock.Suspend();
        CancelHandOrdering();
        OldMaidAudio.Suspend();
        _pendingCue = null;
        _opponentDelay = 0.9f;
    }

    internal static void PauseForSnackMenu()
    {
        if (!IsOpen) return;
        Pause();
        _menuOpen = true;
        if (_root != null) _root.SetActive(false);
        HideSpatialCards();
    }

    // Native end sees the original non-persisted size, including on faults.
    internal static void EndSnack()
    {
        double? original = Scale.IsActive ? Scale.OriginalScale : null;
        try
        {
            if (original.HasValue) OldMaidNativeBridge.RestoreScale((float)original.Value);
            Scale.Reset();
        }
        finally
        {
            DialogueManagerUpdatePatch.EndMiniGameDialogue("old-maid");
            RemoveRenderHook();
            VisibilityClock.Clear();
            VisibilityDiagnostics.Clear();
            OldMaidNativeBridge.ClearRoundChoices();
            RoundEnd.Reset();
            IdleMotion.Reset();
            ThrowSounds.Reset();
            JokerReorder.Reset();
            OldMaidAudio.Dispose();
            _discardReady = false;
            _pendingCue = null;
            _ownedPressFrame = -1;
            CancelHandOrdering();
            var cleanupReported = false;
            foreach (var face in Cards)
            {
                try { face?.Spatial?.Dispose(); }
                catch (Exception error)
                {
                    if (!cleanupReported) Plugin.PluginLog.LogWarning("Old Maid spatial cleanup unavailable: " + error.GetType().Name);
                    cleanupReported = true;
                }
            }
            try { _discardLayer?.Dispose(); }
            catch (Exception error)
            {
                if (!cleanupReported) Plugin.PluginLog.LogWarning("Old Maid discard layer cleanup unavailable: " + error.GetType().Name);
            }
            _discardLayer = null;
            try { _foregroundLayer?.Dispose(); }
            catch (Exception error) { Plugin.PluginLog.LogWarning("Old Maid UI sorting restore unavailable: " + error.GetType().Name); }
            _foregroundLayer = null;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _dropShadows = _dropCards = null;
            _font = null;
            MiniGameConversationEvents.Shared.Clear(publicContextMatch);
            _game = null;
            DialogueManagerUpdatePatch.RefreshMiniGameAnswerRepairLifetime();
            _character = null;
            Array.Clear(Cards, 0, Cards.Length);
            Array.Clear(Launches, 0, Launches.Length);
            Array.Clear(DropBodies, 0, DropBodies.Length);
            Array.Clear(RenderedDrops, 0, RenderedDrops.Length);
            ProjectedDrops.Clear();
            Drops.Clear();
            DrawMotion.Clear();
            PairReveal.Clear();
            Activity.Clear();
            _revealPair = null;
            _flightPlayerHand = _flightOpponentHand = null;
            _movingCard = -1;
            _discardDelay = 0;
            _hover = _playerHover = -1;
            _renderedRevision = -1;
            _menuOpen = false;
            OldMaidCardVisuals.DisposeSharedAssets();
        }
    }

    internal static void Update(bool foreground, bool modal)
    {
        if (!IsOpen) return;
        try
        {
            if (_root == null || _character == null || !OldMaidNativeBridge.IsSnackActive)
            { EndSnack(); return; }
            if (Screen.width < 240 || Screen.height < 240)
            { Pause(); _root.SetActive(false); HideSpatialCards(); return; }
            if (!foreground || modal) Pause();
            if (_menuOpen) return;
            var hidden = DialogueManagerUpdatePatch.ReadMiniGamePresentationHidden();
            _root.SetActive(!hidden);
            if (hidden) { HideSpatialCards(); return; }
            var delta = Math.Min(Time.unscaledDeltaTime, 0.05f);
            var active = Activity.Step(foreground && !modal, Input.GetMouseButton(0), delta);
            if (active) _game!.Resume();
            OldMaidAudio.SyncSettings();
            _foregroundLayer!.Bind(_character, Screen.width, Screen.height);
            if (active && Scale.IsAnimating)
                OldMaidNativeBridge.ApplyScale((float)Scale.Advance(delta));
            UpdateAreas();
            if (!modal) HandlePointer(foreground);
            if (_game == null) return;
            VisibilityClock.Prepare(Time.frameCount, delta);
            if (active && _pendingCue.HasValue)
            {
                // Reordering does not change the public draw event. Bind after
                // all hand motion settles, so a forced shuffle cannot selectively
                // invalidate only one hidden-card outcome's speech candidate.
                if (_game.DrawCount != _pendingCueDrawCount || Time.unscaledTime - _pendingCueAt > 3
                    || _game.Phase == OldMaidPhase.Finished) _pendingCue = null;
                else if (DialogueStable)
                {
                    var cue = _pendingCue.Value;
                    _pendingCue = null;
                    DialogueManagerUpdatePatch.TryShowOldMaidCue(cue);
                }
            }
            if (active) UpdateRoundEnd(delta);
        }
        catch (Exception error) { Fail(error); }
    }

    private static void StartRound()
    {
        HideSpatialCards();
        Drops.Clear();
        Array.Clear(Launches, 0, Launches.Length);
        Array.Clear(DropBodies, 0, DropBodies.Length);
        ProjectedDrops.Clear();
        DrawMotion.Clear();
        PairReveal.Clear();
        Activity.Clear();
        VisibilityClock.Clear();
        CancelHandOrdering();
        OpponentReorderPlanner.Reset();
        JokerReorder.Reset();
        OldMaidNativeBridge.ClearRoundChoices();
        RoundEnd.Reset();
        IdleMotion.Reset();
        ThrowSounds.Reset();
        OldMaidAudio.Suspend();
        _ownedPressFrame = -1;
        _pendingCue = null;
        _discardReady = false;
        _revealPair = null;
        _flightPlayerHand = _flightOpponentHand = null;
        _movingCard = -1;
        _discardDelay = 0;
        _throwOrdinal = 0;
        _game!.Start(Random);
        publicContextMatch = Guid.NewGuid();
        MiniGameConversationEvents.Shared.StartOldMaid(publicContextMatch, _game);
        DialogueManagerUpdatePatch.BeginMiniGameDialogue("old-maid", publicContextMatch);
        DialogueManagerUpdatePatch.RefreshMiniGameAnswerRepairLifetime();
        _hover = _playerHover = -1;
        _renderedRevision = -1;
        _introAnnounced = false;
        _opponentDelay = 0.9f;
        UpdateAreas();
        EmitPairs();
        // The sole render/timeline admission is the native camera's late event.
    }

    private static void EmitPairs()
    {
        var sequence = 0;
        foreach (var pair in _game!.DrainPairs())
        {
            MiniGameConversationEvents.Shared.RecordOldMaidPair(_game, pair);
            var delay = sequence++ * 0.26f;
            Emit(pair.First, pair.Owner, 0, _throwOrdinal, delay);
            Emit(pair.Second, pair.Owner, 1, _throwOrdinal, delay);
            _throwOrdinal++;
        }
        if (sequence > 0) _discardDelay = (sequence - 1) * 0.26f + 0.6f;
    }

    private static void Emit(OldMaidCard card, OldMaidOwner owner, int index, int ordinal, float delay,
        OldMaidCardPose? visibleOrigin = null)
    {
        var area = owner == OldMaidOwner.Player ? _playerArea : _opponentArea;
        var center = area.center;
        center.x += ((ordinal % 5) - 2) * area.width * 0.065f;
        center.x += (index == 0 ? -1 : 1) * area.width * 0.025f;
        if (visibleOrigin.HasValue) center = new Vector2(visibleOrigin.Value.X, visibleOrigin.Value.Y);
        var ui = Math.Clamp(_work.height / 900f, 0.6f, 1.4f);
        var requestedWidth = (owner == OldMaidOwner.Player ? 108f : 68f) * ui;
        // Opening pairs have no rendered pose yet. Use the same area-fit as
        // RenderHand, including short work areas, rather than its requested size.
        var width = visibleOrigin?.Width ?? OldMaidFloatingLayout.GetFan(1,
            area.x, area.y, area.width, area.height, requestedWidth, requestedWidth * 1.5f)[0].Width;
        var origin = new OldMaidCardPose(center.x - _work.x, center.y - _work.y,
            visibleOrigin?.Angle ?? 0, width, visibleOrigin?.Height ?? width * 1.5f);
        // Keep the accepted contact/tumbling simulation. The desktop table
        // projection persistently calibrates size from this owner's real card,
        // lowers the common floor and scatters cards across its full width.
        if (!Drops.Emit(card.Id, owner == OldMaidOwner.Player ? 0 : 1, index, delay))
            throw new InvalidOperationException("Spatial discard unavailable.");
        var body = Drops.Bodies[Drops.Bodies.Count - 1];
        if (!OldMaidSpatialTable.TryCapture(body, origin, _work.width, _work.height, ordinal, out var launch))
            throw new InvalidOperationException("Spatial launch origin unavailable.");
        DropBodies[card.Id] = body;
        Launches[card.Id] = launch;
        ThrowSounds.Register(card.Id, ordinal);
    }

    private static void UpdateAreas()
    {
        _work = OldMaidWorkArea.Read();
        var sofa = SofaBounds();
        var player = OldMaidTableLayout.PlayerArea(_work.x, _work.y, _work.width, _work.height);
        _playerArea = new Rect(player.X - player.Width * 0.5f, player.Y - player.Height * 0.5f,
            player.Width, player.Height);
        var opponent = OldMaidTableLayout.OpponentArea(_work.x, _work.y, _work.width, _work.height,
            new OldMaidCardPose(sofa.center.x, sofa.center.y, 0, sofa.width, sofa.height));
        _opponentArea = new Rect(opponent.X - opponent.Width * 0.5f, opponent.Y - opponent.Height * 0.5f,
            opponent.Width, opponent.Height);
    }

    private static bool TurnVisualBusy => DrawMotion.IsBusy || PairReveal.IsBusy || _discardDelay > 0 || HandOrderingBusy;

    private static void AdvanceDraw(float delta)
    {
        if (_discardDelay > 0) { _discardDelay = Math.Max(0, _discardDelay - delta); return; }
        if (PairReveal.IsBusy)
        {
            PairReveal.Step(delta);
            if (PairReveal.TryConsumeReady() && _revealPair.HasValue)
            {
                var pair = _revealPair.Value;
                Emit(pair.First, pair.Owner, 0, _throwOrdinal, 0, Fit(PairReveal.FirstPose));
                Emit(pair.Second, pair.Owner, 1, _throwOrdinal, 0, Fit(PairReveal.SecondPose));
                _throwOrdinal++;
                _discardDelay = 0.6f;
                PairReveal.Clear();
                _revealPair = null;
                if (_game!.Phase != OldMaidPhase.Finished)
                    QueueCue(pair.Owner == OldMaidOwner.Player ? OldMaidCue.PlayerPair : OldMaidCue.LilithPair);
            }
            return;
        }
        if (DrawMotion.Stage == OldMaidDrawStage.Flying)
        {
            DrawMotion.Step(delta);
            if (DrawMotion.Stage == OldMaidDrawStage.Arrived)
            {
                BeginArrivalPair(_movingCard, Fit(DrawMotion.FlightPose));
                if (!_revealPair.HasValue && _game!.Phase != OldMaidPhase.Finished)
                    QueueCue(_movingToPlayer ? OldMaidCue.PlayerNoPair : OldMaidCue.LilithNoPair);
                JokerReorder.ObserveArrival(_movingToPlayer ? OldMaidOwner.Player : OldMaidOwner.Opponent,
                    _movingCard, _game!.DrawCount);
                DrawMotion.Clear();
                _flightPlayerHand = _flightOpponentHand = null;
                _movingCard = -1;
                _opponentDelay = 0.9f;
            }
            return;
        }
        if (DrawMotion.Stage == OldMaidDrawStage.Selecting)
        {
            DrawMotion.Step(delta);
            if (DrawMotion.Stage != OldMaidDrawStage.SelectionReady) return;
        }
        if (DrawMotion.Stage == OldMaidDrawStage.SelectionReady)
        {
            var slot = DrawMotion.TargetSlot;
            if (_game!.TryPrepareOpponentDraw(out var reserved, out var revision) && reserved == slot)
            {
                var from = VisiblePose(_game.PlayerHand[slot].Id);
                var to = ArrivalPose(_game.OpponentHand.Count, _opponentArea, false);
                CaptureFlightHands(_game.PlayerHand[slot].Id);
                var knownCard = _game.PlayerHand[slot];
                if (_game.TryOpponentDraw(slot, revision))
                {
                    MiniGameConversationEvents.Shared.RecordOldMaidDraw(_game, OldMaidOwner.Opponent, knownCard);
                    BeginFlight(from, to, false); return;
                }
            }
            ClearUncommittedSelection();
            return;
        }
        if (_game!.Phase != OldMaidPhase.OpponentTurn) return;
        _opponentDelay -= delta;
        if (_opponentDelay <= 0 && _game.TryPrepareOpponentDraw(out var selected, out _))
            DrawMotion.TryBeginSelection(_game.PlayerHand.Count, selected);
    }

    private static void BeginArrivalPair(int arrivedCard, OldMaidCardPose arrival)
    {
        var pairs = _game!.DrainPairs();
        if (pairs.Count == 0) return;
        // A single draw into a pair-free hand can create exactly one pair.
        if (pairs.Count != 1) throw new InvalidOperationException("Unexpected pair projection.");
        var pair = pairs[0];
        MiniGameConversationEvents.Shared.RecordOldMaidPair(_game, pair);
        if (pair.First.Id != arrivedCard && pair.Second.Id != arrivedCard)
            throw new InvalidOperationException("Arrival pair identity mismatch.");
        var firstFrom = pair.First.Id == arrivedCard ? arrival : VisiblePose(pair.First.Id);
        var secondFrom = pair.Second.Id == arrivedCard ? arrival : VisiblePose(pair.Second.Id);
        var area = pair.Owner == OldMaidOwner.Player ? _playerArea : _opponentArea;
        var width = arrival.Width;
        var center = area.center;
        var firstTo = Fit(new OldMaidCardPose(center.x - width * 0.55f, center.y, 0, width, width * 1.5f));
        var secondTo = Fit(new OldMaidCardPose(center.x + width * 0.55f, center.y, 0, width, width * 1.5f));
        if (!PairReveal.TryBegin(pair.First.Id, pair.Second.Id, firstFrom, secondFrom, firstTo, secondTo))
            throw new InvalidOperationException("Pair alignment unavailable.");
        _revealPair = pair;
    }

    private static void CaptureFlightHands(int cardId)
    {
        _flightPlayerHand = new List<OldMaidCard>(_game!.PlayerHand);
        _flightOpponentHand = new List<OldMaidCard>(_game.OpponentHand);
        _movingCard = cardId;
    }

    private static void ClearUncommittedSelection()
    {
        DrawMotion.Clear();
        _flightPlayerHand = _flightOpponentHand = null;
        _movingCard = -1;
        _opponentDelay = 0.9f;
    }

    private static OldMaidCardPose VisiblePose(int id)
    {
        var rect = Card(id).Rect;
        return new OldMaidCardPose(rect.anchoredPosition.x, rect.anchoredPosition.y,
            rect.localEulerAngles.z, rect.sizeDelta.x, rect.sizeDelta.y);
    }

    private static OldMaidCardPose ArrivalPose(int currentCount, Rect area, bool player)
    {
        var width = (player ? 108 : 68) * Math.Clamp(_work.height / 900f, 0.6f, 1.4f);
        var poses = OldMaidFloatingLayout.GetFan(currentCount + 1, area.x, area.y,
            area.width, area.height, width, width * 1.5f);
        return poses[poses.Count - 1];
    }

    private static void BeginFlight(OldMaidCardPose from, OldMaidCardPose to, bool toPlayer)
    {
        if (!DrawMotion.TryBeginFlight(from, to, !toPlayer, toPlayer))
            throw new InvalidOperationException("Draw projection unavailable.");
        _movingToPlayer = toPlayer;
        OldMaidAudio.Play(_root, OldMaidSound.Draw);
        _hover = _playerHover = -1;
    }

    private static OldMaidCardPose Fit(OldMaidCardPose pose) =>
        OldMaidTableLayout.FitFlight(pose, _work.x, _work.y, _work.width, _work.height);

    private static Rect SofaBounds()
    {
        var fallback = new Rect(_work.x + _work.width * 0.66f, _work.y, _work.width * 0.34f, _work.height * 0.63f);
        var camera = Camera.main;
        if (_character == null || camera == null) return fallback;
        var found = false;
        var bounds = default(Bounds);
        foreach (var renderer in _character.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude < 0.001f) continue;
            if (found) bounds.Encapsulate(renderer.bounds); else { bounds = renderer.bounds; found = true; }
        }
        if (!found) return fallback;
        var a = camera.WorldToScreenPoint(bounds.min);
        var b = camera.WorldToScreenPoint(bounds.max);
        return a.z <= 0 || b.z <= 0 ? fallback : Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    private static void HandlePointer(bool foreground)
    {
        if (_foregroundLayer?.NativeUiOwnsPointer() == true)
        { _hover = _playerHover = -1; if (HandDrag.IsActive) CancelHandOrdering(); return; }
        if (HandleHandDrag(foreground)) return;
        var previousHover = _hover;
        var previousPlayerHover = _playerHover;
        _hover = _playerHover = -1;
        if (_game == null || _root == null || !_root.activeSelf) return;
        // Stable base-frame footprints, not lifted RectTransforms or glow.
        _hover = HitHand(_game.OpponentHand, previousHover);
        if (_hover < 0) _playerHover = HitHand(_game.PlayerHand, previousPlayerHover);
        if ((_hover < 0 && _playerHover < 0) || !Input.GetMouseButtonDown(0)) return;
        if (!DialogueManagerUpdatePatch.TryActivateOldMaidPointer()) return;
        _ownedPressFrame = Time.frameCount;
        if (!foreground) { Pause(); return; }
        if (_playerHover >= 0)
        {
            TryBeginHandDrag(_playerHover);
            return;
        }
        if (_hover < 0 || Scale.IsAnimating || _game.IsPaused || Activity.IsSuspended || TurnVisualBusy
            || _game.Phase != OldMaidPhase.PlayerTurn) return;
        var from = VisiblePose(_game.OpponentHand[_hover].Id);
        var to = ArrivalPose(_game.PlayerHand.Count, _playerArea, true);
        CaptureFlightHands(_game.OpponentHand[_hover].Id);
        var knownCard = _game.OpponentHand[_hover];
        if (_game.TryPlayerDraw(_hover, _renderedRevision))
        {
            MiniGameConversationEvents.Shared.RecordOldMaidDraw(_game, OldMaidOwner.Player, knownCard);
            BeginFlight(from, to, true);
        }
        else ClearUncommittedSelection();
    }

    private static int HitHand(IReadOnlyList<OldMaidCard> hand, int raised)
    {
        if (raised >= 0 && raised < hand.Count && Hit(Cards[hand[raised].Id])) return raised;
        for (var i = hand.Count - 1; i >= 0; i--) if (Hit(Cards[hand[i].Id])) return i;
        return -1;
    }

    private static bool Hit(Face? face) => face != null && face.Input.gameObject.activeInHierarchy
        && face.Input.raycastTarget && OldMaidCardHitTest.Contains(face.HitPose, Input.mousePosition.x, Input.mousePosition.y);

    private static void Render()
    {
        if (_game == null || _root == null) return;
        Array.Clear(RenderedDrops, 0, RenderedDrops.Length);
        foreach (var face in Cards)
        {
            if (face == null) continue;
            face.Rect.gameObject.SetActive(false);
            face.Input.gameObject.SetActive(false);
        }
        var ui = Math.Clamp(_work.height / 900f, 0.6f, 1.4f);
        // Only discards use the separate native-camera-bound background layer.
        // Hands, pair reveals and draw flights keep their foreground surface.
        if (_discardReady)
        {
            var soundAudible = !_game.IsPaused && !Activity.IsSuspended
                && DialogueManagerUpdatePatch.ReadOldMaidForeground();
            foreach (var projection in ProjectDiscards())
            {
                var face = Card(projection.CardId);
                face.Spatial ??= OldMaidSpatialCardVisual.Create(_dropShadows!, _dropCards!, _discardLayer!.Layer, _font!);
                face.Spatial.SetRenderLayer(_discardLayer!.Layer);
                face.Spatial.SetRenderQueue(_discardLayer.RenderQueue);
                face.Spatial.Render(new OldMaidCard(projection.CardId), projection, projection.TextureCorners,
                    _work.x, _work.y, _work.width, _work.height);
                face.Spatial.Rect.SetAsLastSibling();
                RenderedDrops[projection.CardId] = true;
                var body = DropBodies[projection.CardId]!;
                if (ThrowSounds.Observe(projection.CardId, body.Phase, soundAudible))
                    OldMaidAudio.Play(_root, OldMaidSound.Throw);
            }
        }
        for (var id = 0; id < Cards.Length; id++)
            if (!RenderedDrops[id]) Cards[id]?.Spatial?.Hide();
        var playerHand = _flightPlayerHand ?? _game.PlayerHand;
        var opponentHand = _flightOpponentHand ?? _game.OpponentHand;
        var playerHighlight = DrawMotion.Stage is OldMaidDrawStage.Selecting or OldMaidDrawStage.SelectionReady
            ? DrawMotion.HighlightedSlot : _playerHover;
        RenderHand(playerHand, _playerArea, 108 * ui, false, playerHighlight);
        RenderHand(opponentHand, _opponentArea, 68 * ui, true, _hover);
        if (DrawMotion.Stage == OldMaidDrawStage.Flying && _movingCard >= 0)
        {
            var face = Card(_movingCard);
            SetFace(face, new OldMaidCard(_movingCard), !DrawMotion.FaceUp);
            Place(face, Fit(DrawMotion.FlightPose));
            face.Rect.localScale = new Vector3(DrawMotion.FlipScaleX, 1, 1);
            face.Rect.SetAsLastSibling();
        }
        if (PairReveal.IsBusy)
        {
            RenderPairCard(PairReveal.FirstCardId, PairReveal.FirstPose);
            RenderPairCard(PairReveal.SecondCardId, PairReveal.SecondPose);
        }
        _renderedRevision = _game.Revision;
    }

    private static IReadOnlyList<OldMaidSpatialProjection> ProjectDiscards()
    {
        ProjectedDrops.Clear();
        foreach (var body in DropBodies)
        {
            if (body == null || body.Phase == OldMaidSpatialPhase.Waiting) continue;
            var launch = Launches[body.CardId];
            if (launch == null || !launch.TryProject(body, _work.width, _work.height, out var projection)
                || projection == null)
                throw new InvalidOperationException("Spatial discard projection unavailable.");
            ProjectedDrops.Add(projection);
        }
        // This orders discards within their mathematical floor view. The owned
        // discard layer separately binds behind the actual native character;
        // moving that character never changes these scatter targets.
        ProjectedDrops.Sort((left, right) =>
        {
            var depth = right.Depth.CompareTo(left.Depth);
            return depth != 0 ? depth : left.CardId.CompareTo(right.CardId);
        });
        return ProjectedDrops;
    }

    private static void RenderPairCard(int id, OldMaidCardPose pose)
    {
        var face = Card(id);
        SetFace(face, new OldMaidCard(id), false);
        Place(face, Fit(pose));
        face.Visual.SetGlow(true);
        face.Rect.SetAsLastSibling();
    }

    private static void RenderHand(IReadOnlyList<OldMaidCard> hand, Rect area, float width, bool back, int hover)
    {
        var basePoses = OldMaidFloatingLayout.GetFan(hand.Count, area.x, area.y, area.width, area.height, width, width * 1.5f);
        var visualPoses = OldMaidFloatingLayout.GetFan(hand.Count, area.x, area.y, area.width, area.height, width, width * 1.5f, hover);
        for (var i = 0; i < visualPoses.Count; i++)
        {
            if (hand[i].Id == _movingCard) continue;
            var face = Card(hand[i].Id);
            SetFace(face, hand[i], back);
            var pose = OrderedPose(hand[i].Id, i, back, visualPoses);
            if (!(back ? _opponentOrder.HasValue : HandDrag.IsActive))
                pose = IdleMotion.Apply(pose, back, i);
            Place(face, Fit(pose));
            face.Visual.SetGlow(i == hover);
            face.Rect.SetAsLastSibling();
            if (!TurnVisualBusy && _game!.Phase != OldMaidPhase.Finished)
            {
                face.HitPose = basePoses[i];
                PlaceInput(face.Input.rectTransform, basePoses[i]);
                face.Input.gameObject.SetActive(true);
                face.Input.transform.SetAsLastSibling();
            }
        }
        if (hover >= 0 && hover < hand.Count && hand[hover].Id != _movingCard)
            Card(hand[hover].Id).Rect.SetAsLastSibling();
        RaiseOrderingCard(back);
    }

    private static Face Card(int id)
    {
        if (Cards[id] != null) return Cards[id]!;
        return Cards[id] = new Face
        {
            Visual = OldMaidCardVisuals.Create(_root!.transform, _layer, _font!),
            Input = OldMaidCardVisuals.CreateHitSurface(_root.transform, _layer)
        };
    }

    private static void SetFace(Face face, OldMaidCard card, bool back)
    {
        face.Input.gameObject.SetActive(false);
        face.Visual.SetCard(card, back);
        face.Visual.SetGlow(false);
        face.Visual.SetTilt(0);
    }

    private static void HideSpatialCards()
    {
        VisibilityClock.SuspendDiscards();
        _discardLayer?.Hide();
        if (!_discardReady) return;
        _discardReady = false;
        // Rebind cached native meshes once when this owned surface returns.
        // Suspending the canvas must not advance the simulation or redeal.
        foreach (var face in Cards)
        {
            face?.Spatial?.Invalidate();
            face?.Spatial?.Hide();
        }
    }

    private static bool TryBindDiscardLayer()
    {
        var reason = "discard-layer-unavailable";
        var ready = _discardLayer != null && _character != null
            && _discardLayer.TryBind(_character, Screen.width, Screen.height, out reason);
        if (!ready) HideSpatialCards();
        if (VisibilityDiagnostics.Observe(reason))
            Plugin.PluginLog.LogWarning("Old Maid discard visibility: " + reason + "; " + _discardLayer?.MaterialDiagnostic);
        return ready;
    }

    private static void CreateSurface()
    {
        var source = UnityEngine.Object.FindObjectOfType<DialogueBubbleUI>(true);
        _font = source?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (source == null || _font == null) throw new InvalidOperationException("Native dialogue font unavailable.");
        _layer = -1;
        var window = TransparentWindowNew.instance;
        if (window == null) throw new InvalidOperationException("Native UI hit testing unavailable.");
        var mask = window.hitTestLayerMask.value;
        foreach (var graphic in source.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic != null && graphic.raycastTarget && (mask & (1 << graphic.gameObject.layer)) != 0)
            { _layer = graphic.gameObject.layer; break; }
        }
        if (_layer < 0) throw new InvalidOperationException("Native UI hit layer unavailable.");
        _root = new GameObject("LilithOldMaidFloatingCards", Il2CppType.Of<RectTransform>(),
            Il2CppType.Of<Canvas>(), Il2CppType.Of<GraphicRaycaster>());
        _root.layer = _layer;
        _root.transform.SetParent(null, false);
        var canvas = _root.GetComponent<Canvas>();
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        _foregroundLayer = new OldMaidForegroundLayer(_root, source);
        _discardLayer = OldMaidDiscardLayer.Create();
        _dropShadows = _discardLayer.ShadowParent;
        _dropCards = _discardLayer.CardParent;
        InstallRenderHook();
        // No full-screen hit graphic, status strip, or secondary game menu.
    }

    private static void PlaceInput(RectTransform rect, OldMaidCardPose pose)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(pose.X, pose.Y);
        rect.sizeDelta = new Vector2(pose.Width, pose.Height);
        rect.localRotation = Quaternion.Euler(0, 0, pose.Angle);
        rect.localScale = Vector3.one;
    }

    private static void Place(Face face, OldMaidCardPose pose)
    {
        face.Rect.gameObject.SetActive(true);
        PlaceInput(face.Rect, pose);
        face.Visual.SetSize(pose.Width, pose.Height);
    }

    private static void Fail(Exception error)
    {
        try { EndSnack(); }
        catch (Exception cleanup) { Plugin.PluginLog.LogWarning("Old Maid restore unavailable: " + cleanup.GetType().Name); }
        if (!_faultReported)
        {
            var reason = error is OldMaidUiBindingException binding ? binding.Failure.ToString() : "Other";
            Plugin.PluginLog.LogWarning("Old Maid stopped; native snack retained: " + error.GetType().Name + "; ui=" + reason);
        }
        _faultReported = true;
    }
}
