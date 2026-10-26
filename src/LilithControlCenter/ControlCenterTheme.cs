using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace LilithControlCenter;

/// <summary>Owns presentation resources only; it never reads or saves settings.</summary>
public sealed class ControlCenterTheme : INotifyPropertyChanged
{
    public static ControlCenterTheme Current { get; } = new();

    // Every Color in App.xaml has an explicit dark counterpart. Adding a token
    // without a counterpart fails before any application resource is replaced.
    private static readonly IReadOnlyDictionary<string, Color> DarkColors =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["WindowColor"] = ParseColor("#121821"),
            ["RailColor"] = ParseColor("#151D29"),
            ["PanelColor"] = ParseColor("#1A2230"),
            ["PanelRaisedColor"] = ParseColor("#222E40"),
            ["PanelHoverColor"] = ParseColor("#2A384C"),
            ["TextColor"] = ParseColor("#EDF2F8"),
            ["MutedColor"] = ParseColor("#A9B7CB"),
            ["AccentColor"] = ParseColor("#FF7D86"),
            ["AccentStrongColor"] = ParseColor("#FF9AA2"),
            ["CoralColor"] = ParseColor("#FF626B"),
            ["GoodColor"] = ParseColor("#72D5B0"),
            ["WarningColor"] = ParseColor("#EDC17A"),
            ["DangerColor"] = ParseColor("#FF8598"),
            ["InputColor"] = ParseColor("#151D29"),
            ["BorderColor"] = ParseColor("#708096"),
            ["SelectedColor"] = ParseColor("#442E3A"),
            ["OnAccentColor"] = ParseColor("#FFFFFF"),
            ["DisabledColor"] = ParseColor("#283241"),
            ["HeroStartColor"] = ParseColor("#1A2433"),
            ["HeroMiddleColor"] = ParseColor("#202C3E"),
            ["HeroEndColor"] = ParseColor("#2A394F"),
            ["ShadowColor"] = ParseColor("#000000"),
            ["WindowBorderColor"] = ParseColor("#708096"),
            ["TitleBarColor"] = ParseColor("#171F2C"),
            ["NavSelectedColor"] = ParseColor("#3B2935"),
            ["DecorativeColor"] = ParseColor("#2C3B51"),
            ["HeroAccentColor"] = ParseColor("#633F4A"),
            ["FocusColor"] = ParseColor("#EDF2F8"),
            ["OverlayColor"] = ParseColor("#00121821"),
            ["SuccessSurfaceColor"] = ParseColor("#17382F"),
            ["WarningSurfaceColor"] = ParseColor("#3C3020"),
            ["DangerSurfaceColor"] = ParseColor("#402B36"),
            ["AccentButtonColor"] = ParseColor("#CF4051"),
            ["DangerButtonColor"] = ParseColor("#C94157"),
            ["SwitchOffColor"] = ParseColor("#6A7C94"),
            ["SwitchOffHoverColor"] = ParseColor("#70839B"),
            ["SwitchOnColor"] = ParseColor("#D64F60"),
            ["SwitchOnHoverColor"] = ParseColor("#E16170"),
            ["SwitchIndeterminateColor"] = ParseColor("#96702B"),
            ["SwitchThumbColor"] = ParseColor("#FFFFFF"),
            ["SwitchThumbDetailColor"] = ParseColor("#334157"),
            ["SwitchDisabledTrackColor"] = ParseColor("#38465A"),
            ["SwitchDisabledThumbColor"] = ParseColor("#A9B7CB")
        };

    // These exact values belong to our presentation view models. Do not infer a
    // semantic role for arbitrary colors, external content or a shared white fill.
    private static readonly IReadOnlyDictionary<Color, string> OwnedForegroundBrushes =
        new Dictionary<Color, string>
        {
            [ParseColor("#17233E")] = "TextBrush",
            [ParseColor("#52627A")] = "MutedBrush",
            [ParseColor("#CF4051")] = "AccentBrush",
            [ParseColor("#B93343")] = "AccentStrongBrush",
            [ParseColor("#1D795A")] = "GoodBrush",
            [ParseColor("#936110")] = "WarningBrush",
            [ParseColor("#BD354B")] = "DangerBrush",
            [ParseColor("#FF626B")] = "CoralBrush"
        };

    private ResourceDictionary? _resources;
    private IReadOnlyDictionary<string, Color>? _lightColors;
    private IReadOnlyDictionary<string, string>? _brushColorKeys;
    private LinearGradientBrush? _lightHero;

    public bool IsDark { get; private set; }
    public int Revision { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    private ControlCenterTheme() { }

    public void Apply(Application app, bool isDark)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Dispatcher.VerifyAccess();

        if (_resources is null)
            CaptureLightPalette(app.Resources);
        else if (!ReferenceEquals(_resources, app.Resources))
            throw new InvalidOperationException("The theme belongs to one application resource dictionary.");

        var colors = isDark ? DarkColors : _lightColors!;
        var replacements = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, color) in colors)
            replacements.Add(key, color);
        foreach (var (brushKey, colorKey) in _brushColorKeys!)
            replacements.Add(brushKey, CreateBrush(colors[colorKey]));

        var hero = _lightHero!.CloneCurrentValue();
        hero.GradientStops[0].Color = colors["HeroStartColor"];
        hero.GradientStops[1].Color = colors["HeroMiddleColor"];
        hero.GradientStops[2].Color = colors["HeroEndColor"];
        hero.Freeze();
        replacements.Add("HeroBrush", hero);

        // Replacing only Color leaves application-owned Freezables stale in WPF.
        // Prepare every replacement first, then replace all brushes on the UI
        // thread before notifying bound presentation colors through Revision.
        foreach (var (key, value) in replacements)
            app.Resources[key] = value;

        IsDark = isDark;
        Revision++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDark)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Revision)));
    }

    public Brush ResolveBrush(object? value)
    {
        if (!TryGetColor(value, out var color))
            return value as Brush ?? Brushes.Transparent;

        var resources = _resources ?? Application.Current?.Resources;
        if (OwnedForegroundBrushes.TryGetValue(color, out var key)
            && resources?[key] is SolidColorBrush themed)
        {
            if (value is SolidColorBrush original
                && (original.Opacity != 1 || !original.Transform.Value.IsIdentity
                    || !original.RelativeTransform.Value.IsIdentity))
            {
                var preserved = themed.CloneCurrentValue();
                preserved.Opacity = original.Opacity;
                preserved.Transform = original.Transform.CloneCurrentValue();
                preserved.RelativeTransform = original.RelativeTransform.CloneCurrentValue();
                preserved.Freeze();
                return preserved;
            }
            return themed;
        }

        // Valid unknown colors and brushes keep their exact value. A missing or
        // invalid binding has no invented status and paints transparently.
        return value as Brush ?? CreateBrush(color);
    }

    private void CaptureLightPalette(ResourceDictionary resources)
    {
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        var brushKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in resources)
        {
            if (entry.Key is not string key)
                continue;
            if (entry.Value is Color color)
                colors.Add(key, color);
            else if (entry.Value is SolidColorBrush)
            {
                if (!key.EndsWith("Brush", StringComparison.Ordinal))
                    throw new InvalidOperationException("A theme brush has no semantic key: " + key);
                brushKeys.Add(key, key[..^"Brush".Length] + "Color");
            }
        }

        if (!colors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(DarkColors.Keys))
            throw new InvalidOperationException("Light and dark theme color keys must match exactly.");
        if (brushKeys.Values.Any(key => !colors.ContainsKey(key)))
            throw new InvalidOperationException("Every semantic brush needs a theme color.");
        if (resources["HeroBrush"] is not LinearGradientBrush hero || hero.GradientStops.Count != 3)
            throw new InvalidOperationException("The theme needs its three-stop hero gradient.");

        _lightColors = colors;
        _brushColorKeys = brushKeys;
        _lightHero = hero.CloneCurrentValue();
        _resources = resources;
    }

    private static bool TryGetColor(object? value, out Color color)
    {
        if (value is SolidColorBrush brush)
        {
            color = brush.Color;
            return true;
        }
        if (value is Color typedColor)
        {
            color = typedColor;
            return true;
        }
        if (value is string text)
        {
            try
            {
                color = ParseColor(text);
                return true;
            }
            catch (FormatException) { }
            catch (NotSupportedException) { }
            catch (ArgumentException) { }
        }
        color = default;
        return false;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);
}
