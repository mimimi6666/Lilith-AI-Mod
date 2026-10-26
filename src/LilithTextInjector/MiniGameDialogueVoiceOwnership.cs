using System;

namespace LilithTextInjector;

// A single short-lived publication lease, not a game/session-wide exemption.
// This class knows no dialogue text and cannot claim another same-text node.
internal sealed class MiniGameDialogueVoiceOwnership
{
    internal const double MaximumLifetimeSeconds = 16;
    private long _nextGeneration;
    private long _generation;
    private int _managerIdentity;
    private nint _previousNodeIdentity;
    private nint _sourceNodeIdentity;
    private nint _nodeIdentity;
    private Guid _match;
    private string _locale = string.Empty;
    private double _beganAt;
    private double _expiresAt;
    private bool _publishing;
    private bool _publicationEntered;

    internal long BeginPublication(int managerIdentity, nint previousNodeIdentity, nint sourceNodeIdentity,
        Guid match, string locale, double now, double expiresAt)
    {
        Cancel();
        if (managerIdentity == 0 || sourceNodeIdentity == 0 || sourceNodeIdentity == previousNodeIdentity
            || match == Guid.Empty || string.IsNullOrWhiteSpace(locale)
            || locale.Length > 64 || !double.IsFinite(now) || !double.IsFinite(expiresAt)
            || now < 0 || expiresAt <= now || expiresAt - now > MaximumLifetimeSeconds)
            throw new ArgumentException("A bounded mini-game voice publication identity is required.");
        _generation = checked(++_nextGeneration);
        _managerIdentity = managerIdentity;
        _previousNodeIdentity = previousNodeIdentity;
        _sourceNodeIdentity = sourceNodeIdentity;
        _match = match;
        _locale = locale;
        _beganAt = now;
        _expiresAt = expiresAt;
        _publishing = true;
        return _generation;
    }

    // The caller admits exactly one direct BeginDialogue(source) publication.
    // Foreign ForceSay still cancels the lease; it never enters this scope.
    internal bool TryEnterPublication(long generation, int managerIdentity,
        Guid match, string locale, double now)
    {
        if (generation == 0 || generation != _generation || !_publishing) return false;
        if (_publicationEntered || !Matches(managerIdentity, match, locale, now))
        { Cancel(); return false; }
        _publicationEntered = true;
        return true;
    }

    // Current native BeginDialogue clones source, assigns CurrentNode, then
    // calls the existing PlayNodeVoice seam synchronously before its events.
    // Only that exact current runtime clone can establish voice ownership.
    internal bool TryOwnSynchronousNode(long generation, int managerIdentity,
        nint nodeIdentity, nint currentNodeIdentity, Guid match, string locale, double now)
    {
        // A stale callback must not revoke a newer publication.
        if (generation == 0 || generation != _generation || !_publishing) return false;
        if (!_publicationEntered || !Matches(managerIdentity, match, locale, now)
            || nodeIdentity == 0 || nodeIdentity == _previousNodeIdentity || nodeIdentity == _sourceNodeIdentity
            || nodeIdentity != currentNodeIdentity
            || (_nodeIdentity != 0 && _nodeIdentity != nodeIdentity))
        { Cancel(); return false; }
        _nodeIdentity = nodeIdentity;
        return true;
    }

    internal bool CompletePublication(long generation, bool succeeded, int managerIdentity,
        nint currentNodeIdentity, Guid match, string locale, double now)
    {
        // A stale completion must not revoke a newer publication.
        if (generation == 0 || generation != _generation || !_publishing) return false;
        if (!succeeded || !_publicationEntered || !Matches(managerIdentity, match, locale, now)
            || _nodeIdentity == 0 || currentNodeIdentity != _nodeIdentity)
        { Cancel(); return false; }
        _publishing = false;
        return true;
    }

    internal bool OwnsNode(int managerIdentity, nint nodeIdentity, nint currentNodeIdentity,
        Guid match, string locale, double now) =>
        !_publishing && _nodeIdentity != 0 && nodeIdentity == _nodeIdentity
        && currentNodeIdentity == _nodeIdentity && Matches(managerIdentity, match, locale, now);

    internal bool IsCurrent(Guid match, string locale, double now) =>
        !_publishing && _nodeIdentity != 0 && Matches(_managerIdentity, match, locale, now);

    private bool Matches(int managerIdentity, Guid match, string locale, double now) =>
        _generation != 0 && managerIdentity != 0 && managerIdentity == _managerIdentity
        && match != Guid.Empty && match == _match
        && string.Equals(locale, _locale, StringComparison.Ordinal)
        && double.IsFinite(now) && now >= _beganAt && now < _expiresAt;

    internal void Cancel()
    {
        _generation = 0;
        _managerIdentity = 0;
        _previousNodeIdentity = 0;
        _sourceNodeIdentity = 0;
        _nodeIdentity = 0;
        _match = Guid.Empty;
        _locale = string.Empty;
        _beganAt = _expiresAt = 0;
        _publishing = false;
        _publicationEntered = false;
    }
}
