using System;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UI.TraySettingNew;
using NativeEntries = Il2CppSystem.Collections.Generic.IReadOnlyList<PlayerLineEntry>;
using NativeEntryCount = Il2CppSystem.Collections.Generic.IReadOnlyCollection<PlayerLineEntry>;
using NativeEntryList = Il2CppSystem.Collections.Generic.List<PlayerLineEntry>;
using NativeSelection = Il2CppSystem.Action<PlayerLineEntry>;

namespace LilithTextInjector;

// A local player-choice adapter. Native code retains snack clothing, seating,
// topics, animation, physics and preferences. No model output enters this seam.
internal static partial class OldMaidNativeBridge
{
    private const string HarmonyId = Plugin.PluginGuid + ".old-maid-native";
    private const int GameEntryId = -184201;
    private static ManualLogSource? _log;
    private static bool _installed;
    [ThreadStatic] private static PlayerLineController? _snackMenuScope;
    private static MenuBinding? _menuBinding;
    private static PlayerLineController? _menuController;
    private static global::CharacterController? _scaleOwner;
    private static DialogueManager? _snackOwner;
    private static IntPtr _scaleOwnerPointer;
    private static int _scaleOwnerId;
    private static float _originalRatio;
    private static float _minimumRatio;
    private static float _maximumRatio;
    private static bool _scaleCaptured;
    private static bool _ending;

    internal static global::CharacterController? Character => global::CharacterController.s_activeInstance;

    // Readiness is only a start gate. A native SofaDoubt gesture after starting
    // must not be mistaken for leaving snack time (use IsSnackActive instead).
    internal static bool IsReady
    {
        get
        {
            try
            {
                var character = Character;
                var manager = DialogueManager.instance;
                return _installed && character != null && character.enabled &&
                    character.gameObject.activeInHierarchy && character.IsSofaSit &&
                    character.ClothingState == LilithClothingState.Casual &&
                    !character.IsDrag && !character.IsSleep && !character.IsAnyActionAnimPlaying &&
                    manager != null && manager._snackPajamasSwapRoutine == null &&
                    manager._snackPajamasReturnRoutine == null;
            }
            catch (Exception) { return false; }
        }
    }

    internal static bool IsSnackActive
    {
        get
        {
            try
            {
                var character = Character;
                return character != null && character.enabled && character.gameObject.activeInHierarchy &&
                    character.IsSofaSit && (!_scaleCaptured || IsScaleOwner(character));
            }
            catch (Exception) { return false; }
        }
    }

    internal static void Install(ManualLogSource log)
    {
        if (_installed) return;
        _log = log;
        var harmony = new Harmony(HarmonyId);
        try
        {
            var menu = AccessTools.Method(typeof(PlayerLineController), nameof(PlayerLineController.ShowSnackTimeMenu))
                ?? throw new MissingMethodException(nameof(PlayerLineController.ShowSnackTimeMenu));
            harmony.Patch(menu, prefix: Patch(nameof(EnterSnackMenu)), finalizer: Patch(nameof(LeaveSnackMenu)));
            Prefix(harmony, typeof(PlayerLineController), nameof(PlayerLineController.ShowEntryOptions), nameof(ExtendSnackMenu));
            Prefix(harmony, typeof(DialogueManager), nameof(DialogueManager.EndSnackSeating), nameof(BeforeEndSnack));
            Prefix(harmony, typeof(global::CharacterController), nameof(global::CharacterController.ReleaseSofaSit), nameof(BeforeReleaseSofa));
            Prefix(harmony, typeof(global::CharacterController), "OnDestroy", nameof(BeforeCharacterDestroyed));
            Prefix(harmony, typeof(PlayerLineController), "OnDestroy", nameof(BeforeMenuDestroyed));
            Prefix(harmony, typeof(CharacterInteractionHandler), "HandleMouseDown", nameof(AllowCharacterPointerDown));
            InstallSnackUiStability(harmony);
            ChessSeatLift.Install(harmony);
            _installed = true;
            log.LogInfo("Old Maid native snack-menu adapter initialized; native topics and preference ownership preserved.");
        }
        catch (Exception error)
        {
            try { harmony.UnpatchSelf(); }
            catch (Exception rollbackError)
            {
                log.LogError("Old Maid native patch rollback failed. kind=" + rollbackError.GetType().Name);
                throw new InvalidOperationException("Old Maid partial-patch rollback failed.");
            }
            _snackMenuScope = null;
            _menuBinding = null;
            Warn("install", error);
        }
    }

    private static HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(OldMaidNativeBridge), name);

    private static bool AllowCharacterPointerDown() => !OldMaidPresentation.OwnsNativePointerPress() && !ChessPresentation.OwnsPointerPress();

    private static void Prefix(Harmony harmony, Type type, string method, string prefix)
    {
        var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.Name, method);
        harmony.Patch(target, prefix: Patch(prefix));
    }

    private static bool EnterSnackMenu(PlayerLineController __instance, out PlayerLineController? __state)
    {
        __state = _snackMenuScope;
        if (ChessPresentation.IsOpen) { ChessPresentation.RequestActions(); return false; }
        _snackMenuScope = __instance;
        try { OldMaidPresentation.PauseForSnackMenu(); }
        catch (Exception error) { Warn("open_menu", error); }
        return true;
    }

    private static Exception? LeaveSnackMenu(Exception? __exception, PlayerLineController? __state)
    {
        _snackMenuScope = __state;
        return __exception;
    }

    private static void ExtendSnackMenu(PlayerLineController __instance, ref NativeEntries __0, ref NativeSelection __1)
    {
        ObserveRoundMenuReplacement(__instance);
        ObserveChessMenuReplacement(__instance);
        // ShowEntryOptions is also used by unrelated native menus. Never patch
        // those lists; the verified snack method calls this seam synchronously.
        if (!_installed || _snackMenuScope == null || __instance == null ||
            _snackMenuScope.Pointer != __instance.Pointer || __0 == null || __1 == null)
            return;
        try
        {
            var character = Character;
            if (character == null || !character.IsSofaSit) return;
            var count = __0.Cast<NativeEntryCount>().Count;
            if (count <= 0 || count + 2 > PlayerLineController.MaxOptionCount) return;
            var endId = PlayerLineController.SnackTimeEndEntryId;
            var endIndex = -1;
            for (var index = 0; index < count; index++)
            {
                var entry = __0[index];
                if (entry == null || entry.id == GameEntryId || entry.id == ChessEntryId) return;
                if (entry.id == endId) endIndex = index;
            }
            if (endIndex < 0) return;
            // Native code supports the fifth-entry layout, but a mismatched
            // scene asset must not truncate the end-snack choice. Inspect only;
            // native ApplyLayout remains responsible for activating the root.
            var layoutRoot = __instance.GetLayoutRoot(PlayerLineController.GetLayoutType(count + 2));
            if (layoutRoot == null || layoutRoot.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length < count + 2)
            {
                _log?.LogWarning("Old Maid snack entry unavailable: reason=native_menu_capacity.");
                return;
            }

            var copiedEntries = new NativeEntryList(count + 2);
            var gameEntry = new PlayerLineEntry { id = GameEntryId, LineID = GameEntryId,
                text = DialogueManagerUpdatePatch.MiniGameText(OldMaidPresentation.CanReplay ? "oldmaid.replay" : "oldmaid.play") };
            var chessEntry = new PlayerLineEntry { id = ChessEntryId, LineID = ChessEntryId,
                text = DialogueManagerUpdatePatch.MiniGameText(ChessPresentation.IsOpen ? "chess.continue" : "chess.play") };
            for (var index = 0; index < count; index++)
            {
                if (index == endIndex) { copiedEntries.Add(gameEntry); copiedEntries.Add(chessEntry); }
                copiedEntries.Add(__0[index]);
            }
            var binding = new MenuBinding(__instance, copiedEntries, gameEntry, chessEntry, __1, endId);
            binding.Wrapper = DelegateSupport.ConvertDelegate<NativeSelection>(new Action<PlayerLineEntry>(binding.Select))
                ?? throw new InvalidOperationException("Old Maid menu callback conversion failed.");
            var copiedView = copiedEntries.Cast<NativeEntries>()
                ?? throw new InvalidOperationException("Old Maid menu list conversion failed.");
            // Publish only after allocation/conversion succeeds. Original
            // callback and entry objects remain rooted and unmodified.
            _menuController = __instance;
            _menuBinding = binding;
            __0 = copiedView;
            __1 = binding.Wrapper;
        }
        catch (Exception error) { Warn("menu", error); }
    }

    private sealed class MenuBinding
    {
        internal readonly PlayerLineController Controller;
        // Keep every IL2CPP wrapper alive until the menu is replaced/destroyed;
        // Hide may run before the native OnButtonClicked invokes its callback.
        internal readonly NativeEntryList Entries;
        internal readonly PlayerLineEntry GameEntry;
        internal readonly PlayerLineEntry ChessEntry;
        internal readonly NativeSelection Original;
        internal NativeSelection Wrapper = null!;
        private readonly int _endId;

        internal MenuBinding(PlayerLineController controller, NativeEntryList entries,
            PlayerLineEntry gameEntry, PlayerLineEntry chessEntry, NativeSelection original, int endId)
        {
            Controller = controller;
            Entries = entries;
            GameEntry = gameEntry;
            ChessEntry = chessEntry;
            Original = original;
            _endId = endId;
        }

        internal void Select(PlayerLineEntry selected)
        {
            if (selected != null && selected.Pointer == ChessEntry.Pointer)
            {
                if (!ReferenceEquals(_menuBinding, this)) return;
                if (ChessPresentation.IsOpen) ChessPresentation.RequestActions();
                else if (IsReady) { OldMaidPresentation.EndSnack(); ShowChessModes(); }
                else ShowSnackMenu();
                return;
            }
            // Never allow our synthetic entry into native topic-id arithmetic.
            if (selected != null && selected.Pointer == GameEntry.Pointer)
            {
                if (!ReferenceEquals(_menuBinding, this)) return;
                try
                {
                    if (!IsReady) { ReportStartUnavailable(); ShowSnackMenu(); return; }
                    ChessPresentation.Close();
                    OldMaidPresentation.StartFromSnackMenu();
                    if (!OldMaidPresentation.IsOpen) ShowSnackMenu();
                }
                catch (Exception error)
                {
                    EndOwnedSnack();
                    Warn("start", error);
                    ShowSnackMenu();
                }
                return;
            }
            if (selected != null && selected.id == _endId) EndOwnedSnack();
            else if (_scaleCaptured)
            {
                try { OldMaidPresentation.Pause(); }
                catch (Exception error) { Warn("pause", error); }
            }
            Original.Invoke(selected!);
        }
    }

    internal static void ShowSnackMenu()
    {
        try
        {
            if (!IsSnackActive) return;
            var controller = _menuController;
            if (controller == null) controller = UnityEngine.Object.FindObjectOfType<PlayerLineController>();
            if (controller != null) controller.ShowSnackTimeMenu();
        }
        catch (Exception error) { Warn("return_menu", error); }
    }

    internal static bool TryBeginScale(out float current, out float maximum)
    {
        current = maximum = 0;
        try
        {
            if (!IsReady) return false;
            var character = Character!;
            if (_scaleCaptured)
            {
                if (!IsScaleOwner(character)) return false;
                current = _originalRatio;
                maximum = _maximumRatio;
                return true;
            }
            if (!TryReadNativeRange(out var minimum, out maximum)) return false;
            var transform = character.transform;
            var scale = transform.localScale;
            if (!FinitePositive(scale.x) || Mathf.Abs(scale.x - scale.y) > 0.0001f ||
                Mathf.Abs(scale.x - scale.z) > 0.0001f) return false;
            var lowScale = LilithScreenScale.ComputeFinalScale(minimum, transform);
            var highScale = LilithScreenScale.ComputeFinalScale(maximum, transform);
            if (!FinitePositive(lowScale) || !FinitePositive(highScale) || highScale <= lowScale ||
                scale.x < lowScale - 0.0001f || scale.x > highScale + 0.0001f) return false;
            // Invert the native screen-fraction conversion without applying a
            // probe scale or reading saved preferences. Snapshot what is visible.
            var low = minimum;
            var high = maximum;
            for (var iteration = 0; iteration < 24; iteration++)
            {
                var middle = (low + high) * 0.5f;
                var computed = LilithScreenScale.ComputeFinalScale(middle, transform);
                if (!FinitePositive(computed)) return false;
                if (computed < scale.x) low = middle;
                else high = middle;
            }
            current = Mathf.Clamp((low + high) * 0.5f, minimum, maximum);
            if (Mathf.Abs(LilithScreenScale.ComputeFinalScale(current, transform) - scale.x) > 0.0001f)
                return false;
            _scaleOwner = character;
            _scaleOwnerPointer = character.Pointer;
            _scaleOwnerId = character.GetInstanceID();
            _snackOwner = DialogueManager.instance;
            _originalRatio = current;
            _minimumRatio = minimum;
            _maximumRatio = maximum;
            _scaleCaptured = true;
            return true;
        }
        catch (Exception error) { Warn("scale_capture", error); return false; }
    }

    private static bool TryReadNativeRange(out float minimum, out float maximum)
    {
        minimum = maximum = 0;
        // The widget uses normalized slider coordinates internally. Read its
        // native item range, including inactive scene UI, not Slider.maxValue.
        foreach (var view in Resources.FindObjectsOfTypeAll<TraySettingNewView>())
        {
            if (view == null || !view.gameObject.scene.IsValid()) continue;
            var range = view.ResolveSliderRange("adjust_ratio");
            if (range == null || range.Length != 2 || !FinitePositive(range[0]) ||
                !FinitePositive(range[1]) || range[0] >= range[1]) continue;
            minimum = range[0];
            maximum = range[1];
            return true;
        }
        return false;
    }

    internal static void ApplyScale(float ratio)
    {
        if (!_scaleCaptured || !IsScaleOwner(Character) || !IsSnackActive || !FinitePositive(ratio))
            throw new InvalidOperationException("Old Maid scale ownership is not valid.");
        // Apply is the native non-persisting path: transform, follow scale,
        // last-change time and physics notification stay native-owned.
        LilithScreenScale.Apply(Mathf.Clamp(ratio, _minimumRatio, _maximumRatio), _scaleOwner!.transform);
    }

    internal static void RestoreScale(float original)
    {
        if (!_scaleCaptured) return;
        try
        {
            // The first captured ratio is authoritative; replay/caller values
            // cannot overwrite it or resize a replacement character instance.
            if (IsScaleOwner(_scaleOwner))
                LilithScreenScale.Apply(_originalRatio, _scaleOwner!.transform);
        }
        catch (Exception error)
        {
            // Keep the original snapshot for a later cleanup attempt. Native
            // exit hooks catch this failure and still allow their original code.
            Warn("scale_restore", error);
            throw;
        }
        _scaleCaptured = false;
        _scaleOwner = null;
        _snackOwner = null;
        _scaleOwnerPointer = IntPtr.Zero;
        _scaleOwnerId = 0;
    }

    private static bool IsScaleOwner(global::CharacterController? character)
    {
        try
        {
            return _scaleCaptured && character != null && character.Pointer == _scaleOwnerPointer &&
                character.GetInstanceID() == _scaleOwnerId;
        }
        catch (Exception) { return false; }
    }

    private static bool FinitePositive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

    private static void BeforeEndSnack(DialogueManager __instance)
    {
        ResetUiLift();
        ChessPresentation.Close();
        ClearChessChoices();
        if (_scaleCaptured && _snackOwner != null && __instance.Pointer == _snackOwner.Pointer) EndOwnedSnack();
    }

    private static void BeforeReleaseSofa(global::CharacterController __instance)
    {
        ResetUiLift();
        ChessPresentation.Close();
        if (IsScaleOwner(__instance)) EndOwnedSnack();
    }

    private static void BeforeCharacterDestroyed(global::CharacterController __instance)
    {
        ChessPresentation.Close();
        if (IsScaleOwner(__instance)) EndOwnedSnack();
    }

    private static void BeforeMenuDestroyed(PlayerLineController __instance)
    {
        if (_menuController == null || _menuController.Pointer != __instance.Pointer) return;
        EndOwnedSnack();
        _menuController = null;
        _menuBinding = null;
        ClearRoundChoices();
        ClearChessChoices();
    }

    internal static void Cleanup() => EndOwnedSnack();

    private static void EndOwnedSnack()
    {
        ChessPresentation.Close();
        if (!_scaleCaptured || _ending) return;
        _ending = true;
        try { OldMaidPresentation.EndSnack(); }
        catch (Exception error) { Warn("end", error); }
        finally
        {
            // Restore before native ReleaseSofaSit / pajamas return starts,
            // even when presenter cleanup fails. Never block native cleanup.
            try { if (_scaleCaptured) RestoreScale(_originalRatio); }
            catch (Exception error) { Warn("end_restore", error); }
            finally { _ending = false; }
        }
    }

    private static void Warn(string stage, Exception error) =>
        _log?.LogWarning("Old Maid native adapter: stage=" + stage + " kind=" + error.GetType().Name);
}
