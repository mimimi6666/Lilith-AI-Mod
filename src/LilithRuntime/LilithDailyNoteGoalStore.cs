using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace LilithRuntime;

// Disconnected persistence boundary, not a scheduler or delivery authority.
// A caller may act on a transition only after Commit returns successfully.
internal sealed class LilithDailyNoteGoalStore
{
    internal const string DirectoryName = "daily-note-goal-v1";
    internal const string StateFileName = "goal.v1.json";
    internal const string MarkerFileName = "initialized.v1.json";
    internal const string LeaseFileName = "writer.lock";
    internal const string NextFileName = "goal.next.json";
    internal const int MaximumStateBytes = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _directory;
    private readonly LilithDailyNoteGoalSnapshot _initial;
    private readonly Action<string>? _publicationCheckpoint;

    internal LilithDailyNoteGoalStore(string trustedProfileDirectory, string profileId,
        TimeZoneInfo timeZone, Action<string>? publicationCheckpoint = null)
    {
        _directory = Path.Combine(CanonicalDirectory(trustedProfileDirectory), DirectoryName);
        _initial = LilithDailyNoteGoal.Create(profileId, timeZone);
        _publicationCheckpoint = publicationCheckpoint;
    }

    internal LilithDailyNoteGoalSnapshot Initialize(bool ownerAuthorizedFirstUse)
    {
        CheckDirectory(_directory);
        if (Exists(MarkerFileName)) return Read();
        // A surviving lock is also a first-use tombstone. A partial initialization,
        // missing marker, or missing committed state never resets the budget.
        if (!ownerAuthorizedFirstUse || Exists(StateFileName) || Exists(NextFileName)
            || Exists(LeaseFileName))
            throw new InvalidDataException("Daily note initialization is not authorized or is incomplete.");

        Directory.CreateDirectory(_directory);
        CheckDirectory(_directory);
        using var lease = OpenLease(createNew: true);
        if (Exists(MarkerFileName) || Exists(StateFileName) || Exists(NextFileName))
            throw new InvalidDataException("Daily note initialization changed.");
        // Marker is durable before the first state: an interrupted initialization
        // requires an explicit future recovery design, never an automatic reset.
        WriteNew(MarkerFileName, JsonSerializer.SerializeToUtf8Bytes(new Binding(
            1, _initial.ProfileId, _initial.TimeZoneId, _initial.TimeZoneFingerprint), JsonOptions));
        _publicationCheckpoint?.Invoke("initialized-marker");
        Publish(_initial, first: true);
        return ReadCurrent();
    }

    internal LilithDailyNoteGoalSnapshot Read()
    {
        CheckDirectory(_directory);
        using var lease = OpenLease(createNew: false);
        return ReadCurrent();
    }

    internal LilithDailyNoteTransition Commit(LilithDailyNoteGoalSnapshot expected,
        LilithDailyNoteTransition transition)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(transition);
        CheckDirectory(_directory);
        using var lease = OpenLease(createNew: false);
        var current = ReadCurrent();
        // Compare the complete immutable snapshot, not a stale per-instance cache.
        if (current != expected)
            throw new InvalidDataException("Daily note state changed before commit.");
        ValidateBinding(transition.Next);
        if (!Enum.IsDefined(transition.Effect))
            throw new InvalidDataException("Daily note effect is invalid.");
        if (transition.Next == current)
        {
            if (transition.Effect != LilithDailyNoteEffect.None)
                throw new InvalidDataException("A daily note effect requires a durable transition.");
            return transition;
        }
        if (current.Revision == long.MaxValue || transition.Next.Revision != current.Revision + 1)
            throw new InvalidDataException("Daily note revision is not consecutive.");
        ValidateForwardTransition(current, transition);
        Publish(transition.Next, first: false);
        if (ReadCurrent() != transition.Next)
            throw new InvalidDataException("Daily note commit could not be confirmed.");
        return transition;
    }

    private LilithDailyNoteGoalSnapshot ReadCurrent()
    {
        if (Exists(NextFileName))
            throw new InvalidDataException("Daily note publication is unresolved.");
        using (var document = ReadJson(MarkerFileName))
        {
            Exact(document.RootElement, "SchemaVersion", "ProfileId", "TimeZoneId", "TimeZoneFingerprint");
            var binding = document.RootElement.Deserialize<Binding>(JsonOptions)
                ?? throw new InvalidDataException("Daily note binding is missing.");
            if (binding != new Binding(1, _initial.ProfileId, _initial.TimeZoneId, _initial.TimeZoneFingerprint))
                throw new InvalidDataException("Daily note host binding changed.");
        }
        using var stateDocument = ReadJson(StateFileName);
        var state = stateDocument.RootElement;
        Exact(state, "SchemaVersion", "Revision", "ProfileId", "TimeZoneId", "TimeZoneFingerprint",
            "UtcHighWater", "DayHighWater", "LastSatisfiedDay", "LastSatisfiedDeliveryId", "Current");
        var entry = state.GetProperty("Current");
        if (entry.ValueKind != JsonValueKind.Null)
        {
            Exact(entry, "Day", "GoalId", "Attempts", "Phase", "NotBeforeUtc",
                "LastOpportunitySequence", "Attempt", "Reason");
            var attempt = entry.GetProperty("Attempt");
            if (attempt.ValueKind != JsonValueKind.Null)
                Exact(attempt, "GoalId", "AttemptId", "DeliveryId", "OperationId", "Day",
                    "Ordinal", "ReservedAtUtc", "DeliveryReservedAtUtc");
        }
        var snapshot = state.Deserialize<LilithDailyNoteGoalSnapshot>(JsonOptions)
            ?? throw new InvalidDataException("Daily note state is missing.");
        ValidateBinding(snapshot);
        return snapshot;
    }

    private void ValidateBinding(LilithDailyNoteGoalSnapshot snapshot)
    {
        try { LilithDailyNoteGoal.Validate(snapshot); }
        catch (ArgumentException) { throw new InvalidDataException("Daily note state invariants are invalid."); }
        if (snapshot.ProfileId != _initial.ProfileId || snapshot.TimeZoneId != _initial.TimeZoneId
            || snapshot.TimeZoneFingerprint != _initial.TimeZoneFingerprint)
            throw new InvalidDataException("Daily note state belongs to a different host binding.");
    }

    private static void ValidateForwardTransition(LilithDailyNoteGoalSnapshot prior, LilithDailyNoteTransition transition)
    {
        var next = transition.Next;
        var oldGoal = prior.Current;
        var goal = next.Current;
        if (next.UtcHighWater < prior.UtcHighWater || next.DayHighWater < prior.DayHighWater
            || prior.LastSatisfiedDay.HasValue && (!next.LastSatisfiedDay.HasValue
                || next.LastSatisfiedDay < prior.LastSatisfiedDay
                || next.LastSatisfiedDay == prior.LastSatisfiedDay && next.LastSatisfiedDeliveryId != prior.LastSatisfiedDeliveryId)
            || oldGoal is not null && (goal is null || goal.Day < oldGoal.Day))
            throw new InvalidDataException("Daily note high-water state cannot regress.");
        if (oldGoal is not null && goal is not null)
        {
            var unresolved = oldGoal.Phase is LilithDailyNoteGoalPhase.EvaluationReserved
                or LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Unknown;
            if (goal.Day != oldGoal.Day && unresolved)
                throw new InvalidDataException("An unresolved daily note cannot be replaced by another day.");
            if (goal.Day == oldGoal.Day)
            {
                if (oldGoal.Phase == LilithDailyNoteGoalPhase.Unknown
                    && (goal.Phase is LilithDailyNoteGoalPhase.EvaluationReserved or LilithDailyNoteGoalPhase.DeliveryReserved)
                    || oldGoal.Phase == LilithDailyNoteGoalPhase.DeliveryReserved
                    && goal.Phase == LilithDailyNoteGoalPhase.EvaluationReserved)
                    throw new InvalidDataException("Daily note uncertainty cannot re-arm an earlier effect.");
                if (goal.Phase == LilithDailyNoteGoalPhase.EvaluationReserved && goal.Attempts > oldGoal.Attempts
                    && transition.Effect != LilithDailyNoteEffect.Evaluate
                    || goal.Phase == LilithDailyNoteGoalPhase.DeliveryReserved && oldGoal.Phase != goal.Phase
                    && transition.Effect != LilithDailyNoteEffect.Deliver)
                    throw new InvalidDataException("Daily note reservations require their matching committed effect.");
                if (goal.GoalId != oldGoal.GoalId || goal.Attempts < oldGoal.Attempts
                    || goal.Attempts > oldGoal.Attempts + 1 || goal.LastOpportunitySequence < oldGoal.LastOpportunitySequence
                    || oldGoal.Phase == LilithDailyNoteGoalPhase.Satisfied && goal.Phase != oldGoal.Phase)
                    throw new InvalidDataException("Daily note budget or goal identity cannot reset.");
                if (goal.Attempts == oldGoal.Attempts && oldGoal.Attempt is { } oldAttempt
                    && (goal.Attempt is null || (goal.Attempt with { DeliveryReservedAtUtc = oldAttempt.DeliveryReservedAtUtc }) != oldAttempt
                        || oldAttempt.DeliveryReservedAtUtc.HasValue && goal.Attempt.DeliveryReservedAtUtc != oldAttempt.DeliveryReservedAtUtc))
                    throw new InvalidDataException("Daily note delivery identity cannot change during reconciliation.");
                if (goal.Attempts > oldGoal.Attempts && transition.Effect != LilithDailyNoteEffect.Evaluate)
                    throw new InvalidDataException("A daily note budget reservation requires its matching effect.");
                if (goal.Attempts == oldGoal.Attempts && oldGoal.Phase == LilithDailyNoteGoalPhase.Ready
                    && goal.Phase == LilithDailyNoteGoalPhase.Ready && oldGoal.NotBeforeUtc.HasValue
                    && (!goal.NotBeforeUtc.HasValue || goal.NotBeforeUtc < oldGoal.NotBeforeUtc))
                    throw new InvalidDataException("Daily note retry backoff cannot regress.");
                if (unresolved && (goal.Phase is LilithDailyNoteGoalPhase.Ready or LilithDailyNoteGoalPhase.Exhausted or LilithDailyNoteGoalPhase.Expired)
                    && (transition.Effect != LilithDailyNoteEffect.None || transition.Reason != "verified_no_effect"
                        && !(oldGoal.Phase == LilithDailyNoteGoalPhase.EvaluationReserved && goal.Phase == LilithDailyNoteGoalPhase.Expired)))
                    throw new InvalidDataException("Daily note uncertainty requires explicit no-effect evidence or a bound receipt.");
            }
        }
        if (transition.Effect == LilithDailyNoteEffect.None) return;
        if (goal?.Attempt is null || transition.Attempt != goal.Attempt)
            throw new InvalidDataException("Daily note effect is not bound to the committed attempt.");
        var valid = transition.Effect switch
        {
            LilithDailyNoteEffect.Evaluate => goal.Phase == LilithDailyNoteGoalPhase.EvaluationReserved
                && (oldGoal is null || oldGoal.Day < goal.Day || oldGoal.Phase == LilithDailyNoteGoalPhase.Ready)
                && goal.Attempts == (oldGoal?.Day == goal.Day ? oldGoal.Attempts + 1 : 1)
                && goal.LastOpportunitySequence > (oldGoal?.LastOpportunitySequence ?? 0)
                && (oldGoal?.NotBeforeUtc is null || next.UtcHighWater >= oldGoal.NotBeforeUtc.Value),
            LilithDailyNoteEffect.Deliver => goal.Phase == LilithDailyNoteGoalPhase.DeliveryReserved
                && oldGoal?.Phase == LilithDailyNoteGoalPhase.EvaluationReserved,
            LilithDailyNoteEffect.Reconcile => goal.Phase == LilithDailyNoteGoalPhase.Unknown
                && oldGoal?.Phase is LilithDailyNoteGoalPhase.EvaluationReserved or LilithDailyNoteGoalPhase.DeliveryReserved or LilithDailyNoteGoalPhase.Unknown,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Daily note effect is not a forward kernel transition.");
    }

    private void Publish(LilithDailyNoteGoalSnapshot snapshot, bool first)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        if (bytes.Length > MaximumStateBytes)
            throw new InvalidDataException("Daily note state exceeds its fixed bound.");
        // A fixed, exclusive next leaf also records an interrupted publication.
        // Never delete or recover that leaf to release an uncertain budget.
        WriteNew(NextFileName, bytes);
        _publicationCheckpoint?.Invoke("before-state-publish");
        CheckDirectory(_directory);
        CheckFile(NextFileName, required: true);
        CheckFile(StateFileName, required: !first);
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
            if (stream.Length != 0) throw new InvalidDataException("Daily note writer lease is invalid.");
            if (createNew) stream.Flush(flushToDisk: true);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private void WriteNew(string name, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumStateBytes)
            throw new InvalidDataException("Daily note record length is invalid.");
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
            throw new InvalidDataException("Daily note record length is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Daily note record changed during read.");
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
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
            if (required) throw new InvalidDataException("Daily note durable record is missing.");
            return;
        }
        if ((File.GetAttributes(Leaf(name)) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Daily note record is not an ordinary file.");
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
            throw new ArgumentException("A daily note profile directory cannot be a root or alternate stream.");
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
                throw new InvalidDataException("Daily note directory is not an ordinary local directory.");
        }
    }

    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Daily note object is invalid.");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || names.Any(name => !actual.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("Daily note record fields are not exact.");
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Daily note duplicate field rejected.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            throw new InvalidDataException("Daily note records do not contain collections.");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void VerifyHandle(FileStream stream, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException("Daily note file identity could not be verified.");
        var actual = buffer.ToString();
        if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual[4..];
        if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(path), PathComparison)
            || !GetFileInformationByHandle(stream.SafeFileHandle, out var information)
            || information.NumberOfLinks != 1
            || (information.FileAttributes & (uint)(FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Daily note file identity escaped its exact ordinary leaf.");
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
