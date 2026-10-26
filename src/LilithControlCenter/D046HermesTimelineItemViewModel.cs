using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace LilithControlCenter;

internal enum D046HermesTimelinePresentationKind
{
    UserMessage = 0,
    AssistantMessage = 1,
    Disclosure = 2
}

internal sealed partial class D046HermesTimelineItemViewModel : INotifyPropertyChanged
{
    private string _key;
    private string _text;
    private string _detail;
    private bool _isStreaming;
    private bool _hasConfirmedMessageCompletion;
    private bool _isExpanded;
    private readonly string _heading;
    private readonly string? _headingKey;
    private UsageBudgetFailure? _budgetFailure;
    private IReadOnlyList<UsageBudgetConstraint>? _budgetConstraints;

    internal D046HermesTimelineItemViewModel(
        string key,
        D046HermesEventKind kind,
        D046HermesMessageRole? messageRole,
        string heading,
        string text,
        string detail,
        DateTimeOffset occurredUtc,
        bool isStreaming,
        string? headingKey = null)
    {
        _key = key;
        Kind = kind;
        MessageRole = messageRole;
        PresentationKind = messageRole switch
        {
            D046HermesMessageRole.User
                => D046HermesTimelinePresentationKind.UserMessage,
            D046HermesMessageRole.Assistant
                => D046HermesTimelinePresentationKind.AssistantMessage,
            _ => D046HermesTimelinePresentationKind.Disclosure
        };
        _heading = heading;
        _headingKey = headingKey;
        _text = text;
        _detail = detail;
        OccurredUtc = occurredUtc;
        _isStreaming = isStreaming;
        _isExpanded = kind == D046HermesEventKind.Reasoning
            ? isStreaming
            : kind is D046HermesEventKind.Error
                or D046HermesEventKind.Clarification;
    }

    internal string Key => _key;
    public D046HermesEventKind Kind { get; }
    public D046HermesMessageRole? MessageRole { get; }
    public D046HermesTimelinePresentationKind PresentationKind { get; }
    public bool IsUserMessage =>
        PresentationKind == D046HermesTimelinePresentationKind.UserMessage;
    public bool IsAssistantMessage =>
        PresentationKind == D046HermesTimelinePresentationKind.AssistantMessage;
    public bool IsDisclosure =>
        PresentationKind == D046HermesTimelinePresentationKind.Disclosure;
    public string Heading => _budgetConstraints is { Count: > 0 }
        ? ControlCenterLocalization.Current["budget.constraints.title"] : _headingKey is null
        ? _heading : _headingKey == "chat.toolHeading"
            ? ControlCenterLocalization.Current.Format(_headingKey, _heading["逐夢之典 · ".Length..])
            : ControlCenterLocalization.Current.Get(_headingKey);
    public string Text => _budgetFailure is not null ? UsageBudgetErrorPresentation.Display(_budgetFailure)
        : _budgetConstraints is { Count: > 0 } ? UsageBudgetConstraintPresentation.Display(_budgetConstraints) : _text;
    public string Detail => _detail;
    public DateTimeOffset OccurredUtc { get; }
    public string TimeText => OccurredUtc.ToLocalTime().ToString("HH:mm:ss");
    public bool IsStreaming => _isStreaming;
    internal bool HasConfirmedMessageCompletion => _hasConfirmedMessageCompletion;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Heading));
        if (_budgetFailure is not null || _budgetConstraints is { Count: > 0 }) OnPropertyChanged(nameof(Text));
        RefreshGeneratedImageDisplay();
    }

    internal void SetBudgetMetadata(UsageBudgetFailure? failure, IReadOnlyList<UsageBudgetConstraint>? constraints)
    {
        _budgetFailure = failure;
        _budgetConstraints = constraints;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Heading));
    }

    internal void ApplyStream(
        D046HermesStreamOperation operation,
        string text,
        bool confirmedCompletion = true)
    {
        text ??= string.Empty;
        _hasConfirmedMessageCompletion = confirmedCompletion && operation == D046HermesStreamOperation.Completed
            && Kind == D046HermesEventKind.Message && IsAssistantMessage;
        switch (operation)
        {
            case D046HermesStreamOperation.Started:
            case D046HermesStreamOperation.Replaced:
                _text = text;
                _isStreaming = true;
                break;
            case D046HermesStreamOperation.Appended:
                _text += text;
                _isStreaming = true;
                break;
            case D046HermesStreamOperation.Completed:
                if (text.Length > 0)
                    _text = text;
                _isStreaming = false;
                if (Kind == D046HermesEventKind.Reasoning)
                    IsExpanded = false;
                break;
            default:
                _isStreaming = false;
                break;
        }

        OnPropertyChanged(nameof(Text));
        RefreshImagePresentation();
        RefreshGeneratedImageDisplay();
        OnPropertyChanged(nameof(IsStreaming));
    }

    internal void ApplyResolution(string text, string detail)
    {
        _text = text ?? string.Empty;
        _detail = detail ?? string.Empty;
        _isStreaming = false;
        _hasConfirmedMessageCompletion = false;
        OnPropertyChanged(nameof(Text));
        RefreshImagePresentation();
        RefreshGeneratedImageDisplay();
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsStreaming));
    }

    internal void ReplaceDisplayText(string text)
    {
        _text = text ?? string.Empty;
        OnPropertyChanged(nameof(Text));
        RefreshImagePresentation();
        RefreshGeneratedImageDisplay();
    }

    internal void RebindMessageIdentity(string key, string detail)
    {
        _key = key;
        _detail = detail;
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Detail));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal sealed class D052HermesAudioAttachmentViewModel
{
    internal D052HermesAudioAttachmentViewModel(
        string key,
        string filePath)
    {
        Key = key;
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }

    internal string Key { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public string DisplayText => ControlCenterLocalization.Current.Format("chat.audioAttachment", FileName);
}

internal static class D052HermesAudioPlaybackPolicy
{
    internal const long MaximumAudioBytes = 10 * 1024 * 1024;

    internal static bool TryResolve(
        string? proposedPath,
        string? localApplicationDataPath,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(proposedPath)
            || proposedPath.Length > 1024
            || !Path.IsPathFullyQualified(proposedPath)
            || string.IsNullOrWhiteSpace(localApplicationDataPath)
            || !Path.IsPathFullyQualified(localApplicationDataPath))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(proposedPath);
            var lilithRoot = Path.GetFullPath(Path.Combine(
                localApplicationDataPath,
                "LilithAiMod"));
            var runtimeRoot = Path.Combine(lilithRoot, "Runtime");
            var hermesRoot = Path.Combine(runtimeRoot, "hermes");
            var rootPrefix = hermesRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var relative = Path.GetRelativePath(hermesRoot, fullPath);
            var segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 4
                || segments[0] is "." or ".."
                || !string.Equals(segments[1], "cache", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(segments[2], "audio", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var admittedDirectories = new[]
            {
                lilithRoot,
                runtimeRoot,
                hermesRoot,
                Path.Combine(hermesRoot, segments[0]),
                Path.Combine(hermesRoot, segments[0], segments[1]),
                Path.Combine(hermesRoot, segments[0], segments[1], segments[2])
            };
            foreach (var directoryPath in admittedDirectories)
            {
                var directory = new DirectoryInfo(directoryPath);
                if (!directory.Exists
                    || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return false;
                }
            }

            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension is not (".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac"))
                return false;

            var file = new FileInfo(fullPath);
            if (!file.Exists
                || file.Length <= 0
                || file.Length > MaximumAudioBytes
                || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            resolvedPath = file.FullName;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or IOException
            or NotSupportedException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

internal static class D052HermesMediaDirectivePolicy
{
    internal static string RemoveExactAudioDirective(
        string? text,
        string filePath)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(filePath))
            return text ?? string.Empty;

        var directive = "MEDIA:" + filePath;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var removedDirective = lines.Any(line => string.Equals(
                line.Trim(),
                directive,
                StringComparison.OrdinalIgnoreCase));
        if (!removedDirective)
            return text;

        var retained = lines.Where(line => !string.Equals(
                    line.Trim(),
                    directive,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    line.Trim(),
                    "[[audio_as_voice]]",
                    StringComparison.Ordinal))
            .ToArray();
        var sanitized = string.Join("\n", retained).Trim('\r', '\n');
        return sanitized.Length == 0 ? "語音已產生。" : sanitized;
    }
}

internal sealed partial class D052HermesTurnPresentationViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, D052HermesAudioAttachmentViewModel>
        _audioAttachmentsByKey = new(StringComparer.Ordinal);
    private bool _isActivityExpanded;

    internal D052HermesTurnPresentationViewModel(
        string key,
        string turnId,
        bool isStandaloneSessionRow)
    {
        Key = key;
        TurnId = turnId;
        IsStandaloneSessionRow = isStandaloneSessionRow;
    }

    internal string Key { get; }
    internal string TurnId { get; }
    internal bool IsStandaloneSessionRow { get; }
    public ObservableCollection<D046HermesTimelineItemViewModel> UserMessages { get; }
        = new();
    public ObservableCollection<D046HermesTimelineItemViewModel> ActivityRows { get; }
        = new();
    public ObservableCollection<D046HermesTimelineItemViewModel> AssistantMessages { get; }
        = new();
    public ObservableCollection<D052HermesAudioAttachmentViewModel> AudioAttachments { get; }
        = new();
    public bool HasUserMessages => UserMessages.Count != 0;
    public bool HasActivity => ActivityRows.Count != 0;
    public bool HasAssistantMessages => AssistantMessages.Count != 0;
    public bool HasAudioAttachments => AudioAttachments.Count != 0;
    public bool IsActivityStreaming => ActivityRows.Any(row => row.IsStreaming);
    public string ActivitySummary => ActivityRows.Count switch
    {
        0 => ControlCenterLocalization.Current.Get("chat.activity"),
        1 => ActivityRows[0].Heading,
        _ => ControlCenterLocalization.Current.Format("chat.activityCount", ActivityRows.Count)
    };
    public string ActivityTimeText => ActivityRows.Count == 0
        ? string.Empty
        : ActivityRows[^1].TimeText;
    public bool IsActivityExpanded
    {
        get => _isActivityExpanded;
        set
        {
            if (_isActivityExpanded == value)
                return;
            _isActivityExpanded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void AddUserMessage(D046HermesTimelineItemViewModel item)
    {
        UserMessages.Add(item);
        OnPropertyChanged(nameof(HasUserMessages));
    }

    internal void AddAssistantMessage(D046HermesTimelineItemViewModel item)
    {
        SanitizeAssistantMessage(item);
        AssistantMessages.Add(item);
        OnPropertyChanged(nameof(HasAssistantMessages));
    }

    internal bool RemoveAssistantMessage(D046HermesTimelineItemViewModel item)
    {
        var removed = AssistantMessages.Remove(item);
        if (removed)
        {
            RemoveGeneratedImageCandidates(item);
            OnPropertyChanged(nameof(HasAssistantMessages));
        }
        return removed;
    }

    internal bool TryUpsertAudioAttachment(string key, string filePath)
    {
        if (_audioAttachmentsByKey.TryGetValue(key, out var existing))
        {
            return string.Equals(
                existing.FilePath,
                filePath,
                StringComparison.OrdinalIgnoreCase);
        }

        var created = new D052HermesAudioAttachmentViewModel(key, filePath);
        _audioAttachmentsByKey.Add(key, created);
        AudioAttachments.Add(created);
        foreach (var message in AssistantMessages)
            SanitizeAssistantMessage(message);
        OnPropertyChanged(nameof(HasAudioAttachments));
        return true;
    }

    internal void SanitizeAssistantMessage(D046HermesTimelineItemViewModel item)
    {
        var sanitized = item.Text;
        foreach (var attachment in AudioAttachments)
        {
            sanitized = D052HermesMediaDirectivePolicy.RemoveExactAudioDirective(
                sanitized,
                attachment.FilePath);
        }
        if (!string.Equals(sanitized, item.Text, StringComparison.Ordinal))
            item.ReplaceDisplayText(sanitized);
        RefreshGeneratedImageCandidates(item);
    }

    internal void AddActivity(D046HermesTimelineItemViewModel item)
    {
        ActivityRows.Add(item);
        if (item.Kind == D046HermesEventKind.Reasoning && item.IsStreaming)
            IsActivityExpanded = true;
        RaiseActivityProperties();
    }

    internal void RefreshActivityState(bool collapseCompletedReasoning = false)
    {
        if (collapseCompletedReasoning
            && !IsActivityStreaming
            && ActivityRows.Any(row => row.Kind == D046HermesEventKind.Reasoning))
        {
            IsActivityExpanded = false;
        }
        RaiseActivityProperties();
    }

    private void RaiseActivityProperties()
    {
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(IsActivityStreaming));
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ActivityTimeText));
    }

    internal void RefreshLanguage()
    {
        foreach (var row in UserMessages.Concat(ActivityRows).Concat(AssistantMessages))
            row.RefreshLanguage();
        foreach (var image in GeneratedImages) image.RefreshLanguage();
        RaiseActivityProperties();
        OnPropertyChanged(nameof(AudioAttachments));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal enum D052HermesPresentationUpsertResult
{
    Applied = 0,
    InvalidIdentity = 1,
    IdentityConflict = 2
}

internal sealed partial class D052HermesTurnPresentationCollection
{
    private readonly Dictionary<string, D052HermesTurnPresentationViewModel>
        _turnsById = new(StringComparer.Ordinal);
    private readonly Dictionary<D052HermesMessageIdentity, D046HermesTimelineItemViewModel>
        _messagesByIdentity = new();
    private readonly Dictionary<string, D046HermesTimelineItemViewModel>
        _activityByIdentity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, D046HermesTimelineItemViewModel>
        _activityByEventId = new(StringComparer.Ordinal);
    private string _runtimeGeneration = string.Empty;
    private string _taskId = string.Empty;
    private string _sessionId = string.Empty;

    internal ObservableCollection<D052HermesTurnPresentationViewModel> Turns { get; }
        = new();

    internal bool ScopeMatches(
        string runtimeGeneration,
        string taskId,
        string sessionId)
        => string.Equals(_runtimeGeneration, runtimeGeneration, StringComparison.Ordinal)
            && string.Equals(_taskId, taskId, StringComparison.Ordinal)
            && string.Equals(_sessionId, sessionId, StringComparison.Ordinal);

    internal void ResetScope(
        string runtimeGeneration,
        string taskId,
        string sessionId)
    {
        _runtimeGeneration = runtimeGeneration ?? string.Empty;
        _taskId = taskId ?? string.Empty;
        _sessionId = sessionId ?? string.Empty;
        foreach (var turn in Turns) turn.DeactivateGeneratedImages();
        Turns.Clear();
        _turnsById.Clear();
        _messagesByIdentity.Clear();
        _activityByIdentity.Clear();
        _activityByEventId.Clear();
    }

    internal D052HermesPresentationUpsertResult UpsertAcceptedUserMessage(
        string turnId,
        string userMessageId,
        string frozenText,
        DateTimeOffset occurredUtc)
        => UpsertMessage(
            turnId,
            userMessageId,
            D046HermesMessageRole.User,
            D046HermesStreamOperation.Completed,
            frozenText,
            occurredUtc);

    internal D052HermesPresentationUpsertResult UpsertMessage(
        string turnId,
        string messageId,
        D046HermesMessageRole role,
        D046HermesStreamOperation operation,
        string text,
        DateTimeOffset occurredUtc,
        bool confirmedCompletion = true)
    {
        if (!IsLowercaseHex32(turnId) || !IsLowercaseHex32(messageId))
            return D052HermesPresentationUpsertResult.InvalidIdentity;

        var identity = new D052HermesMessageIdentity(
            _runtimeGeneration,
            _taskId,
            _sessionId,
            turnId,
            messageId);
        if (_messagesByIdentity.TryGetValue(identity, out var item))
        {
            if (item.MessageRole != role)
                return D052HermesPresentationUpsertResult.IdentityConflict;
            item.ApplyStream(operation, text, confirmedCompletion);
            if (role == D046HermesMessageRole.Assistant
                && _turnsById.TryGetValue(turnId, out var existingTurn))
            {
                existingTurn.SanitizeAssistantMessage(item);
            }
            return D052HermesPresentationUpsertResult.Applied;
        }

        var turn = GetOrCreateTurn(turnId);
        item = new D046HermesTimelineItemViewModel(
            "message:" + turnId + ":" + messageId,
            D046HermesEventKind.Message,
            role,
            role switch
            {
                D046HermesMessageRole.User => "逐夢者",
                D046HermesMessageRole.Assistant => "莉莉絲",
                _ => "烤箱"
            },
            string.Empty,
            "訊息 · " + messageId,
            occurredUtc,
            isStreaming: operation != D046HermesStreamOperation.Completed,
            headingKey: DefaultSpeakerKey(role));
        _messagesByIdentity.Add(identity, item);
        item.ApplyStream(operation, text, confirmedCompletion);
        if (role == D046HermesMessageRole.User)
            turn.AddUserMessage(item);
        else if (role == D046HermesMessageRole.Assistant)
            turn.AddAssistantMessage(item);
        else
            turn.AddActivity(item);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult PromoteStreamingPrimaryToInterim(
        string turnId,
        string primaryMessageId,
        string interimMessageId,
        string authoritativeText,
        bool confirmedCompletion = true)
    {
        if (!TryMessageIdentity(turnId, primaryMessageId, out var primaryIdentity)
            || !TryMessageIdentity(turnId, interimMessageId, out var interimIdentity)
            || primaryIdentity.Equals(interimIdentity))
        {
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        }
        if (!_messagesByIdentity.TryGetValue(primaryIdentity, out var primary)
            || primary.MessageRole != D046HermesMessageRole.Assistant
            || !primary.IsStreaming)
        {
            return D052HermesPresentationUpsertResult.IdentityConflict;
        }
        if (_messagesByIdentity.ContainsKey(interimIdentity))
            return D052HermesPresentationUpsertResult.IdentityConflict;

        _messagesByIdentity.Remove(primaryIdentity);
        primary.RebindMessageIdentity(
            "message:" + turnId + ":" + interimMessageId,
            "訊息 · " + interimMessageId);
        primary.ApplyStream(
            D046HermesStreamOperation.Completed,
            authoritativeText,
            confirmedCompletion);
        if (_turnsById.TryGetValue(turnId, out var turn))
            turn.SanitizeAssistantMessage(primary);
        _messagesByIdentity.Add(interimIdentity, primary);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult SettlePreviewedFinal(
        string turnId,
        string interimMessageId,
        string primaryMessageId,
        string authoritativeText,
        bool confirmedCompletion = true)
    {
        if (!TryMessageIdentity(turnId, interimMessageId, out var interimIdentity)
            || !TryMessageIdentity(turnId, primaryMessageId, out var primaryIdentity)
            || interimIdentity.Equals(primaryIdentity)
            || !_turnsById.TryGetValue(turnId, out var turn))
        {
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        }
        if (!_messagesByIdentity.TryGetValue(interimIdentity, out var interim)
            || interim.MessageRole != D046HermesMessageRole.Assistant
            || interim.IsStreaming)
        {
            return D052HermesPresentationUpsertResult.IdentityConflict;
        }

        _messagesByIdentity.TryGetValue(primaryIdentity, out var laterPrimary);
        if (laterPrimary is not null
            && (laterPrimary.MessageRole != D046HermesMessageRole.Assistant
                || ReferenceEquals(laterPrimary, interim)))
        {
            return D052HermesPresentationUpsertResult.IdentityConflict;
        }

        if (laterPrimary is not null)
        {
            if (!turn.RemoveAssistantMessage(laterPrimary))
                return D052HermesPresentationUpsertResult.IdentityConflict;
            _messagesByIdentity.Remove(primaryIdentity);
        }
        _messagesByIdentity.Remove(interimIdentity);
        interim.RebindMessageIdentity(
            "message:" + turnId + ":" + primaryMessageId,
            "訊息 · " + primaryMessageId);
        interim.ApplyStream(
            D046HermesStreamOperation.Completed,
            authoritativeText,
            confirmedCompletion);
        turn.SanitizeAssistantMessage(interim);
        _messagesByIdentity.Add(primaryIdentity, interim);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult SealExactAssistantMessage(
        string turnId,
        string messageId)
    {
        if (!TryMessageIdentity(turnId, messageId, out var identity))
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        if (!_messagesByIdentity.TryGetValue(identity, out var item))
            return D052HermesPresentationUpsertResult.Applied;
        if (item.MessageRole != D046HermesMessageRole.Assistant)
            return D052HermesPresentationUpsertResult.IdentityConflict;
        item.ApplyStream(D046HermesStreamOperation.Completed, string.Empty, confirmedCompletion: false);
        if (_turnsById.TryGetValue(turnId, out var turn))
            turn.SanitizeAssistantMessage(item);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult AddLegacyUnscopedMessage(
        string eventId,
        string messageId,
        D046HermesMessageRole role,
        D046HermesStreamOperation operation,
        string text,
        DateTimeOffset occurredUtc)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        var key = "legacy-message:" + eventId;
        if (_activityByIdentity.ContainsKey(key))
            return D052HermesPresentationUpsertResult.Applied;

        // Legacy history has no authoritative turn identity. Give every
        // durable history row its own group so later live events can never be
        // attached by adjacency, matching text, or provider-specific labels.
        var turn = GetOrCreateStandaloneSessionRow("legacy-message:" + eventId);
        var item = new D046HermesTimelineItemViewModel(
            key,
            D046HermesEventKind.Message,
            role,
            role switch
            {
                D046HermesMessageRole.User => "逐夢者",
                D046HermesMessageRole.Assistant => "莉莉絲",
                _ => "烤箱"
            },
            string.Empty,
            string.IsNullOrWhiteSpace(messageId)
                ? "舊版工作階段訊息"
                : "舊版工作階段訊息 · " + messageId,
            occurredUtc,
            isStreaming: operation != D046HermesStreamOperation.Completed,
            headingKey: DefaultSpeakerKey(role));
        item.ApplyStream(operation, text);
        _activityByIdentity.Add(key, item);
        if (role == D046HermesMessageRole.User)
            turn.AddUserMessage(item);
        else if (role == D046HermesMessageRole.Assistant)
            turn.AddAssistantMessage(item);
        else
            turn.AddActivity(item);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult UpsertReasoning(
        string turnId,
        string reasoningId,
        D046HermesStreamOperation operation,
        string text,
        DateTimeOffset occurredUtc,
        string eventId)
    {
        if (!IsLowercaseHex32(turnId) || string.IsNullOrWhiteSpace(reasoningId))
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        var key = TurnActivityKey(turnId, "reasoning:" + reasoningId);
        var turn = GetOrCreateTurn(turnId);
        if (!_activityByIdentity.TryGetValue(key, out var item))
        {
            item = new D046HermesTimelineItemViewModel(
                key,
                D046HermesEventKind.Reasoning,
                messageRole: null,
                "鏡中的思緒",
                string.Empty,
                "莉莉絲思考 · " + reasoningId,
                occurredUtc,
                isStreaming: operation != D046HermesStreamOperation.Completed,
                headingKey: "chat.reasoningHeading");
            _activityByIdentity.Add(key, item);
            turn.AddActivity(item);
        }
        item.ApplyStream(operation, text);
        if (!string.IsNullOrWhiteSpace(eventId))
            _activityByEventId[eventId] = item;
        turn.RefreshActivityState(
            collapseCompletedReasoning:
                operation == D046HermesStreamOperation.Completed);
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult UpsertActivity(
        string? turnId,
        string semanticIdentity,
        string eventId,
        D046HermesEventKind kind,
        string heading,
        string text,
        string detail,
        DateTimeOffset occurredUtc,
        UsageBudgetFailure? budgetFailure = null,
        IReadOnlyList<UsageBudgetConstraint>? budgetConstraints = null)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return D052HermesPresentationUpsertResult.InvalidIdentity;

        D052HermesTurnPresentationViewModel turn;
        string key;
        if (string.IsNullOrWhiteSpace(turnId))
        {
            // Session-global or legacy events are deliberately isolated. They
            // never borrow the nearest turn by arrival order, label, or text.
            key = "session-event:" + eventId;
            turn = GetOrCreateStandaloneSessionRow(eventId);
        }
        else
        {
            if (!IsLowercaseHex32(turnId))
                return D052HermesPresentationUpsertResult.InvalidIdentity;
            if (string.IsNullOrWhiteSpace(semanticIdentity))
                return D052HermesPresentationUpsertResult.InvalidIdentity;
            key = TurnActivityKey(turnId, semanticIdentity);
            turn = GetOrCreateTurn(turnId);
        }

        if (_activityByIdentity.TryGetValue(key, out var item))
        {
            if (item.Kind != kind)
                return D052HermesPresentationUpsertResult.IdentityConflict;
            item.ApplyResolution(text, detail);
        }
        else
        {
            item = new D046HermesTimelineItemViewModel(
                key,
                kind,
                messageRole: null,
                heading,
                text,
                detail,
                occurredUtc,
                isStreaming: false,
                headingKey: OwnedActivityHeadingKey(heading));
            _activityByIdentity.Add(key, item);
            turn.AddActivity(item);
        }
        item.SetBudgetMetadata(budgetFailure, budgetConstraints);
        _activityByEventId[eventId] = item;
        turn.RefreshActivityState();
        return D052HermesPresentationUpsertResult.Applied;
    }

    internal D052HermesPresentationUpsertResult UpsertAudioAttachment(
        string turnId,
        string toolCallId,
        string filePath)
    {
        if (!IsLowercaseHex32(turnId)
            || string.IsNullOrWhiteSpace(toolCallId)
            || string.IsNullOrWhiteSpace(filePath))
        {
            return D052HermesPresentationUpsertResult.InvalidIdentity;
        }

        var turn = GetOrCreateTurn(turnId);
        return turn.TryUpsertAudioAttachment(
                "audio:" + toolCallId,
                filePath)
            ? D052HermesPresentationUpsertResult.Applied
            : D052HermesPresentationUpsertResult.IdentityConflict;
    }

    internal void MarkConnectionLost()
    {
        // Stop only presentation animation; do not invent a terminal message,
        // cancel a tool, change stored history, or claim the worker has stopped.
        foreach (var turn in Turns)
        {
            foreach (var row in turn.ActivityRows.Concat(turn.AssistantMessages)
                         .Where(candidate => candidate.IsStreaming))
            {
                row.ApplyResolution(row.Text,
                    row.Detail + " · 連線中斷，尚未確認完成");
                if (row.IsAssistantMessage) turn.RefreshGeneratedImageCandidates(row);
            }
            turn.RefreshActivityState();
        }
    }

    internal void SealStreamingReasoning(string turnId)
    {
        if (!IsLowercaseHex32(turnId)
            || !_turnsById.TryGetValue(turnId, out var turn))
        {
            return;
        }

        foreach (var row in turn.ActivityRows.Where(candidate =>
                     candidate.Kind == D046HermesEventKind.Reasoning
                     && candidate.IsStreaming))
        {
            row.ApplyStream(D046HermesStreamOperation.Completed, string.Empty);
        }
        turn.RefreshActivityState(collapseCompletedReasoning: true);
    }

    internal void SealStreamingAssistantMessages(string turnId)
    {
        if (!IsLowercaseHex32(turnId)
            || !_turnsById.TryGetValue(turnId, out var turn))
        {
            return;
        }

        foreach (var row in turn.AssistantMessages.Where(candidate => candidate.IsStreaming))
        {
            row.ApplyStream(D046HermesStreamOperation.Completed, string.Empty, confirmedCompletion: false);
            turn.RefreshGeneratedImageCandidates(row);
        }
    }

    internal bool TryUpdateEventActivity(
        string eventId,
        string text,
        string detail)
    {
        if (!_activityByEventId.TryGetValue(eventId, out var item))
            return false;
        item.ApplyResolution(text, detail);
        foreach (var turn in Turns.Where(candidate => candidate.ActivityRows.Contains(item)))
            turn.RefreshActivityState();
        return true;
    }

    internal static bool IsConversationTimelineScope(
        D046HermesEventScopeKind scopeKind)
        => scopeKind is D046HermesEventScopeKind.Turn
            or D046HermesEventScopeKind.LegacyUnscoped;

    private static string DefaultSpeakerKey(D046HermesMessageRole role)
        => role switch
        {
            D046HermesMessageRole.User => "chat.speakerUser",
            D046HermesMessageRole.Assistant => "chat.speakerLilith",
            _ => "chat.speakerSystem"
        };

    // This argument is a product-owned activity heading, never message text or
    // a player name. Unknown headings (including tool names) remain verbatim.
    private static string? OwnedActivityHeadingKey(string heading)
        => heading switch
        {
            "黑色筆記本" => "chat.notebookHeading",
            "黑色筆記本 · 警告" => "chat.warningHeading",
            "黑色筆記本 · 提醒" => "chat.noticeHeading",
            "鐘錶報告" => "chat.clockHeading",
            "鏡中的思緒" => "chat.reasoningHeading",
            "莉莉絲的提問" => "chat.clarificationHeading",
            "需要系統管理員密碼" => "chat.adminInputHeading",
            "需要安全輸入" => "chat.secureInputHeading",
            "收音機" => "chat.radioHeading",
            _ when heading.StartsWith("逐夢之典 · ", StringComparison.Ordinal) => "chat.toolHeading",
            _ => null
        };

    internal static bool IsLowercaseHex32(string? value)
        => value is { Length: 32 }
            && value.All(character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private D052HermesTurnPresentationViewModel GetOrCreateTurn(string turnId)
    {
        if (_turnsById.TryGetValue(turnId, out var existing))
            return existing;
        var created = new D052HermesTurnPresentationViewModel(
            "turn:" + turnId,
            turnId,
            isStandaloneSessionRow: false);
        _turnsById.Add(turnId, created);
        Turns.Add(created);
        return created;
    }

    private D052HermesTurnPresentationViewModel GetOrCreateStandaloneSessionRow(
        string eventId)
    {
        var key = "session:" + eventId;
        if (_turnsById.TryGetValue(key, out var existing))
            return existing;
        var created = new D052HermesTurnPresentationViewModel(
            key,
            string.Empty,
            isStandaloneSessionRow: true);
        _turnsById.Add(key, created);
        Turns.Add(created);
        return created;
    }

    private string TurnActivityKey(string turnId, string semanticIdentity)
        => _runtimeGeneration
            + "\u001f" + _taskId
            + "\u001f" + _sessionId
            + "\u001f" + turnId
            + "\u001f" + semanticIdentity;

    private bool TryMessageIdentity(
        string turnId,
        string messageId,
        out D052HermesMessageIdentity identity)
    {
        if (!IsLowercaseHex32(turnId) || !IsLowercaseHex32(messageId))
        {
            identity = default;
            return false;
        }
        identity = new D052HermesMessageIdentity(
            _runtimeGeneration,
            _taskId,
            _sessionId,
            turnId,
            messageId);
        return true;
    }

    private readonly record struct D052HermesMessageIdentity(
        string RuntimeGeneration,
        string TaskId,
        string SessionId,
        string TurnId,
        string MessageId);
}

internal sealed record D052SessionLifecycleMutationOutcome<T>(
    bool Eligible,
    bool OriginalVoiceDesired,
    bool VoiceOffVerified,
    bool MutationStarted,
    T? Value,
    Exception? PreparationError,
    Exception? MutationError,
    Exception? RestoreError)
    where T : class;

internal static class D052SessionLifecycleMutationCoordinator
{
    internal static async Task<D052SessionLifecycleMutationOutcome<T>> ExecuteAsync<T>(
        D046HermesVoiceCaptureState captureState,
        Func<CancellationToken, Task<bool>> readVoiceEnabled,
        Func<CancellationToken, Task<bool>> disableAndVerifyVoice,
        Func<CancellationToken, Task<T>> mutateSession,
        Func<CancellationToken, Task> restoreDesiredVoice,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(readVoiceEnabled);
        ArgumentNullException.ThrowIfNull(disableAndVerifyVoice);
        ArgumentNullException.ThrowIfNull(mutateSession);
        ArgumentNullException.ThrowIfNull(restoreDesiredVoice);

        if (captureState is not (D046HermesVoiceCaptureState.Off
            or D046HermesVoiceCaptureState.Idle))
        {
            return new(
                Eligible: false,
                OriginalVoiceDesired: false,
                VoiceOffVerified: false,
                MutationStarted: false,
                Value: null,
                PreparationError: null,
                MutationError: null,
                RestoreError: null);
        }

        bool originalVoiceDesired;
        try
        {
            originalVoiceDesired = await readVoiceEnabled(cancellationToken);
        }
        catch (Exception exception)
        {
            return new(
                Eligible: true,
                OriginalVoiceDesired: false,
                VoiceOffVerified: false,
                MutationStarted: false,
                Value: null,
                PreparationError: exception,
                MutationError: null,
                RestoreError: null);
        }

        var voiceOffVerified = !originalVoiceDesired;
        if (originalVoiceDesired)
        {
            try
            {
                voiceOffVerified = await disableAndVerifyVoice(cancellationToken);
            }
            catch (Exception exception)
            {
                return new(
                    Eligible: true,
                    OriginalVoiceDesired: true,
                    VoiceOffVerified: false,
                    MutationStarted: false,
                    Value: null,
                    PreparationError: exception,
                    MutationError: null,
                    RestoreError: null);
            }
        }

        if (!voiceOffVerified)
        {
            return new(
                Eligible: true,
                OriginalVoiceDesired: originalVoiceDesired,
                VoiceOffVerified: false,
                MutationStarted: false,
                Value: null,
                PreparationError: null,
                MutationError: null,
                RestoreError: null);
        }

        T? value = null;
        Exception? mutationError = null;
        try
        {
            value = await mutateSession(cancellationToken);
        }
        catch (Exception exception)
        {
            mutationError = exception;
        }

        Exception? restoreError = null;
        if (originalVoiceDesired)
        {
            try
            {
                await restoreDesiredVoice(cancellationToken);
            }
            catch (Exception exception)
            {
                restoreError = exception;
            }
        }

        return new(
            Eligible: true,
            OriginalVoiceDesired: originalVoiceDesired,
            VoiceOffVerified: true,
            MutationStarted: true,
            Value: value,
            PreparationError: null,
            MutationError: mutationError,
            RestoreError: restoreError);
    }
}
