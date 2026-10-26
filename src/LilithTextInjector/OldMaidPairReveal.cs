using System;

namespace LilithTextInjector;

internal enum OldMaidPairRevealStage { Idle, Converging, Holding, Ready, Consumed }

/// <summary>
/// Projects an already committed pair: bring both cards together, keep the exact
/// target poses visible for a full second, then offer one consumable throw cue.
/// It owns no hand, random choice, rules revision or native character state.
/// Pause by withholding Step; Clear synchronously on round/lifecycle replacement.
/// </summary>
internal sealed class OldMaidPairReveal
{
    internal const float ConvergeSeconds = 0.25f;
    internal const float HoldSeconds = 1f;
    private const float MaximumDelta = 0.1f;
    private double _elapsed;
    private OldMaidCardPose _firstFrom;
    private OldMaidCardPose _secondFrom;
    private OldMaidCardPose _firstTo;
    private OldMaidCardPose _secondTo;

    internal OldMaidPairRevealStage Stage { get; private set; }
    internal int FirstCardId { get; private set; } = -1;
    internal int SecondCardId { get; private set; } = -1;
    internal OldMaidCardPose FirstPose { get; private set; }
    internal OldMaidCardPose SecondPose { get; private set; }
    internal bool Ready => Stage == OldMaidPairRevealStage.Ready;
    internal bool IsBusy => Stage == OldMaidPairRevealStage.Converging ||
        Stage == OldMaidPairRevealStage.Holding || Stage == OldMaidPairRevealStage.Ready;

    internal bool TryBegin(int firstCardId, int secondCardId,
        OldMaidCardPose firstFrom, OldMaidCardPose secondFrom,
        OldMaidCardPose firstTo, OldMaidCardPose secondTo)
    {
        if (Stage != OldMaidPairRevealStage.Idle ||
            firstCardId < 0 || firstCardId >= OldMaidGame.DeckSize ||
            secondCardId < 0 || secondCardId >= OldMaidGame.DeckSize || firstCardId == secondCardId ||
            !ValidPose(firstFrom) || !ValidPose(secondFrom) || !ValidPose(firstTo) || !ValidPose(secondTo))
            return false;

        FirstCardId = firstCardId;
        SecondCardId = secondCardId;
        FirstPose = _firstFrom = firstFrom;
        SecondPose = _secondFrom = secondFrom;
        _firstTo = firstTo;
        _secondTo = secondTo;
        _elapsed = 0d;
        Stage = OldMaidPairRevealStage.Converging;
        return true;
    }

    internal void Step(float deltaSeconds)
    {
        if (!OldMaidCardGeometry.PositiveFinite(deltaSeconds)) return;
        var delta = Math.Min(deltaSeconds, MaximumDelta);
        if (Stage == OldMaidPairRevealStage.Converging)
        {
            _elapsed = Math.Min(ConvergeSeconds, _elapsed + delta);
            var progress = (float)(_elapsed / ConvergeSeconds);
            var eased = progress * progress * (3f - 2f * progress);
            FirstPose = Interpolate(_firstFrom, _firstTo, eased);
            SecondPose = Interpolate(_secondFrom, _secondTo, eased);
            if (_elapsed >= ConvergeSeconds)
            {
                FirstPose = _firstTo;
                SecondPose = _secondTo;
                // Do not credit the convergence frame's remainder as visible
                // hold time: the exact side-by-side poses must render first.
                _elapsed = 0d;
                Stage = OldMaidPairRevealStage.Holding;
            }
        }
        else if (Stage == OldMaidPairRevealStage.Holding)
        {
            _elapsed = Math.Min(HoldSeconds, _elapsed + delta);
            if (_elapsed >= HoldSeconds) Stage = OldMaidPairRevealStage.Ready;
        }
    }

    internal bool TryConsumeReady()
    {
        if (!Ready) return false;
        Stage = OldMaidPairRevealStage.Consumed;
        return true;
    }

    internal void Clear()
    {
        Stage = OldMaidPairRevealStage.Idle;
        FirstCardId = SecondCardId = -1;
        FirstPose = SecondPose = _firstFrom = _secondFrom = _firstTo = _secondTo = default;
        _elapsed = 0d;
    }

    private static OldMaidCardPose Interpolate(OldMaidCardPose from, OldMaidCardPose to, float progress)
    {
        var angleDelta = (to.Angle - from.Angle) % 360f;
        if (angleDelta > 180f) angleDelta -= 360f;
        else if (angleDelta < -180f) angleDelta += 360f;
        return new OldMaidCardPose(Lerp(from.X, to.X, progress), Lerp(from.Y, to.Y, progress),
            from.Angle + angleDelta * progress,
            Lerp(from.Width, to.Width, progress), Lerp(from.Height, to.Height, progress));
    }

    private static bool ValidPose(OldMaidCardPose pose) =>
        OldMaidCardGeometry.IsFinite(pose.X) && Math.Abs(pose.X) <= 65536f &&
        OldMaidCardGeometry.IsFinite(pose.Y) && Math.Abs(pose.Y) <= 65536f &&
        OldMaidCardGeometry.IsFinite(pose.Angle) && Math.Abs(pose.Angle) <= 360000f &&
        OldMaidCardGeometry.PositiveFinite(pose.Width) && pose.Width <= 4096f &&
        OldMaidCardGeometry.PositiveFinite(pose.Height) && pose.Height <= 4096f;

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
}
