namespace LilithTextInjector;

// Pointer ownership, not conversation activity. Speech/model-request state is
// deliberately absent: speaking cannot cancel a valid board gesture or pause
// a challenge clock. Callers still revalidate revision and legality on commit.
internal readonly record struct ChessInputGate(
    bool Foreground,
    bool Modal,
    bool SurfaceReady,
    bool NativePointer,
    bool NativeChoices,
    bool Animating)
{
    internal bool SurfaceAllowsInput => Foreground && !Modal && SurfaceReady && !NativePointer;
}

internal static class ChessInputPolicy
{
    internal static bool CanMove(ChessInputGate gate, ChessPhase phase, bool playerTurn, bool promotionPending) =>
        gate.SurfaceAllowsInput && !gate.NativeChoices && !gate.Animating
        && phase == ChessPhase.Playing && playerTurn && !promotionPending;

    internal static bool CanAdjustSetupClock(ChessInputGate gate, ChessPhase phase, ChessMode mode, bool ownSetupChoices) =>
        gate.SurfaceAllowsInput && !gate.Animating && phase == ChessPhase.Setup && mode == ChessMode.Challenge
        && (!gate.NativeChoices || ownSetupChoices);
}
