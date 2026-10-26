using System.Globalization;
using System.Text.Json;

namespace LilithRuntimeContracts;

// A short native capability proof, not consent and not a persisted daily attempt.
public sealed record LilithDailyNotePreparation
{
    public string ProfileId { get; }
    public Guid RuntimeGeneration { get; }
    public Guid GameEpoch { get; }
    public Guid PreparationId { get; }
    public string Locale { get; }
    public Guid? UiLeaseId { get; }

    public LilithDailyNotePreparation(string profileId, Guid runtimeGeneration, Guid gameEpoch,
        Guid preparationId, string locale, Guid? uiLeaseId)
    {
        LilithDailyNoteProtocol.RequireProfile(profileId);
        foreach (var id in new[] { runtimeGeneration, gameEpoch, preparationId }) LilithNoteInboxProtocol.RequireId(id);
        if (uiLeaseId is { } ui) LilithNoteInboxProtocol.RequireId(ui);
        LilithDailyNoteProtocol.RequireLocale(locale);
        ProfileId = profileId; RuntimeGeneration = runtimeGeneration; GameEpoch = gameEpoch;
        PreparationId = preparationId; Locale = locale; UiLeaseId = uiLeaseId;
    }
}

public sealed record LilithDailyNoteReadiness
{
    public LilithDailyNotePreparation Request { get; }
    public bool Ready { get; }
    public string Code { get; }
    public Guid? Token { get; }
    public DateTimeOffset? ExpiresAtUtc { get; }
    public string Provider { get; }
    public string Model { get; }
    public bool RequiresUi { get; }
    public Guid? UiLeaseId { get; }

    public LilithDailyNoteReadiness(LilithDailyNotePreparation request, bool ready, string code,
        Guid? token, DateTimeOffset? expiresAtUtc, string provider, string model, bool requiresUi, Guid? uiLeaseId)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(code) || code.Length > 96 || code.Any(c => c is not (>= 'a' and <= 'z'
            or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_')))
            throw new ArgumentException("Daily readiness code rejected.");
        if (provider is null || model is null || provider.Length > 512 || model.Length > 512
            || provider.Any(char.IsControl) || model.Any(char.IsControl)
            || ready && (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)))
            throw new ArgumentException("Daily readiness route rejected.");
        if (ready != (token.HasValue && expiresAtUtc.HasValue) || !ready && (token.HasValue || expiresAtUtc.HasValue))
            throw new ArgumentException("Daily readiness proof shape rejected.");
        if (token is { } proof) LilithNoteInboxProtocol.RequireId(proof);
        if (expiresAtUtc is { } expires) LilithNoteInboxProtocol.RequireUtc(expires);
        if (uiLeaseId is { } ui)
        {
            LilithNoteInboxProtocol.RequireId(ui);
            if (uiLeaseId != request.UiLeaseId) throw new ArgumentException("Daily readiness changed the UI lease.");
        }
        if (ready && requiresUi && (!uiLeaseId.HasValue || uiLeaseId != request.UiLeaseId))
            throw new ArgumentException("Daily readiness requires its registered UI lease.");
        Ready = ready; Code = code; Token = token; ExpiresAtUtc = expiresAtUtc;
        Provider = provider; Model = model; RequiresUi = requiresUi; UiLeaseId = uiLeaseId;
    }
}

public static class LilithDailyNoteReadinessProtocol
{
    public const string PrepareMethod = "lilith.daily_note.prepare";
    public const string CancelMethod = "lilith.daily_note.prepare.cancel";

    public static JsonElement EncodeRequest(LilithDailyNotePreparation request) => JsonSerializer.SerializeToElement(new
    {
        profile_id = request.ProfileId, runtime_generation = request.RuntimeGeneration.ToString("N"),
        game_epoch = request.GameEpoch.ToString("N"), preparation_id = request.PreparationId.ToString("N"),
        locale = request.Locale, ui_lease_id = request.UiLeaseId?.ToString("N")
    });

    public static LilithDailyNotePreparation ReadRequest(JsonElement value)
    {
        LilithNoteInboxProtocol.Exact(value, "profile_id", "runtime_generation", "game_epoch", "preparation_id", "locale", "ui_lease_id");
        return new(Text(value, "profile_id"), Id(value, "runtime_generation"), Id(value, "game_epoch"),
            Id(value, "preparation_id"), Text(value, "locale"), OptionalId(value, "ui_lease_id"));
    }

    public static JsonElement EncodeReadiness(LilithDailyNoteReadiness value) => JsonSerializer.SerializeToElement(new
    {
        profile_id = value.Request.ProfileId, runtime_generation = value.Request.RuntimeGeneration.ToString("N"),
        game_epoch = value.Request.GameEpoch.ToString("N"), preparation_id = value.Request.PreparationId.ToString("N"),
        ready = value.Ready, code = value.Code, token = value.Token?.ToString("N"),
        expires_at_utc = value.ExpiresAtUtc?.ToString("O", CultureInfo.InvariantCulture),
        provider = value.Provider, model = value.Model, requires_ui = value.RequiresUi, ui_lease_id = value.UiLeaseId?.ToString("N")
    });

    public static LilithDailyNoteReadiness ReadReadiness(JsonElement value, LilithDailyNotePreparation request)
    {
        LilithNoteInboxProtocol.Exact(value, "profile_id", "runtime_generation", "game_epoch", "preparation_id",
            "ready", "code", "token", "expires_at_utc", "provider", "model", "requires_ui", "ui_lease_id");
        if (Text(value, "profile_id") != request.ProfileId || Id(value, "runtime_generation") != request.RuntimeGeneration
            || Id(value, "game_epoch") != request.GameEpoch || Id(value, "preparation_id") != request.PreparationId)
            throw new InvalidDataException("Daily readiness identity changed.");
        DateTimeOffset? expires = null;
        if (value.GetProperty("expires_at_utc").ValueKind != JsonValueKind.Null)
        {
            var text = Text(value, "expires_at_utc");
            if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc)
                || utc.ToString("O", CultureInfo.InvariantCulture) != text)
                throw new InvalidDataException("Canonical daily readiness UTC required.");
            LilithNoteInboxProtocol.RequireUtc(utc); expires = utc;
        }
        return new(request, value.GetProperty("ready").GetBoolean(), Text(value, "code"), OptionalId(value, "token"),
            expires, Text(value, "provider"), Text(value, "model"), value.GetProperty("requires_ui").GetBoolean(), OptionalId(value, "ui_lease_id"));
    }

    private static string Text(JsonElement value, string name) => LilithNoteInboxProtocol.String(value, name);
    private static Guid Id(JsonElement value, string name) => LilithNoteInboxProtocol.ParseId(Text(value, name));
    private static Guid? OptionalId(JsonElement value, string name)
        => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Id(value, name);
}
