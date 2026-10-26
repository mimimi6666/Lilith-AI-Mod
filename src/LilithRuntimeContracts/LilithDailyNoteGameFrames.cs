using System;
using System.IO;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithGameBridgeContracts;

public sealed class LilithGameBridgeDailyNoteOpportunityRequest : LilithGameBridgeClientRequest
{
    public const string OpportunityMethod = "game.daily_note.opportunity";
    public override string Method => OpportunityMethod;
    public LilithDailyNoteOpportunity Opportunity { get; }
    public LilithGameBridgeDailyNoteOpportunityRequest(Guid generationId, Guid requestId, long sequence, LilithDailyNoteOpportunity opportunity)
        : base(generationId, requestId, sequence) => Opportunity = opportunity ?? throw new ArgumentNullException(nameof(opportunity));
}

public sealed class LilithGameBridgeDailyNotePollRequest : LilithGameBridgeClientRequest
{
    public const string PollMethod = "game.daily_note.poll";
    public override string Method => PollMethod;
    public string ProfileId { get; }
    public Guid GameEpoch { get; }
    public int WaitMilliseconds { get; }
    public LilithGameBridgeDailyNotePollRequest(Guid generationId, Guid requestId, long sequence, string profileId, Guid gameEpoch, int waitMilliseconds)
        : base(generationId, requestId, sequence)
    {
        LilithDailyNoteProtocol.RequireProfile(profileId); LilithNoteInboxProtocol.RequireId(gameEpoch);
        if (waitMilliseconds is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        ProfileId = profileId; GameEpoch = gameEpoch; WaitMilliseconds = waitMilliseconds;
    }
}

public sealed class LilithGameBridgeDailyNoteCompleteRequest : LilithGameBridgeClientRequest
{
    public const string CompleteMethod = "game.daily_note.complete";
    public override string Method => CompleteMethod;
    public LilithDailyNoteScope Scope { get; }
    public LilithNoteInboxResult Result { get; }
    public LilithGameBridgeDailyNoteCompleteRequest(Guid generationId, Guid requestId, long sequence, LilithDailyNoteScope scope, LilithNoteInboxResult result)
        : base(generationId, requestId, sequence)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope)); Result = result ?? throw new ArgumentNullException(nameof(result));
        if (scope.RuntimeGeneration != generationId) throw new ArgumentException("Daily completion generation rejected.");
        LilithDailyNoteProtocol.RequireResult(scope, result);
    }
}

public sealed class LilithGameBridgeDailyNoteOpportunityResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod;
    public Guid RequestId { get; }
    public bool Accepted { get; }
    public string Code { get; }
    public LilithGameBridgeDailyNoteOpportunityResponse(Guid generationId, Guid requestId, long sequence, bool accepted, string code)
        : base(generationId, sequence)
    { LilithNoteInboxProtocol.RequireId(requestId); LilithNoteInboxGameFrames.ValidateAck(accepted, code); RequestId = requestId; Accepted = accepted; Code = code; }
}

public sealed class LilithGameBridgeDailyNotePollResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgeDailyNotePollRequest.PollMethod;
    public Guid RequestId { get; }
    public bool Succeeded { get; }
    public LilithDailyNoteCommand? Command { get; }
    public string Code { get; }
    public LilithGameBridgeDailyNotePollResponse(Guid generationId, Guid requestId, long sequence, bool succeeded, LilithDailyNoteCommand? command, string code)
        : base(generationId, sequence)
    {
        LilithNoteInboxProtocol.RequireId(requestId); LilithNoteInboxGameFrames.ValidateAck(succeeded, code);
        if (command is not null && (!succeeded || command.Scope.RuntimeGeneration != generationId))
            throw new ArgumentException("Daily poll command generation rejected.");
        RequestId = requestId; Succeeded = succeeded; Command = command; Code = code;
    }
}

public sealed class LilithGameBridgeDailyNoteCompleteResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod;
    public Guid RequestId { get; }
    public LilithDailyNoteScope Scope { get; }
    public bool Accepted { get; }
    public string Code { get; }
    public LilithGameBridgeDailyNoteCompleteResponse(Guid generationId, Guid requestId, long sequence, LilithDailyNoteScope scope, bool accepted, string code)
        : base(generationId, sequence)
    {
        LilithNoteInboxProtocol.RequireId(requestId); LilithNoteInboxGameFrames.ValidateAck(accepted, code);
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (scope.RuntimeGeneration != generationId) throw new ArgumentException("Daily completion acknowledgement generation rejected.");
        RequestId = requestId; Accepted = accepted; Code = code;
    }
}

internal static class LilithDailyNoteGameFrames
{
    internal static bool TryWriteFields(Utf8JsonWriter writer, LilithGameBridgeFrame frame)
    {
        switch (frame)
        {
            case LilithGameBridgeDailyNoteOpportunityRequest request:
                Common(writer, request.RequestId, request.Sequence, request.Method);
                writer.WritePropertyName("opportunity"); LilithDailyNoteProtocol.EncodeOpportunity(request.Opportunity).WriteTo(writer); return true;
            case LilithGameBridgeDailyNotePollRequest request:
                Common(writer, request.RequestId, request.Sequence, request.Method);
                writer.WriteString("profileId", request.ProfileId); writer.WriteString("gameEpoch", request.GameEpoch.ToString("N"));
                writer.WriteNumber("waitMilliseconds", request.WaitMilliseconds); return true;
            case LilithGameBridgeDailyNoteCompleteRequest request:
                Common(writer, request.RequestId, request.Sequence, request.Method);
                writer.WritePropertyName("scope"); LilithDailyNoteProtocol.EncodeScope(request.Scope).WriteTo(writer);
                writer.WritePropertyName("result"); LilithNoteInboxProtocol.EncodeResult(request.Result).WriteTo(writer); return true;
            case LilithGameBridgeDailyNoteOpportunityResponse response:
                Common(writer, response.RequestId, response.Sequence, response.Method);
                writer.WriteBoolean("accepted", response.Accepted); writer.WriteString("code", response.Code); return true;
            case LilithGameBridgeDailyNotePollResponse response:
                Common(writer, response.RequestId, response.Sequence, response.Method);
                writer.WriteBoolean("succeeded", response.Succeeded); writer.WriteString("code", response.Code);
                writer.WritePropertyName("command");
                if (response.Command is null) writer.WriteNullValue(); else LilithDailyNoteProtocol.EncodeCommand(response.Command).WriteTo(writer);
                return true;
            case LilithGameBridgeDailyNoteCompleteResponse response:
                Common(writer, response.RequestId, response.Sequence, response.Method);
                writer.WritePropertyName("scope"); LilithDailyNoteProtocol.EncodeScope(response.Scope).WriteTo(writer);
                writer.WriteBoolean("accepted", response.Accepted); writer.WriteString("code", response.Code); return true;
            default: return false;
        }
    }

    internal static LilithGameBridgeClientRequest ReadRequest(JsonElement root, string method)
    {
        Header(root, "game.request");
        switch (method)
        {
            case LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod:
                Exact(root, "opportunity");
                return new LilithGameBridgeDailyNoteOpportunityRequest(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), LilithDailyNoteProtocol.ReadOpportunity(root.GetProperty("opportunity")));
            case LilithGameBridgeDailyNotePollRequest.PollMethod:
                Exact(root, "profileId", "gameEpoch", "waitMilliseconds");
                return new LilithGameBridgeDailyNotePollRequest(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), Text(root, "profileId"), Id(root, "gameEpoch"), root.GetProperty("waitMilliseconds").GetInt32());
            case LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod:
                Exact(root, "scope", "result");
                return new LilithGameBridgeDailyNoteCompleteRequest(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), LilithDailyNoteProtocol.ReadScope(root.GetProperty("scope")), LilithNoteInboxProtocol.ReadResult(root.GetProperty("result")));
            default: throw new InvalidDataException("Daily request method rejected.");
        }
    }

    internal static LilithGameBridgeFrame ReadResponse(JsonElement root, string method)
    {
        Header(root, "game.response");
        switch (method)
        {
            case LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod:
                Exact(root, "accepted", "code");
                return new LilithGameBridgeDailyNoteOpportunityResponse(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), root.GetProperty("accepted").GetBoolean(), Text(root, "code"));
            case LilithGameBridgeDailyNotePollRequest.PollMethod:
                Exact(root, "succeeded", "command", "code");
                var command = root.GetProperty("command");
                return new LilithGameBridgeDailyNotePollResponse(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), root.GetProperty("succeeded").GetBoolean(),
                    command.ValueKind == JsonValueKind.Null ? null : LilithDailyNoteProtocol.ReadCommand(command), Text(root, "code"));
            case LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod:
                Exact(root, "scope", "accepted", "code");
                return new LilithGameBridgeDailyNoteCompleteResponse(Id(root, "generationId"), Id(root, "requestId"), Sequence(root), LilithDailyNoteProtocol.ReadScope(root.GetProperty("scope")), root.GetProperty("accepted").GetBoolean(), Text(root, "code"));
            default: throw new InvalidDataException("Daily response method rejected.");
        }
    }

    private static void Common(Utf8JsonWriter writer, Guid requestId, long sequence, string method)
    { writer.WriteString("requestId", requestId.ToString("N")); writer.WriteNumber("sequence", sequence); writer.WriteString("method", method); }

    private static void Exact(JsonElement root, params string[] extra)
    {
        var names = new string[6 + extra.Length];
        new[] { "version", "kind", "generationId", "requestId", "sequence", "method" }.CopyTo(names, 0);
        extra.CopyTo(names, 6); LilithNoteInboxProtocol.Exact(root, names);
    }

    private static string Text(JsonElement root, string name) => LilithNoteInboxProtocol.String(root, name);
    private static Guid Id(JsonElement root, string name) => LilithNoteInboxProtocol.ParseId(Text(root, name));
    private static long Sequence(JsonElement root) => root.GetProperty("sequence").GetInt64();
    private static void Header(JsonElement root, string kind)
    {
        if (root.GetProperty("version").GetInt32() != LilithGameBridgeProtocol.Version || Text(root, "kind") != kind)
            throw new InvalidDataException("Daily game envelope rejected.");
    }
}
