using System.Globalization;
using System.Text.Json;

namespace LilithRuntime;

internal sealed partial class LilithRuntimeRequestRouter
{
    private const string TelegramVoiceStatusMethod = "messaging.telegram.voice.status";
    private const string TelegramVoiceSetMethod = "messaging.telegram.voice.set";

    private static void ValidateTelegramPrivateVoiceUser(string userId)
    {
        if (userId.Length is < 1 or > 19 || userId[0] == '0'
            || userId.Any(static c => c is < '0' or > '9')
            || !long.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value <= 0)
            throw new InvalidDataException("The Telegram private voice user was rejected.");
        // Syntax is not authorization. Hermes checks the current native pairing.
    }

    private static void ValidateTelegramVoiceMode(string mode)
    {
        if (mode is not ("off" or "voice_only" or "all"))
            throw new InvalidDataException("The Telegram voice mode was rejected.");
    }

    private static void ValidateTelegramVoiceRevision(string revision)
    {
        if (revision.Length != 64
            || revision.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("The Telegram voice revision was rejected.");
    }

    private static JsonElement SanitizeTelegramVoiceResult(
        string method, JsonElement request, JsonElement result)
    {
        var values = ReadExactObject(result,
            new[] { "user_id", "mode", "revision", "configured", "locale" });
        var userId = RequiredText(values, "user_id", 19);
        var mode = RequiredText(values, "mode", 16);
        var revision = RequiredText(values, "revision", 64);
        var locale = RequiredText(values, "locale", 16);
        ValidateTelegramPrivateVoiceUser(userId);
        ValidateTelegramVoiceMode(mode);
        ValidateTelegramVoiceRevision(revision);
        if (!TryRequiredBoolean(values, "configured", out var configured)
            || (configured ? locale is not ("zh" or "ja") : locale != "unavailable")
            || userId != request.GetProperty("user_id").GetString()
            || (method == TelegramVoiceSetMethod && mode != request.GetProperty("mode").GetString()))
            throw new InvalidDataException("The Telegram voice receipt was inconsistent.");
        return JsonSerializer.SerializeToElement(new
        {
            user_id = userId,
            mode,
            revision,
            configured,
            locale
        });
    }
}
