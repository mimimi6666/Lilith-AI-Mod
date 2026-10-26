using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace LilithControlCenter;

public sealed class ControlCenterLocalization : INotifyPropertyChanged
{
    public static ControlCenterLocalization Current { get; } = new();
    private readonly Dictionary<string, Dictionary<string, string>> _catalog;
    private string _language = ControlCenterPreferences.ResolveSystemLanguage(CultureInfo.CurrentUICulture.Name);
    public string Language => _language;
    // Pinned Hermes/Runtime command-localization metadata supports four locales.
    // New presentation locales must not invalidate chat or alter player text,
    // display_locale, speech_locale, or the backend's locale admission boundary.
    internal string NativeCommandLocale => _language == "ru-RU" ? "en-US" : _language;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal event Action? LanguageChanged;

    private ControlCenterLocalization()
    {
        using var source = typeof(ControlCenterLocalization).Assembly.GetManifestResourceStream(
            "LilithControlCenter.Localization.strings.json") ?? throw new InvalidDataException("UI language resources missing.");
        _catalog = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(source)
            ?? throw new InvalidDataException("Invalid UI language resources.");
    }

    public string this[string key] => Get(key);
    public string Get(string key, string? fallback = null)
        => _catalog.TryGetValue(key, out var values)
            ? values.GetValueOrDefault(_language) ?? values.GetValueOrDefault("en-US") ?? fallback ?? key
            : fallback ?? key;
    public string Format(string key, params object[] values)
        => string.Format(CultureInfo.GetCultureInfo(_language), Get(key), values);
    // Only for explicitly owned, fixed UI labels cached by a view model. Exact
    // resource matches only; never call this on chat, input, native payloads,
    // model names, commands or identifiers. Unknown diagnostic copy stays intact.
    internal string RefreshOwnedUiLabel(string label)
    {
        foreach (var (key, values) in _catalog)
            if ((key.StartsWith("ui.", StringComparison.Ordinal)
                || key.StartsWith("state.", StringComparison.Ordinal)
                || key.StartsWith("chat.", StringComparison.Ordinal)
                || key.StartsWith("budget.error.", StringComparison.Ordinal))
                && values.Values.Contains(label, StringComparer.Ordinal)) return Get(key);
        return label;
    }
    internal void SetLanguage(string language)
    {
        if (!ControlCenterPreferences.SupportedLanguages.Contains(language)) throw new ArgumentException("Unsupported UI language.");
        if (language == _language) return;
        _language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }
}
