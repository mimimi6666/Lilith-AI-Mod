using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;
using Microsoft.Win32.SafeHandles;

namespace LilithTextInjector;

internal sealed class NoteInboxDeliveryRecord
{
    internal Guid DeliveryId { get; }
    internal string PayloadSha256 { get; }
    internal LilithNoteInboxResult? Terminal { get; }
    internal NoteInboxDeliveryRecord(Guid id, string hash, LilithNoteInboxResult? terminal = null)
    { DeliveryId = id; PayloadSha256 = hash; Terminal = terminal; }
}

// Two immutable, independently published records. A surviving Prepared without
// a terminal receipt is deliberately Unknown: it never grants another write.
internal sealed class NoteInboxDeliveryStore
{
    internal const int MaximumReceiptBytes = 16 * 1024;
    private readonly string _directory;
    private readonly Action<string>? _beforePublish;

    internal NoteInboxDeliveryStore(string trustedReceiptDirectory, Action<string>? beforePublish = null)
    {
        _directory = NoteInboxDeliveryPaths.CanonicalDirectory(trustedReceiptDirectory);
        _beforePublish = beforePublish;
    }

    internal NoteInboxDeliveryRecord? Read(Guid deliveryId)
    {
        var prepared = ReceiptPath(deliveryId, "prepared");
        var terminal = ReceiptPath(deliveryId, "result");
        NoteInboxDeliveryPaths.CheckDirectory(_directory);
        if (!File.Exists(prepared))
        {
            if (File.Exists(terminal) || Directory.Exists(prepared) || Directory.Exists(terminal))
                throw new InvalidDataException("Orphaned note receipt.");
            return null;
        }
        using var intentDocument = ReadJson(prepared);
        var intent = intentDocument.RootElement;
        LilithNoteInboxProtocol.Exact(intent, "schema", "delivery_id", "payload_sha256");
        RequireHeader(intent, deliveryId);
        var hash = LilithNoteInboxProtocol.String(intent, "payload_sha256");
        RequireHash(hash);
        if (!File.Exists(terminal))
        {
            if (Directory.Exists(terminal)) throw new InvalidDataException("Invalid note receipt target.");
            return new(deliveryId, hash);
        }
        using var resultDocument = ReadJson(terminal);
        var body = resultDocument.RootElement;
        LilithNoteInboxProtocol.Exact(body, "schema", "delivery_id", "payload_sha256", "native_leaf", "result");
        RequireHeader(body, deliveryId);
        if (LilithNoteInboxProtocol.String(body, "payload_sha256") != hash)
            throw new InvalidDataException("Note receipt binding changed.");
        var result = LilithNoteInboxProtocol.ReadResult(body.GetProperty("result"));
        if (result.DeliveryId != deliveryId || result.Status == LilithNoteInboxStatus.NotFound)
            throw new InvalidDataException("Note terminal identity rejected.");
        var leaf = LilithNoteInboxProtocol.String(body, "native_leaf");
        if (result.Status == LilithNoteInboxStatus.Delivered)
        {
            NoteInboxDeliveryPaths.RequirePngLeaf(leaf);
            if (result.Artifact!.ByteLength > NoteInboxDeliveryExecutor.MaximumArtifactBytes)
                throw new InvalidDataException("Note artifact is unbounded.");
        }
        else if (leaf.Length != 0) throw new InvalidDataException("Non-delivery has an artifact path.");
        return new(deliveryId, hash, result);
    }

    internal bool TryReserve(Guid deliveryId, string payloadSha256, out NoteInboxDeliveryRecord record)
    {
        RequireHash(payloadSha256);
        var existing = Read(deliveryId);
        if (existing != null) { record = existing; return false; }
        NoteInboxDeliveryPaths.CheckDirectory(_directory);
        Directory.CreateDirectory(_directory);
        NoteInboxDeliveryPaths.CheckDirectory(_directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        { schema = 1, delivery_id = deliveryId.ToString("N"), payload_sha256 = payloadSha256 });
        try { PublishNew(ReceiptPath(deliveryId, "prepared"), bytes, "prepared"); }
        catch (IOException) when (File.Exists(ReceiptPath(deliveryId, "prepared")))
        {
            record = Read(deliveryId) ?? throw new InvalidDataException("Note reservation disappeared.");
            return false;
        }
        record = new(deliveryId, payloadSha256);
        return true;
    }

    internal void Complete(NoteInboxDeliveryRecord prepared, LilithNoteInboxResult result, string nativeLeaf = "")
    {
        if (result.DeliveryId != prepared.DeliveryId || result.Status == LilithNoteInboxStatus.NotFound)
            throw new InvalidDataException("Note completion identity rejected.");
        var current = Read(prepared.DeliveryId);
        if (current == null || current.PayloadSha256 != prepared.PayloadSha256 || current.Terminal != null)
            throw new InvalidDataException("Note reservation no longer owns completion.");
        if (result.Status == LilithNoteInboxStatus.Delivered)
            NoteInboxDeliveryPaths.RequirePngLeaf(nativeLeaf);
        else if (nativeLeaf.Length != 0) throw new InvalidDataException("Only a delivered note has a native leaf.");
        PublishNew(ReceiptPath(prepared.DeliveryId, "result"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = 1, delivery_id = prepared.DeliveryId.ToString("N"), payload_sha256 = prepared.PayloadSha256,
            native_leaf = nativeLeaf, result = LilithNoteInboxProtocol.EncodeResult(result)
        }), "result");
    }

    private string ReceiptPath(Guid id, string suffix)
    {
        if (id == Guid.Empty) throw new ArgumentException("A delivery identity is required.");
        return Path.Combine(_directory, id.ToString("N") + "." + suffix + ".json");
    }

    private static void RequireHeader(JsonElement body, Guid expected)
    {
        if (body.GetProperty("schema").GetInt32() != 1
            || LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(body, "delivery_id")) != expected)
            throw new InvalidDataException("Note receipt schema or identity rejected.");
    }

    private static void RequireHash(string value)
    {
        if (value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new InvalidDataException("An exact note payload digest is required.");
    }

    private static JsonDocument ReadJson(string path)
    {
        using var stream = NoteInboxDeliveryPaths.OpenVerifiedRead(path);
        if (stream.Length <= 0 || stream.Length > MaximumReceiptBytes)
            throw new InvalidDataException("Note receipt length rejected.");
        var bytes = new byte[checked((int)stream.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read <= 0) throw new InvalidDataException("Truncated note receipt.");
            offset += read;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Note receipt grew.");
        return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
    }

    private void PublishNew(string destination, byte[] bytes, string phase)
    {
        if (bytes.Length > MaximumReceiptBytes) throw new InvalidDataException("Note receipt is unbounded.");
        NoteInboxDeliveryPaths.CheckDirectory(_directory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                NoteInboxDeliveryPaths.VerifyOpenPath(stream, temporary);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            _beforePublish?.Invoke(phase);
            NoteInboxDeliveryPaths.CheckDirectory(_directory);
            File.Move(temporary, destination); // no overwrite: one winner across store instances
        }
        finally
        {
            // Only our exact, generated direct-child temporary is removable.
            NoteInboxDeliveryPaths.CheckDirectory(_directory);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

internal static class NoteInboxDeliveryPaths
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string CanonicalDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new ArgumentException("A trusted local note directory is required.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!), Comparison))
            throw new ArgumentException("A note directory cannot be a filesystem root.");
        return full;
    }

    internal static void CheckDirectory(string directory)
    {
        for (var current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!Directory.Exists(current) && !File.Exists(current)) continue;
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new InvalidDataException("Note directory is not an ordinary local directory.");
        }
    }

    internal static void RequirePngLeaf(string leaf)
    {
        if (string.IsNullOrWhiteSpace(leaf) || leaf != Path.GetFileName(leaf)
            || leaf.IndexOfAny(new[] { ':', '/', '\\', '\0' }) >= 0
            || !string.Equals(Path.GetExtension(leaf), ".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native note leaf rejected.");
    }

    internal static FileStream OpenVerifiedRead(string path)
    {
        CheckDirectory(Path.GetDirectoryName(path)!);
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Note file is not an ordinary file.");
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            VerifyOpenPath(stream, path);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static void VerifyOpenPath(FileStream stream, string path)
    {
        if (OperatingSystem.IsWindows())
            {
                var buffer = new StringBuilder(32768);
                var length = GetFinalPathNameByHandle(stream.SafeFileHandle, buffer, buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity) throw new IOException("Note file handle could not be attested.");
                var actual = buffer.ToString();
                if (actual.StartsWith("\\\\?\\", StringComparison.Ordinal)) actual = actual.Substring(4);
                if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(path), Comparison))
                    throw new InvalidDataException("Note file handle escaped the exact target.");
            }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
}
