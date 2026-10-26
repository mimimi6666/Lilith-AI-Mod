using System.Windows;

namespace LilithControlCenter;

// Presentation only. The caller owns the operation and must require true;
// Escape, close and the initial keyboard/default action all decline it.
public partial class ThemedConfirmationDialog : Window
{
    public ThemedConfirmationDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
    }

    private void Dialog_Loaded(object sender, RoutedEventArgs e) => CancelButton.Focus();
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
