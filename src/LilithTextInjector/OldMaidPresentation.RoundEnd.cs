using UnityEngine;

namespace LilithTextInjector;

internal static partial class OldMaidPresentation
{
    // Native UI calls happen in Update, never inside a camera/render callback.
    private static void UpdateRoundEnd(float delta)
    {
        if (!CanReplay) return;
        RoundEnd.Step(delta);
        if (RoundEnd.Stage == OldMaidRoundEndStage.CuePending)
        {
            var cue = RoundEnd.Winner == OldMaidWinner.Player ? OldMaidCue.PlayerWon : OldMaidCue.LilithWon;
            if (RoundEnd.TryBeginUiAttempt() && DialogueManagerUpdatePatch.TryShowOldMaidCue(cue)) RoundEnd.CueShown();
        }
        else if (RoundEnd.Stage == OldMaidRoundEndStage.ChoicePending)
        {
            if (!Input.GetMouseButton(0) && RoundEnd.TryBeginUiAttempt()
                && OldMaidNativeBridge.TryShowRoundChoices()) RoundEnd.ChoicesShown();
        }
        else if (RoundEnd.Stage == OldMaidRoundEndStage.ChoiceVisible && !OldMaidNativeBridge.AreRoundChoicesVisible)
        {
            // Native auto-hide/cancel/menu replacement retains the completed
            // round. Re-offer only after other native UI becomes free again.
            RoundEnd.RetryChoices();
        }
    }

    internal static bool TryChooseRoundEnd() => CanReplay && RoundEnd.TryChoose();
    internal static void RoundChoicesDismissed() => RoundEnd.RetryChoices();
}
