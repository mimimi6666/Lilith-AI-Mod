namespace LilithTextInjector;

// Requested selection survives process exit. A dead child is not a new
// selection, while a real language change owns a fresh, bounded retry budget.
internal sealed class LocalVoiceHostLifecycle
{
    private int? _selection;
    private float _startedAt = -1f;

    internal int ConsecutiveFailures { get; private set; }
    internal bool LaunchAttempted { get; private set; }
    internal float RestartAt { get; private set; }
    internal bool Enabled => _selection is 1 or 2;

    internal bool Select(bool enabled, bool japanese, float now)
    {
        var selection = enabled ? (japanese ? 2 : 1) : 0;
        if (_selection == selection) return false;
        var changed = _selection.HasValue;
        _selection = selection;
        ConsecutiveFailures = 0;
        LaunchAttempted = false;
        _startedAt = -1f;
        RestartAt = changed ? now + 1f : now;
        return changed;
    }

    internal bool CanLaunch(float now) =>
        Enabled && !LaunchAttempted && now >= RestartAt;

    internal void MarkLaunchAttempt() => LaunchAttempted = true;

    internal void MarkStarted(float now) => _startedAt = now;

    internal void MarkStopped() => _startedAt = -1f;

    internal void ObserveRunning(float now)
    {
        if (_startedAt >= 0f && now - _startedAt >= 120f)
            ConsecutiveFailures = 0;
    }

    internal void RegisterFailure(float now)
    {
        _startedAt = -1f;
        ConsecutiveFailures = System.Math.Min(3, ConsecutiveFailures + 1);
        LaunchAttempted = ConsecutiveFailures >= 3;
        RestartAt = now + 5f * ConsecutiveFailures;
    }
}
