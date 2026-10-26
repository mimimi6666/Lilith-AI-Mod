using System;

namespace LilithTextInjector;

internal enum OldMaidDrawStage { Idle, Selecting, SelectionReady, Flying, Arrived }

/// <summary>
/// Decorative selection and transfer only. The caller owns the rules revision,
/// commits once, and then projects that result; this clock never moves a card in
/// either hand. Pause by not stepping; Clear on round/lifecycle replacement.
/// </summary>
internal sealed class OldMaidDrawAnimation
{
    internal const float SelectionSeconds = 2.2f;
    internal const float FlightSeconds = 0.65f;
    private const float MaximumDelta = 0.1f;
    private float _elapsed;
    private int _count;
    private int _targetSlot;
    private OldMaidCardPose _from;
    private OldMaidCardPose _to;
    private bool _fromFaceUp;
    private bool _toFaceUp;

    internal OldMaidDrawStage Stage { get; private set; }
    internal int TargetSlot => Stage == OldMaidDrawStage.Selecting || Stage == OldMaidDrawStage.SelectionReady ? _targetSlot : -1;
    internal bool IsBusy => Stage == OldMaidDrawStage.Selecting || Stage == OldMaidDrawStage.SelectionReady || Stage == OldMaidDrawStage.Flying;
    internal int HighlightedSlot { get; private set; } = -1;
    internal OldMaidCardPose FlightPose { get; private set; }
    internal bool FaceUp { get; private set; }
    internal float FlipScaleX { get; private set; } = 1f;
    internal float Progress { get; private set; }

    internal bool TryBeginSelection(int count, int targetSlot)
    {
        if (Stage != OldMaidDrawStage.Idle || count < 1 || count > OldMaidGame.DeckSize ||
            targetSlot < 0 || targetSlot >= count)
            return false;
        _count = count;
        _targetSlot = targetSlot;
        _elapsed = Progress = 0f;
        HighlightedSlot = 0;
        Stage = OldMaidDrawStage.Selecting;
        return true;
    }

    internal bool TryBeginFlight(OldMaidCardPose from, OldMaidCardPose to, bool fromFaceUp, bool toFaceUp)
    {
        if ((Stage != OldMaidDrawStage.Idle && Stage != OldMaidDrawStage.SelectionReady) ||
            !ValidPose(from) || !ValidPose(to))
            return false;
        _from = from;
        _to = to;
        _fromFaceUp = fromFaceUp;
        _toFaceUp = toFaceUp;
        _elapsed = Progress = 0f;
        HighlightedSlot = -1;
        FlightPose = from;
        FaceUp = fromFaceUp;
        FlipScaleX = 1f;
        Stage = OldMaidDrawStage.Flying;
        return true;
    }

    internal void Step(float deltaSeconds)
    {
        if (!OldMaidCardGeometry.PositiveFinite(deltaSeconds)) return;
        var delta = Math.Min(deltaSeconds, MaximumDelta);
        if (Stage == OldMaidDrawStage.Selecting)
        {
            _elapsed = Math.Min(SelectionSeconds, _elapsed + delta);
            Progress = _elapsed / SelectionSeconds;
            HighlightedSlot = SelectionSlot(Progress);
            if (_elapsed >= SelectionSeconds)
            {
                HighlightedSlot = _targetSlot;
                Stage = OldMaidDrawStage.SelectionReady;
            }
        }
        else if (Stage == OldMaidDrawStage.Flying)
        {
            _elapsed = Math.Min(FlightSeconds, _elapsed + delta);
            Progress = _elapsed / FlightSeconds;
            EvaluateFlight(Progress);
            if (_elapsed >= FlightSeconds)
            {
                FlightPose = _to;
                FaceUp = _toFaceUp;
                FlipScaleX = 1f;
                Stage = OldMaidDrawStage.Arrived;
            }
        }
    }

    internal void Clear()
    {
        Stage = OldMaidDrawStage.Idle;
        HighlightedSlot = -1;
        FlightPose = _from = _to = default;
        FaceUp = _fromFaceUp = _toFaceUp = false;
        FlipScaleX = 1f;
        _elapsed = Progress = 0f;
        _count = 0;
        _targetSlot = -1;
    }

    private int SelectionSlot(float progress)
    {
        // A left/right hesitation over slot positions, never card identities.
        // The final stop is the one fair choice reserved by the rules engine.
        var last = _count - 1;
        float position;
        if (progress < 0.28f) position = Lerp(0f, last, progress / 0.28f);
        else if (progress < 0.58f) position = Lerp(last, 0f, (progress - 0.28f) / 0.30f);
        else if (progress < 0.82f) position = Lerp(0f, last, (progress - 0.58f) / 0.24f);
        else position = Lerp(last, _targetSlot, Math.Min(1f, (progress - 0.82f) / 0.14f));
        return Math.Max(0, Math.Min(last, (int)Math.Floor(position + 0.5f)));
    }

    private void EvaluateFlight(float progress)
    {
        var t = progress * progress * (3f - 2f * progress);
        var inverse = 1f - t;
        var dx = _to.X - _from.X;
        var dy = _to.Y - _from.Y;
        var distance = (float)Math.Sqrt(dx * dx + dy * dy);
        var arc = Math.Min(180f, 40f + distance * 0.22f);
        var controlX = (_from.X + _to.X) * 0.5f;
        var controlY = Math.Min(65536f, Math.Max(_from.Y, _to.Y) + arc);
        var angleDelta = (_to.Angle - _from.Angle) % 360f;
        if (angleDelta > 180f) angleDelta -= 360f;
        else if (angleDelta < -180f) angleDelta += 360f;
        FlightPose = new OldMaidCardPose(
            inverse * inverse * _from.X + 2f * inverse * t * controlX + t * t * _to.X,
            inverse * inverse * _from.Y + 2f * inverse * t * controlY + t * t * _to.Y,
            _from.Angle + angleDelta * t,
            Lerp(_from.Width, _to.Width, t), Lerp(_from.Height, _to.Height, t));
        FaceUp = progress < 0.5f ? _fromFaceUp : _toFaceUp;
        FlipScaleX = _fromFaceUp == _toFaceUp ? 1f : Math.Max(0.04f, Math.Abs((float)Math.Cos(progress * Math.PI)));
    }

    private static bool ValidPose(OldMaidCardPose pose) =>
        OldMaidCardGeometry.IsFinite(pose.X) && Math.Abs(pose.X) <= 65536f &&
        OldMaidCardGeometry.IsFinite(pose.Y) && Math.Abs(pose.Y) <= 65536f &&
        OldMaidCardGeometry.IsFinite(pose.Angle) && Math.Abs(pose.Angle) <= 360000f &&
        OldMaidCardGeometry.PositiveFinite(pose.Width) && pose.Width <= 4096f &&
        OldMaidCardGeometry.PositiveFinite(pose.Height) && pose.Height <= 4096f;

    private static float Lerp(float from, float to, float progress) => from + (to - from) * progress;
}
