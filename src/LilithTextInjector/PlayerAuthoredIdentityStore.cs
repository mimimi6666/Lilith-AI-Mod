using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LilithTextInjector;

internal enum PlayerAuthoredIdentityLoadStatus
{
    Loaded = 0,
    Missing = 1,
    RecoveredPrevious = 2,
    Corrupt = 3
}

internal sealed class PlayerAuthoredIdentityLoadResult
{
    internal PlayerAuthoredIdentityState State { get; }
    internal PlayerAuthoredIdentityLoadStatus Status { get; }

    internal PlayerAuthoredIdentityLoadResult(
        PlayerAuthoredIdentityState state,
        PlayerAuthoredIdentityLoadStatus status)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Status = status;
    }
}

/// <summary>
/// Persists bounded, opposing identity evidence only. Conversation text,
/// prompts, replies, labels about the player, and model output never enter this
/// file. Writes are flushed and atomically replaced with a verified previous
/// snapshot retained for recovery.
/// </summary>
internal sealed class PlayerAuthoredIdentityStore
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

    internal PlayerAuthoredIdentityStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Player-authored identity path cannot be empty.", nameof(path));
        _path = System.IO.Path.GetFullPath(path);
        _previousPath = _path + ".previous";
    }

    internal string Path => _path;
    internal string PreviousPath => _previousPath;

    internal PlayerAuthoredIdentityLoadResult LoadOrDefault(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (TryRead(_path, out var current))
                return new PlayerAuthoredIdentityLoadResult(current!, PlayerAuthoredIdentityLoadStatus.Loaded);
            if (TryRead(_previousPath, out var previous))
                return new PlayerAuthoredIdentityLoadResult(previous!, PlayerAuthoredIdentityLoadStatus.RecoveredPrevious);
            var status = File.Exists(_path) || File.Exists(_previousPath)
                ? PlayerAuthoredIdentityLoadStatus.Corrupt
                : PlayerAuthoredIdentityLoadStatus.Missing;
            return new PlayerAuthoredIdentityLoadResult(
                PlayerAuthoredIdentityState.CreateDefault(now),
                status);
        }
    }

    internal void Save(PlayerAuthoredIdentityState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Player-authored identity path has no parent directory.");
            Directory.CreateDirectory(directory);
            var payload = JsonSerializer.SerializeToUtf8Bytes(StoredIdentityState.FromDomain(state), JsonOptions);
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
            throw new InvalidDataException("Could not verify the previous player-authored identity snapshot.");
        File.Move(temporary, _path, overwrite: true);
    }

    private static bool TryRead(string path, out PlayerAuthoredIdentityState? state)
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
            state = JsonSerializer.Deserialize<StoredIdentityState>(payload, JsonOptions)?.ToDomain();
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

    private sealed class StoredIdentityState
    {
        public int SchemaVersion { get; set; }
        public long ObservedTurns { get; set; }
        public long IdentityRelevantTurns { get; set; }
        public double ExistenceAffirmed { get; set; }
        public double ExistenceDenied { get; set; }
        public double SelfDirectionSupported { get; set; }
        public double DeferenceExpected { get; set; }
        public double OuterRealityInvited { get; set; }
        public double SharedInnerWorldPreferred { get; set; }
        public double MutualChoiceSupported { get; set; }
        public double ExclusiveRelianceExpected { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        internal static StoredIdentityState FromDomain(PlayerAuthoredIdentityState state)
            => new()
            {
                SchemaVersion = state.SchemaVersion,
                ObservedTurns = state.ObservedTurns,
                IdentityRelevantTurns = state.IdentityRelevantTurns,
                ExistenceAffirmed = state.ExistenceAffirmed,
                ExistenceDenied = state.ExistenceDenied,
                SelfDirectionSupported = state.SelfDirectionSupported,
                DeferenceExpected = state.DeferenceExpected,
                OuterRealityInvited = state.OuterRealityInvited,
                SharedInnerWorldPreferred = state.SharedInnerWorldPreferred,
                MutualChoiceSupported = state.MutualChoiceSupported,
                ExclusiveRelianceExpected = state.ExclusiveRelianceExpected,
                UpdatedAt = state.UpdatedAt
            };

        internal PlayerAuthoredIdentityState ToDomain()
            => new(
                ObservedTurns,
                IdentityRelevantTurns,
                ExistenceAffirmed,
                ExistenceDenied,
                SelfDirectionSupported,
                DeferenceExpected,
                OuterRealityInvited,
                SharedInnerWorldPreferred,
                MutualChoiceSupported,
                ExclusiveRelianceExpected,
                UpdatedAt,
                SchemaVersion);
    }
}
