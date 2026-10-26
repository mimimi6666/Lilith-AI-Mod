using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace LilithTextInjector;

internal enum D043WavAdmissionReason
{
    None = 0,
    Empty = 1,
    EncodedSizeOutOfRange = 2,
    MissingRiffWaveHeader = 3,
    RiffSizeMismatch = 4,
    TooManyChunks = 5,
    TruncatedChunkHeader = 6,
    ChunkLengthOverflow = 7,
    TruncatedChunk = 8,
    UnexpectedChunk = 9,
    DuplicateFormatChunk = 10,
    DuplicateDataChunk = 11,
    RequiredChunkOrderInvalid = 12,
    MissingFormatChunk = 13,
    MissingDataChunk = 14,
    FormatChunkSizeInvalid = 15,
    EncodingUnsupported = 16,
    ChannelCountOutOfRange = 17,
    SampleRateOutOfRange = 18,
    BitDepthUnsupported = 19,
    BlockAlignmentInvalid = 20,
    ByteRateInvalid = 21,
    DataLengthInvalid = 22,
    FrameCountOutOfRange = 23,
    DurationOutOfRange = 24,
    DecodedSampleCountOutOfRange = 25,
    NonFiniteFloatSample = 26,
    TrailingBytes = 27,
    AdmissionCapsOutOfRange = 28
}

internal readonly struct D043WavAdmissionCaps
{
    private const int V1MinimumEncodedBytes = 44;
    private const int V1MaximumEncodedBytes = 8 * 1024 * 1024;
    private const int V1MaximumChunks = 64;
    private const int V1MinimumChannels = 1;
    private const int V1MaximumChannels = 2;
    private const int V1MinimumSampleRate = 16_000;
    private const int V1MaximumSampleRate = 48_000;
    private const long V1MinimumDurationMicroseconds = 50_000;
    private const long V1MaximumDurationMicroseconds = 20_000_000;
    private const long V1MaximumFrameCount = 960_000;
    private const long V1MaximumDecodedSamples = 2_000_000;
    internal static D043WavAdmissionCaps V1 { get; } = new(
        minimumEncodedBytes: V1MinimumEncodedBytes,
        maximumEncodedBytes: V1MaximumEncodedBytes,
        maximumChunks: V1MaximumChunks,
        minimumChannels: V1MinimumChannels,
        maximumChannels: V1MaximumChannels,
        minimumSampleRate: V1MinimumSampleRate,
        maximumSampleRate: V1MaximumSampleRate,
        minimumDurationMicroseconds: V1MinimumDurationMicroseconds,
        maximumDurationMicroseconds: V1MaximumDurationMicroseconds,
        maximumFrameCount: V1MaximumFrameCount,
        maximumDecodedSamples: V1MaximumDecodedSamples);

    internal int MinimumEncodedBytes { get; }
    internal int MaximumEncodedBytes { get; }
    internal int MaximumChunks { get; }
    internal int MinimumChannels { get; }
    internal int MaximumChannels { get; }
    internal int MinimumSampleRate { get; }
    internal int MaximumSampleRate { get; }
    internal long MinimumDurationMicroseconds { get; }
    internal long MaximumDurationMicroseconds { get; }
    internal long MaximumFrameCount { get; }
    internal long MaximumDecodedSamples { get; }
    internal bool IsWithinV1Policy => MinimumEncodedBytes >= V1MinimumEncodedBytes
        && MaximumEncodedBytes >= MinimumEncodedBytes
        && MaximumEncodedBytes <= V1MaximumEncodedBytes
        && MaximumChunks is >= 2 and <= V1MaximumChunks
        && MinimumChannels >= V1MinimumChannels
        && MaximumChannels >= MinimumChannels
        && MaximumChannels <= V1MaximumChannels
        && MinimumSampleRate >= V1MinimumSampleRate
        && MaximumSampleRate >= MinimumSampleRate
        && MaximumSampleRate <= V1MaximumSampleRate
        && MinimumDurationMicroseconds >= V1MinimumDurationMicroseconds
        && MaximumDurationMicroseconds >= MinimumDurationMicroseconds
        && MaximumDurationMicroseconds <= V1MaximumDurationMicroseconds
        && MaximumFrameCount is >= 1 and <= V1MaximumFrameCount
        && MaximumDecodedSamples is >= 1 and <= V1MaximumDecodedSamples;

    internal D043WavAdmissionCaps(
        int minimumEncodedBytes,
        int maximumEncodedBytes,
        int maximumChunks,
        int minimumChannels,
        int maximumChannels,
        int minimumSampleRate,
        int maximumSampleRate,
        long minimumDurationMicroseconds,
        long maximumDurationMicroseconds,
        long maximumFrameCount,
        long maximumDecodedSamples)
    {
        if (minimumEncodedBytes < V1MinimumEncodedBytes
            || maximumEncodedBytes < minimumEncodedBytes
            || maximumEncodedBytes > V1MaximumEncodedBytes
            || maximumChunks < 2
            || maximumChunks > V1MaximumChunks
            || minimumChannels < V1MinimumChannels
            || maximumChannels < minimumChannels
            || maximumChannels > V1MaximumChannels
            || minimumSampleRate < V1MinimumSampleRate
            || maximumSampleRate < minimumSampleRate
            || maximumSampleRate > V1MaximumSampleRate
            || minimumDurationMicroseconds < V1MinimumDurationMicroseconds
            || maximumDurationMicroseconds < minimumDurationMicroseconds
            || maximumDurationMicroseconds > V1MaximumDurationMicroseconds
            || maximumFrameCount < 1
            || maximumFrameCount > V1MaximumFrameCount
            || maximumDecodedSamples < 1
            || maximumDecodedSamples > V1MaximumDecodedSamples)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
        }

        MinimumEncodedBytes = minimumEncodedBytes;
        MaximumEncodedBytes = maximumEncodedBytes;
        MaximumChunks = maximumChunks;
        MinimumChannels = minimumChannels;
        MaximumChannels = maximumChannels;
        MinimumSampleRate = minimumSampleRate;
        MaximumSampleRate = maximumSampleRate;
        MinimumDurationMicroseconds = minimumDurationMicroseconds;
        MaximumDurationMicroseconds = maximumDurationMicroseconds;
        MaximumFrameCount = maximumFrameCount;
        MaximumDecodedSamples = maximumDecodedSamples;
    }
}

internal sealed class D043AdmittedWavDescriptor
{
    private readonly object _admissionWitness;
    internal string EncodedSha256 { get; }
    internal int EncodedBytes { get; }
    internal ushort Encoding { get; }
    internal ushort Channels { get; }
    internal int SampleRate { get; }
    internal ushort BitsPerSample { get; }
    internal int DataOffset { get; }
    internal int DataLength { get; }
    internal long FrameCount { get; }
    internal long DurationMicroseconds { get; }
    internal long DecodedSampleCount { get; }
    internal bool IsParserSealed
        => D043BoundedWavAdmission.HasExactAdmissionWitness(_admissionWitness);

    internal D043AdmittedWavDescriptor(
        string encodedSha256,
        int encodedBytes,
        ushort encoding,
        ushort channels,
        int sampleRate,
        ushort bitsPerSample,
        int dataOffset,
        int dataLength,
        long frameCount,
        long durationMicroseconds,
        long decodedSampleCount,
        object admissionWitness)
    {
        if (!D043BoundedWavAdmission.HasExactAdmissionWitness(admissionWitness))
        {
            throw new ArgumentException(
                "Only the exact bounded WAV parser may issue this descriptor.",
                nameof(admissionWitness));
        }
        _admissionWitness = admissionWitness;
        EncodedSha256 = encodedSha256;
        EncodedBytes = encodedBytes;
        Encoding = encoding;
        Channels = channels;
        SampleRate = sampleRate;
        BitsPerSample = bitsPerSample;
        DataOffset = dataOffset;
        DataLength = dataLength;
        FrameCount = frameCount;
        DurationMicroseconds = durationMicroseconds;
        DecodedSampleCount = decodedSampleCount;
    }

    public override string ToString()
        => "D043AdmittedWavDescriptor(bytes="
            + EncodedBytes
            + ",channels="
            + Channels
            + ",rate="
            + SampleRate
            + ",frames="
            + FrameCount
            + ")";
}

internal sealed class D043WavAdmissionResult
{
    internal bool Admitted => Reason == D043WavAdmissionReason.None;
    internal D043WavAdmissionReason Reason { get; }
    internal D043AdmittedWavDescriptor? Descriptor { get; }

    private D043WavAdmissionResult(
        D043WavAdmissionReason reason,
        D043AdmittedWavDescriptor? descriptor)
    {
        Reason = reason;
        Descriptor = descriptor;
    }

    internal static D043WavAdmissionResult Accept(
        D043AdmittedWavDescriptor descriptor)
        => new(
            D043WavAdmissionReason.None,
            descriptor ?? throw new ArgumentNullException(nameof(descriptor)));

    internal static D043WavAdmissionResult Reject(D043WavAdmissionReason reason)
    {
        if (reason == D043WavAdmissionReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        return new D043WavAdmissionResult(reason, null);
    }
}

internal static class D043BoundedWavAdmission
{
    private const uint Riff = 0x46464952u;
    private const uint Wave = 0x45564157u;
    private const uint Format = 0x20746D66u;
    private const uint Data = 0x61746164u;
    private static readonly object AdmissionWitness = new();

    internal static bool HasExactAdmissionWitness(object? witness)
        => ReferenceEquals(witness, AdmissionWitness);

    internal static bool IsWithinV1PlaybackEnvelope(
        D043AdmittedWavDescriptor? descriptor)
    {
        if (descriptor is null || !descriptor.IsParserSealed)
            return false;

        var caps = D043WavAdmissionCaps.V1;
        if (descriptor.EncodedBytes < caps.MinimumEncodedBytes
            || descriptor.EncodedBytes > caps.MaximumEncodedBytes
            || descriptor.Channels < caps.MinimumChannels
            || descriptor.Channels > caps.MaximumChannels
            || descriptor.SampleRate < caps.MinimumSampleRate
            || descriptor.SampleRate > caps.MaximumSampleRate
            || descriptor.DurationMicroseconds < caps.MinimumDurationMicroseconds
            || descriptor.DurationMicroseconds > caps.MaximumDurationMicroseconds
            || descriptor.DecodedSampleCount < 1
            || descriptor.DecodedSampleCount > caps.MaximumDecodedSamples
            || descriptor.FrameCount < 1
            || descriptor.FrameCount > caps.MaximumFrameCount
            || descriptor.FrameCount > long.MaxValue / descriptor.Channels
            || descriptor.DecodedSampleCount
                != descriptor.FrameCount * descriptor.Channels
            || descriptor.DataOffset < 12
            || descriptor.DataLength < 1
            || descriptor.DataOffset > descriptor.EncodedBytes - descriptor.DataLength)
        {
            return false;
        }

        return (descriptor.Encoding == 1 && descriptor.BitsPerSample == 16)
               || (descriptor.Encoding == 3 && descriptor.BitsPerSample == 32);
    }

    internal static D043WavAdmissionResult TryAdmit(
        ReadOnlyMemory<byte> encoded,
        D043WavAdmissionCaps caps)
    {
        if (!caps.IsWithinV1Policy)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.AdmissionCapsOutOfRange);
        }
        if (encoded.IsEmpty)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.Empty);
        if (encoded.Length < 12
            || encoded.Length > caps.MaximumEncodedBytes)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.EncodedSizeOutOfRange);
        }

        var bytes = encoded.Span;
        if (ReadUInt32(bytes, 0) != Riff || ReadUInt32(bytes, 8) != Wave)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.MissingRiffWaveHeader);
        }

        var declaredRiffLength = ReadUInt32(bytes, 4);
        if (declaredRiffLength != (uint)(bytes.Length - 8))
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.RiffSizeMismatch);
        }

        ushort encoding = 0;
        ushort channels = 0;
        int sampleRate = 0;
        int byteRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        var formatSeen = false;
        var dataSeen = false;
        var dataOffset = -1;
        var dataLength = -1;
        var offset = 12;
        var chunks = 0;

        while (offset < bytes.Length)
        {
            chunks++;
            if (chunks > caps.MaximumChunks)
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.TooManyChunks);
            }
            if (bytes.Length - offset < 8)
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.TruncatedChunkHeader);
            }

            var kind = ReadUInt32(bytes, offset);
            var declaredLength = ReadUInt32(bytes, offset + 4);
            if (declaredLength > int.MaxValue)
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.ChunkLengthOverflow);
            }

            int body;
            int end;
            int paddedEnd;
            try
            {
                body = checked(offset + 8);
                end = checked(body + (int)declaredLength);
                paddedEnd = checked(end + ((int)declaredLength & 1));
            }
            catch (OverflowException)
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.ChunkLengthOverflow);
            }

            if (end > bytes.Length || paddedEnd > bytes.Length)
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.TruncatedChunk);
            }

            if (kind == Format)
            {
                if (formatSeen)
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.DuplicateFormatChunk);
                }
                if (dataSeen)
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.RequiredChunkOrderInvalid);
                }
                if (declaredLength != 16)
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.FormatChunkSizeInvalid);
                }

                formatSeen = true;
                encoding = ReadUInt16(bytes, body);
                channels = ReadUInt16(bytes, body + 2);
                sampleRate = ReadInt32(bytes, body + 4);
                byteRate = ReadInt32(bytes, body + 8);
                blockAlign = ReadUInt16(bytes, body + 12);
                bitsPerSample = ReadUInt16(bytes, body + 14);
            }
            else if (kind == Data)
            {
                if (dataSeen)
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.DuplicateDataChunk);
                }
                if (!formatSeen)
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.RequiredChunkOrderInvalid);
                }

                dataSeen = true;
                dataOffset = body;
                dataLength = (int)declaredLength;
            }
            else
            {
                return D043WavAdmissionResult.Reject(
                    D043WavAdmissionReason.UnexpectedChunk);
            }

            offset = paddedEnd;
        }

        if (offset != bytes.Length)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.TrailingBytes);
        if (!formatSeen)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.MissingFormatChunk);
        if (!dataSeen)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.MissingDataChunk);
        if (encoding is not 1 and not 3)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.EncodingUnsupported);
        if (channels < caps.MinimumChannels || channels > caps.MaximumChannels)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.ChannelCountOutOfRange);
        }
        if (sampleRate < caps.MinimumSampleRate || sampleRate > caps.MaximumSampleRate)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.SampleRateOutOfRange);
        }
        if ((encoding == 1 && bitsPerSample != 16)
            || (encoding == 3 && bitsPerSample != 32))
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.BitDepthUnsupported);
        }

        int expectedBlockAlign;
        int expectedByteRate;
        try
        {
            expectedBlockAlign = checked(channels * (bitsPerSample / 8));
            expectedByteRate = checked(sampleRate * expectedBlockAlign);
        }
        catch (OverflowException)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.ByteRateInvalid);
        }
        if (blockAlign != expectedBlockAlign)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.BlockAlignmentInvalid);
        if (byteRate != expectedByteRate)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.ByteRateInvalid);
        if (dataLength <= 0 || dataLength % blockAlign != 0)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.DataLengthInvalid);

        var frameCount = dataLength / blockAlign;
        if (frameCount <= 0 || frameCount > caps.MaximumFrameCount)
            return D043WavAdmissionResult.Reject(D043WavAdmissionReason.FrameCountOutOfRange);

        long durationMicroseconds;
        long decodedSamples;
        try
        {
            durationMicroseconds = checked((long)frameCount * 1_000_000L / sampleRate);
            decodedSamples = checked((long)frameCount * channels);
        }
        catch (OverflowException)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.DecodedSampleCountOutOfRange);
        }
        if (durationMicroseconds < caps.MinimumDurationMicroseconds
            || durationMicroseconds > caps.MaximumDurationMicroseconds)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.DurationOutOfRange);
        }
        if (decodedSamples <= 0 || decodedSamples > caps.MaximumDecodedSamples)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.DecodedSampleCountOutOfRange);
        }

        if (encoding == 3)
        {
            var data = bytes.Slice(dataOffset, dataLength);
            for (var index = 0; index < data.Length; index += 4)
            {
                var sample = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(data.Slice(index, 4)));
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                {
                    return D043WavAdmissionResult.Reject(
                        D043WavAdmissionReason.NonFiniteFloatSample);
                }
            }
        }

        if (encoded.Length < caps.MinimumEncodedBytes)
        {
            return D043WavAdmissionResult.Reject(
                D043WavAdmissionReason.EncodedSizeOutOfRange);
        }

        var digest = SHA256.HashData(bytes);
        return D043WavAdmissionResult.Accept(
            new D043AdmittedWavDescriptor(
                Convert.ToHexString(digest),
                bytes.Length,
                encoding,
                channels,
                sampleRate,
                bitsPerSample,
                dataOffset,
                dataLength,
                frameCount,
                durationMicroseconds,
                decodedSamples,
                AdmissionWitness));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
}
