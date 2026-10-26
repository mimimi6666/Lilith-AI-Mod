using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LilithRuntime.Configuration;

namespace LilithControlCenter;

internal sealed class D046Choice<T> : INotifyPropertyChanged
    where T : struct, Enum
{
    public T Value { get; }
    private readonly string _label;
    public string Label => ControlCenterLocalization.Current.RefreshOwnedUiLabel(_label);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new(nameof(Label)));

    internal D046Choice(T value, string label)
    {
        Value = value;
        _label = label;
    }
}

internal enum D046GameControlKeyTarget
{
    None = 0,
    PushToTalk = 1,
    TextChat = 2,
    ConversationHistory = 3,
    Stop = 4
}

internal sealed class D046GameControlKeyCaptureState
{
    internal D046GameControlKeyTarget Target { get; private set; }
    internal bool IsActive => Target != D046GameControlKeyTarget.None;
    internal int? CandidateVirtualKey { get; private set; }
    internal D046FunctionKey? CandidateKey { get; private set; }

    internal void Begin(D046GameControlKeyTarget target)
    {
        if (target == D046GameControlKeyTarget.None)
            throw new ArgumentOutOfRangeException(nameof(target));
        Target = target;
        CandidateVirtualKey = null;
        CandidateKey = null;
    }

    internal bool TryStage(int virtualKey, D046FunctionKey key)
    {
        if (!IsActive)
            throw new InvalidOperationException("No key capture is active.");
        if (CandidateVirtualKey.HasValue)
            return CandidateVirtualKey.Value == virtualKey && CandidateKey == key;
        CandidateVirtualKey = virtualKey;
        CandidateKey = key;
        return true;
    }

    internal bool TryComplete(
        int virtualKey,
        out D046GameControlKeyTarget target,
        out D046FunctionKey key)
    {
        if (!IsActive
            || CandidateVirtualKey != virtualKey
            || !CandidateKey.HasValue)
        {
            target = D046GameControlKeyTarget.None;
            key = default;
            return false;
        }

        target = Target;
        key = CandidateKey.Value;
        Clear();
        return true;
    }

    internal bool Cancel()
    {
        if (!IsActive)
            return false;
        Clear();
        return true;
    }

    private void Clear()
    {
        Target = D046GameControlKeyTarget.None;
        CandidateVirtualKey = null;
        CandidateKey = null;
    }
}

internal static class D046WindowsKeyCapturePolicy
{
    private const int EscapeVirtualKey = 0x1B;

    internal static bool IsEscape(int virtualKey) => virtualKey == EscapeVirtualKey;

    internal static bool IsModifierVirtualKey(int virtualKey)
        => virtualKey is 0x10 or 0x11 or 0x12
            or 0x5B or 0x5C
            or >= 0xA0 and <= 0xA5;

    internal static string DescribeRejection(int virtualKey, bool hasModifierChord)
    {
        if (IsModifierVirtualKey(virtualKey))
            return "Ctrl、Alt、Shift 與 Windows 鍵不能單獨設為快捷鍵。";
        if (hasModifierChord)
            return "目前只接受單一按鍵，不能搭配 Ctrl、Alt、Shift 或 Windows 鍵。";
        return ControlCenterLocalization.Current.Get("state.this.key.is.not.supported.settings.were.not.changed.371");
    }
}

internal sealed class D046GameControlsPanelViewModel : INotifyPropertyChanged
{
    private readonly D046GameControlPreferencesStore _store;
    private readonly Func<IDisposable?> _captureLeaseFactory;
    private readonly D046GameControlKeyCaptureState _capture = new();
    private IDisposable? _captureLease;
    private D046GameControlPreferencesDocument _baseline;
    private D046FunctionKey _pushToTalkKey;
    private D046FunctionKey _textChatKey;
    private D046FunctionKey _conversationHistoryKey;
    private D046FunctionKey _stopKey;
    private D046GameVoiceLanguage _voiceLanguage;
    private D046SpeechInputLanguage _speechInputLanguage;
    private bool _proactiveDialogueEnabled;
    private string _statusText = string.Empty;
    private string _captureFeedbackText = ControlCenterLocalization.Current.Get("state.click.a.shortcut.then.press.one.key.press.esc.to.cancel.372");
    private string _captureFeedbackColor = "#52627A";
    private string? _deferredCaptureRejection;
    private bool _requiresCommit;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RefreshLanguage()
    {
        _statusText = ControlCenterLocalization.Current.RefreshOwnedUiLabel(_statusText);
        _captureFeedbackText = ControlCenterLocalization.Current.RefreshOwnedUiLabel(_captureFeedbackText);
        foreach (var choice in VoiceChoices) choice.RefreshLanguage();
        foreach (var choice in SpeechInputChoices) choice.RefreshLanguage();
        PropertyChanged?.Invoke(this, new(null));
    }

    public IReadOnlyList<D046Choice<D046GameVoiceLanguage>> VoiceChoices { get; } =
        new[]
        {
            new D046Choice<D046GameVoiceLanguage>(
                D046GameVoiceLanguage.TraditionalChinese,
                ControlCenterLocalization.Current.Get("state.chinese.373")),
            new D046Choice<D046GameVoiceLanguage>(
                D046GameVoiceLanguage.Japanese,
                ControlCenterLocalization.Current.Get("state.japanese.374"))
        };

    public IReadOnlyList<D046Choice<D046SpeechInputLanguage>>
        SpeechInputChoices { get; } =
        new[]
        {
            new D046Choice<D046SpeechInputLanguage>(
                D046SpeechInputLanguage.Automatic,
                ControlCenterLocalization.Current.Get("state.detect.automatically.recommended.375")),
            new D046Choice<D046SpeechInputLanguage>(
                D046SpeechInputLanguage.TraditionalChinese,
                ControlCenterLocalization.Current.Get("state.traditional.chinese.376")),
            new D046Choice<D046SpeechInputLanguage>(
                D046SpeechInputLanguage.Japanese,
                ControlCenterLocalization.Current.Get("state.japanese.374")),
            new D046Choice<D046SpeechInputLanguage>(
                D046SpeechInputLanguage.English,
                ControlCenterLocalization.Current.Get("state.english.377"))
        };

    public D046FunctionKey PushToTalkKey
    {
        get => _pushToTalkKey;
        set => SetField(ref _pushToTalkKey, value);
    }

    public D046FunctionKey TextChatKey
    {
        get => _textChatKey;
        set => SetField(ref _textChatKey, value);
    }

    public D046FunctionKey ConversationHistoryKey
    {
        get => _conversationHistoryKey;
        set => SetField(ref _conversationHistoryKey, value);
    }

    public D046FunctionKey StopKey
    {
        get => _stopKey;
        set => SetField(ref _stopKey, value);
    }

    public D046GameVoiceLanguage VoiceLanguage
    {
        get => _voiceLanguage;
        set => SetField(ref _voiceLanguage, value);
    }

    public D046SpeechInputLanguage SpeechInputLanguage
    {
        get => _speechInputLanguage;
        set => SetField(ref _speechInputLanguage, value);
    }

    public bool ProactiveDialogueEnabled
    {
        get => _proactiveDialogueEnabled;
        set => SetField(ref _proactiveDialogueEnabled, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value, refreshState: false);
    }

    public bool IsCapturingKey => _capture.IsActive;
    public D046GameControlKeyTarget CapturingKeyTarget => _capture.Target;
    public string PushToTalkKeyText => GetKeyButtonText(
        D046GameControlKeyTarget.PushToTalk,
        PushToTalkKey);
    public string TextChatKeyText => GetKeyButtonText(
        D046GameControlKeyTarget.TextChat,
        TextChatKey);
    public string ConversationHistoryKeyText => GetKeyButtonText(
        D046GameControlKeyTarget.ConversationHistory,
        ConversationHistoryKey);
    public string StopKeyText => GetKeyButtonText(
        D046GameControlKeyTarget.Stop,
        StopKey);

    public string CaptureFeedbackText
    {
        get => _captureFeedbackText;
        private set => SetField(ref _captureFeedbackText, value, refreshState: false);
    }

    public string CaptureFeedbackColor
    {
        get => _captureFeedbackColor;
        private set => SetField(ref _captureFeedbackColor, value, refreshState: false);
    }

    public bool HasPendingChanges =>
        PushToTalkKey != _baseline.PushToTalkKey
        || TextChatKey != _baseline.TextChatKey
        || ConversationHistoryKey != _baseline.ConversationHistoryKey
        || StopKey != _baseline.StopKey
        || VoiceLanguage != _baseline.VoiceLanguage
        || SpeechInputLanguage != _baseline.SpeechInputLanguage
        || ProactiveDialogueEnabled != _baseline.ProactiveDialogueEnabled;

    public bool HasConflict => !Validation.Accepted;
    public bool CanApply =>
        (HasPendingChanges || _requiresCommit) && !HasConflict && !IsCapturingKey;
    public bool CanCancel => HasPendingChanges || IsCapturingKey;
    public string ValidationText =>
        Validation.Accepted
            ? ControlCenterLocalization.Current.Get("state.all.four.shortcuts.are.distinct.click.one.to.change.it.378")
            : ControlCenterLocalization.Current.Get("state.shortcut.conflict.voice.text.history.and.stop.must.use.different.379");
    public string ValidationColor => Validation.Accepted ? "#1D795A" : "#BD354B";
    public string RevisionText => ControlCenterLocalization.Current.Get("state.desired.saved.revision.380") + _baseline.Revision;

    private D046GameControlValidationResult Validation =>
        D046GameControlPreferencesPolicy.Validate(CreateDraft());

    internal D046GameControlsPanelViewModel(D046GameControlPreferencesStore store)
        : this(store, AcquirePortableCaptureLease)
    {
    }

    internal D046GameControlsPanelViewModel(
        D046GameControlPreferencesStore store,
        Func<IDisposable?> captureLeaseFactory)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _captureLeaseFactory = captureLeaseFactory
            ?? throw new ArgumentNullException(nameof(captureLeaseFactory));
        var loaded = _store.Load();
        _baseline = loaded.Snapshot;
        _requiresCommit = loaded.Disposition != D046GameControlLoadDisposition.Ready;
        LoadDraft(_baseline);
        StatusText = loaded.Disposition switch
        {
            D046GameControlLoadDisposition.Ready =>
                ControlCenterLocalization.Current.Get("ui.repair.12"),
            D046GameControlLoadDisposition.MissingDefaults =>
                ControlCenterLocalization.Current.Get("ui.repair.13"),
            D046GameControlLoadDisposition.PreviousSchemaMigrated =>
                ControlCenterLocalization.Current.Get("ui.repair.14"),
            _ =>
                ControlCenterLocalization.Current.Get("ui.repair.15")
        };
    }

    internal void Apply()
    {
        ReleaseKeyCapture();
        var result = _store.TryApply(
            _baseline.Revision,
            CreateDraft(),
            DateTimeOffset.UtcNow);
        switch (result.Admission)
        {
            case D046GameControlApplyAdmission.Applied:
                _baseline = result.Snapshot;
                _requiresCommit = false;
                LoadDraft(_baseline);
                CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.shortcut.settings.saved.381");
                CaptureFeedbackColor = "#1D795A";
                StatusText =
                    ControlCenterLocalization.Current.Get("state.saved.settings.revision.382")
                    + _baseline.Revision
                    + ControlCenterLocalization.Current.Get("ui.repair.16");
                RaiseState();
                break;
            case D046GameControlApplyAdmission.AppliedRequiresGameRestart:
                _baseline = result.Snapshot;
                _requiresCommit = false;
                LoadDraft(_baseline);
                CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.shortcut.settings.saved.restart.the.game.to.apply.383");
                CaptureFeedbackColor = "#1D795A";
                StatusText =
                    ControlCenterLocalization.Current.Get("state.invalid.settings.repaired.and.saved.as.revision.384")
                    + _baseline.Revision
                    + ControlCenterLocalization.Current.Get("state.restart.the.game.effective.state.is.unknown.until.then.385");
                RaiseState();
                break;
            case D046GameControlApplyAdmission.RevisionConflict:
                _baseline = result.Snapshot;
                _requiresCommit = true;
                StatusText =
                    ControlCenterLocalization.Current.Get("state.another.window.updated.the.saved.revision.to.386")
                    + _baseline.Revision
                    + ControlCenterLocalization.Current.Get("ui.repair.17");
                RaiseState();
                break;
            case D046GameControlApplyAdmission.ValidationRejected:
                StatusText = ControlCenterLocalization.Current.Get("ui.repair.18");
                RaiseState();
                break;
            default:
                StatusText = ControlCenterLocalization.Current.Get("ui.repair.19");
                RaiseState();
                break;
        }
    }

    internal bool RefreshSavedPreferences()
    {
        if (IsCapturingKey)
            return false;
        var loaded = _store.Load();
        if (loaded.Disposition != D046GameControlLoadDisposition.Ready
            || loaded.Snapshot.Revision <= _baseline.Revision)
            return false;

        var previous = _baseline;
        var draft = CreateDraft();
        var latest = loaded.Snapshot;
        // Refresh only untouched fields. A game-language observation must not
        // discard an unfinished key/voice edit or silently submit that draft.
        if (draft.PushToTalkKey == previous.PushToTalkKey)
            draft.PushToTalkKey = latest.PushToTalkKey;
        if (draft.TextChatKey == previous.TextChatKey)
            draft.TextChatKey = latest.TextChatKey;
        if (draft.ConversationHistoryKey == previous.ConversationHistoryKey)
            draft.ConversationHistoryKey = latest.ConversationHistoryKey;
        if (draft.StopKey == previous.StopKey)
            draft.StopKey = latest.StopKey;
        if (draft.VoiceLanguage == previous.VoiceLanguage)
            draft.VoiceLanguage = latest.VoiceLanguage;
        if (draft.SpeechInputLanguage == previous.SpeechInputLanguage)
            draft.SpeechInputLanguage = latest.SpeechInputLanguage;
        if (draft.ProactiveDialogueEnabled == previous.ProactiveDialogueEnabled)
            draft.ProactiveDialogueEnabled = latest.ProactiveDialogueEnabled;
        _baseline = latest;
        _requiresCommit = false;
        LoadDraft(new D046GameControlPreferencesDocument
        {
            PushToTalkKey = draft.PushToTalkKey,
            TextChatKey = draft.TextChatKey,
            ConversationHistoryKey = draft.ConversationHistoryKey,
            StopKey = draft.StopKey,
            VoiceLanguage = draft.VoiceLanguage,
            SpeechInputLanguage = draft.SpeechInputLanguage,
            ProactiveDialogueEnabled = draft.ProactiveDialogueEnabled
        });
        StatusText = ControlCenterLocalization.Current.Get("ui.game.voice.refreshed");
        RaiseState();
        return true;
    }

    internal void Cancel()
    {
        ReleaseKeyCapture();
        LoadDraft(_baseline);
        CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.unsaved.shortcut.changes.cancelled.387");
        CaptureFeedbackColor = "#52627A";
        StatusText = ControlCenterLocalization.Current.Get("ui.repair.20");
        RaiseState();
    }

    internal bool BeginKeyCapture(D046GameControlKeyTarget target)
    {
        if (target is not D046GameControlKeyTarget.PushToTalk
            and not D046GameControlKeyTarget.TextChat
            and not D046GameControlKeyTarget.ConversationHistory
            and not D046GameControlKeyTarget.Stop)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        ReleaseKeyCapture();
        var lease = _captureLeaseFactory();
        if (lease is null)
        {
            CaptureFeedbackText =
                ControlCenterLocalization.Current.Get("state.another.lilith.window.is.waiting.for.a.key.finish.or.cancel.there.388");
            CaptureFeedbackColor = "#BD354B";
            RaiseState();
            return false;
        }

        _captureLease = lease;
        _capture.Begin(target);
        CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.editing.389") + GetTargetLabel(target) + ControlCenterLocalization.Current.Get("state.press.one.key.esc.cancels.390");
        CaptureFeedbackColor = "#B93343";
        RaiseState();
        return true;
    }

    internal void CaptureWindowsVirtualKey(int virtualKey, bool hasModifierChord)
    {
        if (!IsCapturingKey)
            return;
        if (_deferredCaptureRejection != null)
            return;
        if (D046WindowsKeyCapturePolicy.IsEscape(virtualKey))
        {
            CancelKeyCapture(ControlCenterLocalization.Current.Get("state.shortcut.change.cancelled.the.original.key.was.kept.391"));
            return;
        }
        if (hasModifierChord
            && !D046WindowsKeyCapturePolicy.IsModifierVirtualKey(virtualKey))
        {
            DeferCapturedKeyRejection(
                D046WindowsKeyCapturePolicy.DescribeRejection(
                    virtualKey,
                    hasModifierChord));
            return;
        }
        if (!D046FunctionKeyCatalog.TryFromWindowsVirtualKey(virtualKey, out var key))
        {
            DeferCapturedKeyRejection(
                D046WindowsKeyCapturePolicy.DescribeRejection(
                    virtualKey,
                    hasModifierChord));
            return;
        }

        if (!_capture.TryStage(virtualKey, key))
        {
            DeferCapturedKeyRejection(
                ControlCenterLocalization.Current.Get("state.assign.one.key.at.a.time.settings.were.not.changed.392"));
            return;
        }

        var label = D046FunctionKeyCatalog.GetDisplayLabel(key);
        CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.captured.393") + label + ControlCenterLocalization.Current.Get("state.release.the.key.to.finish.394");
        CaptureFeedbackColor = "#B93343";
        RaiseState();
    }

    internal void CommitCapturedVirtualKey(
        int virtualKey,
        bool allCapturedKeysReleased = true)
    {
        if (_deferredCaptureRejection != null)
        {
            if (!allCapturedKeysReleased)
                return;
            var rejection = _deferredCaptureRejection;
            RejectCapturedKeyAndRelease(rejection);
            return;
        }
        if (!allCapturedKeysReleased)
            return;
        if (!_capture.TryComplete(virtualKey, out var target, out var key))
            return;

        if (WouldConflict(target, key))
        {
            RejectCapturedKeyAndRelease(
                ControlCenterLocalization.Current.Get("state.another.game.action.already.uses.this.key.shortcuts.must.be.uniqu.395"));
            return;
        }

        switch (target)
        {
            case D046GameControlKeyTarget.PushToTalk:
                PushToTalkKey = key;
                break;
            case D046GameControlKeyTarget.TextChat:
                TextChatKey = key;
                break;
            case D046GameControlKeyTarget.ConversationHistory:
                ConversationHistoryKey = key;
                break;
            case D046GameControlKeyTarget.Stop:
                StopKey = key;
                break;
            default:
                throw new InvalidOperationException("The key capture target is invalid.");
        }

        ReleaseCaptureLease();
        var label = D046FunctionKeyCatalog.GetDisplayLabel(key);
        CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.selected.396") + label + ControlCenterLocalization.Current.Get("state.click.apply.to.save.397");
        CaptureFeedbackColor = "#1D795A";
        RaiseState();
    }

    internal void CancelKeyCapture(string message)
    {
        if (!ReleaseKeyCapture())
            return;
        CaptureFeedbackText = message;
        CaptureFeedbackColor = "#52627A";
        RaiseState();
    }

    private void RejectCapturedKeyAndRelease(string reason)
    {
        ReleaseKeyCapture();
        CaptureFeedbackText = ControlCenterLocalization.Current.Get("state.not.changed.398") + reason + ControlCenterLocalization.Current.Get("state.click.the.shortcut.again.to.retry.399");
        CaptureFeedbackColor = "#BD354B";
        RaiseState();
    }

    private void DeferCapturedKeyRejection(string reason)
    {
        _deferredCaptureRejection = reason;
        CaptureFeedbackText =
            ControlCenterLocalization.Current.Get("state.not.changed.398") + reason + ControlCenterLocalization.Current.Get("state.release.all.held.keys.first.400");
        CaptureFeedbackColor = "#BD354B";
        RaiseState();
    }

    private bool WouldConflict(
        D046GameControlKeyTarget target,
        D046FunctionKey candidate)
    {
        var draft = CreateDraft();
        switch (target)
        {
            case D046GameControlKeyTarget.PushToTalk:
                draft.PushToTalkKey = candidate;
                break;
            case D046GameControlKeyTarget.TextChat:
                draft.TextChatKey = candidate;
                break;
            case D046GameControlKeyTarget.ConversationHistory:
                draft.ConversationHistoryKey = candidate;
                break;
            case D046GameControlKeyTarget.Stop:
                draft.StopKey = candidate;
                break;
            default:
                return true;
        }

        return !D046GameControlPreferencesPolicy.Validate(draft).Accepted;
    }

    private bool ReleaseKeyCapture()
    {
        var released = _capture.Cancel();
        _deferredCaptureRejection = null;
        ReleaseCaptureLease();
        if (released)
            RaiseState();
        return released;
    }

    private void ReleaseCaptureLease()
    {
        var lease = _captureLease;
        _captureLease = null;
        lease?.Dispose();
    }

    private D046GameControlPreferencesDraft CreateDraft()
        => new()
        {
            PushToTalkKey = PushToTalkKey,
            TextChatKey = TextChatKey,
            ConversationHistoryKey = ConversationHistoryKey,
            StopKey = StopKey,
            VoiceLanguage = VoiceLanguage,
            SpeechInputLanguage = SpeechInputLanguage,
            ProactiveDialogueEnabled = ProactiveDialogueEnabled
        };

    private void LoadDraft(D046GameControlPreferencesDocument snapshot)
    {
        _pushToTalkKey = snapshot.PushToTalkKey;
        _textChatKey = snapshot.TextChatKey;
        _conversationHistoryKey = snapshot.ConversationHistoryKey;
        _stopKey = snapshot.StopKey;
        _voiceLanguage = snapshot.VoiceLanguage;
        _speechInputLanguage = snapshot.SpeechInputLanguage;
        _proactiveDialogueEnabled = snapshot.ProactiveDialogueEnabled;
        OnPropertyChanged(nameof(PushToTalkKey));
        OnPropertyChanged(nameof(TextChatKey));
        OnPropertyChanged(nameof(ConversationHistoryKey));
        OnPropertyChanged(nameof(StopKey));
        OnPropertyChanged(nameof(VoiceLanguage));
        OnPropertyChanged(nameof(SpeechInputLanguage));
        OnPropertyChanged(nameof(ProactiveDialogueEnabled));
    }

    private string GetKeyButtonText(
        D046GameControlKeyTarget target,
        D046FunctionKey key)
        => _capture.Target == target
            ? _capture.CandidateKey.HasValue
                ? ControlCenterLocalization.Current.Get("state.release.401") + D046FunctionKeyCatalog.GetDisplayLabel(
                    _capture.CandidateKey.Value)
                : ControlCenterLocalization.Current.Get("state.press.one.key.402")
            : D046FunctionKeyCatalog.GetDisplayLabel(key);

    private static IDisposable? AcquirePortableCaptureLease()
        => D046GameControlCaptureLease.TryAcquire(out var lease)
            ? lease
            : null;

    private static string GetTargetLabel(D046GameControlKeyTarget target)
        => target switch
        {
            D046GameControlKeyTarget.PushToTalk => ControlCenterLocalization.Current.Get("ui.push.to.talk"),
            D046GameControlKeyTarget.TextChat => ControlCenterLocalization.Current.Get("ui.text.input"),
            D046GameControlKeyTarget.ConversationHistory => ControlCenterLocalization.Current.Get("ui.conversation.history"),
            D046GameControlKeyTarget.Stop => ControlCenterLocalization.Current.Get("ui.stop"),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null,
        bool refreshState = true)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
        if (refreshState)
            RaiseState();
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(HasPendingChanges));
        OnPropertyChanged(nameof(HasConflict));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(IsCapturingKey));
        OnPropertyChanged(nameof(CapturingKeyTarget));
        OnPropertyChanged(nameof(PushToTalkKeyText));
        OnPropertyChanged(nameof(TextChatKeyText));
        OnPropertyChanged(nameof(ConversationHistoryKeyText));
        OnPropertyChanged(nameof(StopKeyText));
        OnPropertyChanged(nameof(ValidationText));
        OnPropertyChanged(nameof(ValidationColor));
        OnPropertyChanged(nameof(RevisionText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
