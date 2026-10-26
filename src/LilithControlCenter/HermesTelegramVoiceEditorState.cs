namespace LilithControlCenter;

// This is a transient view of the native account setting, never a local store.
internal sealed class HermesTelegramVoiceEditorState
{
    internal string? UserId { get; private set; }
    internal HermesTelegramVoiceStatus? Status { get; private set; }
    internal HermesTelegramVoiceMode DraftMode { get; private set; }
    internal long Epoch { get; private set; }
    internal bool IsBusy { get; private set; }
    internal bool CanSave => !IsBusy && Status is not null && DraftMode != Status.Mode;

    internal void Select(string? userId)
    {
        Invalidate();
        UserId = userId;
    }

    internal long Begin()
    {
        if (IsBusy || UserId is null)
            throw new InvalidOperationException("Telegram voice editor is not available.");
        IsBusy = true;
        return ++Epoch;
    }

    internal bool IsCurrent(long epoch, string userId)
        => Epoch == epoch && string.Equals(UserId, userId, StringComparison.Ordinal);

    internal bool Apply(long epoch, HermesTelegramVoiceStatus status)
    {
        if (!IsBusy || !IsCurrent(epoch, status.UserId))
            return false;
        Status = status;
        DraftMode = status.Mode;
        return true;
    }

    internal void Edit(bool enabled, bool everyReply)
    {
        if (IsBusy || Status is null)
            return;
        DraftMode = !enabled ? HermesTelegramVoiceMode.Off
            : everyReply ? HermesTelegramVoiceMode.All : HermesTelegramVoiceMode.VoiceOnly;
    }

    internal void Finish(long epoch)
    {
        if (Epoch == epoch)
            IsBusy = false;
    }

    internal void Invalidate()
    {
        ++Epoch;
        IsBusy = false;
        Status = null;
        DraftMode = HermesTelegramVoiceMode.Off;
    }
}
