using System;
using HarmonyLib;
using UnityEngine;

namespace LilithTextInjector;

internal static partial class OldMaidNativeBridge
{
    private static readonly OldMaidStableUiLift BubbleLift = new(), ChoiceLift = new();
    private static int _bubbleLiftRect, _choiceLiftRect;
    [ThreadStatic] private static RectTransform? _snackUiScope;

    private static void InstallSnackUiStability(Harmony harmony)
    {
        // Native body inspection confirms these LateUpdate paths call the
        // head-drop getter directly, NOT DialogueBubblePoseLift. Scope that
        // getter so physics, sleep and any other consumers stay untouched.
        var bubbleUpdate = AccessTools.Method(typeof(DialogueBubbleUI), "LateUpdate")
            ?? throw new MissingMethodException("DialogueBubbleUI.LateUpdate");
        var choiceUpdate = AccessTools.Method(typeof(PlayerLineController), "LateUpdate")
            ?? throw new MissingMethodException("PlayerLineController.LateUpdate");
        var headDrop = AccessTools.PropertyGetter(typeof(global::CharacterController),
            nameof(global::CharacterController.BubbleHeadDropWorldY))
            ?? throw new MissingMethodException("CharacterController.BubbleHeadDropWorldY");
        harmony.Patch(bubbleUpdate, prefix: Patch(nameof(EnterBubbleUiUpdate)),
            finalizer: Patch(nameof(LeaveSnackUiUpdate)));
        harmony.Patch(choiceUpdate, prefix: Patch(nameof(EnterChoiceUiUpdate)),
            finalizer: Patch(nameof(LeaveSnackUiUpdate)));
        harmony.Patch(headDrop, postfix: Patch(nameof(StabilizeSnackUiHeadDrop)));
    }

    private static void EnterBubbleUiUpdate(DialogueBubbleUI __instance, out RectTransform? __state)
    {
        __state = _snackUiScope;
        _snackUiScope = ReadSnackUiScope(__instance);
    }

    private static void EnterChoiceUiUpdate(PlayerLineController __instance, out RectTransform? __state)
    {
        __state = _snackUiScope;
        _snackUiScope = ReadSnackUiScope(__instance);
    }

    private static RectTransform? ReadSnackUiScope(Component component)
    {
        try { return component == null ? null : component.GetComponent<RectTransform>(); }
        catch (Exception) { return null; } // Optional presentation cannot break native UI.
    }

    private static Exception? LeaveSnackUiUpdate(Exception? __exception, RectTransform? __state)
    {
        _snackUiScope = __state;
        return __exception;
    }

    // Only the actual UI consumer's seated world offset is stabilized; native
    // code still divides by its parent scale. Native follow, horizontal
    // clamp, text typing, button entry animation and callbacks remain native.
    private static void StabilizeSnackUiHeadDrop(global::CharacterController __instance, ref float __result)
    {
        try
        {
            if (!IsSnackActive) { ResetUiLift(); return; }
            if (_snackUiScope == null) return;
            // Do not capture an intermediate value during the first sit-down.
            // Once captured, later seated gestures retain the stable offset.
            if (_bubbleLiftRect == 0 && !IsReady) return;
            var character = Character;
            var camera = character?._mainCamera;
            if (character == null || __instance == null || __instance.Pointer != character.Pointer
                || camera == null || !_snackUiScope.IsChildOf(character.transform)) return;
            var id = _snackUiScope.GetInstanceID();
            if (_bubbleLiftRect == 0) _bubbleLiftRect = id;
            if (id != _bubbleLiftRect && _choiceLiftRect == 0) _choiceLiftRect = id;
            // Exactly two native callers/surfaces, never an unbounded cache.
            var state = id == _bubbleLiftRect ? BubbleLift : id == _choiceLiftRect ? ChoiceLift : null;
            if (state == null) return;
            var parent = _snackUiScope.parent;
            if (parent == null) return;
            var scale = parent.lossyScale;
            var captured = state.IsCaptured;
            __result = state.Apply(true, character.GetInstanceID(), Screen.width, Screen.height,
                Math.Abs(scale.x), Math.Abs(scale.y), camera.orthographicSize, __result);
            if (!captured && state.IsCaptured)
                _log?.LogInfo("Old Maid snack UI offset captured; surface=" + (id == _bubbleLiftRect ? "1" : "2"));
        }
        catch (Exception) { ResetUiLift(); } // Keep the original result on unsupported native state.
    }

    private static void ResetUiLift()
    {
        BubbleLift.Reset(); ChoiceLift.Reset();
        _bubbleLiftRect = _choiceLiftRect = 0;
    }

    // Only called for an explicit rejected game-entry selection. Numeric
    // readiness bits describe existing gates; they never grant readiness.
    private static void ReportStartUnavailable()
    {
        var flags = 0;
        try
        {
            if (_installed) flags |= 1;
            var character = Character;
            var manager = DialogueManager.instance;
            if (character != null && character.enabled && character.gameObject.activeInHierarchy) flags |= 2;
            if (character != null)
            {
                if (character.IsSofaSit) flags |= 4;
                if (character.ClothingState == LilithClothingState.Casual) flags |= 8;
                if (!character.IsDrag) flags |= 16;
                if (!character.IsSleep) flags |= 32;
                if (!character.IsAnyActionAnimPlaying) flags |= 64;
            }
            if (manager != null)
            {
                flags |= 128;
                if (manager._snackPajamasSwapRoutine == null) flags |= 256;
                if (manager._snackPajamasReturnRoutine == null) flags |= 512;
            }
        }
        catch (Exception) { /* Preserve partial, bounded metadata only. */ }
        _log?.LogWarning("Old Maid entry not ready; flags=" + flags.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
