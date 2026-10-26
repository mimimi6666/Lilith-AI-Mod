using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace LilithTextInjector;

internal static partial class OldMaidPresentation
{
    private static readonly OldMaidPresentationClock VisibilityClock = new();
    private static readonly OldMaidVisibilityDiagnostics VisibilityDiagnostics = new();
    private static Il2CppSystem.Action<ScriptableRenderContext, Camera>? _renderCallback;
    private static bool _rendering, _discardReady;
    private static OldMaidCue? _pendingCue;
    private static float _pendingCueAt;
    private static int _pendingCueDrawCount;

    private static void QueueCue(OldMaidCue cue)
    {
        _pendingCue = cue;
        _pendingCueAt = Time.unscaledTime;
        _pendingCueDrawCount = _game?.DrawCount ?? -1;
    }

    private static void InstallRenderHook()
    {
        if (_renderCallback != null) return;
        _renderCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ScriptableRenderContext, Camera>>(
            new Action<ScriptableRenderContext, Camera>(BeforeCameraRendering));
        RenderPipelineManager.add_beginCameraRendering(_renderCallback);
    }

    private static void RemoveRenderHook()
    {
        if (_renderCallback == null) return;
        try
        {
            RenderPipelineManager.remove_beginCameraRendering(_renderCallback);
            _renderCallback = null;
        }
        catch (Exception error) { Plugin.PluginLog.LogWarning("Old Maid render callback cleanup unavailable: " + error.GetType().Name); }
    }

    private static void BeforeCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // SRP callback follows native LateUpdate mesh/material work. Never
        // advance on an unrelated camera, nested callback or stale Update.
        if (_rendering || _game == null || _root == null || _character == null
            || camera == null || camera != _character._mainCamera) return;
        _rendering = true;
        try
        {
            if (!OldMaidNativeBridge.IsSnackActive) { EndSnack(); return; }
            var foreground = DialogueManagerUpdatePatch.ReadOldMaidForeground();
            var modal = DialogueManagerUpdatePatch.ReadOldMaidModal();
            var hidden = DialogueManagerUpdatePatch.ReadMiniGamePresentationHidden();
            if (_menuOpen || hidden || Screen.width < 240 || Screen.height < 240)
            { Pause(); _root.SetActive(false); HideSpatialCards(); return; }
            if (!foreground || modal) Pause();
            _root.SetActive(true);
            _foregroundLayer!.Bind(_character, Screen.width, Screen.height);
            UpdateAreas();
            var ready = TryBindDiscardLayer();
            var active = foreground && !_game.IsPaused && !Activity.IsSuspended && !Scale.IsAnimating;
            if (!VisibilityClock.TryRender(Time.frameCount, ready, active, out var delta, out var discardDelta)) return;
            _discardReady = ready;
            if (delta > 0)
            {
                if (!TurnVisualBusy) IdleMotion.Step(delta);
                if (!AdvanceHandOrdering(delta)) AdvanceDraw(delta);
                if (!_introAnnounced && !TurnVisualBusy)
                { _introAnnounced = true; QueueCue(OldMaidCue.Start); }
                if (CanReplay)
                {
                    MiniGameConversationEvents.Shared.RecordOldMaidResult(_game);
                    RoundEnd.Observe(_game.Winner);
                }
            }
            if (discardDelta > 0) Drops.Step(discardDelta);
            // Only hidden background bodies wait for their own surface. The
            // visible selection/flight/pair/hand-order timeline stays playable.
            Render();
        }
        catch (Exception error) { Fail(error); }
        finally { _rendering = false; }
    }
}
