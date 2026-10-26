using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithControlCenter;

internal sealed record ControlCenterPreferences(int Schema, long Revision, string Language, bool ChatSendSoundEnabled,
    bool DarkModeEnabled = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FollowSystemTheme = false)
{
    internal static readonly string[] SupportedLanguages = new[] { "zh-TW", "zh-CN", "ja-JP", "en-US", "ru-RU" };

    internal static string ResolveSystemLanguage(string culture)
    {
        var parts = culture.Split('-');
        if (parts[0].Equals("ja", StringComparison.OrdinalIgnoreCase)) return "ja-JP";
        if (parts[0].Equals("ru", StringComparison.OrdinalIgnoreCase)) return "ru-RU";
        if (!parts[0].Equals("zh", StringComparison.OrdinalIgnoreCase)) return "en-US";
        if (parts.Any(p => p.Equals("Hant", StringComparison.OrdinalIgnoreCase))) return "zh-TW";
        if (parts.Any(p => p.Equals("Hans", StringComparison.OrdinalIgnoreCase))) return "zh-CN";
        return parts.Any(p => p.Equals("TW", StringComparison.OrdinalIgnoreCase)
            || p.Equals("HK", StringComparison.OrdinalIgnoreCase)
            || p.Equals("MO", StringComparison.OrdinalIgnoreCase)) ? "zh-TW" : "zh-CN";
    }

    internal static ControlCenterPreferences Default(string culture)
        => new(1, 0, ResolveSystemLanguage(culture), true, false);

    internal bool IsValid => Schema == 1 && Revision >= 0 && Revision < long.MaxValue
        && SupportedLanguages.Contains(Language, StringComparer.Ordinal);
}

internal sealed class ControlCenterPreferenceStore
{
    private readonly string _path;
    internal ControlCenterPreferenceStore(string localApplicationData)
        => _path = Path.Combine(Path.GetFullPath(localApplicationData), "LilithAiMod", "Config", "control-center", "ui-v1.json");

    internal static ControlCenterPreferenceStore ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal ControlCenterPreferences Load(string culture, out bool writable)
    {
        writable = false;
        try
        {
            CheckPath();
            if (!File.Exists(_path)) { writable = true; return ControlCenterPreferences.Default(culture); }
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 4096) return ControlCenterPreferences.Default(culture);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var names = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length is < 4 or > 6
                || names.Distinct(StringComparer.Ordinal).Count() != names.Length
                || names.Any(n => n is not ("Schema" or "Revision" or "Language" or "ChatSendSoundEnabled" or "DarkModeEnabled" or "FollowSystemTheme"))
                || !root.TryGetProperty("Schema", out _)
                || !root.TryGetProperty("Revision", out _)
                || !root.TryGetProperty("Language", out _)
                || !root.TryGetProperty("ChatSendSoundEnabled", out _)
                || (root.TryGetProperty("DarkModeEnabled", out var darkMode)
                    && darkMode.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                || (root.TryGetProperty("FollowSystemTheme", out var followSystem)
                    && followSystem.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
                return ControlCenterPreferences.Default(culture);
            var value = root.Deserialize<ControlCenterPreferences>();
            if (value?.IsValid != true) return ControlCenterPreferences.Default(culture);
            writable = true;
            return value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return ControlCenterPreferences.Default(culture); }
    }

    internal ControlCenterPreferences Save(ControlCenterPreferences expected, string language, bool sound,
        bool? darkMode = null, bool? followSystemTheme = null)
    {
        CheckPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var writer = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var current = Load(expected.Language, out var writable);
        if (!writable || current != expected) throw new IOException("ui.preferences_changed_or_invalid");
        var next = new ControlCenterPreferences(1, checked(current.Revision + 1), language, sound,
            darkMode ?? current.DarkModeEnabled,
            followSystemTheme ?? (darkMode.HasValue ? false : current.FollowSystemTheme));
        if (!next.IsValid) throw new ArgumentException("Unsupported UI preference.");
        CheckPath();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, next); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".previous");
            else File.Move(temporary, _path);
            return next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void CheckPath()
    {
        foreach (var suffix in new[] { ".lock", ".previous" })
            if (File.Exists(_path + suffix) && (File.GetAttributes(_path + suffix) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("UI preference companion cannot follow a link.");
        for (var path = _path; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("UI preference path cannot follow a link.");
    }
}
