using System;

namespace LilithTextInjector;

internal enum SilentVoiceWarmupDecision { Wait, Start, Complete, Abort }

// Pure and content-free; the adapter alone owns the AudioClip and Unity effects.
internal sealed class SilentVoiceWarmupPolicy
{
    private readonly long _created;
    private long? _idleSince;
    private long? _started;
    private long _duration;
    private bool _observedPlaying;
    private bool _terminal;

    internal SilentVoiceWarmupPolicy(long now) { _created = now; }

    internal SilentVoiceWarmupDecision Tick(long now, bool cancelled,
        bool playerDemand, bool sourceAvailable, bool sourcePlaying, bool ownsClip)
    {
        if (_terminal) return SilentVoiceWarmupDecision.Abort;
        if (cancelled || playerDemand || now < _created)
            return End(SilentVoiceWarmupDecision.Abort);
        if (_started is long start)
        {
            if (!sourceAvailable || !ownsClip || now < start || now - start > _duration + 2000)
                return End(SilentVoiceWarmupDecision.Abort);
            _observedPlaying |= sourcePlaying;
            if (sourcePlaying) return SilentVoiceWarmupDecision.Wait;
            if (_observedPlaying && now - start >= Math.Max(1, _duration - 80))
                return End(SilentVoiceWarmupDecision.Complete);
            if (now - start < 150) return SilentVoiceWarmupDecision.Wait;
            return End(SilentVoiceWarmupDecision.Abort);
        }
        if (now - _created >= 15000) return End(SilentVoiceWarmupDecision.Abort);
        if (!sourceAvailable || sourcePlaying)
        {
            _idleSince = null;
            return SilentVoiceWarmupDecision.Wait;
        }
        _idleSince ??= now;
        return now - _idleSince.Value >= 750
            ? SilentVoiceWarmupDecision.Start : SilentVoiceWarmupDecision.Wait;
    }

    internal void Started(long now, long durationMilliseconds)
    {
        if (_terminal || _started.HasValue || durationMilliseconds <= 0 || durationMilliseconds > 10000)
            throw new InvalidOperationException("Invalid silent playback start.");
        _started = now;
        _duration = durationMilliseconds;
    }

    private SilentVoiceWarmupDecision End(SilentVoiceWarmupDecision decision)
    {
        _terminal = true;
        return decision;
    }

    internal static byte[] CreateSilentWav(byte[] original)
    {
        var descriptor = D043BoundedWavAdmission.TryAdmit(original, D043WavAdmissionCaps.V1).Descriptor;
        if (descriptor == null || descriptor.DurationMicroseconds > 5_000_000)
            throw new ArgumentException("A valid short PCM16 or float32 WAV is required.");
        var silent = (byte[])original.Clone();
        // Zero bytes encode silence for both admitted PCM16 and IEEE float32.
        Array.Clear(silent, descriptor.DataOffset, descriptor.DataLength);
        return silent;
    }
}
