using System;

namespace LilithTextInjector;

// A native-accepted, fresh physical click is an activation request, not a
// keyboard permission. The adapter must revalidate the windows, invalidate
// shortcut edges and attempt activation once; the normal foreground gate
// remains authoritative even when this policy accepts the request.
internal sealed class MusicPlayerClickFocusState
{
    private bool _hasCandidateFrame;
    private int _lastCandidateFrame;

    internal bool TryRequest(int frame, bool freshLeftPress, bool nativeTargetAccepted,
        bool blocked, IntPtr gameWindow, uint currentProcessId, uint gameProcessId,
        IntPtr pointerRootWindow, uint pointerProcessId,
        IntPtr foregroundWindow, uint foregroundProcessId)
    {
        if (!freshLeftPress || !nativeTargetAccepted)
            return false;
        if (frame < 0 || (_hasCandidateFrame && frame <= _lastCandidateFrame))
            return false;

        // Consume a real candidate before any mutable guard. A second native
        // callback cannot retry a rejected candidate or failed OS activation.
        _hasCandidateFrame = true;
        _lastCandidateFrame = frame;

        if (blocked || gameWindow == IntPtr.Zero || currentProcessId == 0
            || gameProcessId != currentProcessId
            || pointerRootWindow != gameWindow || pointerProcessId != currentProcessId
            || foregroundWindow == IntPtr.Zero || foregroundProcessId == 0)
            return false;

        // Already-active game windows need no operation. A different window
        // belonging to this process may be a native modal dialog: never steal
        // focus from it, even if a game pointer callback is also observed.
        return foregroundWindow != gameWindow && foregroundProcessId != currentProcessId;
    }
}
