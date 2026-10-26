using System;
using System.IO;
using System.Security.Cryptography;

namespace LilithTextInjector;

internal enum OldMaidSound { Draw, Throw }

// One embedded, licensed CC0 draw sample and one original synthesized throw.
// Decoded once per session. No runtime download/file path or game/deck RNG.
internal static class OldMaidCardSound
{
    internal const int SampleRate = 24000;

    internal static float[] Create(OldMaidSound kind)
    {
        if (kind != OldMaidSound.Draw && kind != OldMaidSound.Throw)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == OldMaidSound.Draw) return ReadDrawSample();
        var seconds = kind == OldMaidSound.Draw ? 0.14f : 0.21f;
        var samples = new float[(int)(SampleRate * seconds)];
        uint noise = kind == OldMaidSound.Draw ? 0x74293u : 0x31957u;
        var low = 0f;
        var previous = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            noise ^= noise << 13;
            noise ^= noise >> 17;
            noise ^= noise << 5;
            var white = (noise & 65535) / 32767.5f - 1f;
            low += (white - low) * (kind == OldMaidSound.Draw ? 0.22f : 0.14f);
            var paper = low - previous * 0.65f;
            previous = low;
            var t = i / (float)(samples.Length - 1);
            var envelope = MathF.Pow(MathF.Sin(t * MathF.PI), 1.6f);
            var flutter = 0.7f + 0.3f * MathF.Sin(t * MathF.PI * (kind == OldMaidSound.Draw ? 13 : 9));
            var flick = MathF.Sin(2 * MathF.PI * (kind == OldMaidSound.Draw ? 1350 : 850) * i / SampleRate)
                * MathF.Exp(-t * 32) * 0.12f;
            samples[i] = Math.Clamp((paper * flutter + flick) * envelope * 0.7f, -0.5f, 0.5f);
        }
        samples[0] = samples[samples.Length - 1] = 0;
        return samples;
    }

    // Remove the previous extra 0.55 attenuation without bypassing native SE
    // or listener/mixer mute. A malformed volume fails silent.
    internal static float EffectsVolume(float nativeVolume) =>
        float.IsFinite(nativeVolume) ? Math.Clamp(nativeVolume, 0, 1) : 0;

    private static float[] ReadDrawSample()
    {
        using var stream = typeof(OldMaidCardSound).Assembly.GetManifestResourceStream(
            "LilithTextInjector.Audio.card-slide-1.pcm")
            ?? throw new InvalidDataException("Embedded card sound missing.");
        if (stream.Length != 9600) throw new InvalidDataException("Unexpected card sound size.");
        var bytes = new byte[9600];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
        if (Convert.ToHexString(SHA256.HashData(bytes)) !=
            "A0C2F28D781C7E73887B1A5F285CFC3BE1738397F245725DCED74785A54C82E4")
            throw new InvalidDataException("Card sound identity mismatch.");
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = unchecked((short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8))) / 32768f;
        samples[0] = samples[samples.Length - 1] = 0;
        return samples;
    }
}

// Both cards of one pair share one throw sound. Observing a silent/background
// frame consumes it too: focus restoration never replays a stale sound.
internal sealed class OldMaidThrowSoundGate
{
    private readonly int[] _pairByCard = new int[OldMaidGame.DeckSize];
    private readonly bool[] _heard = new bool[OldMaidGame.PairCount];
    internal OldMaidThrowSoundGate() => Reset();

    internal void Register(int cardId, int pairOrdinal)
    {
        if (cardId < 0 || cardId >= _pairByCard.Length || pairOrdinal < 0 || pairOrdinal >= _heard.Length)
            throw new ArgumentOutOfRangeException(nameof(cardId));
        _pairByCard[cardId] = pairOrdinal;
    }

    internal bool Observe(int cardId, OldMaidSpatialPhase phase, bool audible)
    {
        if (cardId < 0 || cardId >= _pairByCard.Length || phase == OldMaidSpatialPhase.Waiting) return false;
        var pair = _pairByCard[cardId];
        if (pair < 0 || _heard[pair]) return false;
        _heard[pair] = true;
        return audible && phase == OldMaidSpatialPhase.Airborne;
    }

    internal void Reset()
    {
        Array.Fill(_pairByCard, -1);
        Array.Clear(_heard, 0, _heard.Length);
    }
}
