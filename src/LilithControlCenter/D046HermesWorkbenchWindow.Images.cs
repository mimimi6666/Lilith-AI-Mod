using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace LilithControlCenter;

internal sealed class D046HermesImagePreviewConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is D046HermesImageAttachment attachment ? Load(attachment, 256) : null;

    internal static BitmapImage? Load(D046HermesImageAttachment attachment, int edge)
        => LoadFromOwnedRoot(attachment, edge, D046HermesImagePresentation.CurrentImagesRoot);

    internal static BitmapImage? LoadFromOwnedRoot(D046HermesImageAttachment attachment, int edge, string imagesRoot)
    {
        var bytes = D046HermesImagePresentation.ReadVerified(attachment, imagesRoot);
        if (bytes is null) return null;
        try
        {
            D046HermesPendingImage.DemandSignature(attachment.MediaType, bytes);
            return D046HermesPendingImage.CreatePreview(bytes, attachment.MediaType, edge);
        }
        catch (Exception exception) when (exception is IOException or FileFormatException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            return null;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class D046HermesWorkbenchWindow
{
    private bool _generatedImagePreviewLoading;

    private async void GeneratedImage_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image thumbnail) await D046HermesGeneratedImagePreview.LoadThumbnailAsync(thumbnail);
    }

    private void GeneratedImage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image thumbnail) D046HermesGeneratedImagePreview.UnloadThumbnail(thumbnail);
    }

    private async void GeneratedImagePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_generatedImagePreviewLoading
            || sender is not Button { DataContext: D046HermesGeneratedImageDeliveryViewModel image } button
            || !image.IsDelivered) return;
        _generatedImagePreviewLoading = true;
        using var loading = new CancellationTokenSource();
        button.Tag = loading;
        try
        {
            var preview = await D046HermesGeneratedImagePreview.LoadExpandedAsync(image, loading.Token);
            if (preview is not null && IsVisible && button.IsLoaded && ReferenceEquals(button.DataContext, image))
                ShowImagePreview(image.FileName, preview);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(button.Tag, loading)) button.Tag = null; _generatedImagePreviewLoading = false; }
    }

    private void GeneratedImagePreview_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CancellationTokenSource loading }) loading.Cancel();
    }

    private void ImageComposer_PreviewDragOver(object sender, DragEventArgs e)
    {
        // Do not let WPF insert dropped file paths as a textual prompt.
        e.Handled = true;
        e.Effects = _viewModel.CanAddImage && TryGetSingleImageDrop(e.Data, out _)
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void ImageComposer_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!_viewModel.CanAddImage) return;
        if (!TryGetSingleImageDrop(e.Data, out var path))
        {
            ShowImageMessage(ControlCenterLocalization.Current.Get("chat.image.drop.one"));
            return;
        }
        try
        {
            _viewModel.AddImage(path);
            HermesMessageInput.Focus();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or FileFormatException
            or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowImageMessage(exception.Message);
        }
    }

    internal static bool TryGetSingleImageDrop(IDataObject data, out string path)
    {
        path = string.Empty;
        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop, autoConvert: false)
                || data.GetData(DataFormats.FileDrop, autoConvert: false) is not string[] { Length: 1 } files)
                return false;
            var candidate = files[0];
            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 32767
                || !Path.IsPathFullyQualified(candidate)
                || Path.GetExtension(candidate).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                return false;
            path = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException
            or System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            return false;
        }
    }

    private void PendingImagePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.PendingImage is not { } image) return;
        try { ShowImagePreview(image.FileName, image.CreateExpandedPreview()); }
        catch (Exception exception) when (exception is IOException or FileFormatException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            ShowImagePreview(image.FileName, null);
        }
    }

    private void SentImagePreview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: D046HermesImageAttachment image })
            ShowImagePreview(image.FileName, D046HermesImagePreviewConverter.Load(image, 2048));
    }

    private void ShowImagePreview(string fileName, BitmapImage? image)
    {
        // The preview receives only detached pixels. It cannot open a URL,
        // execute a file or survive as an orphan after its owning chat closes.
        var preview = new D046HermesImagePreviewWindow(fileName, image) { Owner = this };
        preview.ShowDialog();
    }

    private void ShowImageMessage(string message)
    {
        var dialog = new ThemedConfirmationDialog(
            ControlCenterLocalization.Current.Get("chat.image.add.failed"), message,
            ControlCenterLocalization.Current.Get("ui.cancel")) { Owner = this };
        dialog.ConfirmButton.Visibility = Visibility.Collapsed;
        dialog.ShowDialog();
    }
}
