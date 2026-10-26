using System;

namespace LilithTextInjector;

// Render admission, not wall time. An unseen interval is never replayed as a
// large time step. Repeated cameras/callbacks cannot advance the round twice.
internal sealed class OldMaidVisibilityClock
{
    private int _prepared = -1, _consumed = -1, _ready = -1;
    private float _delta;
    internal void Prepare(int frame, float delta)
    {
        if (frame < 0 || frame == _prepared) return;
        _prepared = frame;
        _delta = float.IsFinite(delta) && delta > 0 ? Math.Min(delta, 0.05f) : 0;
    }

    internal bool TryRender(int frame, bool available, bool active, out float delta)
    {
        delta = 0;
        if (frame < 0 || frame != _prepared || frame == _consumed) return false;
        _consumed = frame;
        if (available && active)
        {
            if (_ready >= 0 && _ready == frame - 1) delta = _delta;
            _ready = frame;
        }
        else _ready = -1;
        return true;
    }

    internal void Suspend() { _ready = -1; _delta = 0; }
    internal void Clear() { _prepared = _consumed = _ready = -1; _delta = 0; }
}

// Foreground hands/selection/flight and the native-bound discard surface have
// independent visibility. A rejected background binding must not freeze the
// visible turn or cancel a captured hand. Both remain subject to global pause.
internal sealed class OldMaidPresentationClock
{
    private readonly OldMaidVisibilityClock _foreground = new();
    private readonly OldMaidVisibilityClock _discards = new();

    internal void Prepare(int frame, float delta)
    { _foreground.Prepare(frame, delta); _discards.Prepare(frame, delta); }

    internal bool TryRender(int frame, bool discardsAvailable, bool active,
        out float foregroundDelta, out float discardDelta)
    {
        // Called only after the adapter validates the foreground root/camera.
        var admitted = _foreground.TryRender(frame, true, active, out foregroundDelta);
        _discards.TryRender(frame, discardsAvailable, active, out discardDelta);
        return admitted;
    }

    internal void SuspendDiscards() => _discards.Suspend();
    internal void Suspend() { _foreground.Suspend(); _discards.Suspend(); }
    internal void Clear() { _foreground.Clear(); _discards.Clear(); }
}

// Log transitions with a strict lifetime budget; no frame-by-frame spam and
// no asset names, player content or arbitrary exception text.
internal sealed class OldMaidVisibilityDiagnostics
{
    internal const int MaximumMessages = 12;
    private string? _last;
    private int _messages;
    internal bool Observe(string reason)
    {
        if (reason == _last) return false;
        var firstReady = _last == null && reason == "ready";
        _last = reason;
        if (firstReady || _messages >= MaximumMessages) return false;
        _messages++;
        return true;
    }
    internal void Clear() { _last = null; _messages = 0; }
}
