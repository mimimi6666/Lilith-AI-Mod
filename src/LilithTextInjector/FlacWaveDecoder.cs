using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NAudio.Wave;

namespace LilithTextInjector;

// Local music decoding only. This class never plays audio, contacts Runtime, or
// replaces a source file. Its entire lifetime belongs to the caller's MTA worker.
internal static class FlacWaveDecoder
{
    internal const long MaximumDecodedBytes = 512L * 1024 * 1024;
    private const int BufferBytes = 64 * 1024;

    internal static string ValidateSourcePath(string path) => ValidateLocalPath(path, ".flac", true);

    internal static void Decode(string sourcePath, string destinationPath,
        FlacMetadata metadata, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("flac.windows_codec_required");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            throw new InvalidOperationException("flac.background_mta_required");
        string source = ValidateLocalPath(sourcePath, ".flac", true);
        string destination = ValidateLocalPath(destinationPath, ".wav", false);
        long expectedBytes = ValidateMetadata(metadata);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("flac.source_destination_conflict");

        // Keep the source immutable while metadata and the Windows decoder read
        // it. This also prevents a rename from replacing the verified input.
        using var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] expectedMd5 = ReadStreamInfo(sourceLock, metadata);
        bool createdOutput = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new MediaFoundationReader(source,
                new MediaFoundationReader.MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true,
                    SingleReaderObject = true
                });
            ValidateFormat(reader.WaveFormat, metadata);
            bool formatChanged = false;
            reader.WaveFormatChanged += (_, _) => formatChanged = true;
            cancellationToken.ThrowIfCancellationRequested();

            // Never use the path-taking WaveFileWriter constructor: it truncates
            // existing files. The caller owns only this newly created output.
            using var destinationStream = new FileStream(destination, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, BufferBytes, FileOptions.SequentialScan);
            createdOutput = true;
            using var writer = new WaveFileWriter(destinationStream, reader.WaveFormat);
            using var pcmMd5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            byte[] decoded = new byte[BufferBytes];
            byte[] originalPcm = new byte[BufferBytes];
            long written = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = reader.Read(decoded, 0, decoded.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (formatChanged)
                    throw new InvalidDataException("flac.format_changed");
                ValidateFormat(reader.WaveFormat, metadata);
                if (count == 0)
                    break;
                if (count < 0 || count > decoded.Length || count % (metadata.Channels * sizeof(float)) != 0
                    || written > expectedBytes - count)
                    throw new InvalidDataException("flac.decoded_length_mismatch");
                int pcmCount = RestorePcmBytes(decoded, count, originalPcm, metadata.BitsPerSample);
                pcmMd5.AppendData(originalPcm, 0, pcmCount);
                writer.Write(decoded, 0, count);
                written += count;
            }
            if (written != expectedBytes)
                throw new InvalidDataException("flac.decoded_length_mismatch");
            byte[] actualMd5 = pcmMd5.GetHashAndReset();
            // FLAC permits all-zero MD5 (unspecified). When present it checks
            // accidental corruption/loss, not authenticity or security trust.
            if (Array.Exists(expectedMd5, value => value != 0)
                && !CryptographicOperations.FixedTimeEquals(expectedMd5, actualMd5))
                throw new InvalidDataException("flac.pcm_checksum_mismatch");
            cancellationToken.ThrowIfCancellationRequested();
            writer.Flush();
            destinationStream.Flush(true);
        }
        catch (Exception failure)
        {
            if (createdOutput)
            {
                try { File.Delete(destination); }
                catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("flac.failed_output_cleanup", new AggregateException(failure, cleanupFailure));
                }
            }
            throw;
        }
    }

    private static long ValidateMetadata(FlacMetadata metadata)
    {
        if (metadata == null || metadata.SampleRate < 8000 || metadata.SampleRate > 192000
            || metadata.Channels < 1 || metadata.Channels > 2
            || metadata.BitsPerSample < 4 || metadata.BitsPerSample > 24 || metadata.TotalSamples <= 0)
            throw new InvalidDataException("flac.unsupported_stream");
        if (metadata.TotalSamples > MaximumDecodedBytes / (metadata.Channels * sizeof(float)))
            throw new InvalidDataException("flac.decoded_size_limit");
        return metadata.TotalSamples * metadata.Channels * sizeof(float);
    }

    private static string ValidateLocalPath(string path, string extension, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3
            || !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
            || path[1] != ':' || (path[2] != '\\' && path[2] != '/')
            || path.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(path)
            || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("flac.local_file_required");
        string full = Path.GetFullPath(path);
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram))
            throw new InvalidDataException("flac.local_drive_required");
        string? cursor = mustExist ? full : Path.GetDirectoryName(full);
        while (cursor != null)
        {
            if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("flac.reparse_path_not_supported");
            cursor = Path.GetDirectoryName(cursor);
        }
        if (mustExist && (File.GetAttributes(full) & FileAttributes.Directory) != 0)
            throw new InvalidDataException("flac.file_required");
        return full;
    }

    private static byte[] ReadStreamInfo(Stream source, FlacMetadata metadata)
    {
        byte[] header = new byte[42];
        int read = 0;
        while (read < header.Length)
        {
            int count = source.Read(header, read, header.Length - read);
            if (count == 0)
                throw new InvalidDataException("flac.truncated_streaminfo");
            read += count;
        }
        if (header[0] != 'f' || header[1] != 'L' || header[2] != 'a' || header[3] != 'C'
            || (header[4] & 127) != 0 || header[5] != 0 || header[6] != 0 || header[7] != 34)
            throw new InvalidDataException("flac.invalid_streaminfo");
        ulong packed = 0;
        for (int i = 18; i < 26; i++) packed = (packed << 8) | header[i];
        if ((int)(packed >> 44) != metadata.SampleRate
            || (int)((packed >> 41) & 7) + 1 != metadata.Channels
            || (int)((packed >> 36) & 31) + 1 != metadata.BitsPerSample
            || (long)(packed & 0xfffffffffUL) != metadata.TotalSamples)
            throw new InvalidDataException("flac.streaminfo_changed");
        byte[] md5 = new byte[16];
        Buffer.BlockCopy(header, 26, md5, 0, md5.Length);
        return md5;
    }

    private static void ValidateFormat(WaveFormat format, FlacMetadata metadata)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32
            || format.SampleRate != metadata.SampleRate || format.Channels != metadata.Channels
            || format.BlockAlign != metadata.Channels * sizeof(float))
            throw new InvalidDataException("flac.lossless_float_output_unavailable");
    }

    private static int RestorePcmBytes(byte[] floats, int count, byte[] pcm, int bitsPerSample)
    {
        int bytesPerSample = (bitsPerSample + 7) / 8;
        int scale = 1 << (bitsPerSample - 1);
        int output = 0;
        for (int offset = 0; offset < count; offset += sizeof(float))
        {
            float value = BitConverter.ToSingle(floats, offset);
            double scaled = value * (double)scale;
            if (!float.IsFinite(value) || scaled < -scale || scaled >= scale || scaled != Math.Truncate(scaled))
                throw new InvalidDataException("flac.non_lossless_sample");
            int sample = (int)scaled;
            for (int i = 0; i < bytesPerSample; i++) pcm[output++] = (byte)(sample >> (8 * i));
        }
        return output;
    }
}
