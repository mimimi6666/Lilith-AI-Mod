using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LilithControlCenter;

internal enum D046HermesGeneratedImageDeliveryState { Pending, Loading, Delivered, Failed }

/// <summary>Ephemeral view acknowledgement; nomination never means the image was decoded.</summary>
internal sealed class D046HermesGeneratedImageDeliveryViewModel : INotifyPropertyChanged
{
    private readonly Action _changed;
    private CancellationTokenSource? _readCancellation;
    private long _viewGeneration;
    private bool _active = true;
    private D046HermesGeneratedImageDeliveryState _state;

    internal D046HermesGeneratedImageDeliveryViewModel(D046HermesGeneratedImageAttachment attachment, Action changed)
    { Attachment = attachment; _changed = changed; }

    public D046HermesGeneratedImageAttachment Attachment { get; }
    public string FilePath => Attachment.FilePath;
    public string FileName => Attachment.FileName;
    public bool IsDelivered => _active && _state == D046HermesGeneratedImageDeliveryState.Delivered;
    public bool IsFailed => _active && _state == D046HermesGeneratedImageDeliveryState.Failed;
    public bool IsLoading => _active && _state is D046HermesGeneratedImageDeliveryState.Pending or D046HermesGeneratedImageDeliveryState.Loading;
    public string FallbackPath => IsFailed ? Attachment.FilePath : string.Empty;
    public string FailureText => ControlCenterLocalization.Current.Get("chat.image.generated.failed");
    internal bool IsActive => _active;
    internal long ViewGeneration => _viewGeneration;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal long BeginView(CancellationTokenSource cancellation)
    {
        CancelRead();
        _readCancellation = cancellation;
        _viewGeneration++;
        _state = D046HermesGeneratedImageDeliveryState.Loading;
        Notify();
        return _viewGeneration;
    }

    internal bool IsCurrentView(long generation) => _active && generation == _viewGeneration;
    internal bool CompleteView(long generation, bool decoded)
    {
        if (!IsCurrentView(generation)) return false;
        _state = decoded ? D046HermesGeneratedImageDeliveryState.Delivered : D046HermesGeneratedImageDeliveryState.Failed;
        Notify();
        return true;
    }

    internal void ReleaseRead(long generation, CancellationTokenSource cancellation)
    {
        if (generation == _viewGeneration && ReferenceEquals(_readCancellation, cancellation)) _readCancellation = null;
    }

    internal void EndView(long generation)
    {
        if (!IsCurrentView(generation)) return;
        CancelRead();
        _viewGeneration++;
        // Failure is useful fallback, not a transient loaded-view claim.
        if (_state != D046HermesGeneratedImageDeliveryState.Failed) _state = D046HermesGeneratedImageDeliveryState.Pending;
        Notify();
    }

    internal void ReportExpandedPreviewFailure()
    {
        if (!_active) return;
        CancelRead();
        _viewGeneration++;
        _state = D046HermesGeneratedImageDeliveryState.Failed;
        Notify();
    }

    internal void Deactivate()
    {
        CancelRead();
        _active = false;
        _viewGeneration++;
        _state = D046HermesGeneratedImageDeliveryState.Pending;
        Notify();
    }

    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FailureText)));
    private void CancelRead()
    {
        var cancellation = _readCancellation;
        _readCancellation = null;
        cancellation?.Cancel();
    }
    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        _changed();
    }
}

internal sealed partial class D046HermesTimelineItemViewModel
{
    private IReadOnlyCollection<string> _deliveredGeneratedImagePaths = Array.Empty<string>();
    private IReadOnlyCollection<string> _pendingGeneratedImagePaths = Array.Empty<string>();

    // Pending images have an explicit loading placeholder, never a success
    // claim or exposed transport path. A failed decode restores the raw text.
    public string GeneratedImageDisplayText => IsAssistantMessage && HasConfirmedMessageCompletion
        ? D046HermesGeneratedImageDeliveryPolicy.ReplacePendingDirectives(
            D046HermesGeneratedImageDeliveryPolicy.RemoveDeliveredDirectives(Text, _deliveredGeneratedImagePaths),
            _pendingGeneratedImagePaths, ControlCenterLocalization.Current.Get("chat.image.generated.loading")) : Text;
    public bool HasGeneratedImageDisplayContent => IsStreaming || !string.IsNullOrWhiteSpace(GeneratedImageDisplayText);

    internal void SetGeneratedImagePresentationPaths(IReadOnlyCollection<string> delivered, IReadOnlyCollection<string> pending)
    {
        _deliveredGeneratedImagePaths = delivered;
        _pendingGeneratedImagePaths = pending;
        RefreshGeneratedImageDisplay();
    }
    private void RefreshGeneratedImageDisplay()
    {
        OnPropertyChanged(nameof(GeneratedImageDisplayText));
        OnPropertyChanged(nameof(HasGeneratedImageDisplayContent));
    }
}

internal sealed partial class D052HermesTurnPresentationViewModel
{
    private readonly Dictionary<string, D046HermesGeneratedImageAttachment> _generatedImagesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D046HermesGeneratedImageDeliveryViewModel> _generatedImagesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<D046HermesTimelineItemViewModel, IReadOnlyList<D046HermesGeneratedImageAttachment>> _generatedCandidatesByMessage = new();
    public ObservableCollection<D046HermesGeneratedImageDeliveryViewModel> GeneratedImages { get; } = new();
    public bool HasGeneratedImages => GeneratedImages.Count != 0;

    internal bool TryUpsertGeneratedImage(string key, D046HermesGeneratedImageAttachment image)
    {
        if (_generatedImagesByKey.TryGetValue(key, out var existing)) return existing == image;
        if (!TryAddGeneratedImage(image)) return false;
        _generatedImagesByKey.Add(key, image);
        return true;
    }

    private bool TryAddGeneratedImage(D046HermesGeneratedImageAttachment image)
    {
        if (_generatedImagesByPath.TryGetValue(image.FilePath, out var existing)) return existing.Attachment == image;
        if (GeneratedImages.Count >= 8) return false;
        var delivery = new D046HermesGeneratedImageDeliveryViewModel(image, RefreshGeneratedImageMessageText);
        _generatedImagesByPath.Add(image.FilePath, delivery);
        GeneratedImages.Add(delivery);
        OnPropertyChanged(nameof(HasGeneratedImages));
        RefreshGeneratedImageMessageText();
        return true;
    }

    internal void RefreshGeneratedImageCandidates(D046HermesTimelineItemViewModel message)
    {
        if (!message.IsAssistantMessage) return;
        // Incomplete/replaced streaming text cannot retain earlier nominations.
        if (IsStandaloneSessionRow || message.Kind != D046HermesEventKind.Message || !message.HasConfirmedMessageCompletion)
            _generatedCandidatesByMessage.Remove(message);
        else _generatedCandidatesByMessage[message] = D046HermesGeneratedImageDeliveryPolicy.GetCandidates(message.Text);
        PruneGeneratedImages();
        RefillGeneratedImageCandidates();
        message.SetGeneratedImagePresentationPaths(DeliveredGeneratedImagePaths(), PendingGeneratedImagePaths());
    }

    internal void RemoveGeneratedImageCandidates(D046HermesTimelineItemViewModel message)
    {
        _generatedCandidatesByMessage.Remove(message);
        PruneGeneratedImages();
        RefillGeneratedImageCandidates();
        message.SetGeneratedImagePresentationPaths(Array.Empty<string>(), Array.Empty<string>());
    }

    private void PruneGeneratedImages()
    {
        var retained = _generatedImagesByKey.Values.Select(image => image.FilePath)
            .Concat(_generatedCandidatesByMessage.Values.SelectMany(images => images).Select(image => image.FilePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _generatedImagesByPath.Where(pair => !retained.Contains(pair.Key)).ToArray())
        {
            _generatedImagesByPath.Remove(removed.Key);
            removed.Value.Deactivate();
            GeneratedImages.Remove(removed.Value);
        }
        OnPropertyChanged(nameof(HasGeneratedImages));
    }

    internal void DeactivateGeneratedImages()
    {
        foreach (var delivery in GeneratedImages) delivery.Deactivate();
    }

    private void RefillGeneratedImageCandidates()
    {
        foreach (var candidate in _generatedCandidatesByMessage.Values.SelectMany(images => images))
        {
            if (GeneratedImages.Count >= 8) break;
            TryAddGeneratedImage(candidate);
        }
    }

    private string[] DeliveredGeneratedImagePaths() => GeneratedImages.Where(image => image.IsDelivered).Select(image => image.FilePath).ToArray();
    private string[] PendingGeneratedImagePaths() => GeneratedImages.Where(image => image.IsLoading).Select(image => image.FilePath).ToArray();
    private void RefreshGeneratedImageMessageText()
    {
        var paths = DeliveredGeneratedImagePaths();
        var pending = PendingGeneratedImagePaths();
        foreach (var message in AssistantMessages) message.SetGeneratedImagePresentationPaths(paths, pending);
    }
}
