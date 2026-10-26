using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LilithRuntimeContracts;

// Host-issued daily authority, never a synthetic player conversation.
public sealed record LilithDailyNoteScope
{
    public string ProfileId { get; }
    public Guid GoalId { get; }
    public Guid AttemptId { get; }
    public Guid DeliveryId { get; }
    public Guid OperationId { get; }
    public Guid RuntimeGeneration { get; }
    public Guid GameEpoch { get; }
    public Guid LeaseId { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public LilithDailyNoteScope(string profileId, Guid goalId, Guid attemptId, Guid deliveryId,
        Guid operationId, Guid runtimeGeneration, Guid gameEpoch, Guid leaseId, DateTimeOffset expiresAtUtc)
    {
        LilithDailyNoteProtocol.RequireProfile(profileId);
        foreach (var id in new[] { goalId, attemptId, deliveryId, operationId, runtimeGeneration, gameEpoch, leaseId })
            LilithNoteInboxProtocol.RequireId(id);
        LilithNoteInboxProtocol.RequireUtc(expiresAtUtc);
        ProfileId = profileId; GoalId = goalId; AttemptId = attemptId; DeliveryId = deliveryId;
        OperationId = operationId; RuntimeGeneration = runtimeGeneration; GameEpoch = gameEpoch;
        LeaseId = leaseId; ExpiresAtUtc = expiresAtUtc;
    }
}

public sealed record LilithDailyNoteOpportunity
{
    public string ProfileId { get; }
    public Guid GameEpoch { get; }
    public long Sequence { get; }
    public bool Enabled { get; }
    public bool Safe { get; }
    public string Locale { get; }
    public DateTimeOffset ObservedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }

    public LilithDailyNoteOpportunity(string profileId, Guid gameEpoch, long sequence, bool enabled,
        bool safe, string locale, DateTimeOffset observedAtUtc, DateTimeOffset expiresAtUtc, bool playerAbsent = false)
    {
        LilithDailyNoteProtocol.RequireProfile(profileId); LilithNoteInboxProtocol.RequireId(gameEpoch);
        LilithDailyNoteProtocol.RequireLocale(locale);
        LilithNoteInboxProtocol.RequireUtc(observedAtUtc); LilithNoteInboxProtocol.RequireUtc(expiresAtUtc);
        if (sequence <= 0 || safe && !enabled || expiresAtUtc <= observedAtUtc
            || expiresAtUtc - observedAtUtc > TimeSpan.FromSeconds(5))
            throw new ArgumentException("Daily note opportunity is not fresh and bounded.");
        ProfileId = profileId; GameEpoch = gameEpoch; Sequence = sequence; Enabled = enabled;
        Safe = safe; Locale = locale; ObservedAtUtc = observedAtUtc; ExpiresAtUtc = expiresAtUtc;
        PlayerAbsent = playerAbsent;
    }

    public bool PlayerAbsent { get; }
}

public sealed record LilithDailyNoteCommand
{
    public LilithDailyNoteScope Scope { get; }
    public LilithNoteInboxAction Action { get; }
    public string? Text { get; }
    public string Locale { get; }

    public LilithDailyNoteCommand(LilithDailyNoteScope scope, LilithNoteInboxAction action, string? text, string locale)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (!Enum.IsDefined(typeof(LilithNoteInboxAction), action)) throw new ArgumentException("Daily note action rejected.");
        LilithDailyNoteProtocol.RequireLocale(locale);
        if (action == LilithNoteInboxAction.Deliver) LilithNoteInboxProtocol.ValidateText(text);
        else if (text is not null) throw new ArgumentException("Daily status has no note text.");
        Action = action; Text = text; Locale = locale;
    }
}

public static class LilithDailyNoteProtocol
{
    public const string ProfileId = "local";
    public const string HostRequestKind = "lilith.daily_note.host-request";
    public const string EvaluateMethod = "lilith.daily_note.evaluate";
    public const string CompleteMethod = "lilith.daily_note.complete";
    public const string CancelMethod = "lilith.daily_note.cancel";

    public static void RequireProfile(string value)
    { if (value != ProfileId) throw new ArgumentException("Daily note profile is not the pinned local profile."); }

    public static void RequireLocale(string value)
    { if (value is not ("zh-TW" or "zh-CN" or "ja" or "en")) throw new ArgumentException("Daily note locale rejected."); }

    public static JsonElement EncodeScope(LilithDailyNoteScope scope) => JsonSerializer.SerializeToElement(new
    {
        profile_id = scope.ProfileId, goal_id = scope.GoalId.ToString("N"), attempt_id = scope.AttemptId.ToString("N"),
        delivery_id = scope.DeliveryId.ToString("N"), operation_id = scope.OperationId.ToString("N"),
        runtime_generation = scope.RuntimeGeneration.ToString("N"), game_epoch = scope.GameEpoch.ToString("N"),
        lease_id = scope.LeaseId.ToString("N"), expires_at_utc = scope.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)
    });

    public static LilithDailyNoteScope ReadScope(JsonElement value)
    {
        LilithNoteInboxProtocol.Exact(value, "profile_id", "goal_id", "attempt_id", "delivery_id", "operation_id",
            "runtime_generation", "game_epoch", "lease_id", "expires_at_utc");
        return new(Text(value, "profile_id"), Id(value, "goal_id"), Id(value, "attempt_id"), Id(value, "delivery_id"),
            Id(value, "operation_id"), Id(value, "runtime_generation"), Id(value, "game_epoch"), Id(value, "lease_id"), Utc(value, "expires_at_utc"));
    }

    public static JsonElement EncodeOpportunity(LilithDailyNoteOpportunity value) => JsonSerializer.SerializeToElement(new
    {
        profile_id = value.ProfileId, game_epoch = value.GameEpoch.ToString("N"), sequence = value.Sequence,
        enabled = value.Enabled, safe = value.Safe, locale = value.Locale, player_absent = value.PlayerAbsent,
        observed_at_utc = value.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        expires_at_utc = value.ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)
    });

    public static LilithDailyNoteOpportunity ReadOpportunity(JsonElement value)
    {
        var extended = value.TryGetProperty("player_absent", out var absent);
        LilithNoteInboxProtocol.Exact(value, extended
            ? new[] { "profile_id", "game_epoch", "sequence", "enabled", "safe", "locale", "observed_at_utc", "expires_at_utc", "player_absent" }
            : new[] { "profile_id", "game_epoch", "sequence", "enabled", "safe", "locale", "observed_at_utc", "expires_at_utc" });
        return new(Text(value, "profile_id"), Id(value, "game_epoch"), value.GetProperty("sequence").GetInt64(),
            value.GetProperty("enabled").GetBoolean(), value.GetProperty("safe").GetBoolean(), Text(value, "locale"),
            Utc(value, "observed_at_utc"), Utc(value, "expires_at_utc"), extended && absent.GetBoolean());
    }

    public static JsonElement EncodeCommand(LilithDailyNoteCommand value) => JsonSerializer.SerializeToElement(new
    { scope = EncodeScope(value.Scope), action = value.Action == LilithNoteInboxAction.Deliver ? "deliver" : "status", text = value.Text, locale = value.Locale });

    public static LilithDailyNoteCommand ReadCommand(JsonElement value)
    {
        LilithNoteInboxProtocol.Exact(value, "scope", "action", "text", "locale");
        var action = Text(value, "action") switch
        { "deliver" => LilithNoteInboxAction.Deliver, "status" => LilithNoteInboxAction.Status, _ => throw new ArgumentException("Daily note action rejected.") };
        return new(ReadScope(value.GetProperty("scope")), action,
            value.GetProperty("text").ValueKind == JsonValueKind.Null ? null : Text(value, "text"), Text(value, "locale"));
    }

    public static void RequireResult(LilithDailyNoteScope scope, LilithNoteInboxResult result)
    {
        if (result.OperationId != scope.OperationId || result.DeliveryId != scope.DeliveryId)
            throw new ArgumentException("Daily note result changed the registered operation or delivery.");
    }

    private static string Text(JsonElement value, string name) => LilithNoteInboxProtocol.String(value, name);
    private static Guid Id(JsonElement value, string name) => LilithNoteInboxProtocol.ParseId(Text(value, name));
    private static DateTimeOffset Utc(JsonElement value, string name)
    {
        var text = Text(value, name);
        if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc)
            || utc.ToString("O", CultureInfo.InvariantCulture) != text)
            throw new InvalidDataException("Canonical daily UTC timestamp required.");
        LilithNoteInboxProtocol.RequireUtc(utc); return utc;
    }
}
