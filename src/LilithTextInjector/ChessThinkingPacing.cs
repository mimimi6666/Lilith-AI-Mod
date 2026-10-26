using System;

namespace LilithTextInjector;

// Presentation timing only: no move selection, worker, clock mutation or sleep.
// The caller supplies one random sample at the start of each opponent turn.
internal sealed class ChessThinkingPacing
{
    internal const double MinimumSeconds = 2.5;
    internal const double MaximumSeconds = 5.5;
    private Guid match;
    private int revision = -1;
    private double startedAt;
    private double targetSeconds;

    internal void Begin(Guid currentMatch, int currentRevision, double turnStartedAt, double sample01)
    {
        Clear();
        if (currentMatch == Guid.Empty || currentRevision < 0
            || !double.IsFinite(turnStartedAt) || turnStartedAt < 0
            || !double.IsFinite(sample01) || sample01 < 0 || sample01 > 1)
            throw new ArgumentException("A bounded chess turn identity and timing sample are required.");
        match = currentMatch;
        revision = currentRevision;
        startedAt = turnStartedAt;
        targetSeconds = MinimumSeconds + (MaximumSeconds - MinimumSeconds) * sample01;
    }

    internal bool IsReady(Guid currentMatch, int currentRevision, double now,
        ChessMode mode, double remainingSeconds)
    {
        if (match == Guid.Empty || currentMatch != match || currentRevision != revision
            || !double.IsFinite(now) || now < startedAt
            || mode is not (ChessMode.Casual or ChessMode.Challenge)) return false;
        var wait = targetSeconds;
        if (mode == ChessMode.Challenge)
        {
            if (!double.IsFinite(remainingSeconds) || remainingSeconds < 0) return false;
            // Read the live clock on every release check. Time pressure can
            // shorten an existing wait; it never pauses or grants extra time.
            if (remainingSeconds <= 5) wait = 0;
            else if (remainingSeconds <= 15) wait = Math.Min(wait, (remainingSeconds - 5) * 0.15);
        }
        return now - startedAt >= wait;
    }

    internal void Clear()
    {
        match = Guid.Empty;
        revision = -1;
        startedAt = targetSeconds = 0;
    }
}
