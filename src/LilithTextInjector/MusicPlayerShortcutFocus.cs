using System;

namespace LilithTextInjector;

// Windows foreground ownership is authoritative for these local shortcuts.
// A process match alone would also admit its native file picker or dialog.
internal static class MusicPlayerShortcutFocus
{
    internal static bool IsWindowsGameForeground(IntPtr gameWindow,
        IntPtr foregroundWindow, uint currentProcessId, uint foregroundProcessId)
        => gameWindow != IntPtr.Zero
            && foregroundWindow == gameWindow
            && currentProcessId != 0
            && foregroundProcessId == currentProcessId;
}
