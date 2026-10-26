using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LilithRuntimeContracts;

public sealed record LilithDailyNoteUiPresence(Guid UiEpoch, long Sequence, bool Attached);
public sealed record LilithDailyNoteUiPresenceReceipt(Guid UiEpoch, long Sequence,
    Guid? UiLeaseId, DateTimeOffset? ExpiresAtUtc, bool Accepted);
public sealed record LilithDailyNoteApprovalResponse(LilithDailyNoteScope Scope,
    Guid ApprovalId, Guid UiLeaseId, string Choice);
public sealed record LilithDailyNoteApprovalReceipt(LilithDailyNoteScope Scope,
    Guid ApprovalId, Guid UiLeaseId, string Choice, bool Accepted, int Resolved, string Code);
public sealed record LilithDailyNoteApprovalClosed(LilithDailyNoteScope Scope,
    Guid ApprovalId, Guid UiLeaseId, string Code);

public sealed record LilithDailyNoteApprovalRequest
{
    public LilithDailyNoteScope Scope { get; }
    public Guid ApprovalId { get; }
    public Guid UiLeaseId { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public string PatternKey { get; }
    public string Command { get; }
    public string Description { get; }
    public IReadOnlyList<string> Choices { get; }
    public bool AllowSession { get; }
    public bool AllowPermanent { get; }
    public bool SmartDenied { get; }

    public LilithDailyNoteApprovalRequest(LilithDailyNoteScope scope, Guid approvalId,
        Guid uiLeaseId, DateTimeOffset expiresAtUtc, string patternKey, string command,
        string description, IEnumerable<string> choices, bool allowSession,
        bool allowPermanent, bool smartDenied)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        LilithNoteInboxProtocol.RequireId(approvalId); LilithNoteInboxProtocol.RequireId(uiLeaseId);
        LilithNoteInboxProtocol.RequireUtc(expiresAtUtc);
        if (expiresAtUtc > scope.ExpiresAtUtc) throw new ArgumentException("Approval outlives daily scope.");
        LilithDailyNoteApprovalProtocol.RequireNativeText(patternKey, 256, false);
        LilithDailyNoteApprovalProtocol.RequireNativeText(command, 16 * 1024, true);
        LilithDailyNoteApprovalProtocol.RequireNativeText(description, 16 * 1024, true);
        var offered = (choices ?? throw new ArgumentNullException(nameof(choices))).ToArray();
        if (offered.Length is < 1 or > 4 || offered.Distinct(StringComparer.Ordinal).Count() != offered.Length)
            throw new ArgumentException("Native approval choices must be unique and bounded.");
        foreach (var choice in offered) LilithDailyNoteApprovalProtocol.RequireChoice(choice);
        ApprovalId = approvalId; UiLeaseId = uiLeaseId; ExpiresAtUtc = expiresAtUtc;
        PatternKey = patternKey; Command = command; Description = description;
        Choices = Array.AsReadOnly(offered); AllowSession = allowSession;
        AllowPermanent = allowPermanent; SmartDenied = smartDenied;
    }
    public override string ToString() => "LilithDailyNoteApprovalRequest { NativePresentation = <redacted> }";
}

// Independent UI channel. None of these messages creates or selects a chat task.
public static class LilithDailyNoteApprovalProtocol
{
    public const string PresenceMethod = "lilith.daily_note.ui.presence";
    public const string RespondMethod = "lilith.daily_note.approval.respond";
    public const string ApprovalEvent = "lilith.daily_note.approval.request";
    public const string ClosedEvent = "lilith.daily_note.approval.closed";
    public static readonly TimeSpan UiLeaseLifetime = TimeSpan.FromSeconds(5);

    public static JsonElement EncodePresence(LilithDailyNoteUiPresence value)
    { Validate(value); return JsonSerializer.SerializeToElement(new { ui_epoch = N(value.UiEpoch), sequence = value.Sequence, attached = value.Attached }); }
    public static LilithDailyNoteUiPresence ReadPresence(JsonElement value)
    {
        Exact(value, "ui_epoch", "sequence", "attached");
        var result = new LilithDailyNoteUiPresence(Id(value, "ui_epoch"), value.GetProperty("sequence").GetInt64(), Bool(value, "attached"));
        Validate(result); return result;
    }
    public static JsonElement EncodePresenceReceipt(LilithDailyNoteUiPresenceReceipt value)
    {
        Validate(value);
        return JsonSerializer.SerializeToElement(new { ui_epoch = N(value.UiEpoch), sequence = value.Sequence,
            ui_lease_id = value.UiLeaseId?.ToString("N"), expires_at_utc = value.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture), accepted = value.Accepted });
    }
    public static LilithDailyNoteUiPresenceReceipt ReadPresenceReceipt(JsonElement value)
    {
        Exact(value, "ui_epoch", "sequence", "ui_lease_id", "expires_at_utc", "accepted");
        var result = new LilithDailyNoteUiPresenceReceipt(Id(value, "ui_epoch"), value.GetProperty("sequence").GetInt64(),
            value.GetProperty("ui_lease_id").ValueKind == JsonValueKind.Null ? null : Id(value, "ui_lease_id"),
            value.GetProperty("expires_at_utc").ValueKind == JsonValueKind.Null ? null : Utc(value, "expires_at_utc"), Bool(value, "accepted"));
        Validate(result); return result;
    }
    public static JsonElement EncodeRequest(LilithDailyNoteApprovalRequest value)
        => JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(value.Scope),
            approval_id = N(value.ApprovalId), ui_lease_id = N(value.UiLeaseId), expires_at_utc = O(value.ExpiresAtUtc),
            pattern_key = value.PatternKey, command = value.Command, description = value.Description,
            choices = value.Choices, allow_session = value.AllowSession, allow_permanent = value.AllowPermanent, smart_denied = value.SmartDenied });
    public static LilithDailyNoteApprovalRequest ReadRequest(JsonElement value)
    {
        Exact(value, "scope", "approval_id", "ui_lease_id", "expires_at_utc", "pattern_key", "command", "description", "choices", "allow_session", "allow_permanent", "smart_denied");
        var choices = value.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > 4) throw new InvalidDataException("Native choices rejected.");
        return new(Scope(value), Id(value, "approval_id"), Id(value, "ui_lease_id"), Utc(value, "expires_at_utc"),
            Text(value, "pattern_key"), Text(value, "command"), Text(value, "description"),
            choices.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new InvalidDataException("Native choice rejected.")),
            Bool(value, "allow_session"), Bool(value, "allow_permanent"), Bool(value, "smart_denied"));
    }
    public static JsonElement EncodeResponse(LilithDailyNoteApprovalResponse value)
    {
        Validate(value); return JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(value.Scope),
            approval_id = N(value.ApprovalId), ui_lease_id = N(value.UiLeaseId), choice = value.Choice });
    }
    public static LilithDailyNoteApprovalResponse ReadResponse(JsonElement value)
    {
        Exact(value, "scope", "approval_id", "ui_lease_id", "choice");
        var result = new LilithDailyNoteApprovalResponse(Scope(value), Id(value, "approval_id"), Id(value, "ui_lease_id"), Text(value, "choice"));
        Validate(result); return result;
    }
    public static JsonElement EncodeReceipt(LilithDailyNoteApprovalReceipt value)
    {
        Validate(value); return JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(value.Scope),
            approval_id = N(value.ApprovalId), ui_lease_id = N(value.UiLeaseId), choice = value.Choice,
            accepted = value.Accepted, resolved = value.Resolved, code = value.Code });
    }
    public static LilithDailyNoteApprovalReceipt ReadReceipt(JsonElement value)
    {
        Exact(value, "scope", "approval_id", "ui_lease_id", "choice", "accepted", "resolved", "code");
        var result = new LilithDailyNoteApprovalReceipt(Scope(value), Id(value, "approval_id"), Id(value, "ui_lease_id"),
            Text(value, "choice"), Bool(value, "accepted"), value.GetProperty("resolved").GetInt32(), Text(value, "code"));
        Validate(result); return result;
    }
    public static JsonElement EncodeClosed(LilithDailyNoteApprovalClosed value)
    {
        Validate(value); return JsonSerializer.SerializeToElement(new { scope = LilithDailyNoteProtocol.EncodeScope(value.Scope),
            approval_id = N(value.ApprovalId), ui_lease_id = N(value.UiLeaseId), code = value.Code });
    }
    public static LilithDailyNoteApprovalClosed ReadClosed(JsonElement value)
    {
        Exact(value, "scope", "approval_id", "ui_lease_id", "code");
        var result = new LilithDailyNoteApprovalClosed(Scope(value), Id(value, "approval_id"), Id(value, "ui_lease_id"), Text(value, "code"));
        Validate(result); return result;
    }
    public static void Validate(LilithDailyNoteUiPresence value)
    { LilithNoteInboxProtocol.RequireId(value.UiEpoch); if (value.Sequence <= 0) throw new ArgumentException("Positive UI sequence required."); }
    public static void Validate(LilithDailyNoteUiPresenceReceipt value)
    {
        Validate(new LilithDailyNoteUiPresence(value.UiEpoch, value.Sequence, false));
        if (value.UiLeaseId.HasValue != value.ExpiresAtUtc.HasValue || !value.Accepted && value.UiLeaseId.HasValue)
            throw new ArgumentException("UI lease receipt shape rejected.");
        if (value.UiLeaseId is { } id) LilithNoteInboxProtocol.RequireId(id);
        if (value.ExpiresAtUtc is { } at) LilithNoteInboxProtocol.RequireUtc(at);
    }
    public static void Validate(LilithDailyNoteApprovalResponse value)
    { RequireIdentity(value.Scope, value.ApprovalId, value.UiLeaseId); RequireChoice(value.Choice); }
    public static void Validate(LilithDailyNoteApprovalReceipt value)
    {
        RequireIdentity(value.Scope, value.ApprovalId, value.UiLeaseId); RequireChoice(value.Choice); RequireCode(value.Code);
        if (value.Resolved is < 0 or > 1 || value.Accepted != (value.Resolved == 1) || value.Accepted != (value.Code == "ok"))
            throw new ArgumentException("Approval receipt outcome rejected.");
    }
    public static void Validate(LilithDailyNoteApprovalClosed value)
    { RequireIdentity(value.Scope, value.ApprovalId, value.UiLeaseId); RequireCode(value.Code); }
    public static void RequireChoice(string value)
    { if (value is not ("once" or "session" or "always" or "deny")) throw new ArgumentException("Native approval choice rejected."); }
    public static void RequireCode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 96 || value[0] is < 'a' or > 'z'
            || value.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_')))
            throw new ArgumentException("Daily approval code rejected.");
    }
    internal static void RequireNativeText(string value, int limit, bool multiline)
    {
        if (value is null || value.Length > limit || !multiline && value.Length == 0
            || value.Any(c => char.IsControl(c) && !(multiline && (c is '\r' or '\n' or '\t'))))
            throw new ArgumentException("Native approval presentation rejected.");
    }
    private static void RequireIdentity(LilithDailyNoteScope scope, Guid approval, Guid lease)
    { if (scope is null) throw new ArgumentNullException(nameof(scope)); LilithNoteInboxProtocol.RequireId(approval); LilithNoteInboxProtocol.RequireId(lease); }
    private static LilithDailyNoteScope Scope(JsonElement value) => LilithDailyNoteProtocol.ReadScope(value.GetProperty("scope"));
    private static string N(Guid value) => value.ToString("N");
    private static string O(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string Text(JsonElement value, string name) => LilithNoteInboxProtocol.String(value, name);
    private static Guid Id(JsonElement value, string name) => LilithNoteInboxProtocol.ParseId(Text(value, name));
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static void Exact(JsonElement value, params string[] keys) => LilithNoteInboxProtocol.Exact(value, keys);
    private static DateTimeOffset Utc(JsonElement value, string name)
    {
        var text = Text(value, name);
        if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) || O(result) != text)
            throw new InvalidDataException("Canonical approval UTC timestamp required.");
        LilithNoteInboxProtocol.RequireUtc(result); return result;
    }
}
