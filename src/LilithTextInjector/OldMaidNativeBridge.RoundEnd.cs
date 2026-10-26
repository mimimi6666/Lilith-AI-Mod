using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using NativeEntries = Il2CppSystem.Collections.Generic.IReadOnlyList<PlayerLineEntry>;
using NativeEntryList = Il2CppSystem.Collections.Generic.List<PlayerLineEntry>;
using NativeSelection = Il2CppSystem.Action<PlayerLineEntry>;

namespace LilithTextInjector;

internal static partial class OldMaidNativeBridge
{
    private const int ReplayEntryId = -184202, StopCardsEntryId = -184203;
    private static RoundMenuBinding? _roundMenuBinding;
    private static bool _showingRoundChoices;
    private static bool _roundOptionsFaultReported;

    internal static bool AreRoundChoicesVisible
    {
        get
        {
            try { return _roundMenuBinding != null && _roundMenuBinding.OwnsVisibleMenu; }
            catch (Exception) { return false; }
        }
    }

    internal static bool TryShowRoundChoices()
    {
        try
        {
            if (!OldMaidPresentation.CanReplay || !IsReady
                || !DialogueManagerUpdatePatch.CanPresentOldMaidLocalUi()) return false;
            var controller = _menuController;
            if (controller == null) controller = UnityEngine.Object.FindObjectOfType<PlayerLineController>();
            if (controller == null || controller.HasActiveOrPendingOptions) return false;
            var layout = controller.GetLayoutRoot(PlayerLineController.GetLayoutType(2));
            if (layout == null || layout.GetComponentsInChildren<UnityEngine.UI.Button>(true).Length < 2) return false;
            var binding = new RoundMenuBinding(controller);
            binding.Callback = DelegateSupport.ConvertDelegate<NativeSelection>(
                new Action<PlayerLineEntry>(binding.Select))
                ?? throw new InvalidOperationException("Round choice callback conversion failed.");
            _roundMenuBinding = binding;
            _menuController = controller;
            _showingRoundChoices = true;
            controller.ShowEntryOptions(binding.Entries.Cast<NativeEntries>(), binding.Callback);
            _roundOptionsFaultReported = false;
            return true;
        }
        catch (Exception error)
        {
            ClearRoundChoices();
            if (!_roundOptionsFaultReported) Warn("round_options", error);
            _roundOptionsFaultReported = true;
            return false;
        }
        finally { _showingRoundChoices = false; }
    }

    private static void ObserveRoundMenuReplacement(PlayerLineController controller)
    {
        if (_showingRoundChoices || _roundMenuBinding == null || controller == null
            || _roundMenuBinding.Controller.Pointer != controller.Pointer) return;
        // New native menu now owns its surface. Do not hide it or forward any
        // synthetic ID into its callback, topic lookup, or persistent records.
        _roundMenuBinding = null;
        OldMaidPresentation.RoundChoicesDismissed();
    }

    internal static void ClearRoundChoices()
    {
        var binding = _roundMenuBinding;
        _roundMenuBinding = null;
        try { if (binding != null && binding.OwnsVisibleMenu) binding.Controller.Hide(); }
        catch (Exception error) { Warn("round_cleanup", error); }
    }

    private sealed class RoundMenuBinding
    {
        internal readonly PlayerLineController Controller;
        internal readonly NativeEntryList Entries = new(2);
        private readonly PlayerLineEntry _replay = new()
            { id = ReplayEntryId, LineID = ReplayEntryId, text = DialogueManagerUpdatePatch.MiniGameText("replay") };
        private readonly PlayerLineEntry _stop = new()
            { id = StopCardsEntryId, LineID = StopCardsEntryId, text = DialogueManagerUpdatePatch.MiniGameText("leave") };
        internal NativeSelection Callback = null!;

        internal RoundMenuBinding(PlayerLineController controller)
        {
            Controller = controller;
            Entries.Add(_replay);
            Entries.Add(_stop);
        }

        internal bool OwnsVisibleMenu => Controller != null && Controller._isShowing
            && Controller._isEntrySelectMode && Controller._entrySelectCallback != null
            && Callback != null && Controller._entrySelectCallback.Pointer == Callback.Pointer;

        internal void Select(PlayerLineEntry selected)
        {
            // Native Hide may precede this callback; keep wrappers rooted
            // until selection/replacement, and validate this exact generation.
            if (!ReferenceEquals(_roundMenuBinding, this) || selected == null
                || (selected.Pointer != _replay.Pointer && selected.Pointer != _stop.Pointer)
                || !IsSnackActive || !OldMaidPresentation.TryChooseRoundEnd()) return;
            _roundMenuBinding = null;
            try
            {
                if (selected.Pointer == _replay.Pointer)
                {
                    OldMaidPresentation.StartFromSnackMenu();
                    if (OldMaidPresentation.CanReplay) OldMaidPresentation.RoundChoicesDismissed();
                }
                else
                {
                    // This restores our size lease/clears cards ONLY. Native
                    // seating, snack clothing and snack lifetime stay active.
                    EndOwnedSnack();
                    ShowSnackMenu();
                }
            }
            catch (Exception error)
            {
                Warn("round_choice", error);
                OldMaidPresentation.RoundChoicesDismissed();
            }
        }
    }
}
