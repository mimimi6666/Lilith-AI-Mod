using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;
using System.Collections.Immutable;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal abstract record LilithLettersCommand
{
    internal sealed record Configure(LilithLettersSettings Settings, DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Record(LilithLetterTurnReference Source, DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Classify(Guid Key, LilithLetterConversationKind Kind, DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Collect(DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Resume(DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Prune(DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record BeginEvaluation(DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record BeginPreparation(DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Withdraw(Guid Key, DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record ReconcileAttestedSources(ImmutableArray<Guid> UnattestedKeys, DateTimeOffset Now) : LilithLettersCommand;
    internal sealed record Reserve(Guid DeliveryId, LilithLetterOpportunity Opportunity, bool MaterialSufficient) : LilithLettersCommand;
    internal sealed record Resolve(Guid DeliveryId, LilithLetterDeliveryPhase Phase, DateTimeOffset Now,
        DateTimeOffset? DeliveredAtUtc = null) : LilithLettersCommand;
}

// Separate, bounded metadata only. No native tool, transcript or live path default.
// Publication must complete before any future caller may use a reservation.
internal sealed class LilithLettersStore
{
    internal const string DirectoryName = "letters-state-v1";
    internal const string StateFileName = "letters.v1.json";
    internal const string MarkerFileName = "initialized.v1.json";
    internal const string LeaseFileName = "writer.lock";
    internal const string NextFileName = "letters.next.json";
    internal const int MaximumStateBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    { MaxDepth = 12, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string _directory;
    private readonly LilithLettersSnapshot _initial;
    private readonly TimeZoneInfo _zone;
    private readonly LilithLettersTiming _timing;
    private readonly Action<string>? _publicationCheckpoint;

    internal LilithLettersStore(string trustedProfileDirectory, string profileId, TimeZoneInfo zone,
        LilithLettersSettings? initialSettings = null, Action<string>? publicationCheckpoint = null, LilithLettersTiming? timing = null)
    {
        _directory = Path.Combine(CanonicalDirectory(trustedProfileDirectory), DirectoryName);
        _zone = zone;
        _timing = timing ?? LilithLettersTiming.Production;
        _initial = LilithLettersState.Create(profileId, zone, initialSettings ?? new());
        _publicationCheckpoint = publicationCheckpoint;
    }

    internal LilithLettersSnapshot Initialize(bool hostAuthorizedFirstUse)
    {
        CheckDirectory(_directory);
        if (Exists(MarkerFileName)) return Read();
        if (!hostAuthorizedFirstUse || Exists(StateFileName) || Exists(NextFileName) || Exists(LeaseFileName))
            throw new InvalidDataException("Letter initialization is not authorized or is incomplete.");
        Directory.CreateDirectory(_directory);
        CheckDirectory(_directory);
        using var lease = OpenLease(true);
        if (Exists(MarkerFileName) || Exists(StateFileName) || Exists(NextFileName))
            throw new InvalidDataException("Letter initialization changed.");
        WriteNew(MarkerFileName, JsonSerializer.SerializeToUtf8Bytes(BindingValue(), JsonOptions));
        _publicationCheckpoint?.Invoke("initialized-marker");
        Publish(_initial, true);
        return ReadCurrent();
    }

    internal LilithLettersSnapshot Read()
    {
        CheckDirectory(_directory);
        using var lease = OpenLease(false);
        return ReadCurrent();
    }

    internal LilithLettersSnapshot Apply(LilithLettersSnapshot expected, LilithLettersCommand command)
    {
        ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(command);
        CheckDirectory(_directory);
        using var lease = OpenLease(false);
        var current = ReadCurrent();
        if (!JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions)
                .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonOptions)))
            throw new InvalidDataException("Letter state changed before commit.");
        // No arbitrary caller-provided replacement snapshot or transition delegate.
        var next = command switch
        {
            LilithLettersCommand.Configure c => LilithLettersState.Configure(current, c.Settings, c.Now),
            LilithLettersCommand.Record c => LilithLettersState.Record(current, c.Source, c.Now),
            LilithLettersCommand.Classify c => LilithLettersState.Classify(current, c.Key, c.Kind, c.Now),
            LilithLettersCommand.Collect c => LilithLettersState.Collect(current, c.Now, _timing),
            LilithLettersCommand.Resume c => LilithLettersState.Resume(current, c.Now),
            LilithLettersCommand.Prune c => LilithLettersState.Prune(current, c.Now),
            LilithLettersCommand.BeginEvaluation c => LilithLettersState.BeginEvaluation(current, c.Now, _zone, _timing),
            LilithLettersCommand.BeginPreparation c => LilithLettersState.BeginPreparation(current, c.Now, _timing),
            LilithLettersCommand.Withdraw c => LilithLettersState.Withdraw(current, c.Key, c.Now),
            LilithLettersCommand.ReconcileAttestedSources c => LilithLettersState.ReconcileAttestedSources(current, c.UnattestedKeys, c.Now, _timing),
            LilithLettersCommand.Reserve c => LilithLettersState.Reserve(current, c.DeliveryId, c.Opportunity, c.MaterialSufficient, _zone, _timing),
            LilithLettersCommand.Resolve c => LilithLettersState.Resolve(current, c.DeliveryId, c.Phase, c.Now, c.DeliveredAtUtc),
            _ => throw new ArgumentException("Unsupported letter transition.")
        };
        if (ReferenceEquals(next, current)) return current;
        ValidateBinding(next);
        Publish(next, false);
        var confirmed = ReadCurrent();
        if (!JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(confirmed, JsonOptions)))
            throw new InvalidDataException("Letter publication could not be confirmed.");
        return confirmed;
    }

    private Binding BindingValue() => new(1, _initial.ProfileId, _initial.TimeZoneId, _initial.TimeZoneRules);
    private LilithLettersSnapshot ReadCurrent()
    {
        if (Exists(NextFileName)) throw new InvalidDataException("Letter publication is unresolved.");
        using (var marker = ReadJson(MarkerFileName))
        {
            Exact(marker.RootElement, "SchemaVersion", "ProfileId", "TimeZoneId", "TimeZoneFingerprint");
            if (marker.RootElement.Deserialize<Binding>(JsonOptions) != BindingValue())
                throw new InvalidDataException("Letter binding changed.");
        }
        using var document = ReadJson(StateFileName);
        var root = document.RootElement;
        var hasBudget = root.TryGetProperty("Evaluation", out var evaluation);
        var fields = new List<string> { "SchemaVersion", "Revision", "ProfileId", "TimeZoneId", "TimeZoneRules", "HighWaterUtc", "Settings", "Batch", "Turns", "Deliveries" };
        if (hasBudget) fields.Add("Evaluation");
        if (root.TryGetProperty("LastPreparationUtc", out _)) fields.Add("LastPreparationUtc");
        Exact(root, fields.ToArray());
        if (hasBudget && evaluation.ValueKind != JsonValueKind.Null) Exact(evaluation, "LastStartedUtc", "LocalDay", "Attempts");
        Exact(root.GetProperty("Settings"), "Enabled", "DailyLimit");
        var batch = root.GetProperty("Batch");
        if (batch.ValueKind != JsonValueKind.Null)
            Exact(batch, "BatchId", "FirstTurnId", "LastTurnId", "FirstChatUtc", "LastChatUtc", "SealedAtUtc", "PendingAtRestart");
        var turns = root.GetProperty("Turns"); var deliveries = root.GetProperty("Deliveries");
        if (turns.ValueKind != JsonValueKind.Array || turns.GetArrayLength() > LilithLettersHistory.MaximumTurns
            || deliveries.ValueKind != JsonValueKind.Array || deliveries.GetArrayLength() > 4096)
            throw new InvalidDataException("Letter collections are invalid.");
        foreach (var turn in turns.EnumerateArray())
        {
            Exact(turn, "Source", "Kind", "Used", "Withdrawn");
            Exact(turn.GetProperty("Source"), "Key", "ProfileId", "StoredSessionId", "NativeTurnId", "CompletedAtUtc", "UserSha256", "AssistantSha256");
        }
        foreach (var delivery in deliveries.EnumerateArray())
        {
            Exact(delivery, "BatchId", "Receipt", "MaterialKeys");
            Exact(delivery.GetProperty("Receipt"), "DeliveryId", "ReservedAtUtc", "Phase", "DeliveredAtUtc");
        }
        var state = root.Deserialize<LilithLettersSnapshot>(JsonOptions)
            ?? throw new InvalidDataException("Letter state missing.");
        ValidateBinding(state);
        return state;
    }

    private void ValidateBinding(LilithLettersSnapshot state)
    {
        LilithLettersState.Validate(state);
        if (state.ProfileId != _initial.ProfileId || state.TimeZoneId != _initial.TimeZoneId || state.TimeZoneRules != _initial.TimeZoneRules)
            throw new InvalidDataException("Letter state belongs to another host binding.");
    }

    private void Publish(LilithLettersSnapshot state, bool first)
    {
        WriteNew(NextFileName, JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
        _publicationCheckpoint?.Invoke("before-state-publish");
        CheckDirectory(_directory); CheckFile(NextFileName, true); CheckFile(StateFileName, !first);
        if (first) File.Move(Leaf(NextFileName), Leaf(StateFileName));
        else File.Replace(Leaf(NextFileName), Leaf(StateFileName), null);
        _publicationCheckpoint?.Invoke("after-state-publish");
    }

    private FileStream OpenLease(bool createNew)
    {
        CheckFile(LeaseFileName, required: !createNew);
        var stream = new FileStream(Leaf(LeaseFileName), createNew ? FileMode.CreateNew : FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            VerifyHandle(stream, Leaf(LeaseFileName));
            if (stream.Length != 0) throw new InvalidDataException("Letter writer lease is invalid.");
            if (createNew) stream.Flush(flushToDisk: true);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private void WriteNew(string name, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumStateBytes)
            throw new InvalidDataException("Letter record length is invalid.");
        CheckDirectory(_directory);
        CheckFile(name, required: false);
        using var stream = new FileStream(Leaf(name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        VerifyHandle(stream, Leaf(name));
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private JsonDocument ReadJson(string name)
    {
        CheckFile(name, required: true);
        using var stream = new FileStream(Leaf(name), FileMode.Open, FileAccess.Read, FileShare.Read);
        VerifyHandle(stream, Leaf(name));
        if (stream.Length <= 0 || stream.Length > MaximumStateBytes)
            throw new InvalidDataException("Letter record length is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Letter record changed during read.");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private bool Exists(string name)
    {
        try { _ = File.GetAttributes(Leaf(name)); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private void CheckFile(string name, bool required)
    {
        CheckDirectory(_directory);
        if (!Exists(name))
        {
            if (required) throw new InvalidDataException("Letter durable record is missing.");
            return;
        }
        if ((File.GetAttributes(Leaf(name)) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Letter record is not an ordinary file.");
    }

    private string Leaf(string name) => Path.Combine(_directory, name);

    private static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("A host-pinned local profile directory is required.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.GetPathRoot(full)!;
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(root), PathComparison)
            || full[root.Length..].Contains(':'))
            throw new ArgumentException("A letter profile directory cannot be a root or alternate stream.");
        return full;
    }

    private static void CheckDirectory(string directory)
    {
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("Letter directory is not an ordinary local directory.");
        }
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Letter object is invalid.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || names.Any(name => !actual.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("Letter record fields are not exact.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Letter duplicate field rejected.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void VerifyHandle(FileStream stream, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("Letter file identity could not be verified.");
        var actual = buffer.ToString();
        if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual[4..];
        if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(path), PathComparison)
            || !GetFileInformationByHandle(stream.SafeFileHandle, out var information)
            || information.NumberOfLinks != 1
            || (information.FileAttributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Letter file identity escaped its exact ordinary leaf.");
    }

    private sealed record Binding(int SchemaVersion, string ProfileId, string TimeZoneId, string TimeZoneFingerprint);

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
