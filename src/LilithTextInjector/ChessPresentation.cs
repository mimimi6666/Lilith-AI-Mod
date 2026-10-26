using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace LilithTextInjector;

// Isolated local mini-game. No model calls, physics root writes, or background
// game loop. Update owns rules/input; late native-camera callback owns visuals.
internal static partial class ChessPresentation
{
    private static ChessSession? session;
    private static ChessProgress? progress;
    private static GameObject? root;
    private static OldMaidForegroundLayer? foregroundLayer;
    private static ChessSeatLift? lift;
    private static ChessAssets? assets;
    private static ChessEngine? engine;
    private static Task<ChessEngineReply>? engineTask;
    private static CancellationTokenSource? engineCancellation;
    private static ChessSession? engineSession;
    private static readonly ChessThinkingPacing opponentPacing = new();
    private static readonly System.Random opponentTimingRandom = new();
    private static ChessLayout? layout;
    private static global::CharacterController? character;
    private static Il2CppSystem.Action<ScriptableRenderContext, Camera>? renderCallback;
    private static readonly ChessInteraction input = new();
    private static readonly ChessDoubleClick doubleClick = new();
    private static int selected => input.Selected;
    private static int pressFrame = -1, lastRenderedFrame = -10;
    private static int ignoreInputUntil, renderRevision = -1;
    private static double animationUntil;
    private static bool closing, rendering, outcomeSaved, resultAnnounced;
    private static bool claimArmed;
    private static string? pendingPromotion;
    private static int pendingPromotionRevision = -1;
    private static bool actionsRequested, setupChoicesShown, soundPending;
    private static ChessSound pendingSound;
    private static ChessChoiceKind choiceKind;
    private static Rect gestureBoard;
    private enum ChessChoiceKind { None, Setup, Actions, Promotion }
    private static ChessMode mode;
    internal static bool IsOpen => session != null || root != null;
    internal static int DialogueRevision => session?.Rules.Revision ?? -1;
    internal static bool DialogueFinished => session?.Phase == ChessPhase.Finished;
    internal static bool DialogueStable => session != null && session.Phase != ChessPhase.Setup
        && !closing && Now >= animationUntil;
    internal static MiniGameDialogueCue? DialogueResultCue => ChessDialogueEvents.Result(
        session?.Rules.Result ?? ChessResult.None, session?.PlayerOutcome ?? 0,
        session?.Mode == ChessMode.Challenge && (session.WhiteSeconds <= 0 || session.PinkSeconds <= 0));
    private static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;

    internal static void Open(ChessMode selectedMode)
    {
        Close();
        var stage = "readiness";
        try
        {
            if (!OldMaidNativeBridge.IsReady) { OldMaidNativeBridge.ShowSnackMenu(); return; }
            OldMaidPresentation.EndSnack();
            stage = "progress";
            progress = ChessProgress.Load(DialogueManagerUpdatePatch.ChessProgressPath);
            mode = selectedMode;
            character = OldMaidNativeBridge.Character;
            session = new ChessSession(mode, progress.NextPlayerWhite, progress.Strength, progress.Minutes);
            stage = "artwork";
            assets = new ChessAssets();
            stage = "surface";
            CreateSurface();
            stage = "character_display";
            lift = new ChessSeatLift(character!);
            stage = "render_subscription";
            renderCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
                new Action<ScriptableRenderContext, Camera>(BeforeRendering));
            RenderPipelineManager.add_beginCameraRendering(renderCallback);
            ignoreInputUntil = Time.frameCount + 2;
        }
        catch (Exception error)
        {
            var detail = error is OldMaidUiBindingException binding ? binding.Failure.ToString()
                : error is ChessArtworkException art ? art.Artwork + "/" + art.Failure : "unavailable";
            Plugin.PluginLog.LogWarning("Chess start unavailable: stage=" + stage + "; " + error.GetType().Name + "; " + detail);
            Close(); OldMaidNativeBridge.ShowSnackMenu();
        }
    }

    internal static void Update(bool foreground, bool modal)
    {
        if (session == null) return;
        if (character == null || !OldMaidNativeBridge.IsSnackActive || lift?.Failed == true) { Close(); return; }
        var now = Now;
        session.Tick(now); // elapsed challenge time never pauses for focus/UI/animation
        if (!foreground || modal || Time.frameCount - lastRenderedFrame > 2) CancelInput();
        if (root != null) root.SetActive(!DialogueManagerUpdatePatch.ReadMiniGamePresentationHidden());
        ChessAudio.SyncSettings();
        var animating = now < animationUntil;
        if (!animating && soundPending) { soundPending = false; ChessAudio.Play(root, pendingSound); }
        var busy = DialogueManagerUpdatePatch.ChessDialogueBusy();
        if (session.Phase == ChessPhase.Finished)
        {
            MiniGameConversationEvents.Shared.RecordChessResult(session);
            if (!outcomeSaved)
            {
                CancelEngine();
                CancelInput(); pendingPromotion = null; pendingPromotionRevision = -1;
                OldMaidNativeBridge.ClearChessChoices(); choiceKind = ChessChoiceKind.None;
                progress!.Complete(session.PlayerOutcome); SaveProgress(); outcomeSaved = true;
            }
            if (!animating && !resultAnnounced && foreground && !modal && !busy)
                resultAnnounced = DialogueManagerUpdatePatch.TryChessResultCue(session.PlayerOutcome);
        }
        if (session == null) return;
        if (!animating && session.Phase == ChessPhase.Playing && !session.PlayerTurn)
        {
            if (session.Rules.CanClaimDraw) { session.Rules.ClaimDraw(); session.ObserveEnd(); }
            else if (engineTask == null) BeginEngine();
            else if (engineTask is { IsCompleted: true } completed
                && (completed.IsFaulted || completed.IsCanceled
                    || opponentPacing.IsReady(publicContextMatch, session.Rules.Revision, now, session.Mode,
                        session.PlayerWhite ? session.PinkSeconds : session.WhiteSeconds))) FinishEngine();
        }
        if (session == null || root == null || modal) return;
        if (foreground && !busy) PresentChoices();
        // Speech/request busy protects new cues and menus, not board input.
        HandlePointer(foreground, animating);
    }

    private static void Start()
    {
        if (session == null || session.Phase != ChessPhase.Setup) return;
        progress!.NextPlayerWhite = !session.PlayerWhite;
        progress.Minutes = session.Minutes;
        SaveProgress();
        session.Start(Now);
        EnsurePublicContextIdentity(session);
        BeginOpponentThinking(Now);
        MiniGameConversationEvents.Shared.StartChess(publicContextMatch, session);
        OldMaidNativeBridge.ClearChessChoices(); choiceKind = ChessChoiceKind.None;
        DialogueManagerUpdatePatch.BeginMiniGameDialogue("chess", publicContextMatch);
        DialogueManagerUpdatePatch.OfferMiniGameDialogue("chess", session.PlayerWhite
            ? MiniGameDialogueCue.ChessStartPlayerWhite : MiniGameDialogueCue.ChessStartLilithWhite, session.Rules.Revision);
        ignoreInputUntil = Time.frameCount + 1;
    }

    private static void SaveProgress()
    {
        try { progress!.Save(DialogueManagerUpdatePatch.ChessProgressPath); }
        catch (Exception error)
        {
            DialogueManagerUpdatePatch.TryChessNotice(ChessNotice.ProgressUnavailable);
            Plugin.PluginLog.LogWarning("Chess progress save unavailable: " + error.GetType().Name);
        }
    }

    private static void CancelInput()
    {
        input.Cancel(); doubleClick.Reset();
    }

    internal static void ConsumeChoiceInput()
    {
        // Native option callbacks can hide their buttons before this frame's
        // board Update. Do not reuse that same press/release as a board action.
        CancelInput();
        ignoreInputUntil = Math.Max(ignoreInputUntil, Time.frameCount + 1);
    }

    internal static void RequestActions()
    {
        if (session == null || pendingPromotion != null) return;
        CancelInput();
        if (choiceKind == ChessChoiceKind.Actions && OldMaidNativeBridge.ChessChoicesVisible)
        { OldMaidNativeBridge.ClearChessChoices(); choiceKind = ChessChoiceKind.None; actionsRequested = false; return; }
        actionsRequested = true;
    }

    private static void PresentChoices()
    {
        var current = session!;
        if (OldMaidNativeBridge.ChessChoicesVisible) return;
        if (pendingPromotion != null)
        {
            if (!ChessPromotionChoicePolicy.CanOffer(current.Phase, current.PlayerTurn, current.Rules.Revision,
                pendingPromotionRevision, pendingPromotion, current.Rules.LegalMoves))
            { pendingPromotion = null; pendingPromotionRevision = -1; choiceKind = ChessChoiceKind.None; }
            else if (!Input.GetMouseButton(0))
                TryShowPromotionChoices(current, pendingPromotion, pendingPromotionRevision);
            return;
        }
        if (current.Phase == ChessPhase.Setup)
        {
            // Showing/dismissing options does not start a session. Reopening
            // setup must keep its adjusted clock and must never enter Restart.
            if (setupChoicesShown && !actionsRequested) return;
            if (OldMaidNativeBridge.TryShowChessChoices(new[] { DialogueManagerUpdatePatch.MiniGameText("start"), DialogueManagerUpdatePatch.MiniGameText("leave") }, choice =>
            {
                if (!ReferenceEquals(session, current) || current.Phase != ChessPhase.Setup) return;
                choiceKind = ChessChoiceKind.None;
                if (choice == 0) Start(); else { Close(); OldMaidNativeBridge.ShowSnackMenu(); }
            })) { setupChoicesShown = true; actionsRequested = false; choiceKind = ChessChoiceKind.Setup; }
            return;
        }
        if (!actionsRequested) return;
        var canClaim = current.Phase == ChessPhase.Playing && current.PlayerTurn
            && (current.Rules.CanClaimDraw || current.Rules.CanClaimByIntendedMove);
        var labels = canClaim ? new[] { DialogueManagerUpdatePatch.MiniGameText("restart"), DialogueManagerUpdatePatch.MiniGameText("leave"),
                DialogueManagerUpdatePatch.MiniGameText(claimArmed ? "draw.cancel" : "draw.claim") }
            : new[] { DialogueManagerUpdatePatch.MiniGameText("restart"), DialogueManagerUpdatePatch.MiniGameText("leave") };
        if (OldMaidNativeBridge.TryShowChessChoices(labels, choice =>
        {
            if (!ReferenceEquals(session, current)) return;
            choiceKind = ChessChoiceKind.None;
            if (choice == 2)
            {
                current.Tick(Now);
                if (current.Phase != ChessPhase.Playing || !current.PlayerTurn) return;
                if (current.Rules.ClaimDraw()) current.ObserveEnd();
                else claimArmed = !claimArmed;
                return;
            }
            var oldMode = mode; Close();
            if (choice == 0) Open(oldMode); else OldMaidNativeBridge.ShowSnackMenu();
        })) { actionsRequested = false; choiceKind = ChessChoiceKind.Actions; }
    }

    private static void BeginEngine()
    {
        var current = session!;
        engine ??= new ChessEngine(Path.Combine(ChessAssets.DirectoryPath, "stockfish.exe"), ChessAssets.EngineHash);
        engineCancellation = new CancellationTokenSource();
        var request = new ChessEngineRequest(current.Rules.StartFen, current.Rules.History.ToArray(), current.Rules.LegalMoves,
            current.Rules.Revision, current.Strength);
        var worker = engine; var token = engineCancellation.Token;
        engineSession = current;
        engineTask = Task.Run(() => worker.ChooseAsync(request, token), token);
    }

    private static void FinishEngine()
    {
        var completed = engineTask!; engineTask = null;
        var capturedSession = engineSession; engineSession = null;
        engineCancellation?.Dispose(); engineCancellation = null;
        try
        {
            var reply = completed.GetAwaiter().GetResult();
            if (!ReferenceEquals(session, capturedSession) || session?.Phase != ChessPhase.Playing
                || session.PlayerTurn || reply.Revision != session.Rules.Revision) return;
            if (session.Rules.ClaimDrawWith(reply.Move)) session.ObserveEnd();
            else Commit(reply.Move, false);
        }
        catch (Exception error)
        {
            Plugin.PluginLog.LogWarning("Chess opponent unavailable: " + error.GetType().Name);
            Close(); DialogueManagerUpdatePatch.TryChessNotice(ChessNotice.OpponentUnavailable); OldMaidNativeBridge.ShowSnackMenu();
        }
    }

    private static void CancelEngine()
    {
        engineCancellation?.Cancel();
        if (engineTask != null)
            _ = engineTask.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        engineTask = null; engineSession = null; opponentPacing.Clear();
        engineCancellation?.Dispose(); engineCancellation = null;
        engine?.Dispose(); engine = null;
    }

    private static void BeginOpponentThinking(double turnStartedAt)
    {
        opponentPacing.Clear();
        if (session?.Phase == ChessPhase.Playing && !session.PlayerTurn)
            opponentPacing.Begin(publicContextMatch, session.Rules.Revision, turnStartedAt,
                opponentTimingRandom.NextDouble());
    }

    private static void Commit(string move, bool fromPlayer)
    {
        var current = session!;
        var turnStartedAt = Now;
        current.Tick(turnStartedAt); // no move can beat a clock already at zero
        var from = ChessRules.Square(move.Substring(0, 2)); var to = ChessRules.Square(move.Substring(2, 2));
        var piece = current.Rules.PieceAt(from); var victim = current.Rules.PieceAt(to);
        var capturedAt = to;
        var wasInCheck = current.Rules.InCheck;
        if (char.ToLowerInvariant(piece) == 'p' && victim == '.' && from % 8 != to % 8)
        { capturedAt = to + (char.IsUpper(piece) ? -8 : 8); victim = current.Rules.PieceAt(capturedAt); }
        if (!current.TryMove(move, current.Rules.Revision, fromPlayer)) return;
        conversationAwareness.RecordCommittedMove(current, move, piece, victim, capturedAt);
        MiniGameConversationEvents.Shared.RecordChessMove(current, move, piece, victim, capturedAt);
        BeginOpponentThinking(turnStartedAt);
        movingFrom = from; movingTo = to; movingPiece = piece; capturedPiece = victim; capturedSquare = capturedAt;
        animationStarted = Now; animationUntil = animationStarted + 0.38;
        input.Cancel(); pendingPromotion = null; pendingPromotionRevision = -1;
        pendingSound = victim == '.' ? ChessSound.Move : ChessSound.Capture; soundPending = true;
        var cue = ChessDialogueEvents.Move(fromPlayer, piece, victim, from, to, move,
            wasInCheck, current.Rules.InCheck, current.Phase == ChessPhase.Finished, capturedAt != to);
        if (cue.HasValue) DialogueManagerUpdatePatch.OfferMiniGameDialogue("chess", cue.Value, current.Rules.Revision);
    }

    internal static bool OwnsPointerPress()
    {
        if (pressFrame == Time.frameCount) return true;
        return session != null && root != null && root.activeInHierarchy && layout != null
            && !DialogueManagerUpdatePatch.ReadOldMaidModal()
            && Input.GetMouseButtonDown(0) && Time.frameCount > ignoreInputUntil
            && foregroundLayer?.NativeUiOwnsPointer() == false && HitOwned(Input.mousePosition);
    }

    private static bool HitOwned(Vector3 pointer) => boardRect.Contains(pointer) || HitPiece(pointer) >= 0
        || (mode == ChessMode.Challenge && clockRect.Contains(pointer)) || characterRect.Contains(pointer);

    private static void HandlePointer(bool foreground, bool animating)
    {
        if (session == null || layout == null || renderRevision != session.Rules.Revision || Time.frameCount - lastRenderedFrame > 1
            || Time.frameCount <= ignoreInputUntil) { input.Cancel(); return; }
        var p = Input.mousePosition;
        var down = Input.GetMouseButtonDown(0);
        var nativeUi = foregroundLayer?.NativeUiOwnsPointer() != false;
        var owned = HitOwned(p);
        if (down && !nativeUi && owned)
        {
            pressFrame = Time.frameCount;
            if (!DialogueManagerUpdatePatch.TryActivateOldMaidPointer()) { CancelInput(); return; }
            foreground = DialogueManagerUpdatePatch.ReadOldMaidForeground();
        }
        var gate = new ChessInputGate(foreground, DialogueManagerUpdatePatch.ReadOldMaidModal(),
            root!.activeInHierarchy, nativeUi, OldMaidNativeBridge.ChessInputChoicesPending(), animating);
        if (!gate.SurfaceAllowsInput) { CancelInput(); return; }
        var ownedChoices = OldMaidNativeBridge.ChessChoicesVisible;
        if (gate.NativeChoices && choiceKind == ChessChoiceKind.Actions && ownedChoices)
        {
            // Dismiss an owned action menu on an outside press, but consume that
            // press: it must not also select/move a piece beneath the menu.
            CancelInput();
            if (down)
            { OldMaidNativeBridge.ClearChessChoices(); choiceKind = ChessChoiceKind.None; actionsRequested = false; }
            return;
        }
        if (gate.NativeChoices && !(ownedChoices && choiceKind == ChessChoiceKind.Setup))
        { CancelInput(); return; }
        if (down && owned && !boardRect.Contains(p) && !(mode == ChessMode.Challenge && clockRect.Contains(p))
            && HitPiece(p) < 0 && characterRect.Contains(p))
        {
            input.Cancel();
            if (!gate.NativeChoices && !animating && doubleClick.Press(Now, new System.Numerics.Vector2(p.x, p.y), Math.Max(12, layout.Width * 0.018f))) RequestActions();
            return;
        }
        if (down) doubleClick.Reset();
        if (session.Phase == ChessPhase.Setup)
        {
            if (down && clockRect.Contains(p) && ChessInputPolicy.CanAdjustSetupClock(gate, session.Phase, mode,
                ownedChoices && choiceKind == ChessChoiceKind.Setup))
            {
                var x = (p.x - clockRect.x) / clockRect.width * 1254;
                var y = 1254 - (p.y - clockRect.y) / clockRect.height * 1254;
                if (Math.Pow((x - 495) / 110, 2) + Math.Pow((y - 470) / 80, 2) <= 1) session.AdjustMinutes(1);
                else if (Math.Pow((x - 815) / 95, 2) + Math.Pow((y - 260) / 70, 2) <= 1) session.AdjustMinutes(-1);
            }
            return;
        }
        if (!ChessInputPolicy.CanMove(gate, session.Phase, session.PlayerTurn, pendingPromotion != null))
        { input.Cancel(); return; }
        if (input.Pressed)
        {
            if (gestureBoard != boardRect) { input.Cancel(); return; }
            input.Move(new System.Numerics.Vector2(p.x, p.y), Math.Max(6, layout.Width * 0.006f));
            if (Input.GetMouseButtonUp(0))
            {
                var target = input.Dragging
                    ? layout.Hit(input.DragFoot.X - workOffset.x, input.DragFoot.Y - workOffset.y, session.PlayerWhite)
                    : ResolveSquare(p);
                var move = input.Release(target, session.Rules.Revision);
                if (move.HasValue) ProposePlayerMove(move.Value);
            }
            else if (!Input.GetMouseButton(0)) input.Cancel();
            return;
        }
        if (!down || !owned) return;
        var square = ResolveSquare(p);
        if (square < 0) { input.Cancel(); return; }
        var fen = session.Rules.PieceAt(square);
        var foot = ScreenPoint(layout.SquareCenter(square, session.PlayerWhite));
        gestureBoard = boardRect;
        input.Press(square, fen != '.' && char.IsUpper(fen) == session.PlayerWhite, session.Rules.Revision,
            new System.Numerics.Vector2(p.x, p.y), new System.Numerics.Vector2(foot.x, foot.y));
    }

    private static int ResolveSquare(Vector3 p)
    {
        if (session == null || layout == null) return -1;
        var floorSquare = layout.Hit(p.x - workOffset.x, p.y - workOffset.y, session.PlayerWhite);
        // Match the draw order: an opaque piece covers board dots. Transparent
        // sprite margins still fall through to the underlying board square.
        var pieceSquare = HitPiece(p);
        return pieceSquare >= 0 ? pieceSquare : floorSquare;
    }

    private static void ProposePlayerMove(ChessMoveGesture gesture)
    {
        var current = session;
        if (current == null || current.Phase != ChessPhase.Playing || !current.PlayerTurn || gesture.Revision != current.Rules.Revision) return;
        var candidate = ChessRules.Square(gesture.From) + ChessRules.Square(gesture.To);
        var legal = current.Rules.LegalMoves;
        if (legal.Contains(candidate, StringComparer.Ordinal)) { PlayerMove(candidate); return; }
        if (legal.Any(m => m.StartsWith(candidate, StringComparison.Ordinal) && m.Length == 5))
        {
            pendingPromotion = candidate;
            pendingPromotionRevision = gesture.Revision;
            if (!DialogueManagerUpdatePatch.ChessDialogueBusy())
                TryShowPromotionChoices(current, candidate, gesture.Revision);
        }
    }

    private static void TryShowPromotionChoices(ChessSession current, string candidate, int revision)
    {
        if (!ReferenceEquals(session, current) || pendingPromotion != candidate || pendingPromotionRevision != revision
            || !ChessPromotionChoicePolicy.CanOffer(current.Phase, current.PlayerTurn, current.Rules.Revision,
                revision, candidate, current.Rules.LegalMoves)) return;
        if (OldMaidNativeBridge.TryShowChessChoices(new[] { DialogueManagerUpdatePatch.MiniGameText("promotion.queen"),
                DialogueManagerUpdatePatch.MiniGameText("promotion.rook"), DialogueManagerUpdatePatch.MiniGameText("promotion.bishop"),
                DialogueManagerUpdatePatch.MiniGameText("promotion.knight") }, choice =>
            {
                if (!ReferenceEquals(session, current) || pendingPromotion != candidate || pendingPromotionRevision != revision) return;
                current.Tick(Now); // Choosing after the clock expires cannot revive a finished game.
                var move = ChessPromotionChoicePolicy.SelectedMove(current.Phase, current.PlayerTurn,
                    current.Rules.Revision, revision, candidate, current.Rules.LegalMoves, choice);
                pendingPromotion = null; pendingPromotionRevision = -1; choiceKind = ChessChoiceKind.None;
                if (move != null) PlayerMove(move);
            })) choiceKind = ChessChoiceKind.Promotion;
        // Failure/replacement/outside dismissal retains the request for Update.
    }

    private static void PlayerMove(string move)
    {
        if (session == null) return;
        if (claimArmed)
        {
            session.Tick(Now);
            if (session.Phase == ChessPhase.Playing && session.Rules.ClaimDrawWith(move))
            { session.ObserveEnd(); claimArmed = false; input.Cancel(); }
            else DialogueManagerUpdatePatch.TryChessNotice(ChessNotice.InvalidClaim);
        }
        else Commit(move, true);
    }

    private static int HitPiece(Vector3 point)
    {
        if (session == null || assets == null) return -1;
        // The front-most opaque pixel wins; transparent sprite margins do not
        // steal clicks from another square or a piece farther behind it.
        for (var rank = 0; rank < 8; rank++)
        for (var file = 7; file >= 0; file--)
        {
            var display = rank * 8 + file; var square = session.PlayerWhite ? display : 63 - display;
            var piece = session.Rules.PieceAt(square); var view = pieceImages[square];
            if (piece == '.' || view == null || !view.gameObject.activeInHierarchy) continue;
            var rect = view.rectTransform; var size = rect.sizeDelta;
            var origin = rect.anchoredPosition - Vector2.Scale(rect.pivot, size);
            if (assets.HitPiece(piece, (point.x - origin.x) / size.x, (point.y - origin.y) / size.y)) return square;
        }
        return -1;
    }

    private static void BeforeRendering(ScriptableRenderContext context, Camera camera)
    {
        if (rendering || session == null || root == null || character == null || camera != character._mainCamera) return;
        rendering = true;
        try
        {
            if (!OldMaidNativeBridge.IsSnackActive) { Close(); return; }
            if (DialogueManagerUpdatePatch.ReadMiniGamePresentationHidden()) { root.SetActive(false); CancelInput(); return; }
            if (DialogueManagerUpdatePatch.ReadOldMaidModal()) CancelInput();
            if (Screen.width < 640 || Screen.height < 400) { Close(); OldMaidNativeBridge.ShowSnackMenu(); return; }
            root.SetActive(true);
            foregroundLayer!.Bind(character, Screen.width, Screen.height);
            var work = OldMaidWorkArea.Read(); workOffset = work.position;
            var center = camera.WorldToScreenPoint(character.transform.position).x - work.x;
            layout = new ChessLayout(work.width, work.height, center);
            lift?.Fit(layout, work.height, work.y, work.x);
            Render(); lastRenderedFrame = Time.frameCount;
        }
        catch (Exception error)
        {
            Plugin.PluginLog.LogWarning("Chess rendering stopped: " + error.GetType().Name);
            Close();
        }
        finally { rendering = false; }
    }

    internal static void Close()
    {
        if (closing) return;
        closing = true;
        try
        {
            var hadSession = IsOpen;
            Cleanup(() => DialogueManagerUpdatePatch.EndMiniGameDialogue("chess"));
            Cleanup(CancelEngine);
            Cleanup(OldMaidNativeBridge.ClearChessChoices);
            Cleanup(() => { if (renderCallback != null) RenderPipelineManager.remove_beginCameraRendering(renderCallback); }); renderCallback = null;
            Cleanup(() => lift?.Dispose()); lift = null;
            Cleanup(() => foregroundLayer?.Dispose()); foregroundLayer = null;
            Cleanup(() => clockDigits?.Dispose()); clockDigits = null;
            Cleanup(() => { if (root != null) UnityEngine.Object.Destroy(root); }); root = null;
            Cleanup(() => assets?.Dispose()); assets = null;
            if (hadSession) Cleanup(ChessAudio.Dispose);
        }
        catch (Exception error) { Plugin.PluginLog.LogWarning("Chess cleanup unavailable: " + error.GetType().Name); }
        finally
        {
            MiniGameConversationEvents.Shared.Clear(publicContextMatch);
            session = null; progress = null; character = null; layout = null;
            DialogueManagerUpdatePatch.RefreshMiniGameAnswerRepairLifetime();
            conversationAwareness.Clear();
            CancelInput(); pendingPromotion = null; pendingPromotionRevision = -1; lastRenderedFrame = -10; renderRevision = -1;
            outcomeSaved = resultAnnounced = claimArmed = actionsRequested = setupChoicesShown = soundPending = false;
            choiceKind = ChessChoiceKind.None; animationUntil = 0; closing = false;
            Array.Clear(pieceImages, 0, pieceImages.Length); Array.Clear(shadows, 0, shadows.Length);
            Array.Clear(targets, 0, targets.Length);
            movingFrom = movingTo = capturedSquare = -1;
        }
    }

    private static void Cleanup(Action release)
    {
        try { release(); }
        catch (Exception error) { Plugin.PluginLog.LogWarning("Chess resource cleanup: " + error.GetType().Name); }
    }
}
