namespace LilithTextInjector;

internal enum MusicPlayerShortcut { None, Previous, Next, TogglePause }

// Physical key edges, not chord edges: changing focus or releasing a modifier
// while a key remains held must never replay a suppressed command.
internal sealed class MusicPlayerShortcutState
{
    private bool _initialized;
    private bool _wasAllowed;
    private bool _previousDown;
    private bool _nextDown;
    private bool _pauseDown;

    internal MusicPlayerShortcut Observe(bool allowed, bool control,
        bool otherModifier, bool previousDown, bool nextDown, bool pauseDown)
    {
        var previousPressed = previousDown && !_previousDown;
        var nextPressed = nextDown && !_nextDown;
        var pausePressed = pauseDown && !_pauseDown;
        _previousDown = previousDown;
        _nextDown = nextDown;
        _pauseDown = pauseDown;
        var canIssue = _initialized && _wasAllowed && allowed;
        _wasAllowed = allowed;
        if (!_initialized)
        {
            _initialized = true;
            return MusicPlayerShortcut.None;
        }

        var heldCount = (previousDown ? 1 : 0) + (nextDown ? 1 : 0) + (pauseDown ? 1 : 0);
        if (!canIssue || !control || otherModifier || heldCount != 1)
            return MusicPlayerShortcut.None;
        if (previousPressed) return MusicPlayerShortcut.Previous;
        if (nextPressed) return MusicPlayerShortcut.Next;
        if (pausePressed) return MusicPlayerShortcut.TogglePause;
        return MusicPlayerShortcut.None;
    }

    internal void Invalidate()
    {
        _initialized = false;
        _wasAllowed = false;
    }
}
