namespace LilithControlCenter;

// UI-thread state only. Observing Windows never writes the player's preference.
internal sealed class ControlCenterThemeSelection
{
    private readonly Func<ControlCenterPreferences> _preferences;
    private readonly Func<bool?> _systemDarkMode;
    private readonly Action<bool> _apply;
    private bool? _appliedDarkMode;

    internal ControlCenterThemeSelection(Func<ControlCenterPreferences> preferences,
        Func<bool?> systemDarkMode, Action<bool> apply)
    {
        _preferences = preferences;
        _systemDarkMode = systemDarkMode;
        _apply = apply;
    }

    internal void Refresh()
    {
        var preference = _preferences();
        var dark = preference.FollowSystemTheme
            ? _systemDarkMode() ?? _appliedDarkMode ?? preference.DarkModeEnabled
            : preference.DarkModeEnabled;
        if (_appliedDarkMode == dark) return;
        _apply(dark);
        _appliedDarkMode = dark;
    }

    internal static bool? ReadDarkModeValue(object? appsUseLightTheme) => appsUseLightTheme switch
    {
        0 => true,
        1 => false,
        _ => null
    };
}
