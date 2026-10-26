using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LilithTextInjector;

internal enum CommunicationPatternLoadStatus
{
    Loaded = 0,
    Missing = 1,
    RecoveredPrevious = 2,
    Corrupt = 3
}

internal sealed class CommunicationPatternLoadResult
{
    internal CommunicationPatternState State { get; }
    internal CommunicationPatternLoadStatus Status { get; }

    internal CommunicationPatternLoadResult(
        CommunicationPatternState state,
        CommunicationPatternLoadStatus status)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Status = status;
    }
}

/// <summary>
/// Persists only bounded communication baselines and pattern scores. It never
/// receives or stores message text, prompts, replies, or audio.
/// </summary>
internal sealed class CommunicationPatternStore
{
    private const int EnvelopeFormatVersion = 1;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly string _previousPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    internal CommunicationPatternStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Communication-pattern path cannot be empty.", nameof(path));
        _path = System.IO.Path.GetFullPath(path);
        _previousPath = _path + ".previous";
    }

    internal string Path => _path;
    internal string PreviousPath => _previousPath;

    internal CommunicationPatternLoadResult LoadOrDefault(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (TryRead(_path, out var current))
                return new CommunicationPatternLoadResult(current!, CommunicationPatternLoadStatus.Loaded);
            if (TryRead(_previousPath, out var previous))
                return new CommunicationPatternLoadResult(previous!, CommunicationPatternLoadStatus.RecoveredPrevious);
            var status = File.Exists(_path) || File.Exists(_previousPath)
                ? CommunicationPatternLoadStatus.Corrupt
                : CommunicationPatternLoadStatus.Missing;
            return new CommunicationPatternLoadResult(
                CommunicationPatternState.CreateDefault(now),
                status);
        }
    }

    internal void Save(CommunicationPatternState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Communication-pattern path has no parent directory.");
            Directory.CreateDirectory(directory);
            var payload = JsonSerializer.SerializeToUtf8Bytes(StoredPatternState.FromDomain(state), JsonOptions);
            var envelope = new StoredEnvelope
            {
                FormatVersion = EnvelopeFormatVersion,
                PayloadBase64 = Convert.ToBase64String(payload),
                PayloadSha256 = ComputeSha256(payload)
            };
            var encoded = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
            var temporary = _path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(
                           temporary,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4_096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(encoded, 0, encoded.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temporary, _path, _previousPath, ignoreMetadataErrors: true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        ReplaceWithVerifiedFallback(temporary);
                    }
                }
                else
                {
                    File.Move(temporary, _path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private void ReplaceWithVerifiedFallback(string temporary)
    {
        File.Copy(_path, _previousPath, overwrite: true);
        if (!TryRead(_previousPath, out _))
            throw new InvalidDataException("Could not verify the previous communication-pattern snapshot.");
        File.Move(temporary, _path, overwrite: true);
    }

    private static bool TryRead(string path, out CommunicationPatternState? state)
    {
        state = null;
        if (!File.Exists(path))
            return false;
        try
        {
            var envelope = JsonSerializer.Deserialize<StoredEnvelope>(File.ReadAllBytes(path), JsonOptions);
            if (envelope == null
                || envelope.FormatVersion != EnvelopeFormatVersion
                || string.IsNullOrWhiteSpace(envelope.PayloadBase64)
                || string.IsNullOrWhiteSpace(envelope.PayloadSha256))
            {
                return false;
            }
            var payload = Convert.FromBase64String(envelope.PayloadBase64);
            if (!string.Equals(ComputeSha256(payload), envelope.PayloadSha256, StringComparison.Ordinal))
                return false;
            state = JsonSerializer.Deserialize<StoredPatternState>(payload, JsonOptions)?.ToDomain();
            return state != null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or FormatException
            or CryptographicException
            or ArgumentException)
        {
            return false;
        }
    }

    private static string ComputeSha256(byte[] payload)
        => Convert.ToHexString(SHA256.HashData(payload));

    private sealed class StoredEnvelope
    {
        public int FormatVersion { get; set; }
        public string PayloadBase64 { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
    }

    private sealed class StoredPatternState
    {
        public int SchemaVersion { get; set; }
        public long ObservedTurns { get; set; }
        public double BaselineDirectness { get; set; }
        public double BaselineExpressiveness { get; set; }
        public double EmotionalBlackmailScore { get; set; }
        public double MoralCoercionScore { get; set; }
        public double ThreatScore { get; set; }
        public double GaslightingScore { get; set; }
        public double WithdrawalThreatScore { get; set; }
        public double PuaTacticScore { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        internal static StoredPatternState FromDomain(CommunicationPatternState state)
            => new()
            {
                SchemaVersion = state.SchemaVersion,
                ObservedTurns = state.ObservedTurns,
                BaselineDirectness = state.BaselineDirectness,
                BaselineExpressiveness = state.BaselineExpressiveness,
                EmotionalBlackmailScore = state.EmotionalBlackmailScore,
                MoralCoercionScore = state.MoralCoercionScore,
                ThreatScore = state.ThreatScore,
                GaslightingScore = state.GaslightingScore,
                WithdrawalThreatScore = state.WithdrawalThreatScore,
                PuaTacticScore = state.PuaTacticScore,
                UpdatedAt = state.UpdatedAt
            };

        internal CommunicationPatternState ToDomain()
            => new(
                ObservedTurns,
                BaselineDirectness,
                BaselineExpressiveness,
                EmotionalBlackmailScore,
                MoralCoercionScore,
                ThreatScore,
                GaslightingScore,
                WithdrawalThreatScore,
                PuaTacticScore,
                UpdatedAt,
                SchemaVersion);
    }
}
