using System;
using System.Collections.Generic;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal enum D046GameControlProjectionOffer
{
    Unchanged = 1,
    PendingUpdated = 2,
    RejectedPortableDowngrade = 3,
    RejectedRevisionRegression = 4,
    RejectedRevisionConflict = 5
}

internal enum D046GameControlProjectionCommit
{
    NoPending = 1,
    Applied = 2,
    DeferredRecording = 3,
    DeferredHeldKey = 4
}

internal sealed class D046GameControlProjectionSnapshot :
    IEquatable<D046GameControlProjectionSnapshot>
{
    internal bool FromPortableStore { get; }
    internal long Revision { get; }
    internal int PushToTalkKeyId { get; }
    internal int TextChatKeyId { get; }
    internal int ConversationHistoryKeyId { get; }
    internal int StopKeyId { get; }
    internal D046FunctionKey? PushToTalkPortableKey { get; }
    internal D046FunctionKey? TextChatPortableKey { get; }
    internal D046FunctionKey? ConversationHistoryPortableKey { get; }
    internal D046FunctionKey? StopPortableKey { get; }
    internal D046GameVoiceLanguage VoiceLanguage { get; }
    internal D046SpeechInputLanguage SpeechInputLanguage { get; }
    internal bool ProactiveDialogueEnabled { get; }

    internal D046GameControlProjectionSnapshot(
        bool fromPortableStore,
        long revision,
        int pushToTalkKeyId,
        int textChatKeyId,
        int conversationHistoryKeyId,
        int stopKeyId,
        D046GameVoiceLanguage voiceLanguage,
        D046SpeechInputLanguage speechInputLanguage,
        D046FunctionKey? pushToTalkPortableKey = null,
        D046FunctionKey? textChatPortableKey = null,
        D046FunctionKey? conversationHistoryPortableKey = null,
        D046FunctionKey? stopPortableKey = null,
        bool proactiveDialogueEnabled = false)
    {
        FromPortableStore = fromPortableStore;
        Revision = revision;
        PushToTalkKeyId = pushToTalkKeyId;
        TextChatKeyId = textChatKeyId;
        ConversationHistoryKeyId = conversationHistoryKeyId;
        StopKeyId = stopKeyId;
        VoiceLanguage = voiceLanguage;
        SpeechInputLanguage = speechInputLanguage;
        PushToTalkPortableKey = pushToTalkPortableKey;
        TextChatPortableKey = textChatPortableKey;
        ConversationHistoryPortableKey = conversationHistoryPortableKey;
        StopPortableKey = stopPortableKey;
        ProactiveDialogueEnabled = proactiveDialogueEnabled;
    }

    internal bool UsesJapaneseVoice =>
        VoiceLanguage == D046GameVoiceLanguage.Japanese;

    internal D046GameControlProjectionSnapshot WithSpeechInputLanguage(
        D046SpeechInputLanguage speechInputLanguage)
        => new(
            FromPortableStore,
            Revision,
            PushToTalkKeyId,
            TextChatKeyId,
            ConversationHistoryKeyId,
            StopKeyId,
            VoiceLanguage,
            speechInputLanguage,
            PushToTalkPortableKey,
            TextChatPortableKey,
            ConversationHistoryPortableKey,
            StopPortableKey,
            ProactiveDialogueEnabled);

    internal bool ContainsKey(int keyId)
        => PushToTalkKeyId == keyId
            || TextChatKeyId == keyId
            || ConversationHistoryKeyId == keyId
            || StopKeyId == keyId;

    public bool Equals(D046GameControlProjectionSnapshot? other)
        => other != null
            && FromPortableStore == other.FromPortableStore
            && Revision == other.Revision
            && PushToTalkKeyId == other.PushToTalkKeyId
            && TextChatKeyId == other.TextChatKeyId
            && ConversationHistoryKeyId == other.ConversationHistoryKeyId
            && StopKeyId == other.StopKeyId
            && VoiceLanguage == other.VoiceLanguage
            && SpeechInputLanguage == other.SpeechInputLanguage
            && ProactiveDialogueEnabled == other.ProactiveDialogueEnabled;

    public override bool Equals(object? obj)
        => Equals(obj as D046GameControlProjectionSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FromPortableStore);
        hash.Add(Revision);
        hash.Add(PushToTalkKeyId);
        hash.Add(TextChatKeyId);
        hash.Add(ConversationHistoryKeyId);
        hash.Add(StopKeyId);
        hash.Add(VoiceLanguage);
        hash.Add(SpeechInputLanguage);
        hash.Add(ProactiveDialogueEnabled);
        return hash.ToHashCode();
    }
}

internal sealed class D046GameControlPhysicalSample
{
    private readonly HashSet<int> _heldKeyIds;

    internal bool MicrophoneRecording { get; }
    internal bool ControlDown { get; }
    internal bool ShiftDown { get; }
    internal bool AltDown { get; }
    internal bool HasAnyModifier => ControlDown || ShiftDown || AltDown;

    internal D046GameControlPhysicalSample(
        bool microphoneRecording,
        IEnumerable<int>? heldKeyIds = null,
        bool controlDown = false,
        bool shiftDown = false,
        bool altDown = false)
    {
        MicrophoneRecording = microphoneRecording;
        _heldKeyIds = heldKeyIds == null
            ? new HashSet<int>()
            : new HashSet<int>(heldKeyIds);
        ControlDown = controlDown;
        ShiftDown = shiftDown;
        AltDown = altDown;
    }

    internal bool IsHeld(int keyId) => _heldKeyIds.Contains(keyId);
}

internal sealed class D046GameControlProjectionState
{
    internal D046GameControlProjectionSnapshot? Effective { get; private set; }
    internal D046GameControlProjectionSnapshot? Pending { get; private set; }

    internal static bool IsAuthoritativeLoad(
        D046GameControlLoadDisposition disposition)
        => disposition == D046GameControlLoadDisposition.Ready
            || disposition == D046GameControlLoadDisposition.PreviousSchemaMigrated;

    internal D046GameControlProjectionOffer Offer(
        D046GameControlProjectionSnapshot desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (Effective?.Equals(desired) == true)
        {
            Pending = null;
            return D046GameControlProjectionOffer.Unchanged;
        }
        if (Effective is { FromPortableStore: true } effectivePortable)
        {
            if (!desired.FromPortableStore)
            {
                Pending = null;
                return D046GameControlProjectionOffer.RejectedPortableDowngrade;
            }
            if (desired.Revision < effectivePortable.Revision)
            {
                Pending = null;
                return D046GameControlProjectionOffer.RejectedRevisionRegression;
            }
            if (desired.Revision == effectivePortable.Revision)
            {
                Pending = null;
                return D046GameControlProjectionOffer.RejectedRevisionConflict;
            }
        }
        if (Pending?.Equals(desired) == true)
            return D046GameControlProjectionOffer.Unchanged;

        Pending = desired;
        return D046GameControlProjectionOffer.PendingUpdated;
    }

    internal D046GameControlProjectionCommit TryCommit(
        D046GameControlPhysicalSample physical)
    {
        ArgumentNullException.ThrowIfNull(physical);
        if (Pending == null)
            return D046GameControlProjectionCommit.NoPending;
        if (physical.MicrophoneRecording)
            return D046GameControlProjectionCommit.DeferredRecording;
        if (ContainsHeldKey(Pending, physical)
            || (Effective != null && ContainsHeldKey(Effective, physical)))
        {
            return D046GameControlProjectionCommit.DeferredHeldKey;
        }

        Effective = Pending;
        Pending = null;
        return D046GameControlProjectionCommit.Applied;
    }

    private static bool ContainsHeldKey(
        D046GameControlProjectionSnapshot snapshot,
        D046GameControlPhysicalSample physical)
        => physical.IsHeld(snapshot.PushToTalkKeyId)
            || physical.IsHeld(snapshot.TextChatKeyId)
            || physical.IsHeld(snapshot.ConversationHistoryKeyId)
            || physical.IsHeld(snapshot.StopKeyId);
}

internal readonly struct D046HotkeyTransition
{
    internal bool Pressed { get; }
    internal bool Released { get; }

    internal D046HotkeyTransition(bool pressed, bool released)
    {
        Pressed = pressed;
        Released = released;
    }
}

internal static class D046GameControlHotkeyAdmission
{
    // F6/F7/F10 are Windows-level shortcuts for the transparent desktop-pet
    // surface. The game window normally is not the foreground window, so
    // foreground ownership cannot be an admission condition. Only the
    // explicit key-capture lease and the active text-entry modal suppress a
    // new press; an already-started push-to-talk release remains observable
    // through D046ExactHotkeyLatch.
    internal static bool ShouldSuppressNewPresses(
        bool keyCaptureLeaseActive,
        bool textInputModalOpen)
        => keyCaptureLeaseActive || textInputModalOpen;
}

internal sealed class D046ExactHotkeyLatch
{
    private bool _wasDown;

    internal D046HotkeyTransition Observe(
        bool keyDown,
        bool controlDown,
        bool shiftDown,
        bool altDown,
        bool systemModifierDown = false,
        bool suppressNewPresses = false)
    {
        var pressed = keyDown
            && !_wasDown
            && !controlDown
            && !shiftDown
            && !altDown
            && !systemModifierDown
            && !suppressNewPresses;
        var released = !keyDown && _wasDown;
        _wasDown = keyDown;
        return new D046HotkeyTransition(pressed, released);
    }

    internal void Reset() => _wasDown = false;
}
