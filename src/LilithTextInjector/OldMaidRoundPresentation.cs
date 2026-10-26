using System;

namespace LilithTextInjector;

// Visual-only motion. It never supplies input geometry, moves game objects or
// reads wall time. The presenter advances it on the admitted foreground clock.
internal sealed class OldMaidIdleMotion
{
    internal const float PeriodSeconds = 5.6f;
    private float _elapsed;

    internal void Step(float delta)
    {
        if (OldMaidCardGeometry.PositiveFinite(delta))
            _elapsed = (_elapsed + Math.Min(delta, 0.05f)) % PeriodSeconds;
    }

    internal OldMaidCardPose Apply(OldMaidCardPose pose, bool opponent, int slot)
    {
        var phase = _elapsed * (2f * MathF.PI / PeriodSeconds) + (opponent ? 1.4f : 0f) + slot * 0.12f;
        var amplitude = Math.Min(3f, pose.Height * 0.018f);
        return new OldMaidCardPose(pose.X + MathF.Sin(phase) * amplitude * 0.2f,
            pose.Y + MathF.Sin(phase) * amplitude, pose.Angle + MathF.Sin(phase) * 0.3f,
            pose.Width, pose.Height);
    }

    internal void Reset() => _elapsed = 0;
}

internal enum OldMaidRoundEndStage { Idle, CuePending, Reading, ChoicePending, ChoiceVisible, Chosen }

// One round, one outcome, one explicit choice. Busy native dialogue/focus loss
// can delay presentation without losing the result or accumulating a backlog.
internal sealed class OldMaidRoundEndFlow
{
    internal const float ReadingSeconds = 3.2f;
    private float _elapsed;
    private float _retryAfter;
    internal OldMaidRoundEndStage Stage { get; private set; }
    internal OldMaidWinner Winner { get; private set; }

    internal void Observe(OldMaidWinner winner)
    {
        if (Stage != OldMaidRoundEndStage.Idle ||
            (winner != OldMaidWinner.Player && winner != OldMaidWinner.Opponent)) return;
        Winner = winner;
        Stage = OldMaidRoundEndStage.CuePending;
    }

    internal void CueShown()
    {
        if (Stage != OldMaidRoundEndStage.CuePending) return;
        _elapsed = 0;
        Stage = OldMaidRoundEndStage.Reading;
    }

    internal void Step(float delta)
    {
        if (!OldMaidCardGeometry.PositiveFinite(delta)) return;
        _retryAfter = Math.Max(0, _retryAfter - Math.Min(delta, 0.05f));
        if (Stage != OldMaidRoundEndStage.Reading) return;
        _elapsed = Math.Min(ReadingSeconds, _elapsed + Math.Min(delta, 0.05f));
        if (_elapsed >= ReadingSeconds) Stage = OldMaidRoundEndStage.ChoicePending;
    }

    internal bool TryBeginUiAttempt()
    {
        if (_retryAfter > 0 || (Stage != OldMaidRoundEndStage.CuePending && Stage != OldMaidRoundEndStage.ChoicePending))
            return false;
        _retryAfter = 0.5f;
        return true;
    }

    internal void ChoicesShown()
    {
        if (Stage == OldMaidRoundEndStage.ChoicePending) Stage = OldMaidRoundEndStage.ChoiceVisible;
    }

    internal bool TryChoose()
    {
        if (Stage != OldMaidRoundEndStage.ChoiceVisible) return false;
        Stage = OldMaidRoundEndStage.Chosen;
        return true;
    }

    internal void RetryChoices()
    {
        if (Stage is OldMaidRoundEndStage.ChoiceVisible or OldMaidRoundEndStage.Chosen)
        {
            Stage = OldMaidRoundEndStage.ChoicePending;
            _retryAfter = 0.5f;
        }
    }

    internal void Reset()
    {
        Stage = OldMaidRoundEndStage.Idle;
        Winner = OldMaidWinner.None;
        _elapsed = _retryAfter = 0;
    }
}
