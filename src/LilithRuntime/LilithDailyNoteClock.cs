namespace LilithRuntime;

internal readonly record struct LilithDailyNoteClockReading(DateTimeOffset UtcNow, bool Trusted, string Reason);

// Accepted policy: each cold Runtime start uses this user's OS calendar, not
// an invented offline time attestation. Warm wall-clock jumps remain closed.
internal sealed class LilithDailyNoteClock
{
    private readonly TimeProvider _time;
    private readonly Func<TimeZoneInfo> _currentZone;
    private readonly string _zoneFingerprint;
    private readonly DateTimeOffset _anchorUtc;
    private readonly long _anchorTimestamp;
    private bool _faulted;
    internal TimeZoneInfo TimeZone { get; }

    internal LilithDailyNoteClock(TimeProvider? time = null, Func<TimeZoneInfo>? currentZone = null)
    {
        _time = time ?? TimeProvider.System;
        _currentZone = currentZone ?? (() => TimeZoneInfo.Local);
        TimeZone = _currentZone();
        _zoneFingerprint = LilithDailyNoteGoal.Fingerprint(TimeZone);
        _anchorUtc = _time.GetUtcNow();
        _anchorTimestamp = _time.GetTimestamp();
    }

    internal LilithDailyNoteClockReading Read(DateTimeOffset highWater)
    {
        var now = _time.GetUtcNow();
        try
        {
            var elapsed = _time.GetElapsedTime(_anchorTimestamp, _time.GetTimestamp());
            if (elapsed < TimeSpan.Zero || now.Offset != TimeSpan.Zero
                || now <= DateTimeOffset.MinValue.AddDays(1) || now >= DateTimeOffset.MaxValue.AddDays(-1)
                || Math.Abs((now - _anchorUtc - elapsed).TotalSeconds) > 2
                || _currentZone().Id != TimeZone.Id || LilithDailyNoteGoal.Fingerprint(_currentZone()) != _zoneFingerprint)
                _faulted = true;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        { _faulted = true; }
        if (_faulted) return new(now, false, "clock_changed");
        return now < highWater ? new(now, false, "clock_rollback") : new(now, true, "os_calendar");
    }

    internal DateTimeOffset DeliveryDeadline(DateTimeOffset now)
    {
        var localDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZone).DateTime);
        var nextDay = localDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A rare midnight DST gap advances to its first valid instant; an
        // ambiguous midnight chooses the earlier UTC boundary conservatively.
        for (var minute = 0; minute < 1440 && TimeZone.IsInvalidTime(nextDay); minute++) nextDay = nextDay.AddMinutes(1);
        if (TimeZone.IsInvalidTime(nextDay)) throw new InvalidOperationException("Daily calendar boundary unavailable.");
        var midnightUtc = TimeZone.IsAmbiguousTime(nextDay)
            ? new DateTimeOffset(nextDay, TimeZone.GetAmbiguousTimeOffsets(nextDay).Max()).ToUniversalTime()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextDay, TimeZone), TimeSpan.Zero);
        return now.AddSeconds(60) < midnightUtc ? now.AddSeconds(60) : midnightUtc;
    }
}
