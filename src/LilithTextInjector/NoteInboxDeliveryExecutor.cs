using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithTextInjector;

// Synchronous by design: the owning Unity dispatcher supplies the original sink
// and repeats its own policy checks. This class never chooses a renderer or path.
internal sealed class NoteInboxDeliveryExecutor
{
    internal const long MaximumArtifactBytes = 8L * 1024 * 1024;
    private readonly NoteInboxDeliveryStore _store;
    private readonly string _nativeRoot;
    private readonly Func<string, string> _saveNativeNote;
    private readonly Func<DateTimeOffset> _utcNow;

    internal NoteInboxDeliveryExecutor(NoteInboxDeliveryStore store, string trustedNativeRoot,
        Func<string, string> saveNativeNote, Func<DateTimeOffset>? utcNow = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _nativeRoot = NoteInboxDeliveryPaths.CanonicalDirectory(trustedNativeRoot);
        _saveNativeNote = saveNativeNote ?? throw new ArgumentNullException(nameof(saveNativeNote));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal LilithNoteInboxResult Query(LilithNoteInboxCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return QueryCore(ForManual(command));
    }

    internal LilithNoteInboxResult Query(LilithDailyNoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return QueryCore(ForDaily(command));
    }

    private LilithNoteInboxResult QueryCore(DeliveryOperation command)
    {
        try
        {
            var record = _store.Read(command.DeliveryId);
            return record == null ? Result(command, LilithNoteInboxStatus.NotFound, "note.not_found") : Existing(command, record);
        }
        catch { return Result(command, LilithNoteInboxStatus.Unknown, "note.receipt_unavailable"); }
    }

    internal LilithNoteInboxResult Execute(LilithNoteInboxCommand command, Func<bool> mayWrite)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteCore(ForManual(command), mayWrite);
    }

    internal LilithNoteInboxResult Execute(LilithDailyNoteCommand command, Func<bool> mayWrite)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteCore(ForDaily(command), mayWrite);
    }

    private LilithNoteInboxResult ExecuteCore(DeliveryOperation command, Func<bool> mayWrite)
    {
        ArgumentNullException.ThrowIfNull(mayWrite);
        if (command.Action == LilithNoteInboxAction.Status) return QueryCore(command);
        var hash = command.PayloadSha256;
        NoteInboxDeliveryRecord prepared;
        try
        {
            var record = _store.Read(command.DeliveryId);
            if (record != null) return Existing(command, record, hash);
            if (!IsAdmitted(command, mayWrite)) return Result(command, LilithNoteInboxStatus.Deferred, "note.not_admitted");
            NoteInboxDeliveryPaths.CheckDirectory(_nativeRoot);
            if (!_store.TryReserve(command.DeliveryId, hash, out prepared)) return Existing(command, prepared, hash);
        }
        catch
        {
            // A failed reservation must not be assumed absent: after a publish
            // boundary or corrupt record, only a positive absence proves no call.
            try
            {
                if (_store.Read(command.DeliveryId) == null)
                    return Result(command, LilithNoteInboxStatus.Failed, "note.prepare_failed");
            }
            catch { }
            return Result(command, LilithNoteInboxStatus.Unknown, "note.prepare_unknown");
        }
        if (!IsAdmitted(command, mayWrite))
            return CompleteOrUnknown(command, prepared, Result(command, LilithNoteInboxStatus.Deferred, "note.not_admitted"));
        try
        {
            // Once this call begins, even a thrown exception may follow a write.
            var returnedPath = _saveNativeNote(command.Text!);
            var completedAt = _utcNow().ToUniversalTime();
            var path = VerifyReturnedPath(returnedPath);
            byte[] bytes;
            using (var stream = NoteInboxDeliveryPaths.OpenVerifiedRead(path))
            {
                if (stream.Length <= 0 || stream.Length > MaximumArtifactBytes) throw new InvalidDataException("Note image length rejected.");
                bytes = new byte[checked((int)stream.Length)];
                ReadExactly(stream, bytes, bytes.Length);
                if (stream.ReadByte() != -1) throw new InvalidDataException("Note image changed length.");
            }
            ValidatePng(bytes);
            var artifact = new LilithNoteInboxArtifact(command.DeliveryId, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, completedAt);
            return CompleteOrUnknown(command, prepared, Result(command, LilithNoteInboxStatus.Delivered, "ok", artifact), Path.GetFileName(path));
        }
        catch
        {
            return CompleteOrUnknown(command, prepared, Result(command, LilithNoteInboxStatus.Unknown, "note.write_unknown"));
        }
    }

    private LilithNoteInboxResult CompleteOrUnknown(DeliveryOperation command, NoteInboxDeliveryRecord prepared,
        LilithNoteInboxResult result, string leaf = "")
    {
        try { _store.Complete(prepared, result, leaf); return result; }
        catch { return Result(command, LilithNoteInboxStatus.Unknown, "note.receipt_unavailable"); }
    }

    private bool IsAdmitted(DeliveryOperation command, Func<bool> mayWrite)
    {
        try { return _utcNow() < command.ExpiresAtUtc && mayWrite(); }
        catch { return false; }
    }

    private string VerifyReturnedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Native sink returned no absolute image identity.");
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), _nativeRoot, NoteInboxDeliveryPaths.Comparison))
            throw new InvalidDataException("Native sink returned an unexpected directory.");
        NoteInboxDeliveryPaths.RequirePngLeaf(Path.GetFileName(full));
        NoteInboxDeliveryPaths.CheckDirectory(_nativeRoot);
        return full;
    }

    internal static string PayloadHash(LilithNoteInboxCommand command)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, origin_turn_id = command.OriginTurnId, client_turn_id = command.ClientTurnId.ToString("N"),
            text = command.Text, locale = command.Locale
        })));

    internal static string PayloadHash(LilithDailyNoteCommand command)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, purpose = "lilith.daily-note.delivery.v1", profile_id = command.Scope.ProfileId,
            goal_id = command.Scope.GoalId.ToString("N"), attempt_id = command.Scope.AttemptId.ToString("N"),
            delivery_id = command.Scope.DeliveryId.ToString("N"), operation_id = command.Scope.OperationId.ToString("N"),
            text = command.Text, locale = command.Locale
        })));

    private static DeliveryOperation ForManual(LilithNoteInboxCommand command)
        => new(command.OperationId, command.DeliveryId, command.Action, command.Text, command.ExpiresAtUtc,
            command.Action == LilithNoteInboxAction.Deliver ? PayloadHash(command) : string.Empty, false);

    private static DeliveryOperation ForDaily(LilithDailyNoteCommand command)
        => new(command.Scope.OperationId, command.Scope.DeliveryId, command.Action, command.Text, command.Scope.ExpiresAtUtc,
            command.Action == LilithNoteInboxAction.Deliver ? PayloadHash(command) : string.Empty, true);

    private static LilithNoteInboxResult Existing(DeliveryOperation command, NoteInboxDeliveryRecord record, string? expectedHash = null)
    {
        if (expectedHash != null && record.PayloadSha256 != expectedHash)
            return Result(command, LilithNoteInboxStatus.Rejected, "note.payload_conflict");
        if (command.PreserveRecordedOperation && record.Terminal is { } terminal)
            return terminal.OperationId == command.OperationId && terminal.DeliveryId == command.DeliveryId
                ? terminal : Result(command, LilithNoteInboxStatus.Unknown, "daily.receipt_identity_mismatch");
        return record.Terminal == null ? Result(command, LilithNoteInboxStatus.Unknown, "note.prepared_unknown")
            : Result(command, record.Terminal.Status, record.Terminal.Code, record.Terminal.Artifact);
    }

    private static LilithNoteInboxResult Result(DeliveryOperation command, LilithNoteInboxStatus status,
        string code, LilithNoteInboxArtifact? artifact = null)
        => new(command.OperationId, command.DeliveryId, status, code, artifact);

    private sealed record DeliveryOperation(Guid OperationId, Guid DeliveryId, LilithNoteInboxAction Action,
        string? Text, DateTimeOffset ExpiresAtUtc, string PayloadSha256, bool PreserveRecordedOperation);

    // Validate a bounded, non-interlaced native PNG, including chunk CRCs and
    // decoded scanline length. No Unity/image library or player text is loaded.
    private static void ValidatePng(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(signature)) throw new InvalidDataException("PNG signature rejected.");
        var offset = 8;
        var width = 0;
        var height = 0;
        var channels = 0;
        var header = false;
        var end = false;
        var dataSeen = false;
        var dataClosed = false;
        using var compressed = new MemoryStream();
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > MaximumArtifactBytes || length > bytes.Length - offset - 12) throw new InvalidDataException("PNG chunk length rejected.");
            var count = (int)length;
            var kind = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var content = bytes.AsSpan(offset + 8, count);
            if (Crc32(bytes.AsSpan(offset + 4, count + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + count, 4)))
                throw new InvalidDataException("PNG checksum rejected.");
            if (!header && kind != "IHDR") throw new InvalidDataException("PNG header missing.");
            if (kind == "IHDR")
            {
                if (header || count != 13) throw new InvalidDataException("PNG header rejected.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(content));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4)));
                channels = content[9] switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || channels == 0
                    || content[8] != 8 || content[10] != 0 || content[11] != 0 || content[12] != 0
                    || (long)(width * channels + 1) * height > 64 * 1024 * 1024)
                    throw new InvalidDataException("Native PNG format is unsupported or unbounded.");
                header = true;
            }
            else if (kind == "IDAT")
            {
                if (dataClosed) throw new InvalidDataException("Noncontiguous PNG image data.");
                dataSeen = true;
                compressed.Write(content);
            }
            else if (kind == "IEND")
            {
                if (count != 0 || offset + 12 != bytes.Length || compressed.Length == 0) throw new InvalidDataException("PNG ending rejected.");
                end = true;
            }
            else
            {
                if (kind[0] is >= 'A' and <= 'Z') throw new InvalidDataException("Unsupported critical PNG chunk.");
                if (dataSeen) dataClosed = true;
            }
            offset += count + 12;
        }
        if (!end) throw new InvalidDataException("PNG ending missing.");
        compressed.Position = 0;
        using var inflated = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked(width * channels)];
        for (var y = 0; y < height; y++)
        {
            var filter = inflated.ReadByte();
            if (filter < 0 || filter > 4) throw new InvalidDataException("PNG scanline rejected.");
            ReadExactly(inflated, row, row.Length);
        }
        if (inflated.ReadByte() != -1) throw new InvalidDataException("PNG decoded length rejected.");
    }

    private static void ReadExactly(Stream stream, byte[] bytes, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(bytes, offset, count - offset);
            if (read <= 0) throw new InvalidDataException("Truncated note artifact.");
            offset += read;
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        }
        return ~crc;
    }
}
