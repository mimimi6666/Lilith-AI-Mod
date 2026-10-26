using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LilithGameBridgeContracts;
using Microsoft.Win32.SafeHandles;

namespace LilithRuntime;

internal sealed record LilithPlayerDisplayNameSnapshot(long Revision, string DisplayName);

// A single current official-game fact, not a semantic-memory or settings writer.
// The ready Runtime supplies the trusted profile root; callers supply no path/scope.
internal sealed class LilithPlayerDisplayNameStore
{
    internal const int MaximumBytes = 4096;
    private const string NextFile = "player-display-name.next.json";
    private const string LockFile = "player-display-name.writer.lock";
    private readonly string _home;
    private readonly Action<string>? _checkpoint;

    internal LilithPlayerDisplayNameStore(string trustedProfileHome, Action<string>? checkpoint = null)
    {
        if (string.IsNullOrWhiteSpace(trustedProfileHome) || !Path.IsPathFullyQualified(trustedProfileHome)
            || trustedProfileHome.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("A local host-owned profile root is required.");
        _home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedProfileHome));
        var root = Path.GetPathRoot(_home)!;
        if (_home == Path.TrimEndingDirectorySeparator(root) || _home[root.Length..].Contains(':'))
            throw new ArgumentException("A profile root or alternate stream is not accepted.");
        _checkpoint = checkpoint;
    }

    internal LilithPlayerDisplayNameSnapshot? Read()
    {
        CheckDirectories();
        CheckLeaf(NextFile, required: false);
        if (File.Exists(Leaf(NextFile)))
            throw new InvalidDataException("Player-name publication is unresolved.");
        CheckLeaf(LilithPlayerDisplayName.StateFileName, required: false);
        if (!File.Exists(Leaf(LilithPlayerDisplayName.StateFileName))) return null;
        using var stream = OpenOrdinary(LilithPlayerDisplayName.StateFileName, FileMode.Open,
            FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length is <= 0 or > MaximumBytes)
            throw new InvalidDataException("Player-name record length rejected.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Player-name record changed during read.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
        var value = document.RootElement;
        var names = new[] { "schema_version", "profile_id", "source", "revision", "display_name" };
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Player-name schema rejected.");
        var properties = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (properties.Length != names.Length || properties.Distinct(StringComparer.Ordinal).Count() != names.Length
            || names.Any(name => !properties.Contains(name, StringComparer.Ordinal))
            || !value.GetProperty("schema_version").TryGetInt32(out var version) || version != 1
            || value.GetProperty("profile_id").GetString() != LilithPlayerDisplayName.ProfileId
            || value.GetProperty("source").GetString() != LilithPlayerDisplayName.Source
            || !value.GetProperty("revision").TryGetInt64(out var revision) || revision <= 0)
            throw new InvalidDataException("Player-name scope or schema rejected.");
        return new(revision, LilithPlayerDisplayName.Validate(value.GetProperty("display_name").GetString()!));
    }

    internal LilithPlayerDisplayNameSnapshot Set(string displayName)
    {
        LilithPlayerDisplayName.Validate(displayName);
        CheckDirectories();
        // The managed profile must already exist. Name sync never creates a profile.
        if (!Directory.Exists(_home)) throw new DirectoryNotFoundException("Managed profile is unavailable.");
        CheckLeaf(LockFile, required: false);
        using var lease = OpenOrdinary(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (lease.Length != 0) throw new InvalidDataException("Player-name writer lease rejected.");
        var current = Read();
        if (current?.DisplayName == displayName) return current;
        if (current?.Revision == long.MaxValue) throw new InvalidDataException("Player-name revision exhausted.");
        var next = new LilithPlayerDisplayNameSnapshot((current?.Revision ?? 0) + 1, displayName);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1, profile_id = LilithPlayerDisplayName.ProfileId,
            source = LilithPlayerDisplayName.Source, revision = next.Revision, display_name = next.DisplayName
        });
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Player-name state exceeds its bound.");
        using (var stream = OpenOrdinary(NextFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        _checkpoint?.Invoke("before-publish");
        CheckDirectories();
        CheckLeaf(NextFile, required: true);
        CheckLeaf(LilithPlayerDisplayName.StateFileName, required: current is not null);
        if (current is null) File.Move(Leaf(NextFile), Leaf(LilithPlayerDisplayName.StateFileName));
        else File.Replace(Leaf(NextFile), Leaf(LilithPlayerDisplayName.StateFileName), null);
        _checkpoint?.Invoke("after-publish");
        if (Read() != next) throw new InvalidDataException("Player-name publication could not be confirmed.");
        return next;
    }

    private string Leaf(string name) => Path.Combine(_home, name);
    private void CheckDirectories()
    {
        for (var directory = _home; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("Player-name directory is not an ordinary local directory.");
        }
    }

    private void CheckLeaf(string name, bool required)
    {
        CheckDirectories();
        FileAttributes attributes;
        try { attributes = File.GetAttributes(Leaf(name)); }
        catch (FileNotFoundException) when (!required) { return; }
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Player-name record is not an ordinary file.");
    }

    private FileStream OpenOrdinary(string name, FileMode mode, FileAccess access, FileShare share)
    {
        CheckLeaf(name, required: mode == FileMode.Open);
        var stream = new FileStream(Leaf(name), mode, access, share);
        try { VerifyHandle(stream, Leaf(name)); return stream; }
        catch { stream.Dispose(); throw; }
    }

    private static void VerifyHandle(FileStream stream, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("Player-name file identity unavailable.");
        var actual = buffer.ToString();
        if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual[4..];
        if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            || !GetFileInformationByHandle(stream.SafeFileHandle, out var information)
            || information.NumberOfLinks != 1
            || (information.FileAttributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Player-name record escaped its ordinary leaf.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        internal uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, int length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}

internal sealed class LilithPlayerDisplayNameSync
{
    private readonly object _gate = new();
    private readonly LilithPlayerDisplayNameStore _store;
    private readonly Guid _generation;
    private uint _process;
    private ulong _created;
    private CancellationToken _peerExited;
    private Guid _epoch;
    private long _sequence;
    private string? _value;

    internal LilithPlayerDisplayNameSync(string trustedProfileHome, Guid generation,
        Action<string>? checkpoint = null)
    { _store = new(trustedProfileHome, checkpoint); _generation = generation; }

    internal LilithGameBridgePlayerNameResponse Apply(LilithGameBridgePlayerNameRequest request,
        ILilithGameBridgePeerLease peer)
    {
        lock (_gate)
        {
            if (request.GenerationId != _generation || !peer.IsValid() || peer.PeerExited.IsCancellationRequested
                || peer.ProcessId == 0 || peer.ProcessCreationTime == 0)
                return Rejected(request);
            if (_epoch != Guid.Empty && (_process != peer.ProcessId || _created != peer.ProcessCreationTime))
            {
                if (!_peerExited.IsCancellationRequested) return Rejected(request);
                _epoch = Guid.Empty; _sequence = 0; _value = null;
            }
            if (_epoch != Guid.Empty && (_epoch != request.GameEpoch || request.ObservationSequence < _sequence
                || request.ObservationSequence == _sequence && request.DisplayName != _value))
                return Rejected(request);
            // Capture the peer before a potentially ambiguous publication. A failed
            // disk write cannot allow another still-live game to claim this scope.
            _process = peer.ProcessId; _created = peer.ProcessCreationTime;
            _peerExited = peer.PeerExited; _epoch = request.GameEpoch;
            // Retain the observation even if publication becomes uncertain. Only
            // that observation's exact retry or a newer observation may follow.
            _sequence = request.ObservationSequence; _value = request.DisplayName;
            var snapshot = _store.Set(request.DisplayName);
            return new(_generation, request.RequestId, 2, request.GameEpoch,
                request.ObservationSequence, true, snapshot.Revision);
        }
    }

    private LilithGameBridgePlayerNameResponse Rejected(LilithGameBridgePlayerNameRequest request)
        => new(_generation, request.RequestId, 2, request.GameEpoch, request.ObservationSequence, false, 0);
}
