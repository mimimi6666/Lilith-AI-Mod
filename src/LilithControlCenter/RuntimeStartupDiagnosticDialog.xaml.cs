using System.Windows;

namespace LilithControlCenter;

public partial class RuntimeStartupDiagnosticDialog : Window
{
    private readonly RuntimeStartupDiagnosticPresentation _diagnostic;

    internal RuntimeStartupDiagnosticDialog(RuntimeStartupDiagnosticPresentation diagnostic)
    {
        _diagnostic = diagnostic;
        InitializeComponent();
        var text = ControlCenterLocalization.Current;
        Title = text["startup.failureTitle"];
        ReasonText.Text = text[diagnostic.ReasonKey];
        StageText.Text = text.Format("startup.diagnostic.stage", text[diagnostic.StageKey]);
        ActionText.Text = text[diagnostic.ActionKey];
        DiagnosticText.Text = diagnostic.ToCopyText();
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
        => CopyStatusText.Text = ControlCenterLocalization.Current[_diagnostic.TryCopy(Clipboard.SetText)
            ? "startup.diagnostic.copied" : "startup.diagnostic.copyFailed"];

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
