using System.Globalization;
using System.IO;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : IHermesTelegramVoiceRuntimeProtocol
{
    public const string TelegramVoiceStatusMethod = "messaging.telegram.voice.status";
    public const string TelegramVoiceSetMethod = "messaging.telegram.voice.set";

    public D046HermesRuntimeRequestBinding BindTelegramVoiceStatus(string userId)
    {
        RequireTelegramVoiceUserId(userId);
        return Bind(TelegramVoiceStatusMethod, new { user_id = userId });
    }

    public D046HermesRuntimeRequestBinding BindTelegramVoiceSet(
        string userId, HermesTelegramVoiceMode mode, string expectedRevision)
    {
        RequireTelegramVoiceUserId(userId);
        RequireTelegramVoiceRevision(expectedRevision);
        return Bind(TelegramVoiceSetMethod, new
        {
            user_id = userId,
            mode = TelegramVoiceModeText(mode),
            expected_revision = expectedRevision
        });
    }

    public HermesTelegramVoiceStatus DecodeTelegramVoiceStatus(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        if (binding.Method is not (TelegramVoiceStatusMethod or TelegramVoiceSetMethod))
            throw new InvalidDataException("Telegram voice response used another operation.");
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "user_id", "mode", "revision", "configured", "locale");
        var request = TelegramExactObject(binding.HermesPayload,
            binding.Method == TelegramVoiceSetMethod
                ? new[] { "user_id", "mode", "expected_revision" }
                : new[] { "user_id" });
        var userId = TelegramString(values, "user_id", 20, allowEmpty: false);
        var revision = TelegramString(values, "revision", 64, allowEmpty: false);
        try
        {
            RequireTelegramVoiceUserId(userId);
            RequireTelegramVoiceRevision(revision);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Telegram voice identity or revision was rejected.", exception);
        }
        var modeText = TelegramString(values, "mode", 16, allowEmpty: false);
        var mode = modeText switch
        {
            "off" => HermesTelegramVoiceMode.Off,
            "voice_only" => HermesTelegramVoiceMode.VoiceOnly,
            "all" => HermesTelegramVoiceMode.All,
            _ => throw new InvalidDataException("Telegram voice mode was rejected.")
        };
        if (!string.Equals(userId, TelegramString(request, "user_id", 20, false), StringComparison.Ordinal)
            || (binding.Method == TelegramVoiceSetMethod
                && !string.Equals(modeText, TelegramString(request, "mode", 16, false), StringComparison.Ordinal)))
            throw new InvalidDataException("Telegram voice receipt did not match its requested account or mode.");
        var locale = TelegramString(values, "locale", 16, allowEmpty: false) switch
        {
            "unavailable" => HermesTelegramVoiceLocale.Unavailable,
            "zh" => HermesTelegramVoiceLocale.Zh,
            "ja" => HermesTelegramVoiceLocale.Ja,
            _ => throw new InvalidDataException("Telegram voice locale was rejected.")
        };
        var configured = TelegramBoolean(values, "configured");
        if (configured != (locale != HermesTelegramVoiceLocale.Unavailable))
            throw new InvalidDataException("Telegram voice configuration contradicted its locale.");
        return new HermesTelegramVoiceStatus(userId, mode, revision, configured, locale);
    }

    private static string TelegramVoiceModeText(HermesTelegramVoiceMode mode)
        => mode switch
        {
            HermesTelegramVoiceMode.Off => "off",
            HermesTelegramVoiceMode.VoiceOnly => "voice_only",
            HermesTelegramVoiceMode.All => "all",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static void RequireTelegramVoiceUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId) || userId.Length > 19
            || userId[0] is < '1' or > '9'
            || userId.Any(static c => c is < '0' or > '9')
            || !long.TryParse(userId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException("Telegram voice settings require an exact private account ID.", nameof(userId));
    }

    private static void RequireTelegramVoiceRevision(string revision)
    {
        if (revision is null || revision.Length != 64
            || revision.Any(static c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Telegram voice revision was rejected.", nameof(revision));
    }
}
