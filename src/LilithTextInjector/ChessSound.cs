using System;
using System.IO;
using System.Security.Cryptography;

namespace LilithTextInjector;

internal enum ChessSound { Move, Capture }

// Licensed Kenney CC0 samples, embedded offline. Identity and byte limits are
// checked before conversion; the game never downloads audio or reads a path.
internal static class ChessSoundSamples
{
    internal const int SampleRate = 24000;
    internal static float[] Read(ChessSound kind)
    {
        var (name, size, hash) = kind switch
        {
            ChessSound.Move => ("chess-move.pcm", 7560, "8BC40125AA303D37EDAA71DBF00324DFECB849B73C03A5928477EE8395A2A540"),
            ChessSound.Capture => ("chess-capture.pcm", 11940, "82CAF53A5134D7C3DE2D39FD1320EB838CC8FF114CAF6A783FFDEE6C781BE534"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var stream = typeof(ChessSoundSamples).Assembly.GetManifestResourceStream("LilithTextInjector.Audio." + name)
            ?? throw new InvalidDataException("Chess sound missing.");
        if (stream.Length != size) throw new InvalidDataException("Chess sound size mismatch.");
        var bytes = new byte[size]; var offset = 0;
        while (offset < size)
        {
            var read = stream.Read(bytes, offset, size - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        if (Convert.ToHexString(SHA256.HashData(bytes)) != hash) throw new InvalidDataException("Chess sound identity mismatch.");
        var samples = new float[size / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = unchecked((short)(bytes[i * 2] | bytes[i * 2 + 1] << 8)) / 32768f;
        samples[0] = samples[samples.Length - 1] = 0;
        return samples;
    }
}
