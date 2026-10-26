using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

internal enum PlayerReplyCurrentFactRequirement
{
    None = 0,
    CurrentConfiguredWeather = 1,
    GeneralSearch = 2
}

internal enum LocalWeatherContextAvailability
{
    Disabled = 0,
    Available = 1,
    Unavailable = 2
}

internal sealed class LocalWeatherContextObservation
{
    internal LocalWeatherContextAvailability Availability { get; }
    internal string PromptContext { get; }
    internal string ConfigurationFingerprint { get; }
    internal long ObservedTimestamp { get; }
    internal long ExpiresTimestamp { get; }

    internal LocalWeatherContextObservation(
        LocalWeatherContextAvailability availability,
        string promptContext,
        string configurationFingerprint,
        long observedTimestamp,
        long expiresTimestamp)
    {
        if (!Enum.IsDefined(typeof(LocalWeatherContextAvailability), availability))
            throw new ArgumentOutOfRangeException(nameof(availability));
        if (promptContext == null
            || promptContext.Length > 2048
            || promptContext.Any(
                value => char.IsControl(value)
                    && value != '\r'
                    && value != '\n'
                    && value != '\t'))
        {
            throw new ArgumentException(
                "Weather Prompt context must be bounded text.",
                nameof(promptContext));
        }
        if (string.IsNullOrWhiteSpace(configurationFingerprint)
            || configurationFingerprint.Length > 128
            || configurationFingerprint.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A bounded weather configuration fingerprint is required.",
                nameof(configurationFingerprint));
        }
        if (observedTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(observedTimestamp));

        if (availability == LocalWeatherContextAvailability.Available)
        {
            if (string.IsNullOrWhiteSpace(promptContext)
                || expiresTimestamp <= observedTimestamp)
            {
                throw new ArgumentException(
                    "Available weather requires bounded context and a future expiry.",
                    nameof(expiresTimestamp));
            }
        }
        else
        {
            if (expiresTimestamp != observedTimestamp)
            {
                throw new ArgumentException(
                    "Non-available weather cannot carry a freshness lease.",
                    nameof(expiresTimestamp));
            }
            if (availability == LocalWeatherContextAvailability.Disabled
                && promptContext.Length != 0)
            {
                throw new ArgumentException(
                    "Disabled weather cannot carry Prompt context.",
                    nameof(promptContext));
            }
        }

        Availability = availability;
        PromptContext = promptContext;
        ConfigurationFingerprint = configurationFingerprint;
        ObservedTimestamp = observedTimestamp;
        ExpiresTimestamp = expiresTimestamp;
    }

    internal bool IsFresh(long nowTimestamp)
        => Availability == LocalWeatherContextAvailability.Available
            && nowTimestamp >= ObservedTimestamp
            && nowTimestamp < ExpiresTimestamp;
}

internal sealed class PlayerReplyCurrentFactAdmission
{
    internal PlayerReplyCurrentFactRequirement Requirement { get; }
    internal bool LocalWeatherContextAdmitted { get; }
    internal bool SearchRequested => Requirement switch
    {
        PlayerReplyCurrentFactRequirement.None => false,
        PlayerReplyCurrentFactRequirement.CurrentConfiguredWeather =>
            !LocalWeatherContextAdmitted,
        PlayerReplyCurrentFactRequirement.GeneralSearch => true,
        _ => true
    };
    internal string PromptContext { get; }

    private PlayerReplyCurrentFactAdmission(
        PlayerReplyCurrentFactRequirement requirement,
        bool localWeatherContextAdmitted,
        string promptContext)
    {
        if (!Enum.IsDefined(
                typeof(PlayerReplyCurrentFactRequirement),
                requirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement));
        }
        if (localWeatherContextAdmitted
            && requirement
                != PlayerReplyCurrentFactRequirement
                    .CurrentConfiguredWeather)
        {
            throw new ArgumentException(
                "Only current configured weather can admit local context.",
                nameof(localWeatherContextAdmitted));
        }
        if (promptContext == null
            || promptContext.Length > 2048
            || promptContext.Any(
                value => char.IsControl(value)
                    && value != '\r'
                    && value != '\n'
                    && value != '\t'))
        {
            throw new ArgumentException(
                "Current-fact Prompt context must be bounded text.",
                nameof(promptContext));
        }
        if (localWeatherContextAdmitted
            && string.IsNullOrWhiteSpace(promptContext))
        {
            throw new ArgumentException(
                "Admitted local weather requires Prompt context.",
                nameof(promptContext));
        }
        if (requirement != PlayerReplyCurrentFactRequirement.None
            && !localWeatherContextAdmitted
            && promptContext.Length != 0)
        {
            throw new ArgumentException(
                "Unadmitted current facts cannot carry Prompt context.",
                nameof(promptContext));
        }
        Requirement = requirement;
        LocalWeatherContextAdmitted = localWeatherContextAdmitted;
        PromptContext = promptContext;
    }

    internal static PlayerReplyCurrentFactAdmission Ordinary(
        string promptContext)
        => new(
            PlayerReplyCurrentFactRequirement.None,
            localWeatherContextAdmitted: false,
            promptContext);

    internal static PlayerReplyCurrentFactAdmission AdmittedWeather(
        string promptContext)
        => new(
            PlayerReplyCurrentFactRequirement.CurrentConfiguredWeather,
            localWeatherContextAdmitted: true,
            promptContext);

    internal static PlayerReplyCurrentFactAdmission Excluded(
        PlayerReplyCurrentFactRequirement requirement)
    {
        if (requirement
            is not (PlayerReplyCurrentFactRequirement
                    .CurrentConfiguredWeather
                or PlayerReplyCurrentFactRequirement.GeneralSearch))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement));
        }
        return new PlayerReplyCurrentFactAdmission(
            requirement,
            localWeatherContextAdmitted: false,
            promptContext: string.Empty);
    }
}

internal static class PlayerReplyCurrentFactRouting
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Deliberately closed to location-free, present-condition wording.  A
    // named place, future period or forecast wording remains GeneralSearch.
    private static readonly Regex CurrentConfiguredWeatherZh = new(
        "^\\s*(?:(?:\\u8acb|\\u8bf7)(?:\\u554f|\\u95ee)?|(?:\\u9ebb\\u7169|\\u9ebb\\u70e6)(?:\\u4f60)?)?\\s*"
        + "(?:(?:\\u5e6b|\\u5e2e)\\u6211\\s*)?"
        + "(?:(?:\\u67e5|\\u770b)(?:\\u4e00)?(?:\\u4e0b|\\u770b)?\\s*)?"
        + "(?:\\u73fe\\u5728|\\u73b0\\u5728|\\u76ee\\u524d|\\u4eca\\u5929|\\u4eca\\u65e5)?\\s*"
        + "(?:\\u7684\\s*)?(?:\\u5929\\u6c23|\\u5929\\u6c14)\\s*"
        + "(?:\\u5982\\u4f55|\\u600e\\u9ebc\\u6a23|\\u600e\\u4e48\\u6837|\\u600e\\u6a23|\\u600e\\u6837|"
        + "\\u597d\\u55ce|\\u597d\\u5417|\\u662f\\u4ec0\\u9ebc|\\u662f\\u4ec0\\u4e48|"
        + "(?:\\u60c5\\u6cc1|\\u72c0\\u6cc1|\\u72b6\\u51b5)\\u5982\\u4f55|\\u5462)?"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex CurrentConfiguredWeatherEn = new(
        "^\\s*(?:please\\s+)?(?:(?:tell\\s+me|check)\\s+)?"
        + "(?:(?:(?:what(?:'s|\\s+is)|how(?:'s|\\s+is))\\s+)"
        + "(?:the\\s+)?)?"
        + "(?:current\\s+weather|weather(?:\\s+(?:right\\s+now|now|today))?)"
        + "(?:\\s+like)?\\s*[?.!]*\\s*$",
        Options);

    private static readonly Regex CurrentConfiguredWeatherJa = new(
        "^\\s*(?:\\u4eca|\\u73fe\\u5728|\\u4eca\\u65e5)?(?:\\u306e)?\\s*"
        + "\\u5929\\u6c17(?:\\u306f)?\\s*"
        + "(?:\\u3069\\u3046|\\u3069\\u3046\\u3067\\u3059\\u304b|"
        + "\\u3069\\u3046\\u306a\\u3063\\u3066\\u3044\\u307e\\u3059\\u304b)?"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex NamedWeatherQuestionZh = new(
        "^\\s*(?:(?:\\u8acb|\\u8bf7)(?:\\u554f|\\u95ee)?\\s*)?"
        + "(?:[\\p{L}\\p{M}\\p{N}\\u00b7\\u30fb.\\-]{1,24})\\s*"
        + "(?:\\u7684\\s*)?"
        + "(?:\\u5929\\u6c23|\\u5929\\u6c14|\\u6c23\\u6eab|\\u6c14\\u6e29|"
        + "\\u6eab\\u5ea6|\\u6e29\\u5ea6)\\s*"
        + "(?:\\u5982\\u4f55|\\u600e\\u9ebc\\u6a23|\\u600e\\u4e48\\u6837|"
        + "\\u600e\\u6a23|\\u600e\\u6837|\\u597d\\u55ce|\\u597d\\u5417|"
        + "\\u662f\\u591a\\u5c11|\\u662f\\u5e7e\\u5ea6|\\u662f\\u51e0\\u5ea6|"
        + "\\u591a\\u5c11\\u5ea6|\\u5e7e\\u5ea6|\\u51e0\\u5ea6)"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex NamedTemperatureQuestionZh = new(
        "^\\s*(?:(?:\\u8acb|\\u8bf7)(?:\\u554f|\\u95ee)?\\s*)?"
        + "(?!(?:\\u73fe\\u5728|\\u73b0\\u5728|\\u76ee\\u524d|"
        + "\\u4eca\\u5929|\\u4eca\\u65e5)\\s*)"
        + "(?:[\\p{L}\\p{M}\\p{N}\\u00b7\\u30fb.\\-]{1,24})\\s*"
        + "(?:(?:\\u73fe\\u5728|\\u73b0\\u5728|\\u76ee\\u524d|"
        + "\\u4eca\\u5929|\\u4eca\\u65e5)\\s*)?"
        + "(?:\\u591a\\u5c11\\u5ea6|\\u5e7e\\u5ea6|\\u51e0\\u5ea6)"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex ForecastWeatherQuestionZh = new(
        "^\\s*(?:(?:\\u8acb|\\u8bf7)(?:\\u554f|\\u95ee)?\\s*)?"
        + "(?=[^\\r\\n]{0,80}(?:\\u5982\\u4f55|\\u600e\\u9ebc|\\u600e\\u4e48|"
        + "\\u600e\\u6a23|\\u600e\\u6837|\\u591a\\u5c11\\u5ea6|"
        + "\\u5e7e\\u5ea6|\\u51e0\\u5ea6|\\u6703\\u4e0d\\u6703|"
        + "\\u4f1a\\u4e0d\\u4f1a|\\u662f\\u5426|\\u6709\\u6c92\\u6709|"
        + "\\u6709\\u6ca1\\u6709|\\u55ce|\\u5417|\\u5462|[?\\uff1f]))"
        + "(?:\\u660e\\u5929|\\u660e\\u65e5|\\u5f8c\\u5929|\\u540e\\u5929|"
        + "\\u672a\\u4f86|\\u672a\\u6765|\\u9031\\u672b|\\u5468\\u672b|"
        + "\\u4e0b\\u9031|\\u4e0b\\u5468)\\s*(?:\\u7684\\s*)?"
        + "(?:(?:\\u5929\\u6c23|\\u5929\\u6c14|\\u6c23\\u6eab|\\u6c14\\u6e29|"
        + "\\u6eab\\u5ea6|\\u6e29\\u5ea6)\\s*"
        + "(?:\\u5982\\u4f55|\\u600e\\u9ebc\\u6a23|\\u600e\\u4e48\\u6837|"
        + "\\u600e\\u6a23|\\u600e\\u6837|\\u591a\\u5c11\\u5ea6|"
        + "\\u5e7e\\u5ea6|\\u51e0\\u5ea6)?|"
        + "(?:(?:\\u6703\\u4e0d\\u6703|\\u4f1a\\u4e0d\\u4f1a|\\u6703|\\u4f1a|"
        + "\\u662f\\u5426|\\u6709\\u6c92\\u6709|\\u6709\\u6ca1\\u6709)\\s*)?"
        + "(?:\\u4e0b\\u96e8|\\u4e0b\\u96ea|\\u964d\\u96e8|\\u964d\\u96ea|"
        + "\\u591a\\u5c11\\u5ea6|\\u5e7e\\u5ea6|\\u51e0\\u5ea6))"
        + "\\s*(?:\\u55ce|\\u5417|\\u5462)?"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex TodayPrecipitationQuestionZh = new(
        "^\\s*(?:(?:\\u8acb|\\u8bf7)(?:\\u554f|\\u95ee)?\\s*)?"
        + "(?=[^\\r\\n]{0,48}(?:\\u6703\\u4e0d\\u6703|\\u4f1a\\u4e0d\\u4f1a|"
        + "\\u662f\\u5426|\\u6709\\u6c92\\u6709|\\u6709\\u6ca1\\u6709|"
        + "\\u55ce|\\u5417|\\u5462|[?\\uff1f]))"
        + "(?:\\u4eca\\u5929|\\u4eca\\u65e5)\\s*"
        + "(?:(?:\\u6703\\u4e0d\\u6703|\\u4f1a\\u4e0d\\u4f1a|\\u6703|\\u4f1a|"
        + "\\u662f\\u5426|\\u6709\\u6c92\\u6709|\\u6709\\u6ca1\\u6709)\\s*)?"
        + "(?:\\u4e0b\\u96e8|\\u4e0b\\u96ea|\\u964d\\u96e8|\\u964d\\u96ea)"
        + "\\s*(?:\\u55ce|\\u5417|\\u5462)?"
        + "\\s*[?\\uff1f.!\\u3002\\uff01]*\\s*$",
        Options);

    private static readonly Regex NonConfiguredWeatherQuestionEn = new(
        "^\\s*(?:please\\s+)?(?:"
        + "(?:(?:what(?:'s|\\s+is)|how(?:'s|\\s+is))\\s+(?:the\\s+)?)?"
        + "(?:weather|temperature)\\s+(?:in|at|for)\\s+.{1,64}|"
        + "(?:will|is)\\s+(?:it|there)\\s+(?:rain|snow)"
        + "(?:\\s+(?:today|tomorrow|later|this\\s+weekend|next\\s+week))?"
        + ")\\s*[?.!]*\\s*$",
        Options);

    private static readonly Regex NonConfiguredWeatherQuestionJa = new(
        "^\\s*(?:(?:\\u660e\\u65e5|\\u660e\\u5f8c\\u65e5|\\u6765\\u9031|"
        + "\\u4eca\\u9031\\u672b).{0,16}(?:\\u5929\\u6c17|\\u96e8|\\u96ea|"
        + "\\u6c17\\u6e29)|.{1,24}(?:\\u306e)?(?:\\u5929\\u6c17|\\u6c17\\u6e29)"
        + "(?:\\u306f)?(?:\\u3069\\u3046|\\u4f55\\u5ea6))"
        + "(?:\\u3067\\u3059\\u304b|\\u304b)?\\s*[?\\uff1f\\u3002]*\\s*$",
        Options);

    internal static PlayerReplyCurrentFactRequirement Classify(
        string userText)
    {
        if (IsCurrentConfiguredWeatherRequest(userText))
        {
            return PlayerReplyCurrentFactRequirement
                .CurrentConfiguredWeather;
        }
        if (IsNonConfiguredWeatherRequest(userText))
        {
            return PlayerReplyCurrentFactRequirement.GeneralSearch;
        }
        if (!GeminiGoogleSearchIntent.ShouldForceSearch(userText))
            return PlayerReplyCurrentFactRequirement.None;

        return PlayerReplyCurrentFactRequirement.GeneralSearch;
    }

    internal static PlayerReplyCurrentFactAdmission Admit(
        PlayerReplyCurrentFactRequirement requirement,
        LocalWeatherContextObservation observation,
        long nowTimestamp)
        => Admit(
            requirement,
            observation,
            observation?.ConfigurationFingerprint
                ?? throw new ArgumentNullException(nameof(observation)),
            nowTimestamp);

    internal static PlayerReplyCurrentFactAdmission Admit(
        PlayerReplyCurrentFactRequirement requirement,
        LocalWeatherContextObservation observation,
        string activeConfigurationFingerprint,
        long nowTimestamp)
    {
        if (!Enum.IsDefined(typeof(PlayerReplyCurrentFactRequirement), requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
        if (observation == null)
            throw new ArgumentNullException(nameof(observation));
        if (string.IsNullOrWhiteSpace(activeConfigurationFingerprint)
            || activeConfigurationFingerprint.Length > 128
            || activeConfigurationFingerprint.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A bounded active weather configuration fingerprint is required.",
                nameof(activeConfigurationFingerprint));
        }
        if (nowTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(nowTimestamp));

        var weatherFresh = observation.IsFresh(nowTimestamp)
            && string.Equals(
                observation.ConfigurationFingerprint,
                activeConfigurationFingerprint,
                StringComparison.Ordinal);
        return requirement switch
        {
            PlayerReplyCurrentFactRequirement.None =>
                PlayerReplyCurrentFactAdmission.Ordinary(
                    weatherFresh
                        ? observation.PromptContext
                        : string.Empty),
            PlayerReplyCurrentFactRequirement.CurrentConfiguredWeather
                when weatherFresh =>
                PlayerReplyCurrentFactAdmission.AdmittedWeather(
                    observation.PromptContext),
            PlayerReplyCurrentFactRequirement.CurrentConfiguredWeather =>
                PlayerReplyCurrentFactAdmission.Excluded(requirement),
            PlayerReplyCurrentFactRequirement.GeneralSearch =>
                PlayerReplyCurrentFactAdmission.Excluded(requirement),
            _ => throw new ArgumentOutOfRangeException(nameof(requirement))
        };
    }

    private static bool IsCurrentConfiguredWeatherRequest(string userText)
        => !string.IsNullOrWhiteSpace(userText)
            && (CurrentConfiguredWeatherZh.IsMatch(userText)
                || CurrentConfiguredWeatherEn.IsMatch(userText)
                || CurrentConfiguredWeatherJa.IsMatch(userText));

    private static bool IsNonConfiguredWeatherRequest(string userText)
        => !string.IsNullOrWhiteSpace(userText)
            && (NamedWeatherQuestionZh.IsMatch(userText)
                || NamedTemperatureQuestionZh.IsMatch(userText)
                || ForecastWeatherQuestionZh.IsMatch(userText)
                || TodayPrecipitationQuestionZh.IsMatch(userText)
                || NonConfiguredWeatherQuestionEn.IsMatch(userText)
                || NonConfiguredWeatherQuestionJa.IsMatch(userText));
}
