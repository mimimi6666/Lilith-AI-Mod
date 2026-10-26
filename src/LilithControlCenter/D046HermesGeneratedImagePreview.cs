using System.IO;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace LilithControlCenter;

internal static class D046HermesGeneratedImagePreview
{
    // Bound actual native file/decode workers, including an unexpectedly stalled
    // disk/codec. Cancelling a view never launches replacement workers over it.
    private static readonly SemaphoreSlim Workers = new(2, 2);

    internal static async Task LoadThumbnailAsync(Image thumbnail,
        Func<D046HermesGeneratedImageAttachment, CancellationToken, Task<BitmapImage?>>? loader = null)
    {
        thumbnail.Dispatcher.VerifyAccess();
        var delivery = thumbnail.DataContext as D046HermesGeneratedImageDeliveryViewModel;
        var image = delivery?.Attachment ?? thumbnail.DataContext as D046HermesGeneratedImageAttachment;
        if (image is null || delivery is { IsActive: false }) return;
        if (thumbnail.Tag is ThumbnailView prior) prior.Detach();
        var view = new ThumbnailView(image, delivery);
        var dataContext = thumbnail.DataContext;
        thumbnail.Tag = view;
        thumbnail.Source = null;
        try
        {
            var preview = await (loader is null ? LoadAsync(image, 256, view.Cancellation.Token) : loader(image, view.Cancellation.Token));
            await thumbnail.Dispatcher.InvokeAsync(() =>
            {
                if (thumbnail.IsLoaded && ReferenceEquals(thumbnail.Tag, view)
                    && ReferenceEquals(thumbnail.DataContext, dataContext)
                    && (delivery is null || delivery.IsCurrentView(view.Generation)))
                {
                    thumbnail.Source = preview;
                    // The actual current Image owns detached pixels before the
                    // message is allowed to hide a matching MEDIA directive.
                    delivery?.CompleteView(view.Generation, preview is not null);
                }
            });
        }
        catch (OperationCanceledException) { }
        finally { view.ReleaseRead(); }
    }

    internal static void UnloadThumbnail(Image thumbnail)
    {
        if (thumbnail.Tag is ThumbnailView view) view.Detach();
        thumbnail.Tag = null;
        thumbnail.Source = null;
    }

    internal static async Task<BitmapImage?> LoadExpandedAsync(D046HermesGeneratedImageDeliveryViewModel delivery,
        CancellationToken cancellationToken,
        Func<D046HermesGeneratedImageAttachment, CancellationToken, Task<BitmapImage?>>? loader = null)
    {
        if (!delivery.IsDelivered) return null;
        var generation = delivery.ViewGeneration;
        var preview = await (loader is null ? LoadAsync(delivery.Attachment, 2048, cancellationToken)
            : loader(delivery.Attachment, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (!delivery.IsCurrentView(generation)) return null;
        if (preview is null) delivery.ReportExpandedPreviewFailure();
        return preview;
    }

    private sealed class ThumbnailView
    {
        private bool _released;
        private readonly D046HermesGeneratedImageDeliveryViewModel? _delivery;
        internal CancellationTokenSource Cancellation { get; } = new();
        internal long Generation { get; }
        internal ThumbnailView(D046HermesGeneratedImageAttachment image, D046HermesGeneratedImageDeliveryViewModel? delivery)
        { _delivery = delivery; Generation = delivery?.BeginView(Cancellation) ?? 0; }
        internal void Detach()
        {
            if (!_released) Cancellation.Cancel();
            _delivery?.EndView(Generation);
            ReleaseRead();
        }
        internal void ReleaseRead()
        {
            if (_released) return;
            _released = true;
            _delivery?.ReleaseRead(Generation, Cancellation);
            Cancellation.Dispose();
        }
    }

    internal static async Task<BitmapImage?> LoadAsync(D046HermesGeneratedImageAttachment image,
        int edge, CancellationToken cancellationToken, string? imagesRoot = null)
    {
        await Workers.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = D046HermesGeneratedImagePresentation.ReadVerified(image,
                    imagesRoot ?? D046HermesGeneratedImagePresentation.CurrentImagesRoot);
                if (bytes is null) return null;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    D046HermesPendingImage.DemandSignature(image.MediaType, bytes);
                    // Reuse bounded dimensions/downsampling and Freeze. Only
                    // detached pixels leave this background worker, never paths.
                    var preview = D046HermesPendingImage.CreatePreview(bytes, image.MediaType, Math.Clamp(edge, 1, 2048));
                    cancellationToken.ThrowIfCancellationRequested();
                    return preview;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or FileFormatException or NotSupportedException
                    or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
                { return null; }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { Workers.Release(); }
    }
}
