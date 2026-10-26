using System.Windows.Data;
using System.Windows.Markup;
using System.Globalization;

namespace LilithControlCenter;

// Explicit resource keys only: never traverse or translate a conversation, input,
// credential, model response, native tool identifier or arbitrary runtime value.
[MarkupExtensionReturnType(typeof(string))]
public sealed class UiTextExtension : MarkupExtension
{
    public string Key { get; set; }
    public UiTextExtension(string key) => Key = key;
    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding("[" + Key + "]") { Source = ControlCenterLocalization.Current, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}

// Only these product-owned status fields may be localized at the view boundary.
// Never use this extension for conversations, model IDs or native tool results.
public sealed class OwnedUiLabelExtension : MarkupExtension
{
    public string Path { get; }
    public OwnedUiLabelExtension(string path)
    {
        if (path is not "ConnectionText" and not "ConnectionDetail" and not "LocalVoiceOutputState")
            throw new ArgumentException("Not an owned UI status field.", nameof(path));
        Path = path;
    }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new OwnedLabelConverter() };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(new Binding(nameof(ControlCenterLocalization.Language)) { Source = ControlCenterLocalization.Current });
        return binding.ProvideValue(serviceProvider);
    }
    private sealed class OwnedLabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
            => values.Length > 0 && values[0] is string label
                ? ControlCenterLocalization.Current.RefreshOwnedUiLabel(label) : "—";
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
