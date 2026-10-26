using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LilithControlCenter;

public partial class D046HermesImagePreviewWindow : Window
{
    internal D046HermesImagePreviewWindow(string fileName, BitmapImage? image)
    {
        InitializeComponent();
        MaxWidth = SystemParameters.WorkArea.Width;
        MaxHeight = SystemParameters.WorkArea.Height;
        FileNameLabel.Text = fileName;
        PreviewImage.Source = image;
        UnavailableLabel.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Preview_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
    }
    private void Heading_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
            DragMove();
    }
}
