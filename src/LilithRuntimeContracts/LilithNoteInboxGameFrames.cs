using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithGameBridgeContracts;

public sealed class LilithGameBridgeNoteInboxPollRequest : LilithGameBridgeClientRequest
{
    public const string PollMethod = "game.noteinbox.poll";
    public override string Method => PollMethod;
    public int WaitMilliseconds { get; }
    public LilithGameBridgeNoteInboxPollRequest(Guid generationId, Guid requestId, long sequence, int waitMilliseconds)
        : base(generationId, requestId, sequence)
    {
        if (waitMilliseconds is < 0 or > 30_000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        WaitMilliseconds = waitMilliseconds;
    }
}
public sealed class LilithGameBridgeNoteInboxCompleteRequest : LilithGameBridgeClientRequest
{
    public const string CompleteMethod = "game.noteinbox.complete";
    public override string Method => CompleteMethod;
    public LilithNoteInboxResult Result { get; }
    public LilithGameBridgeNoteInboxCompleteRequest(Guid generationId, Guid requestId, long sequence, LilithNoteInboxResult result)
        : base(generationId, requestId, sequence) => Result = result ?? throw new ArgumentNullException(nameof(result));
}
public sealed class LilithGameBridgeNoteInboxPollResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgeNoteInboxPollRequest.PollMethod;
    public Guid RequestId { get; }
    public bool Succeeded { get; }
    public LilithNoteInboxCommand? Command { get; }
    public string Code { get; }
    public LilithGameBridgeNoteInboxPollResponse(Guid generationId, Guid requestId, long sequence,
        bool succeeded, LilithNoteInboxCommand? command, string code) : base(generationId, sequence)
    {
        LilithNoteInboxProtocol.RequireId(requestId);
        LilithNoteInboxGameFrames.ValidateAck(succeeded, code);
        if (command is not null && (!succeeded || command.RuntimeGeneration != generationId))
            throw new ArgumentException("Note poll command authority rejected.");
        RequestId = requestId; Succeeded = succeeded; Command = command; Code = code;
    }
}
public sealed class LilithGameBridgeNoteInboxCompleteResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgeNoteInboxCompleteRequest.CompleteMethod;
    public Guid RequestId { get; }
    public Guid OperationId { get; }
    public Guid DeliveryId { get; }
    public bool Accepted { get; }
    public string Code { get; }
    public LilithGameBridgeNoteInboxCompleteResponse(Guid generationId, Guid requestId, long sequence,
        Guid operationId, Guid deliveryId, bool accepted, string code) : base(generationId, sequence)
    {
        LilithNoteInboxProtocol.RequireId(requestId); LilithNoteInboxProtocol.RequireId(operationId);
        LilithNoteInboxProtocol.RequireId(deliveryId); LilithNoteInboxGameFrames.ValidateAck(accepted, code);
        RequestId = requestId; OperationId = operationId; DeliveryId = deliveryId; Accepted = accepted; Code = code;
    }
}

internal static class LilithNoteInboxGameFrames
{
    internal static void ValidateAck(bool accepted, string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > 96 || code[0] is not (>= 'a' and <= 'z') || code.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not ('.' or '_'))
            || accepted != (code == "ok")) throw new ArgumentException("Note acknowledgement rejected.");
    }
    internal static bool TryWriteFields(Utf8JsonWriter writer, LilithGameBridgeFrame frame)
    {
        switch (frame)
        {
            case LilithGameBridgeNoteInboxPollRequest request:
                Common(writer, request.RequestId, request.Sequence, request.Method);
                writer.WriteNumber("waitMilliseconds", request.WaitMilliseconds); return true;
            case LilithGameBridgeNoteInboxCompleteRequest request:
                Common(writer, request.RequestId, request.Sequence, request.Method);
                writer.WritePropertyName("result"); LilithNoteInboxProtocol.EncodeResult(request.Result).WriteTo(writer); return true;
            case LilithGameBridgeNoteInboxPollResponse response:
                Common(writer, response.RequestId, response.Sequence, response.Method);
                writer.WriteBoolean("succeeded", response.Succeeded); writer.WriteString("code", response.Code);
                writer.WritePropertyName("command");
                if (response.Command is null) writer.WriteNullValue(); else LilithNoteInboxProtocol.EncodeCommand(response.Command).WriteTo(writer);
                return true;
            case LilithGameBridgeNoteInboxCompleteResponse response:
                Common(writer, response.RequestId, response.Sequence, response.Method);
                writer.WriteString("operationId", response.OperationId.ToString("N"));
                writer.WriteString("deliveryId", response.DeliveryId.ToString("N"));
                writer.WriteBoolean("accepted", response.Accepted); writer.WriteString("code", response.Code); return true;
            default: return false;
        }
    }
    private static void Common(Utf8JsonWriter writer, Guid requestId, long sequence, string method)
    {
        writer.WriteString("requestId", requestId.ToString("N")); writer.WriteNumber("sequence", sequence); writer.WriteString("method", method);
    }
    internal static LilithGameBridgeClientRequest ReadRequest(JsonElement root, bool poll)
    {
        LilithNoteInboxProtocol.Exact(root, poll
            ? new[] { "version", "kind", "generationId", "requestId", "sequence", "method", "waitMilliseconds" }
            : new[] { "version", "kind", "generationId", "requestId", "sequence", "method", "result" });
        Header(root, "game.request");
        return poll ? new LilithGameBridgeNoteInboxPollRequest(Id(root, "generationId"), Id(root, "requestId"), root.GetProperty("sequence").GetInt64(), root.GetProperty("waitMilliseconds").GetInt32())
            : new LilithGameBridgeNoteInboxCompleteRequest(Id(root, "generationId"), Id(root, "requestId"), root.GetProperty("sequence").GetInt64(), LilithNoteInboxProtocol.ReadResult(root.GetProperty("result")));
    }
    internal static LilithGameBridgeFrame ReadResponse(JsonElement root, bool poll)
    {
        LilithNoteInboxProtocol.Exact(root, poll
            ? new[] { "version", "kind", "generationId", "requestId", "sequence", "method", "succeeded", "command", "code" }
            : new[] { "version", "kind", "generationId", "requestId", "sequence", "method", "operationId", "deliveryId", "accepted", "code" });
        Header(root, "game.response");
        if (poll)
        {
            var command = root.GetProperty("command");
            return new LilithGameBridgeNoteInboxPollResponse(Id(root, "generationId"), Id(root, "requestId"), root.GetProperty("sequence").GetInt64(),
                root.GetProperty("succeeded").GetBoolean(), command.ValueKind == JsonValueKind.Null ? null : LilithNoteInboxProtocol.ReadCommand(command), LilithNoteInboxProtocol.String(root, "code"));
        }
        return new LilithGameBridgeNoteInboxCompleteResponse(Id(root, "generationId"), Id(root, "requestId"), root.GetProperty("sequence").GetInt64(),
            Id(root, "operationId"), Id(root, "deliveryId"), root.GetProperty("accepted").GetBoolean(), LilithNoteInboxProtocol.String(root, "code"));
    }
    private static Guid Id(JsonElement root, string name) => LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(root, name));
    private static void Header(JsonElement root, string kind)
    {
        if (root.GetProperty("version").GetInt32() != LilithGameBridgeProtocol.Version || LilithNoteInboxProtocol.String(root, "kind") != kind)
            throw new InvalidDataException("Note game envelope rejected.");
    }
}
