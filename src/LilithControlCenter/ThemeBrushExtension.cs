using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace LilithControlCenter;

// Status projections retain their existing values. Only known presentation colors
// are resolved against the active palette, including when an existing view stays open.
[MarkupExtensionReturnType(typeof(Brush))]
public sealed class ThemeBrushExtension : MarkupExtension
{
    public string Path { get; }

    public ThemeBrushExtension(string path) => Path = path;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => CreateBinding(new Binding(Path)).ProvideValue(serviceProvider);

    internal static MultiBinding CreateBinding(BindingBase valueBinding)
    {
        var binding = new MultiBinding
        {
            Mode = BindingMode.OneWay,
            Converter = ThemeBrushConverter.Instance
        };
        binding.Bindings.Add(valueBinding);
        binding.Bindings.Add(new Binding(nameof(ControlCenterTheme.Revision))
        {
            Source = ControlCenterTheme.Current,
            Mode = BindingMode.OneWay
        });
        return binding;
    }

    private sealed class ThemeBrushConverter : IMultiValueConverter
    {
        internal static readonly ThemeBrushConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            => ControlCenterTheme.Current.ResolveBrush(values.Length > 0 ? values[0] : null);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
