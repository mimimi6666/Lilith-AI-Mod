using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// A temporary input producer, not a Runtime/game-peer impersonator or a second
// tool permission system. The game main thread still owns every actual effect.
internal enum GameChatTestEndpointAction { Send, Stop, Status }
internal enum GameChatTestEndpointState
{
    Ready, Busy, Submitted, ProviderCompleted, Published, Failed,
    StopRequested, Interrupted, Rejected, Unknown
}

// Only an owned, still-leased test turn can retain this bounded capture result.
// It never contains a snapshot, message, card, square, identity or exception text.
internal enum GameChatTestMiniGameKind { None, OldMaid, Chess }
internal enum GameChatTestMiniGameSchema { None, PublicV1, LilithViewV2 }
internal enum GameChatTestMiniGameCaptureReason
{
    NoGame, NoMatch, Setup, Closing, DrawAnimation, PairReveal, DiscardDelay,
    ScaleAnimation, MoveAnimation, PublicOnlyReorder, PublicOnlyAwareness,
    Captured, ProjectionUnavailable, Exception
}
internal enum GameChatTestMiniGameException
{
    None, ArgumentException, InvalidDataException, InvalidOperationException,
    NullReferenceException, IndexOutOfRangeException, OverflowException,
    TypeLoadException, TypeInitializationException, MissingMethodException,
    FileLoadException, FileNotFoundException, Other
}

internal sealed class GameChatTestMiniGameCapture
{
    internal GameChatTestMiniGameKind GameKind { get; }
    internal GameChatTestMiniGameSchema Schema { get; }
    internal bool Present { get; }
    internal GameChatTestMiniGameCaptureReason Reason { get; }
    internal GameChatTestMiniGameException ExceptionType { get; }

    internal GameChatTestMiniGameCapture(GameChatTestMiniGameKind gameKind,
        GameChatTestMiniGameSchema schema, bool present, GameChatTestMiniGameCaptureReason reason,
        GameChatTestMiniGameException exceptionType = GameChatTestMiniGameException.None)
    {
        if (!Enum.IsDefined(typeof(GameChatTestMiniGameKind), gameKind)
            || !Enum.IsDefined(typeof(GameChatTestMiniGameSchema), schema)
            || !Enum.IsDefined(typeof(GameChatTestMiniGameCaptureReason), reason)
            || !Enum.IsDefined(typeof(GameChatTestMiniGameException), exceptionType)
            || present != (schema != GameChatTestMiniGameSchema.None)
            || (present && gameKind == GameChatTestMiniGameKind.None)
            || ((reason == GameChatTestMiniGameCaptureReason.Exception)
                != (exceptionType != GameChatTestMiniGameException.None)))
            throw new ArgumentException("Test capture metadata rejected.");
        GameKind = gameKind; Schema = schema; Present = present; Reason = reason; ExceptionType = exceptionType;
    }

    internal static GameChatTestMiniGameException ClassifyException(Exception exception) => exception switch
    {
        ArgumentException => GameChatTestMiniGameException.ArgumentException,
        InvalidDataException => GameChatTestMiniGameException.InvalidDataException,
        InvalidOperationException => GameChatTestMiniGameException.InvalidOperationException,
        NullReferenceException => GameChatTestMiniGameException.NullReferenceException,
        IndexOutOfRangeException => GameChatTestMiniGameException.IndexOutOfRangeException,
        OverflowException => GameChatTestMiniGameException.OverflowException,
        TypeLoadException => GameChatTestMiniGameException.TypeLoadException,
        TypeInitializationException => GameChatTestMiniGameException.TypeInitializationException,
        MissingMethodException => GameChatTestMiniGameException.MissingMethodException,
        FileLoadException => GameChatTestMiniGameException.FileLoadException,
        FileNotFoundException => GameChatTestMiniGameException.FileNotFoundException,
        _ => GameChatTestMiniGameException.Other
    };

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("present", Present);
        writer.WriteString("gameKind", GameKind switch
        { GameChatTestMiniGameKind.OldMaid => "old-maid", GameChatTestMiniGameKind.Chess => "chess", _ => "none" });
        writer.WriteString("schema", Schema switch
        { GameChatTestMiniGameSchema.PublicV1 => "public-v1", GameChatTestMiniGameSchema.LilithViewV2 => "lilith-view-v2", _ => "none" });
        writer.WriteString("reason", Reason switch
        {
            GameChatTestMiniGameCaptureReason.NoGame => "no-game", GameChatTestMiniGameCaptureReason.NoMatch => "no-match",
            GameChatTestMiniGameCaptureReason.Setup => "setup", GameChatTestMiniGameCaptureReason.Closing => "closing",
            GameChatTestMiniGameCaptureReason.DrawAnimation => "draw-animation", GameChatTestMiniGameCaptureReason.PairReveal => "pair-reveal",
            GameChatTestMiniGameCaptureReason.DiscardDelay => "discard-delay", GameChatTestMiniGameCaptureReason.ScaleAnimation => "scale-animation",
            GameChatTestMiniGameCaptureReason.MoveAnimation => "move-animation", GameChatTestMiniGameCaptureReason.PublicOnlyReorder => "public-only-reorder",
            GameChatTestMiniGameCaptureReason.PublicOnlyAwareness => "public-only-awareness", GameChatTestMiniGameCaptureReason.Captured => "captured",
            GameChatTestMiniGameCaptureReason.ProjectionUnavailable => "projection-unavailable", _ => "exception"
        });
        // Enum values are the entire whitelist; never serialize GetType/Message/InnerException.
        writer.WriteString("exceptionType", ExceptionType == GameChatTestMiniGameException.None ? "none" : ExceptionType.ToString());
        writer.WriteEndObject();
    }
}

internal sealed class GameChatTestEndpointOptions : IDisposable
{
    internal const string EnabledVariable = "LILITH_GAME_CHAT_TEST_ENABLED";
    internal const string TokenVariable = "LILITH_GAME_CHAT_TEST_TOKEN";
    internal const string ExpiryVariable = "LILITH_GAME_CHAT_TEST_EXPIRES_UTC";
    internal static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(30);
    private readonly byte[] _token;
    internal Guid RunId { get; } = Guid.NewGuid();
    internal int ProcessId { get; }
    internal DateTimeOffset ExpiresAtUtc { get; }
    internal TimeSpan Lifetime { get; }
    internal string PipeName => "lilith-game-chat-test-v1-" + ProcessId.ToString(CultureInfo.InvariantCulture);

    private GameChatTestEndpointOptions(byte[] token, int processId,
        DateTimeOffset now, DateTimeOffset expiry)
    { _token = token; ProcessId = processId; ExpiresAtUtc = expiry; Lifetime = expiry - now; }

    internal static GameChatTestEndpointOptions? TryRead(
        Func<string, string?> read, DateTimeOffset now, int processId)
    {
        if (processId <= 0 || read(EnabledVariable) != "1") return null;
        if (!DateTimeOffset.TryParseExact(read(ExpiryVariable), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.Offset != TimeSpan.Zero
            || expiry <= now || expiry - now > MaximumLifetime) return null;
        var token = DecodeToken(read(TokenVariable));
        return token == null ? null : new GameChatTestEndpointOptions(token, processId, now, expiry);
    }

    internal bool Authenticate(string token)
    {
        var candidate = DecodeToken(token);
        if (candidate == null) return false;
        try { return CryptographicOperations.FixedTimeEquals(_token, candidate); }
        finally { CryptographicOperations.ZeroMemory(candidate); }
    }

    private static byte[]? DecodeToken(string? value)
    {
        if (value == null || value.Length != 64) return null;
        try { return Convert.FromHexString(value); }
        catch (FormatException) { return null; }
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(_token);
}

internal sealed class GameChatTestEndpointRequest
{
    internal string Token { get; }
    internal Guid RequestId { get; }
    internal GameChatTestEndpointAction Action { get; }
    internal Guid TargetId { get; }
    internal string Text { get; }
    internal GameChatTestEndpointRequest(string token, Guid requestId,
        GameChatTestEndpointAction action, Guid targetId, string text)
    { Token = token; RequestId = requestId; Action = action; TargetId = targetId; Text = text; }
}

internal sealed class GameChatTestEndpointReceipt
{
    internal GameChatTestEndpointState State { get; }
    internal string Code { get; }
    internal string Text { get; }
    internal string OperationId { get; }
    internal bool TextTruncated { get; }
    internal GameChatTestMiniGameCapture? MiniGameCapture { get; }
    internal LilithMiniGameDiagnosticReceipt? MiniGameDiagnostic { get; }
    internal GameChatTestEndpointReceipt(GameChatTestEndpointState state, string code,
        string text = "", string operationId = "", bool alreadyTruncated = false,
        GameChatTestMiniGameCapture? miniGameCapture = null,
        LilithMiniGameDiagnosticReceipt? miniGameDiagnostic = null)
    {
        if (!Enum.IsDefined(typeof(GameChatTestEndpointState), state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (!SafeCode(code, 96) || !SafeCode(operationId, 128, allowEmpty: true))
            throw new ArgumentException("Test receipt metadata rejected.");
        State = state; Code = code; OperationId = operationId;
        Text = GameChatTestEndpointCodec.BoundReply(text, out var truncated);
        TextTruncated = truncated || alreadyTruncated;
        MiniGameCapture = miniGameCapture;
        MiniGameDiagnostic = miniGameDiagnostic;
    }
    private static bool SafeCode(string value, int maximum, bool allowEmpty = false)
    {
        if (value == null || value.Length > maximum || (!allowEmpty && value.Length == 0)) return false;
        foreach (var character in value)
            if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                    or '_' or '-' or '.')) return false;
        return true;
    }
}

internal static class GameChatTestEndpointCodec
{
    internal const int MaximumFrameBytes = 128 * 1024;
    internal const int MaximumInputCharacters = 4096;
    internal const int MaximumReplyUtf8Bytes = 16 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static GameChatTestEndpointRequest Decode(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumFrameBytes) throw new InvalidDataException("Test frame rejected.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Test object required.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in root.EnumerateObject())
            if (!keys.Add(member.Name)) throw new InvalidDataException("Duplicate test member.");
        if (!keys.SetEquals(new[] { "version", "token", "requestId", "action", "targetId", "text" })
            || root.GetProperty("version").ValueKind != JsonValueKind.Number
            || !root.GetProperty("version").TryGetInt32(out var version) || version != 1)
            throw new InvalidDataException("Test schema rejected.");
        string Read(string name) => root.GetProperty(name).ValueKind == JsonValueKind.String
            ? root.GetProperty(name).GetString()! : throw new InvalidDataException("Test string required.");
        var token = Read("token");
        if (token.Length != 64) throw new InvalidDataException("Test credential shape rejected.");
        var requestId = Identity(Read("requestId"), false);
        var target = Identity(Read("targetId"), true);
        var action = Read("action") switch
        {
            "send" => GameChatTestEndpointAction.Send,
            "stop" => GameChatTestEndpointAction.Stop,
            "status" => GameChatTestEndpointAction.Status,
            _ => throw new InvalidDataException("Test action rejected.")
        };
        var text = Read("text");
        if (text.Length > MaximumInputCharacters) throw new InvalidDataException("Test input too long.");
        _ = Utf8.GetByteCount(text); // Reject invalid Unicode rather than silently rewriting a test message.
        foreach (var character in text)
            if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                throw new InvalidDataException("Test control character rejected.");
        if (action == GameChatTestEndpointAction.Send)
        {
            if (target != Guid.Empty || string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Test send shape rejected.");
        }
        else if (text.Length != 0 || (action == GameChatTestEndpointAction.Stop && target == Guid.Empty))
            throw new InvalidDataException("Test control shape rejected.");
        return new GameChatTestEndpointRequest(token, requestId, action, target, text);
    }

    private static Guid Identity(string text, bool allowEmpty)
    {
        if (allowEmpty && text.Length == 0) return Guid.Empty;
        if (text.Length != 32 || !Guid.TryParseExact(text, "N", out var value) || value == Guid.Empty)
            throw new InvalidDataException("Test identity rejected.");
        return value;
    }

    internal static byte[] Encode(Guid runId, Guid requestId, Guid targetId, GameChatTestEndpointReceipt receipt)
    {
        var state = receipt.State switch
        {
            GameChatTestEndpointState.Ready => "ready", GameChatTestEndpointState.Busy => "busy",
            GameChatTestEndpointState.Submitted => "submitted", GameChatTestEndpointState.ProviderCompleted => "providerCompleted",
            GameChatTestEndpointState.Published => "published", GameChatTestEndpointState.Failed => "failed",
            GameChatTestEndpointState.StopRequested => "stop_requested", GameChatTestEndpointState.Interrupted => "interrupted",
            GameChatTestEndpointState.Rejected => "rejected", _ => "unknown"
        };
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1); writer.WriteBoolean("test", true);
            writer.WriteString("runId", runId.ToString("N")); writer.WriteString("requestId", requestId.ToString("N"));
            writer.WriteString("targetId", targetId == Guid.Empty ? "" : targetId.ToString("N")); writer.WriteString("state", state);
            writer.WriteString("code", receipt.Code); writer.WriteString("text", receipt.Text); writer.WriteString("operationId", receipt.OperationId);
            writer.WriteBoolean("textTruncated", receipt.TextTruncated); writer.WriteString("historyEffect", "real_f7_conversation");
            if (receipt.MiniGameCapture != null)
            {
                writer.WritePropertyName("miniGameCapture");
                receipt.MiniGameCapture.Write(writer);
            }
            if (receipt.MiniGameDiagnostic != null)
            {
                writer.WritePropertyName("miniGameDiagnostic");
                receipt.MiniGameDiagnostic.Write(writer);
            }
            writer.WriteEndObject();
        }
        var result = output.ToArray();
        if (result.Length > MaximumFrameBytes) throw new InvalidDataException("Test receipt too large.");
        return result;
    }

    internal static string BoundReply(string text, out bool truncated)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));
        var bytes = Utf8.GetBytes(text);
        truncated = bytes.Length > MaximumReplyUtf8Bytes;
        if (!truncated) return text;
        var end = MaximumReplyUtf8Bytes;
        while ((bytes[end] & 0xC0) == 0x80) end--;
        return Utf8.GetString(bytes, 0, end);
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumFrameBytes) throw new InvalidDataException("Test frame length rejected.");
        var bytes = new byte[length];
        await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }
    internal static async Task WriteFrameAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumFrameBytes) throw new InvalidDataException("Test frame length rejected.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    private static async Task ReadExactlyAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException();
            offset += count;
        }
    }
}

internal sealed class GameChatTestEndpointCommand
{
    internal Guid RequestId { get; }
    internal GameChatTestEndpointAction Action { get; }
    internal Guid TargetId { get; }
    internal string Text { get; private set; }
    internal TaskCompletionSource<GameChatTestEndpointReceipt> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal long Deadline { get; }
    internal int Phase { get; set; } // 0 queued, 1 dispatched, 2 closed; guarded by broker lock.
    internal GameChatTestEndpointCommand(GameChatTestEndpointRequest request, long deadline)
    { RequestId = request.RequestId; Action = request.Action; TargetId = request.TargetId; Text = request.Text; Deadline = deadline; }
    internal void ClearText() => Text = string.Empty;
}

internal sealed class GameChatTestEndpointBroker : IDisposable
{
    internal const int MaximumSends = 16;
    internal const int MaximumRequests = 256;
    internal const int MaximumPending = 8;
    internal const long DispatchDeadlineMilliseconds = 2000;
    private readonly object _gate = new();
    private readonly GameChatTestEndpointOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<long> _milliseconds;
    private readonly long _expires;
    private readonly Queue<GameChatTestEndpointCommand> _queue = new();
    private readonly HashSet<Guid> _seen = new();
    private readonly HashSet<Guid> _owned = new();
    private readonly Dictionary<Guid, GameChatTestEndpointCommand> _pending = new();
    private bool _disposed;
    internal GameChatTestEndpointBroker(GameChatTestEndpointOptions options,
        Func<DateTimeOffset>? clock = null, Func<long>? milliseconds = null)
    {
        _options = options; _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var stopwatch = Stopwatch.StartNew();
        _milliseconds = milliseconds ?? (() => stopwatch.ElapsedMilliseconds);
        _expires = _milliseconds() + (long)options.Lifetime.TotalMilliseconds;
    }
    internal bool IsActive { get { lock (_gate) return Active(); } }
    private bool Active() => !_disposed && _clock() < _options.ExpiresAtUtc && _milliseconds() < _expires;
    private static GameChatTestEndpointReceipt Reject(string code) => new(GameChatTestEndpointState.Rejected, code);

    internal Task<GameChatTestEndpointReceipt> Enqueue(GameChatTestEndpointRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!Active()) return Task.FromResult(Reject("test.expired"));
            if (!_options.Authenticate(request.Token)) return Task.FromResult(Reject("test.unauthorized"));
            if (_seen.Contains(request.RequestId)) return Task.FromResult(Reject("test.duplicate_request"));
            // Leave one bounded cancellation allowance per maximum send even
            // after status polling exhausts the normal request budget.
            if (_seen.Count >= MaximumRequests
                && (request.Action != GameChatTestEndpointAction.Stop || !_owned.Contains(request.TargetId)
                    || _seen.Count >= MaximumRequests + MaximumSends))
                return Task.FromResult(Reject("test.request_capacity"));
            _seen.Add(request.RequestId); // Consumed even if refused; never silently retry a send.
            if (cancellationToken.IsCancellationRequested) return Task.FromResult(Reject("test.cancelled_before_dispatch"));
            if (_pending.Count >= MaximumPending) return Task.FromResult(Reject("test.queue_capacity"));
            if (request.Action == GameChatTestEndpointAction.Send)
            {
                if (_owned.Count >= MaximumSends) return Task.FromResult(Reject("test.send_capacity"));
                _owned.Add(request.RequestId);
            }
            else if (request.TargetId != Guid.Empty && !_owned.Contains(request.TargetId))
                return Task.FromResult(Reject("test.target_not_owned"));
            var command = new GameChatTestEndpointCommand(request, _milliseconds() + DispatchDeadlineMilliseconds);
            _pending.Add(command.RequestId, command); _queue.Enqueue(command);
            return AwaitCommandAsync(command, cancellationToken);
        }
    }

    private async Task<GameChatTestEndpointReceipt> AwaitCommandAsync(GameChatTestEndpointCommand command, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => Cancel(command));
        return await command.Completion.Task.ConfigureAwait(false);
    }
    private void Cancel(GameChatTestEndpointCommand command)
    {
        lock (_gate)
        {
            if (!_pending.Remove(command.RequestId)) return;
            var dispatched = command.Phase == 1;
            command.Phase = 2;
            // Main-thread admission may have just claimed this immutable input.
            // Do not replace it with empty text between dequeue and SubmitAiInput.
            if (!dispatched) command.ClearText();
            command.Completion.TrySetResult(new GameChatTestEndpointReceipt(
                dispatched ? GameChatTestEndpointState.Unknown : GameChatTestEndpointState.Rejected,
                dispatched ? "test.outcome_unknown_use_status" : "test.cancelled_before_dispatch"));
        }
    }

    // Only the Plugin's Unity Update caller may consume this queue.
    internal bool TryDequeue(out GameChatTestEndpointCommand? command)
    {
        lock (_gate)
        {
            command = null;
            while (_queue.Count != 0)
            {
                var next = _queue.Dequeue();
                if (next.Phase != 0) continue;
                if (!Active() || _milliseconds() >= next.Deadline)
                {
                    _pending.Remove(next.RequestId); next.Phase = 2; next.ClearText();
                    next.Completion.TrySetResult(Reject(Active() ? "test.dispatch_timeout" : "test.expired"));
                    continue;
                }
                next.Phase = 1; command = next; return true;
            }
            return false;
        }
    }

    internal bool Complete(GameChatTestEndpointCommand command, GameChatTestEndpointReceipt receipt)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(command.RequestId, out var owned) || !ReferenceEquals(owned, command)
                || command.Phase != 1) return false;
            // Endpoint status never carries ordinary chat text or operation IDs.
            if (command.Action == GameChatTestEndpointAction.Status && command.TargetId == Guid.Empty
                && (receipt.Text.Length != 0 || receipt.OperationId.Length != 0 || receipt.MiniGameCapture != null
                    || receipt.MiniGameDiagnostic != null))
                receipt = Reject("test.endpoint_status_content_rejected");
            _pending.Remove(command.RequestId); command.Phase = 2; command.ClearText();
            return command.Completion.TrySetResult(receipt);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            foreach (var command in _pending.Values)
            {
                var dispatched = command.Phase == 1; command.Phase = 2;
                if (!dispatched) command.ClearText();
                command.Completion.TrySetResult(new GameChatTestEndpointReceipt(
                    dispatched ? GameChatTestEndpointState.Unknown : GameChatTestEndpointState.Rejected,
                    dispatched ? "test.outcome_unknown_use_status" : "test.closed_before_dispatch"));
            }
            _pending.Clear(); _queue.Clear(); _owned.Clear(); _seen.Clear(); _options.Dispose();
        }
    }
}
